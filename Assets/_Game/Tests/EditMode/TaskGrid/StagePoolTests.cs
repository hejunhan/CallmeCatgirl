using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CallmeCatgirl.Gameplay.TaskGrid;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = System.Random;

namespace CallmeCatgirl.Tests
{
    public sealed class StagePoolTests
    {
        private readonly List<ScriptableObject> resources = new List<ScriptableObject>();
        private static Type StoryType => Type.GetType("CallmeCatgirl.Gameplay.ConversationStory, Assembly-CSharp", true);
        private static Type ConfigType => Type.GetType("CallmeCatgirl.Gameplay.StoryConfiguration, Assembly-CSharp", true);
        private static Type PoolType => Type.GetType("CallmeCatgirl.Gameplay.OrdinaryStagePool, Assembly-CSharp", true);
        private static Type EconomyType => Type.GetType("CallmeCatgirl.Gameplay.EconomyManager, Assembly-CSharp", true);
        private static Type PersistenceType(string name)
        {
            string fullName = "CallmeCatgirl.Persistence." + name;
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(fullName))
                .FirstOrDefault(found => found != null);
            Assert.That(type, Is.Not.Null, "Missing persistence type " + fullName);
            return type;
        }
        private static Type ProgressionType(string name)
            => Type.GetType("CallmeCatgirl.Gameplay.Progression." + name + ", CallmeCatgirl.Progression", true);
        private static object Call(object target, string name, params object[] args)
        {
            var methods = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(m => m.Name == name && m.GetParameters().Length == args.Length).ToArray();
            Assert.That(methods.Length, Is.EqualTo(1), "Ambiguous/missing method " + name);
            return methods[0].Invoke(target, args);
        }
        private static T Field<T>(object target, string name)
            => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(target);
        private static T Value<T>(object target, string name)
            => (T)target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(target);
        private static void Set(object target, string name, object value)
            => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(target, value);
        private static int StageOf(string id)
            => (int)PoolType.GetMethod("StageOf", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { id });
        private static void SetStage(object story, Func<int> provider)
            => story.GetType().GetProperty("StageProvider").SetValue(story, provider);
        private static TaskDefinition Basis() => new TaskDefinition("A", "fallback", new[] { new Cell(0, 0) }, 100, 10, 60, 0,
            new[] { new AbilityRequirement(AbilityKind.Writing, 1, 2) });
        private static TaskSimulation Simulation() => new TaskSimulation(1, 1, new[] { new Cell(0, 0) },
            new ModelDefinition("model", 1, new Dictionary<AbilityKind, double>
            {
                { AbilityKind.Writing, 1000 }, { AbilityKind.Code, 1000 },
                { AbilityKind.Reasoning, 1000 }, { AbilityKind.Retrieval, 1000 }
            }));
        private static object NewStory(object config, object pool, Func<int> stage, Random random)
        {
            var story = Activator.CreateInstance(StoryType, new object[] { config, null, random, pool });
            SetStage(story, stage);
            return story;
        }
        private ScriptableObject SlotConfig()
        {
            var config = (ScriptableObject)ScriptableObject.CreateInstance(ConfigType);
            var userType = ConfigType.GetNestedType("UserStory");
            var sessionType = ConfigType.GetNestedType("ConversationSession");
            var kindField = userType.GetField("Kind");
            var users = Array.CreateInstance(userType, 3);
            for (int i = 0; i < 3; i++)
            {
                var user = Activator.CreateInstance(userType);
                Set(user, "Kind", Enum.ToObject(kindField.FieldType, 0));
                Set(user, "UserId", ((char)('A' + i)).ToString());
                Set(user, "DisplayName", "Slot " + (char)('A' + i));
                Set(user, "Sessions", Array.CreateInstance(sessionType, 0));
                users.SetValue(user, i);
            }
            Set(config, "Users", users);
            resources.Add(config);
            return config;
        }
        private object LoadPool()
        {
            var pool = (ScriptableObject)PoolType.GetMethod("Load", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
            Assert.That(pool, Is.Not.Null, "ordinary-pool-stages.json must load.");
            resources.Add(pool);
            return pool;
        }
        private static object Capture(object story) => Call(story, "CaptureState");
        private static object[] CapturedUsers(object story) => ((Array)Field<object>(Capture(story), "Users")).Cast<object>().ToArray();
        private static string PoolEntry(object userState) => Field<string>(userState, "PoolEntryId");
        private static string[] Used(object story) => Field<string[]>(Capture(story), "UsedPoolEntryIds");
        private static string[] PoolIds(object pool)
            => ((Array)Field<object>(pool, "Users")).Cast<object>().Select(u => Field<string>(u, "UserId")).ToArray();
        private static string[] SessionIds(object user)
            => ((Array)Field<object>(user, "Sessions")).Cast<object>().Select(s => Field<string>(s, "Id")).ToArray();
        private static Dictionary<string, string> Mapping(object story)
            => CapturedUsers(story).ToDictionary(u => Field<string>(u, "UserId"), PoolEntry);
        private static void PrepareAll(object story)
        {
            foreach (string slot in new[] { "A", "B", "C" }) Assert.That((bool)Call(story, "PrepareSlot", slot), Is.True);
        }
        private static void CompleteSession(object story, string user, ref int serial)
        {
            var basis = Basis();
            var definition = (TaskDefinition)Call(story, "BuildNextTask", user,
                new Dictionary<string, TaskDefinition>(), basis);
            Assert.That(definition, Is.Not.Null, "No task remains for " + user);
            var simulation = Simulation();
            string id = "task-" + (++serial);
            Assert.That(simulation.TryCreate(id, user, id, definition, out var task), Is.True);
            Call(story, "Arrived", task);
            Assert.That(simulation.TryPlace(id, new Cell(0, 0), 0, out var failure), Is.True, failure.ToString());
            simulation.Advance(definition.Calculation / definition.Speed + 1);
            Assert.That(simulation.TrySubmit(id), Is.True);
            Call(story, "Submitted", task);
            Assert.That((bool)Call(story, "Choose", 0), Is.True);
            if (Value<bool>(story, "IsReply")) Assert.That((bool)Call(story, "Choose", 0), Is.True);
        }
        private static void CompleteCustomer(object story, string user, ref int serial)
        {
            CompleteSession(story, user, ref serial);
            CompleteSession(story, user, ref serial);
            Assert.That((bool)Call(story, "Finished", user), Is.True, "Two authored sessions must finish " + user);
        }
        private static object NewEconomy() => Activator.CreateInstance(EconomyType, new object[] { 100, 1 });

