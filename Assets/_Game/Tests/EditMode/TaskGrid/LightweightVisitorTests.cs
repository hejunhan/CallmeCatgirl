using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CallmeCatgirl.Gameplay.TaskGrid;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CallmeCatgirl.Tests
{
    public sealed class LightweightVisitorTests
    {
        private readonly List<ScriptableObject> resources = new List<ScriptableObject>();
        private static Type ConfigType => Type.GetType("CallmeCatgirl.Gameplay.StoryConfiguration, Assembly-CSharp", true);
        private static Type FactoryType => Type.GetType("CallmeCatgirl.Gameplay.LightweightVisitorQueue, Assembly-CSharp", true);
        private static Type PoolType => Type.GetType("CallmeCatgirl.Gameplay.OrdinaryStagePool, Assembly-CSharp", true);

        private static object Call(object target, string name, params object[] args)
        {
            var methods = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(m => m.Name == name && m.GetParameters().Length == args.Length).ToArray();
            Assert.That(methods.Length, Is.EqualTo(1), "Ambiguous/missing method " + name);
            return methods[0].Invoke(target, args);
        }
        private static object CallStatic(Type type, string name, params object[] args)
        {
            var methods = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(m => m.Name == name && m.GetParameters().Length == args.Length).ToArray();
            Assert.That(methods.Length, Is.EqualTo(1), "Ambiguous/missing static method " + name);
            return methods[0].Invoke(null, args);
        }
        private static T Value<T>(object target, string name)
            => (T)target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(target);
        private static void Set(object target, string name, object value)
            => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(target, value);
        private static Array ArrayOf(Type element, params object[] values)
        {
            var array = Array.CreateInstance(element, values.Length);
            for (int i = 0; i < values.Length; i++) array.SetValue(values[i], i);
            return array;
        }

        private static TaskDefinition Definition(string id = "A") => new TaskDefinition(id, "source", new[] { new Cell(0, 0) },
            100, 10, 60, 12, new[] { new AbilityRequirement(AbilityKind.Writing, 1, 2) });

        private static TaskSimulation Simulation() => new TaskSimulation(1, 1, new[] { new Cell(0, 0) },
            new ModelDefinition("model", 1, new Dictionary<AbilityKind, double> { { AbilityKind.Writing, 1000 } }));

        private static VisitorIdentity Filler(string name = "林小满", string text = "帮我改一下简历。")
            => new VisitorIdentity(name, text, "A", "legacy-A");

        [Test] public void PolicyFillsDeficitAndStopsAtStageBudget()
        {
            Assert.That(VisitorQueuePolicy.ArrivalCount(1, 3, 0, 0, 0), Is.EqualTo(0), "Stage 1 is tutorial only.");
            Assert.That(VisitorQueuePolicy.ArrivalCount(2, 6, 60, 0, 0), Is.EqualTo(6));
            Assert.That(VisitorQueuePolicy.ArrivalCount(2, 6, 60, 50, 5), Is.EqualTo(1));
            Assert.That(VisitorQueuePolicy.ArrivalCount(2, 6, 60, 50, 6), Is.EqualTo(0), "Story arrivals already reach target.");
            Assert.That(VisitorQueuePolicy.ArrivalCount(2, 6, 60, 54, 0), Is.EqualTo(0), "targetRequests - 6 fillers are exhausted.");
            Assert.That(VisitorQueuePolicy.ArrivalCount(3, 9, 150, 0, 3), Is.EqualTo(6));
            Assert.That(VisitorQueuePolicy.ArrivalCount(4, 11, 220, 0, 0), Is.EqualTo(11));
            Assert.That(VisitorQueuePolicy.ArrivalCount(5, 12, 0, 0, 0), Is.EqualTo(0));
        }

        [Test] public void StageChangeKeepsIndependentTargetAndCounter()
        {
            int stage2Generated = VisitorQueuePolicy.StoryRequestSlots;
            for (int i = 0; i < 54; i++)
            {
                int arrivals = VisitorQueuePolicy.ArrivalCount(2, 6, 60, stage2Generated, 0);
                if (arrivals == 0) break;
                stage2Generated += arrivals;
            }
            Assert.That(stage2Generated, Is.EqualTo(54));
            Assert.That(VisitorQueuePolicy.ArrivalCount(2, 6, 60, stage2Generated, 0), Is.EqualTo(0));
            Assert.That(VisitorQueuePolicy.ArrivalCount(3, 9, 150, 0, 0), Is.EqualTo(9), "The next stage starts from its own counter.");
        }

        [Test] public void FillerTasksShareQueueConversationWhileStorySlotsStayUnique()
        {
            var simulation = Simulation();
            for (int serial = 1; serial <= 3; serial++)
                Assert.That(simulation.TryCreate(VisitorQueuePolicy.RequestId(2, serial), VisitorQueuePolicy.ConversationId,
                    serial.ToString(), Definition(), Filler(), out _), Is.True, "Filler " + serial);
            Assert.That(simulation.Tasks.Values.Count(task => task.IsFiller), Is.EqualTo(3));
            Assert.That(simulation.Tasks.Values.All(task => task.ConversationId == "filler"), Is.True);

            Assert.That(simulation.TryCreate("A-1", "A", "1", Definition(), out _), Is.True);
            Assert.That(simulation.TryCreate("A-2", "A", "2", Definition(), out _), Is.False, "Story slot still rejects a second active request.");
            Assert.That(VisitorQueuePolicy.UnfinishedCount(simulation.Tasks.Values), Is.EqualTo(4));
        }

        [Test] public void FillerStateRoundTripsWithDisplayIdentity()
        {
            var simulation = Simulation();
            Assert.That(simulation.TryCreate("filler-2-1", "filler", "2-1", Definition(), Filler(), out _), Is.True);
            Assert.That(simulation.TryCreate("filler-2-2", "filler", "2-2", Definition(),
                new VisitorIdentity("周序", "这段代码为什么报错？", "B", "B"), out _), Is.True);
            Assert.That(simulation.TryCreate("A-1", "A", "1", Definition(), out _), Is.True);
            Assert.That(simulation.TryCreate("filler-3-1", "filler", "3-1", Definition(),
                new VisitorIdentity("苏禾", "帮我理一下思路。", "C", "C"), out _), Is.True);
            simulation.DrainEvents();

            var restored = TaskSimulation.RestoreState(simulation.CaptureState());
            Assert.That(restored.Tasks.Count, Is.EqualTo(4));
            var first = restored.Tasks["filler-2-1"];
            Assert.That(first.IsFiller, Is.True);
            Assert.That(first.ConversationId, Is.EqualTo("filler"));
            Assert.That(first.Visitor.DisplayName, Is.EqualTo("林小满"));
            Assert.That(first.Visitor.SourceText, Is.EqualTo("帮我改一下简历。"));
            Assert.That(first.Visitor.RequestFamily, Is.EqualTo("A"));
            Assert.That(restored.Tasks["A-1"].IsFiller, Is.False);
            Assert.That(restored.Tasks["A-1"].Visitor, Is.Null);
            Assert.That(restored.TryCreate("filler-2-3", "filler", "3", Definition(), Filler("新的来客", "能帮我看一下吗？"), out _), Is.True);
        }

        [Test] public void RestoredCountersNeverFallBehindSurvivingFillers()
        {
            var simulation = Simulation();
            Assert.That(simulation.TryCreate("filler-2-7", "filler", "filler-2-7", Definition(), Filler(), out _), Is.True);
            Assert.That(simulation.TryCreate("filler-3-2", "filler", "filler-3-2", Definition(), Filler(), out _), Is.True);
            var merged = VisitorQueuePolicy.MergeCounters(new[] { 0, 4, 0, 0, 0 }, simulation.Tasks.Values);
            Assert.That(merged, Is.EqualTo(new[] { 0, 7, 2, 0, 0 }), "Saved counters keep progress, task serials prevent reuse.");
            Assert.That(VisitorQueuePolicy.MergeCounters(null, simulation.Tasks.Values), Is.EqualTo(new[] { 0, 7, 2, 0, 0 }));
        }

        [Test] public void FillerIdentityRequiresNameAndSourceText()
        {
            Assert.Throws<ArgumentException>(() => new VisitorIdentity("", "文本", "A"));
            Assert.Throws<ArgumentException>(() => new VisitorIdentity("名字", "", "A"));
        }

        [Test] public void SourcesIncludeEarlierStagesAndFallBackToCustomerName()
        {
            var story = Story("legacy-A", "林小满", "A", "帮我看一下简历。");
            var ordinary = Story("sample-01", "迟到的人", "B", "这个功能怎么调？");
            var stages = Story("stage1-c01", "", "A", "第一阶段的委托。", "stage2-c01", "第二阶段客户", "A", "当前阶段的委托。");

            var sources = ((IEnumerable)CallStatic(FactoryType, "Sources", story, ordinary, stages, 2)).Cast<object>().ToArray();
            Assert.That(sources.Select(source => Value<string>(source, "CustomerId")),
                Is.EquivalentTo(new[] { "legacy-A", "sample-01", "stage1-c01" }));
            Assert.That(Value<string>(sources.Single(source => Value<string>(source, "CustomerId") == "stage1-c01"), "DisplayName"),
                Is.EqualTo("客户 stage1-c01"));

            var stage3 = ((IEnumerable)CallStatic(FactoryType, "Sources", story, ordinary, stages, 3)).Cast<object>()
                .Select(source => Value<string>(source, "CustomerId")).ToArray();
            Assert.That(stage3, Does.Contain("stage2-c01"));
            Assert.That(stage3, Does.Contain("stage1-c01"));
        }

        [Test] public void FillerDefinitionUsesStageDifficultyAndFillerTokens()
        {
            object source = Activator.CreateInstance(FactoryType.Assembly.GetType("CallmeCatgirl.Gameplay.VisitorSource"),
                "林小满", "帮我改一下简历。", "A", "legacy-A");
            var ruleArguments = new object[] { 2, null };
            Assert.That((bool)CallStatic(PoolType, "TryGetRule", ruleArguments), Is.True);
            object rule = ruleArguments[1];
            Assert.That(Value<double>(rule, "Calculation"), Is.EqualTo(900));
            Assert.That(Value<double>(rule, "FillerTokens"), Is.EqualTo(12));
            Assert.That(Value<int>(rule, "TargetConcurrent"), Is.EqualTo(6));
            Assert.That(Value<int>(rule, "TargetRequests"), Is.EqualTo(60));

            var definition = (TaskDefinition)CallStatic(FactoryType, "Build", "filler-2-1", source, rule, new[] { new Cell(0, 0) });
            Assert.That(definition.Calculation, Is.EqualTo(900));
            Assert.That(definition.Patience, Is.EqualTo(120));
            Assert.That(definition.Tokens, Is.EqualTo(12));
            Assert.That(definition.Requirements.Single().Kind, Is.EqualTo(AbilityKind.Writing));
            Assert.That(definition.Requirements.Single().Minimum, Is.EqualTo(90));
            Assert.That(definition.Requirements.Single().Excellent, Is.EqualTo(108));
        }

        private ScriptableObject Story(params string[] rows)
        {
            var config = (ScriptableObject)ScriptableObject.CreateInstance(ConfigType);
            var userType = ConfigType.GetNestedType("UserStory");
            var sessionType = ConfigType.GetNestedType("ConversationSession");
            var roundType = ConfigType.GetNestedType("StoryNode");
            var users = Array.CreateInstance(userType, rows.Length / 4);
            for (int i = 0; i < rows.Length / 4; i++)
            {
                var user = Activator.CreateInstance(userType);
                Set(user, "Kind", Enum.ToObject(userType.GetField("Kind").FieldType, 0));
                Set(user, "UserId", rows[i * 4]);
                Set(user, "DisplayName", rows[i * 4 + 1]);
                var session = Activator.CreateInstance(sessionType);
                Set(session, "Id", "s1");
                Set(session, "RequestId", rows[i * 4 + 2]);
                var round = Activator.CreateInstance(roundType);
                Set(round, "Id", "r1");
                Set(round, "UserMessage", rows[i * 4 + 3]);
                Set(session, "Rounds", ArrayOf(roundType, round));
                Set(user, "Sessions", ArrayOf(sessionType, session));
                users.SetValue(user, i);
            }
            Set(config, "Users", users);
            resources.Add(config);
            return config;
        }

        [TearDown] public void Cleanup()
        {
            foreach (var resource in resources) if (resource != null) Object.DestroyImmediate(resource);
            resources.Clear();
            PoolType.GetField("cached", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
        }
    }
}
