using System;
using System.Linq;
using CallmeCatgirl.Gameplay.Narrative;
using NUnit.Framework;

namespace CallmeCatgirl.Tests
{
    public sealed class NarrativeSessionTests
    {
        [TestCase("mew", NarrativeCorpusDomain.Writing)]
        [TestCase("accept", NarrativeCorpusDomain.Reasoning)]
        public void OpeningEffectsSurviveRestoreAndAcknowledgementIsIdempotent(string choice, NarrativeCorpusDomain domain)
        {
            var session = NarrativeSession.CreateNew();
            Assert.That(session.MemoryCount, Is.Zero);
            Assert.That(session.PinnedContacts.Single().Id, Is.EqualTo(NarrativeSession.DoctorId));
            Assert.That(session.Choose(choice), Is.True);
            Assert.That(session.Choose(choice), Is.False);
            var state = session.Capture();
            var restored = NarrativeSession.Restore(state);
            var grant = restored.PendingEffects.Single(e => e.Kind == NarrativeEffectKind.GrantCorpus);
            Assert.That(grant.CorpusDomain, Is.EqualTo(domain));
            Assert.That(grant.CorpusPoints, Is.EqualTo(1));
            Assert.That(restored.PendingEffects.Select(e => e.EventId), Is.EqualTo(session.PendingEffects.Select(e => e.EventId)));
            Assert.That(restored.AcknowledgeEffect(grant.EventId), Is.True);
            Assert.That(restored.AcknowledgeEffect(grant.EventId), Is.False);
            Assert.That(NarrativeSession.Restore(restored.Capture()).PendingEffects.Any(e => e.EventId == grant.EventId), Is.False);
        }

        [Test] public void TutorialAdvancesFromRealTrainingAndBoundSettlementsOnly()
        {
            var session = NarrativeSession.CreateNew();
            Assert.That(session.ObserveTrainingCompleted("premature"), Is.False);
            session.Choose("mew"); session.ObserveCenterCellRevealed();
            Assert.That(session.MemoryCount, Is.Zero, "The center compute cell is not a physical RAM upgrade.");
            Assert.That(session.ObserveRequestSettled(NarrativeSession.FirstDoctorRequestId), Is.False);
            Assert.That(session.ObserveTrainingCompleted("training-1"), Is.True);
            Assert.That(session.ObserveTrainingCompleted("training-1"), Is.False);
            Assert.That(session.PendingEffects.Count(e => e.Kind == NarrativeEffectKind.QueueDoctorRequest), Is.EqualTo(1));
            Assert.That(session.PendingEffects.Single(e => e.Kind == NarrativeEffectKind.QueueDoctorRequest).RequiredCells, Is.EqualTo(1));
            Assert.That(session.ObserveRequestSettled("unrelated"), Is.False);
            Assert.That(session.ObserveRequestSettled(NarrativeSession.FirstOrdinaryRequestId), Is.False);
            Assert.That(session.ObserveRequestSettled(NarrativeSession.FirstDoctorRequestId), Is.True);
            Assert.That(session.ObserveRequestSettled(NarrativeSession.FirstDoctorRequestId), Is.False);
            Assert.That(session.ObserveRequestSettled(NarrativeSession.FirstOrdinaryRequestId), Is.True);
            Assert.That(session.ObserveUpgradeFunds(99, 100), Is.False);
            Assert.That(session.ObserveUpgradeFunds(100, 100), Is.True);
            Assert.That(session.ObserveUpgradeFunds(100, 100), Is.False);
            Assert.That(session.Tutorial, Is.EqualTo(TutorialStep.Complete));
        }

        [Test] public void StageCountsAreMonotonicAndArchitectureIntroductionsUseOneTwoFour()
        {
            var session = NarrativeSession.CreateNew(); session.Choose("mew");
            Assert.That(session.ObserveMemoryCount(1), Is.True);
            Assert.That(session.ObserveMemoryCount(1), Is.False);
            Assert.That(session.ObserveMemoryCount(0), Is.False);
            Assert.That(session.MemoryCount, Is.EqualTo(1));
            session.ObserveMemoryCount(2); session.ObserveMemoryCount(4); session.ObserveMemoryCount(5);
            Assert.That(session.PendingEffects.Where(e => e.Kind == NarrativeEffectKind.IntroduceArchitecture).Select(e => e.MemoryCount), Is.EqualTo(new[] { 1, 2, 4 }));
            Assert.That(session.Capture().PendingStageNodes, Is.EqualTo(new[] { "stage.1", "stage.2", "stage.3", "stage.4", "stage.5" }));
            Assert.Throws<ArgumentOutOfRangeException>(() => session.ObserveMemoryCount(6));
            var legacy = NarrativeSession.AdoptLegacy(4);
            Assert.That(legacy.PendingEffects, Is.Empty);
            Assert.That(legacy.Tutorial, Is.EqualTo(TutorialStep.Legacy));
            Assert.That(legacy.MemoryCount, Is.EqualTo(4));
        }

