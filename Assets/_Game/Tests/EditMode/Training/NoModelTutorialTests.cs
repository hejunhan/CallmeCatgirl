using System;
using System.Collections.Generic;
using System.Linq;
using CallmeCatgirl.Gameplay.TaskGrid;
using NUnit.Framework;

namespace CallmeCatgirl.Gameplay.Training.Tests
{
    /// <summary>Tutorial plan A: no model exists until the first training creates it.</summary>
    public sealed class NoModelTutorialTests
    {
        private readonly Cell openCell = new Cell(1, 1);

        [Test]
        public void NewSessionStartsWithoutAModelAndUsesBaseCompute()
        {
            var f = new Fixture();
            Assert.That(f.Session.Models, Is.Empty);
            Assert.That(f.Session.ActiveModelId, Is.Null);
            Assert.That(f.Session.ActiveModel, Is.Null);
            Assert.That(f.Session.TokensPerSecond, Is.EqualTo(10d));
            Assert.That(f.Session.StarterTrainingAvailable, Is.True);
            Assert.That(f.Session.CompactTrainingAvailable, Is.False);
            Assert.That(f.Session.SelectModel("missing"), Is.EqualTo(TrainingResult.UnknownModel));
            Assert.That(f.Session.RenameModel("missing", "name"), Is.EqualTo(TrainingResult.UnknownModel));
            Assert.That(f.Session.UpgradeCompute(), Is.EqualTo(TrainingResult.UnknownModel));
        }

        [Test]
        public void FirstTrainingCreatesModelOneAndTutorialTrainingCreatesModelNine()
        {
            var f = new Fixture();
            var opening = f.Grant("opening", AbilityKind.Writing, 1);

            var first = f.Start(TrainingSession.StarterRecipeId, opening);
            Assert.That(first.PaidCost, Is.Zero);
            f.Finish(first);
            Assert.That(first.Output.Definition.Points[AbilityKind.Writing], Is.EqualTo(1d));
            Assert.That(f.Session.Models.Count, Is.EqualTo(1));
            Assert.That(f.Session.ActiveModelId, Is.Null, "Completing a training must not invent an active model.");
            Assert.That(f.Session.SelectModel(first.Output.Id), Is.EqualTo(TrainingResult.Success));
            Assert.That(f.Sim.Model.Points[AbilityKind.Writing], Is.EqualTo(1d));

            var secondCorpus = f.Grant("narrative-doctor-corpus-nine", AbilityKind.Writing, 9);
            Assert.That(f.Session.CompactTrainingAvailable, Is.True);
            Assert.That(f.Session.Recipes.Any(r => r.Id == TrainingSession.TutorialCorpusRecipeId), Is.True);
            var second = f.Start(TrainingSession.TutorialCorpusRecipeId, secondCorpus);
            Assert.That(second.PaidCost, Is.EqualTo(20));
            Assert.That(f.Sim.Tasks[second.RequestId].Definition.Shape.Count, Is.EqualTo(1));
            f.Finish(second);
            Assert.That(second.Output.Definition.Points[AbilityKind.Writing], Is.EqualTo(9d));
            Assert.That(f.Session.Models.Count, Is.EqualTo(2));
            Assert.That(f.Session.SelectModel(second.Output.Id), Is.EqualTo(TrainingResult.Success));
            Assert.That(f.Sim.Model.Points[AbilityKind.Writing], Is.EqualTo(9d));
        }

        [Test]
        public void CompactTutorialTrainingRejectsOnePointAndKeepsNineAvailable()
        {
            var f = new Fixture();
            var opening = f.Grant("opening", AbilityKind.Writing, 1);
            var first = f.Start(TrainingSession.StarterRecipeId, opening);
            f.Finish(first);
            var nine = f.Grant("narrative-doctor-corpus-nine", AbilityKind.Writing, 9);
            Assert.That(f.Session.CompactTrainingAvailable, Is.True);

            Assert.That(f.Session.StartConversationTraining(TrainingSession.TutorialCorpusRecipeId, "baseline",
                "模型9", "narrative-doctor", 1, out _), Is.EqualTo(TrainingResult.InvalidInput));
            Assert.That(f.Session.Jobs.Count, Is.EqualTo(1));
            Assert.That(f.Session.CompactTrainingAvailable, Is.True, "A rejected draft must keep the recipe available.");
            Assert.That(f.Session.RemainingPoints(nine), Is.EqualTo(9d), "A rejected draft must not consume corpus.");

            Assert.That(f.Session.StartConversationTraining(TrainingSession.TutorialCorpusRecipeId, "baseline",
                "模型9", "narrative-doctor", 9, out var job), Is.EqualTo(TrainingResult.Success));
            Assert.That(job.PaidCost, Is.EqualTo(20));
            Assert.That(f.Sim.Tasks[job.RequestId].Definition.Shape.Count, Is.EqualTo(1));
        }

