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
            var initial = (System.Collections.Generic.List<Cell>)memory.GetType().GetMethod("GetOpenCells").Invoke(memory, new object[] { 0 });
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