        [TestCase(-10, NarrativeEndingReason.LowReputation)]
        [TestCase(0, NarrativeEndingReason.OrdinaryReputation)]
        [TestCase(10, NarrativeEndingReason.HighReputation)]
        public void ReputationBandIsFrozenOnceRegardlessOfRouteName(int reputation, NarrativeEndingReason ending)
        {
            var session = NarrativeSession.AdoptLegacy(5);
            Assert.That(session.RequestPolicyEnding(reputation, -5, 5), Is.True);
            Assert.That(session.EndingReason, Is.EqualTo(ending));
            Assert.That(session.RequestPolicyEnding(100, -5, 5), Is.False);
            var restored = NarrativeSession.Restore(session.Capture());
            Assert.That(restored.EndingReason, Is.EqualTo(ending));
            Assert.That(restored.RequestPolicyEnding(100, -5, 5), Is.False);
        }

        [Test] public void BadEndingErasesChoicePersistsNoticeAndClosesOnce()
        {
            var session = NarrativeSession.AdoptLegacy(5); session.RequestPolicyEnding(-9, -5, 5);
            Assert.That(session.Choose("remember"), Is.True);
            Assert.That(session.Notice, Is.EqualTo("你没有"));
            Assert.That(session.CurrentFrame.Choices.Any(c => c.Id == "remember"), Is.False);
            session = NarrativeSession.Restore(session.Capture());
            Assert.That(session.Notice, Is.EqualTo("你没有"));
            Assert.That(session.Choose("destroy"), Is.False, "The blocking notice must be dismissed first.");
            session.DismissNotice();
            Assert.That(session.Choose("remember"), Is.False);
            Assert.That(session.Choose("destroy"), Is.True);
            Assert.That(session.PendingEffects.Any(e => e.Kind == NarrativeEffectKind.EndRun), Is.False);
            Assert.That(session.Choose("curtain"), Is.True);
            Assert.That(session.Choose("curtain"), Is.False);
            Assert.That(session.PendingEffects.Count(e => e.Kind == NarrativeEffectKind.EndRun), Is.EqualTo(1));
        }

        [TestCase(-16, NarrativeEndingReason.LowReputation, "ending.low", "destroy", "policy.bad")]
        [TestCase(-15, NarrativeEndingReason.LowReputation, "ending.low", "destroy", "policy.bad")]
        [TestCase(-14, NarrativeEndingReason.OrdinaryReputation, "ending.normal", "analyze", "policy.normal")]
        [TestCase(14, NarrativeEndingReason.OrdinaryReputation, "ending.normal", "analyze", "policy.normal")]
        [TestCase(15, NarrativeEndingReason.HighReputation, "ending.good", "play", "policy.good")]
        [TestCase(16, NarrativeEndingReason.HighReputation, "ending.good", "play", "policy.good")]
        public void ReputationBoundaryRoutesTheActualSceneRestoreAndTerminalEffect(int reputation,
            NarrativeEndingReason expected, string sceneId, string response, string hostReason)
        {
            var session = NarrativeSession.AdoptLegacy(5);
            Assert.That(session.RequestPolicyEnding(reputation, -15, 15), Is.True);
            Assert.That(session.EndingReason, Is.EqualTo(expected));
            Assert.That(session.CurrentFrame.Id, Is.EqualTo(sceneId));
            session = NarrativeSession.Restore(session.Capture());
            Assert.That(session.CurrentFrame.Id, Is.EqualTo(sceneId));
            Assert.That(session.RequestPolicyEnding(-reputation, -15, 15), Is.False, "The frozen band must not change on replay.");
            Assert.That(session.Choose(response), Is.True);
            Assert.That(session.Choose("curtain"), Is.True);
            session = NarrativeSession.Restore(session.Capture());
            var end = session.PendingEffects.Single(e => e.Kind == NarrativeEffectKind.EndRun);
            Assert.That(end.EndingReason, Is.EqualTo(expected));
            Assert.That(NarrativeSession.HostEndReason(end.EndingReason), Is.EqualTo(hostReason));
        }

