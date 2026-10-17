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
    public sealed class StoryTextImportTests
    {
        private readonly List<ScriptableObject> resources = new List<ScriptableObject>();
        private static readonly string[] Ids = { "A", "B", "C" };
        private static Type Importer => Type.GetType("CallmeCatgirl.EditorTools.StoryTextImporter, Assembly-CSharp-Editor", true);
        private static string Sample => File.ReadAllText("Examples/StoryText/快速验证_林小满.txt").Replace("\r\n", "\n");
        private static object Call(object o, string name, params object[] args) => o.GetType().GetMethod(name).Invoke(o, args);
        private static T Field<T>(object o, string name) => (T)o.GetType().GetField(name).GetValue(o);
        private static T Value<T>(object o, string name) => (T)o.GetType().GetProperty(name).GetValue(o);
        private ScriptableObject Parse(string text)
        {
            var config = (ScriptableObject)Importer.GetMethod("Parse").Invoke(null, new object[] { text, Ids, null });
            resources.Add(config); return config;
        }
        private static Array Users(object config) => Field<Array>(config, "Users");
        private static Array Sessions(object user) => Field<Array>(user, "Sessions");
        private static Array Rounds(object session) => Field<Array>(session, "Rounds");
        private static Array Choices(object node) => Field<Array>(node, "Choices");
        private static object Story(object config) => Activator.CreateInstance(Type.GetType("CallmeCatgirl.Gameplay.ConversationStory, Assembly-CSharp", true), config);
        private static TaskDefinition Template(string id = "A") => new TaskDefinition(id, "template", new[] { new Cell(0, 0) }, 100, 10, 90, 7,
            new[] { new AbilityRequirement(AbilityKind.Writing, 10, 16) });
        private static TaskDefinition Next(object story, TaskDefinition basis = null) => (TaskDefinition)Call(story, "BuildNextTask", "A",
            new Dictionary<string, TaskDefinition> { { "A", basis ?? Template() } }, basis ?? Template());
        private static TaskSimulation Simulation() => new TaskSimulation(8, 8, Enumerable.Range(0, 64).Select(i => new Cell(i % 8, i / 8)),
            new ModelDefinition("model", 3, new Dictionary<AbilityKind, double> { { AbilityKind.Writing, 4 }, { AbilityKind.Code, 1 }, { AbilityKind.Reasoning, 1 } }));
        private static TaskInstance Submit(TaskSimulation sim, object story, string id)
        {
            var def = Next(story); Assert.That(sim.TryCreate(id, "A", id, def, out var task), Is.True);
            Call(story, "Arrived", task); Assert.That(sim.TryPlace(id, new Cell(0, 0), 0, out _), Is.True);
            sim.Advance(def.Calculation / def.Speed); Assert.That(sim.TrySubmit(id), Is.True); Call(story, "Submitted", task); return task;
        }

        [Test] public void ParsesIndependentSessionsShapesThresholdsAndMultilineDialogue()
        {
            var cfg = Parse("\uFEFF" + Sample.Replace("\n", "\r\n")); var user = Users(cfg).GetValue(0); var sessions = Sessions(user);
            Assert.That(sessions.Length, Is.EqualTo(2)); Assert.That(Rounds(sessions.GetValue(1)).Length, Is.EqualTo(3));
            Assert.That(Choices(Rounds(sessions.GetValue(1)).GetValue(0)).Length, Is.EqualTo(3));
            Assert.That(Field<string>(user, "InitialHistory"), Does.Contain("\n我：我在"));
            Assert.That(Field<string>(Rounds(sessions.GetValue(1)).GetValue(1), "UserMessage"), Does.Contain("\n你能帮我"));
            var story = Story(cfg); var basis = Template(); var task = Next(story, basis);
            Assert.That(task.Shape, Is.EquivalentTo(new[] { new Cell(0, 0), new Cell(1, 0), new Cell(0, 1), new Cell(1, 1) }));
            Assert.That(task.Tokens, Is.EqualTo(100)); Assert.That(basis.Tokens, Is.EqualTo(7)); Assert.That(basis.Shape.Count, Is.EqualTo(1));
            Assert.That(task.Requirements.Select(r => r.Minimum), Is.EqualTo(new[] { 4d, 1d, 1d }));
        }

        [Test] public void PlainDialogueWithoutNumbersUsesTemplateAndKnownNameCanOmitId()
        {
            var cfg = Parse("角色：林小满\n【会话：问候】\n【第1轮】\n用户：你好。\n选项1：你好呀。");
            var def = Next(Story(cfg)); Assert.That(def.Calculation, Is.EqualTo(100)); Assert.That(def.Speed, Is.EqualTo(10));
            Assert.That(def.Tokens, Is.EqualTo(7)); Assert.That(def.Shape.Count, Is.EqualTo(1));
            var choice = Choices(Rounds(Sessions(Users(cfg).GetValue(0)).GetValue(0)).GetValue(0)).GetValue(0);
            Assert.That(Field<string>(choice, "PlayerReply"), Is.EqualTo("你好呀。"));
        }

        [Test] public void ObsoleteStoryFieldsAreRemovedAndFirstRoundIsDerivedFromSessions()
        {
            var cfg = Parse(Sample); var user = Users(cfg).GetValue(0);
            Assert.That(user.GetType().GetField("Nodes"), Is.Null);
            Assert.That(user.GetType().GetField("StartNodeId"), Is.Null);
            var first = Rounds(Sessions(user).GetValue(0)).GetValue(0);
            Assert.That(Value<string>(user, "FirstNodeId"), Is.EqualTo(Field<string>(first, "Id")));
            Assert.That(Choices(first).GetValue(0).GetType().GetField("NextRequestId"), Is.Null);
        }

        [Test] public void ExplicitZeroTokenIncomeDoesNotFallBackToTemplate()
        {
            var cfg = Parse(Sample.Replace("Token收入：100", "Token收入：0"));
            Assert.That(Next(Story(cfg)).Tokens, Is.EqualTo(0));
        }

        [Test] public void NamedRoundAndAsciiPatternWorkAndResolveLocalJump()
        {
            var cfg = Parse(Sample.Replace("■■\n■■", "##\n##").Replace("【第3轮】", "【轮次：安慰】").Replace("跳转到：第3轮", "跳转到：安慰"));
            var rounds = Rounds(Sessions(Users(cfg).GetValue(0)).GetValue(1));
            Assert.That(Field<string>(Choices(rounds.GetValue(0)).GetValue(2), "NextNodeId"), Is.EqualTo("s2/安慰"));
        }

        [Test] public void SessionOverrideChangesShapeSpeedTokensAndDoesNotLeakToLaterSession()
        {
            var cfg = Parse(Sample + "\n【会话：下次问候】\n【第1轮】\n用户：你好。\n选项1：你好呀。\n");
            var story = Story(cfg); var sim = Simulation(); var first = Submit(sim, story, "one"); Call(story, "Choose", 0); sim.TryCloseFeedback(first.Id);
            var second = Next(story); Assert.That(second.Speed, Is.EqualTo(15)); Assert.That(second.Tokens, Is.EqualTo(120));
            Assert.That(second.Calculation, Is.EqualTo(90)); Assert.That(second.Requirements.First().Minimum, Is.EqualTo(8));
            Assert.That(second.Shape, Is.EquivalentTo(new[] { new Cell(0, 0), new Cell(0, 1), new Cell(0, 2), new Cell(1, 2) }));
            Assert.That(second.RotatedShape(1), Is.EquivalentTo(new[] { new Cell(0, 0), new Cell(1, 0), new Cell(2, 0), new Cell(0, 1) }));
            var task = Submit(sim, story, "two"); Assert.That(task.Result.Overall, Is.EqualTo(Quality.Complete));
            Call(story, "Choose", 1); sim.TryCloseFeedback(task.Id);
            var third = Next(story); Assert.That(third.Speed, Is.EqualTo(10)); Assert.That(third.Tokens, Is.EqualTo(100));
            Assert.That(third.Calculation, Is.EqualTo(30)); Assert.That(third.Shape, Is.EquivalentTo(first.Definition.Shape));
        }

        [Test] public void OneCharacterTextApplyKeepsOtherCharacters()
        {
            var jsonImporter = Type.GetType("CallmeCatgirl.EditorTools.StoryJsonImporter, Assembly-CSharp-Editor", true);
            var cfg = (ScriptableObject)jsonImporter.GetMethod("Parse").Invoke(null, new object[] { File.ReadAllText("Examples/StoryImport/story-10-sessions.example.json"), Ids });
            resources.Add(cfg); var b = Users(cfg).GetValue(1); var c = Users(cfg).GetValue(2);
            Importer.GetMethod("Apply").Invoke(null, new object[] { Sample, cfg, Ids, null });
            Assert.That(Sessions(Users(cfg).GetValue(0)).Length, Is.EqualTo(2));
            Assert.That(Users(cfg).GetValue(1), Is.SameAs(b)); Assert.That(Users(cfg).GetValue(2), Is.SameAs(c));
        }

        [TestCase("计算量：30", "计算量：-1")]
        [TestCase("计算量：30", "计算量：NaN")]
        [TestCase("计算速度：10", "计算速度：Infinity")]
        [TestCase("Token收入：100", "Token收入：-1")]
        [TestCase("角色ID：A", "角色ID：missing")]
        [TestCase("条件：优秀", "条件：完美")]
        [TestCase("优秀能力：写作8，代码2，推理2", "优秀能力：写作4，代码2，推理2")]
        [TestCase("优秀能力：写作8，代码2，推理2", "优秀能力：写作8，推理2")]
        [TestCase("优秀能力：写作8，代码2，推理2\n", "")]
        [TestCase("■■\n■■", "□□\n□□")]
        [TestCase("■■\n■■", "■■\n■")]
        [TestCase("■■\n■■", "■□\n□■")]
        [TestCase("跳转到：第3轮", "跳转到：第9轮")]
        [TestCase("【第2轮】", "【第1轮】")]
        [TestCase("【第2轮】", "【不存在】")]
        public void InvalidTextNeverPartiallyReplacesTarget(string from, string to)
        {
            var cfg = Parse(Sample); var users = Users(cfg);
            Assert.Throws<TargetInvocationException>(() => Importer.GetMethod("Apply").Invoke(null, new object[] { Sample.Replace(from, to), cfg, Ids, null }));
            Assert.That(Users(cfg), Is.SameAs(users));
        }

        [Test] public void FullRoleTextExamplesHaveDifferentGrowingRoundCounts()
        {
            int index = 0;
            foreach (var file in new[] { "A_林小满_10次会话.txt", "B_周序_10次会话.txt", "C_苏禾_10次会话.txt" })
            {
                var cfg = Parse(File.ReadAllText(Path.Combine("Examples/StoryText", file))); var sessions = Sessions(Users(cfg).GetValue(0));
                Assert.That(sessions.Length, Is.EqualTo(10));
                Assert.That(sessions.Cast<object>().Sum(s => Rounds(s).Length), Is.EqualTo(new[] { 31, 40, 26 }[index++]));
            }
        }

        [Test] public void ExistingAssetUsesItsDeclaredShapeSpeedAndTokens()
        {
            var cfg = AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/_Game/Data/TaskGrid/CoreGameplayConfiguration.asset");
            var story = Story(Field<object>(cfg, "Story")); var request = Field<Array>(cfg, "Requests").GetValue(0);
            var basis = (TaskDefinition)Call(request, "Build"); var def = Next(story, basis);
            var profile = Users(Field<object>(cfg, "Story")).Cast<object>().FirstOrDefault(u => Field<string>(u, "UserId") == basis.Id);
            var difficulty = profile == null ? null : Field<object>(Sessions(profile).GetValue(0), "Task");
            var pattern = difficulty == null ? null : Field<string[]>(difficulty, "Shape");
            var expectedShape = pattern != null && pattern.Length > 0 ? (Cell[])Type.GetType("CallmeCatgirl.Gameplay.ShapePattern, Assembly-CSharp", true).GetMethod("Parse").Invoke(null, new object[] { pattern }) : basis.Shape;
            float speed = difficulty == null ? 0 : Field<float>(difficulty, "Speed");
            float tokens = difficulty == null ? -1 : Field<float>(difficulty, "Tokens");
            Assert.That(def.Shape, Is.EquivalentTo(expectedShape)); Assert.That(def.Speed, Is.EqualTo(speed > 0 ? speed : basis.Speed)); Assert.That(def.Tokens, Is.EqualTo(tokens >= 0 ? tokens : basis.Tokens));
        }

        [Test] public void EditorFileImportChecksBoardSizeBeforeReplacingAnything()
        {
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, Sample);
                var editor = Type.GetType("StoryConfigurationEditor, Assembly-CSharp-Editor", true);
                var method = editor.GetMethod("ParseFile", BindingFlags.Static | BindingFlags.NonPublic);
                var cfg = (ScriptableObject)method.Invoke(null, new object[] { path, true }); resources.Add(cfg);
                Assert.That(Sessions(Users(cfg).GetValue(0)).Length, Is.EqualTo(2));
                File.WriteAllText(path, Sample.Replace("■■\n■■", new string('■', 9) + "\n" + new string('■', 9)));
                var error = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, new object[] { path, true }));
                Assert.That(error.InnerException.Message, Does.Contain("超过棋盘"));
            }
            finally { File.Delete(path); }
        }

        [Test] public void JsonImportCanUseTheSameShapePatternWithoutChangingItsOldDefaults()
        {
            var jsonImporter = Type.GetType("CallmeCatgirl.EditorTools.StoryJsonImporter, Assembly-CSharp-Editor", true);
            var json = File.ReadAllText("Examples/StoryImport/minimal.example.json").Replace("\"calculation\": 30", "\"calculation\": 30, \"shape\": [\"#.\", \"#.\", \"##\"]");
            var cfg = (ScriptableObject)jsonImporter.GetMethod("Parse").Invoke(null, new object[] { json, Ids }); resources.Add(cfg);
            var task = Next(Story(cfg)); Assert.That(task.Shape.Count, Is.EqualTo(4));
            Assert.That(task.Shape, Does.Contain(new Cell(1, 2))); Assert.That(task.Tokens, Is.EqualTo(7));
        }

        [UnityTest] public IEnumerator TextShapeIsPlacedAndRotatedAndMultiRoundSessionIsSettledOnce()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity"); yield return new EnterPlayMode(); yield return null;
            var owner = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Single(c => c.GetType().FullName == "CallmeCatgirl.Gameplay.CoreGameplayController");
            var field = owner.GetType().GetField("configuration", BindingFlags.Instance | BindingFlags.NonPublic);
            var original = (ScriptableObject)field.GetValue(owner); var cfg = Object.Instantiate(original); resources.Add(cfg);
            try
            {
                cfg.GetType().GetField("ModelPoints").SetValue(cfg, new Vector4(4, 1, 1, 0));
                cfg.GetType().GetField("AbilityInterval").SetValue(cfg, 3f);
                cfg.GetType().GetField("TaskGeneration").SetValue(cfg, null);
                cfg.GetType().GetField("Story").SetValue(cfg, Parse(Sample)); field.SetValue(owner, cfg); Call(owner, "ResetSimulation");
                var sim = Value<TaskSimulation>(owner, "Simulation");
                Assert.That(sim.Tasks["A-1"].Definition.Shape.Count, Is.EqualTo(4));
                Complete(owner, sim, "A-1"); Call(owner, "SubmitSelected"); Call(owner, "ChooseReply", 0); Call(owner, "NextRound");
                string id = Value<string>(owner, "SelectedId"); var task = sim.Tasks[id];
                Assert.That(task.Definition.Speed, Is.EqualTo(15)); Assert.That(task.Definition.Tokens, Is.EqualTo(120));
                Assert.That(task.Definition.RotatedShape(1), Is.EquivalentTo(new[] { new Cell(0, 0), new Cell(1, 0), new Cell(2, 0), new Cell(0, 1) }));
                Complete(owner, sim, id); int funds = Value<int>(owner, "Funds"), complete = Value<int>(owner, "CompletedTasks");
                sim.Paused = false; Call(owner, "SubmitSelected"); yield return null;
                Assert.That(Value<int>(owner, "Funds"), Is.EqualTo(funds + 120));
                var button = GameObject.Find("ReplyChoice_0").GetComponent<Button>(); button.onClick.Invoke(); yield return null;
                Assert.That(Value<bool>(owner, "IsStoryOpen"), Is.True);
                Assert.That(GameObject.Find("Result").GetComponent<Text>().text, Does.Contain("第2/3轮"));
                Assert.That(GameObject.Find("HistoryScroll").GetComponentInChildren<Text>().text, Does.Contain("我怕做不完"));
                double time = sim.GameTime; Call(owner, "AdvanceGame", 50d); Assert.That(sim.GameTime, Is.EqualTo(time));
                GameObject.Find("ReplyChoice_0").GetComponent<Button>().onClick.Invoke(); yield return null;
                sim.Paused = true; GameObject.Find("ReplyChoice_0").GetComponent<Button>().onClick.Invoke(); yield return null;
                Assert.That(Value<bool>(owner, "IsStoryOpen"), Is.False); Assert.That(task.State, Is.EqualTo(RequestState.Closed));
                Assert.That(Value<int>(owner, "Funds"), Is.EqualTo(funds + 120));
                Assert.That(Value<int>(owner, "CompletedTasks"), Is.EqualTo(complete + 1));
                int count = sim.Tasks.Count; Call(owner, "NextRound"); Assert.That(sim.Tasks.Count, Is.EqualTo(count));
            }
            finally { field.SetValue(owner, original); }
            yield return new ExitPlayMode();
        }
        private static void Complete(MonoBehaviour owner, TaskSimulation sim, string id)
        {
            var cell = Enumerable.Range(0, sim.Width * sim.Height).Select(i => new Cell(i % sim.Width, i / sim.Width)).First(c => sim.Preview(id, c, 0) == PlacementFailure.None);
            Assert.That(sim.TryPlace(id, cell, 0, out _), Is.True); Assert.That(sim.Occupancy.Count, Is.EqualTo(sim.Tasks[id].Definition.Shape.Count));
            sim.Paused = false; sim.Advance(sim.Tasks[id].Definition.Calculation / sim.Tasks[id].Definition.Speed); sim.Paused = true; Call(owner, "SelectRequest", id);
        }
        [TearDown] public void DestroyResources() { foreach (var resource in resources) if (resource != null) Object.DestroyImmediate(resource); resources.Clear(); }
        [UnityTearDown] public IEnumerator ExitIfNeeded() { if (EditorApplication.isPlaying) yield return new ExitPlayMode(); }
    }
}