        [Test] public void PoolContainsFifteenCustomersAcrossFiveStages()
        {
            var pool = LoadPool();
            var users = ((Array)Field<object>(pool, "Users")).Cast<object>().ToArray();
            Assert.That(users.Length, Is.EqualTo(15));
            Assert.That(PoolIds(pool).Distinct().Count(), Is.EqualTo(15));
            for (int stage = 1; stage <= 5; stage++)
            {
                var stageUsers = users.Where(u => StageOf(Field<string>(u, "UserId")) == stage).ToArray();
                Assert.That(stageUsers.Length, Is.EqualTo(3), "Stage " + stage + " needs three customers.");
                Assert.That(stageUsers.All(u => SessionIds(u).Length == 2), Is.True, "Every stage customer has two sessions.");
            }
        }

        [Test] public void FirstRefreshBindsThreeDistinctStageOneCustomers()
        {
            var pool = LoadPool();
            var config = SlotConfig();
            var story = NewStory(config, pool, () => 1, new ZeroRandom());
            PrepareAll(story);

            var users = CapturedUsers(story);
            var entries = users.Select(PoolEntry).ToArray();
            Assert.That(users.Length, Is.EqualTo(3));
            Assert.That(entries.Distinct().Count(), Is.EqualTo(3));
            Assert.That(entries.All(id => StageOf(id) == 1), Is.True);
            Assert.That(Used(story).Length, Is.EqualTo(3));
            Assert.That(Used(story).OrderBy(id => id), Is.EqualTo(entries.OrderBy(id => id)));
            foreach (string slot in new[] { "A", "B", "C" })
            {
                Assert.That((string)Call(story, "Name", slot), Is.Not.EqualTo("用户 " + slot));
                Assert.That(Convert.ToInt32(Call(story, "Kind", slot)), Is.EqualTo(0));
                Assert.That((bool)Call(story, "Finished", slot), Is.False);
                var definition = (TaskDefinition)Call(story, "BuildNextTask", slot,
                    new Dictionary<string, TaskDefinition>(), Basis());
                Assert.That(definition, Is.Not.Null);
                Assert.That(definition.Title, Is.Not.Null.And.Not.Empty);
            }
            Assert.That(users.Select(u => (string)Call(story, "Name", Field<string>(u, "UserId"))).Distinct().Count(), Is.EqualTo(3));
        }

