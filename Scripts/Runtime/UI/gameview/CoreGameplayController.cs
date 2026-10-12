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
        public int Funds { get; private set; }
        public int CompletedTasks { get; private set; }

        // J04 经营状态
        public int CurrentMonth { get; private set; } = 1;
        public int DebtAmount { get; private set; }
        public bool IsInGracePeriod { get; private set; }
        public bool GameEnded { get; private set; }
        public string EndingText { get; private set; }
        private float monthTimer;

        public MemoryUpgradeConfiguration.UpgradeStep NextMemoryUpgrade
            => configuration.MemoryUpgrades.TryGetNextLevel(MemoryLevel, out var step) ? step : null;
        public bool CanUpgradeMemory => NextMemoryUpgrade != null && NextMemoryUpgrade.Cost >= 0
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
            if (Simulation == null || GameEnded) return;
            Simulation.Advance(Time.unscaledDeltaTime * PlaybackSpeed);

            // 月度时间累计
            if (!Simulation.Paused)
            {
                monthTimer += Time.unscaledDeltaTime * PlaybackSpeed;
                if (monthTimer >= configuration.SecondsPerGameMonth)
                {
                    monthTimer -= configuration.SecondsPerGameMonth;
                    ProcessMonthlySettlement();
                }
            }

            if (Keyboard.current != null)
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
                MemoryLevel = configuration.MemoryUpgrades.InitialLevel; Funds = configuration.InitialFunds; CompletedTasks = 0;
                CurrentMonth = 1; DebtAmount = 0; IsInGracePeriod = false; GameEnded = false; monthTimer = 0;
                EndingText = string.Empty;

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
        public void TogglePause() => Simulation.Paused = !Simulation.Paused;
        public void CycleSpeed() => PlaybackSpeed = PlaybackSpeed == 0.5f ? 1f : PlaybackSpeed == 1 ? 0.25f : 0.5f;
        public void SetSpeed(float value) => PlaybackSpeed = value;
        public void RotateSelected()
        {
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
            if (SelectedTask == null || !Simulation.TryWithdraw(SelectedId)) return;
            canvasView?.CancelDrag(); Message = "已撤回：进度和能力保留，忍耐继续。";
        }
        public void SubmitSelected()
        {
            if (SelectedTask != null && Simulation.TrySubmit(SelectedId)) Message = "已提交：释放格子并发出一次结算事件。";
        }
        public void AddRequest()
        {
            if (definitions.Count == 0) return;
            string suffix = (++serial).ToString();
            Simulation.TryCreate("new-" + suffix, "new-user-" + suffix, "extra-" + suffix, definitions.Values.First(), out var task);
            SelectedId = task?.Id;
        }
        public void NextRound()
        {
            var previous = SelectedTask;
            if (previous == null || !Simulation.TryCloseFeedback(previous.Id)) return;
            string round = "round-" + (++serial);
            Simulation.TryCreate(previous.ConversationId + "-" + round, previous.ConversationId, round, previous.Definition, out var next);
            SelectedId = next?.Id;
        }
        public void UpgradeMemory()
        {
            ReadEvents();
            var next = NextMemoryUpgrade;
            if (next == null) { Message = "内存已满级。"; return; }
            if (!CanUpgradeMemory)
            { Message = CompletedTasks < next.RequiredCompletedTasks ? $"需要累计达标提交 {next.RequiredCompletedTasks} 个任务。" : "升级资金不足。"; return; }
            int before = Simulation.OpenCellCount;
            if (!Simulation.TryUnlockCells(configuration.MemoryUpgrades.GetOpenCells(next.TargetLevel)))
            { Message = "升级格子配置越界，未扣费。"; return; }
            Funds -= next.Cost;
            MemoryLevel = next.TargetLevel;
            Message = $"内存升级至 Lv{MemoryLevel}：新增 {Simulation.OpenCellCount - before} 格，花费 {next.Cost}。";
            AddLog(Message);
            canvasView?.Refresh();
        }

        // J04 月度结算、欠费、宽限逻辑
        private void ProcessMonthlySettlement()
        {
            int monthlyFee = configuration.MonthlyMaintenanceFee;
            if (IsInGracePeriod)
            {
                // 宽限期：需要还清旧欠款+本月维护费
                int totalRequired = DebtAmount + monthlyFee;
                if (Funds >= totalRequired)
                {
                    Funds -= totalRequired;
                    DebtAmount = 0;
                    IsInGracePeriod = false;
                    CurrentMonth++;
                    AddLog($"第{CurrentMonth}月：还清欠款+维护费共{totalRequired}，退出宽限期");
                }
                else
                {
                    TriggerFailedEnding($"欠费宽限期满，仍无法偿还合计{totalRequired}费用，服务器停机，经营失败。");
                }
            }
            else
            {
                // 正常月份扣维护费
                if (Funds >= monthlyFee)
                {
                    Funds -= monthlyFee;
                    CurrentMonth++;
                    AddLog($"第{CurrentMonth}月：扣除维护费{monthlyFee}");
                }
                else
                {
                    int debt = monthlyFee - Funds;
                    DebtAmount = debt;
                    Funds = 0;
                    IsInGracePeriod = true;
                    CurrentMonth++;
                    AddLog($"第{CurrentMonth}月：资金不足，欠费{debt}，进入1个月宽限期");
                }
            }
            canvasView?.Refresh();
        }

        private void TriggerFailedEnding(string reason)
        {
            GameEnded = true;
            Simulation.Paused = true;
            EndingText = reason;
            AddLog("经营失败：" + reason);
            canvasView?.Refresh();
        }

        public void AddFunds(int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            Funds = checked(Funds + amount);
            canvasView?.Refresh();
        }
        private void ReadEvents()
        {
            foreach (var item in Simulation.DrainEvents())
            {
                // 成功提交结算
                if (item.Kind == TaskEventKind.Submitted)
                {
                    if (item.Result != null && item.Result.Overall != Quality.BelowStandard)
                    {
                        CompletedTasks++;
                        int income = Mathf.RoundToInt((float)item.BillableTokens * configuration.TokenPrice);
                        Funds += income;
                    }
                }
                // 超时结算
                if (item.Kind == TaskEventKind.TimedOut)
                {
                    int income = Mathf.RoundToInt((float)item.BillableTokens * configuration.TokenPrice);
                    Funds += income;
                }

                AddLog($"{item.GameTime:0.0}s  {item.RequestId}  {EventText(item.Kind)}"
                    + (item.Kind == TaskEventKind.Submitted || item.Kind == TaskEventKind.TimedOut ? $"  token={item.BillableTokens:0.##}" : ""));
                TaskEventRaised?.Invoke(item);
            }
        }
        private void AddLog(string content)
        {
            logs.Insert(0, content);
            if (logs.Count > 7) logs.RemoveAt(logs.Count - 1);
        }
        public static string AbilityText(AbilityKind kind) => kind == AbilityKind.Writing ? "写作" : kind == AbilityKind.Code ? "代码" : kind == AbilityKind.Reasoning ? "推理" : "检索";
        public static string QualityText(Quality quality) => quality == Quality.Excellent ? "优秀" : quality == Quality.Complete ? "完成" : "未达标";
        public static string StateText(RequestState state) => new[] { "等待摆放", "运行中", "已撤回", "算完待确认", "已提交待回复", "超时失败", "反馈结束" }[(int)state];
        public static string FailureText(PlacementFailure failure) => new[] { "无", "任务不存在", "当前状态不可摆放", "越界", "未开放格", "与其他任务重叠" }[(int)failure];
        private static string EventText(TaskEventKind kind) => new[] { "请求到达", "合法摆放", "撤回", "算完判质", "提交结算", "超时结算", "反馈结束" }[(int)kind];
    }
}
