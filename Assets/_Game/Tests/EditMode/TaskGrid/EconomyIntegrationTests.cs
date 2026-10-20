using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CallmeCatgirl.Gameplay.TaskGrid;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace CallmeCatgirl.Tests
{
    public sealed class EconomyIntegrationTests
    {
        private static void SetMonthDuration(object config, float seconds)
        {
            config.GetType().GetField("DaysPerGameMonth").SetValue(config, 1);
            config.GetType().GetField("SecondsPerGameDay").SetValue(config, seconds);
        }

        private static Type WalletType => Type.GetType("CallmeCatgirl.Gameplay.EconomyManager, Assembly-CSharp", true);
        private static object Wallet(int funds = 80, int price = 1) => Activator.CreateInstance(WalletType, funds, price);
        private static object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method).Invoke(target, args);
        private static T Value<T>(object target, string property) => (T)target.GetType().GetProperty(property).GetValue(target);
        private static void Set(object target, string property, object value) => target.GetType().GetProperty(property).SetValue(target, value);
        private static TaskEvent Terminal(bool submitted, double calculation = 100, double patience = 40, double tokens = 100)
        {
            var model = new ModelDefinition("model", 3, new Dictionary<AbilityKind, double> { { AbilityKind.Writing, 4 } });
            var definition = new TaskDefinition("def", "task", new[] { new Cell(0, 0) }, calculation, 10, patience, tokens,
                new[] { new AbilityRequirement(AbilityKind.Writing, 10, 16) });
            var sim = new TaskSimulation(1, 1, new[] { new Cell(0, 0) }, model);
            sim.TryCreate("A", "user", "1", definition, out _); sim.TryPlace("A", new Cell(0, 0), 0, out _);
            sim.Advance(submitted ? calculation / 10 : patience);
            if (submitted) sim.TrySubmit("A");
            return sim.DrainEvents().Single(e => e.Kind == (submitted ? TaskEventKind.Submitted : TaskEventKind.TimedOut));
        }
        [Test] public void SuccessfulSubmissionPaysOnceAndFailedQualityDoesNotPay()
        {
            var wallet = Wallet(0); var item = Terminal(true);
            Assert.That(Call(wallet, "Settle", item), Is.EqualTo(100));
            Assert.That(Call(wallet, "Settle", item), Is.EqualTo(0));
            Assert.That(Value<int>(wallet, "CurrentFunds"), Is.EqualTo(100));
            wallet = Wallet(0);
            Assert.That(Call(wallet, "Settle", Terminal(true, calculation: 10)), Is.EqualTo(0));
            Assert.That(Value<int>(wallet, "CurrentFunds"), Is.Zero);
        }
        [Test] public void TimeoutPaysOnlyPartialTokensAndUsesOneRoundingRule()
        {
            var wallet = Wallet(0, 3); var item = Terminal(false, patience: 5, tokens: 3);
            Assert.That(item.BillableTokens, Is.EqualTo(1.5));
            Assert.That(Call(wallet, "Settle", item), Is.EqualTo(4));
            Assert.That(Call(wallet, "Settle", item), Is.EqualTo(0));
            Assert.That(Value<int>(wallet, "CurrentFunds"), Is.EqualTo(4));
        }
        [Test] public void MonthlyFeesEnterGraceThenBankruptcy()
        {
            var wallet = Wallet();
            Assert.That(Call(wallet, "SettleMonth", 50), Is.EqualTo(true));
            Assert.That(Value<int>(wallet, "CurrentFunds"), Is.EqualTo(30));
            Assert.That(Call(wallet, "SettleMonth", 50), Is.EqualTo(true));
            Assert.That(Value<int>(wallet, "CurrentFunds"), Is.Zero);
            Assert.That(Value<int>(wallet, "DebtAmount"), Is.EqualTo(20));
            Assert.That(Value<bool>(wallet, "IsInGracePeriod"), Is.True);
            Assert.That(Call(wallet, "SettleMonth", 50), Is.EqualTo(false));
            Assert.That(Value<bool>(wallet, "IsBankrupt"), Is.True);
        }
        [Test] public void RepaymentPaysDebtAndCurrentFeeAndExitsGrace()
        {
            var wallet = Wallet(); Call(wallet, "SettleMonth", 50); Call(wallet, "SettleMonth", 50);
            Call(wallet, "AddFunds", 70);
            Assert.That(Call(wallet, "SettleMonth", 50), Is.EqualTo(true));
            Assert.That(Value<int>(wallet, "CurrentFunds"), Is.Zero);
            Assert.That(Value<int>(wallet, "DebtAmount"), Is.Zero);
            Assert.That(Value<bool>(wallet, "IsInGracePeriod"), Is.False);
        }
        [Test] public void UpgradeSpendingUsesSameWalletAndCannotOverdraw()
        {
            var wallet = Wallet();
            Assert.That(Call(wallet, "TrySpendFunds", 100), Is.EqualTo(false));
            Assert.That(Value<int>(wallet, "CurrentFunds"), Is.EqualTo(80));
            Assert.That(Call(wallet, "TrySpendFunds", 60), Is.EqualTo(true));
            Assert.That(Value<int>(wallet, "CurrentFunds"), Is.EqualTo(20));
        }
        [Test] public void DebugIgnoreSpendingSkipsChargesUntilDisabled()
        {
            var wallet = Wallet();
            Call(wallet, "SettleMonth", 50); Call(wallet, "SettleMonth", 50); Call(wallet, "SettleMonth", 50);
            Assert.That(Value<bool>(wallet, "IsBankrupt"), Is.True);
            Set(wallet, "DebugIgnoreSpending", true);
            Assert.That(Call(wallet, "TrySpendFunds", 60), Is.True);
            Assert.That(Value<int>(wallet, "CurrentFunds"), Is.Zero);
            Assert.That(Call(wallet, "SettleMonth", 500), Is.True);
            Assert.That(Value<int>(wallet, "DebtAmount"), Is.Zero);
            Assert.That(Value<bool>(wallet, "IsInGracePeriod"), Is.False);
            Assert.That(Value<bool>(wallet, "IsBankrupt"), Is.False);
            Set(wallet, "DebugIgnoreSpending", false);
            Assert.That(Call(wallet, "TrySpendFunds", 1), Is.False);
            Call(wallet, "AddFunds", 10);
            Assert.That(Call(wallet, "TrySpendFunds", 4), Is.True);
            Assert.That(Value<int>(wallet, "CurrentFunds"), Is.EqualTo(6));
        }
        [UnityTest] public IEnumerator MultipleMonthsAreSettledAndPauseFreezesMonthlyClock()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity");
            yield return new EnterPlayMode(); yield return null;
            var controller=Controller();controller.GetType().GetProperty("ManualCommandClock").SetValue(controller,true);
            var sim=Value<TaskSimulation>(controller,"Simulation");
            Call(controller,"AddFunds",1500);
            Assert.That(Value<double>(controller,"SecondsPerDay"),Is.EqualTo(120));
            Assert.That(Value<int>(controller,"DaysPerMonth"),Is.EqualTo(7));
            Assert.That(Value<int>(controller,"MonthlyMaintenanceFee"),Is.EqualTo(350));
            Assert.That(Value<double>(controller,"MonthTimeRemaining"),Is.EqualTo(840));
            Call(controller,"AdvanceGame",120d);Assert.That(sim.GameTime,Is.Zero);
            sim.Paused=false;Call(controller,"AdvanceGame",119d);
            Assert.That(Value<int>(controller,"CurrentDay"),Is.EqualTo(1));
            Assert.That(Value<int>(controller,"Funds"),Is.EqualTo(1500));
            Call(controller,"AdvanceGame",1d);
            Assert.That(Value<int>(controller,"CurrentDay"),Is.EqualTo(2));
            Assert.That(Value<double>(controller,"DayTimeRemaining"),Is.EqualTo(120));
            Assert.That(Value<int>(controller,"Funds"),Is.EqualTo(1500),"Daily boundaries never charge maintenance.");
            Call(controller,"AdvanceGame",719d);
            Assert.That(Value<int>(controller,"CurrentDay"),Is.EqualTo(7));
            Assert.That(Value<int>(controller,"Funds"),Is.EqualTo(1500));
            Call(controller,"AdvanceGame",1d);
            Assert.That(Value<int>(controller,"CurrentMonth"),Is.EqualTo(2));
            Assert.That(Value<int>(controller,"CurrentDay"),Is.EqualTo(1));
            Assert.That(Value<int>(controller,"Funds"),Is.EqualTo(1150));
            Call(controller,"AdvanceGame",840d); Assert.That(Value<int>(controller,"Funds"),Is.EqualTo(800));
            Call(controller,"AdvanceGame",840d); Assert.That(Value<int>(controller,"Funds"),Is.EqualTo(450));
            Call(controller,"AdvanceGame",840d); Assert.That(Value<int>(controller,"Funds"),Is.EqualTo(100));
            Call(controller,"AdvanceGame",840d);
            Assert.That(Value<int>(controller,"CurrentMonth"),Is.EqualTo(6));
            Assert.That(Value<bool>(controller,"IsInGracePeriod"),Is.True);
            Assert.That(Value<int>(controller,"DebtAmount"),Is.EqualTo(250));
            Assert.That(Value<bool>(controller,"GameEnded"),Is.False);
            Call(controller,"AdvanceGame",840d);
            Assert.That(Value<bool>(controller,"GameEnded"),Is.True);
            Assert.That(sim.GameTime,Is.EqualTo(5040));
            Assert.That(sim.Paused,Is.True);
            Assert.That(Value<double>(controller,"DayTimeRemaining"),Is.Zero);
            Assert.That(Value<double>(controller,"MonthTimeRemaining"),Is.Zero);
            yield return new ExitPlayMode();
        }
        [UnityTest] public IEnumerator IncomeOnMonthlyBoundaryIsAppliedBeforeMaintenance()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity");
            yield return new EnterPlayMode(); yield return null;
            VerifyMonthlyBoundary(Controller());
            yield return new ExitPlayMode();
        }
        // Create callback closures after the Play Mode domain reload, not across EnterPlayMode.
        private static void VerifyMonthlyBoundary(MonoBehaviour controller)
        {
            var original = Configuration(controller); var config = Object.Instantiate(original);
            Action<TaskEvent> submit = item => { if (item.Kind == TaskEventKind.Ready) Call(controller, "SubmitSelected"); };
            try
            {
                config.GetType().GetField("InitialFunds").SetValue(config, 0);
                config.GetType().GetField("MonthlyMaintenanceFee").SetValue(config, 100);
                SetMonthDuration(config, 10f);
                Configure(controller, config); Call(controller, "ResetSimulation");
                controller.GetType().GetEvent("TaskEventRaised").AddEventHandler(controller, submit);
                var sim = Value<TaskSimulation>(controller, "Simulation");
                var origin = Enumerable.Range(0, sim.Width * sim.Height).Select(i => new Cell(i % sim.Width, i / sim.Width))
                    .First(c => sim.Preview("A-1", c, 0) == PlacementFailure.None);
                sim.TryPlace("A-1", origin, 0, out _); sim.Paused = false;
                Call(controller, "AdvanceGame", 10d); sim.Paused = true;
                Assert.That(Value<int>(controller, "CompletedTasks"), Is.EqualTo(1));
                Assert.That(Value<int>(controller, "Funds"), Is.Zero);
                Assert.That(Value<bool>(controller, "IsInGracePeriod"), Is.False);
                Assert.That(Value<int>(controller, "CurrentMonth"), Is.EqualTo(2));
            }
            finally
            {
                controller.GetType().GetEvent("TaskEventRaised").RemoveEventHandler(controller, submit);
                Configure(controller, original); Object.DestroyImmediate(config);
            }
        }
        [UnityTest] public IEnumerator BankruptcyStopsCommandsAndRestartRestoresTheSession()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity");
            yield return new EnterPlayMode(); yield return null;
            var controller = Controller(); var original = Configuration(controller); var config = Object.Instantiate(original);
            try
            {
                config.GetType().GetField("InitialFunds").SetValue(config, 80);
                config.GetType().GetField("MonthlyMaintenanceFee").SetValue(config, 50);SetMonthDuration(config,20f);
                Configure(controller, config); Call(controller, "ResetSimulation");
                var sim = Value<TaskSimulation>(controller, "Simulation"); sim.Paused = false;
                Call(controller, "AdvanceGame", 100d);
                Assert.That(Value<bool>(controller, "GameEnded"), Is.True);
                Assert.That(sim.GameTime, Is.EqualTo(60));
                Assert.That(sim.Paused, Is.True);
                Assert.That(Value<int>(controller, "DebtAmount"), Is.EqualTo(20));
                Assert.That(GameObject.Find("EndingPanel"), Is.Not.Null);
                Assert.That(GameObject.Find("EndingReason").GetComponent<Text>().text, Does.Contain("20"));
                int count = sim.Tasks.Count;
                Call(controller, "TogglePause"); Call(controller, "AddRequest"); Call(controller, "UpgradeMemory");
                Assert.That(sim.Paused, Is.True); Assert.That(sim.Tasks.Count, Is.EqualTo(count));
                GameObject.Find("EndingRestartButton").GetComponent<Button>().onClick.Invoke();
                Assert.That(Value<bool>(controller, "GameEnded"), Is.False);
                Assert.That(Value<int>(controller, "CurrentMonth"), Is.EqualTo(1));
                Assert.That(Value<int>(controller, "Funds"), Is.EqualTo(80));
                Assert.That(Value<int>(controller, "DebtAmount"), Is.Zero);
                Assert.That(Value<TaskSimulation>(controller, "Simulation").Paused, Is.True);
                Assert.That(GameObject.Find("EndingPanel"), Is.Null);
            }
            finally { Configure(controller, original); Object.DestroyImmediate(config); }
            yield return new ExitPlayMode();
        }
        private static MonoBehaviour Controller() => Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
            .Single(item => item.GetType().FullName == "CallmeCatgirl.Gameplay.CoreGameplayController");
        private static ScriptableObject Configuration(MonoBehaviour controller) =>
            (ScriptableObject)controller.GetType().GetField("configuration", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(controller);
        private static void Configure(MonoBehaviour controller, ScriptableObject config) =>
            controller.GetType().GetField("configuration", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(controller, config);
        [UnityTearDown] public IEnumerator StopPlayMode() { if (EditorApplication.isPlaying) yield return new ExitPlayMode(); }
    }
}