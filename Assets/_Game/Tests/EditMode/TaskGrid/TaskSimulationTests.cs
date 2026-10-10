using System;
using System.Collections.Generic;
using System.Linq;
using CallmeCatgirl.Gameplay.TaskGrid;
using NUnit.Framework;

namespace CallmeCatgirl.Tests
{
    public sealed class TaskSimulationTests
    {
        private static readonly Cell[] Line = { new Cell(0, 0), new Cell(1, 0) };
        private static ModelDefinition Model(double interval = 3) => new ModelDefinition("model", interval,
            new Dictionary<AbilityKind, double> { {AbilityKind.Writing, 4}, {AbilityKind.Code, 1}, {AbilityKind.Reasoning, 1} });
        private static TaskDefinition Definition(double calculation = 100, double patience = 40, Cell[] shape = null)
            => new TaskDefinition("def", "test", shape ?? Line, calculation, 10, patience, 100,
                new[] { new AbilityRequirement(AbilityKind.Writing, 10, 16),
                    new AbilityRequirement(AbilityKind.Code, 2, 4), new AbilityRequirement(AbilityKind.Reasoning, 2, 4) });
        private static TaskSimulation Simulation(ModelDefinition model = null)
            => new TaskSimulation(5, 5, Enumerable.Range(0, 25).Select(i => new Cell(i % 5, i / 5)), model ?? Model());
        private static TaskInstance Add(TaskSimulation sim, string id = "A", TaskDefinition definition = null)
        { Assert.That(sim.TryCreate(id, "user-" + id, "1", definition ?? Definition(), out var task), Is.True); return task; }
        private static void Place(TaskSimulation sim, string id, int x = 0, int y = 0, int rotation = 0)
        { Assert.That(sim.TryPlace(id, new Cell(x, y), rotation, out var failure), Is.True, failure.ToString()); }

        [Test] public void RepeatedRoundAndRequestAreRejectedUntilFeedbackEnds()
        {
            var sim = Simulation(); var a = Add(sim);
            Assert.That(sim.TryCreate("B", "user-A", "1", Definition(), out _), Is.False);
            Assert.That(sim.TryCreate("A", "different", "2", Definition(), out _), Is.False);
            Place(sim, a.Id); sim.Advance(10); sim.TrySubmit(a.Id);
            Assert.That(sim.TryCreate("B", "user-A", "2", Definition(), out _), Is.False);
            Assert.That(sim.TryCloseFeedback(a.Id), Is.True);
            Assert.That(sim.TryCreate("B", "user-A", "2", Definition(), out _), Is.True);
            Assert.That(sim.TryCreate("C", "user-A", "1", Definition(), out _), Is.False);
        }

        [Test] public void RotationNormalizesNegativeOffsetsAndFourTurnsRestoreShape()
        {
            var def = Definition(shape: new[] { new Cell(0, 0), new Cell(0, 1), new Cell(1, 1) });
            Assert.That(def.RotatedShape(1), Is.EquivalentTo(new[] { new Cell(0, 0), new Cell(1, 0), new Cell(0, 1) }));
            Assert.That(def.RotatedShape(4), Is.EqualTo(def.Shape));
            Assert.That(def.RotatedShape(-1), Is.EqualTo(def.RotatedShape(3)));
        }

        [TestCase(-1, 0)] [TestCase(4, 0)] [TestCase(0, 5)]
        public void OutOfBoundsPlacementChangesNothing(int x, int y)
        {
            var sim = Simulation(); var task = Add(sim);
            Assert.That(sim.TryPlace(task.Id, new Cell(x, y), 0, out var failure), Is.False);
            Assert.That(failure, Is.EqualTo(PlacementFailure.OutOfBounds));
            Assert.That(task.State, Is.EqualTo(RequestState.Waiting)); Assert.That(sim.Occupancy.Count, Is.Zero);
        }

        [Test] public void LockedCellsRejectPlacement()
        {
            var sim = new TaskSimulation(3, 3, new[] {new Cell(0, 0)}, Model()); var task = Add(sim);
            Assert.That(sim.Preview(task.Id, new Cell(0, 0), 0), Is.EqualTo(PlacementFailure.LockedCell));
        }

        [Test] public void InvalidMovePreservesPoseProgressAndBothOccupancies()
        {
            var sim = Simulation(); var a = Add(sim); var b = Add(sim, "B");
            Place(sim, a.Id); Place(sim, b.Id, 0, 1); sim.Advance(1);
            Assert.That(sim.TryPlace(a.Id, new Cell(0, 1), 0, out var failure), Is.False);
            Assert.That(failure, Is.EqualTo(PlacementFailure.Overlap));
            Assert.That(a.Origin, Is.EqualTo(new Cell(0, 0))); Assert.That(a.Progress, Is.EqualTo(10));
            Assert.That(sim.TaskAt(new Cell(0, 0)), Is.EqualTo(a.Id));
            Assert.That(sim.TaskAt(new Cell(0, 1)), Is.EqualTo(b.Id));
        }

