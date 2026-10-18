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
    public sealed class MemoryAbilityGrowthTests
    {
        private MonoBehaviour owner;
        private ScriptableObject original, config, memory;
        private object originalTraining;
        private static object Call(object o, string name, params object[] args) => o.GetType().GetMethod(name).Invoke(o, args);
        private static T Value<T>(object o, string name) => (T)o.GetType().GetProperty(name).GetValue(o);
        private static void Set(object o, string name, object value) => o.GetType().GetField(name).SetValue(o, value);
        private static Button Button(string name) => GameObject.Find(name).GetComponent<Button>();
        private object FirstStep => ((Array)memory.GetType().GetField("Levels").GetValue(memory)).GetValue(0);
        private void Prepare()
        {
            owner = Object.FindFirstObjectByType<MonoBehaviour>(FindObjectsInactive.Include);
            owner = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Single(o => o.GetType().FullName == "CallmeCatgirl.Gameplay.CoreGameplayController");
            var field = owner.GetType().GetField("configuration", BindingFlags.Instance | BindingFlags.NonPublic);
            original = (ScriptableObject)field.GetValue(owner); config = Object.Instantiate(original);
            memory = Object.Instantiate((ScriptableObject)config.GetType().GetField("MemoryUpgrades").GetValue(config));
            Set(config, "MemoryUpgrades", memory); Set(config, "Story", null); Set(config, "ModelPoints", new Vector4(4, 1, 1, 0));
            Set(config, "TaskGeneration", null); Set(config, "OrdinaryDialoguePool", null); Set(config, "OpeningBalance", null);
            Set(config, "InitialFunds", 1500); Set(memory, "InitialLevel", 1);
            Set(FirstStep, "AbilityPoints", 3); Set(FirstStep, "RequiredCompletedTasks", 0); Set(FirstStep, "Cost", 100);
            // These scenarios exercise the manual point-allocation mode, which is disabled while training is bound.
            var trainingField = owner.GetType().GetField("training", BindingFlags.Instance | BindingFlags.NonPublic);
            originalTraining = trainingField.GetValue(owner); trainingField.SetValue(owner, null);
            field.SetValue(owner, config); Call(owner, "ResetSimulation");
        }
        private static void Place(TaskSimulation sim, string id)
        {
            var cell = Enumerable.Range(0, sim.Width * sim.Height).Select(i => new Cell(i % sim.Width, i / sim.Width)).First(c => sim.Preview(id, c, 0) == PlacementFailure.None);
            Assert.That(sim.TryPlace(id, cell, 0, out _), Is.True);
        }
        private static void Click(string name)
        {
            Canvas.ForceUpdateCanvases(); var button = Button(name); var rect = (RectTransform)button.transform;
            var pointer = new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center)) };
            var hits = new System.Collections.Generic.List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
            Assert.That(ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject), Is.EqualTo(button.gameObject));
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler);
        }
        [Test] public void ConfigurationOnlyRequiresRewardPoints()
        {
            var type = Type.GetType("CallmeCatgirl.Gameplay.MemoryUpgradeConfiguration+UpgradeStep, Assembly-CSharp", true);
            Assert.That(type.GetField("AbilityPoints"), Is.Not.Null); Assert.That(type.GetField("AbilityGains"), Is.Null);
            Assert.That(type.GetField("GrowthAbility"), Is.Null);
        }
        [UnityTest] public IEnumerator UpgradeAwardsPointsAndPlayerDistributesAcrossAbilitiesWithoutExtraMoney()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity"); yield return new EnterPlayMode(); yield return null; Prepare();
            var sim = Value<TaskSimulation>(owner, "Simulation"); var old = sim.Model; Call(owner, "OpenMemoryUpgrade"); yield return null;
            Assert.That(Button("AllocateWritingButton").interactable, Is.False); Click("ConfirmMemoryUpgradeButton");
            Assert.That(Value<int>(owner, "AvailableAbilityPoints"), Is.Zero);
            Assert.That(Value<int>(owner, "Funds"), Is.EqualTo(1500));
            MemoryUpgradeTestActions.Skip(owner);
            Assert.That(Value<int>(owner, "AvailableAbilityPoints"), Is.EqualTo(3)); Assert.That(sim.Model, Is.SameAs(old));
            Assert.That(Value<int>(owner, "Funds"), Is.EqualTo(1400)); Assert.That(Value<int>(owner, "MemoryLevel"), Is.EqualTo(2));
            Assert.That(GameObject.Find("AbilityPointsLabel").GetComponent<Text>().text, Does.Contain("3"));
            Click("AllocateWritingButton"); Click("AllocateCodeButton"); Click("AllocateReasoningButton");
            Assert.That(sim.Model.Points[AbilityKind.Writing], Is.EqualTo(5)); Assert.That(sim.Model.Points[AbilityKind.Code], Is.EqualTo(2));
            Assert.That(sim.Model.Points[AbilityKind.Reasoning], Is.EqualTo(2)); Assert.That(sim.Model.Points[AbilityKind.Retrieval], Is.EqualTo(0));
            Assert.That(Value<int>(owner, "AvailableAbilityPoints"), Is.Zero); Assert.That(Value<int>(owner, "Funds"), Is.EqualTo(1400));
            Assert.That(Button("AllocateWritingButton").interactable, Is.False); Button("AllocateWritingButton").onClick.Invoke();
            Assert.That(sim.Model.Points[AbilityKind.Writing], Is.EqualTo(5)); Call(owner, "UpgradeMemory");
            Assert.That(Value<int>(owner, "AvailableAbilityPoints"), Is.Zero); Assert.That(Value<int>(owner, "Funds"), Is.EqualTo(1400));
            yield return new ExitPlayMode();
        }
        [UnityTest] public IEnumerator UnspentPointsPersistAcrossCloseAndLevelsAndCanBeSpentAtMaximumLevel()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity"); yield return new EnterPlayMode(); yield return null; Prepare();
            var levels = (Array)memory.GetType().GetField("Levels").GetValue(memory);
            for (int i = 1; i < levels.Length; i++) { Set(levels.GetValue(i), "AbilityPoints", 2); Set(levels.GetValue(i), "RequiredCompletedTasks", 0); Set(levels.GetValue(i), "Cost", 0); }
            Call(owner, "OpenMemoryUpgrade"); Call(owner, "UpgradeMemory"); MemoryUpgradeTestActions.Skip(owner); Call(owner, "CloseMemoryUpgrade");
            Assert.That(Value<int>(owner, "AvailableAbilityPoints"), Is.EqualTo(3)); Call(owner, "AllocateAbility", AbilityKind.Writing);
            Assert.That(Value<int>(owner, "AvailableAbilityPoints"), Is.EqualTo(3));
            Call(owner, "OpenMemoryUpgrade"); Call(owner, "AllocateAbility", AbilityKind.Writing); Call(owner, "AllocateAbility", AbilityKind.Writing);
            Assert.That(Value<TaskSimulation>(owner, "Simulation").Model.Points[AbilityKind.Writing], Is.EqualTo(6));
            for (int i = 0; i < 3; i++) { Call(owner, "UpgradeMemory"); MemoryUpgradeTestActions.Skip(owner); }
            Assert.That(Value<int>(owner, "MemoryLevel"), Is.EqualTo(5)); Assert.That(Value<int>(owner, "AvailableAbilityPoints"), Is.EqualTo(7));
            Assert.That(Button("ConfirmMemoryUpgradeButton").interactable, Is.False); Assert.That(Button("AllocateRetrievalButton").interactable, Is.True);
            yield return null; Click("AllocateRetrievalButton"); Assert.That(Value<TaskSimulation>(owner, "Simulation").Model.Points[AbilityKind.Retrieval], Is.EqualTo(1));
            Assert.That(Value<int>(owner, "AvailableAbilityPoints"), Is.EqualTo(6));
            yield return new ExitPlayMode();
        }
        [UnityTest] public IEnumerator AllocationPreservesTimeProgressOldAbilitiesAndCycleRemainder()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity"); yield return new EnterPlayMode(); yield return null; Prepare();
            var sim = Value<TaskSimulation>(owner, "Simulation"); Place(sim, "A-1"); sim.Paused = false; sim.Advance(4);
            var task = sim.Tasks["A-1"]; var origin = task.Origin; double time = sim.GameTime;
            Call(owner, "OpenMemoryUpgrade"); Call(owner, "UpgradeMemory"); MemoryUpgradeTestActions.Skip(owner); Call(owner, "AllocateAbility", AbilityKind.Writing);
            Assert.That(sim.Paused, Is.False); Assert.That(sim.GameTime, Is.EqualTo(time)); Assert.That(task.Progress, Is.EqualTo(40));
            Assert.That(task.GetAbility(AbilityKind.Writing), Is.EqualTo(4)); Assert.That(task.Origin, Is.EqualTo(origin));
            Call(owner, "AdvanceGame", 50d); Assert.That(sim.GameTime, Is.EqualTo(time));
            Call(owner, "CloseMemoryUpgrade"); sim.Advance(2); sim.Paused = true;
            Assert.That(task.Progress, Is.EqualTo(60)); Assert.That(task.GetAbility(AbilityKind.Writing), Is.EqualTo(9));
            Place(sim, "B-1"); sim.Paused = false; sim.Advance(3); sim.Paused = true;
            Assert.That(sim.Tasks["B-1"].GetAbility(AbilityKind.Writing), Is.EqualTo(5));
            Call(owner, "ResetSimulation"); Assert.That(Value<int>(owner, "AvailableAbilityPoints"), Is.Zero);
            Assert.That(Value<TaskSimulation>(owner, "Simulation").Model.Points[AbilityKind.Writing], Is.EqualTo(4));
            yield return new ExitPlayMode();
        }
        [UnityTest] public IEnumerator FailedOrInvalidRewardsDoNotGrantPointsOrSpendMoney()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity"); yield return new EnterPlayMode(); yield return null; Prepare();
            var sim = Value<TaskSimulation>(owner, "Simulation"); int cells = sim.OpenCellCount; var old = sim.Model;
            Set(FirstStep, "AbilityPoints", -1); Call(owner, "UpgradeMemory"); Assert.That(Value<bool>(owner, "CanUpgradeMemory"), Is.False);
            Assert.That(Value<int>(owner, "AvailableAbilityPoints"), Is.Zero); Assert.That(Value<int>(owner, "Funds"), Is.EqualTo(1500)); Assert.That(sim.OpenCellCount, Is.EqualTo(cells));
            Set(FirstStep, "AbilityPoints", 3); Set(FirstStep, "Cost", 1600); Call(owner, "UpgradeMemory"); Assert.That(Value<int>(owner, "AvailableAbilityPoints"), Is.Zero);
            Set(FirstStep, "Cost", 100); Call(owner, "OpenMemoryUpgrade"); Call(owner, "UpgradeMemory");
            MemoryUpgradeTestActions.Skip(owner);
            Call(owner, "AllocateAbility", (AbilityKind)999); Assert.That(Value<int>(owner, "AvailableAbilityPoints"), Is.EqualTo(3)); Assert.That(sim.Model, Is.SameAs(old));
            yield return new ExitPlayMode();
        }
        [UnityTest] public IEnumerator ZeroRewardOnlyExpandsMemoryAndPausedStateStaysPaused()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity"); yield return new EnterPlayMode(); yield return null; Prepare();
            Set(FirstStep, "AbilityPoints", 0); var sim = Value<TaskSimulation>(owner, "Simulation"); var model = sim.Model; int cells = sim.OpenCellCount;
            Call(owner, "OpenMemoryUpgrade"); Call(owner, "UpgradeMemory");
            MemoryUpgradeTestActions.Skip(owner);
            Assert.That(sim.OpenCellCount, Is.GreaterThan(cells)); Assert.That(sim.Model, Is.SameAs(model)); Assert.That(sim.Paused, Is.True);
            Assert.That(Value<int>(owner, "AvailableAbilityPoints"), Is.Zero); Assert.That(Value<int>(owner, "Funds"), Is.EqualTo(1400));
            yield return new ExitPlayMode();
        }
        [UnityTest] public IEnumerator PointBalanceOverflowRejectsUpgradeBeforeUnlockOrCharge()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity"); yield return new EnterPlayMode(); yield return null; Prepare();
            owner.GetType().GetProperty("AvailableAbilityPoints").SetValue(owner, int.MaxValue);
            var sim = Value<TaskSimulation>(owner, "Simulation"); int cells = sim.OpenCellCount;
            Call(owner, "UpgradeMemory"); Assert.That(Value<int>(owner, "AvailableAbilityPoints"), Is.EqualTo(int.MaxValue));
            Assert.That(Value<int>(owner, "Funds"), Is.EqualTo(1500)); Assert.That(sim.OpenCellCount, Is.EqualTo(cells));
            yield return new ExitPlayMode();
        }
        [TearDown] public void Restore()
        {
            if (owner != null)
            {
                owner.GetType().GetField("configuration", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(owner, original);
                owner.GetType().GetField("training", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(owner, originalTraining);
            }
            if (config != null) Object.DestroyImmediate(config); if (memory != null) Object.DestroyImmediate(memory);
        }
        [UnityTearDown] public IEnumerator ExitIfNeeded() { if (EditorApplication.isPlaying) yield return new ExitPlayMode(); }
    }
}