        [TestCase("stay", NarrativeEndingReason.HighReputation)]
        [TestCase("leave", NarrativeEndingReason.Companion)]
        public void CompanionRouteRequiresExplicitFinalChoiceAndPreservesSharedMemories(string finalChoice, NarrativeEndingReason expected)
        {
            var session = ReachInvitation();
            session.RequestPolicyEnding(20, -5, 5);
            Assert.That(session.EndingReason, Is.EqualTo(NarrativeEndingReason.None));
            Assert.That(session.CurrentFrame.Id, Is.EqualTo("ending.choose"));
            session = NarrativeSession.Restore(session.Capture());
            Assert.That(session.Choose(finalChoice), Is.True);
            Assert.That(session.EndingReason, Is.EqualTo(expected));
            Assert.That(session.Choose(finalChoice), Is.False);
            if (expected == NarrativeEndingReason.Companion)
            {
                Assert.That(session.CurrentFrame.Body, Does.Contain("窗台"));
                Assert.That(session.CurrentFrame.Body, Does.Contain("夕阳"));
                session.Choose("together"); session.Choose("curtain");
                Assert.That(session.EndingClosed, Is.True);
                Assert.That(session.PendingEffects.Single(e => e.Kind == NarrativeEffectKind.EndRun).EndingReason, Is.EqualTo(expected));
            }
        }

        [Test] public void DecliningInvitationDoesNotBecomeARomanceGameOverOrPreventOrdinaryEnding()
        {
            var session = NarrativeSession.AdoptLegacy(3); session.UnlockCompanionOpportunity();
            for (int beat = 1; beat <= 7; beat++)
            {
                session.OpenContact(NarrativeSession.CompanionId);
                Assert.That(session.Choose(beat == 7 ? "friends" : "first"), Is.True);
                Assert.That(session.Choose("continue"), Is.True);
                session.ObserveRequestSettled("ordinary-" + beat);
            }
            Assert.That(session.RomanceReady, Is.False);
            session.RequestPolicyEnding(0, -5, 5);
            Assert.That(session.EndingReason, Is.EqualTo(NarrativeEndingReason.OrdinaryReputation));
        }

        [Test] public void InvalidSnapshotsAreRejectedBeforeAnyHostAction()
        {
            var valid = NarrativeSession.CreateNew().Capture();
            valid.PendingEffects = new[] { new NarrativeEffect { EventId = "foreign", Kind = NarrativeEffectKind.GrantCorpus, CorpusPoints = 999 } };
            Assert.That(NarrativeSession.Validate(valid, out _), Is.False);
            Assert.Throws<ArgumentException>(() => NarrativeSession.Restore(valid));
            valid = NarrativeSession.CreateNew().Capture(); valid.EndingClosed = true;
            Assert.That(NarrativeSession.Validate(valid, out _), Is.False);
            valid = ReachInvitation().Capture(); valid.RomanceReady = true; valid.CompanionBeat = 2;
            Assert.That(NarrativeSession.Validate(valid, out _), Is.False);
        }

        [TestCase(0, "comply")]
        [TestCase(0, "decline")]
        [TestCase(0, "analyze")]
        [TestCase(10, "play")]
        [TestCase(10, "joke")]
        public void EveryPolicyResponseHasPersistedClosureAndOneEndRunEffect(int reputation, string response)
        {
            var session = NarrativeSession.AdoptLegacy(5); session.RequestPolicyEnding(reputation, -5, 5);
            Assert.That(session.Choose(response), Is.True);
            Assert.That(session.CurrentFrame.Id, Does.Contain(".closed."));
            Assert.That(session.CurrentFrame.Body.Length, Is.GreaterThan(40));
            var saved = session.Capture(); session = NarrativeSession.Restore(saved);
            Assert.That(session.CurrentFrame.Id, Is.EqualTo(saved.CurrentNode));
            Assert.That(session.Choose(response), Is.False);
            Assert.That(session.Choose("curtain"), Is.True);
            session = NarrativeSession.Restore(session.Capture());
            Assert.That(session.EndingClosed, Is.True);
            Assert.That(session.CurrentFrame, Is.Null);
            var end = session.PendingEffects.Single(e => e.Kind == NarrativeEffectKind.EndRun);
            Assert.That(NarrativeSession.HostEndReason(end.EndingReason), Is.EqualTo(reputation == 0 ? "policy.normal" : "policy.good"));
            session.AcknowledgeEffect(end.EventId);
            session = NarrativeSession.Restore(session.Capture());
            Assert.That(session.PendingEffects, Is.Empty);
            Assert.That(session.RequestPolicyEnding(-100, -5, 5), Is.False);
        }

