using System.Collections;
using System.Linq;
using CallmeCatgirl.Gameplay.TaskGrid;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace CallmeCatgirl.Tests
{
    public sealed class CoreGameplayCanvasTests
    {
        [UnityTest]
        public IEnumerator SavedCanvasSupportsDragWithdrawAndSubmit()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity");
            Assert.That(GameObject.Find("GameplayCanvas"), Is.Not.Null, "Canvas must already exist in the saved scene.");
            yield return new EnterPlayMode();
            yield return null;
            var controller = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                .Single(b => b.GetType().FullName == "CallmeCatgirl.Gameplay.CoreGameplayController");
            var sim = (TaskSimulation)controller.GetType().GetProperty("Simulation").GetValue(controller);
            sim.Paused = true;
            ButtonNamed("PauseButton").onClick.Invoke(); Assert.That(sim.Paused, Is.False);
            ButtonNamed("PauseButton").onClick.Invoke(); Assert.That(sim.Paused, Is.True);
            var view = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                .Single(b => b.GetType().FullName == "CallmeCatgirl.UI.CoreGameplayCanvasView");
            var board = (RectTransform)view.GetType().GetProperty("Board").GetValue(view);
            Canvas.ForceUpdateCanvases();
            Assert.That(GameObject.Find("MemoryPanel").transform.Find("ModelInfo"), Is.Not.Null);
            Assert.That(GameObject.Find("MemoryPanel").transform.Find("ModelInfo").GetComponent<Text>().text, Does.Contain("每 3 秒"));
            ButtonNamed("Speed4Button").onClick.Invoke();
            Assert.That(controller.GetType().GetProperty("PlaybackSpeed").GetValue(controller), Is.EqualTo(4f));
            ButtonNamed("SpeedButton").onClick.Invoke();
            Assert.That(controller.GetType().GetProperty("PlaybackSpeed").GetValue(controller), Is.EqualTo(1f));
            var card = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                .Single(b => b.GetType().FullName == "CallmeCatgirl.UI.TaskCardView" && (string)b.GetType().GetProperty("RequestId").GetValue(b) == "A-1");
            bool panelWasActive = Panel().activeSelf;
            var legal = FreeOrigin(sim, "A-1");
            var data = new PointerEventData(EventSystem.current) { position = new Vector2(10,10), button = PointerEventData.InputButton.Left };
            ((IBeginDragHandler)card).OnBeginDrag(data);
            data.position = ScreenCell(board, legal.X, legal.Y);
            ((IDragHandler)card).OnDrag(data); ((IEndDragHandler)card).OnEndDrag(data);
            Assert.That(sim.Tasks["A-1"].State, Is.EqualTo(RequestState.Running));
            Assert.That(sim.Tasks["A-1"].Origin, Is.EqualTo(legal));
            Assert.That(GameObject.Find("OccupancyLabel").GetComponent<Text>().text, Does.StartWith("已占用 4 /"));
            ButtonNamed("RotateButton").onClick.Invoke();
            Assert.That(sim.Tasks["A-1"].Rotation, Is.EqualTo(1));
            yield return null;
            var module = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                .Single(b => b.GetType().FullName == "CallmeCatgirl.UI.TaskModuleView" && b.name == "RunningModule_A-1");
            data.position = ScreenCell(board,legal.X,legal.Y); ((IBeginDragHandler)module).OnBeginDrag(data);
            data.position = ScreenCell(board,-1,-1); ((IEndDragHandler)module).OnEndDrag(data);
            Assert.That(sim.Tasks["A-1"].Origin, Is.EqualTo(legal), "Illegal drop retains the original placement.");
            ButtonNamed("WithdrawButton").onClick.Invoke();
            view.GetType().GetMethod("Refresh").Invoke(view,null);
            Assert.That(sim.Tasks["A-1"].State, Is.EqualTo(RequestState.Withdrawn));
            Assert.That(card.gameObject.activeSelf, Is.True);
            ((IBeginDragHandler)card).OnBeginDrag(data); data.position = ScreenCell(board,legal.X,legal.Y); ((IEndDragHandler)card).OnEndDrag(data);
            sim.Paused = false; sim.Advance(10); sim.Paused = true;
            view.GetType().GetMethod("Refresh").Invoke(view,null);
            Assert.That(sim.Tasks["A-1"].GetAbility(AbilityKind.Writing), Is.EqualTo(12));
            Assert.That(ButtonNamed("SubmitButton").interactable, Is.True);
            ButtonNamed("SubmitButton").onClick.Invoke();
            Assert.That(sim.Tasks["A-1"].State, Is.EqualTo(RequestState.Submitted));
            controller.GetType().GetMethod("ChooseReply").Invoke(controller, new object[] { 0 });
            ButtonNamed("CloseButton").onClick.Invoke();
            view.GetType().GetMethod("Refresh").Invoke(view,null);
            Assert.That(Panel().activeSelf, Is.EqualTo(panelWasActive), "Closing task details preserves the authored conversation-panel visibility.");
            controller.GetType().GetMethod("SelectConversation").Invoke(controller, new object[] { "A" });
            view.GetType().GetMethod("Refresh").Invoke(view,null);
            ButtonNamed("NextRoundButton").onClick.Invoke();
            Assert.That(sim.Tasks.Count, Is.EqualTo(4));
            controller.GetType().GetMethod("SelectConversation").Invoke(controller, new object[] { "B" });
            Assert.That(controller.GetType().GetProperty("SelectedId").GetValue(controller), Is.EqualTo("B-1"));
            Assert.That(Object.FindObjectsByType<Text>(FindObjectsSortMode.None).All(t => t.font != null), Is.True);
            yield return new ExitPlayMode();
        }
        [UnityTest]
        public IEnumerator SpeedSelectionStaysVisibleAfterFocusChangesAndReset()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity");
            yield return new EnterPlayMode(); yield return null;
            var controller = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                .Single(b => b.GetType().FullName == "CallmeCatgirl.Gameplay.CoreGameplayController");
            var view = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                .Single(b => b.GetType().FullName == "CallmeCatgirl.UI.CoreGameplayCanvasView");
            var sim = (TaskSimulation)controller.GetType().GetProperty("Simulation").GetValue(controller);
            var buttons = new[] { ButtonNamed("0.5SpeedButton"), ButtonNamed("SpeedButton"), ButtonNamed("Speed2Button"), ButtonNamed("Speed4Button") };
            float[] values = { .5f, 1, 2, 4 };
            var pause = ButtonNamed("PauseButton");
            var activeColor = buttons[0].targetGraphic.color; var pauseActive = pause.targetGraphic.color;
            Assert.That(sim.Paused, Is.True);
            Assert.That((float)controller.GetType().GetProperty("PlaybackSpeed").GetValue(controller), Is.EqualTo(.5f));
            for (int i = 0; i < buttons.Length; i++) Assert.That(ControlSelected(buttons[i], activeColor), Is.EqualTo(i == 0));
            for (int i = 0; i < buttons.Length; i++)
            {
                ClickButton(buttons[i]);
                Assert.That((float)controller.GetType().GetProperty("PlaybackSpeed").GetValue(controller), Is.EqualTo(values[i]));
                Assert.That(ControlSelected(buttons[i], activeColor), Is.True, "Active speed must remain highlighted.");
                for (int j = 0; j < buttons.Length; j++)
                    if (j != i) Assert.That(ControlSelected(buttons[j], activeColor), Is.False);
                if (GameObject.Find("SpeedReadout") is GameObject readout) Assert.That(readout.GetComponent<Text>().text, Is.EqualTo($"速度 {values[i]:0.##}x · 已暂停"));
                Assert.That(ControlSelected(pause, pauseActive), Is.True);
                EventSystem.current.SetSelectedGameObject(ButtonNamed("ResetButton").gameObject);
                yield return null;
                Assert.That(ControlSelected(buttons[i], activeColor), Is.True, "UI focus must not clear speed selection.");
                Assert.That(ControlSelected(pause, pauseActive), Is.True, "UI focus must not clear pause highlighting.");
                double before = sim.GameTime; yield return null;
                Assert.That(sim.GameTime, Is.EqualTo(before));
                ClickButton(pause); Assert.That(sim.Paused, Is.False);
                Assert.That(ControlSelected(pause, pauseActive), Is.False);
                yield return null;
                Assert.That(sim.GameTime - before, Is.EqualTo(Time.unscaledDeltaTime * values[i]).Within(.0001));
                if (GameObject.Find("SpeedReadout") is GameObject runningReadout) Assert.That(runningReadout.GetComponent<Text>().text, Does.Contain("运行中"));
                ClickButton(pause); Assert.That(sim.Paused, Is.True);
                Assert.That(ControlSelected(pause, pauseActive), Is.True);
            }
            // 弹窗也会冻结游戏，暂停按钮禁用但仍表示暂停状态；关闭后恢复原状态。
            ClickButton(pause);
            controller.GetType().GetMethod("OpenMemoryUpgrade").Invoke(controller, null);
            Assert.That(pause.interactable, Is.False); Assert.That(ControlSelected(pause, pauseActive), Is.True);
            controller.GetType().GetMethod("CloseMemoryUpgrade").Invoke(controller, null);
            Assert.That(pause.interactable, Is.True); Assert.That(ControlSelected(pause, pauseActive), Is.False);
            // 键盘空格也调用同一TogglePause命令，验证命令能立即更新高亮。
            controller.GetType().GetMethod("TogglePause").Invoke(controller, null);
            Assert.That(ControlSelected(pause, pauseActive), Is.True);
            controller.GetType().GetMethod("ResetSimulation").Invoke(controller, null);
            sim = (TaskSimulation)controller.GetType().GetProperty("Simulation").GetValue(controller);
            Assert.That(sim.Paused, Is.True); Assert.That(ControlSelected(pause, pauseActive), Is.True);
            for (int i = 0; i < buttons.Length; i++)
                Assert.That(ControlSelected(buttons[i], activeColor), Is.EqualTo(i == 0));
            yield return new ExitPlayMode();
        }
        private static void ClickButton(Button button)
        {
            Canvas.ForceUpdateCanvases(); var rect = (RectTransform)button.transform;
            var pointer = new PointerEventData(EventSystem.current)
            { position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center)), button = PointerEventData.InputButton.Left };
            var hits = new System.Collections.Generic.List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
            Assert.That(hits.Count, Is.GreaterThan(0), "No hit: " + button.name);
            var receiver = ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject);
            Assert.That(receiver, Is.EqualTo(button.gameObject), "Top hit: " + hits[0].gameObject.name);
            ExecuteEvents.Execute(receiver, pointer, ExecuteEvents.pointerClickHandler);
        }
        [UnityTest]
        public IEnumerator AvatarsFollowConversationsAndAppearOnNewRequestCards()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity");
            yield return new EnterPlayMode();
            yield return null;
            var controller = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                .Single(b => b.GetType().FullName == "CallmeCatgirl.Gameplay.CoreGameplayController");
            var view = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                .Single(b => b.GetType().FullName == "CallmeCatgirl.UI.CoreGameplayCanvasView");
            var sim = (TaskSimulation)controller.GetType().GetProperty("Simulation").GetValue(controller);
            sim.Paused = true;
            Panel().SetActive(true);
            var portrait = GameObject.Find("ConversationAvatar").GetComponent<Image>();
            var expected = new[] { "Avatar_Female_BrownHair_Glasses", "Avatar_Male_BlackHair_Glasses", "Avatar_Female_SilverHair_Bow" };
            for (int i = 0; i < expected.Length; i++)
            {
                ButtonNamed("User_" + "ABC"[i]).onClick.Invoke();
                view.GetType().GetMethod("Refresh").Invoke(view, null);
                Assert.That(portrait.sprite.name, Is.EqualTo(expected[i]));
                Assert.That(portrait.raycastTarget, Is.False, "Avatars must not intercept input.");
                Assert.That(ButtonNamed("User_" + "ABC"[i]).transform.Find("Avatar").GetComponent<Image>().sprite, Is.EqualTo(portrait.sprite));
                var card = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                    .Single(b => b.GetType().FullName == "CallmeCatgirl.UI.TaskCardView"
                        && (string)b.GetType().GetProperty("RequestId").GetValue(b) == "ABC"[i] + "-1");
                Assert.That(card.transform.Find("Avatar").GetComponent<Image>().sprite, Is.EqualTo(portrait.sprite));
            }
            controller.GetType().GetMethod("AddRequest").Invoke(controller, null);
            view.GetType().GetMethod("Refresh").Invoke(view, null);
            Assert.That(portrait.enabled, Is.False, "Operating requests must not impersonate story characters.");
            var newCard = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                .Single(b => b.GetType().FullName == "CallmeCatgirl.UI.TaskCardView"
                    && (string)b.GetType().GetProperty("RequestId").GetValue(b) == "new-1");
            Assert.That(newCard.transform.Find("Avatar").GetComponent<Image>().enabled, Is.False);
            controller.GetType().GetMethod("CloseInfo").Invoke(controller, null);
            view.GetType().GetMethod("Refresh").Invoke(view, null);
            Assert.That(portrait.enabled, Is.False);
            Assert.That(GameObject.Find("ConversationPanel"), Is.Not.Null);
            yield return new ExitPlayMode();
        }
        [UnityTest]
        public IEnumerator StartsAndResetsPausedUntilPlayerStarts()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity");
            yield return new EnterPlayMode();
            yield return null;
            var controller = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                .Single(b => b.GetType().FullName == "CallmeCatgirl.Gameplay.CoreGameplayController");
            var sim = (TaskSimulation)controller.GetType().GetProperty("Simulation").GetValue(controller);
            Assert.That(sim.Paused, Is.True);
            Assert.That(sim.GameTime, Is.Zero);
            Assert.That(ButtonNamed("PauseButton").GetComponentInChildren<Text>().text, Is.EqualTo("开始 [空格]"));
            sim.TryPlace("A-1", FreeOrigin(sim, "A-1"), 0, out _);
            ButtonNamed("Speed4Button").onClick.Invoke();
            yield return new WaitForSecondsRealtime(0.1f);
            Assert.That(sim.Paused, Is.True, "Speed selection must not start the session.");
            Assert.That(sim.GameTime, Is.Zero);
            Assert.That(sim.Tasks["A-1"].Progress, Is.Zero);
            Assert.That(sim.Tasks["A-1"].Remaining(sim.GameTime), Is.EqualTo(sim.Tasks["A-1"].Definition.Patience));
            ButtonNamed("PauseButton").onClick.Invoke();
            yield return null;
            Assert.That(sim.Paused, Is.False);
            Assert.That(sim.GameTime, Is.GreaterThan(0));
            Assert.That(sim.Tasks["A-1"].Progress, Is.GreaterThan(0));
            Assert.That(ButtonNamed("PauseButton").GetComponentInChildren<Text>().text, Is.EqualTo("暂停 [空格]"));
            controller.GetType().GetMethod("ResetSimulation").Invoke(controller, null);
            sim = (TaskSimulation)controller.GetType().GetProperty("Simulation").GetValue(controller);
            yield return new WaitForSecondsRealtime(0.1f);
            Assert.That(sim.Paused, Is.True);
            Assert.That(sim.GameTime, Is.Zero);
            Assert.That(sim.Occupancy.Count, Is.Zero);
            Assert.That(ButtonNamed("PauseButton").GetComponentInChildren<Text>().text, Is.EqualTo("开始 [空格]"));
            yield return new ExitPlayMode();
        }
        [UnityTest]
        public IEnumerator MemoryUpgradeButtonEnforcesConditionsAndPreservesTasks()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity");
            Assert.That(GameObject.Find("UpgradeMemoryButton"), Is.Not.Null);
            yield return new EnterPlayMode();
            yield return null;
            var controller = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                .Single(b => b.GetType().FullName == "CallmeCatgirl.Gameplay.CoreGameplayController");
            int Value(string name) => (int)controller.GetType().GetProperty(name).GetValue(controller);
            void Command(string name) => controller.GetType().GetMethod(name).Invoke(controller, null);
            var sim = (TaskSimulation)controller.GetType().GetProperty("Simulation").GetValue(controller);
            int initialCells = sim.OpenCellCount;
            var task = sim.Tasks["A-1"];
            sim.TryPlace(task.Id, FreeOrigin(sim, task.Id), 0, out _);
            sim.Paused = false; sim.Advance(4); sim.Paused = true;
            ButtonNamed("UpgradeMemoryButton").onClick.Invoke();
            int nextCells = ((System.Collections.Generic.IReadOnlyList<Cell>)controller.GetType().GetProperty("NextMemoryCells").GetValue(controller)).Count;
            var button = ButtonNamed("ConfirmMemoryUpgradeButton");
            button.onClick.Invoke();
            Assert.That(Value("MemoryLevel"), Is.EqualTo(2));
            Assert.That(Value("Funds"), Is.EqualTo(1400));
            Assert.That(sim.OpenCellCount, Is.EqualTo(nextCells));

            Assert.That(sim.Tasks[task.Id], Is.SameAs(task));
            Assert.That(task.Progress, Is.EqualTo(40));
            Assert.That(sim.Occupancy.Count, Is.EqualTo(4));
            Assert.That(sim.Paused, Is.True);
            Assert.That(button.interactable, Is.False, "Next level requires three completed tasks.");
            button.onClick.Invoke();
            Assert.That(Value("Funds"), Is.EqualTo(1400));
            Assert.That(Value("MemoryLevel"), Is.EqualTo(2));
            Command("CloseMemoryUpgrade");
            Assert.That(sim.TryPlace(task.Id, FreeOrigin(sim, task.Id), 0, out _), Is.True);
            sim.TryPlace("B-1", FreeOrigin(sim, "B-1"), 0, out _);
            sim.Paused = false; sim.Advance(10); sim.Paused = true;
            controller.GetType().GetMethod("SelectRequest").Invoke(controller, new object[] { "B-1" });
            Command("SubmitSelected");
            controller.GetType().GetMethod("ChooseReply").Invoke(controller, new object[] { 0 });
            yield return null;
            Assert.That(Value("CompletedTasks"), Is.Zero, "Below-standard submissions do not satisfy the upgrade condition.");
            controller.GetType().GetMethod("SelectRequest").Invoke(controller, new object[] { "A-1" });
            for (int completed = 1; completed <= 10; completed++)
            {
                if (completed > 1)
                {
                    Command("NextRound");
                    string id = (string)controller.GetType().GetProperty("SelectedId").GetValue(controller);
                    Assert.That(sim.TryPlace(id, FreeOrigin(sim, id), 0, out _), Is.True);
                    sim.Paused = false; sim.Advance(10); sim.Paused = true;
                }
                Command("SubmitSelected");
                controller.GetType().GetMethod("ChooseReply").Invoke(controller, new object[] { 0 });
                yield return null;
                Assert.That(Value("CompletedTasks"), Is.EqualTo(completed));
                if (completed == 3 || completed == 6 || completed == 10)
                {
                    Command("OpenMemoryUpgrade");
                    Assert.That(button.interactable, Is.True);
                    button.onClick.Invoke();
                    Command("CloseMemoryUpgrade");
                }
            }
            Assert.That(Value("MemoryLevel"), Is.EqualTo(5));
            Assert.That(Value("Funds"), Is.EqualTo(1000));
            Assert.That(sim.OpenCellCount, Is.EqualTo(64));
            Command("OpenMemoryUpgrade");
            Assert.That(button.interactable, Is.False);
            button.onClick.Invoke();
            Assert.That(Value("Funds"), Is.EqualTo(1000));
            Command("ResetSimulation");
            sim = (TaskSimulation)controller.GetType().GetProperty("Simulation").GetValue(controller);
            Assert.That(Value("MemoryLevel"), Is.EqualTo(1));
            Assert.That(Value("Funds"), Is.EqualTo(1500));
            Assert.That(Value("CompletedTasks"), Is.Zero);
            Assert.That(sim.OpenCellCount, Is.EqualTo(initialCells));
            Assert.That(sim.Paused, Is.True);
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator MemoryUpgradeRejectsInsufficientFundsAndAcceptsExternalCredit()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity");
            yield return new EnterPlayMode();
            yield return null;
            var controller = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                .Single(b => b.GetType().FullName == "CallmeCatgirl.Gameplay.CoreGameplayController");
            var serialized = new UnityEditor.SerializedObject(controller);
            var original = (ScriptableObject)serialized.FindProperty("configuration").objectReferenceValue;
            var config = Object.Instantiate(original);
            try
            {
                config.GetType().GetField("InitialFunds").SetValue(config, 0);
                serialized.FindProperty("configuration").objectReferenceValue = config;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                controller.GetType().GetMethod("ResetSimulation").Invoke(controller, null);
                Assert.That(ButtonNamed("UpgradeMemoryButton").interactable, Is.True);
                ButtonNamed("UpgradeMemoryButton").onClick.Invoke();
                var button = ButtonNamed("ConfirmMemoryUpgradeButton");
                Assert.That(button.interactable, Is.False);
                button.onClick.Invoke();
                Assert.That(controller.GetType().GetProperty("MemoryLevel").GetValue(controller), Is.EqualTo(1));
                Assert.That(controller.GetType().GetProperty("Funds").GetValue(controller), Is.EqualTo(0));
                controller.GetType().GetMethod("AddFunds").Invoke(controller, new object[] { 100 });
                Assert.That(button.interactable, Is.True);
                button.onClick.Invoke();
                Assert.That(controller.GetType().GetProperty("MemoryLevel").GetValue(controller), Is.EqualTo(2));
                Assert.That(controller.GetType().GetProperty("Funds").GetValue(controller), Is.EqualTo(0));
            }
            finally
            {
                serialized.FindProperty("configuration").objectReferenceValue = original;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Object.DestroyImmediate(config);
            }
            yield return new ExitPlayMode();
        }
        private static bool ControlSelected(Button button, Color legacyActive)
        {
            var artwork = button.GetComponents<Component>().FirstOrDefault(c => c.GetType().Name == "ButtonArtwork");
            return artwork != null ? (bool)artwork.GetType().GetProperty("IsSelected").GetValue(artwork) : button.targetGraphic.color == legacyActive;
        }
        private static GameObject Panel() => Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).Single(t => t.name == "ConversationPanel").gameObject;
        private static Cell FreeOrigin(TaskSimulation sim, string id) => Enumerable.Range(0, sim.Width * sim.Height)
            .Select(i => new Cell(i % sim.Width, i / sim.Width)).First(c => sim.Preview(id, c, 0) == PlacementFailure.None);
        private static Button ButtonNamed(string name) => Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Single(b => b.name == name);
        private static Vector2 ScreenCell(RectTransform board,int x,int y) => RectTransformUtility.WorldToScreenPoint(null,
            board.TransformPoint(new Vector3(board.rect.xMin+(x+0.5f)*board.rect.width/8,board.rect.yMax-(y+0.5f)*board.rect.height/8,0)));
        [UnityTearDown] public IEnumerator StopPlayMode() { if(UnityEditor.EditorApplication.isPlaying) yield return new ExitPlayMode(); }
    }
}
