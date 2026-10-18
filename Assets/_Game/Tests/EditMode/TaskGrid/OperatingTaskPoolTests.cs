using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CallmeCatgirl.Gameplay.TaskGrid;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace CallmeCatgirl.Tests
{
    public sealed class OperatingTaskPoolTests
    {
        private readonly List<ScriptableObject> resources = new List<ScriptableObject>();
        private MonoBehaviour owner;
        private ScriptableObject original;
        private static Type GenerationType => Type.GetType("CallmeCatgirl.Gameplay.TaskGenerationConfiguration, Assembly-CSharp", true);
        private static object Call(object o, string method, params object[] args) => o.GetType().GetMethod(method).Invoke(o, args);
        private static T Field<T>(object o, string name) => (T)o.GetType().GetField(name).GetValue(o);
        private static void Set(object o, string name, object value) => o.GetType().GetField(name).SetValue(o, value);
        private static T Value<T>(object o, string name) => (T)o.GetType().GetProperty(name).GetValue(o);
        private ScriptableObject Generation()
        { var g = ScriptableObject.CreateInstance(GenerationType); resources.Add(g); return g; }
        private static void SingleShape(object g)
        {
            var type = GenerationType.GetNestedType("ShapePreset"); var shape = Activator.CreateInstance(type);
            Set(shape, "Name", "fixture"); Set(shape, "Pattern", new[] { "#" });
            var shapes = Array.CreateInstance(type, 1); shapes.SetValue(shape, 0); Set(g, "Shapes", shapes);
        }
        private static Array Pool(object generation, int count = 2)
        {
            var type = generation.GetType().GetField("OperatingTasks").FieldType.GetElementType();
            var pool = Array.CreateInstance(type, count);
            for (int i = 0; i < count; i++)
            {
                var task = Activator.CreateInstance(type);
                Set(task, "Id", "ops-" + i); Set(task, "Title", "经营样例" + i);
                Set(task, "Calculation", 6f); Set(task, "Speed", 1f); Set(task, "Patience", 20f); Set(task, "Tokens", 2f);
                Set(task, "Minimum", new Vector3(1, 0, 0)); Set(task, "Excellent", new Vector3(3, 3, 3)); pool.SetValue(task, i);
            }
            Set(generation, "OperatingTasks", pool); return pool;
        }
        [Test] public void RealPoolHasFiveEasyLongLowRewardTasksWithQueueAllowance()
        {
            var g = AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/_Game/Data/TaskGrid/TaskGenerationConfiguration.asset");
            Call(g, "Validate", 8, 8); var pool = Field<Array>(g, "OperatingTasks"); Assert.That(pool.Length, Is.EqualTo(5));
            foreach (var source in pool)
            {
                var def = (TaskDefinition)Call(source, "Build");
                Assert.That(def.Calculation / def.Speed, Is.InRange(60, 150)); Assert.That(def.Tokens, Is.InRange(15, 30));
                Assert.That(def.Patience, Is.GreaterThanOrEqualTo(def.Calculation / def.Speed * 1.9 + 60));
                Assert.That(def.Requirements.All(r => r.Minimum == 1), Is.True);
            }
        }
        [Test] public void SamplingIsSeededUsesSharedShapesAndDoesNotMutateTemplates()
        {
            var g = Generation(); SingleShape(g); var pool = Pool(g);
            var a = new System.Random(23); var b = new System.Random(23); var seen = new HashSet<string>();
            for (int i = 0; i < 50; i++)
            {
                var left = (TaskDefinition)Call(g, "PickOperatingTask", a); var right = (TaskDefinition)Call(g, "PickOperatingTask", b);
                Assert.That(left.Id, Is.EqualTo(right.Id)); Assert.That(left.Shape, Is.EqualTo(new[] { new Cell(0, 0) }));
                Assert.That(left.Calculation, Is.EqualTo(6)); Assert.That(left.Tokens, Is.EqualTo(2)); seen.Add(left.Id);
            }
            Assert.That(seen.Count, Is.EqualTo(2)); Assert.That(Field<float>(pool.GetValue(0), "Calculation"), Is.EqualTo(6));
        }
        [Test] public void EmptyPoolPreservesLegacyRandomStream()
        {
            var g = Generation(); var a = new System.Random(12); var b = new System.Random(12);
            Assert.That(Value<bool>(g, "HasOperatingTasks"), Is.False); Assert.That(Call(g, "PickOperatingTask", a), Is.Null);
            Assert.That(a.Next(), Is.EqualTo(b.Next()));
        }
        [TestCase("Calculation", float.NaN)] [TestCase("Speed", 0f)] [TestCase("Tokens", -1f)]
        public void InvalidTaskValuesAreRejected(string field, float value)
        {
            var g = Generation(); var pool = Pool(g); Set(pool.GetValue(0), field, value);
            Assert.Throws<TargetInvocationException>(() => Call(g, "Validate", 8, 8));
        }
        [Test] public void DuplicateIdsAndInvalidAbilityThresholdsAreRejected()
        {
            var g = Generation(); var pool = Pool(g); Set(pool.GetValue(1), "Id", "ops-0");
            Assert.Throws<TargetInvocationException>(() => Call(g, "Validate", 8, 8));
            Set(pool.GetValue(1), "Id", "ops-1"); Set(pool.GetValue(0), "Minimum", new Vector3(float.NaN, 0, 0));
            Assert.Throws<TargetInvocationException>(() => Call(g, "Validate", 8, 8));
        }
        private ScriptableObject Configure(bool twoCharacters)
        {
            owner = Object.FindObjectsByType<MonoBehaviour>().Single(o => o.GetType().Name == "CoreGameplayController");
            var field = owner.GetType().GetField("configuration", BindingFlags.Instance | BindingFlags.NonPublic);
            original = (ScriptableObject)field.GetValue(owner); var config = Object.Instantiate(original); resources.Add(config);
            Set(config, "OpeningBalance", null); Set(config, "OrdinaryDialoguePool", null);
            var g = Generation(); SingleShape(g); Pool(g); Set(config, "TaskGeneration", g);
            Set(config, "MonthlyMaintenanceFee", 0); Set(config, "SecondsPerGameMonth", 30f); Set(config, "TokenPrice", 1);
            Set(config, "InitialAffinity", 0f); Set(config, "SuccessAffinity", 1f); Set(config, "FailureAffinity", -.5f);
            Set(config, "ModelPoints", new Vector4(1, 1, 1, 0)); Set(config, "AbilityInterval", 1f);
            var type = config.GetType().GetNestedType("DemoTask"); var task = Activator.CreateInstance(type);
            Set(task, "Id", "A"); Set(task, "Title", "角色任务"); Set(task, "Calculation", 3f); Set(task, "Speed", 1f);
            Set(task, "Patience", 300f); Set(task, "Tokens", 100f); Set(task, "Minimum", new Vector3(1, 0, 0));
            Set(task, "Excellent", new Vector3(2, 2, 2)); var tasks = Array.CreateInstance(type, 1); tasks.SetValue(task, 0); Set(config, "Requests", tasks);
            var ids = twoCharacters ? new[] { "A", "B" } : new[] { "A" };
            var characters = ids.Select(id => "{\"id\":\"" + id + "\",\"kind\":\"Scripted\",\"name\":\"角色" + id + "\",\"sessions\":[{\"id\":\"s1\",\"title\":\"角色任务一\",\"requestId\":\"A\",\"rounds\":[{\"id\":\"r1\",\"message\":\"你好\",\"choices\":[{\"label\":\"回复\"}]}]}" + (twoCharacters ? ",{\"id\":\"s2\",\"title\":\"角色任务二\",\"requestId\":\"A\",\"rounds\":[{\"id\":\"r1\",\"message\":\"再聊\",\"choices\":[{\"label\":\"回复\"}]}]}" : "") + "]}");
            var importer = Type.GetType("CallmeCatgirl.EditorTools.StoryJsonImporter, Assembly-CSharp-Editor", true);
            var story = (ScriptableObject)importer.GetMethod("Parse").Invoke(null, new object[] { "{\"version\":1,\"characters\":[" + string.Join(",", characters) + "]}", new[] { "A", "B" } });
            resources.Add(story); Set(config, "Story", story);
            var memory = Object.Instantiate(Field<ScriptableObject>(config, "MemoryUpgrades")); resources.Add(memory); Set(config, "MemoryUpgrades", memory);
            var next = Field<Array>(memory, "Levels").GetValue(0); Set(next, "Cost", 0); Set(next, "RequiredCompletedTasks", 0);
            var multipliers = Field<object>(next, "TaskMultipliers"); Set(multipliers, "CalculationMin", 2d); Set(multipliers, "CalculationMax", 2d);
            Set(multipliers, "RewardMin", 3d); Set(multipliers, "RewardMax", 3d);
            owner.GetType().GetField("training", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(owner, null);
                config.GetType().GetField("OrdinaryDialoguePool").SetValue(config, null);
                config.GetType().GetField("OpeningBalance").SetValue(config, null);
                field.SetValue(owner, config); Call(owner, "ResetSimulation"); return config;
        }
        private static void Place(TaskSimulation sim, string id)
        {
            for (int i = 0; i < sim.Width * sim.Height; i++) if (sim.TryPlace(id, new Cell(i % sim.Width, i / sim.Width), 0, out _)) return;
            Assert.Fail("Cannot place single-cell task.");
        }
        private static void Advance(object owner, double seconds)
        { var sim = Value<TaskSimulation>(owner, "Simulation"); sim.Paused = false; Call(owner, "AdvanceGame", seconds); sim.Paused = true; }
        private void FinishRole()
        {
            var sim = Value<TaskSimulation>(owner, "Simulation"); Place(sim, "A-1"); Advance(owner, 3); Call(owner, "SubmitSelected"); Call(owner, "ChooseReply", 0);
        }
        [UnityTest] public IEnumerator MonthlyRefreshPrioritizesStoryAndFillsQuotaWhileJobsSettleWithoutDialogue()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity"); yield return new EnterPlayMode(); yield return null;
            CheckMonthlyFlow();
        }
        // 场景切换后再构造带LINQ闭包的普通调用，避免EnterPlayMode序列化迭代器中的捕获变量。
        private void CheckMonthlyFlow()
        {
            Configure(true); FinishRole(); var sim = Value<TaskSimulation>(owner, "Simulation"); var story = Value<object>(owner, "Story");
            Advance(owner, 27); var batch = sim.Tasks.Values.Where(t => t.ArrivedAt == 30).ToArray(); Assert.That(batch.Length, Is.EqualTo(5));
            var jobs = batch.Where(t => (bool)Call(owner, "IsOperatingTask", t.Id)).ToArray(); Assert.That(jobs.Length, Is.EqualTo(3));
            Assert.That(batch.Single(t => t.ConversationId == "A").Definition.Title, Is.EqualTo("角色任务二"));
            Assert.That(batch.Single(t => t.ConversationId == "B").Definition.Title, Is.EqualTo("角色任务一"));
            var job = jobs[0]; Place(sim, job.Id); Advance(owner, 6); int funds = Value<int>(owner, "Funds"), completed = Value<int>(owner, "CompletedTasks");
            Call(owner, "SelectRequest", job.Id); Call(owner, "SubmitSelected");
            Assert.That(job.State, Is.EqualTo(RequestState.Closed)); Assert.That(Value<bool>(owner, "IsStoryOpen"), Is.False);
            Assert.That(Value<int>(owner, "Funds"), Is.EqualTo(funds + 2)); Assert.That(Value<int>(owner, "CompletedTasks"), Is.EqualTo(completed + 1));
            Assert.That(Call(story, "Affinity", "A"), Is.EqualTo(1)); Assert.That(Call(story, "Affinity", "B"), Is.EqualTo(0));
            Call(owner, "OpenConversationHistory"); Assert.That(Value<bool>(owner, "IsStoryOpen"), Is.False);
            Advance(owner, 14); Assert.That(jobs[1].State, Is.EqualTo(RequestState.Closed)); Assert.That(Call(story, "Affinity", "A"), Is.EqualTo(1));
            Advance(owner, 10); var second = sim.Tasks.Values.Where(t => t.ArrivedAt == 60).ToArray();
            Assert.That(second.Length, Is.EqualTo(5)); Assert.That(second.All(t => (bool)Call(owner, "IsOperatingTask", t.Id)), Is.True);
            Assert.That(batch.Where(t => !jobs.Contains(t)).All(t => t.State == RequestState.Waiting), Is.True);
            Call(owner, "SelectRequest", second[0].Id);
            var view = Object.FindObjectsByType<MonoBehaviour>().Single(o => o.GetType().Name == "CoreGameplayCanvasView"); Call(view, "Refresh");
            typeof(SystemFeedbackTests).GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { "OperatingTasks.png" });
        }
        [UnityTest] public IEnumerator ManualJobsAndFinishedStoryUsePoolAndUpgradesOnlyScaleNewJobs()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity"); yield return new EnterPlayMode(); yield return null;
            Configure(false); FinishRole(); Assert.That(Call(Value<object>(owner, "Story"), "Finished", "A"), Is.True);
            Call(owner, "AddRequest"); var old = Value<TaskInstance>(owner, "SelectedTask"); var definition = old.Definition;
            Assert.That(Call(owner, "IsOperatingTask", old.Id), Is.True); Assert.That(old.Definition.Calculation, Is.EqualTo(6));
            Call(owner, "OpenMemoryUpgrade"); Call(owner, "UpgradeMemory");
            Assert.That(Value<bool>(owner,"IsMemoryInstallOpen"),Is.True);
            Call(Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Single(o=>o.GetType().Name=="StandaloneMemoryInstall"),"SkipInstall");
            Call(owner, "CloseMemoryUpgrade");
            Assert.That(Value<int>(owner, "MemoryLevel"), Is.EqualTo(2)); Assert.That(old.Definition, Is.SameAs(definition));
            Call(owner, "AddRequest"); var next = Value<TaskInstance>(owner, "SelectedTask");
            Assert.That(next.Definition.Calculation, Is.EqualTo(12)); Assert.That(next.Definition.Tokens, Is.EqualTo(6));
            Advance(owner, 27); var sim = Value<TaskSimulation>(owner, "Simulation");
            var batch = sim.Tasks.Values.Where(t => t.ArrivedAt == 30).ToArray(); Assert.That(batch.Length, Is.EqualTo(5));
            Assert.That(batch.All(t => (bool)Call(owner, "IsOperatingTask", t.Id)), Is.True);
            Assert.That(sim.Tasks.Values.Count(t => t.ConversationId == "A"), Is.EqualTo(1), "Finished role task never returns as filler.");
        }
        [UnityTest] public IEnumerator PoolWorksWithoutRoleTemplatesAndResetDropsOldRequestClassification()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity"); yield return new EnterPlayMode(); yield return null;
            var config = Configure(false); Set(config, "Story", null);
            var type = config.GetType().GetField("Requests").FieldType.GetElementType(); Set(config, "Requests", Array.CreateInstance(type, 0)); Call(owner, "ResetSimulation");
            Call(owner, "AddRequest"); string id = Value<string>(owner, "SelectedId"); Assert.That(Call(owner, "IsOperatingTask", id), Is.True);
            Call(owner, "ResetSimulation"); Assert.That(Call(owner, "IsOperatingTask", id), Is.False);
            var sim = Value<TaskSimulation>(owner, "Simulation"); Assert.That(sim.Tasks.Count, Is.Zero); Advance(owner, 30);
            Assert.That(sim.Tasks.Count, Is.EqualTo(5)); Assert.That(sim.Tasks.Values.All(t => (bool)Call(owner, "IsOperatingTask", t.Id)), Is.True);
        }
        [UnityTearDown] public IEnumerator StopPlay()
        {
            if (owner != null && original != null) owner.GetType().GetField("configuration", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(owner, original);
            if (EditorApplication.isPlaying) yield return new ExitPlayMode();
        }
        [TearDown] public void ReleaseResources()
        { foreach (var resource in resources) if (resource != null) Object.DestroyImmediate(resource); resources.Clear(); }
    }
}
