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
    public sealed class RelationshipTests
    {
        private readonly List<ScriptableObject> resources = new List<ScriptableObject>();
        private MonoBehaviour owner;
        private ScriptableObject original;
        private static object Call(object o, string name, params object[] args) => o.GetType().GetMethod(name).Invoke(o, args);
        private static T Value<T>(object o, string name) => (T)o.GetType().GetProperty(name).GetValue(o);
        private static T Field<T>(object o, string name) => (T)o.GetType().GetField(name).GetValue(o);
        private static void Set(object o, string name, object value) => o.GetType().GetField(name).SetValue(o, value);
        private static Type StoryType => Type.GetType("CallmeCatgirl.Gameplay.ConversationStory, Assembly-CSharp", true);
        private ScriptableObject Profiles(params string[] ids)
        {
            var profiles = ids.Select(id => "{\"id\":\"" + id + "\",\"kind\":\"Scripted\",\"name\":\"测试角色" + id + "\",\"sessions\":["
                + "{\"id\":\"s1\",\"title\":\"任务一\",\"requestId\":\"A\",\"rounds\":["
                + "{\"id\":\"r1\",\"message\":\"第一轮\",\"choices\":[{\"label\":\"继续\",\"reply\":\"好\"}]},"
                + "{\"id\":\"r2\",\"message\":\"第二轮\",\"choices\":[{\"label\":\"结束会话\",\"reply\":\"再见\"}]}]},"
                + "{\"id\":\"s2\",\"title\":\"任务二\",\"requestId\":\"A\",\"rounds\":["
                + "{\"id\":\"r1\",\"message\":\"下一任务\",\"choices\":[{\"label\":\"回复\",\"reply\":\"好\"}]}]}]}");
            string json = "{\"version\":1,\"characters\":[" + string.Join(",", profiles) + "]}";
            var importer = Type.GetType("CallmeCatgirl.EditorTools.StoryJsonImporter, Assembly-CSharp-Editor", true);
            var config = (ScriptableObject)importer.GetMethod("Parse").Invoke(null, new object[] { json, new[] { "A", "B", "C" } });
            resources.Add(config); return config;
        }
        private static TaskDefinition Definition => new TaskDefinition("A", "基础任务", new[] { new Cell(0, 0) }, 1, 1, 5, 10,
            new[] { new AbilityRequirement(AbilityKind.Writing, 1, 2) });
        private static TaskDefinition Next(object story, string user = "A") => (TaskDefinition)Call(story, "BuildNextTask", user,
            new Dictionary<string, TaskDefinition> { { "A", Definition } }, Definition);
        private static TaskSimulation Sim(double points) => new TaskSimulation(1, 1, new[] { new Cell(0, 0) },
            new ModelDefinition("model", 1, new Dictionary<AbilityKind, double> { { AbilityKind.Writing, points } }));
        private static TaskInstance Submit(object story, double points, string id = "1", string user = "A")
        {
            var sim = Sim(points); Assert.That(sim.TryCreate(id, user, id, Next(story, user), out var task), Is.True);
            Call(story, "Arrived", task); Assert.That(sim.TryPlace(id, new Cell(0, 0), 0, out _), Is.True);
            sim.Advance(1); Assert.That(sim.TrySubmit(id), Is.True); Call(story, "Submitted", task); return task;
        }
        private static void Reply(object story) { Call(story, "Choose", 0); Call(story, "Choose", 0); }

        [TestCase(0d, -.5d, "任务一")]
        [TestCase(1d, 1d, "任务二")]
        [TestCase(2d, 1d, "任务二")]
        public void AffinitySettlesOnceAndOnlyPassingTasksAdvance(double points, double expected, string title)
        {
            var story = Activator.CreateInstance(StoryType, new object[] { Profiles("A", "B") });
            Assert.That(Call(story, "Affinity", "A"), Is.EqualTo(0));
            var task = Submit(story, points); Call(story, "Submitted", task);
            Assert.That(Call(story, "Affinity", "A"), Is.EqualTo(expected));
            Assert.That(Call(story, "Affinity", "B"), Is.EqualTo(0));
            Call(story, "Choose", 0); Assert.That(Value<bool>(story, "IsReply"), Is.True);
            Assert.That(Call(story, "Affinity", "A"), Is.EqualTo(expected), "Extra dialogue rounds do not settle again.");
            Call(story, "Choose", 0); Assert.That(Next(story).Title, Is.EqualTo(title));
            Call(story, "OpenHistory", "A"); Call(story, "CloseHistory");
            Assert.That(Call(story, "Affinity", "A"), Is.EqualTo(expected));
        }
        [Test] public void FailureCanRetryUntilPassAndFinalFailureDoesNotEndStory()
        {
            var config = Profiles("A"); var story = Activator.CreateInstance(StoryType, new object[] { config });
            Submit(story, 0, "fail"); Reply(story); Assert.That(Next(story).Title, Is.EqualTo("任务一"));
            Submit(story, 1, "pass"); Reply(story); Assert.That(Next(story).Title, Is.EqualTo("任务二"));
            Submit(story, 0, "final-fail"); Reply(story);
            Assert.That(Call(story, "Finished", "A"), Is.False); Assert.That(Next(story).Title, Is.EqualTo("任务二"));
            Submit(story, 1, "final-pass"); Reply(story);
            Assert.That(Call(story, "Finished", "A"), Is.True); Assert.That(Next(story), Is.Null);
            Assert.That(Call(story, "Affinity", "A"), Is.EqualTo(1));
            var restarted = Activator.CreateInstance(StoryType, new object[] { config });
            Assert.That(Call(restarted, "Affinity", "A"), Is.EqualTo(0)); Assert.That(Next(restarted).Title, Is.EqualTo("任务一"));
        }
        [Test] public void TimeoutDeductsOnceKeepsTaskAndReadyDoesNotSettle()
        {
            var story = Activator.CreateInstance(StoryType, new object[] { Profiles("A") }); var sim = Sim(1);
            sim.TryCreate("timeout", "A", "1", Next(story), out var task); Call(story, "Arrived", task);
            sim.Advance(5); Call(story, "TimedOut", task); Call(story, "TimedOut", task);
            Assert.That(Call(story, "Affinity", "A"), Is.EqualTo(-.5)); Assert.That(Next(story).Title, Is.EqualTo("任务一"));
            var readySim = Sim(1); readySim.TryCreate("ready", "A", "2", Next(story), out var ready);
            readySim.TryPlace("ready", new Cell(0, 0), 0, out _); readySim.Advance(1);
            Call(story, "Submitted", ready); Assert.That(Call(story, "Affinity", "A"), Is.EqualTo(-.5));
        }
        [Test] public void CompletedSessionCannotBeIssuedAgainThroughBackwardBranch()
        {
            var config = Profiles("A"); var user = Field<Array>(config, "Users").GetValue(0);
            var sessions = Field<Array>(user, "Sessions"); var first = Field<Array>(sessions.GetValue(0), "Rounds").GetValue(0);
            var last = Field<Array>(sessions.GetValue(1), "Rounds").GetValue(0);
            Set(Field<Array>(last, "Choices").GetValue(0), "NextNodeId", Field<string>(first, "Id"));
            var story = Activator.CreateInstance(StoryType, new object[] { config }); Submit(story, 1); Reply(story);
            Submit(story, 1, "2"); Reply(story); Assert.That(Call(story, "Finished", "A"), Is.True);
            Assert.That(Next(story), Is.Null);
        }
        [Test] public void ConfigurableAffinityValuesAndInvalidNumbersAreHandled()
        {
            var config = Profiles("A"); var story = Activator.CreateInstance(StoryType, new object[] { config, null, null, 4d, 2d, -1.5d });
            Submit(story, 0); Reply(story); Assert.That(Call(story, "Affinity", "A"), Is.EqualTo(2.5));
            Submit(story, 1, "2"); Reply(story); Assert.That(Call(story, "Affinity", "A"), Is.EqualTo(4.5));
            Assert.Throws<TargetInvocationException>(() => Activator.CreateInstance(StoryType, new object[] { config, null, null, double.NaN, 1d, -.5d }));
        }
        private void ConfigureController()
        {
            owner = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Single(o => o.GetType().Name == "CoreGameplayController");
            var field = owner.GetType().GetField("configuration", BindingFlags.Instance | BindingFlags.NonPublic);
            original = (ScriptableObject)field.GetValue(owner); var config = Object.Instantiate(original); resources.Add(config);
            Set(config, "OpeningBalance", null); Set(config, "OrdinaryDialoguePool", null);
            var generation = Object.Instantiate(Field<ScriptableObject>(config, "TaskGeneration")); resources.Add(generation);
            var presetType = generation.GetType().GetNestedType("ShapePreset");
            var preset = Activator.CreateInstance(presetType); Set(preset, "Name", "fixture"); Set(preset, "Pattern", new[] { "#" });
            var shapes = Array.CreateInstance(presetType, 1); shapes.SetValue(preset, 0); Set(generation, "Shapes", shapes);
            var poolType = generation.GetType().GetField("OperatingTasks").FieldType.GetElementType();
            Set(generation, "OperatingTasks", Array.CreateInstance(poolType, 0));
            Set(generation, "RequestsPerRefresh", 5); Set(generation, "MonthsPerRefresh", 1);
            var requests = Field<Array>(config, "Requests"); var one = Array.CreateInstance(requests.GetType().GetElementType(), 1);
            var template = requests.GetValue(0); Set(template, "Calculation", 3f); Set(template, "Speed", 1f); Set(template, "Patience", 15f);
            Set(template, "Minimum", new Vector3(1, 0, 0)); Set(template, "Excellent", new Vector3(2, 0, 0)); one.SetValue(template, 0);
            Set(config, "Requests", one); Set(config, "Story", Profiles("A", "B", "C")); Set(config, "TaskGeneration", generation);
            Set(config, "MonthlyMaintenanceFee", 0); Set(config, "SecondsPerGameMonth", 30f);
            Set(config, "ModelPoints", new Vector4(1, 0, 0, 0)); Set(config, "AbilityInterval", 3f);
            Set(config, "InitialAffinity", 0f); Set(config, "SuccessAffinity", 1f); Set(config, "FailureAffinity", -.5f);
            owner.GetType().GetField("training", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(owner, null);
                config.GetType().GetField("OrdinaryDialoguePool").SetValue(config, null);
                config.GetType().GetField("OpeningBalance").SetValue(config, null);
                field.SetValue(owner, config); Call(owner, "ResetSimulation");
        }
        private static void Place(TaskSimulation sim, string id)
        {
            for (int rotation = 0; rotation < 4; rotation++)
                for (int i = 0; i < sim.Width * sim.Height; i++)
                    if (sim.TryPlace(id, new Cell(i % sim.Width, i / sim.Width), rotation, out _)) return;
            Assert.Fail("Cannot place fixture task.");
        }
        private static void Capture(string name) => typeof(SystemFeedbackTests).GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { name });
        [UnityTest] public IEnumerator MonthlyRefreshAdvancesPassedCharacterAndRetriesTimeoutWithoutDuplicates()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity"); yield return new EnterPlayMode(); yield return null;
            ConfigureController(); var sim = Value<TaskSimulation>(owner, "Simulation"); var story = Value<object>(owner, "Story");
            Place(sim, "A-1"); sim.Paused = false; Call(owner, "AdvanceGame", 3d); sim.Paused = true;
            Assert.That(Call(story, "Affinity", "A"), Is.EqualTo(0));
            Call(owner, "SubmitSelected"); Assert.That(Call(story, "Affinity", "A"), Is.EqualTo(1));
            Assert.That(GameObject.Find("UserName").GetComponent<Text>().text, Does.Contain("亲密度 1"));
            yield return null; Capture("AffinityPopup.png");
            Call(owner, "ChooseReply", 0); Call(owner, "ChooseReply", 0);
            sim.Paused = false; Call(owner, "AdvanceGame", 27d); sim.Paused = true;
            var batch = sim.Tasks.Values.Where(t => t.ArrivedAt == 30).ToArray(); Assert.That(batch.Length, Is.EqualTo(3));
            Assert.That(batch.Single(t => t.ConversationId == "A").Definition.Title, Is.EqualTo("任务二"));
            Assert.That(batch.Where(t => t.ConversationId != "A").All(t => t.Definition.Title == "任务一"), Is.True);
            Call(owner, "SelectConversation", "A"); Capture("AffinityMain.png");
            sim.Paused = false; Call(owner, "AdvanceGame", 30d); sim.Paused = true;
            Assert.That(Call(story, "Affinity", "A"), Is.EqualTo(.5)); Assert.That(Call(story, "Affinity", "B"), Is.EqualTo(-.5));
            var retries = sim.Tasks.Values.Where(t => t.ArrivedAt == 60).ToArray(); Assert.That(retries.Length, Is.EqualTo(3));
            Assert.That(retries.Single(t => t.ConversationId == "A").Definition.Title, Is.EqualTo("任务二"));
            foreach (var group in sim.Tasks.Values.Where(t => t.State == RequestState.Waiting).GroupBy(t => t.ConversationId)) Assert.That(group.Count(), Is.EqualTo(1));
            Call(owner, "ResetSimulation"); Assert.That(Call(Value<object>(owner, "Story"), "Affinity", "A"), Is.EqualTo(0));
        }
        [UnityTest] public IEnumerator MonthlyQuotaPreservesBusyTasksAndPrioritizesWaitingCharacters()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity"); yield return new EnterPlayMode(); yield return null;
            ConfigureController(); var config = owner.GetType().GetField("configuration", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
            Set(Field<object>(config, "TaskGeneration"), "RequestsPerRefresh", 1);
            Set(Field<Array>(config, "Requests").GetValue(0), "Patience", 200f); Call(owner, "ResetSimulation");
            var sim = Value<TaskSimulation>(owner, "Simulation"); var existing = sim.Tasks["A-1"];
            sim.Paused = false; Call(owner, "AdvanceGame", 90d); sim.Paused = true;
            Assert.That(sim.Tasks.Count, Is.EqualTo(3)); Assert.That(sim.Tasks["A-1"], Is.SameAs(existing));
            Assert.That(sim.Tasks.Values.Count(t => t.ConversationId == "B"), Is.EqualTo(1));
            Assert.That(sim.Tasks.Values.Count(t => t.ConversationId == "C"), Is.EqualTo(1));
            Assert.That(existing.State, Is.EqualTo(RequestState.Waiting));
        }
        [UnityTearDown] public IEnumerator CleanupPlay()
        {
            if (owner != null && original != null) owner.GetType().GetField("configuration", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(owner, original);
            if (EditorApplication.isPlaying) yield return new ExitPlayMode();
        }
        [TearDown] public void CleanupAssets()
        {
            foreach (var resource in resources) if (resource != null) Object.DestroyImmediate(resource);
            resources.Clear();
        }
    }
}
