using System;
using System.Collections;
using System.Linq;
using CallmeCatgirl.Gameplay.TaskGrid;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace CallmeCatgirl.Tests
{
    public sealed class CoreGameplaySceneTests
    {
        [UnityTest]
        public IEnumerator SavedSceneStartsAndUsesRealConfiguration()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity");
            yield return new EnterPlayMode();
            {
                var controller = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                    .Single(b => b.GetType().FullName == "CallmeCatgirl.Gameplay.CoreGameplayController");
                var sim = (TaskSimulation)controller.GetType().GetProperty("Simulation").GetValue(controller);
                Assert.That(sim, Is.Not.Null); Assert.That(sim.Tasks.Count, Is.EqualTo(3));
                Assert.That(sim.IsOpen(new Cell(0, 0)), Is.False);
                Assert.That(sim.TryPlace("A-1", FreeOrigin(sim, "A-1"), 0, out _), Is.True);
                Assert.That(sim.TryPlace("B-1", FreeOrigin(sim, "B-1"), 0, out _), Is.True);
                sim.Paused = false; sim.Advance(10); sim.Paused = true;
                Assert.That(sim.Tasks["A-1"].State, Is.EqualTo(RequestState.Ready));
                Assert.That(sim.Tasks["A-1"].GetAbility(AbilityKind.Writing), Is.EqualTo(12));
                Assert.That(sim.Tasks["A-1"].GetAbility(AbilityKind.Code), Is.EqualTo(3));
                Assert.That(sim.Tasks["A-1"].Result.Overall, Is.EqualTo(Quality.Complete));
                Assert.That(sim.Tasks["B-1"].Result.Overall, Is.EqualTo(Quality.BelowStandard));
                Assert.That(sim.TrySubmit("A-1"), Is.True);
                Assert.That(sim.TrySubmit("A-1"), Is.False);
            }
            yield return new ExitPlayMode();
        }

        [Test]
        public void SavedMemoryGridIsTheSingleSourceOfInitialAndUpgradeCells()
        {
            var memory = UnityEditor.AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/_Game/Data/TaskGrid/MemoryUpgradeConfiguration.asset");
            var config = UnityEditor.AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/_Game/Data/TaskGrid/CoreGameplayConfiguration.asset");
            Assert.That(config.GetType().GetField("MemoryUpgrades").GetValue(config), Is.SameAs(memory));
            Assert.That(config.GetType().GetField("OpenCells"), Is.Null, "Initial cells must not have a second configuration source.");
            int previousCount = 0;
            for (int level = 1; level <= 5; level++)
            {
                var cells = (System.Collections.Generic.List<Cell>)memory.GetType().GetMethod("GetOpenCells").Invoke(memory, new object[] { level });
                Assert.That(cells.Count, Is.GreaterThanOrEqualTo(previousCount)); previousCount = cells.Count;
                Assert.That(cells.Distinct().Count(), Is.EqualTo(cells.Count));
            }
            var sim = (TaskSimulation)config.GetType().GetMethod("BuildSimulation").Invoke(config, null);
            Assert.That(sim.IsOpen(new Cell(0, 0)), Is.False);
            var initial = (System.Collections.Generic.List<Cell>)memory.GetType().GetMethod("GetOpenCells").Invoke(memory, new object[] { 1 });
            Assert.That(sim.OpenCellCount, Is.EqualTo(initial.Count));
            Assert.That(initial.All(sim.IsOpen), Is.True);
            var editor = UnityEditor.Editor.CreateEditor(memory);
            try { Assert.That(editor.GetType().Name, Is.EqualTo("MemoryUpgradeConfigurationEditor")); }
            finally { UnityEngine.Object.DestroyImmediate(editor); }
        }

        [Test]
        public void PaintedMemoryCellChangesRuntimeLayoutAndSupportsUndo()
        {
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/_Game/Data/TaskGrid/MemoryUpgradeConfiguration.asset");
            var memory = UnityEngine.Object.Instantiate(asset);
            try
            {
                var serialized = new UnityEditor.SerializedObject(memory);
                var first = serialized.FindProperty("CellUnlockLevels").GetArrayElementAtIndex(0);
                int original = first.intValue;
                int before = ((System.Collections.Generic.List<Cell>)memory.GetType().GetMethod("GetOpenCells").Invoke(memory, new object[] { 1 })).Count;
                first.intValue = 1;
                serialized.ApplyModifiedProperties();
                var cells = (System.Collections.Generic.List<Cell>)memory.GetType().GetMethod("GetOpenCells").Invoke(memory, new object[] { 1 });
                Assert.That(cells.Count, Is.EqualTo(before + (original == 1 ? 0 : 1)));
                Assert.That(cells.Contains(new Cell(0, 0)), Is.True);
                UnityEditor.Undo.FlushUndoRecordObjects();
                UnityEditor.Undo.PerformUndo();
                serialized.Update();
                Assert.That(serialized.FindProperty("CellUnlockLevels").GetArrayElementAtIndex(0).intValue, Is.EqualTo(original));
                foreach (var level in new[] { 0, 5 })
                {
                    serialized.FindProperty("CellUnlockLevels").GetArrayElementAtIndex(0).intValue = level;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    cells = (System.Collections.Generic.List<Cell>)memory.GetType().GetMethod("GetOpenCells").Invoke(memory, new object[] { 1 });
                    Assert.That(cells.Contains(new Cell(0, 0)), Is.False);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(memory); }
        }
        private static Cell FreeOrigin(TaskSimulation sim, string id) => Enumerable.Range(0, sim.Width * sim.Height)
            .Select(i => new Cell(i % sim.Width, i / sim.Width)).First(c => sim.Preview(id, c, 0) == PlacementFailure.None);
        [UnityTearDown]
        public IEnumerator EnsurePlayModeIsClosed()
        {
            if (UnityEditor.EditorApplication.isPlaying) yield return new ExitPlayMode();
        }
    }
}