        [Test] public void MoveAndRotateRetainProgressAndAbilityFrequency()
        {
            var sim = Simulation(); var a = Add(sim); Place(sim, a.Id); sim.Advance(2);
            Place(sim, a.Id, 2, 1, 1); sim.Advance(1);
            Assert.That(sim.TaskAt(new Cell(0, 0)), Is.Null);
            Assert.That(sim.TaskAt(new Cell(2, 2)), Is.EqualTo(a.Id));
            Assert.That(a.Progress, Is.EqualTo(30)); Assert.That(a.GetAbility(AbilityKind.Writing), Is.EqualTo(4));
        }

        [Test] public void PreviewWhileDraggingDoesNotReleaseOrPauseOriginalTask()
        {
            var sim = Simulation(); var a = Add(sim); Place(sim, a.Id);
            sim.Preview(a.Id, new Cell(2, 2), 1); sim.Advance(3);
            Assert.That(sim.TaskAt(new Cell(0, 0)), Is.EqualTo(a.Id)); Assert.That(a.Progress, Is.EqualTo(30));
        }

        [Test] public void ConcurrentTasksDoNotShareSpeed()
        {
            var sim = Simulation(); var a = Add(sim); var b = Add(sim, "B");
            Place(sim, a.Id); Place(sim, b.Id, 0, 1); sim.Advance(3);
            Assert.That(a.Progress, Is.EqualTo(30)); Assert.That(b.Progress, Is.EqualTo(30));
            Assert.That(a.GetAbility(AbilityKind.Code), Is.EqualTo(1)); Assert.That(b.GetAbility(AbilityKind.Code), Is.EqualTo(1));
        }

        [Test] public void WithdrawalRetainsAllProgressAndRemainderWhileDeadlineContinues()
        {
            var sim = Simulation(); var a = Add(sim); Place(sim, a.Id); sim.Advance(4);
            Assert.That(sim.TryWithdraw(a.Id), Is.True); sim.Advance(5);
            Assert.That(sim.Occupancy.Count, Is.Zero); Assert.That(a.Progress, Is.EqualTo(40));
            Assert.That(a.GetAbility(AbilityKind.Writing), Is.EqualTo(4)); Assert.That(a.AbilityRemainder, Is.EqualTo(1));
            Assert.That(a.Remaining(sim.GameTime), Is.EqualTo(31));
            Place(sim, a.Id); sim.Advance(2); Assert.That(a.GetAbility(AbilityKind.Writing), Is.EqualTo(8));
        }

        [Test] public void GlobalPauseFreezesTimeCalculationAbilitiesAndDeadline()
        {
            var sim = Simulation(); var a = Add(sim); Place(sim, a.Id); sim.Advance(1);
            sim.Paused = true; sim.Advance(30);
            Assert.That(sim.GameTime, Is.EqualTo(1)); Assert.That(a.Progress, Is.EqualTo(10));
            Assert.That(a.AbilityRemainder, Is.EqualTo(1)); Assert.That(a.Remaining(sim.GameTime), Is.EqualTo(39));
            sim.Paused = false; sim.Advance(2); Assert.That(a.GetAbility(AbilityKind.Writing), Is.EqualTo(4));
        }

        [Test] public void ReadyStaysOccupiedAndQualityFrozenUntilSubmit()
        {
            var sim = Simulation(); var a = Add(sim); Place(sim, a.Id); sim.Advance(10);
            var result = a.Result; sim.Advance(100);
            Assert.That(a.State, Is.EqualTo(RequestState.Ready)); Assert.That(a.Result, Is.SameAs(result));
            Assert.That(a.GetAbility(AbilityKind.Writing), Is.EqualTo(12)); Assert.That(sim.Occupancy.Count, Is.EqualTo(2));
            Assert.That(sim.TryPlace(a.Id, new Cell(2, 2), 0, out _), Is.False);
            Assert.That(sim.TryWithdraw(a.Id), Is.False);
            Assert.That(sim.TrySubmit(a.Id), Is.True); Assert.That(sim.Occupancy.Count, Is.Zero);
            Assert.That(sim.TrySubmit(a.Id), Is.False);
            Assert.That(sim.DrainEvents().Count(e => e.Kind == TaskEventKind.Submitted), Is.EqualTo(1));
        }

        [Test] public void CompletingOnDeadlineIncludesFinalAbilityTickAndWinsTimeout()
        {
            var sim = Simulation(Model(1)); var a = Add(sim, definition: Definition(100, 10)); Place(sim, a.Id);
            sim.Advance(100);
            Assert.That(a.State, Is.EqualTo(RequestState.Ready)); Assert.That(a.FinishedAt, Is.EqualTo(10));
            Assert.That(a.GetAbility(AbilityKind.Writing), Is.EqualTo(40));
            Assert.That(sim.DrainEvents().Any(e => e.Kind == TaskEventKind.TimedOut), Is.False);
        }

