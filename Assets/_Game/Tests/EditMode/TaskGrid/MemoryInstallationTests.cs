using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
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
    public sealed class MemoryInstallationTests
    {
        private const string Core = "Assets/_Game/Scenes/Prototypes/CoreGameplay.unity";
        private string slotDirectory;
        private sealed class CompletionCounter { public int Count; public void Increment() { Count++; } }
        private static MonoBehaviour Owner() => Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Single(o => o.GetType().Name == "CoreGameplayController");
        private static MonoBehaviour Game() => Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None).Single(o => o.GetType().Name == "StandaloneMemoryInstall");
        private static T Get<T>(object o, string name)
        {
            var property=o.GetType().GetProperty(name);
            return (T)(property!=null?property.GetValue(o):o.GetType().GetField(name).GetValue(o));
        }
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
        private static object Api(object owner,string json)
        {
            var command=owner.GetType().Assembly.GetType("CallmeCatgirl.Gameplay.GameCommand",true);
            var result=Call(owner,"ExecuteCommand",JsonUtility.FromJson(json,command));
            Assert.That(Get<bool>(result,"ok"),Is.True,json+" => "+Get<string>(result,"code"));return result;
        }
        private static void CloseNarrative(object owner)
        {
            var narrative=Get<object>(owner,"Narrative");
            for(int i=0;i<20;i++)
            {
                var frame=Get<object>(narrative,"CurrentFrame");if(frame==null)return;
                if(Get<bool>(frame,"CanClose")&&(bool)Call(narrative,"CloseConversation"))return;
                var choices=((IEnumerable)Get<object>(frame,"Choices")).Cast<object>().ToArray();
                Assert.That(choices,Is.Not.Empty,"Narrative must expose a close or actual progression choice.");
                Assert.That(Call(narrative,"Choose",Get<string>(choices[0],"Id")),Is.EqualTo(true));
            }
            Assert.Fail("Narrative did not reach a closable frame.");
        }
        private static void FinishRequest(object owner,string id)
        {
            CloseNarrative(owner);
            var simulation=Get<TaskSimulation>(owner,"Simulation");
            var cell=Enumerable.Range(0,simulation.Width*simulation.Height).Select(i=>new Cell(i%simulation.Width,i/simulation.Width))
                .First(c=>simulation.Preview(id,c,0)==PlacementFailure.None);
            Api(owner,"{\"kind\":\"place\",\"id\":\""+id+"\",\"x\":"+cell.X+",\"y\":"+cell.Y+"}");
            Api(owner,"{\"kind\":\"pause\",\"paused\":false}");
            // Advance real work until ready. Calculation/throughput alone is not the integrated token-budget duration.
            var task=simulation.Tasks[id];
            for(int i=0;i<1000&&task.State==RequestState.Running;i++)
            {
                double before=task.Progress;Api(owner,"{\"kind\":\"advance\",\"seconds\":0.25}");
                Assert.That(task.Progress,Is.GreaterThan(before),"Eligible fixture work must actually progress: "+id);
            }
            Assert.That(task.State,Is.EqualTo(RequestState.Ready),id);
            Assert.That(task.Result.Overall,Is.GreaterThanOrEqualTo(Quality.Complete),id);
            Api(owner,"{\"kind\":\"submit\",\"id\":\""+id+"\"}");
        }
        private static IEnumerator PrepareEligibleRun(object owner)
        {
            // Production starts at zero RAM/zero money. Qualify the isolated transaction fixture through real gameplay.
            ManualClock(owner);var simulation=Get<TaskSimulation>(owner,"Simulation");
            Assert.That(Get<int>(owner,"MemoryLevel"),Is.Zero);Assert.That(Get<int>(owner,"Funds"),Is.Zero);
            var narrative=Get<object>(owner,"Narrative");Assert.That(Call(narrative,"Choose","mew"),Is.EqualTo(true));
            yield return null;CloseNarrative(owner);yield return null;
            Assert.That(simulation.OpenCellCount,Is.EqualTo(1));
            Api(owner,"{\"kind\":\"train\",\"recipe\":\"tutorial-first-model\",\"architecture\":\"baseline\",\"displayName\":\"升级事务测试模型\",\"investments\":[{\"ConversationId\":\"narrative-doctor\",\"Points\":1}]}");
            var session=Get<object>(Get<object>(owner,"Training"),"Session");
            var job=((IEnumerable)Get<object>(session,"Jobs")).Cast<object>().Single();
            FinishRequest(owner,Get<string>(job,"RequestId"));yield return null;CloseNarrative(owner);yield return null;
            string doctor=(string)narrative.GetType().GetField("FirstDoctorRequestId").GetValue(null);
            string ordinary=(string)narrative.GetType().GetField("FirstOrdinaryRequestId").GetValue(null);
            FinishRequest(owner,doctor);yield return null;CloseNarrative(owner);yield return null;
            FinishRequest(owner,ordinary);yield return null;CloseNarrative(owner);
            var story=Get<object>(owner,"Story");
            for(int i=0;i<20&&Get<bool>(story,"IsReply");i++)Call(owner,"ChooseReplyAtRevision",0,Get<int>(story,"Revision"));
            Call(owner,"CloseConversationHistory");yield return null;CloseNarrative(owner);yield return null;
            Assert.That(Get<int>(owner,"CompletedTasks"),Is.EqualTo(2));Assert.That(Get<int>(owner,"Funds"),Is.EqualTo(120));
            Assert.That(Get<bool>(owner,"CanUpgradeMemory"),Is.True,"The test must isolate upgrade behavior after meeting the real tutorial requirements.");
        }
        private static void Click(object game, string field)
        {
            var layers=Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).FirstOrDefault(o=>o.GetType().Name=="UIWindowLayers");
            if(layers!=null)Call(layers,"Synchronize");
            Canvas.ForceUpdateCanvases(); var button = (Button)Field(game, field);
            var r = (RectTransform)button.transform;
            var pointer = new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(null, r.TransformPoint(r.rect.center)) };
            var hits = new System.Collections.Generic.List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
            Assert.That(hits.Count, Is.GreaterThan(0), field);
            string groups=string.Join("; ",button.GetComponentsInParent<CanvasGroup>(true).Select(g=>g.name+" active="+g.gameObject.activeInHierarchy+" input="+g.interactable+" raycasts="+g.blocksRaycasts));
            string activeWindow=layers==null?"standalone":Get<string>(layers,"ActiveWindow");
            Assert.That(ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject), Is.EqualTo(button.gameObject), field + " top hit: " + hits[0].gameObject.name+"; active window="+activeWindow+"; "+groups);
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler);
        }
        private static void Capture(string file)
        {
            var capture = typeof(SystemFeedbackTests).GetMethod("Capture", BindingFlags.NonPublic | BindingFlags.Static);
            capture.Invoke(null, new object[] { file }); capture.Invoke(null, new object[] { file });
        }
        private static MemoryInstallSimulation Physics(object game) => Get<MemoryInstallSimulation>(game, "PhysicsState");
        private static GameObject Ram(object game) => Get<RectTransform>(game, "RamTransform").gameObject;
        private static Vector2 Screen(object game, Vec2 point) => (Vector2)Call(game, "SimulationToScreen", point, null);
        private static PointerEventData Pointer(object game, Vec2 point, int id = -1) => new PointerEventData(EventSystem.current)
        { pointerId = id, button = PointerEventData.InputButton.Left, position = Screen(game, point), useDragThreshold = false };
        private static void Advance(object game, float seconds)
        { for (int i = 0; i < Mathf.CeilToInt(seconds * 60); i++) Call(game, "AdvancePhysics", 1f / 60); }
        private static void PressEnd(object game, int end, string capture = null)
        {
            var sim = Physics(game);
            var pointer = Pointer(game, sim.Ram.LocalToWorld(new Vec2(sim.NodeRestX(end), 0)));
            ExecuteEvents.Execute(Ram(game), pointer, ExecuteEvents.pointerDownHandler);
            Assert.That(sim.IsGrabbed, Is.True, "A real RAM pointer-down must establish the constraint.");
            float anchorY = sim.GripWorld.Y;
            for (int i = 0; i < 240 && !sim.IsCaptured(end); i++)
            {
                pointer.position = Screen(game, new Vec2(sim.GripWorld.X, anchorY + 90));
                ExecuteEvents.Execute(Ram(game), pointer, ExecuteEvents.dragHandler);
                Call(game, "AdvancePhysics", 1f / 60);
            }
            Assert.That(sim.IsCaptured(end), Is.True, "The actual UI geometry must permit one-end capture.");
            if (capture != null) Capture(capture);
            ExecuteEvents.Execute(Ram(game), pointer, ExecuteEvents.pointerUpHandler);
            Assert.That(sim.IsGrabbed, Is.False);
        }
        private static void SolveThroughPointer(object game, bool capture = false)
        {
            PressEnd(game, 0, capture ? "MemoryInstallEndPress.png" : null); Advance(game, .3f);
            Assert.That(Physics(game).LeftCaptured, Is.True, "The first end must survive releasing to switch sides.");
            PressEnd(game, 2); Advance(game, 3);
            Assert.That(Physics(game).Solved, Is.True, "Real mouse down/drag/up and passive settling must solve the installation.");
            Assert.That(((Button)Field(game, "finishButton")).gameObject.activeSelf, Is.True);
        }
        [UnityTest] public IEnumerator UpgradeStartsMinigameAndFinishingAppliesChargeLevelCellsAndPointsExactlyOnce()
        {
            EditorSceneManager.OpenScene(Core); yield return new EnterPlayMode(); yield return null;
            var owner = Owner(); yield return PrepareEligibleRun(owner); ManualClock(owner);
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
            ((Button)Field(game, "pressRightButton")).onClick.Invoke();
            ((Button)Field(game, "pressLeftButton")).onClick.Invoke();
            Assert.That(((Button)Field(game, "finishButton")).gameObject.activeSelf, Is.False, "Legacy buttons cannot bypass physics.");
            float oldScale = Time.timeScale;
            try { Time.timeScale = 0; SolveThroughPointer(game, true); }
            finally { Time.timeScale = oldScale; }
            Assert.That(Mathf.DeltaAngle(((RectTransform)Field(game, "memoryStick")).localEulerAngles.z, -Physics(game).Ram.Theta * Mathf.Rad2Deg), Is.EqualTo(0).Within(.01));
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
            Call(owner, "CloseMemoryUpgrade"); CloseNarrative(owner); yield return null;
            Assert.That(Get<bool>(owner, "IsModalOpen"), Is.False);
            Call(owner, "AdvanceGame", .25d); Assert.That(sim.GameTime, Is.GreaterThan(time));
            yield return new ExitPlayMode();
        }
        [UnityTest] public IEnumerator SkipAppliesTheSameTransactionOnceAndResetCancelsPendingInstall()
        {
            EditorSceneManager.OpenScene(Core); yield return new EnterPlayMode(); yield return null;
            var owner = Owner(); yield return PrepareEligibleRun(owner); ManualClock(owner); var game = Game();
            var sim = Get<CallmeCatgirl.Gameplay.TaskGrid.TaskSimulation>(owner, "Simulation");
            int funds = Get<int>(owner, "Funds"), cells = sim.OpenCellCount, level=Get<int>(owner,"MemoryLevel");
            var next = Get<object>(owner, "NextMemoryUpgrade"); int cost = (int)next.GetType().GetField("Cost").GetValue(next);
            // 场景3：显式跳过与完成走同一事务，结果完全一致且只生效一次。
            Call(owner, "OpenMemoryUpgrade"); Call(owner, "UpgradeMemory"); yield return null; Click(game, "skipButton");
            Assert.That(Get<bool>(game, "IsOpen"), Is.False);
            Assert.That(Get<int>(owner, "Funds"), Is.EqualTo(funds - cost)); Assert.That(Get<int>(owner, "MemoryLevel"), Is.EqualTo(level+1));
            Assert.That(sim.OpenCellCount, Is.GreaterThan(cells));
            ((Button)Field(game, "skipButton")).onClick.Invoke(); Call(game, "SkipInstall");
            Assert.That(Get<int>(owner, "Funds"), Is.EqualTo(funds - cost)); Assert.That(Get<int>(owner, "MemoryLevel"), Is.EqualTo(level+1));
            // 每次成功升级都会开启一次全新安装，且提交前不扣费。
            CloseNarrative(owner);yield return null;
            var second=Get<object>(owner,"NextMemoryUpgrade");
            int secondCost=(int)second.GetType().GetField("Cost").GetValue(second);
            // Fund only this isolated wallet's next transaction. Production opening values stay zero.
            Call(owner,"AddFunds",secondCost);int beforeSecond=Get<int>(owner,"Funds");
            Call(owner, "UpgradeMemory");
            Assert.That(Get<int>(owner, "MemoryLevel"), Is.EqualTo(level+1), "Completion, not start, applies the upgrade.");
            Assert.That(Get<int>(owner,"Funds"),Is.EqualTo(beforeSecond));
            Assert.That(Get<bool>(game, "IsOpen"), Is.True, "Every successful level must start a fresh installation.");
            Assert.That(((Button)Field(game, "finishButton")).gameObject.activeSelf, Is.False);
            // Reopening is a fresh Canvas activation; physical input arrives after its next rendering/layout pass.
            yield return null;
            Click(game, "skipButton"); Assert.That(Get<int>(owner, "MemoryLevel"), Is.EqualTo(level+2));
            Assert.That(Get<int>(owner,"Funds"),Is.EqualTo(beforeSecond-secondCost));
            Call(owner,"CloseMemoryUpgrade");CloseNarrative(owner);yield return null;
            // Earn the third required completed order through the real stage-two request, rather than setting a counter.
            var narrative=Get<object>(owner,"Narrative");
            string doctorContact=(string)narrative.GetType().GetField("DoctorId").GetValue(null);
            for(int i=0;i<3&&!sim.Tasks.ContainsKey("narrative-doctor-stage-2");i++)
            {
                Assert.That(Call(narrative,"OpenContact",doctorContact),Is.EqualTo(true));
                var frame=Get<object>(narrative,"CurrentFrame");
                var choice=((IEnumerable)Get<object>(frame,"Choices")).Cast<object>().First();
                Assert.That(Call(narrative,"Choose",Get<string>(choice,"Id")),Is.EqualTo(true));
                CloseNarrative(owner);yield return null;
            }
            Assert.That(sim.Tasks.ContainsKey("narrative-doctor-stage-2"),Is.True);
            FinishRequest(owner,"narrative-doctor-stage-2");yield return null;CloseNarrative(owner);yield return null;
            var third=Get<object>(owner,"NextMemoryUpgrade");Call(owner,"AddFunds",(int)third.GetType().GetField("Cost").GetValue(third));
            // 场景6：重置中止未完成的安装意图，不扣费、不升级；旧回调不能提交之后的意图。
            Call(owner, "UpgradeMemory"); Assert.That(Get<bool>(owner, "IsMemoryInstallPending"), Is.True);
            Call(owner, "ResetSimulation"); ManualClock(owner);
            Assert.That(Get<bool>(owner, "IsMemoryInstallPending"), Is.False); Assert.That(Get<bool>(game, "IsOpen"), Is.False);
            Assert.That(Get<bool>(Get<object>(owner,"Narrative"),"RequiresPause"),Is.True,"Only the legitimate fresh tutorial may block input after reset.");
            Assert.That(Get<int>(owner, "Funds"), Is.Zero); Assert.That(Get<int>(owner, "MemoryLevel"), Is.Zero);
            ((Button)Field(game, "skipButton")).onClick.Invoke(); ((Button)Field(game, "finishButton")).onClick.Invoke();
            Assert.That(Get<int>(owner, "Funds"), Is.Zero); Assert.That(Get<int>(owner, "MemoryLevel"), Is.Zero);
            yield return PrepareEligibleRun(owner);
            Call(owner, "OpenMemoryUpgrade"); Call(owner, "UpgradeMemory");
            Assert.That(((Button)Field(game, "finishButton")).gameObject.activeSelf, Is.False);
            var count = new CompletionCounter(); Subscribe(game, count.Increment);
            yield return null; Click(game, "skipButton"); Assert.That(count.Count, Is.EqualTo(1));
            Assert.That(Get<int>(owner, "Funds"), Is.EqualTo(funds - cost)); Assert.That(Get<int>(owner, "MemoryLevel"), Is.EqualTo(level+1));
            Call(owner, "ResetSimulation"); Assert.That(Get<bool>(owner, "IsMemoryInstallPending"), Is.False);
            yield return new ExitPlayMode();
        }
        [UnityTest] public IEnumerator FundsSpentDuringPendingInstallFailCommitWithoutChargeAndAllowRetry()
        {
            EditorSceneManager.OpenScene(Core); yield return new EnterPlayMode(); yield return null;
            var owner = Owner(); yield return PrepareEligibleRun(owner); ManualClock(owner); var game = Game();
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
            var owner = Owner(); yield return PrepareEligibleRun(owner); ManualClock(owner); var game = Game();
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
            var owner = Owner(); yield return PrepareEligibleRun(owner); ManualClock(owner); var game = Game();
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
            var owner = Owner(); yield return PrepareEligibleRun(owner); ManualClock(owner); int level = Get<int>(owner, "MemoryLevel");
            Call(Get<object>(owner, "Economy"), "TrySpendFunds", Get<int>(owner, "Funds"));
            Call(owner, "OpenMemoryUpgrade"); Call(owner, "UpgradeMemory");
            Assert.That(Get<bool>(owner, "IsMemoryInstallOpen"), Is.False); Assert.That(Get<int>(owner, "MemoryLevel"), Is.EqualTo(level));
            yield return new ExitPlayMode();
        }
        [UnityTest] public IEnumerator RestoreClearsInstallationOnlyAfterSuccessfulRestore()
        {
            EditorSceneManager.OpenScene(Core); yield return new EnterPlayMode(); yield return null;
            var owner = Owner(); yield return PrepareEligibleRun(owner); ManualClock(owner); string slot = "memory-install-test-" + Guid.NewGuid().ToString("N");
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
            SolveThroughPointer(game); yield return null; Click(game, "finishButton");
            Assert.That(Get<bool>(game, "IsOpen"), Is.False); Assert.That(game.gameObject.activeInHierarchy, Is.True);
            yield return new ExitPlayMode();
        }
        [UnityTest] public IEnumerator WholeRamOwnsInputFixedGripSurvivesRotationAndForeignPointerCannotRelease()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/MemoryInstallScene.unity"); yield return new EnterPlayMode(); yield return null;
            var game = Game(); var sim = Physics(game); Canvas.ForceUpdateCanvases();
            Assert.That(sim.Tuning.RamThickness, Is.EqualTo(240f * 236 / 700).Within(.01), "The visible 700x236 strip must have a matching physical hit box.");
            foreach (var local in new[] { new Vec2(-110, -35), new Vec2(0, 35), new Vec2(110, -35) })
            {
                var data = Pointer(game, sim.Ram.LocalToWorld(local));
                var hits = new System.Collections.Generic.List<RaycastResult>(); EventSystem.current.RaycastAll(data, hits);
                Assert.That(hits.Count, Is.GreaterThan(0));
                Assert.That(ExecuteEvents.GetEventHandler<IPointerDownHandler>(hits[0].gameObject), Is.EqualTo(Ram(game)), "The whole RAM art must own the top input hit.");
                ExecuteEvents.Execute(Ram(game), data, ExecuteEvents.pointerDownHandler);
                Assert.That(sim.IsGrabbed, Is.True); Assert.That(sim.GripLocal.X, Is.EqualTo(local.X).Within(.02)); Assert.That(sim.GripLocal.Y, Is.EqualTo(local.Y).Within(.02));
                ExecuteEvents.Execute(Ram(game), data, ExecuteEvents.pointerUpHandler);
            }
            var grip = new Vec2(-90, -20);
            var pointer = Pointer(game, sim.Ram.LocalToWorld(grip), 17);
            ExecuteEvents.Execute(Ram(game), pointer, ExecuteEvents.pointerDownHandler);
            float y = sim.GripWorld.Y;
            for (int i = 0; i < 20; i++)
            {
                pointer.position = Screen(game, new Vec2(sim.GripWorld.X, y + 55));
                ExecuteEvents.Execute(Ram(game), pointer, ExecuteEvents.dragHandler); Call(game, "AdvancePhysics", 1f / 60);
            }
            Assert.That(Math.Abs(sim.Ram.Theta), Is.GreaterThan(.02));
            Assert.That(sim.GripLocal.X, Is.EqualTo(grip.X).Within(.02)); Assert.That(sim.GripLocal.Y, Is.EqualTo(grip.Y).Within(.02));
            var expected = sim.Ram.LocalToWorld(grip);
            Assert.That((sim.GripWorld - expected).Length, Is.LessThan(.03));
            var foreign = Pointer(game, sim.Ram.LocalToWorld(Vec2.Zero), 18);
            ExecuteEvents.Execute(Ram(game), foreign, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(Ram(game), foreign, ExecuteEvents.pointerUpHandler);
            Assert.That(sim.IsGrabbed, Is.True); Assert.That(Get<int?>(game, "ActivePointerId"), Is.EqualTo(17));
            game.SendMessage("OnApplicationFocus", false);
            Assert.That(sim.IsGrabbed, Is.False); Assert.That(sim.AppliedForce, Is.EqualTo(Vec2.Zero));
            Assert.That(Get<int?>(game, "ActivePointerId"), Is.Null);
            Call(game, "ResetGame"); Call(game, "OpenGame");
            Assert.That(sim.IsGrabbed, Is.False); Assert.That(sim.Solved, Is.False);
            yield return new ExitPlayMode();
        }
        [UnityTest] public IEnumerator CenterPressureBendsRealSocketMeshAndReboundsWithoutSolving()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/MemoryInstallScene.unity"); yield return new EnterPlayMode(); yield return null;
            var game = Game(); var sim = Physics(game);
            Assert.That(sim, Is.Not.Null, "Standalone Start -> OpenGame must initialize physics before the test captures state.");
            var slot = game.GetComponentsInChildren<MonoBehaviour>(true).First(x => x.GetType().Name == "MemorySocketGraphic");
            var image = slot.GetComponent<Image>();
            Assert.That(image.raycastTarget, Is.False); Assert.That(image.enabled, Is.True, "Warp the original image rather than overlay a rigid duplicate.");
            Assert.That(image.isActiveAndEnabled,Is.True,"Inspect the live, rendered slot art.");
            var originalVertices = CaptureCanvas(game, "MemoryInstallPhysicalInitial.png", 1280, 720, image);
            Assert.That(originalVertices.Length, Is.GreaterThan(40), "Slot image must be tessellated to render local bending.");
            var pointer = Pointer(game, sim.Ram.LocalToWorld(Vec2.Zero));
            ExecuteEvents.Execute(Ram(game), pointer, ExecuteEvents.pointerDownHandler);
            Assert.That(sim.IsGrabbed, Is.True, "The center-pressure case must use the real pointer constraint.");
            float y = sim.GripWorld.Y;
            float oldScale = Time.timeScale;
            try
            {
                Time.timeScale = 0;
                for (int i = 0; i < 90; i++)
                {
                    pointer.position = Screen(game, new Vec2(sim.GripWorld.X, y + 120));
                    ExecuteEvents.Execute(Ram(game), pointer, ExecuteEvents.dragHandler); Call(game, "AdvancePhysics", 1f / 60);
                }
                Assert.That(sim.BoardBend, Is.GreaterThan(.5)); Assert.That(sim.NodeDeflection(1), Is.GreaterThan(sim.NodeDeflection(0)));
                Assert.That(sim.LeftCaptured || sim.RightCaptured || sim.Solved, Is.False);
                Assert.That(Get<float>(slot, "CenterDeflection"), Is.EqualTo(sim.NodeDeflection(1)).Within(.001));
                var deformedVertices = CaptureCanvas(game, "MemoryInstallCenterSag.png", 1280, 720, image);
                Assert.That(deformedVertices.Length,Is.EqualTo(originalVertices.Length));
                // A capturing lambda hoists originalVertices into a closure allocated before EnterPlayMode.
                // The test runner cannot restore that closure across its domain reload. Keep this comparison local.
                float maximumSquaredDelta = 0;
                for (int i = 0; i < deformedVertices.Length; i++)
                    maximumSquaredDelta = Mathf.Max(maximumSquaredDelta, (deformedVertices[i] - originalVertices[i]).sqrMagnitude);
                Assert.That(maximumSquaredDelta, Is.GreaterThan(.5));
                ExecuteEvents.Execute(Ram(game), pointer, ExecuteEvents.pointerUpHandler); Advance(game, 1.2f);
                Assert.That(sim.Solved, Is.False); Assert.That(sim.BoardBend, Is.LessThan(.2));
                Assert.That(((RectTransform)Field(game, "leftClip")).gameObject.activeInHierarchy, Is.True);
                Assert.That(((RectTransform)Field(game, "rightClip")).gameObject.activeInHierarchy, Is.True);
                Assert.That(((Button)Field(game, "pressLeftButton")).gameObject.activeSelf, Is.False);
                Assert.That(((Button)Field(game, "pressRightButton")).enabled, Is.False);
                Call(game, "ResetGame"); Call(game, "OpenGame");
                PressEnd(game, 2); CaptureCanvas(game, "MemoryInstallRightEndPress.png", 1920, 1080); Advance(game, .3f);
                PressEnd(game, 0); Advance(game, 3);
                Assert.That(sim.Solved, Is.True, "Either end can be installed first with the authored geometry.");
                CaptureCanvas(game, "MemoryInstallSolved.png", 1920, 1080);
            }
            finally { Time.timeScale = oldScale; }
            yield return new ExitPlayMode();
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Vector3[] RenderedVertices(Graphic image, string phase)
        {
            Assert.That(ReferenceEquals(image, null), Is.False, phase + ": Graphic managed reference is null.");
            Assert.That(image != null, Is.True, phase + ": Graphic native object has been destroyed.");
            CanvasRenderer renderer;
            try { renderer = image.canvasRenderer; }
            catch (Exception error) { Assert.Fail(phase + ": Graphic.canvasRenderer getter threw: " + error); return null; }
            Assert.That(ReferenceEquals(renderer, null), Is.False, phase + ": Graphic.canvasRenderer returned a null managed reference.");
            Assert.That(renderer != null, Is.True, phase + ": CanvasRenderer native object has been destroyed.");
            Mesh mesh;
            try { mesh = renderer.GetMesh(); }
            catch (Exception error) { Assert.Fail(phase + ": CanvasRenderer.GetMesh() threw: " + error); return null; }
            Assert.That(ReferenceEquals(mesh, null), Is.False, phase + ": CanvasRenderer.GetMesh() returned a null managed reference.");
            Assert.That(mesh != null, Is.True, phase + ": GetMesh() returned a destroyed native Mesh.");
            Vector3[] vertices;
            try { vertices = mesh.vertices; }
            catch (Exception error) { Assert.Fail(phase + ": Mesh.vertices getter threw: " + error); return null; }
            Assert.That(vertices, Is.Not.Null, phase + ": Mesh.vertices returned null.");
            TestContext.WriteLine(phase + ": frame=" + Time.frameCount + " vertices=" + vertices.Length
                + " active=" + image.isActiveAndEnabled + " culled=" + renderer.cull);
            // Mesh.vertices is already a managed snapshot; never clone/destroy the Canvas renderer's owned mesh.
            return vertices;
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void AssertStandaloneCaptureState(MonoBehaviour game, string phase)
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            Assert.That(scene.path, Is.EqualTo("Assets/_Game/Scenes/Prototypes/MemoryInstallScene.unity"), phase + ": active scene");
            Assert.That(game != null && game.isActiveAndEnabled, Is.True, phase + ": standalone controller must be live");
            var games = Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(candidate => candidate != null && candidate.GetType().Name == "StandaloneMemoryInstall").ToArray();
            Assert.That(games.Length, Is.EqualTo(1), phase + ": standalone controller count");
            Assert.That(games[0], Is.SameAs(game), phase + ": capture must use the only standalone controller");
            Assert.That(Get<bool>(game, "IsOpen"), Is.True, phase + ": standalone must remain open");
            Assert.That(Physics(game), Is.Not.Null, phase + ": initialized physics");
            var ram = Get<RectTransform>(game, "RamTransform");
            Assert.That(ram != null && ram.gameObject.activeInHierarchy, Is.True, phase + ": live RAM art");
            Assert.That(EventSystem.current != null && EventSystem.current.isActiveAndEnabled, Is.True, phase + ": live EventSystem");
            TestContext.WriteLine(phase + ": capture state ready; sceneRoots=" + scene.rootCount + " controllers=" + games.Length);
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Vector3[] CaptureCanvas(MonoBehaviour game, string name, int width, int height, Graphic sampledGraphic = null)
        {
            AssertStandaloneCaptureState(game, name);
            var canvas = game.GetComponentInParent<Canvas>();
            Assert.That(canvas != null, Is.True, name + ": live parent Canvas");
            var mode = canvas.renderMode; var oldCamera = canvas.worldCamera; float distance = canvas.planeDistance;
            var cameraObject = new GameObject("MemoryInstallCapture", typeof(Camera)); var camera = cameraObject.GetComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = 5; camera.nearClipPlane = .1f; camera.farClipPlane = 200;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.12f, .13f, .14f);
            var target = new RenderTexture(width, height, 24); camera.targetTexture = target;
            var active = RenderTexture.active; var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 10;
                Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0); texture.Apply();
                Directory.CreateDirectory("Logs/SystemFeedback"); File.WriteAllBytes("Logs/SystemFeedback/" + name, texture.EncodeToPNG());
                // Snapshot the actual renderer while the screenshot camera is still bound. Only the
                // managed vertices escape; the finally block restores the Canvas and destroys that camera.
                return !ReferenceEquals(sampledGraphic, null) ? RenderedVertices(sampledGraphic, name + " after Camera.Render before Canvas restore") : null;
            }
            finally
            {
                canvas.renderMode = mode; canvas.worldCamera = oldCamera; canvas.planeDistance = distance;
                RenderTexture.active = active; camera.targetTexture = null;
                Object.DestroyImmediate(texture); Object.DestroyImmediate(target); Object.DestroyImmediate(cameraObject); Canvas.ForceUpdateCanvases();
            }
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
