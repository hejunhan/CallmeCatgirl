using System;
using System.Collections.Generic;
using System.Linq;
using CallmeCatgirl.Gameplay.TaskGrid;
using NUnit.Framework;

namespace CallmeCatgirl.Gameplay.Training.Tests
{
    public sealed class CorpusInventoryQueryTests
    {
        private sealed class Fixture
        {
            public TaskSimulation Sim;
            public TrainingSession Session;
            public int Money;

            public Fixture()
            {
                var cells = Enumerable.Range(0, 64).Select(i => new Cell(i % 8, i / 8)).ToArray();
                Sim = new TaskSimulation(8, 8, cells, new ModelDefinition("starter", 1,
                    new Dictionary<AbilityKind,double>{{AbilityKind.Writing,100},{AbilityKind.Code,100},{AbilityKind.Reasoning,100}}));
                Sim.Paused = true; Sim.TrySetTokenThroughput(10);
                Session = new TrainingSession(Sim, new CatalogModel("初始", "base", Sim.Model, null),
                    new[]{new TrainingRecipe("train","训练",20,1,120,1,1,
                        new[]{new Cell(0,0),new Cell(1,0),new Cell(2,0),new Cell(0,1),new Cell(1,1),new Cell(2,1)},2,100,5,1.5)},
                    new[]{new ArchitectureDefinition("base","基础",1,OutlineTransform.Keep)},
                    new[]{new ComputeTier("one",10,0)},
                    amount => { if (Money < amount) return false; Money -= amount; return true; },
                    amount => Money += amount,
                    profile => Sim.TryChangeExecutionProfile(profile.Model, profile.TokensPerSecond, profile.OpenCells, profile.Architecture),
                    rate => Sim.TrySetTokenThroughput(rate),
                    () => cells);
                Money = 200;
            }

            public void Award(string conversationId, params (AbilityKind kind, double points)[] scores)
            {
                string id = "request-" + Sim.Tasks.Count;
                var def = new TaskDefinition(id, "测试", new[]{new Cell(0,0)}, 10, 1, 100, 0,
                    new[]{new AbilityRequirement(AbilityKind.Writing,1,2)});
                Assert.That(Sim.TryCreate(id, conversationId, id, def, out _), Is.True);
                Assert.That(Sim.TryPlace(id, new Cell(0,0), 0, out _), Is.True);
                Sim.Paused = false; Sim.Advance(1); Sim.Paused = true;
                Assert.That(Sim.TrySubmit(id), Is.True);
                Assert.That(Session.RecordDialogue(id, "final", "choice",
                    scores.ToDictionary(p => p.kind, p => p.points), "test"), Is.EqualTo(TrainingResult.Success));
                Assert.That(Sim.TryCloseFeedback(id), Is.True);
            }
        }

        [Test]
        public void ReservationChangesRemainingDomainInventoryButKeepsHistory()
        {
            var f = new Fixture();
            f.Award("A", (AbilityKind.Writing,8));
            f.Award("A", (AbilityKind.Code,2));
            f.Award("B", (AbilityKind.Writing,4), (AbilityKind.Code,6));

            Assert.That(f.Session.QueryConversationCorpora(domain: AbilityKind.Code)
                .Select(c => c.ConversationId), Is.EqualTo(new[]{"B","A"}));

            var codeRecord = f.Session.GetConversationCorpus("A").Records
                .Single(r => r.Scores.ContainsKey(AbilityKind.Code));
            Assert.That(f.Session.StartTraining("train","base","消耗代码",new[]{codeRecord.Id},out _),
                Is.EqualTo(TrainingResult.Success));

            var partial = f.Session.GetConversationCorpus("A");
            Assert.That(partial.AvailablePoints, Is.EqualTo(8).Within(1e-9));
            Assert.That(partial.AvailableScores.ContainsKey(AbilityKind.Code), Is.False);
            Assert.That(partial.TotalScores[AbilityKind.Code], Is.EqualTo(2).Within(1e-9));
            Assert.That(f.Session.QueryConversationCorpora(domain: AbilityKind.Code)
                .Select(c => c.ConversationId), Is.EqualTo(new[]{"B"}));
            Assert.That(f.Session.QueryConversationCorpora(domain: AbilityKind.Writing)
                .Select(c => c.ConversationId), Is.EqualTo(new[]{"A","B"}));
            Assert.That(f.Session.QueryConversationCorpora(minimumAvailablePoints: 9)
                .Select(c => c.ConversationId), Is.EqualTo(new[]{"B"}));

            Assert.That(f.Session.StartConversationTraining("train","base","全部","A",8,out _),
                Is.EqualTo(TrainingResult.Success));
            Assert.That(f.Session.QueryConversationCorpora(domain: AbilityKind.Writing)
                .Select(c => c.ConversationId), Is.EqualTo(new[]{"B"}));

            var history = f.Session.GetConversationCorpus("A");
            Assert.That(history.AvailablePoints, Is.EqualTo(0).Within(1e-9));
            Assert.That(history.AvailableScores.ContainsKey(AbilityKind.Code), Is.False);
            Assert.That(history.TotalScores[AbilityKind.Code], Is.EqualTo(2).Within(1e-9));
        }

