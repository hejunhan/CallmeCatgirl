using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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
    public sealed class StoryImportTests
    {
        private readonly List<ScriptableObject> resources = new List<ScriptableObject>();
        private static readonly string[] Ids = { "A", "B", "C" };
        private static Type Importer => Type.GetType("CallmeCatgirl.EditorTools.StoryJsonImporter, Assembly-CSharp-Editor", true);
        private static object Call(object o, string name, params object[] args) => o.GetType().GetMethod(name).Invoke(o, args);
        private static T Field<T>(object o, string name) => (T)o.GetType().GetField(name).GetValue(o);
        private static T Value<T>(object o, string name) => (T)o.GetType().GetProperty(name).GetValue(o);
        private static string Example(string file) => File.ReadAllText(Path.Combine("Examples/StoryImport", file));
        private ScriptableObject Parse(string json)
        {
            var result = (ScriptableObject)Importer.GetMethod("Parse").Invoke(null, new object[] { json, Ids });
            resources.Add(result); return result;
        }
        private static void Apply(string json, ScriptableObject target, bool all = false)
            => Importer.GetMethod("Apply").Invoke(null, new object[] { json, target, Ids, all });
        private static Array Users(object config) => Field<Array>(config, "Users");
        private static Array Sessions(object user) => Field<Array>(user, "Sessions");
        private static Array Rounds(object session) => Field<Array>(session, "Rounds");
        private static Array Choices(object node) => Field<Array>(node, "Choices");
        private static object Story(object config) => Activator.CreateInstance(Type.GetType("CallmeCatgirl.Gameplay.ConversationStory, Assembly-CSharp", true), config);
        private static TaskDefinition Template(string id = "A") => new TaskDefinition(id, "template", new[] { new Cell(0, 0) }, 100, 10, 90, 100,
            new[] { new AbilityRequirement(AbilityKind.Writing, 10, 16), new AbilityRequirement(AbilityKind.Code, 2, 4) });
        private static TaskDefinition Next(object story, string user = "A") => (TaskDefinition)Call(story, "BuildNextTask", user,
            new Dictionary<string, TaskDefinition> { { "A", Template() }, { "B", Template("B") }, { "C", Template("C") } }, Template());
        private static TaskSimulation Simulation() => new TaskSimulation(3, 3, Enumerable.Range(0, 9).Select(i => new Cell(i % 3, i / 3)),
            new ModelDefinition("model", 3, new Dictionary<AbilityKind, double> { { AbilityKind.Writing, 4 }, { AbilityKind.Code, 1 }, { AbilityKind.Reasoning, 1 } }));
        private static TaskInstance Submit(TaskSimulation sim, object story, string id, string user = "A")
        {
            var def = Next(story, user);
            Assert.That(sim.TryCreate(id, user, id, def, out var task), Is.True);
            Call(story, "Arrived", task);
            Assert.That(sim.TryPlace(id, new Cell(0, 0), 0, out _), Is.True);
            sim.Advance(def.Calculation / def.Speed);
            Assert.That(sim.TrySubmit(id), Is.True);
            Call(story, "Submitted", task); return task;
        }

        [Test] public void FullExampleKeepsIndependentCountsAndCompilesSequentialTargets()
        {
            var cfg = Parse("\uFEFF" + Example("story-10-sessions.example.json"));
            var expected = new[] { 31, 40, 26 };
            for (int i = 0; i < 3; i++)
            {
                var sessions = Sessions(Users(cfg).GetValue(i));
                Assert.That(sessions.Length, Is.EqualTo(10));
                Assert.That(sessions.Cast<object>().Sum(s => Rounds(s).Length), Is.EqualTo(expected[i]));
                Assert.That(Rounds(sessions.GetValue(9)).Length, Is.GreaterThan(Rounds(sessions.GetValue(0)).Length));
            }
            var a = Users(cfg).GetValue(0);
            var first = Rounds(Sessions(a).GetValue(0)).GetValue(0);
            Assert.That(Field<string>(Choices(first).GetValue(0), "NextNodeId"), Is.EqualTo("s02/r1"));
            var last = Rounds(Sessions(a).GetValue(9)).Cast<object>().Last();
            Assert.That(Field<string>(Choices(last).GetValue(0), "NextNodeId"), Is.Null.Or.Empty);
        }

        [Test] public void SingleCharacterImportPreservesOthersAndReplaceAllIsExplicit()
        {
            var cfg = Parse(Example("story-10-sessions.example.json"));
            var b = Users(cfg).GetValue(1); var c = Users(cfg).GetValue(2);
            Apply(Example("minimal.example.json"), cfg);
            Assert.That(Sessions(Users(cfg).GetValue(0)).Length, Is.EqualTo(2));
            Assert.That(Users(cfg).GetValue(1), Is.SameAs(b));
            Assert.That(Users(cfg).GetValue(2), Is.SameAs(c));
            Apply(Example("minimal.example.json"), cfg, true);
            Assert.That(Users(cfg).Length, Is.EqualTo(1));
        }

        [TestCase("\"version\": 1", "\"version\": 2")]
        [TestCase("\"id\": \"A\"", "\"id\": \"D\"")]
        [TestCase("\"requestId\": \"A\"", "\"requestId\": \"missing\"")]
        [TestCase("\"id\": \"s2\"", "\"id\": \"s1\"")]
        [TestCase("\"id\": \"r2\"", "\"id\": \"r1\"")]
        [TestCase("\"nextRoundId\": \"END\"", "\"nextRoundId\": \"missing\"")]
        [TestCase("\"nextRoundId\": \"END\"", "\"nextRoundId\": \"s1/r1\"")]
        [TestCase("\"requirement\": \"Any\"", "\"requirement\": \"bogus\"")]
        [TestCase("\"kind\": \"Code\"", "\"kind\": \"Writing\"")]
        [TestCase("\"excellent\": 8", "\"excellent\": 0")]
        public void InvalidImportLeavesOriginalUntouched(string from, string to)
        {
            var cfg = Parse(Example("story-10-sessions.example.json")); var original = Users(cfg);
            Assert.Throws<TargetInvocationException>(() => Apply(Example("minimal.example.json").Replace(from, to), cfg));
            Assert.That(Users(cfg), Is.SameAs(original));
        }

        [Test] public void OncePerSessionMultipleChoicesRetainRequestQualityAndHistory()
        {
            var story = Story(Parse(Example("minimal.example.json"))); var sim = Simulation();
            var first = Submit(sim, story, "A1");
            Assert.That(first.Definition.Calculation, Is.EqualTo(30));
            Assert.That(first.Result.Overall, Is.EqualTo(Quality.Complete));
            Assert.That((bool)Call(story, "Choose", 0), Is.True);
            Assert.That(Value<bool>(story, "IsOpen"), Is.False);
            Assert.That(sim.TryCloseFeedback(first.Id), Is.True);
            var second = Submit(sim, story, "A2");
            Assert.That(second.Definition.Calculation, Is.EqualTo(90));
            Assert.That(second.GetAbility(AbilityKind.Writing), Is.EqualTo(12));
            var quality = second.Result.Overall; var time = sim.GameTime;
            Call(story, "Choose", 0);
            Assert.That(Value<bool>(story, "IsReply"), Is.True);
            Assert.That(Value<string>(story, "RequestId"), Is.EqualTo("A2"));
            Assert.That(Value<Quality>(story, "Result"), Is.EqualTo(quality));
            Assert.That(Call(story, "Progress", "A"), Does.Contain("第2/3轮"));
            Call(story, "Choose", 0); Call(story, "Choose", 0);
            Assert.That(Value<bool>(story, "IsOpen"), Is.False);
            Assert.That(Call(story, "Finished", "A"), Is.True);
            Assert.That(Call(story, "History", "A"), Does.Contain("一步一步来"));
            Assert.That(sim.GameTime, Is.EqualTo(time)); Assert.That(sim.Tasks.Count, Is.EqualTo(2));
            Assert.That(sim.DrainEvents().Count(e => e.Kind == TaskEventKind.Submitted), Is.EqualTo(2));
        }

        [Test] public void EndBranchSkipsRemainingRoundsAndFinishesOnlyCurrentSession()
        {
            var story = Story(Parse(Example("minimal.example.json"))); var sim = Simulation();
            var task = Submit(sim, story, "A1"); Call(story, "Choose", 0); sim.TryCloseFeedback(task.Id);
            Submit(sim, story, "A2"); Call(story, "Choose", 1);
            Assert.That(Call(story, "Finished", "A"), Is.True);
            Assert.That(Call(story, "History", "A"), Does.Not.Contain("三件事"));
        }

        [Test] public void UnavailableChoicesFallbackAdvancesAndExplicitJumpStaysInSession()
        {
            var json = Example("minimal.example.json").Replace("\"requirement\": \"Any\"", "\"requirement\": \"ExcellentOnly\"")
                .Replace("\"requirement\": \"CompleteOrBetter\"", "\"requirement\": \"ExcellentOnly\"");
            var story = Story(Parse(json)); var sim = Simulation();
            var task = Submit(sim, story, "A1"); Call(story, "Choose", 0); sim.TryCloseFeedback(task.Id);
            Submit(sim, story, "A2"); Call(story, "Choose", 0);
            Assert.That(Value<bool>(story, "IsOpen"), Is.True);
            Assert.That(Call(story, "Progress", "A"), Does.Contain("第2/3轮"));
            Call(story, "Choose", 0); Call(story, "Choose", 0);
            Assert.That(Call(story, "Finished", "A"), Is.True);
            var cfg = Parse(Example("minimal.example.json").Replace("\"nextRoundId\": \"END\"", "\"nextRoundId\": \"r3\""));
            var node = Rounds(Sessions(Users(cfg).GetValue(0)).GetValue(1)).GetValue(0);
            Assert.That(Field<string>(Choices(node).GetValue(1), "NextNodeId"), Is.EqualTo("s2/r3"));
        }

        [Test] public void QueueWaitsForWholeSessionAndProgressIsIndependentPerCharacter()
        {
            var cfg = Parse(Example("story-10-sessions.example.json")); Apply(Example("minimal.example.json"), cfg);
            var story = Story(cfg); var sim = Simulation();
            var first = Submit(sim, story, "A1"); Call(story, "Choose", 0); sim.TryCloseFeedback(first.Id);
            Submit(sim, story, "A2"); Submit(sim, story, "B1", "B");
            Call(story, "Choose", 0); Call(story, "Choose", 0);
            Assert.That(Value<string>(story, "UserId"), Is.EqualTo("A"));
            Call(story, "Choose", 0);
            Assert.That(Value<string>(story, "UserId"), Is.EqualTo("B"));
            Assert.That(Value<bool>(story, "IsReply"), Is.True);
            Assert.That(Call(story, "Progress", "B"), Does.Contain("第1/10次"));
            Assert.That(Call(story, "History", "B"), Does.Not.Contain("明天有好多事情"));
        }

        [UnityTest] public IEnumerator ScenePopupContinuesRoundsAndSettlesOnlyOnceThenDisablesNextTask()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity");
            yield return new EnterPlayMode(); yield return null;
            var owner = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                .Single(c => c.GetType().FullName == "CallmeCatgirl.Gameplay.CoreGameplayController");
            var field = owner.GetType().GetField("configuration", BindingFlags.Instance | BindingFlags.NonPublic);
            var original = (ScriptableObject)field.GetValue(owner); var cfg = Object.Instantiate(original);
            resources.Add(cfg);
            try
            {
                cfg.GetType().GetField("ModelPoints").SetValue(cfg, new Vector4(4, 1, 1, 0));
                cfg.GetType().GetField("AbilityInterval").SetValue(cfg, 3f);
                cfg.GetType().GetField("TaskGeneration").SetValue(cfg, null);
                cfg.GetType().GetField("Story").SetValue(cfg, Parse(Example("minimal.example.json")));
                field.SetValue(owner, cfg); Call(owner, "ResetSimulation");
                var sim = Value<TaskSimulation>(owner, "Simulation");
                Complete(owner, sim, "A-1"); Call(owner, "SubmitSelected"); Call(owner, "ChooseReply", 0);
                Call(owner, "NextRound"); string id = Value<string>(owner, "SelectedId");
                Assert.That(sim.Tasks[id].Definition.Calculation, Is.EqualTo(90));
                Complete(owner, sim, id); int funds = Value<int>(owner, "Funds"); int complete = Value<int>(owner, "CompletedTasks");
                Call(owner, "SubmitSelected"); yield return null;
                Assert.That(Value<int>(owner, "Funds"), Is.EqualTo(funds + 100));
                Assert.That(Value<int>(owner, "CompletedTasks"), Is.EqualTo(complete + 1));
                int settledFunds = Value<int>(owner, "Funds"); double time = sim.GameTime;
                var button = GameObject.Find("ReplyChoice_0").GetComponent<Button>(); button.onClick.Invoke(); yield return null;
                Assert.That(Value<bool>(owner, "IsStoryOpen"), Is.True);
                Assert.That(GameObject.Find("Result").GetComponent<Text>().text, Does.Contain("第2/3轮"));
                Assert.That(GameObject.Find("CloseHistory"), Is.Null);
                Call(owner, "AdvanceGame", 100d); Call(owner, "NextRound"); Call(owner, "SubmitSelected");
                Assert.That(sim.GameTime, Is.EqualTo(time)); Assert.That(sim.Tasks.Count, Is.EqualTo(4));
                GameObject.Find("ReplyChoice_0").GetComponent<Button>().onClick.Invoke(); yield return null;
                GameObject.Find("ReplyChoice_0").GetComponent<Button>().onClick.Invoke(); yield return null;
                Assert.That(Value<bool>(owner, "IsStoryOpen"), Is.False);
                Assert.That(sim.Tasks[id].State, Is.EqualTo(RequestState.Closed));
                Call(owner, "NextRound"); Assert.That(sim.Tasks.Count, Is.EqualTo(4));
                Assert.That(Value<int>(owner, "Funds"), Is.EqualTo(settledFunds));
                Assert.That(Value<int>(owner, "CompletedTasks"), Is.EqualTo(complete + 1));
                Call(owner, "ResetSimulation");
                Assert.That(Call(Value<object>(owner, "Story"), "Finished", "A"), Is.False);
                Assert.That(Value<TaskSimulation>(owner, "Simulation").Tasks["A-1"].Definition.Calculation, Is.EqualTo(30));
            }
            finally { field.SetValue(owner, original); }
            yield return new ExitPlayMode();
        }
        private static void Complete(MonoBehaviour owner, TaskSimulation sim, string id)
        {
            var cell = Enumerable.Range(0, sim.Width * sim.Height).Select(i => new Cell(i % sim.Width, i / sim.Width))
                .First(c => sim.Preview(id, c, 0) == PlacementFailure.None);
            Assert.That(sim.TryPlace(id, cell, 0, out _), Is.True);
            sim.Paused = false; sim.Advance(sim.Tasks[id].Definition.Calculation / sim.Tasks[id].Definition.Speed); sim.Paused = true;
            Call(owner, "SelectRequest", id);
        }
        [TearDown] public void DestroyResources()
        { foreach (var resource in resources) if (resource != null) Object.DestroyImmediate(resource); resources.Clear(); }
        [UnityTearDown] public IEnumerator ExitIfNeeded() { if (EditorApplication.isPlaying) yield return new ExitPlayMode(); }
    }
}
