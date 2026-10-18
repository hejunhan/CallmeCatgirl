using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
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
    public sealed class MemoryInstallationTests
    {
        private const string Core = "Assets/_Game/Scenes/Prototypes/CoreGameplay.unity";
        private string slotDirectory;
        private sealed class CompletionCounter { public int Count; public void Increment() { Count++; } }
        private static MonoBehaviour Owner() => Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Single(o => o.GetType().Name == "CoreGameplayController");
        private static MonoBehaviour Game() => Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None).Single(o => o.GetType().Name == "StandaloneMemoryInstall");
        private static T Get<T>(object o, string name) => (T)o.GetType().GetProperty(name).GetValue(o);
        private static object Call(object o, string name, params object[] args) => o.GetType().GetMethod(name).Invoke(o, args);
        private static object Field(object o, string name)
        {
            for (var type = o.GetType(); type != null; type = type.BaseType)
            { var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic); if (field != null) return field.GetValue(o); }
            throw new Exception("Missing field: " + name);
        }
        private static void Subscribe(object game, Action callback)
        {
            EventInfo found=null;
            for(var type=game.GetType();type!=null;type=type.BaseType)
            {
                found=type.GetEvent("InstallCompleted",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.DeclaredOnly);
                if(found!=null)break;
            }
            Assert.That(found,Is.Not.Null,"Missing completion event on "+game.GetType().AssemblyQualifiedName+" base="+game.GetType().BaseType);
            found.AddEventHandler(game,callback);
        }
        private static void ManualClock(object owner) => owner.GetType().GetProperty("ManualCommandClock").SetValue(owner, true);
        private static void Click(object game, string field)
        {
            var layers=Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).FirstOrDefault(o=>o.GetType().Name=="UIWindowLayers");
            if(layers!=null)Call(layers,"Synchronize");
            Canvas.ForceUpdateCanvases(); var button = (Button)Field(game, field);
            var r = (RectTransform)button.transform;
            var pointer = new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(null, r.TransformPoint(r.rect.center)) };
            var hits = new System.Collections.Generic.List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
            Assert.That(hits.Count, Is.GreaterThan(0), field);
            Assert.That(ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject), Is.EqualTo(button.gameObject), field + " top hit: " + hits[0].gameObject.name);
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler);
        }
        private static void Capture(string file)
        {
            var capture = typeof(SystemFeedbackTests).GetMethod("Capture", BindingFlags.NonPublic | BindingFlags.Static);
            capture.Invoke(null, new object[] { file }); capture.Invoke(null, new object[] { file });
        }
        [UnityTest] public IEnumerator UpgradeChargesOnceThenRequiresInstallationAndPreservesTime()
        {
            EditorSceneManager.OpenScene(Core); yield return new EnterPlayMode(); yield return null;
            var owner = Owner(); ManualClock(owner);
            var sim = Get<CallmeCatgirl.Gameplay.TaskGrid.TaskSimulation>(owner, "Simulation"); sim.Paused = false;
            int funds = Get<int>(owner, "Funds"), level = Get<int>(owner, "MemoryLevel"), cells = sim.OpenCellCount;
            var next = Get<object>(owner, "NextMemoryUpgrade"); int cost = (int)next.GetType().GetField("Cost").GetValue(next);
            double time = sim.GameTime, month = Get<double>(owner, "MonthTimeRemaining");
            Call(owner, "OpenMemoryUpgrade"); Call(owner, "UpgradeMemory");
            var game = Game(); Assert.That(Get<bool>(owner, "IsMemoryInstallOpen"), Is.True);
            Assert.That(Get<int>(owner, "MemoryLevel"), Is.EqualTo(level + 1)); Assert.That(Get<int>(owner, "Funds"), Is.EqualTo(funds - cost));
            Assert.That(sim.OpenCellCount, Is.GreaterThan(cells)); Assert.That(Get<bool>(owner, "IsModalOpen"), Is.True);
            Assert.That(Get<bool>(owner, "CanUpgradeMemory"), Is.False);
            Call(owner, "UpgradeMemory"); Call(owner, "CloseMemoryUpgrade"); Call(owner, "AdvanceGame", 100d); Call(owner, "TogglePause");
            Assert.That(sim.GameTime, Is.EqualTo(time)); Assert.That(Get<double>(owner, "MonthTimeRemaining"), Is.EqualTo(month)); Assert.That(sim.Paused, Is.False);
            Assert.That(Call(owner, "SaveRun"), Is.EqualTo(false)); Assert.That(Get<int>(owner, "Funds"), Is.EqualTo(funds - cost));
            ((Button)Field(game, "finishButton")).onClick.Invoke(); Assert.That(Get<bool>(game, "IsOpen"), Is.True, "Finish must not bypass the installation.");
            Capture("MemoryInstallInitial.png");
            Click(game, "pressRightButton"); Assert.That(((Button)Field(game, "finishButton")).gameObject.activeSelf, Is.False);
            Click(game, "pressLeftButton"); Click(game, "pressRightButton");
            Assert.That(((RectTransform)Field(game, "memoryStick")).localEulerAngles.z, Is.EqualTo(0).Within(.01));
            Assert.That(((RectTransform)Field(game, "leftClip")).localEulerAngles.z, Is.EqualTo(0).Within(.01));
            Assert.That(((RectTransform)Field(game, "rightClip")).localEulerAngles.z, Is.EqualTo(0).Within(.01));
            Capture("MemoryInstallReady.png");
            var completions = new CompletionCounter(); Subscribe(game, completions.Increment);
            Click(game, "finishButton"); ((Button)Field(game, "finishButton")).onClick.Invoke();
            Assert.That(completions.Count, Is.EqualTo(1)); Assert.That(Get<bool>(game, "IsOpen"), Is.False);
            Assert.That(Get<int>(owner, "Funds"), Is.EqualTo(funds - cost)); Assert.That(Get<bool>(owner, "IsMemoryUpgradeOpen"), Is.True);
            Call(owner, "CloseMemoryUpgrade"); Assert.That(Get<bool>(owner, "IsModalOpen"), Is.False);
            Call(owner, "AdvanceGame", .25d); Assert.That(sim.GameTime, Is.GreaterThan(time));
            yield return new ExitPlayMode();
        }
        [UnityTest] public IEnumerator SkipAndRestartDoNotDuplicatePaymentOrListeners()
        {
            EditorSceneManager.OpenScene(Core); yield return new EnterPlayMode(); yield return null;
            var owner = Owner(); ManualClock(owner); var game = Game(); int funds = Get<int>(owner, "Funds");
            Call(owner, "OpenMemoryUpgrade"); Call(owner, "UpgradeMemory"); yield return null; Click(game, "skipButton");
            Assert.That(Get<bool>(game, "IsOpen"), Is.False); int charged = Get<int>(owner, "Funds");
            ((Button)Field(game, "skipButton")).onClick.Invoke(); Assert.That(Get<int>(owner, "Funds"), Is.EqualTo(charged));
            owner.GetType().GetProperty("CompletedTasks").SetValue(owner, 100);
            Call(owner, "UpgradeMemory");
            Assert.That(Get<int>(owner, "MemoryLevel"), Is.EqualTo(3));
            Assert.That(Get<bool>(game, "IsOpen"), Is.True, "Every successful level must start a fresh installation.");
            Assert.That(((Button)Field(game, "finishButton")).gameObject.activeSelf, Is.False);
            Call(owner, "ResetSimulation"); ManualClock(owner);
            Assert.That(Get<int>(owner, "Funds"), Is.EqualTo(funds)); Assert.That(Get<bool>(game, "IsOpen"), Is.False);
            Call(owner, "OpenMemoryUpgrade"); Call(owner, "UpgradeMemory");
            Assert.That(((Button)Field(game, "finishButton")).gameObject.activeSelf, Is.False);
            var count = new CompletionCounter(); Subscribe(game, count.Increment);
            yield return null; Click(game, "skipButton"); Assert.That(count.Count, Is.EqualTo(1));
            Call(owner, "ResetSimulation"); Assert.That(Get<bool>(owner, "IsModalOpen"), Is.False);
            yield return new ExitPlayMode();
        }
        [UnityTest] public IEnumerator FailedUpgradeDoesNotOpenInstallation()
        {
            EditorSceneManager.OpenScene(Core); yield return new EnterPlayMode(); yield return null;
            var owner = Owner(); ManualClock(owner); int level = Get<int>(owner, "MemoryLevel");
            Call(Get<object>(owner, "Economy"), "TrySpendFunds", Get<int>(owner, "Funds"));
            Call(owner, "OpenMemoryUpgrade"); Call(owner, "UpgradeMemory");
            Assert.That(Get<bool>(owner, "IsMemoryInstallOpen"), Is.False); Assert.That(Get<int>(owner, "MemoryLevel"), Is.EqualTo(level));
            yield return new ExitPlayMode();
        }
        [UnityTest] public IEnumerator RestoreClearsInstallationOnlyAfterSuccessfulRestore()
        {
            EditorSceneManager.OpenScene(Core); yield return new EnterPlayMode(); yield return null;
            var owner = Owner(); ManualClock(owner); string slot = "memory-install-test-" + Guid.NewGuid().ToString("N");
            Call(owner, "ConfigureSaveSlot", slot); slotDirectory = Path.GetDirectoryName(Get<string>(owner, "SavePath"));
            Assert.That(Call(owner, "SaveRun"), Is.EqualTo(true)); int level = Get<int>(owner, "MemoryLevel");
            Call(owner, "OpenMemoryUpgrade"); Call(owner, "UpgradeMemory");
            Call(owner, "ConfigureSaveSlot", slot + "-missing"); Assert.That(Call(owner, "ContinueRun"), Is.EqualTo(false));
            Assert.That(Get<bool>(owner, "IsMemoryInstallOpen"), Is.True);
            Call(owner, "ConfigureSaveSlot", slot); Assert.That(Call(owner, "ContinueRun"), Is.EqualTo(true));
            Assert.That(Get<bool>(owner, "IsMemoryInstallOpen"), Is.False); Assert.That(Get<int>(owner, "MemoryLevel"), Is.EqualTo(level));
            yield return new ExitPlayMode();
        }
        [UnityTest] public IEnumerator StandaloneSceneUsesTheSameInteractionWithoutMissingReferences()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/MemoryInstallScene.unity"); yield return new EnterPlayMode(); yield return null;
            var game = Game(); Assert.That(Get<bool>(game, "IsOpen"), Is.True); Assert.That(Field(game, "tipText"), Is.Not.Null);
            Click(game, "pressLeftButton"); Click(game, "pressRightButton"); yield return null; Click(game, "finishButton");
            Assert.That(Get<bool>(game, "IsOpen"), Is.False); Assert.That(game.gameObject.activeInHierarchy, Is.True);
            yield return new ExitPlayMode();
        }
        [Test] public void AuthoredPrefabContainsOneBoundControllerAndNoMissingScripts()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/UI/CoreGameplay/MemoryInstallGamePanel.prefab");
            Assert.That(prefab, Is.Not.Null);
            var games = prefab.GetComponentsInChildren<MonoBehaviour>(true).Where(o => o != null && o.GetType().Name == "StandaloneMemoryInstall").ToArray();
            Assert.That(games.Length, Is.EqualTo(1));
            foreach (var t in prefab.GetComponentsInChildren<Transform>(true)) Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject), Is.Zero);
            foreach (string field in new[] { "gamePanel", "pressLeftButton", "pressRightButton", "skipButton", "finishButton", "tipText", "memoryStick", "leftClip", "rightClip" })
                Assert.That(Field(games[0], field), Is.Not.Null, field);
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if (EditorApplication.isPlaying) yield return new ExitPlayMode();
            if (slotDirectory != null && Directory.Exists(slotDirectory))
            {
                Assert.That(Path.GetFileName(slotDirectory), Does.StartWith("memory-install-test-"));
                Directory.Delete(slotDirectory, true); slotDirectory = null;
            }
        }
    }
}
