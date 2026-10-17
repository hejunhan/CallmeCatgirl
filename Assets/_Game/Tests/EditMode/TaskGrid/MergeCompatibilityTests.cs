using System;
using System.Collections;
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
    public sealed class MergeCompatibilityTests
    {
        private static object Call(object o, string name, params object[] args) => o.GetType().GetMethod(name).Invoke(o, args);
        private static T Get<T>(object o, string name) => (T)o.GetType().GetProperty(name).GetValue(o);
        private static T Field<T>(object o, string name) => (T)o.GetType().GetField(name).GetValue(o);
        private static void Set(object o, string name, object value) => o.GetType().GetField(name).SetValue(o, value);
        private static void Complete(object owner, string id)
        {
            var sim = Get<TaskSimulation>(owner, "Simulation");
            Call(owner, "SelectRequest", id);
            bool placed = false;
            for (int rotation = 0; rotation < 4 && !placed; rotation++)
                for (int i = 0; i < sim.Width * sim.Height && !placed; i++)
                    placed = sim.TryPlace(id, new Cell(i % sim.Width, i / sim.Width), rotation, out _);
            Assert.That(placed, Is.True);
            var task = sim.Tasks[id];
            double seconds = sim.TokenThroughput > 0 ? (task.Definition.UsesCalculationBudget ? task.Definition.Calculation : task.Definition.Tokens) / sim.TokenThroughput : task.Definition.Calculation / task.Definition.Speed;
            sim.Paused = false; Call(owner, "AdvanceGame", seconds + .01d); sim.Paused = true;
            Assert.That(task.State, Is.EqualTo(RequestState.Ready));
            Call(owner, "SubmitSelected");
        }
        [Test] public void OperatingCalculationBudgetSurvivesSnapshotWithoutBecomingItsSmallReward()
        {
            var sim = new TaskSimulation(1, 1, new[] { new Cell(0, 0) },
                new ModelDefinition("model", 1, new System.Collections.Generic.Dictionary<AbilityKind, double>()));
            sim.Paused = true; Assert.That(sim.TrySetTokenThroughput(10), Is.True);
            var definition = new TaskDefinition("ops", "经营", new[] { new Cell(0, 0) }, 600, 10, 240, 15,
                Array.Empty<AbilityRequirement>(), usesCalculationBudget: true);
            sim.TryCreate("op-1", "op-user", "1", definition, out var task); sim.TryPlace(task.Id, new Cell(0, 0), 0, out _);
            sim.Paused = false; sim.Advance(30); sim.DrainEvents();
            Assert.That(task.State, Is.EqualTo(RequestState.Running)); Assert.That(task.Progress, Is.EqualTo(300));
            var restored = TaskSimulation.RestoreState(sim.CaptureState());
            Assert.That(restored.Tasks[task.Id].Definition.UsesCalculationBudget, Is.True);
            restored.Advance(30); Assert.That(restored.Tasks[task.Id].State, Is.EqualTo(RequestState.Ready));
            Assert.That(restored.TrySubmit(task.Id), Is.True);
            Assert.That(restored.DrainEvents().Last().BillableTokens, Is.EqualTo(15));
        }
        [UnityTest] public IEnumerator AffinityProgressOperatingTasksAndRandomSurviveProductionSaveRestore()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity");
            yield return new EnterPlayMode(); yield return null;
            CheckSaveRestore();
            yield return new ExitPlayMode();
        }
        private static void CheckSaveRestore()
        {
            var owner = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Single(o => o.GetType().Name == "CoreGameplayController");
            var configField = owner.GetType().GetField("configuration", BindingFlags.NonPublic | BindingFlags.Instance);
            var original = (ScriptableObject)configField.GetValue(owner);
            var config = Object.Instantiate(original);
            var importer = Type.GetType("CallmeCatgirl.EditorTools.StoryJsonImporter, Assembly-CSharp-Editor", true);
            const string json = "{\"version\":1,\"characters\":[{\"id\":\"A\",\"kind\":\"Scripted\",\"name\":\"合并测试\",\"sessions\":[{\"id\":\"s1\",\"title\":\"任务一\",\"requestId\":\"A\",\"task\":{\"shape\":[\"#\"],\"calculation\":3,\"patience\":500},\"rounds\":[{\"id\":\"r1\",\"message\":\"你好\",\"choices\":[{\"label\":\"继续\"}]}]},{\"id\":\"s2\",\"title\":\"任务二\",\"requestId\":\"A\",\"rounds\":[{\"id\":\"r1\",\"message\":\"再聊\",\"choices\":[{\"label\":\"返回已完成会话\"}]}]}]}]}";
            var storyConfig = (ScriptableObject)importer.GetMethod("Parse").Invoke(null, new object[] { json, new[] { "A", "B", "C" } });
            var user = Field<Array>(storyConfig, "Users").GetValue(0);
            var sessions = Field<Array>(user, "Sessions");
            Set(Field<Array>(Field<Array>(sessions.GetValue(1), "Rounds").GetValue(0), "Choices").GetValue(0), "NextNodeId", "s1/r1");
            string slot = "merge-qa-" + Guid.NewGuid().ToString("N");
            try
            {
                Set(config, "Story", storyConfig); Set(config, "OpeningBalance", null); Set(config, "OrdinaryDialoguePool", null);
                Set(config, "MonthlyMaintenanceFee", 0); Set(config, "SecondsPerGameMonth", 10000f);
                foreach (var definition in Field<Array>(config, "Requests"))
                { Set(definition, "Minimum", Vector3.zero); Set(definition, "Excellent", Vector3.zero); }
                configField.SetValue(owner, config); Call(owner, "ResetSimulation"); Call(owner, "ConfigureSaveSlot", slot);
                Assert.That(Get<bool>(owner, "TrainingEnabled"), Is.True);
                Call(owner, "ExecuteCommandJson", "{\"kind\":\"clock\",\"paused\":true}");
                Complete(owner, "A-1"); Call(owner, "ChooseReply", 0);
                var story = Get<object>(owner, "Story");
                Assert.That(Call(story, "Affinity", "A"), Is.EqualTo(1d));
                Assert.That(Call(story, "SessionNumber", "A"), Is.EqualTo(2));
                Call(owner, "AddRequest"); string op = Get<string>(owner, "SelectedId");
                Assert.That(Call(owner, "IsOperatingTask", op), Is.True);
                Complete(owner, op);
                Assert.That(Get<TaskSimulation>(owner, "Simulation").Tasks[op].State, Is.EqualTo(RequestState.Closed));
                Assert.That(Get<bool>(story, "IsReply"), Is.False);
                Assert.That(Call(story, "Affinity", "A"), Is.EqualTo(1d));
                int funds = Get<int>(owner, "Funds"), count = Get<int>(owner, "CompletedTasks");
                Assert.That(Call(owner, "SaveRun"), Is.EqualTo(true), Get<string>(owner, "Message"));
                Call(owner, "AddRequest"); string first = Get<string>(owner, "SelectedId");
                var expected = Get<TaskSimulation>(owner, "Simulation").Tasks[first].Definition;
                Assert.That(Call(owner, "ContinueRun"), Is.EqualTo(true), Get<string>(owner, "Message"));
                story = Get<object>(owner, "Story");
                Assert.That(Call(story, "Affinity", "A"), Is.EqualTo(1d));
                Assert.That(Call(story, "SessionNumber", "A"), Is.EqualTo(2));
                Assert.That(Call(owner, "IsOperatingTask", op), Is.True);
                Assert.That(Get<int>(owner, "Funds"), Is.EqualTo(funds));
                Assert.That(Get<int>(owner, "CompletedTasks"), Is.EqualTo(count));
                Call(owner, "AddRequest"); Assert.That(Get<string>(owner, "SelectedId"), Is.EqualTo(first));
                var actual = Get<TaskSimulation>(owner, "Simulation").Tasks[first].Definition;
                Assert.That(actual.Title, Is.EqualTo(expected.Title)); Assert.That(actual.Shape, Is.EqualTo(expected.Shape));
                Call(owner, "SelectRequest", "A-1"); Call(owner, "NextRound");
                string next = Get<string>(owner, "SelectedId"); Assert.That(next, Is.Not.EqualTo("A-1"));
                Complete(owner, next); Call(owner, "ChooseReply", 0);
                Assert.That(Call(story, "Finished", "A"), Is.EqualTo(true), "A completed session cannot return through a backward branch after load.");
                Assert.That(Call(story, "Affinity", "A"), Is.EqualTo(2d));
                Assert.That(Call(owner, "SaveRun"), Is.EqualTo(true), Get<string>(owner, "Message"));
                Assert.That(Call(owner, "ContinueRun"), Is.EqualTo(true), Get<string>(owner, "Message"));
                Assert.That(Call(Get<object>(owner, "Story"), "Finished", "A"), Is.EqualTo(true));
            }
            finally { configField.SetValue(owner, original); Object.DestroyImmediate(config); Object.DestroyImmediate(storyConfig); }
        }
        [UnityTest] public IEnumerator HalfSpeedArtworkAndOperatingSubmissionRemainUsableInWorkspace()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity");
            yield return new EnterPlayMode(); yield return null;
            CheckWorkspace();
            yield return new ExitPlayMode();
        }
        private static void CheckWorkspace()
        {
            var owner = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Single(o => o.GetType().Name == "CoreGameplayController");
            var buttons = Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var half = buttons.Single(b => b.name == "0.5SpeedButton");
            Assert.That(half.GetComponent<Image>().sprite.name, Is.EqualTo("button_speed_0.5x_selected"));
            Assert.That(half.GetComponentInChildren<Text>(true).enabled, Is.False);
            Call(owner, "AddRequest"); string id = Get<string>(owner, "SelectedId");
            Assert.That(Call(owner, "IsOperatingTask", id), Is.True);
            var submit = buttons.Single(b => b.name == "SubmitButton"); Assert.That(submit.gameObject.activeInHierarchy, Is.True);
            var canvas = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Single(o => o.GetType().Name == "CoreGameplayCanvasView");
            Call(canvas, "OpenRequestConversation", id);
            Assert.That(Get<bool>(Get<object>(owner, "Story"), "IsReply"), Is.False);
            Assert.That(GameObject.Find("Conversation_new-user-1"), Is.Null, "Operating work must not create fake contacts.");
            typeof(SystemFeedbackTests).GetMethod("Capture", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { "MergeWorkspace.png" });
        }
        [UnityTearDown] public IEnumerator StopPlay() { if (EditorApplication.isPlaying) yield return new ExitPlayMode(); }
    }
}
