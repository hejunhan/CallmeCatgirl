using System;
using System.Collections.Generic;
using CallmeCatgirl.Gameplay.Progression;
using CallmeCatgirl.Gameplay.TaskGrid;
using NUnit.Framework;

namespace CallmeCatgirl.Tests
{
    public sealed class ProgressionTests
    {
        private TaskSimulation simulation;
        private ProgressionTree tree;
        private int funds;
        [SetUp] public void Prepare()
        {
            simulation = new TaskSimulation(1, 1, new[] { new Cell(0, 0) },
                new ModelDefinition("test", 1, new Dictionary<AbilityKind, double> { [AbilityKind.Writing] = 1 }));
            funds = 1000;
            tree = new ProgressionTree(ProgressionTree.CreateDefaultNodes(), simulation, cost =>
            { if (funds < cost) return false; funds -= cost; return true; });
        }
        [Test] public void PurchaseChargesOnceAndRejectsMissingPrerequisite()
        {
            Assert.That(tree.TryPurchase("Writing-2"), Is.EqualTo(PurchaseResult.MissingPrerequisite));
            Assert.That(tree.TryPurchase("Writing-1"), Is.EqualTo(PurchaseResult.Success));
            Assert.That(tree.TryPurchase("Writing-1"), Is.EqualTo(PurchaseResult.AlreadyUnlocked));
            Assert.That(funds, Is.EqualTo(900));
            Assert.That(simulation.Model.Points[AbilityKind.Writing], Is.EqualTo(2));
        }
        [Test] public void InsufficientFundsAndUnknownNodeHaveNoEffect()
        {
            funds = 0;
            Assert.That(tree.TryPurchase("Code-1"), Is.EqualTo(PurchaseResult.InsufficientFunds));
            Assert.That(tree.TryPurchase("missing"), Is.EqualTo(PurchaseResult.UnknownNode));
            Assert.That(tree.Capture().UnlockedIds, Is.Empty);
            Assert.That(simulation.Model.Points[AbilityKind.Writing], Is.EqualTo(1));
        }
        [Test] public void RestoreIsIdempotentAndPreservesIndependentAbilityAllocation()
        {
            tree.TryPurchase("Writing-1");
            var state = tree.Capture();
            simulation.Paused = true;
            simulation.TryChangeModel(new ModelDefinition("test", 1,
                new Dictionary<AbilityKind, double> { [AbilityKind.Writing] = 3 }));
            Assert.That(tree.TryRestore(state), Is.True);
            Assert.That(tree.TryRestore(state), Is.True);
            Assert.That(simulation.Model.Points[AbilityKind.Writing], Is.EqualTo(3));
            Assert.That(funds, Is.EqualTo(900));
            Assert.That(tree.TryRestore(new ProgressionState()), Is.True);
            Assert.That(simulation.Model.Points[AbilityKind.Writing], Is.EqualTo(2));
        }
        [Test] public void InvalidSaveIsAtomic()
        {
            Assert.That(tree.TryRestore(new ProgressionState { UnlockedIds = new[] { "Writing-2" } }), Is.False);
            Assert.That(tree.TryRestore(new ProgressionState { UnlockedIds = new[] { "Writing-1", "Writing-1" } }), Is.False);
            Assert.That(tree.TryRestore(new ProgressionState { UnlockedIds = new[] { "unknown" } }), Is.False);
            Assert.That(tree.TryRestore(new ProgressionState { Version = 99 }), Is.False);
            Assert.That(tree.Capture().UnlockedIds, Is.Empty);
        }
        [TestCase(false)] [TestCase(true)] public void PurchasePreservesPauseAndActuallyChangesTaskQuality(bool paused)
        {
            simulation.Paused = paused;
            tree.TryPurchase("Writing-1");
            Assert.That(simulation.Paused, Is.EqualTo(paused));
            Assert.That(simulation.GameTime, Is.Zero);
            var definition = new TaskDefinition("task", "task", new[] { new Cell(0, 0) }, 2, 1, 10, 100,
                new[] { new AbilityRequirement(AbilityKind.Writing, 3, 4) });
            simulation.TryCreate("request", "conversation", "round", definition, out var task);
            simulation.TryPlace(task.Id, new Cell(0, 0), 0, out _);
            simulation.Paused = false; simulation.Advance(2);
            Assert.That(task.GetAbility(AbilityKind.Writing), Is.EqualTo(4));
            Assert.That(task.Result.Overall, Is.EqualTo(Quality.Excellent));
        }
        [Test] public void CyclicOrMissingPrerequisiteConfigurationRejected()
        {
            var node = new ProgressionNode { Id = "a", Prerequisites = new[] { "a" } };
            Assert.Throws<ArgumentException>(() => new ProgressionTree(new[] { node }, simulation, _ => true));
            node.Prerequisites = new[] { "missing" };
            Assert.Throws<ArgumentException>(() => new ProgressionTree(new[] { node }, simulation, _ => true));
        }
        [Test] public void WalletCallbackCannotPurchaseReentrantly()
        {
            ProgressionTree guarded = null;
            guarded = new ProgressionTree(ProgressionTree.CreateDefaultNodes(), simulation, _ =>
            { Assert.That(guarded.TryPurchase("Writing-1"), Is.EqualTo(PurchaseResult.Busy)); return true; });
            Assert.That(guarded.TryPurchase("Writing-1"), Is.EqualTo(PurchaseResult.Success));
            Assert.That(simulation.Model.Points[AbilityKind.Writing], Is.EqualTo(2));
        }
        [Test] public void MidTaskPurchasePreservesProgressAndFractionalAbilityTick()
        {
            var definition = new TaskDefinition("task", "task", new[] { new Cell(0, 0) }, 3, 1, 10, 100,
                new[] { new AbilityRequirement(AbilityKind.Writing, 3, 5) });
            simulation.TryCreate("request", "conversation", "round", definition, out var task);
            simulation.TryPlace(task.Id, new Cell(0, 0), 0, out _);
            simulation.Advance(1.5);
            tree.TryPurchase("Writing-1");
            Assert.That(task.Progress, Is.EqualTo(1.5));
            Assert.That(task.GetAbility(AbilityKind.Writing), Is.EqualTo(1));
            simulation.Advance(0.5);
            Assert.That(task.GetAbility(AbilityKind.Writing), Is.EqualTo(3));
            Assert.That(simulation.GameTime, Is.EqualTo(2));
        }
    }
}
