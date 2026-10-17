using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using CallmeCatgirl.Gameplay.TaskGrid;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace CallmeCatgirl.Tests
{
    public sealed class ConversationStoryTests
    {
        private MonoBehaviour owner;
        private ScriptableObject original, fixtureConfig, fixtureStory;
        [UnitySetUp] public IEnumerator Prepare()
        {
            EditorSceneManager.OpenScene(Scene); yield return new EnterPlayMode(); yield return null;
            owner = Controller();
            var field = owner.GetType().GetField("configuration", BindingFlags.Instance | BindingFlags.NonPublic);
            original = (ScriptableObject)field.GetValue(owner); fixtureConfig = Object.Instantiate(original);
            var importer = Type.GetType("CallmeCatgirl.EditorTools.StoryJsonImporter, Assembly-CSharp-Editor", true);
            fixtureStory = (ScriptableObject)importer.GetMethod("Parse").Invoke(null, new object[] { @"{""version"": 1, ""characters"": [{""id"": ""A"", ""name"": ""林小满"", ""initialHistory"": ""林小满：还醒着吗？"", ""sessions"": [{""id"": ""s1"", ""title"": ""一句晚安"", ""requestId"": ""A"", ""rounds"": [{""id"": ""r1"", ""message"": ""可以认真说一句晚安吗？"", ""choices"": [{""label"": ""温柔地说晚安"", ""reply"": ""辛苦啦，晚安喵。"", ""response"": """", ""requirement"": ""Any""}, {""label"": ""聊聊计划"", ""reply"": ""今天最让你担心的是什么？"", ""response"": """", ""requirement"": ""Any""}, {""label"": ""特别的晚安信"", ""reply"": ""愿你做个好梦。"", ""response"": """", ""requirement"": ""ExcellentOnly""}]}]}, {""id"": ""s2"", ""title"": ""聊聊明天"", ""requestId"": ""C"", ""rounds"": [{""id"": ""r1"", ""message"": ""我应该从哪里开始？"", ""choices"": [{""label"": ""从小事开始"", ""reply"": ""先完成最小的一件事。"", ""response"": """", ""requirement"": ""Any""}]}]}]}, {""id"": ""C"", ""name"": ""苏禾"", ""sessions"": [{""id"": ""s1"", ""title"": ""待办"", ""rounds"": [{""id"": ""r1"", ""message"": ""能帮我整理待办吗？"", ""choices"": [{""label"": ""开始整理"", ""reply"": ""先整理最紧急的事情。"", ""response"": """", ""requirement"": ""Any""}]}]}, {""id"": ""s2"", ""title"": ""复盘"", ""rounds"": [{""id"": ""r1"", ""message"": ""我们复盘一下吧。"", ""choices"": [{""label"": ""好的"", ""reply"": ""看看做了什么。"", ""response"": """", ""requirement"": ""Any""}]}]}]}]}", new[] { "A", "B", "C" } });
            fixtureConfig.GetType().GetField("OrdinaryDialoguePool").SetValue(fixtureConfig, null);
            foreach (var profile in (Array)fixtureStory.GetType().GetField("Users").GetValue(fixtureStory))
            {
                var kind = profile.GetType().GetField("Kind");
                kind.SetValue(profile, Enum.Parse(kind.FieldType, "Scripted"));
            }
            fixtureConfig.GetType().GetField("Story").SetValue(fixtureConfig, fixtureStory);
            field.SetValue(owner, fixtureConfig); Call(owner, "ResetSimulation");
        }
        private const string Scene = "Assets/_Game/Scenes/Prototypes/CoreGameplay.unity";
        private static MonoBehaviour Controller() => Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
            .Single(c => c.GetType().FullName == "CallmeCatgirl.Gameplay.CoreGameplayController");
        private static T Value<T>(object o, string name) => (T)o.GetType().GetProperty(name).GetValue(o);
        private static object Call(object o, string name, params object[] args) => o.GetType().GetMethod(name).Invoke(o, args);
        private static Button Button(string name) => Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Single(b => b.name == name && (b.gameObject.activeSelf || name == "CloseHistory"));
        private static void Complete(MonoBehaviour owner, string id, Cell origin, double duration)
        {
            var sim = Value<TaskSimulation>(owner, "Simulation");
            Place(sim, id, origin);
            sim.Paused = false; sim.Advance(duration); sim.Paused = true;
            Assert.That(sim.Tasks[id].State, Is.EqualTo(RequestState.Ready));
            Call(owner, "SelectRequest", id);
        }
        private static void Place(TaskSimulation sim, string id, Cell preferred)
        {
            var origin = sim.Preview(id, preferred, 0) == PlacementFailure.None ? preferred :
                Enumerable.Range(0, sim.Width * sim.Height).Select(i => new Cell(i % sim.Width, i / sim.Width))
                    .First(cell => sim.Preview(id, cell, 0) == PlacementFailure.None);
            Assert.That(sim.TryPlace(id, origin, 0, out _), Is.True);
        }

        [UnityTest] public IEnumerator SubmissionOpensRealModalAndBlocksTimeAndUnderlyingInput()
        {
            var owner = Controller(); var sim = Value<TaskSimulation>(owner, "Simulation");
            Complete(owner, "A-1", new Cell(2, 0), 10);
            Place(sim, "B-1", new Cell(2, 3));
            sim.Paused = false;
            Call(owner, "SubmitSelected");
            var story = Value<object>(owner, "Story");
            Assert.That(Value<bool>(owner, "IsStoryOpen"), Is.True);
            Assert.That(GameObject.Find("ConversationPopup"), Is.Not.Null);
            Assert.That(GameObject.Find("UserName").GetComponent<Text>().text, Is.EqualTo("林小满"));
            Assert.That(GameObject.Find("HistoryScroll").GetComponentInChildren<Text>().text, Does.Contain("还醒着吗"));
            Assert.That(GameObject.Find("Result").GetComponent<Text>().text, Does.Contain("完成"));
            Assert.That(Button("CloseHistory").gameObject.activeSelf, Is.False, "Reply must be chosen before closing.");
            Assert.That(Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Count(b => b.name.StartsWith("ReplyChoice_")), Is.EqualTo(2));
            double time = sim.GameTime, progress = sim.Tasks["B-1"].Progress, month = Value<double>(owner, "MonthTimeRemaining");
            int funds = Value<int>(owner, "Funds");
            Call(owner, "AdvanceGame", 80d); Call(owner, "TogglePause"); Call(owner, "NextRound"); Call(owner, "SubmitSelected");
            yield return new WaitForSecondsRealtime(.12f);
            Assert.That(sim.GameTime, Is.EqualTo(time)); Assert.That(sim.Tasks["B-1"].Progress, Is.EqualTo(progress));
            Assert.That(Value<double>(owner, "MonthTimeRemaining"), Is.EqualTo(month));
            Assert.That(Value<int>(owner, "Funds"), Is.EqualTo(funds)); Assert.That(sim.Tasks.Count, Is.EqualTo(3));
            Assert.That(sim.Paused, Is.False, "Modal must preserve the previous running state.");
            Canvas.ForceUpdateCanvases();
            var button = Button("ReplyChoice_0"); var rect = (RectTransform)button.transform;
            var pointer = new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center)) };
            var hits = new System.Collections.Generic.List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
            Assert.That(ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject), Is.EqualTo(button.gameObject));
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler);
            Assert.That(Value<bool>(owner, "IsStoryOpen"), Is.False); Assert.That(sim.Paused, Is.False);
            Call(owner, "AdvanceGame", 1d); sim.Paused = true;
            Assert.That(sim.GameTime, Is.EqualTo(time + 1)); Assert.That(sim.Tasks["B-1"].Progress, Is.GreaterThan(progress));
            Assert.That(Call(story, "History", "A"), Does.Contain("辛苦啦"));
        }

        [UnityTest] public IEnumerator ChoicesRouteNextTaskAndNextSubmissionAndHistoryRemainsPerUser()
        {
            var owner = Controller(); var sim = Value<TaskSimulation>(owner, "Simulation");
            Complete(owner, "A-1", new Cell(2, 0), 10); Call(owner, "SubmitSelected"); Call(owner, "ChooseReply", 1);
            Assert.That(sim.Paused, Is.True, "A paused session must remain paused after choosing.");
            Call(owner, "NextRound"); string next = Value<string>(owner, "SelectedId");
            Assert.That(sim.Tasks[next].ConversationId, Is.EqualTo("A"));
            Assert.That(sim.Tasks[next].Definition.Id, Is.EqualTo("C"));
            Complete(owner, next, new Cell(2, 0), 3); Call(owner, "SubmitSelected");
            var story = Value<object>(owner, "Story");
            Assert.That(Call(story, "History", "A"), Does.Contain("从哪里开始"));
            Assert.That(Call(story, "History", "B"), Does.Not.Contain("从哪里开始"));
            Call(owner, "ChooseReply", 0);
            Call(owner, "OpenConversationHistory");
            Assert.That(Value<bool>(story, "IsReply"), Is.False);
            Assert.That(Button("CloseHistory").gameObject.activeSelf, Is.True);
            Assert.That(GameObject.Find("HistoryScroll").GetComponentInChildren<Text>().text, Does.Contain("最小的一件"));
            Button("CloseHistory").onClick.Invoke(); Assert.That(Value<bool>(owner, "IsStoryOpen"), Is.False);
            Call(owner, "ResetSimulation");
            story = Value<object>(owner, "Story");
            Assert.That(Call(story, "History", "A"), Does.Not.Contain("最小的一件"));
            yield break;
        }

        [UnityTest] public IEnumerator QualityConditionsRevealExcellentChoicesAndFallbackPreventsDeadEnd()
        {
            VerifyQualityAndFallback(Controller());
            yield break;
        }
        private static void VerifyQualityAndFallback(MonoBehaviour owner)
        {
            var field = owner.GetType().GetField("configuration", BindingFlags.Instance | BindingFlags.NonPublic);
            var original = (ScriptableObject)field.GetValue(owner); var config = Object.Instantiate(original);
            try
            {
                config.GetType().GetField("ModelPoints").SetValue(config, new Vector4(8, 4, 4, 0));
                field.SetValue(owner, config); Call(owner, "ResetSimulation");
                Complete(owner, "A-1", new Cell(2, 0), 10); Call(owner, "SubmitSelected");
                Assert.That(GameObject.Find("Result").GetComponent<Text>().text, Does.Contain("优秀"));
                Assert.That(Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Count(b => b.name.StartsWith("ReplyChoice_")), Is.EqualTo(3));
                Call(owner, "ChooseReply", 2);
                config.GetType().GetField("Story").SetValue(config, null); Call(owner, "ResetSimulation");
                Complete(owner, "A-1", new Cell(2, 0), 10); Call(owner, "SubmitSelected");
                Assert.That(Button("ReplyChoice_0").GetComponentInChildren<Text>().text, Is.EqualTo("发送回复"));
                Call(owner, "ChooseReply", -1); Assert.That(Value<bool>(owner, "IsStoryOpen"), Is.True);
                Call(owner, "ChooseReply", 0); Assert.That(Value<bool>(owner, "IsStoryOpen"), Is.False);
            }
            finally { field.SetValue(owner, original); Object.DestroyImmediate(config); }
        }

        [UnityTest] public IEnumerator SimultaneousSubmissionsAreQueuedAndSettledOnlyOnce()
        {
            var owner = Controller(); var sim = Value<TaskSimulation>(owner, "Simulation");
            Complete(owner, "A-1", new Cell(2, 0), 10); Complete(owner, "C-1", new Cell(4, 2), 3);
            int funds = Value<int>(owner, "Funds");
            Assert.That(sim.TrySubmit("A-1"), Is.True); Assert.That(sim.TrySubmit("C-1"), Is.True);
            yield return null;
            var story = Value<object>(owner, "Story");
            Assert.That(Value<string>(story, "UserId"), Is.EqualTo("A"));
            Assert.That(Value<int>(owner, "Funds"), Is.EqualTo(funds + 130));
            Call(owner, "ChooseReply", 0);
            Assert.That(Value<string>(story, "UserId"), Is.EqualTo("C")); Assert.That(Value<bool>(owner, "IsStoryOpen"), Is.True);
            Call(owner, "ChooseReply", 0); Assert.That(Value<bool>(owner, "IsStoryOpen"), Is.False);
            Assert.That(Value<int>(owner, "Funds"), Is.EqualTo(funds + 130));
            Assert.That(Call(story, "History", "C"), Does.Not.Contain("晚安喵"));
        }
        [UnityTest] public IEnumerator HistoryReviewAndNewUsersHaveFallbackAndResetClosesModal()
        {
            var owner = Controller();
            Call(owner, "AddRequest"); yield return null;
            string id = Value<string>(owner, "SelectedId"); Complete(owner, id, new Cell(2, 0), 10);
            Call(owner, "SubmitSelected");
            var story = Value<object>(owner, "Story");
            Assert.That(Value<string>(story, "UserId"), Is.EqualTo("new-user-1"));
            Assert.That(GameObject.Find("HistoryScroll").GetComponent<ScrollRect>().vertical, Is.True);
            Assert.That(GameObject.Find("ChoiceScroll").GetComponent<ScrollRect>().vertical, Is.True);
            Call(owner, "ChooseReply", 0); Call(owner, "OpenConversationHistory");
            Call(owner, "ResetSimulation"); Assert.That(Value<bool>(owner, "IsStoryOpen"), Is.False);
        }
        [UnityTest] public IEnumerator LongHistoryAndEightChoicesCanBeScrolledAndSelected()
        {
            var owner = Controller();
            var field = owner.GetType().GetField("configuration", BindingFlags.Instance | BindingFlags.NonPublic);
            var original = (ScriptableObject)field.GetValue(owner); var config = Object.Instantiate(original);
            var storyConfig = Object.Instantiate((ScriptableObject)config.GetType().GetField("Story").GetValue(config));
            try
            {
                var users = (Array)storyConfig.GetType().GetField("Users").GetValue(storyConfig);
                var user = users.GetValue(0);
                user.GetType().GetField("InitialHistory").SetValue(user,
                    string.Join("\n", Enumerable.Range(0, 60).Select(i => "林小满：这是之前的第 " + i + " 条对话记录。")));
                var session = ((Array)user.GetType().GetField("Sessions").GetValue(user)).GetValue(0);
                var node = ((Array)session.GetType().GetField("Rounds").GetValue(session)).GetValue(0);
                var choicesField = node.GetType().GetField("Choices"); var choiceType = choicesField.FieldType.GetElementType();
                var choices = Array.CreateInstance(choiceType, 8);
                for (int i = 0; i < choices.Length; i++)
                {
                    var choice = Activator.CreateInstance(choiceType);
                    choiceType.GetField("Label").SetValue(choice, "可滚动选项 " + i);
                    choiceType.GetField("PlayerReply").SetValue(choice, "长列表回复 " + i); choices.SetValue(choice, i);
                }
                choicesField.SetValue(node, choices);
                config.GetType().GetField("Story").SetValue(config, storyConfig); field.SetValue(owner, config);
                Call(owner, "ResetSimulation"); Complete(owner, "A-1", new Cell(2, 0), 10); Call(owner, "SubmitSelected");
                yield return null; Canvas.ForceUpdateCanvases();
                var history = GameObject.Find("HistoryScroll").GetComponent<ScrollRect>();
                var options = GameObject.Find("ChoiceScroll").GetComponent<ScrollRect>();
                Assert.That(history.content.rect.height, Is.GreaterThan(history.viewport.rect.height));
                Assert.That(options.content.rect.height, Is.GreaterThan(options.viewport.rect.height));
                float bottom = history.content.anchoredPosition.y;
                history.verticalNormalizedPosition = 1; options.verticalNormalizedPosition = 0;
                yield return null; Canvas.ForceUpdateCanvases();
                Assert.That(history.content.anchoredPosition.y, Is.LessThan(bottom));
                var button = Button("ReplyChoice_7"); var rect = (RectTransform)button.transform;
                var position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
                Assert.That(RectTransformUtility.RectangleContainsScreenPoint(options.viewport, position, null), Is.True);
                var pointer = new PointerEventData(EventSystem.current) { position = position };
                var hits = new System.Collections.Generic.List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
                Assert.That(ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject), Is.EqualTo(button.gameObject));
                ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler);
                Assert.That(Value<bool>(owner, "IsStoryOpen"), Is.False);
                Assert.That(Call(Value<object>(owner, "Story"), "History", "A"), Does.Contain("长列表回复 7"));
            }
            finally { field.SetValue(owner, original); Object.DestroyImmediate(config); Object.DestroyImmediate(storyConfig); }
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if (owner != null) owner.GetType().GetField("configuration", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(owner, original);
            if (fixtureConfig != null) Object.DestroyImmediate(fixtureConfig);
            if (fixtureStory != null) Object.DestroyImmediate(fixtureStory);
            if (EditorApplication.isPlaying) yield return new ExitPlayMode();
        }
    }
}