        [Test] public void NonSlotIdsNeverDrawFromStagePool()
        {
            var pool = LoadPool();
            var config = SlotConfig();
            var story = NewStory(config, pool, () => 1, new Random(9));
            PrepareAll(story);
            int used = Used(story).Length;
            int users = CapturedUsers(story).Length;

            Assert.That((bool)Call(story, "PrepareSlot", "narrative-first-visitor"), Is.False);
            Assert.That((bool)Call(story, "PrepareSlot", "new-user-1"), Is.False);
            Assert.DoesNotThrow(() => Call(story, "BuildNextTask", "narrative-first-visitor",
                new Dictionary<string, TaskDefinition>(), Basis()));
            Assert.That(Used(story).Length, Is.EqualTo(used));
            Assert.That(CapturedUsers(story).Length, Is.EqualTo(users + 1));
            Assert.That((bool)Call(story, "Finished", "narrative-first-visitor"), Is.False);
            Assert.That(Call(story, "Name", "narrative-first-visitor"), Is.Not.Null);
        }

        [Test] public void EachCustomerKeepsTwoSessionsWhenRandomWouldEndEarly()
        {
            var pool = LoadPool();
            var config = SlotConfig();
            var story = NewStory(config, pool, () => 1, new ZeroRandom());
            Assert.That((bool)Call(story, "PrepareSlot", "A"), Is.True);
            int serial = 0;
            CompleteSession(story, "A", ref serial);
            Assert.That((bool)Call(story, "Finished", "A"), Is.False, "Stage customers never roll a random ending.");
            Assert.That((int)Call(story, "SessionNumber", "A"), Is.EqualTo(2));
            CompleteSession(story, "A", ref serial);
            Assert.That((bool)Call(story, "Finished", "A"), Is.True);
            Assert.That(Call(story, "BuildNextTask", "A", new Dictionary<string, TaskDefinition>(), Basis()), Is.Null);
        }

        [Test] public void StageUpgradeRebindsFinishedSlotAndKeepsBusySlots()
        {
            var pool = LoadPool();
            var config = SlotConfig();
            int stage = 1;
            var story = NewStory(config, pool, () => stage, new Random(2));
            PrepareAll(story);
            var before = Mapping(story);

            int serial = 0;
            CompleteCustomer(story, "A", ref serial);
            stage = 2;
            Assert.That((bool)Call(story, "PrepareSlot", "A"), Is.True);
            var after = Mapping(story);

            Assert.That(StageOf(after["A"]), Is.EqualTo(2));
            Assert.That(Used(story).Contains(before["A"]), Is.True);
            Assert.That(after["B"], Is.EqualTo(before["B"]));
            Assert.That(after["C"], Is.EqualTo(before["C"]));
            Assert.That((bool)Call(story, "PrepareSlot", "B"), Is.True);
            var keptB = CapturedUsers(story).Single(u => Field<string>(u, "UserId") == "B");
            Assert.That(PoolEntry(keptB), Is.EqualTo(before["B"]));
            Assert.That(Used(story).Length, Is.EqualTo(4));
        }

        [Test] public void FullPoolIsConsumedOnceAndExhaustionReturnsFalseWithoutThrowing()
        {
            var pool = LoadPool();
            var config = SlotConfig();
            int stage = 1;
            var story = NewStory(config, pool, () => stage, new Random(3));
            int serial = 0;
            for (stage = 1; stage <= 5; stage++)
            {
                PrepareAll(story);
                foreach (string slot in new[] { "A", "B", "C" }) CompleteCustomer(story, slot, ref serial);
            }

            Assert.That(Used(story).Length, Is.EqualTo(15));
            Assert.That(Used(story).Distinct().Count(), Is.EqualTo(15));
            var slots = Mapping(story);
            Assert.That(slots.Count, Is.EqualTo(3));
            Assert.That(slots.Values.All(id => StageOf(id) == 5), Is.True);
            string previous = slots["A"];
            Assert.That((bool)Call(story, "PrepareSlot", "A"), Is.False);
            Assert.That((bool)Call(story, "PrepareSlot", "D"), Is.False);
            var keptA = CapturedUsers(story).Single(u => Field<string>(u, "UserId") == "A");
            Assert.That(PoolEntry(keptA), Is.EqualTo(previous));
            Assert.That(Used(story).Length, Is.EqualTo(15));
        }

