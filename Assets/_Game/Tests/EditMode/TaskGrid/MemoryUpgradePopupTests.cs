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
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace CallmeCatgirl.Tests
{
    public sealed class MemoryUpgradePopupTests
    {
        private const string Scene = "Assets/_Game/Scenes/Prototypes/CoreGameplay.unity";
        private static MonoBehaviour Controller() => Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
            .Single(o => o.GetType().FullName == "CallmeCatgirl.Gameplay.CoreGameplayController");
        private static T Value<T>(object o, string name) => (T)o.GetType().GetProperty(name).GetValue(o);
        private static object Call(object o, string name, params object[] args) => o.GetType().GetMethod(name).Invoke(o, args);
        private static Button Button(string name) => Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Single(b => b.name == name);
        private static void Place(TaskSimulation sim, string id)
        {
            var cell = Enumerable.Range(0, sim.Width * sim.Height).Select(i => new Cell(i % sim.Width, i / sim.Width))
                .First(c => sim.Preview(id, c, 0) == PlacementFailure.None);
            Assert.That(sim.TryPlace(id, cell, 0, out _), Is.True);
        }
        private static void Click(string name)
        {
            Canvas.ForceUpdateCanvases(); var button = Button(name); var rect = (RectTransform)button.transform;
            var pointer = new PointerEventData(EventSystem.current)
            { position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center)) };
            var hits = new System.Collections.Generic.List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
            Assert.That(hits.Count, Is.GreaterThan(0), "No graphics were hit for " + name);
            Assert.That(ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject), Is.EqualTo(button.gameObject),
                "Top hit: " + hits[0].gameObject.name);
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler);
        }
        [UnityTest] public IEnumerator OpeningDoesNotSpendAndClosingPreservesRunningOrPausedTime()
        {
            EditorSceneManager.OpenScene(Scene); yield return new EnterPlayMode(); yield return null;
            var owner = Controller(); var sim = Value<TaskSimulation>(owner, "Simulation"); Place(sim, "A-1");
            sim.Paused = false; sim.Advance(2);
            double time = sim.GameTime, progress = sim.Tasks["A-1"].Progress, month = Value<double>(owner, "MonthTimeRemaining");
            int funds = Value<int>(owner, "Funds"), level = Value<int>(owner, "MemoryLevel");
            Click("UpgradeMemoryButton");
            CapturePopup("docs/images/memory-upgrade-popup.png", 1600, 1000);
            CapturePopup("docs/images/memory-upgrade-popup-small.png", 1280, 720);
            Assert.That(GameObject.Find("MemoryUpgradePopup"), Is.Not.Null); Assert.That(Value<bool>(owner, "IsModalOpen"), Is.True);
            Assert.That(Value<int>(owner, "Funds"), Is.EqualTo(funds)); Assert.That(Value<int>(owner, "MemoryLevel"), Is.EqualTo(level));
            Assert.That(Button("PauseButton").interactable, Is.False);
            Call(owner, "AdvanceGame", 100d); Call(owner, "TogglePause"); Call(owner, "WithdrawSelected"); Call(owner, "AddRequest");
            yield return new WaitForSecondsRealtime(.1f);
            Assert.That(sim.GameTime, Is.EqualTo(time)); Assert.That(sim.Tasks["A-1"].Progress, Is.EqualTo(progress));
            Assert.That(Value<double>(owner, "MonthTimeRemaining"), Is.EqualTo(month)); Assert.That(sim.Tasks.Count, Is.EqualTo(3));
            Assert.That(sim.Paused, Is.False); Assert.That(sim.Tasks["A-1"].State, Is.EqualTo(RequestState.Running));
            Click("CloseMemoryUpgradeButton"); Assert.That(Value<bool>(owner, "IsModalOpen"), Is.False);
            Assert.That(sim.Paused, Is.False); yield return null; Assert.That(sim.GameTime, Is.GreaterThan(time));
            sim.Paused = true; Click("UpgradeMemoryButton"); Click("CloseMemoryUpgradeButton");
            Assert.That(sim.Paused, Is.True); Assert.That(Value<int>(owner, "Funds"), Is.EqualTo(funds));
            yield return new ExitPlayMode();
        }
        [UnityTest] public IEnumerator ConfirmUnlocksPreviewedCellsAndChargesOnceWithoutChangingTasks()
        {
            EditorSceneManager.OpenScene(Scene); yield return new EnterPlayMode(); yield return null;
            var owner = Controller(); var sim = Value<TaskSimulation>(owner, "Simulation"); Place(sim, "A-1");
            sim.Paused = false; sim.Advance(4); sim.Paused = true;
            double progressBeforeUpgrade = sim.Tasks["A-1"].Progress;
            var task = sim.Tasks["A-1"]; var abilities = task.Abilities.ToArray(); var origin = task.Origin;
            int funds = Value<int>(owner, "Funds"), before = sim.OpenCellCount;
            var next = Value<object>(owner, "NextMemoryUpgrade"); int cost = (int)next.GetType().GetField("Cost").GetValue(next);
            int count = Value<int>(owner, "NextMemoryCellCount");
            var cells = Value<System.Collections.Generic.IReadOnlyList<Cell>>(owner, "NextMemoryCells").ToArray();
            Click("UpgradeMemoryButton"); yield return null;
            int colored = GameObject.Find("GridPreview").GetComponentsInChildren<Image>().Count(i => i.color.r > .4f);
            Assert.That(colored, Is.EqualTo(count)); Assert.That(GameObject.Find("UpgradeConditions").GetComponent<Text>().text, Does.Contain("满足"));
            Click("ConfirmMemoryUpgradeButton");
            Assert.That(Value<int>(owner, "Funds"), Is.EqualTo(funds)); Assert.That(sim.OpenCellCount, Is.EqualTo(before));
            MemoryUpgradeTestActions.Skip(owner);
            Assert.That(Value<int>(owner, "Funds"), Is.EqualTo(funds - cost)); Assert.That(sim.OpenCellCount, Is.EqualTo(before + count));
            Assert.That(cells.All(sim.IsOpen), Is.True); Assert.That(Value<bool>(owner, "IsMemoryUpgradeOpen"), Is.True);
            Assert.That(task.Progress, Is.EqualTo(progressBeforeUpgrade)); Assert.That(task.Origin, Is.EqualTo(origin));
            Assert.That(task.Abilities.ToArray(), Is.EqualTo(abilities)); Assert.That(sim.Occupancy.Count, Is.EqualTo(4));
            Assert.That(GameObject.Find("UpgradeFeedback").GetComponent<Text>().text, Does.Contain("内存升级至"));
            Assert.That(Button("ConfirmMemoryUpgradeButton").interactable, Is.False, "The next level needs more completed submissions.");
            Button("ConfirmMemoryUpgradeButton").onClick.Invoke(); Assert.That(Value<int>(owner, "Funds"), Is.EqualTo(funds - cost));
            Click("CloseMemoryUpgradeButton"); Assert.That(sim.Paused, Is.True);
            yield return new ExitPlayMode();
        }
        [UnityTest] public IEnumerator UnaffordableAndMaxLevelRemainViewableButCannotSpend()
        {
            EditorSceneManager.OpenScene(Scene); yield return new EnterPlayMode(); yield return null;
            var owner = Controller(); var field = owner.GetType().GetField("configuration", BindingFlags.Instance | BindingFlags.NonPublic);
            var original = (ScriptableObject)field.GetValue(owner); var config = Object.Instantiate(original);
            var memoryField = config.GetType().GetField("MemoryUpgrades");
            var memory = Object.Instantiate((ScriptableObject)memoryField.GetValue(config));
            try
            {
                config.GetType().GetField("InitialFunds").SetValue(config, 0); memoryField.SetValue(config, memory);
                field.SetValue(owner, config); Call(owner, "ResetSimulation");
                Assert.That(Button("UpgradeMemoryButton").interactable, Is.True); Click("UpgradeMemoryButton"); yield return null;
                Assert.That(Button("ConfirmMemoryUpgradeButton").interactable, Is.False);
                Assert.That(GameObject.Find("UpgradeConditions").GetComponent<Text>().text, Does.Contain("不足"));
                Button("ConfirmMemoryUpgradeButton").onClick.Invoke(); Assert.That(Value<int>(owner, "Funds"), Is.Zero);
                Call(owner, "AddFunds", 100); Assert.That(Button("ConfirmMemoryUpgradeButton").interactable, Is.True);
                Click("ConfirmMemoryUpgradeButton"); Assert.That(Value<int>(owner, "Funds"), Is.EqualTo(100));
                MemoryUpgradeTestActions.Skip(owner); Assert.That(Value<int>(owner, "Funds"), Is.Zero);
                int max = ((Array)memory.GetType().GetField("Levels").GetValue(memory)).Cast<object>()
                    .Max(o => (int)o.GetType().GetField("TargetLevel").GetValue(o));
                memory.GetType().GetField("InitialLevel").SetValue(memory, max); Call(owner, "ResetSimulation");
                Click("UpgradeMemoryButton"); yield return null; Assert.That(Button("ConfirmMemoryUpgradeButton").interactable, Is.False);
                Assert.That(GameObject.Find("UpgradeConditions").GetComponent<Text>().text, Does.Contain("全部升级已完成"));
                Button("ConfirmMemoryUpgradeButton").onClick.Invoke(); Assert.That(Value<int>(owner, "Funds"), Is.Zero);
                Click("CloseMemoryUpgradeButton"); Assert.That(Value<bool>(owner, "IsModalOpen"), Is.False);
            }
            finally { field.SetValue(owner, original); Object.DestroyImmediate(config); Object.DestroyImmediate(memory); }
            yield return new ExitPlayMode();
        }
        [UnityTest] public IEnumerator StoryReplyTakesPriorityAndRestartClosesUpgradeModal()
        {
            EditorSceneManager.OpenScene(Scene); yield return new EnterPlayMode(); yield return null;
            var owner = Controller(); var sim = Value<TaskSimulation>(owner, "Simulation"); Place(sim, "A-1");
            sim.Paused = false; sim.Advance(10); sim.Paused = true;
            Click("UpgradeMemoryButton"); sim.TrySubmit("A-1"); yield return null;
            Assert.That(Value<bool>(owner, "IsMemoryUpgradeOpen"), Is.False); Assert.That(Value<bool>(owner, "IsStoryOpen"), Is.True);
            Assert.That(GameObject.Find("MemoryUpgradePopup"), Is.Null); Assert.That(GameObject.Find("ConversationPopup"), Is.Not.Null);
            Call(owner, "OpenMemoryUpgrade"); Assert.That(Value<bool>(owner, "IsMemoryUpgradeOpen"), Is.False);
            Call(owner, "ChooseReply", 0); Click("UpgradeMemoryButton"); Call(owner, "ResetSimulation");
            Assert.That(Value<bool>(owner, "IsModalOpen"), Is.False); Assert.That(GameObject.Find("MemoryUpgradePopup"), Is.Null);
            Assert.That(Value<TaskSimulation>(owner, "Simulation").Paused, Is.True);
            yield return new ExitPlayMode();
        }
        [UnityTearDown] public IEnumerator Cleanup() { if (EditorApplication.isPlaying) yield return new ExitPlayMode(); }
        private static void CapturePopup(string file, int width, int height)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            var canvas = GameObject.Find("GameplayCanvas").GetComponent<Canvas>();
            var mode = canvas.renderMode; var originalCamera = canvas.worldCamera; float distance = canvas.planeDistance;
            var camera = new GameObject("MemoryPopupCapture").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.04f, .045f, .055f);
            camera.orthographic = true; camera.orthographicSize = 5; camera.nearClipPlane = .1f; camera.farClipPlane = 200;
            var texture = new RenderTexture(width, height, 24); camera.targetTexture = texture;
            var previous = RenderTexture.active; Texture2D png = null;
            try
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 10;
                Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = texture;
                png = new Texture2D(width, height, TextureFormat.RGB24, false);
                png.ReadPixels(new Rect(0, 0, width, height), 0, 0); png.Apply();
                Directory.CreateDirectory(Path.GetDirectoryName(file)); File.WriteAllBytes(file, png.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous; canvas.renderMode = mode; canvas.worldCamera = originalCamera; canvas.planeDistance = distance;
                camera.targetTexture = null; Object.DestroyImmediate(camera.gameObject); Object.DestroyImmediate(texture);
                if (png != null) Object.DestroyImmediate(png); Canvas.ForceUpdateCanvases();
            }
        }
    }
}
