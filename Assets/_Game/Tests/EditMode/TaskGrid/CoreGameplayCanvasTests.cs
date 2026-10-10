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
            var data = new PointerEventData(EventSystem.current) { position = new Vector2(10,10), button = PointerEventData.InputButton.Left };
            ((IBeginDragHandler)card).OnBeginDrag(data);
            data.position = ScreenCell(board, 2, 0);
            ((IDragHandler)card).OnDrag(data); ((IEndDragHandler)card).OnEndDrag(data);
            Assert.That(sim.Tasks["A-1"].State, Is.EqualTo(RequestState.Running));
            Assert.That(sim.Tasks["A-1"].Origin, Is.EqualTo(new Cell(2,0)));
            Assert.That(GameObject.Find("OccupancyLabel").GetComponent<Text>().text, Does.StartWith("已占用 4 /"));
            ButtonNamed("RotateButton").onClick.Invoke();
            Assert.That(sim.Tasks["A-1"].Rotation, Is.EqualTo(1));
            yield return null;
            var module = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                .Single(b => b.GetType().FullName == "CallmeCatgirl.UI.TaskModuleView" && b.name == "RunningModule_A-1");
            data.position = ScreenCell(board,2,0); ((IBeginDragHandler)module).OnBeginDrag(data);
            data.position = ScreenCell(board,0,0); ((IEndDragHandler)module).OnEndDrag(data);
            Assert.That(sim.Tasks["A-1"].Origin, Is.EqualTo(new Cell(2,0)), "Illegal drop retains the original placement.");
            ButtonNamed("WithdrawButton").onClick.Invoke();
            view.GetType().GetMethod("Refresh").Invoke(view,null);
            Assert.That(sim.Tasks["A-1"].State, Is.EqualTo(RequestState.Withdrawn));
            Assert.That(card.gameObject.activeSelf, Is.True);
            ((IBeginDragHandler)card).OnBeginDrag(data); data.position = ScreenCell(board,2,0); ((IEndDragHandler)card).OnEndDrag(data);
            sim.Paused = false; sim.Advance(10); sim.Paused = true;
            view.GetType().GetMethod("Refresh").Invoke(view,null);
            Assert.That(sim.Tasks["A-1"].GetAbility(AbilityKind.Writing), Is.EqualTo(12));
            Assert.That(ButtonNamed("SubmitButton").interactable, Is.True);
            ButtonNamed("SubmitButton").onClick.Invoke();
            Assert.That(sim.Tasks["A-1"].State, Is.EqualTo(RequestState.Submitted));
            ButtonNamed("CloseButton").onClick.Invoke();
            view.GetType().GetMethod("Refresh").Invoke(view,null);
            Assert.That(GameObject.Find("ConversationPanel"), Is.Not.Null, "Communication stays visible when details close.");
            ButtonNamed("User_A").onClick.Invoke();
            view.GetType().GetMethod("Refresh").Invoke(view,null);
            ButtonNamed("NextRoundButton").onClick.Invoke();
            Assert.That(sim.Tasks.Count, Is.EqualTo(4));
            ButtonNamed("User_B").onClick.Invoke();
            Assert.That(controller.GetType().GetProperty("SelectedId").GetValue(controller), Is.EqualTo("B-1"));
            Assert.That(Object.FindObjectsByType<Text>(FindObjectsSortMode.None).All(t => t.font != null), Is.True);
            yield return new ExitPlayMode();
        }
        [UnityTest]
        public IEnumerator SpeedSelectionStaysVisibleAfterFocusChangesAndReset()
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
            var buttons = new[] { ButtonNamed("SpeedButton"), ButtonNamed("Speed2Button"), ButtonNamed("Speed4Button") };
            var idle = buttons.Select(b => b.targetGraphic.color).ToArray();
            for (int i = 0; i < buttons.Length; i++)
            {
                Canvas.ForceUpdateCanvases();
                var rect = (RectTransform)buttons[i].transform;
                var pointer = new PointerEventData(EventSystem.current)
                {
                    position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center)),
                    button = PointerEventData.InputButton.Left
                };
                var hits = new System.Collections.Generic.List<RaycastResult>();
                EventSystem.current.RaycastAll(pointer, hits);
                Assert.That(hits.Count, Is.GreaterThan(0));
                var receiver = ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject);
                Assert.That(receiver, Is.EqualTo(buttons[i].gameObject));
                ExecuteEvents.Execute(receiver, pointer, ExecuteEvents.pointerClickHandler);
                view.GetType().GetMethod("Refresh").Invoke(view, null);
                Assert.That(buttons[i].targetGraphic.color, Is.Not.EqualTo(idle[i]), "Active speed must remain highlighted.");
                for (int j = 0; j < buttons.Length; j++)
                    if (j != i) Assert.That(buttons[j].targetGraphic.color, Is.EqualTo(idle[j]));
                Assert.That(GameObject.Find("SpeedReadout").GetComponent<Text>().text,
                    Is.EqualTo($"速度 {1 << i}x · 已暂停"));
                EventSystem.current.SetSelectedGameObject(ButtonNamed("User_B").gameObject);
                yield return null;
                Assert.That(buttons[i].targetGraphic.color, Is.Not.EqualTo(idle[i]), "Changing UI focus must not clear speed selection.");
                double before = sim.GameTime;
                yield return null;
                Assert.That(sim.GameTime, Is.EqualTo(before));
                sim.Paused = false;
                yield return null;
                Assert.That(sim.GameTime - before, Is.EqualTo(Time.unscaledDeltaTime * (1 << i)).Within(0.0001));
                Assert.That(GameObject.Find("SpeedReadout").GetComponent<Text>().text, Does.Contain("运行中"));
                sim.Paused = true;
            }
            controller.GetType().GetMethod("ResetSimulation").Invoke(controller, null);
            float resetSpeed = (float)controller.GetType().GetProperty("PlaybackSpeed").GetValue(controller);
            for (int i = 0; i < buttons.Length; i++)
                Assert.That(buttons[i].targetGraphic.color == idle[i], Is.EqualTo(!Mathf.Approximately(resetSpeed, 1 << i)));
            yield return new ExitPlayMode();
        }
        private static Button ButtonNamed(string name) => Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Single(b => b.name == name);
        private static Vector2 ScreenCell(RectTransform board,int x,int y) => RectTransformUtility.WorldToScreenPoint(null,
            board.TransformPoint(new Vector3(board.rect.xMin+(x+0.5f)*board.rect.width/8,board.rect.yMax-(y+0.5f)*board.rect.height/8,0)));
        [UnityTearDown] public IEnumerator StopPlayMode() { if(UnityEditor.EditorApplication.isPlaying) yield return new ExitPlayMode(); }
    }
}
