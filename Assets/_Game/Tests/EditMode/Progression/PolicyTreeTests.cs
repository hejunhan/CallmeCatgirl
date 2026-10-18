using System;
using System.Collections.Generic;
using System.Linq;
using CallmeCatgirl.Gameplay.Progression;
using CallmeCatgirl.Gameplay.TaskGrid;
using NUnit.Framework;

namespace CallmeCatgirl.Tests
{
    public sealed class PolicyTreeTests
    {
        private ProgressionTree tree;
        private int funds;
        [SetUp] public void SetUp()
        {
            funds = 100000;
            var simulation = new TaskSimulation(1, 1, new[] { new Cell(0, 0) },
                new ModelDefinition("policy-test", 1, new Dictionary<AbilityKind, double> { [AbilityKind.Writing] = 1 }));
            tree = new ProgressionTree(ProgressionTree.CreateDefaultNodes(), simulation, cost =>
            { if (funds < cost) return false; funds -= cost; return true; });
        }

        // Given both routes are available, buying open source locks closed source without charging it.
        [Test] public void OpenAndClosedRoutesAreMutuallyExclusiveAndEffectsRemainIndependent()
        {
            Buy("policy-foundations"); Buy("route-open");
            int before = funds;
            Assert.That(tree.TryPurchase("route-closed"), Is.EqualTo(PurchaseResult.ExcludedByChoice));
            Assert.That(funds, Is.EqualTo(before));
            Assert.That(tree.Effects.CustomerArrivalMultiplier, Is.LessThan(1));
            Assert.That(tree.Reputation, Is.GreaterThan(0));
            Assert.That(tree.Effects.TrainingScoreMultiplier, Is.GreaterThan(1));
        }

        // Given a book bought against 12 points, later inventory changes and reload cannot improve/replay it.
        [Test] public void BookRewardFreezesPurchaseTimeQualityAndAcknowledgementSurvivesReload()
        {
            Buy("policy-foundations");
            double best = 12; tree.BestAvailableCorpusPoints = () => best;
            Buy("branch-book-scan"); best = 100;
            var reward = tree.PendingCorpusRewards.Single();
            Assert.That(reward.FrozenPoints, Is.EqualTo(15));
            var saved = tree.Capture();
            Assert.That(tree.TryRestore(saved), Is.True);
            Assert.That(tree.PendingCorpusRewards.Single().FrozenPoints, Is.EqualTo(15));
            Assert.That(tree.AcknowledgeCorpusReward(reward.EventId), Is.True);
            Assert.That(tree.AcknowledgeCorpusReward(reward.EventId), Is.False);
            Assert.That(tree.TryRestore(tree.Capture()), Is.True);
            Assert.That(tree.PendingCorpusRewards, Is.Empty);
            Assert.That(tree.TryPurchase("branch-book-scan"), Is.EqualTo(PurchaseResult.AlreadyUnlocked));
        }

        [Test] public void EmptyInventoryBookHasPositiveBaselineAndFailedPaymentCreatesNoReward()
        {
            Buy("policy-foundations"); tree.BestAvailableCorpusPoints = () => 0;
            funds = 0;
            Assert.That(tree.TryPurchase("branch-book-scan"), Is.EqualTo(PurchaseResult.InsufficientFunds));
            Assert.That(tree.PendingCorpusRewards, Is.Empty);
            funds = 1000; Buy("branch-book-scan");
            Assert.That(tree.PendingCorpusRewards.Single().FrozenPoints, Is.EqualTo(4));
        }

        [Test] public void SecondPageUnlocksOpportunityAndTerminalIsOnceAndReputationBased()
        {
            Buy("policy-foundations"); Buy("route-closed"); Buy("main-emergence");
            Assert.That(tree.Effects.EmotionOpportunityUnlocked, Is.True);
            Assert.That(tree.RecordReputationEvent("earned-trust", 60), Is.True);
            Assert.That(tree.RecordReputationEvent("earned-trust", 60), Is.False);
            Buy("main-public-future");
            Assert.That(tree.PendingTerminal.Band, Is.EqualTo(PolicyEndingBand.Good));
            Assert.That(tree.RecordReputationEvent("too-late", -100), Is.False);
            Assert.That(tree.AcknowledgeTerminal(), Is.True);
            Assert.That(tree.PendingTerminal, Is.Null);
            Assert.That(tree.TryRestore(tree.Capture()), Is.True);
            Assert.That(tree.PendingTerminal, Is.Null);
            Assert.That(tree.TryPurchase("main-public-future"), Is.EqualTo(PurchaseResult.AlreadyUnlocked));
        }

