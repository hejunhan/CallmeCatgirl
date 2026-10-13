using System;
using System.Collections.Generic;
using System.Linq;
using CallmeCatgirl.Gameplay.TaskGrid;
using CallmeCatgirl.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CallmeCatgirl.Gameplay
{
    /// <summary>Owns the simulation and commands; visual layout is serialized in the Canvas scene.</summary>
    public sealed class CoreGameplayController : MonoBehaviour
    {
        [SerializeField] private GameplayConfiguration configuration;
        [SerializeField] private CoreGameplayCanvasView canvasView;
        public TaskSimulation Simulation { get; private set; }
        public string SelectedId { get; private set; }
        public TaskInstance SelectedTask => SelectedId != null && Simulation.Tasks.TryGetValue(SelectedId, out var task) ? task : null;
        public float PlaybackSpeed { get; private set; }
        public int MemoryLevel { get; private set; }
        public EconomyManager Economy { get; private set; }
        public int Funds => Economy.CurrentFunds;
        public int CurrentMonth { get; private set; }
        public int DebtAmount => Economy.DebtAmount;
        public bool IsInGracePeriod => Economy.IsInGracePeriod;
        public bool GameEnded { get; private set; }
        public string EndingText { get; private set; }
        public double MonthTimeRemaining => Math.Max(0, configuration.SecondsPerGameMonth - monthTimer);
        private double monthTimer;
        public int CompletedTasks { get; private set; }
        public MemoryUpgradeConfiguration.UpgradeStep NextMemoryUpgrade
            => configuration.MemoryUpgrades.TryGetNextLevel(MemoryLevel, out var step) ? step : null;
        public bool CanUpgradeMemory => !GameEnded && NextMemoryUpgrade != null && NextMemoryUpgrade.Cost >= 0
            && Funds >= NextMemoryUpgrade.Cost && CompletedTasks >= NextMemoryUpgrade.RequiredCompletedTasks;
        public int NextMemoryCellCount => NextMemoryUpgrade == null ? 0
            : configuration.MemoryUpgrades.GetOpenCells(NextMemoryUpgrade.TargetLevel).Count - Simulation.OpenCellCount;
        public string Message { get; private set; }
        public IReadOnlyList<string> Logs => logs;
        public event Action<TaskEvent> TaskEventRaised;
        private readonly List<string> logs = new List<string>();
        private readonly Dictionary<string, TaskDefinition> definitions = new Dictionary<string, TaskDefinition>();
        private int serial;

        private void Awake() => ResetSimulation();
        private void Start() { if (canvasView != null && Simulation != null) canvasView.Initialize(this); }
        private void Update()
        {
            if (Simulation == null) return;
            AdvanceGame(Time.unscaledDeltaTime * PlaybackSpeed);
            if (!GameEnded && Keyboard.current != null)
            {
                if (Keyboard.current.rKey.wasPressedThisFrame) RotateSelected();
                if (Keyboard.current.spaceKey.wasPressedThisFrame) TogglePause();
                if (Keyboard.current.escapeKey.wasPressedThisFrame && canvasView != null) canvasView.CancelDrag();
            }
            ReadEvents();
            if (canvasView != null) canvasView.Refresh();
        }

        public void ResetSimulation()
        {
            if (configuration == null) { Debug.LogError("CoreGameplayController requires a GameplayConfiguration.", this); enabled = false; return; }
            try
            {
                Simulation = configuration.BuildSimulation(); Simulation.Paused = true; definitions.Clear(); logs.Clear(); serial = 0;
                MemoryLevel = configuration.MemoryUpgrades.InitialLevel; CompletedTasks = 0;
                if (configuration.SecondsPerGameMonth <= 0 || float.IsNaN(configuration.SecondsPerGameMonth)
                    || float.IsInfinity(configuration.SecondsPerGameMonth)) throw new InvalidOperationException("每月游戏秒数必须是正的有限数值。");
                if (Economy != null) Economy.OnEconomyLog -= AddEconomyLog;
                Economy = configuration.BuildEconomyManager(); Economy.OnEconomyLog += AddEconomyLog;
                CurrentMonth = 1; monthTimer = 0; GameEnded = false; EndingText = string.Empty;
                foreach (var source in configuration.Requests)
                {
                    var definition = source.Build(); definitions.Add(definition.Id, definition);
                    Simulation.TryCreate(definition.Id + "-1", definition.Id, "1", definition, out _);
                }
                SelectedId = Simulation.Tasks.Keys.FirstOrDefault(); PlaybackSpeed = configuration.PlaybackSpeed;
                Message = "拖动请求到格子。R旋转，空格暂停，Esc取消拖拽；撤回不重置忍耐。";
                ReadEvents();
                if (canvasView != null && canvasView.IsInitialized) canvasView.Initialize(this);
            }
            catch (Exception exception) { Debug.LogException(exception, this); enabled = false; }
        }

        public void SelectRequest(string id) { if (Simulation.Tasks.ContainsKey(id)) SelectedId = id; }
        public void SelectConversation(string id)
        {
            var task = Simulation.Tasks.Values.LastOrDefault(t => t.ConversationId == id);
            if (task != null) SelectedId = task.Id;
        }
        public void CloseInfo() => SelectedId = null;
        public void SetMessage(string value) => Message = value;
        public void TogglePause() { if (!GameEnded) Simulation.Paused = !Simulation.Paused; }
        public void CycleSpeed() => PlaybackSpeed = PlaybackSpeed == 0.5f ? 1f : PlaybackSpeed == 1 ? 0.25f : 0.5f;
        public void SetSpeed(float value) => PlaybackSpeed = value;
        public void RotateSelected()
        {
            if (GameEnded) return;
            if (canvasView != null && canvasView.IsDragging) { canvasView.RotateDrag(); return; }
            var task = SelectedTask; if (task == null) return;
            if (task.State == RequestState.Running)
            {
                Simulation.TryPlace(task.Id, task.Origin, task.Rotation + 1, out var failure);
                Message = failure == PlacementFailure.None ? "旋转完成，进度保留。" : "旋转失败：" + FailureText(failure);
            }
            else Simulation.TryRotateCandidate(task.Id);
        }
        public void WithdrawSelected()
        {
            if (GameEnded) return;
            if (SelectedTask == null || !Simulation.TryWithdraw(SelectedId)) return;
            canvasView?.CancelDrag(); Message = "已撤回：进度和能力保留，忍耐继续。";
        }
        public void SubmitSelected()
        {
            if (GameEnded) return;
            if (SelectedTask != null && Simulation.TrySubmit(SelectedId))
            { ReadEvents(); Message = "已提交：释放格子并完成结算。"; canvasView?.Refresh(); }
        }
        public void AddRequest()
        {
            if (GameEnded) return;
            if (definitions.Count == 0) return;
            string suffix = (++serial).ToString();
            Simulation.TryCreate("new-" + suffix, "new-user-" + suffix, "extra-" + suffix, definitions.Values.First(), out var task);
            SelectedId = task?.Id;
        }
        public void NextRound()
        {
            if (GameEnded) return;
            var previous = SelectedTask;
            if (previous == null || !Simulation.TryCloseFeedback(previous.Id)) return;
            string round = "round-" + (++serial);
            Simulation.TryCreate(previous.ConversationId + "-" + round, previous.ConversationId, round, previous.Definition, out var next);
            SelectedId = next?.Id;
        }
        public void UpgradeMemory()
        {
            if (GameEnded) return;
            ReadEvents();
            var next = NextMemoryUpgrade;
            if (next == null) { Message = "内存已满级。"; return; }
            if (!CanUpgradeMemory)
            { Message = CompletedTasks < next.RequiredCompletedTasks ? $"需要累计达标提交 {next.RequiredCompletedTasks} 个任务。" : "升级资金不足。"; return; }
            int before = Simulation.OpenCellCount;
            if (!Simulation.TryUnlockCells(configuration.MemoryUpgrades.GetOpenCells(next.TargetLevel)))
            { Message = "升级格子配置越界，未扣费。"; return; }
            Economy.TrySpendFunds(next.Cost);
            MemoryLevel = next.TargetLevel;
            Message = $"内存升级至 Lv{MemoryLevel}：新增 {Simulation.OpenCellCount - before} 格，花费 {next.Cost}。";
            logs.Insert(0, Message);
            if (logs.Count > 7) logs.RemoveAt(logs.Count - 1);
            canvasView?.Refresh();
        }
        public void AddFunds(int amount) { Economy.AddFunds(amount); canvasView?.Refresh(); }
        // 分段推进到每个月底，先处理任务结算，再扣维护费，跨多月也不会漏结算。
        public void AdvanceGame(double delta)
        {
            if (double.IsNaN(delta) || double.IsInfinity(delta) || delta < 0) throw new ArgumentOutOfRangeException(nameof(delta));
            if (Simulation == null || GameEnded || Simulation.Paused) return;
            while (delta > 0 && !GameEnded)
            {
                double step = Math.Min(delta, configuration.SecondsPerGameMonth - monthTimer);
                Simulation.Advance(step); ReadEvents(); monthTimer += step; delta -= step;
                if (monthTimer + 1e-9 < configuration.SecondsPerGameMonth) continue;
                monthTimer = 0;
                if (Economy.SettleMonth(configuration.MonthlyMaintenanceFee)) CurrentMonth++;
                else
                {
                    GameEnded = true; Simulation.Paused = true;
                    EndingText = $"宽限期结束，无法偿还欠款 {DebtAmount} 和本月维护费 {configuration.MonthlyMaintenanceFee}。服务器停机，经营失败。";
                    AddEconomyLog(EndingText);
                }
            }
            canvasView?.Refresh();
        }
        private void AddEconomyLog(string content)
        {
            logs.Insert(0, content); if (logs.Count > 7) logs.RemoveAt(logs.Count - 1);
        }
        private void ReadEvents()
        {
            foreach (var item in Simulation.DrainEvents())
            {
                if (item.Kind == TaskEventKind.Submitted && item.Result != null && item.Result.Overall != Quality.BelowStandard) CompletedTasks++;
                Economy.Settle(item);
                logs.Insert(0, $"{item.GameTime:0.0}s  {item.RequestId}  {EventText(item.Kind)}"
                    + (item.Kind == TaskEventKind.Submitted || item.Kind == TaskEventKind.TimedOut ? $"  token={item.BillableTokens:0.##}" : ""));
                if (logs.Count > 7) logs.RemoveAt(logs.Count - 1);
                TaskEventRaised?.Invoke(item);
            }
        }
        public static string AbilityText(AbilityKind kind) => kind == AbilityKind.Writing ? "写作" : kind == AbilityKind.Code ? "代码" : kind == AbilityKind.Reasoning ? "推理" : "检索";
        public static string QualityText(Quality quality) => quality == Quality.Excellent ? "优秀" : quality == Quality.Complete ? "完成" : "未达标";
        public static string StateText(RequestState state) => new[] { "等待摆放", "运行中", "已撤回", "算完待确认", "已提交待回复", "超时失败", "反馈结束" }[(int)state];
        public static string FailureText(PlacementFailure failure) => new[] { "无", "任务不存在", "当前状态不可摆放", "越界", "未开放格", "与其他任务重叠" }[(int)failure];
        private static string EventText(TaskEventKind kind) => new[] { "请求到达", "合法摆放", "撤回", "算完判质", "提交结算", "超时结算", "反馈结束" }[(int)kind];
    }
}