        [TestCase(1d, Quality.BelowStandard)]
        [TestCase(9d, Quality.Excellent)]
        public void SecondRequestBarDiscriminatesModelOneFromModelNine(double modelPoints, Quality expected)
        {
            var sim = new TaskSimulation(4, 4, new[] { openCell },
                new ModelDefinition("initial", 1, new Dictionary<AbilityKind, double> { { AbilityKind.Writing, modelPoints } }),
                null);
            sim.Paused = true;
            Assert.That(sim.TrySetTokenThroughput(10), Is.True);
            var definition = new TaskDefinition("second", "第二份委托", new[] { new Cell(0, 0) },
                20, 1, 600, 40, new[] { new AbilityRequirement(AbilityKind.Writing, 6, 9) }, usesCalculationBudget: true);
            Assert.That(sim.TryCreate("second", "narrative-doctor", "second", definition, out var task), Is.True);
            Assert.That(sim.TryPlace(task.Id, openCell, 0, out _), Is.True);
            sim.Paused = false;
            sim.Advance(3);
            sim.Paused = true;
            Assert.That(task.State, Is.EqualTo(RequestState.Ready));
            Assert.That(task.Result.Overall, Is.EqualTo(expected));
        }

        [Test]
        public void EmptyModelStateAndUnselectedModelRoundTripThroughJson()
        {
            var f = new Fixture();
            var empty = f.RestoreFromJson();
            Assert.That(empty.Models, Is.Empty);
            Assert.That(empty.ActiveModelId, Is.Null);
            Assert.That(empty.ActiveModel, Is.Null);
            Assert.That(empty.TokensPerSecond, Is.EqualTo(10d));

            var opening = f.Grant("opening", AbilityKind.Writing, 1);
            var first = f.Start(TrainingSession.StarterRecipeId, opening);
            f.Finish(first);
            Assert.That(f.Session.ActiveModelId, Is.Null);
            Assert.That(f.Session.Models.Count, Is.EqualTo(1));
            var restored = f.RestoreFromJson();
            Assert.That(restored.Models.Count, Is.EqualTo(1));
            Assert.That(restored.ActiveModelId, Is.Null);
            Assert.That(restored.ActiveModel, Is.Null);
            Assert.That(restored.TokensPerSecond, Is.EqualTo(10d));
            Assert.That(restored.SelectModel(restored.Models.Single().Id), Is.EqualTo(TrainingResult.Success));
        }

        private sealed class Fixture
        {
            internal readonly TaskSimulation Sim;
            internal readonly TrainingSession Session;
            private int funds = 200;

            internal Fixture()
            {
                Sim = new TaskSimulation(4, 4, new[] { new Cell(1, 1) },
                    new ModelDefinition("initial", 1, new Dictionary<AbilityKind, double>()), null);
                Sim.Paused = true;
                Assert.That(Sim.TrySetTokenThroughput(10), Is.True);
                Session = new TrainingSession(Sim, null,
                    new[] { new TrainingRecipe("dialogue-training", "对话训练", 20, 1, 120, 1, 1,
                        new[] { new Cell(0, 0), new Cell(1, 0), new Cell(0, 1), new Cell(1, 1) }, 2, 100, 5, 1.5) },
                    new[] { new ArchitectureDefinition("baseline", "Transformer", 1, OutlineTransform.Keep) },
                    new[] { new ComputeTier("compute-1", 10, 0) },
                    amount => { if (funds < amount) return false; funds -= amount; return true; },
                    amount => funds += amount,
                    profile => Sim.TryChangeExecutionProfile(profile.Model, profile.TokensPerSecond, profile.OpenCells, profile.Architecture),
                    rate => Sim.TrySetTokenThroughput(rate),
                    () => new[] { new Cell(1, 1) }, currentStage: () => 1);
            }

            internal string Grant(string eventId, AbilityKind kind, double points)
            {
                Assert.That(Session.GrantScriptedCorpus(eventId, "narrative-doctor",
                    new Dictionary<AbilityKind, double> { { kind, points } }, "test"), Is.EqualTo(TrainingResult.Success));
                return Session.Corpus.Last().Id;
            }

            internal TrainingJob Start(string recipe, string corpusId)
            {
                Assert.That(Session.StartTraining(recipe, "baseline", "新模型", new[] { corpusId }, out var job),
                    Is.EqualTo(TrainingResult.Success));
                return job;
            }

            internal void Finish(TrainingJob job)
            {
                var task = Sim.Tasks[job.RequestId];
                Assert.That(Sim.TryPlace(task.Id, new Cell(1, 1), 0, out var failure), Is.True, failure.ToString());
                Sim.Paused = false;
                Sim.Advance(1);
                Sim.Paused = true;
                Assert.That(task.State, Is.EqualTo(RequestState.Ready));
                Assert.That(Session.CompleteTraining(job.RequestId), Is.EqualTo(TrainingResult.Success));
                Sim.DrainEvents();
            }

            internal TrainingSession RestoreFromJson()
            {
                Sim.DrainEvents();
                var json = UnityEngine.JsonUtility.ToJson(Session.CaptureState());
                var decoded = UnityEngine.JsonUtility.FromJson<TrainingSessionState>(json);
                var simulation = TaskSimulation.RestoreState(Sim.CaptureState());
                return TrainingSession.RestoreState(decoded, simulation, Session.Recipes, Session.Architectures, Session.ComputeTiers,
                    amount => { if (funds < amount) return false; funds -= amount; return true; },
                    amount => funds += amount,
                    profile => simulation.TryChangeExecutionProfile(profile.Model, profile.TokensPerSecond, profile.OpenCells, profile.Architecture),
                    rate => simulation.TrySetTokenThroughput(rate),
                    () => new[] { new Cell(1, 1) }, currentStage: () => 1, restoredTokensPerSecond: simulation.TokenThroughput);
            }
        }
    }
}