        [Test] public void NameAndFinishedNeverBindUnknownCustomers()
        {
            var pool = LoadPool();
            var config = SlotConfig();
            var story = NewStory(config, pool, () => 1, new Random(4));
            Assert.DoesNotThrow(() => Call(story, "Name", "A"));
            Assert.That((bool)Call(story, "Finished", "A"), Is.False);
            Assert.That((bool)Call(story, "Finished", "missing"), Is.False);
            Assert.That((double)Call(story, "Affinity", "missing"), Is.EqualTo(0));
            Assert.That(CapturedUsers(story).Length, Is.EqualTo(0));
            Assert.That(Used(story).Length, Is.EqualTo(0));
        }

        [Test] public void VersionFiveRoundTripsThroughValidationAndRestoreBuilder()
        {
            var pool = LoadPool();
            var config = SlotConfig();
            int stage = 1;
            var story = NewStory(config, pool, () => stage, new Random(5));
            PrepareAll(story);
            var saved = Capture(story);
            Assert.That(Field<int>(saved, "Version"), Is.EqualTo(5));
            Assert.That(Field<bool>(saved, "UsesStagePool"), Is.True);

            var data = BuildRunData(story, Simulation(), "stage-revision");
            ValidateRun(data, "stage-revision");
            var restored = BuildRun(data, config, pool, "stage-revision", () => stage);
            var restoredStory = Value<object>(restored, "Story");
            var restoredState = Capture(restoredStory);
            Assert.That(Field<bool>(restoredState, "UsesStagePool"), Is.True);
            Assert.That(Field<string[]>(restoredState, "UsedPoolEntryIds"), Is.EqualTo(Field<string[]>(saved, "UsedPoolEntryIds")));
            var expected = Mapping(story);
            var actual = Mapping(restoredStory);
            Assert.That(actual.Count, Is.EqualTo(expected.Count));
            foreach (var pair in expected) Assert.That(actual[pair.Key], Is.EqualTo(pair.Value));
            Assert.That((string)Call(restoredStory, "Name", "A"), Is.EqualTo((string)Call(story, "Name", "A")));

            int serial = 100;
            CompleteCustomer(restoredStory, "A", ref serial);
            stage = 2;
            Assert.That((bool)Call(restoredStory, "PrepareSlot", "A"), Is.True);
            Assert.That(StageOf(Mapping(restoredStory)["A"]), Is.EqualTo(2));
            Assert.That(Used(restoredStory).Length, Is.EqualTo(4));
        }

        [Test] public void VersionFiveRejectsDuplicatePoolEntryAndLegacyUsage()
        {
            var pool = LoadPool();
            var config = SlotConfig();
            var story = NewStory(config, pool, () => 1, new Random(6));
            PrepareAll(story);
            var data = BuildRunData(story, Simulation(), "duplicate-revision");
            var saved = Field<object>(data, "Story");
            var users = ((Array)Field<object>(saved, "Users")).Cast<object>().ToArray();
            Set(users[1], "PoolEntryId", Field<string>(users[0], "PoolEntryId"));
            var duplicate = Assert.Throws<TargetInvocationException>(() => BuildRun(data, config, pool, "duplicate-revision", () => 1));
            Assert.That(duplicate.InnerException, Is.TypeOf<ArgumentException>());
            Assert.That(duplicate.InnerException.Message, Does.Contain("Duplicate stage customer binding"));

            var legacy = BuildRunData(story, Simulation(), "legacy-revision");
            var legacyStory = Field<object>(legacy, "Story");
            Set(legacyStory, "UsesStagePool", false);
            var validation = Assert.Throws<TargetInvocationException>(() => ValidateRun(legacy, "legacy-revision"));
            Assert.That(validation.InnerException.GetType().Name, Is.EqualTo("SaveCompatibilityException"));
            Assert.That(validation.InnerException.Message, Does.Contain("Legacy story cannot carry stage pool usage"));
        }

