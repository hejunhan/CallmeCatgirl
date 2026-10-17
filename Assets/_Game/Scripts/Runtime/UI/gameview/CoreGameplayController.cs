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
        public ConversationStory Story { get; private set; }
        public bool IsStoryOpen => Story != null && Story.IsOpen;
        public bool IsMemoryUpgradeOpen { get; private set; }
        public bool IsModalOpen => IsStoryOpen || IsMemoryUpgradeOpen;
        public IReadOnlyList<Cell> NextMemoryCells => NextMemoryUpgrade == null ? Array.Empty<Cell>()
            : configuration.MemoryUpgrades.GetOpenCells(NextMemoryUpgrade.TargetLevel);
        public string SelectedId { get; private set; }
        public TaskInstance SelectedTask => SelectedId != null && Simulation.Tasks.TryGetValue(SelectedId, out var task) ? task : null;
        public float PlaybackSpeed { get; private set; }
        public int MemoryLevel { get; private set; }
        public string ModelName => configuration.MemoryUpgrades.GetModelName(MemoryLevel, Simulation.Model.Id);
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
        public int AvailableAbilityPoints { get; private set; }
        public bool CanAllocateAbility => !GameEnded && IsMemoryUpgradeOpen && !IsStoryOpen && AvailableAbilityPoints > 0;
        public MemoryUpgradeConfiguration.UpgradeStep NextMemoryUpgrade
            => configuration.MemoryUpgrades.TryGetNextLevel(MemoryLevel, out var step) ? step : null;
        public bool CanUpgradeMemory => !GameEnded && !IsStoryOpen && NextMemoryUpgrade != null
            && (NextMemoryUpgrade.TaskMultipliers == null || NextMemoryUpgrade.TaskMultipliers.IsValid) && NextMemoryUpgrade.Cost >= 0 && NextMemoryUpgrade.AbilityPoints >= 0
            && AvailableAbilityPoints <= int.MaxValue - NextMemoryUpgrade.AbilityPoints
            && Funds >= NextMemoryUpgrade.Cost && CompletedTasks >= NextMemoryUpgrade.RequiredCompletedTasks;
        public string NextMemoryPointRewardText => NextMemoryUpgrade == null ? string.Empty
            : NextMemoryUpgrade.AbilityPoints < 0 ? "奖励点数配置无效" : $"升级奖励：{NextMemoryUpgrade.AbilityPoints} 点，由玩家自由分配";
        public string NextTaskMultiplierText => NextMemoryUpgrade == null ? string.Empty
            : configuration.MemoryUpgrades.GetTaskMultipliers(NextMemoryUpgrade.TargetLevel)?.Description ?? "计算量 ×1 · 奖励 ×1";
        public int NextMemoryCellCount => NextMemoryUpgrade == null ? 0
            : configuration.MemoryUpgrades.GetOpenCells(NextMemoryUpgrade.TargetLevel).Count - Simulation.OpenCellCount;
        public string Message { get; private set; }
        public IReadOnlyList<string> Logs => logs;
        public event Action<TaskEvent> TaskEventRaised;
        private readonly List<string> logs = new List<string>();
        private readonly Dictionary<string, TaskDefinition> taskBases = new Dictionary<string, TaskDefinition>();
        private readonly Dictionary<string, TaskDefinition> definitions = new Dictionary<string, TaskDefinition>();
        private readonly HashSet<string> operatingTasks = new HashSet<string>();
        public bool IsOperatingTask(string requestId) => requestId != null && operatingTasks.Contains(requestId);
        private int serial;
        private System.Random requestRandom;
        private TaskDefinition[] requestTemplates;

        private void Awake() => ResetSimulation();
        private void Start() { if (canvasView != null && Simulation != null) canvasView.Initialize(this); }
        private void Update()
        {
            if (Simulation == null) return;
            AdvanceGame(Time.unscaledDeltaTime * PlaybackSpeed);
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame && IsMemoryUpgradeOpen)
                CloseMemoryUpgrade();
            if (!GameEnded && !IsModalOpen && Keyboard.current != null)
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
                IsMemoryUpgradeOpen = false;
                configuration.MemoryUpgrades.ValidateTaskMultipliers();
                configuration.TaskGeneration?.Validate(configuration.MemoryUpgrades.BoardWidth, configuration.MemoryUpgrades.BoardHeight);
                requestRandom = configuration.TaskGeneration?.CreateRandom() ?? new System.Random();
                Story = new ConversationStory(configuration.Story, configuration.TaskGeneration, requestRandom,
                    configuration.InitialAffinity, configuration.SuccessAffinity, configuration.FailureAffinity);
                Simulation = configuration.BuildSimulation(); Simulation.Paused = true; definitions.Clear(); taskBases.Clear(); operatingTasks.Clear(); logs.Clear(); serial = 0;
                MemoryLevel = configuration.MemoryUpgrades.InitialLevel; CompletedTasks = 0; AvailableAbilityPoints = 0;
                if (configuration.SecondsPerGameMonth <= 0 || float.IsNaN(configuration.SecondsPerGameMonth)
                    || float.IsInfinity(configuration.SecondsPerGameMonth)) throw new InvalidOperationException("每月游戏秒数必须是正的有限数值。");
                if (Economy != null) Economy.OnEconomyLog -= AddEconomyLog;
                Economy = configuration.BuildEconomyManager(); Economy.OnEconomyLog += AddEconomyLog;
                CurrentMonth = 1; monthTimer = 0; GameEnded = false; EndingText = string.Empty;
                foreach (var source in configuration.Requests)
                {
                    var definition = source.Build(); definitions.Add(definition.Id, definition);
                }
                requestTemplates = definitions.Values.ToArray();
                configuration.Story?.Validate(definitions.Keys);
                foreach (var definition in definitions.Values)
                    CreateTask(definition.Id + "-1", definition.Id, "1", definition);
                SelectedId = Simulation.Tasks.Keys.FirstOrDefault(); PlaybackSpeed = configuration.PlaybackSpeed;
                Message = "拖动请求到同一背包内的格子，不能跨隔板。R旋转，空格暂停，Esc取消拖拽；撤回不重置忍耐。";
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
        public void TogglePause() { if (!GameEnded && !IsModalOpen) { Simulation.Paused = !Simulation.Paused; canvasView?.Refresh(); } }
        public void CycleSpeed() => PlaybackSpeed = PlaybackSpeed == 0.5f ? 1f : PlaybackSpeed == 1 ? 0.25f : 0.5f;
        public void SetSpeed(float value) { PlaybackSpeed = value; canvasView?.Refresh(); }
        public void RotateSelected()
        {
            if (GameEnded || IsModalOpen) return;
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
            if (GameEnded || IsModalOpen) return;
            if (SelectedTask == null || !Simulation.TryWithdraw(SelectedId)) return;
            canvasView?.CancelDrag(); Message = "已撤回：进度和能力保留，忍耐继续。";
        }
        public void SubmitSelected()
        {
            if (GameEnded || IsModalOpen) return;
            if (SelectedTask != null && Simulation.TrySubmit(SelectedId))
            { ReadEvents(); Message = "已提交：释放格子并完成结算。"; canvasView?.Refresh(); }
        }
        public void AddRequest()
        {
            if (GameEnded || IsModalOpen) return;
            var task = CreateRandomRequest();
            if (task != null) SelectedId = task.Id;
            ReadEvents(); canvasView?.Refresh();
        }
        private TaskInstance CreateRandomRequest()
        {
            var operating = configuration.TaskGeneration?.PickOperatingTask(requestRandom);
            if (operating == null && requestTemplates.Length == 0) return null;
            string suffix = (++serial).ToString(), user = "new-user-" + suffix;
            var basis = operating ?? requestTemplates[requestRandom.Next(requestTemplates.Length)];
            return CreateTask("new-" + suffix, user, "extra-" + suffix, basis, operating != null);
        }
        // 有剧情时按角色派发；每个角色至多一个请求，候选不足不复制已完成会话凑数。
        private TaskInstance CreateMonthlyRequest()
        {
            var profiles = configuration.Story?.Users;
            if (profiles == null || profiles.Length == 0) return CreateRandomRequest();
            if (requestTemplates.Length == 0) return null;
            var eligible = profiles.Where(p => !Story.Finished(p.UserId))
                .Select(p => new { User = p.UserId, Last = Simulation.Tasks.Values.LastOrDefault(t => t.ConversationId == p.UserId) })
                .Where(p => p.Last == null || ((p.Last.State == RequestState.Submitted || p.Last.State == RequestState.TimedOut
                    || p.Last.State == RequestState.Closed) && !Story.AwaitingReply(p.Last.Id)))
                .OrderBy(p => p.Last?.ArrivedAt ?? double.NegativeInfinity).FirstOrDefault();
            if (eligible == null) return configuration.TaskGeneration?.HasOperatingTasks == true ? CreateRandomRequest() : null;
            var previous = eligible.Last;
            if (previous != null && previous.State != RequestState.Closed && !Simulation.TryCloseFeedback(previous.Id)) return null;
            var fallback = previous != null && taskBases.TryGetValue(previous.Id, out var original) ? original : requestTemplates[0];
            string round = "month-" + (++serial);
            return CreateTask(eligible.User + "-" + round, eligible.User, round, fallback);
        }
        private TaskInstance CreateTask(string requestId, string user, string round, TaskDefinition fallback, bool operating = false)
        {
            var basis = operating ? fallback : Story.BuildNextTask(user, definitions, fallback);
            if (basis == null) return null;
            var scaled = configuration.MemoryUpgrades.ScaleTask(MemoryLevel, basis, requestRandom);
            if (!Simulation.TryCreate(requestId, user, round, scaled, out var task)) return null;
            if (operating) operatingTasks.Add(requestId);
            taskBases.Add(requestId, basis); // 保存未乘倍率的原始任务，下一会话不能拿已放大的值再乘。
            return task;
        }
        private void RefreshMonthlyRequests()
        {
            var generation = configuration.TaskGeneration;
            if (generation == null || !generation.ShouldRefresh(CurrentMonth)) return;
            int count = 0;
            for (int i = 0; i < generation.RequestsPerRefresh; i++)
            {
                if (CreateMonthlyRequest() == null) break;
                count++;
            }
            ReadEvents();
            Message = $"第{CurrentMonth}月：新增{count}个请求，已有请求继续保留。";
            AddEconomyLog(Message);
        }
        public void NextRound()
        {
            if (GameEnded || IsModalOpen) return;
            var previous = SelectedTask;
            if (previous == null || IsOperatingTask(previous.Id) || Story.Finished(previous.ConversationId) || Story.AwaitingReply(previous.Id) || !Simulation.TryCloseFeedback(previous.Id)) return;
            var basis = taskBases.TryGetValue(previous.Id, out var original) ? original : previous.Definition;
            string round = "round-" + (++serial);
            var next = CreateTask(previous.ConversationId + "-" + round, previous.ConversationId, round, basis);
            ReadEvents();
            SelectedId = next?.Id;
        }
        public void OpenMemoryUpgrade()
        {
            if (GameEnded || IsStoryOpen) return;
            IsMemoryUpgradeOpen = true; Message = "查看新增格子，点击确认后才会扣费。倍率只影响升级后新生成的请求。"; canvasView?.CancelDrag(); canvasView?.Refresh();
        }
        public void CloseMemoryUpgrade() { IsMemoryUpgradeOpen = false; canvasView?.Refresh(); }
        public void UpgradeMemory()
        {
            if (GameEnded || IsStoryOpen) return;
            ReadEvents();
            var next = NextMemoryUpgrade;
            if (next == null) { Message = "内存已满级。"; return; }
            if (next.TaskMultipliers != null && !next.TaskMultipliers.IsValid)
            { Message = "新任务倍率范围配置无效，未升级或扣费。"; canvasView?.Refresh(); return; }
            if (next.AbilityPoints < 0 || AvailableAbilityPoints > int.MaxValue - next.AbilityPoints)
            { Message = "奖励点数配置无效或超过上限，未升级或扣费。"; canvasView?.Refresh(); return; }
            if (!CanUpgradeMemory)
            { Message = CompletedTasks < next.RequiredCompletedTasks ? $"需要累计达标提交 {next.RequiredCompletedTasks} 个任务。" : "升级资金不足。"; return; }
            int before = Simulation.OpenCellCount;
            if (!Simulation.TryUnlockCells(configuration.MemoryUpgrades.GetOpenCells(next.TargetLevel)))
            { Message = "升级格子配置越界，未扣费。"; return; }
            Economy.TrySpendFunds(next.Cost);
            MemoryLevel = next.TargetLevel; AvailableAbilityPoints += next.AbilityPoints;
            Message = $"内存升级至 Lv{MemoryLevel}：新增 {Simulation.OpenCellCount - before} 格，花费 {next.Cost}，获得 {next.AbilityPoints} 点（剩余 {AvailableAbilityPoints} 点）";
            logs.Insert(0, Message);
            if (logs.Count > 7) logs.RemoveAt(logs.Count - 1);
            canvasView?.Refresh();
        }
        public void AllocateAbility(AbilityKind ability)
        {
            if (!CanAllocateAbility || !Enum.IsDefined(typeof(AbilityKind), ability)) return;
            var current = Simulation.Model;
            var points = current.Points.ToDictionary(p => p.Key, p => p.Value);
            points.TryGetValue(ability, out double before); points[ability] = before + 1;
            var model = new ModelDefinition(current.Id, current.Interval, points);
            bool paused = Simulation.Paused, applied;
            try { Simulation.Paused = true; applied = Simulation.TryChangeModel(model); }
            finally { Simulation.Paused = paused; }
            if (!applied) return;
            AvailableAbilityPoints--;
            Message = $"{AbilityText(ability)}每周期产出 {before:0.##} → {before + 1:0.##}，剩余 {AvailableAbilityPoints} 点。";
            canvasView?.Refresh();
        }

        public void ChooseReply(int index)
        {
            string user = Story.UserId, request = Story.RequestId;
            if (!Story.Choose(index)) return;
            if (Story.Finished(user)) { Simulation.TryCloseFeedback(request); ReadEvents(); }
            Message = Story.Finished(user) ? Story.Name(user) + "的剧情已完成。" :
                Story.IsReply && Story.RequestId == request ? "请继续选择，完成本次会话。" : "本次会话结束；达标推进，失败重试，下一次月刷新会自动派发。";
            canvasView?.Refresh();
        }
        public void OpenConversationHistory()
        {
            if (SelectedTask == null || GameEnded || IsMemoryUpgradeOpen) return;
            if (IsOperatingTask(SelectedId))
            { Message = "经营委托只做任务结算，不包含角色剧情或亲密度。"; canvasView?.Refresh(); return; }
            Story.OpenHistory(SelectedTask.ConversationId); canvasView?.CancelDrag(); canvasView?.Refresh();
        }
        public void CloseConversationHistory() { Story.CloseHistory(); canvasView?.Refresh(); }
        public void AddFunds(int amount) { Economy.AddFunds(amount); canvasView?.Refresh(); }
        // 分段推进到每个月底，先处理任务结算，再扣维护费，跨多月也不会漏结算。
        public void AdvanceGame(double delta)
        {
            if (double.IsNaN(delta) || double.IsInfinity(delta) || delta < 0) throw new ArgumentOutOfRangeException(nameof(delta));
            if (Simulation == null || GameEnded || Simulation.Paused || IsModalOpen) return;
            while (delta > 0 && !GameEnded && !IsModalOpen)
            {
                double step = Math.Min(delta, configuration.SecondsPerGameMonth - monthTimer);
                Simulation.Advance(step); ReadEvents(); monthTimer += step; delta -= step;
                if (monthTimer + 1e-9 < configuration.SecondsPerGameMonth) continue;
                monthTimer = 0;
                if (Economy.SettleMonth(configuration.MonthlyMaintenanceFee)) { CurrentMonth++; RefreshMonthlyRequests(); }
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
                int income = Economy.Settle(item);
                if (Simulation.Tasks.TryGetValue(item.RequestId, out var task))
                {
                    if (IsOperatingTask(task.Id))
                    {
                        // 经营任务直接完成反馈，不占剧情，不创建角色亲密度记录。
                        if (item.Kind == TaskEventKind.Submitted || item.Kind == TaskEventKind.TimedOut)
                            Simulation.TryCloseFeedback(task.Id);
                    }
                    else
                    {
                        if (item.Kind == TaskEventKind.Arrived) Story.Arrived(task);
                        if (item.Kind == TaskEventKind.TimedOut) Story.TimedOut(task);
                        if (item.Kind == TaskEventKind.Submitted) { IsMemoryUpgradeOpen = false; Story.Submitted(task); canvasView?.CancelDrag(); }
                    }
                }
                logs.Insert(0, $"{item.GameTime:0.0}s  {item.RequestId}  {EventText(item.Kind)}"
                    + (item.Kind == TaskEventKind.Submitted || item.Kind == TaskEventKind.TimedOut ? $"  token={item.BillableTokens:0.##}" : ""));
                if (logs.Count > 7) logs.RemoveAt(logs.Count - 1);
                if (item.Kind == TaskEventKind.Submitted || item.Kind == TaskEventKind.TimedOut) canvasView?.ShowSettlement(item, income);
                TaskEventRaised?.Invoke(item);
            }
        }
        public static string AbilityText(AbilityKind kind) => kind == AbilityKind.Writing ? "写作" : kind == AbilityKind.Code ? "代码" : kind == AbilityKind.Reasoning ? "推理" : "检索";
        public static string QualityText(Quality quality) => quality == Quality.Excellent ? "优秀" : quality == Quality.Complete ? "完成" : "未达标";
        public static string StateText(RequestState state) => new[] { "等待摆放", "运行中", "已撤回", "算完待确认", "已提交待回复", "超时失败", "反馈结束" }[(int)state];
        public static string FailureText(PlacementFailure failure) => new[] { "无", "任务不存在", "当前状态不可摆放", "越界", "未开放格", "与其他任务重叠", "跨越隔板，任务必须完整放在同一背包内" }[(int)failure];
        private static string EventText(TaskEventKind kind) => new[] { "请求到达", "合法摆放", "撤回", "算完判质", "提交结算", "超时结算", "反馈结束" }[(int)kind];
    }
}