        [Test] public void InvalidRestoreDoesNotChangeFundsReputationOrPendingSignals()
        {
            Buy("policy-foundations"); Buy("route-open"); int before = funds;
            var state = tree.Capture(); state.UnlockedIds = state.UnlockedIds.Concat(new[] { "route-closed" }).ToArray();
            Assert.That(tree.TryRestore(state), Is.False);
            Assert.That(tree.Reputation, Is.GreaterThan(0));
            Assert.That(funds, Is.EqualTo(before));
            var tampered = tree.Capture(); tampered.Reputation += 100;
            Assert.That(tree.TryRestore(tampered), Is.False);
            Assert.That(tree.PendingTerminal, Is.Null);
        }

        [Test] public void AccusationAddsCustomersAndReducesConversationEndChanceWithoutPreventingCompletion()
        {
            Buy("policy-foundations"); Buy("route-closed"); Buy("branch-distillation-claim");
            Assert.That(tree.Effects.CustomerArrivalMultiplier, Is.GreaterThan(1));
            Assert.That(tree.Effects.EndProbabilityMultiplier, Is.LessThan(1));
            Assert.That(tree.Reputation, Is.LessThan(0));
            // No policy mutates a request, its work, its state, or drains settlement events.
            Assert.That(tree.Effects.InferenceSpeedMultiplier, Is.GreaterThan(0));
        }

        [Test] public void LegacyVersionOneRestoresStableAbilityIdsWithoutStoryRewards()
        {
            Assert.That(tree.TryRestore(new ProgressionState { Version = 1, UnlockedIds = new[] { "Writing-1" } }), Is.True);
            Assert.That(tree.IsUnlocked("Writing-1"), Is.True);
            Assert.That(tree.PendingCorpusRewards, Is.Empty);
            Assert.That(tree.PendingTerminal, Is.Null);
        }

        [Test] public void InventoryCallbackCannotReenterPurchaseOrMutateReputation()
        {
            Buy("policy-foundations"); int before = funds;
            tree.BestAvailableCorpusPoints = () => {
                Assert.That(tree.TryPurchase("route-open"), Is.EqualTo(PurchaseResult.Busy));
                Assert.That(tree.RecordReputationEvent("reentrant", 100), Is.False);
                return 8;
            };
            Buy("branch-book-scan");
            Assert.That(funds, Is.EqualTo(before - 180));
            Assert.That(tree.Reputation, Is.Zero);
            Assert.That(tree.PendingCorpusRewards.Single().FrozenPoints, Is.EqualTo(10));
        }

        [Test] public void MissingRewardLedgerIsRejectedAtomicallyAndSnapshotsCannotAlterLiveState()
        {
            Buy("policy-foundations"); Buy("branch-book-scan");
            var state = tree.Capture(); state.CorpusRewards = Array.Empty<PolicyCorpusReward>();
            Assert.That(tree.TryRestore(state), Is.False);
            tree.PendingCorpusRewards.Single().Acknowledged = true;
            tree.Effects.AutoPlacement = true;
            tree.Nodes.First(n => n.Id == "policy-foundations").Effects.TrainingScoreMultiplier = 9;
            Assert.That(tree.PendingCorpusRewards.Count, Is.EqualTo(1));
            Assert.That(tree.Effects.AutoPlacement, Is.False);
            Assert.That(tree.Effects.TrainingScoreMultiplier, Is.EqualTo(1.10));
        }

        [TestCase(-15, PolicyEndingBand.Bad)]
        [TestCase(-14, PolicyEndingBand.Normal)]
        [TestCase(14, PolicyEndingBand.Normal)]
        [TestCase(15, PolicyEndingBand.Good)]
        public void ReputationBandsHaveExplicitInclusiveBoundaries(int reputation, PolicyEndingBand expected)
            => Assert.That(ProgressionTree.ClassifyReputation(reputation), Is.EqualTo(expected));

        private void Buy(string id) => Assert.That(tree.TryPurchase(id), Is.EqualTo(PurchaseResult.Success), id);
    }
}