        private static object BuildRunData(object story, TaskSimulation simulation, string revision)
        {
            var data = Activator.CreateInstance(PersistenceType("RunSaveData"));
            Set(data, "SchemaVersion", 1);
            Set(data, "RunId", Guid.NewGuid().ToString("D"));
            Set(data, "ContentRevision", revision);
            Set(data, "SavedAtUtc", DateTimeOffset.UtcNow.ToString("O"));
            Set(data, "Simulation", simulation.CaptureState());
            var economy = NewEconomy();
            Set(data, "Economy", Call(economy, "CaptureState"));
            Set(data, "Policies", Activator.CreateInstance(ProgressionType("ProgressionState")));
            Set(data, "Story", Capture(story));
            Set(data, "Extensions", Array.CreateInstance(PersistenceType("SaveExtensionState"), 0));

            var host = Activator.CreateInstance(PersistenceType("HostState"));
            Set(host, "Version", 3);
            Set(host, "CurrentMonth", 1);
            Set(host, "MonthElapsed", 0d);
            Set(host, "SecondsPerMonth", 1d);
            Set(host, "MonthlyMaintenanceFee", 0);
            Set(host, "PlaybackSpeed", 1f);
            Set(host, "MemoryLevel", 1);
            Set(host, "CompletedTasks", 0);
            Set(host, "RequestSerial", 0);
            Set(host, "SelectedRequestId", null);
            Set(host, "GameEnded", false);
            Set(host, "EndingText", string.Empty);
            Set(host, "EndReason", string.Empty);
            Set(host, "MemoryUpgradeOpen", false);
            Set(host, "TaskBases", Array.CreateInstance(PersistenceType("TaskBasisState"), 0));
            Set(host, "OperatingRequestIds", Array.Empty<string>());
            var random = Activator.CreateInstance(PersistenceType("RandomStreamState"));
            Set(random, "Version", 1);
            Set(random, "AlgorithmId", "xorshift32");
            Set(random, "StateJson", "{\"word\":\"00000001\"}");
            Set(host, "GenerationRandom", random);
            Set(data, "Host", host);
            return data;
        }

        private static void ValidateRun(object data, string revision)
        {
            var validate = PersistenceType("RunSaveValidation").GetMethod("Validate", BindingFlags.Public | BindingFlags.Static);
            var extensions = Array.CreateInstance(Type.GetType("CallmeCatgirl.Persistence.IRunSaveExtension, CallmeCatgirl.Persistence", true), 0);
            validate.Invoke(null, new object[] { data, revision, extensions, null, null });
        }

        private static object BuildRun(object data, object config, object pool, string revision, Func<int> stage)
        {
            var build = PersistenceType("RunRestoreBuilder").GetMethod("Build", BindingFlags.Public | BindingFlags.Static);
            var nodeType = build.GetParameters()[4].ParameterType.GetGenericArguments()[0];
            var nodes = Array.CreateInstance(nodeType, 0);
            var extensions = Array.CreateInstance(Type.GetType("CallmeCatgirl.Persistence.IRunSaveExtension, CallmeCatgirl.Persistence", true), 0);
            var randomStateType = PersistenceType("RandomStreamState");
            var restoreRandomType = typeof(Func<,>).MakeGenericType(randomStateType, typeof(Random));
            var restoreRandom = Delegate.CreateDelegate(restoreRandomType,
                typeof(StagePoolTests).GetMethod("RestoreRandom", BindingFlags.NonPublic | BindingFlags.Static).MakeGenericMethod(randomStateType));
            return build.Invoke(null, new object[]
            {
                data, revision, config, null, nodes, restoreRandom, 0, extensions, null, pool,
                0d, 1d, -0.5d, null, stage
            });
        }

        private static Random RestoreRandom<T>(T state) => new Random(1);

        [TearDown] public void Cleanup()
        {
            foreach (var resource in resources) if (resource != null) Object.DestroyImmediate(resource);
            resources.Clear();
            PoolType.GetField("cached", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
        }

        private sealed class ZeroRandom : Random
        {
            public override int Next(int maxValue) => 0;
            public override double NextDouble() => 0;
        }
    }
}