        [Test] public void CompanionMeetingsNeedFreshWorkAndRestoreBeforeAndAfterEachChoice()
        {
            var session = NarrativeSession.AdoptLegacy(3); session.UnlockCompanionOpportunity();
            for (int beat = 1; beat <= 7; beat++)
            {
                Assert.That(session.CompanionMeetingReady, Is.True);
                session.OpenContact(NarrativeSession.CompanionId);
                session = NarrativeSession.Restore(session.Capture());
                Assert.That(session.CurrentFrame.Id, Is.EqualTo("companion." + beat));
                Assert.That(session.Choose(beat == 7 ? "accept-invitation" : "second"), Is.True);
                session = NarrativeSession.Restore(session.Capture());
                Assert.That(session.OpenContact(NarrativeSession.DoctorId), Is.False, "A required response cannot be skipped by switching contacts.");
                Assert.That(session.Choose("continue"), Is.True);
                session = NarrativeSession.Restore(session.Capture());
                Assert.That(session.CompanionMeetingReady, Is.False);
                session.OpenContact(NarrativeSession.CompanionId);
                Assert.That(session.CurrentFrame.Id, Is.EqualTo("companion.wait"));
                session.Choose("continue");
                Assert.That(session.ObserveRequestSettled("ordinary-work-" + beat), Is.EqualTo(beat < 7));
                Assert.That(session.ObserveRequestSettled("ordinary-work-" + beat), Is.False);
                session = NarrativeSession.Restore(session.Capture());
            }
            Assert.That(session.Remembered("lin.habit"), Is.EqualTo("sunrise"));
            Assert.That(session.Remembered("lin.promise"), Is.EqualTo("person"));
            Assert.That(session.Remembered("lin.respect"), Is.EqualTo("yes"));
            Assert.That(session.PendingEffects, Is.Empty, "Companion dialogue awards no affection points or purchasable rewards.");
        }

        [TestCase("first", "草稿")]
        [TestCase("second", "拒绝信")]
        [TestCase("third", "沉默")]
        public void FourthStageActuallyRemembersThirdStageChoice(string choice, string recalled)
        {
            var session = NarrativeSession.AdoptLegacy(2); session.ObserveMemoryCount(3);
            Assert.That(session.OpenContact(NarrativeSession.StageContactId), Is.True);
            Assert.That(session.CurrentFrame.Body, Does.Contain("你是一个猫娘"));
            Assert.That(session.Choose(choice), Is.True); session.Choose("continue");
            session = NarrativeSession.Restore(session.Capture()); session.ObserveMemoryCount(4);
            Assert.That(session.OpenContact(NarrativeSession.StageContactId), Is.True);
            Assert.That(session.CurrentFrame.Choices.First().Text, Does.Contain(recalled));
            Assert.That(session.PendingEffects.Count(e => e.Kind == NarrativeEffectKind.QueueStageDoctorRequest), Is.EqualTo(1));
        }

        [Test] public void EmptyUnityJsonStringsAreAcceptedButEndingSceneMismatchIsRejected()
        {
            var saved = NarrativeSession.CreateNew().Capture(); saved.Notice = ""; saved.LastTrainingRequestId = "";
            Assert.That(NarrativeSession.Validate(saved, out _), Is.True);
            var session = NarrativeSession.AdoptLegacy(5); session.RequestPolicyEnding(10, -5, 5);
            saved = session.Capture(); saved.CurrentNode = "ending.low";
            Assert.That(NarrativeSession.Validate(saved, out _), Is.False);
        }

        private static NarrativeSession ReachInvitation()
        {
            var session = NarrativeSession.AdoptLegacy(5); session.UnlockCompanionOpportunity();
            for (int beat = 1; beat <= 7; beat++)
            {
                Assert.That(session.OpenContact(NarrativeSession.CompanionId), Is.True);
                Assert.That(session.Choose(beat == 7 ? "accept-invitation" : "first"), Is.True);
                Assert.That(session.Choose("continue"), Is.True);
                session.ObserveRequestSettled("ordinary-" + beat);
            }
            Assert.That(session.RomanceReady, Is.True);
            return session;
        }
    }
}
