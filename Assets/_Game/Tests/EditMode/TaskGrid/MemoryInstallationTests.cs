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
        [UnityTest] public IEnumerator UpgradeStartsMinigameAndFinishingAppliesChargeLevelCellsAndPointsExactlyOnce()
        {
            EditorSceneManager.OpenScene(Core); yield return new EnterPlayMode(); yield return null;
            var owner = Owner(); ManualClock(owner);
            var sim = Get<CallmeCatgirl.Gameplay.TaskGrid.TaskSimulation>(owner, "Simulation"); sim.Paused = false;
            int funds = Get<int>(owner, "Funds"), level = Get<int>(owner, "MemoryLevel"), cells = sim.OpenCellCount, points = Get<int>(owner, "AvailableAbilityPoints");
            var next = Get<object>(owner, "NextMemoryUpgrade");
            int cost = (int)next.GetType().GetField("Cost").GetValue(next), reward = (int)next.GetType().GetField("AbilityPoints").GetValue(next);
            double time = sim.GameTime, month = Get<double>(owner, "MonthTimeRemaining");
            Call(owner, "OpenMemoryUpgrade"); Call(owner, "UpgradeMemory");
            var game = Game();
            // 场景1：开始升级只打开小游戏，资金、等级、格子、点数全部保持不变。
            Assert.That(Get<bool>(owner, "IsMemoryInstallOpen"), Is.True);
            Assert.That(Get<bool>(owner, "IsMemoryInstallPending"), Is.True);
            Assert.That(Get<int>(owner, "Funds"), Is.EqualTo(funds)); Assert.That(Get<int>(owner, "MemoryLevel"), Is.EqualTo(level));
            Assert.That(sim.OpenCellCount, Is.EqualTo(cells)); Assert.That(Get<int>(owner, "AvailableAbilityPoints"), Is.EqualTo(points));
            Assert.That(Get<bool>(owner, "IsModalOpen"), Is.True);
            Assert.That(Get<bool>(owner, "CanUpgradeMemory"), Is.False);
            // 场景7：挂起期间重复开始、推进时间、切换暂停、保存均为无操作，暂停状态保持。
            Call(owner, "UpgradeMemory"); Call(owner, "AdvanceGame", 100d); Call(owner, "TogglePause");
            Assert.That(sim.GameTime, Is.EqualTo(time)); Assert.That(Get<double>(owner, "MonthTimeRemaining"), Is.EqualTo(month)); Assert.That(sim.Paused, Is.False);
            Assert.That(Call(owner, "SaveRun"), Is.EqualTo(false)); Assert.That(Get<int>(owner, "Funds"), Is.EqualTo(funds));
            ((Button)Field(game, "finishButton")).onClick.Invoke(); Assert.That(Get<bool>(game, "IsOpen"), Is.True, "Finish must not bypass the installation.");
            Capture("MemoryInstallInitial.png");
            Click(game, "pressRightButton"); Assert.That(((Button)Field(game, "finishButton")).gameObject.activeSelf, Is.False);
            Click(game, "pressLeftButton"); Click(game, "pressRightButton");
            Assert.That(((RectTransform)Field(game, "memoryStick")).localEulerAngles.z, Is.EqualTo(0).Within(.01));
            Assert.That(((RectTransform)Field(game, "leftClip")).localEulerAngles.z, Is.EqualTo(0).Within(.01));
            Assert.That(((RectTransform)Field(game, "rightClip")).localEulerAngles.z, Is.EqualTo(0).Within(.01));
            Capture("MemoryInstallReady.png");
            var completions = new CompletionCounter(); Subscribe(game, completions.Increment);
            Click(game, "finishButton"); ((Button)Field(game, "finishButton")).onClick.Invoke(); ((Button)Field(game, "skipButton")).onClick.Invoke();
            // 场景2：恰好扣费一次、升一级、开放格子与奖励点数；重复 Finish/Skip 回调不再产生效果。
            Assert.That(completions.Count, Is.EqualTo(1)); Assert.That(Get<bool>(game, "IsOpen"), Is.False);
            Assert.That(Get<bool>(owner, "IsMemoryInstallPending"), Is.False);
            Assert.That(Get<int>(owner, "Funds"), Is.EqualTo(funds - cost)); Assert.That(Get<int>(owner, "MemoryLevel"), Is.EqualTo(level + 1));
            Assert.That(sim.OpenCellCount, Is.GreaterThan(cells)); Assert.That(Get<int>(owner, "AvailableAbilityPoints"), Is.EqualTo(points + reward));
            Assert.That(Get<bool>(owner, "IsMemoryUpgradeOpen"), Is.True);
            Call(owner, "CloseMemoryUpgrade"); Assert.That(Get<bool>(owner, "IsModalOpen"), Is.False);
            Call(owner, "AdvanceGame", .25d); Assert.That(sim.GameTime, Is.GreaterThan(time));
            yield return new ExitPlayMode();
        }
        [UnityTest] public IEnumerator SkipAppliesTheSameTransactionOnceAndResetCancelsPendingInstall()
        {
            EditorSceneManager.OpenScene(Core); yield return new EnterPlayMode(); yield return null;
            var owner = Owner(); ManualClock(owner); var game = Game();
            var sim = Get<CallmeCatgirl.Gameplay.TaskGrid.TaskSimulation>(owner, "Simulation");
            int funds = Get<int>(owner, "Funds"), cells = sim.OpenCellCount;
            var next = Get<object>(owner, "NextMemoryUpgrade"); int cost = (int)next.GetType().GetField("Cost").GetValue(next);
            // 场景3：显式跳过与完成走同一事务，结果完全一致且只生效一次。
            Call(owner, "OpenMemoryUpgrade"); Call(owner, "UpgradeMemory"); yield return null; Click(game, "skipButton");
            Assert.That(Get<bool>(game, "IsOpen"), Is.False);
            Assert.That(Get<int>(owner, "Funds"), Is.EqualTo(funds - cost)); Assert.That(Get<int>(owner, "MemoryLevel"), Is.EqualTo(2));
            Assert.That(sim.OpenCellCount, Is.GreaterThan(cells));
            ((Button)Field(game, "skipButton")).onClick.Invoke(); Call(game, "SkipInstall");
            Assert.That(Get<int>(owner, "Funds"), Is.EqualTo(funds - cost)); Assert.That(Get<int>(owner, "MemoryLevel"), Is.EqualTo(2));
            // 每次成功升级都会开启一次全新安装，且提交前不扣费。
            owner.GetType().GetProperty("CompletedTasks").SetValue(owner, 100);
            Call(owner, "UpgradeMemory");
            Assert.That(Get<int>(owner, "MemoryLevel"), Is.EqualTo(2), "Completion, not start, applies the upgrade.");
            Assert.That(Get<bool>(game, "IsOpen"), Is.True, "Every successful level must start a fresh installation.");
            Assert.That(((Button)Field(game, "finishButton")).gameObject.activeSelf, Is.False);
            Click(game, "skipButton"); Assert.That(Get<int>(owner, "MemoryLevel"), Is.EqualTo(3));
            // 场景6：重置中止未完成的安装意图，不扣费、不升级；旧回调不能提交之后的意图。
            Call(owner, "UpgradeMemory"); Assert.That(Get<bool>(owner, "IsMemoryInstallPending"), Is.True);
            Call(owner, "ResetSimulation"); ManualClock(owner);
            Assert.That(Get<bool>(owner, "IsMemoryInstallPending"), Is.False); Assert.That(Get<bool>(game, "IsOpen"), Is.False);
            Assert.That(Get<bool>(owner, "IsModalOpen"), Is.False);
            Assert.That(Get<int>(owner, "Funds"), Is.EqualTo(funds)); Assert.That(Get<int>(owner, "MemoryLevel"), Is.EqualTo(1));
            ((Button)Field(game, "skipButton")).onClick.Invoke(); ((Button)Field(game, "finishButton")).onClick.Invoke();
            Assert.That(Get<int>(owner, "Funds"), Is.EqualTo(funds)); Assert.That(Get<int>(owner, "MemoryLevel"), Is.EqualTo(1));
            Call(owner, "OpenMemoryUpgrade"); Call(owner, "UpgradeMemory");
            Assert.That(((Button)Field(game, "finishButton")).gameObject.activeSelf, Is.False);
            var count = new CompletionCounter(); Subscribe(game, count.Increment);
            yield return null; Click(game, "skipButton"); Assert.That(count.Count, Is.EqualTo(1));
            Assert.That(Get<int>(owner, "Funds"), Is.EqualTo(funds - cost)); Assert.That(Get<int>(owner, "MemoryLevel"), Is.EqualTo(2));
            Call(owner, "ResetSimulation"); Assert.That(Get<bool>(owner, "IsModalOpen"), Is.False);
            yield return new ExitPlayMode();
        }
        [UnityTest] public IEnumerator FundsSpentDuringPendingInstallFailCommitWithoutChargeAndAllowRetry()
        {
            EditorSceneManager.OpenScene(Core); yield return new EnterPlayMode(); yield return null;
            var owner = Owner(); ManualClock(owner); var game = Game();
            var sim = Get<CallmeCatgirl.Gameplay.TaskGrid.TaskSimulation>(owner, "Simulation");
            int funds = Get<int>(owner, "Funds"), level = Get<int>(owner, "MemoryLevel"), cells = sim.OpenCellCount;
            var next = Get<object>(owner, "NextMemoryUpgrade"); int cost = (int)next.GetType().GetField("Cost").GetValue(next);
            Call(owner, "OpenMemoryUpgrade"); Call(owner, "UpgradeMemory");
            Assert.That(Get<bool>(owner, "IsMemoryInstallPending"), Is.True);
            // 场景5：挂起期间资金被其他钱包命令花掉，提交时重校验失败。
            Call(Get<object>(owner, "Economy"), "TrySpendFunds", funds);
            Assert.That(Get<int>(owner, "Funds"), Is.Zero);
            Call(game, "SkipInstall");
            Assert.That(Get<int>(owner, "Funds"), Is.Zero, "No charge and no negative balance.");
            Assert.That(Get<int>(owner, "MemoryLevel"), Is.EqualTo(level)); Assert.That(sim.OpenCellCount, Is.EqualTo(cells));
            Assert.That(Get<bool>(owner, "IsMemoryInstallPending"), Is.False);
            Assert.That(Get<bool>(owner, "IsMemoryInstallOpen"), Is.False, "Failed commit must not leave a modal lock.");
            Call(owner, "AddFunds", cost); Call(owner, "UpgradeMemory");
            Assert.That(Get<bool>(owner, "IsMemoryInstallOpen"), Is.True, "Retry once funded starts a fresh install.");
            Call(game, "SkipInstall");
            Assert.That(Get<int>(owner, "Funds"), Is.Zero); Assert.That(Get<int>(owner, "MemoryLevel"), Is.EqualTo(level + 1));
            Assert.That(sim.OpenCellCount, Is.GreaterThan(cells));
            yield return new ExitPlayMode();
        }
        [UnityTest] public IEnumerator CloseOrDisableInterruptsPendingInstallWithoutEffects()
        {
            EditorSceneManager.OpenScene(Core); yield return new EnterPlayMode(); yield return null;
            var owner = Owner(); ManualClock(owner); var game = Game();
            var sim = Get<CallmeCatgirl.Gameplay.TaskGrid.TaskSimulation>(owner, "Simulation");
            int funds = Get<int>(owner, "Funds"), level = Get<int>(owner, "MemoryLevel"), cells = sim.OpenCellCount;
            // 场景6：关闭升级窗口取消未完成的安装意图。
            Call(owner, "OpenMemoryUpgrade"); Call(owner, "UpgradeMemory");
            Assert.That(Get<bool>(owner, "IsMemoryInstallPending"), Is.True);
            Call(owner, "CloseMemoryUpgrade");
            Assert.That(Get<bool>(owner, "IsMemoryInstallPending"), Is.False); Assert.That(Get<bool>(owner, "IsMemoryInstallOpen"), Is.False);
            Assert.That(Get<bool>(owner, "IsModalOpen"), Is.False);
            Assert.That(Get<int>(owner, "Funds"), Is.EqualTo(funds)); Assert.That(Get<int>(owner, "MemoryLevel"), Is.EqualTo(level));
            Assert.That(sim.OpenCellCount, Is.EqualTo(cells));
            ((Button)Field(game, "skipButton")).onClick.Invoke(); ((Button)Field(game, "finishButton")).onClick.Invoke();
            Assert.That(Get<int>(owner, "Funds"), Is.EqualTo(funds)); Assert.That(Get<int>(owner, "MemoryLevel"), Is.EqualTo(level));
            // 重新打开是一次全新的有效安装；外部禁用面板同样中止意图（看门狗），且不留下模态锁。
            Call(owner, "OpenMemoryUpgrade"); Call(owner, "UpgradeMemory");
            Assert.That(Get<bool>(game, "IsOpen"), Is.True);
            game.gameObject.SetActive(false); yield return null;
            Assert.That(Get<bool>(owner, "IsMemoryInstallPending"), Is.False, "Watchdog cancels an externally disabled install.");
            Assert.That(Get<bool>(owner, "IsMemoryInstallOpen"), Is.False); Assert.That(Get<bool>(owner, "IsModalOpen"), Is.True, "Upgrade window stays open but no install lock remains.");
            Assert.That(Get<int>(owner, "Funds"), Is.EqualTo(funds)); Assert.That(Get<int>(owner, "MemoryLevel"), Is.EqualTo(level));
            game.gameObject.SetActive(true); Call(owner, "UpgradeMemory");
            Assert.That(Get<bool>(game, "IsOpen"), Is.True, "Reopening starts a fresh valid install.");
            Call(game, "SkipInstall");
            Assert.That(Get<int>(owner, "MemoryLevel"), Is.EqualTo(level + 1));
            yield return new ExitPlayMode();
        }
        [UnityTest] public IEnumerator InvalidGeometryOrMissingInstallUiFailSafely()
        {
            EditorSceneManager.OpenScene(Core); yield return new EnterPlayMode(); yield return null;
            var owner = Owner(); ManualClock(owner); var game = Game();
            var sim = Get<CallmeCatgirl.Gameplay.TaskGrid.TaskSimulation>(owner, "Simulation");
            int funds = Get<int>(owner, "Funds"), level = Get<int>(owner, "MemoryLevel"), cells = sim.OpenCellCount;
            var config = owner.GetType().GetField("configuration", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
            var memory = config.GetType().GetField("MemoryUpgrades").GetValue(config);
            var unlockField = memory.GetType().GetField("CellUnlockLevels");
            var validCells = (int[])unlockField.GetValue(memory);
            var gameField = owner.GetType().GetField("memoryInstallGame", BindingFlags.Instance | BindingFlags.NonPublic);
            try
            {
                // 场景8：开始时几何配置无效，不打开小游戏、不产生任何变化。
                unlockField.SetValue(memory, new int[3]);
                Call(owner, "UpgradeMemory");
                Assert.That(Get<bool>(owner, "IsMemoryInstallPending"), Is.False); Assert.That(Get<bool>(owner, "IsMemoryInstallOpen"), Is.False);
                Assert.That(Get<int>(owner, "Funds"), Is.EqualTo(funds)); Assert.That(Get<int>(owner, "MemoryLevel"), Is.EqualTo(level));
                Assert.That(sim.OpenCellCount, Is.EqualTo(cells));
                unlockField.SetValue(memory, validCells);
                // 缺少安装界面时安全失败，而不是立即升级。
                gameField.SetValue(owner, null);
                Call(owner, "UpgradeMemory");
                Assert.That(Get<bool>(owner, "IsMemoryInstallPending"), Is.False);
                Assert.That(Get<int>(owner, "Funds"), Is.EqualTo(funds)); Assert.That(Get<int>(owner, "MemoryLevel"), Is.EqualTo(level));
                gameField.SetValue(owner, game);
                // 提交时几何配置损坏：不扣费、不升级、意图清除、可重试。
                Call(owner, "UpgradeMemory"); Assert.That(Get<bool>(owner, "IsMemoryInstallPending"), Is.True);
                unlockField.SetValue(memory, new int[3]);
                Call(game, "SkipInstall");
                Assert.That(Get<bool>(owner, "IsMemoryInstallPending"), Is.False); Assert.That(Get<bool>(owner, "IsMemoryInstallOpen"), Is.False);
                Assert.That(Get<int>(owner, "Funds"), Is.EqualTo(funds)); Assert.That(Get<int>(owner, "MemoryLevel"), Is.EqualTo(level));
                Assert.That(sim.OpenCellCount, Is.EqualTo(cells));
                unlockField.SetValue(memory, validCells);
                Call(owner, "UpgradeMemory"); Call(game, "SkipInstall");
                Assert.That(Get<int>(owner, "MemoryLevel"), Is.EqualTo(level + 1));
                Assert.That(Get<int>(owner, "Funds"), Is.LessThan(funds)); Assert.That(sim.OpenCellCount, Is.GreaterThan(cells));
            }
            finally { unlockField.SetValue(memory, validCells); gameField.SetValue(owner, game); }
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
