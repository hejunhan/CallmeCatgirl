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
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace CallmeCatgirl.Tests
{
    public sealed class MemoryPartitionTests
    {
        private static readonly Cell[] Line = { new Cell(0, 0), new Cell(1, 0) };
        private static ModelDefinition Model() => new ModelDefinition("test", 3, new Dictionary<AbilityKind, double> { { AbilityKind.Writing, 4 } });
        private static TaskSimulation Simulation(int[] bags = null, IEnumerable<Cell> open = null)
            => new TaskSimulation(4, 4, open ?? Enumerable.Range(0, 16).Select(i => new Cell(i % 4, i / 4)), Model(), bags);
        private static TaskInstance Add(TaskSimulation sim, string id = "test", Cell[] shape = null)
        {
            var def = new TaskDefinition(id, id, shape ?? Line, 100, 10, 60, 100, Array.Empty<AbilityRequirement>());
            Assert.That(sim.TryCreate(id, id, "1", def, out var task), Is.True); return task;
        }
        private static int[] LeftRight() => Enumerable.Range(0, 16).Select(i => i % 4 < 2 ? 0 : 1).ToArray();

        [Test] public void EveryOccupiedCellMustStayInOneBagButWholeTaskMayMoveBetweenBags()
        {
            var sim = Simulation(LeftRight()); var task = Add(sim);
            Assert.That(sim.Preview(task.Id, new Cell(1, 0), 0), Is.EqualTo(PlacementFailure.CrossesPartition));
            Assert.That(sim.TryPlace(task.Id, new Cell(1, 0), 0, out var failure), Is.False);
            Assert.That(failure, Is.EqualTo(PlacementFailure.CrossesPartition)); Assert.That(task.State, Is.EqualTo(RequestState.Waiting));
            Assert.That(sim.Occupancy, Is.Empty);
            Assert.That(sim.TryPlace(task.Id, new Cell(0, 0), 0, out _), Is.True);
            Assert.That(sim.TryPlace(task.Id, new Cell(2, 0), 0, out _), Is.True);
            Assert.That(sim.TaskAt(new Cell(0, 0)), Is.Null); Assert.That(sim.TaskAt(new Cell(2, 0)), Is.EqualTo(task.Id));
        }
        [Test] public void CrossPartitionMoveAndRotationAreAtomicAndKeepProgressAbilitiesAndEvents()
        {
            var sim = Simulation(LeftRight()); var task = Add(sim);
            Assert.That(sim.TryPlace(task.Id, new Cell(1, 1), 1, out _), Is.True); sim.Advance(4); sim.DrainEvents();
            Assert.That(sim.TryPlace(task.Id, task.Origin, 0, out var failure), Is.False);
            Assert.That(failure, Is.EqualTo(PlacementFailure.CrossesPartition));
            Assert.That(sim.TryPlace(task.Id, new Cell(1, 0), 0, out _), Is.False);
            Assert.That(task.Origin, Is.EqualTo(new Cell(1, 1))); Assert.That(task.Rotation, Is.EqualTo(1));
            Assert.That(task.Progress, Is.EqualTo(40)); Assert.That(task.GetAbility(AbilityKind.Writing), Is.EqualTo(4));
            Assert.That(sim.Occupancy.Keys, Is.EquivalentTo(new[] { new Cell(1, 1), new Cell(1, 2) })); Assert.That(sim.DrainEvents(), Is.Empty);
        }
        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
        public void RotatedShapesNeverCrossHorizontalOrVerticalPartitions(int rotation)
        {
            var bags = Enumerable.Range(0, 16).Select(i => (i % 4 < 2 ? 0 : 1) + (i / 4 < 2 ? 0 : 2)).ToArray();
            var sim = Simulation(bags); var task = Add(sim, shape: new[] { new Cell(0, 0), new Cell(1, 0), new Cell(0, 1) });
            Assert.That(sim.Preview(task.Id, new Cell(0, 0), rotation), Is.EqualTo(PlacementFailure.None));
            Assert.That(sim.Preview(task.Id, new Cell(1, 1), rotation), Is.EqualTo(PlacementFailure.CrossesPartition));
        }
        [Test] public void IrregularBagChecksShapeCellsRatherThanItsBoundingRectangle()
        {
            var bags = new int[16]; bags[5] = 1;
            var sim = Simulation(bags); var task = Add(sim, shape: new[] { new Cell(0, 0), new Cell(1, 0), new Cell(0, 1) });
            Assert.That(sim.TryPlace(task.Id, new Cell(0, 0), 0, out _), Is.True);
        }
        [Test] public void UnlockingAnotherBagKeepsPartitionAndRunningTaskAndAllowsNewTaskInsideIt()
        {
            var sim = Simulation(LeftRight(), new[] { new Cell(0, 0), new Cell(1, 0) }); var task = Add(sim);
            Assert.That(sim.TryPlace(task.Id, new Cell(0, 0), 0, out _), Is.True); sim.Advance(4);
            Assert.That(sim.TryUnlockCells(new[] { new Cell(2, 0), new Cell(3, 0) }), Is.True);
            Assert.That(task.Progress, Is.EqualTo(40)); Assert.That(sim.Occupancy.Count, Is.EqualTo(2));
            var next = Add(sim, "next");
            Assert.That(sim.Preview(next.Id, new Cell(1, 0), 0), Is.EqualTo(PlacementFailure.Overlap));
            Assert.That(sim.TryWithdraw(task.Id), Is.True);
            Assert.That(sim.Preview(next.Id, new Cell(1, 0), 0), Is.EqualTo(PlacementFailure.CrossesPartition));
            Assert.That(sim.TryPlace(next.Id, new Cell(2, 0), 0, out _), Is.True);
        }
        [Test] public void OldConstructorUsesOneBagAndInputArrayCannotChangeLivePartitions()
        {
            var old = Simulation(); var task = Add(old);
            Assert.That(old.TryPlace(task.Id, new Cell(1, 0), 0, out _), Is.True);
            var bags = LeftRight(); var sim = Simulation(bags); bags[1] = 1; bags[2] = 0;
            Assert.That(sim.CompartmentAt(new Cell(1, 0)), Is.Zero); Assert.That(sim.CompartmentAt(new Cell(2, 0)), Is.EqualTo(1));
            Assert.Throws<ArgumentException>(() => Simulation(new int[3]));
            bags = new int[16]; bags[0] = -1; Assert.Throws<ArgumentException>(() => Simulation(bags));
            Assert.Throws<ArgumentOutOfRangeException>(() => sim.CompartmentAt(new Cell(4, 0)));
        }
        [Test] public void ConfigurationSupportsOldAssetsAndRejectsWrongSizeOrNegativeIds()
        {
            var type = Type.GetType("CallmeCatgirl.Gameplay.MemoryUpgradeConfiguration, Assembly-CSharp", true);
            var cfg = ScriptableObject.CreateInstance(type);
            try
            {
                type.GetField("CellCompartments").SetValue(cfg, Array.Empty<int>());
                Assert.That((int[])type.GetMethod("GetCompartments").Invoke(cfg, null), Is.EqualTo(new int[64]));
                type.GetField("CellCompartments").SetValue(cfg, new int[2]);
                Assert.That(Assert.Throws<TargetInvocationException>(() => type.GetMethod("GetCompartments").Invoke(cfg, null)).InnerException, Is.TypeOf<InvalidOperationException>());
                var bags = new int[64]; bags[0] = -1; type.GetField("CellCompartments").SetValue(cfg, bags);
                Assert.That(Assert.Throws<TargetInvocationException>(() => type.GetMethod("GetCompartments").Invoke(cfg, null)).InnerException, Is.TypeOf<InvalidOperationException>());
            }
            finally { Object.DestroyImmediate(cfg); }
        }

        private static object Call(object o, string name, params object[] args) => o.GetType().GetMethod(name).Invoke(o, args);
        private static T Value<T>(object o, string name) => (T)o.GetType().GetProperty(name).GetValue(o);
        private static void Set(object o, string name, object value) => o.GetType().GetField(name).SetValue(o, value);
        [Test] public void PaintedCompartmentChangesPlacementAndSupportsUndoWithoutChangingUnlockLevels()
        {
            var original = AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/_Game/Data/TaskGrid/CoreGameplayConfiguration.asset");
            var config = Object.Instantiate(original);
            var memory = Object.Instantiate((ScriptableObject)config.GetType().GetField("MemoryUpgrades").GetValue(config));
            try
            {
                Set(config, "MemoryUpgrades", memory);
                var sim = (TaskSimulation)Call(config, "BuildSimulation"); var task = Add(sim);
                Assert.That(sim.Preview(task.Id, new Cell(2, 2), 0), Is.EqualTo(PlacementFailure.None));
                var oldUnlocks = ((int[])memory.GetType().GetField("CellUnlockLevels").GetValue(memory)).ToArray();
                var data = new SerializedObject(memory); data.FindProperty("CellCompartments").GetArrayElementAtIndex(3 + 2 * sim.Width).intValue = 9;
                data.ApplyModifiedProperties();
                var changed = (TaskSimulation)Call(config, "BuildSimulation"); var next = Add(changed);
                Assert.That(changed.Preview(next.Id, new Cell(2, 2), 0), Is.EqualTo(PlacementFailure.CrossesPartition));
                Assert.That(memory.GetType().GetField("CellUnlockLevels").GetValue(memory), Is.EqualTo(oldUnlocks));
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
                var restored = (TaskSimulation)Call(config, "BuildSimulation"); var afterUndo = Add(restored);
                Assert.That(restored.Preview(afterUndo.Id, new Cell(2, 2), 0), Is.EqualTo(PlacementFailure.None));
            }
            finally { Object.DestroyImmediate(config); Object.DestroyImmediate(memory); }
        }
        private static Graphic Partition(string name) => GameObject.Find(name).GetComponent<Graphic>();
        private static int VertexCount(Graphic graphic)
        {
            using (var mesh = new VertexHelper())
            {
                graphic.GetType().GetMethod("OnPopulateMesh", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(VertexHelper) }, null).Invoke(graphic, new object[] { mesh });
                return mesh.currentVertCount;
            }
        }
        [UnityTest] public IEnumerator SceneDragPreviewRejectsCrossBagAndUpgradeShowsPartitionsWithoutBlockingClicks()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity"); yield return new EnterPlayMode(); yield return null;
            var owner = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Single(o => o.GetType().FullName == "CallmeCatgirl.Gameplay.CoreGameplayController");
            var field = owner.GetType().GetField("configuration", BindingFlags.Instance | BindingFlags.NonPublic);
            var original = (ScriptableObject)field.GetValue(owner); var config = Object.Instantiate(original);
            var memory = Object.Instantiate((ScriptableObject)config.GetType().GetField("MemoryUpgrades").GetValue(config));
            try
            {
                Set(config, "MemoryUpgrades", memory); Set(config, "Story", null); Set(config, "ModelPoints", new Vector4(4, 1, 1, 0));
                Set(memory, "CellCompartments", Enumerable.Range(0, 64).Select(i => i / 8 < 5 ? 0 : 1).ToArray());
                foreach (object step in (Array)memory.GetType().GetField("Levels").GetValue(memory))
                { Set(step, "Cost", 0); Set(step, "RequiredCompletedTasks", 0); }
                field.SetValue(owner, config); Call(owner, "ResetSimulation");
                var sim = Value<TaskSimulation>(owner, "Simulation");
                var def = new TaskDefinition("line", "line", Line, 100, 10, 60, 100, Array.Empty<AbilityRequirement>());
                Assert.That(sim.TryCreate("line", "line", "1", def, out var task), Is.True);
                Assert.That(sim.Preview(task.Id, new Cell(2, 4), 1), Is.EqualTo(PlacementFailure.LockedCell));
                Call(owner, "OpenMemoryUpgrade"); Call(owner, "UpgradeMemory"); Call(owner, "UpgradeMemory"); yield return null;
                Assert.That(sim.Preview(task.Id, new Cell(2, 4), 1), Is.EqualTo(PlacementFailure.CrossesPartition));
                Assert.That(Partition("PreviewPartitions").raycastTarget, Is.False);
                Assert.That(VertexCount(Partition("PreviewPartitions")), Is.GreaterThan(0));
                Assert.That(GameObject.Find("PreviewLegend").GetComponent<Text>().text, Does.Contain("隔板"));
                Assert.That(GameObject.Find("ConfirmMemoryUpgradeButton").GetComponent<Button>().interactable, Is.True);
                Capture("PartitionPopup.png");
                Call(owner, "CloseMemoryUpgrade"); yield return null;
                Assert.That(Partition("BoardPartitions").raycastTarget, Is.False);
                int separated = VertexCount(Partition("BoardPartitions"));
                // 同一开放布局，取消内部隔板后应少一条完整的8格横线。
                var flat = new TaskSimulation(sim.Width, sim.Height, Enumerable.Range(0, 64).Select(i => new Cell(i % 8, i / 8)).Where(sim.IsOpen), sim.Model);
                Call(Partition("BoardPartitions"), "Show", flat, null);
                Assert.That(separated - VertexCount(Partition("BoardPartitions")), Is.EqualTo(8 * 4));
                var view = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Single(o => o.GetType().FullName == "CallmeCatgirl.UI.CoreGameplayCanvasView");
                var board = Value<RectTransform>(view, "Board");
                var point = board.TransformPoint(new Vector3(board.rect.xMin + 2.5f * board.rect.width / 8, board.rect.yMax - 4.5f * board.rect.height / 8));
                var pointer = new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(null, point) };
                Call(view, "BeginDrag", task.Id, pointer, false); Call(view, "RotateDrag"); Call(view, "EndDrag", pointer);
                Assert.That(task.State, Is.EqualTo(RequestState.Waiting)); Assert.That(Value<string>(owner, "Message"), Does.Contain("跨越隔板"));
                Assert.That(sim.TryPlace(task.Id, new Cell(2, 5), 0, out _), Is.True);
                sim.Paused = false; sim.Advance(4); sim.Paused = true;
                Call(owner, "OpenMemoryUpgrade"); Call(owner, "UpgradeMemory");
                Assert.That(task.Progress, Is.EqualTo(40)); Assert.That(sim.CompartmentAt(task.Origin), Is.EqualTo(1));
                Assert.That(sim.TaskAt(new Cell(3, 5)), Is.EqualTo(task.Id));
                Call(owner, "CloseMemoryUpgrade"); yield return null; Capture("PartitionBoard.png");
            }
            finally { field.SetValue(owner, original); Object.DestroyImmediate(config); Object.DestroyImmediate(memory); }
            yield return new ExitPlayMode();
        }
        [UnityTearDown] public IEnumerator ExitIfNeeded() { if (EditorApplication.isPlaying) yield return new ExitPlayMode(); }
        private static void Capture(string file)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            var canvas = GameObject.Find("GameplayCanvas").GetComponent<Canvas>();
            var mode = canvas.renderMode; var originalCamera = canvas.worldCamera; float distance = canvas.planeDistance;
            var camera = new GameObject("PartitionCapture").AddComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = 5; camera.nearClipPlane = .1f; camera.farClipPlane = 200;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.04f, .045f, .055f);
            var texture = new RenderTexture(1600, 1000, 24); camera.targetTexture = texture;
            var previous = RenderTexture.active; Texture2D png = null;
            try
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 10;
                Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = texture;
                png = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
                png.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0); png.Apply();
                System.IO.Directory.CreateDirectory("Logs/MemoryPartitions"); System.IO.File.WriteAllBytes("Logs/MemoryPartitions/" + file, png.EncodeToPNG());
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