        [Test]
        public void CombinesDomainDisplayTextAndInclusiveThreshold()
        {
            var f = new Fixture();
            f.Award("alpha-1", (AbilityKind.Writing,10), (AbilityKind.Code,2));
            f.Award("BETA-2", (AbilityKind.Writing,7));
            f.Award("甲-对话", (AbilityKind.Reasoning,9));

            var names = new Dictionary<string,string>(StringComparer.Ordinal)
            {
                {"alpha-1","写作对话"}, {"BETA-2","代码对话"}, {"甲-对话","推理对话"}
            };
            Func<string,string> name = id => names.TryGetValue(id, out var value) ? value : null;

            Assert.That(f.Session.QueryConversationCorpora(AbilityKind.Writing, "写作", 0, name)
                .Select(c => c.ConversationId), Is.EqualTo(new[]{"alpha-1"}));
            Assert.That(f.Session.QueryConversationCorpora(text: "alpha-1")
                .Select(c => c.ConversationId), Is.EqualTo(new[]{"alpha-1"}));
            Assert.That(f.Session.QueryConversationCorpora(text: "   ALPHA   ")
                .Select(c => c.ConversationId), Is.EqualTo(new[]{"alpha-1"}));

            Assert.That(f.Session.QueryConversationCorpora(domain: AbilityKind.Writing, minimumAvailablePoints: 12)
                .Select(c => c.ConversationId), Is.EqualTo(new[]{"alpha-1"}));
            // The public minimum remains inclusive without widening it by an epsilon.
            Assert.That(f.Session.QueryConversationCorpora(domain: AbilityKind.Writing, minimumAvailablePoints: 12 + 5e-10),
                Is.Empty);

            Assert.That(f.Session.QueryConversationCorpora(text: "   ")
                .Select(c => c.ConversationId), Is.EqualTo(new[]{"alpha-1","甲-对话","BETA-2"}));
            Assert.That(f.Session.QueryConversationCorpora(AbilityKind.Writing, "代码", 7, name)
                .Select(c => c.ConversationId), Is.EqualTo(new[]{"BETA-2"}));
        }

        [Test]
        public void EqualScoresSortByIdentifierRegardlessOfInsertionOrder()
        {
            string[] Query(bool reverse)
            {
                var f = new Fixture();
                if (reverse)
                {
                    f.Award("charlie", (AbilityKind.Writing,5));
                    f.Award("Alpha", (AbilityKind.Writing,5));
                    f.Award("bravo", (AbilityKind.Writing,5));
                }
                else
                {
                    f.Award("bravo", (AbilityKind.Writing,5));
                    f.Award("Alpha", (AbilityKind.Writing,5));
                    f.Award("charlie", (AbilityKind.Writing,5));
                }
                return f.Session.QueryConversationCorpora(domain: AbilityKind.Writing)
                    .Select(c => c.ConversationId).ToArray();
            }
            Assert.That(Query(false), Is.EqualTo(new[]{"Alpha","bravo","charlie"}));
            Assert.That(Query(true), Is.EqualTo(new[]{"Alpha","bravo","charlie"}));
        }

        [Test]
        public void QueryIsReadOnlyAndInvalidArgumentsFailBeforeCallback()
        {
            var f = new Fixture();
            f.Award("A", (AbilityKind.Writing,8), (AbilityKind.Code,2));
            f.Award("B", (AbilityKind.Reasoning,6));
            Assert.That(f.Session.StartConversationTraining("train","base","预留","A",3,out _),
                Is.EqualTo(TrainingResult.Success));

            var inventory = f.Session.ConversationCorpora
                .Select(c => (c.ConversationId, c.TotalPoints, c.AvailablePoints)).ToArray();
            var ledger = f.Session.Corpus.Select(r => f.Session.RemainingPoints(r.Id)).ToArray();
            var jobs = f.Session.Jobs.Select(j => j.RequestId).ToArray();
            int money = f.Money;

            int calls = 0;
            Func<string,string> name = id => { calls++; return id == "A" ? "写作对话" : "其他"; };
            Assert.That(f.Session.QueryConversationCorpora(AbilityKind.Writing, "写作", 0, name)
                .Select(c => c.ConversationId), Is.EqualTo(new[]{"A"}));
            Assert.That(calls, Is.GreaterThan(0));

            Assert.That(f.Session.ConversationCorpora
                .Select(c => (c.ConversationId, c.TotalPoints, c.AvailablePoints)), Is.EqualTo(inventory));
            Assert.That(f.Session.Corpus.Select(r => f.Session.RemainingPoints(r.Id)), Is.EqualTo(ledger));
            Assert.That(f.Session.Jobs.Select(j => j.RequestId), Is.EqualTo(jobs));
            Assert.That(f.Money, Is.EqualTo(money));

            calls = 0;
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                f.Session.QueryConversationCorpora(minimumAvailablePoints: -0.01, displayName: name));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                f.Session.QueryConversationCorpora(minimumAvailablePoints: double.NaN, displayName: name));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                f.Session.QueryConversationCorpora(minimumAvailablePoints: double.PositiveInfinity, displayName: name));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                f.Session.QueryConversationCorpora(minimumAvailablePoints: double.NegativeInfinity, displayName: name));
            Assert.Throws<ArgumentException>(() =>
                f.Session.QueryConversationCorpora(domain: (AbilityKind)999, displayName: name));
            Assert.That(calls, Is.Zero);

            Assert.That(f.Session.ConversationCorpora
                .Select(c => (c.ConversationId, c.TotalPoints, c.AvailablePoints)), Is.EqualTo(inventory));
            Assert.That(f.Session.Corpus.Select(r => f.Session.RemainingPoints(r.Id)), Is.EqualTo(ledger));
            Assert.That(f.Session.Jobs.Select(j => j.RequestId), Is.EqualTo(jobs));
            Assert.That(f.Money, Is.EqualTo(money));
        }
    }
}