        [Test] public void LargeFrameStopsAtDeadlineAndSettlesPartialTokensOnce()
        {
            var sim = Simulation(); var a = Add(sim, definition: Definition(100, 6)); Place(sim, a.Id);
            sim.Advance(100); sim.Advance(100);
            Assert.That(a.State, Is.EqualTo(RequestState.TimedOut)); Assert.That(a.Progress, Is.EqualTo(60));
            Assert.That(a.GetAbility(AbilityKind.Writing), Is.EqualTo(8)); Assert.That(sim.Occupancy.Count, Is.Zero);
            var outcomes = sim.DrainEvents().Where(e => e.Kind == TaskEventKind.TimedOut).ToArray();
            Assert.That(outcomes.Length, Is.EqualTo(1)); Assert.That(outcomes[0].BillableTokens, Is.EqualTo(60));
            Assert.That(sim.TrySubmit(a.Id), Is.False); Assert.That(sim.TryPlace(a.Id, new Cell(0, 0), 0, out _), Is.False);
        }

        [Test] public void UnstartedTimeoutProducesZeroTokens()
        {
            var sim = Simulation(); var a = Add(sim); sim.Advance(41);
            Assert.That(a.Progress, Is.Zero); Assert.That(a.GetAbility(AbilityKind.Writing), Is.Zero);
            Assert.That(sim.DrainEvents().Single(e => e.Kind == TaskEventKind.TimedOut).BillableTokens, Is.Zero);
        }

        [TestCase(12, 3, 3, Quality.Complete)]
        [TestCase(12, 1, 10, Quality.BelowStandard)]
        [TestCase(16, 4, 4, Quality.Excellent)]
        [TestCase(100, 4, 3, Quality.Complete)]
        public void QualityUsesEachRequiredAbilityAndLowestGrade(double writing, double code, double reasoning, Quality expected)
        {
            var points = new Dictionary<AbilityKind, double> { {AbilityKind.Writing, writing}, {AbilityKind.Code, code}, {AbilityKind.Reasoning, reasoning} };
            Assert.That(QualityResult.Evaluate(Definition(), points).Overall, Is.EqualTo(expected));
        }

        [Test] public void UnrequiredAbilityDoesNotLowerQualityAndSnapshotCannotChange()
        {
            var def = new TaskDefinition("x", "x", Line, 1, 1, 2, 1,
                new[] {new AbilityRequirement(AbilityKind.Writing, 1, 2)});
            var points = new Dictionary<AbilityKind, double> { {AbilityKind.Writing, 2} };
            var result = QualityResult.Evaluate(def, points); points[AbilityKind.Writing] = 0;
            Assert.That(result.Overall, Is.EqualTo(Quality.Excellent)); Assert.That(result.Produced[AbilityKind.Writing], Is.EqualTo(2));
        }

        [Test] public void LargeAndSmallTimeStepsHaveIdenticalResults()
        {
            var big = Simulation(); var small = Simulation(); var a = Add(big); var b = Add(small);
            Place(big, a.Id); Place(small, b.Id); big.Advance(12);
            for (int i = 0; i < 1200; i++) small.Advance(0.01);
            Assert.That(b.Progress, Is.EqualTo(a.Progress).Within(1e-7));
            Assert.That(b.GetAbility(AbilityKind.Writing), Is.EqualTo(a.GetAbility(AbilityKind.Writing)));
            Assert.That(b.Result.Overall, Is.EqualTo(a.Result.Overall)); Assert.That(b.FinishedAt, Is.EqualTo(a.FinishedAt).Within(1e-7));
        }

        [Test] public void ChangingModelRequiresPauseAndOnlyAddsNewContributions()
        {
            var sim = Simulation(); var a = Add(sim); Place(sim, a.Id); sim.Advance(4);
            var codeModel = new ModelDefinition("code", 6, new Dictionary<AbilityKind, double> {{AbilityKind.Code, 5}});
            Assert.That(sim.TryChangeModel(codeModel), Is.False);
            sim.Paused = true; Assert.That(sim.TryChangeModel(codeModel), Is.True);
            Assert.That(a.AbilityRemainder, Is.EqualTo(2)); Assert.That(a.GetAbility(AbilityKind.Code), Is.EqualTo(1));
            sim.Paused = false; sim.Advance(4);
            Assert.That(a.GetAbility(AbilityKind.Writing), Is.EqualTo(4)); Assert.That(a.GetAbility(AbilityKind.Code), Is.EqualTo(6));
        }

        [TestCase(double.NaN)] [TestCase(double.PositiveInfinity)] [TestCase(-1)]
        public void InvalidTimeStepIsRejected(double dt)
        { Assert.Throws<ArgumentOutOfRangeException>(() => Simulation().Advance(dt)); }

        [Test] public void DuplicateCellsAndInvalidThresholdsAreRejected()
        {
            Assert.Throws<ArgumentException>(() => Definition(shape: new[] {new Cell(0, 0), new Cell(0, 0)}));
            Assert.Throws<ArgumentException>(() => new AbilityRequirement(AbilityKind.Writing, 2, 2));
        }
    }
}
