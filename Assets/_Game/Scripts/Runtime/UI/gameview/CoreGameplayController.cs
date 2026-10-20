using System;
using System.Collections.Generic;
using System.Linq;
using CallmeCatgirl.Gameplay.TaskGrid;
using CallmeCatgirl.UI;
using CallmeCatgirl.Gameplay.Training;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CallmeCatgirl.Gameplay
{
    /// <summary>Owns the simulation and commands; visual layout is serialized in the Canvas scene.</summary>
    public sealed partial class CoreGameplayController : MonoBehaviour
    {
        [SerializeField] private GameplayConfiguration configuration;
        [SerializeField] private CoreGameplayCanvasView canvasView;
        [SerializeField] private TrainingController training;
        [SerializeField] private TrainingPanel trainingPanel;
        public TrainingController Training => training;
        public bool TrainingEnabled => training != null && training.IsBound;
        public bool IsTrainingOpen => trainingPanel != null && trainingPanel.IsOpen;
        public TaskSimulation Simulation { get; private set; }
        public ConversationStory Story { get; private set; }
        public bool IsStoryOpen => Story != null && Story.IsOpen;
        [SerializeField] private MemoryInstallGame memoryInstallGame;
        private MemoryUpgradeConfiguration.UpgradeStep pendingMemoryUpgrade;
        private int installAttempt;
        private Action installCompletionHandler;
        public bool IsMemoryInstallPending => pendingMemoryUpgrade != null;
        public bool IsMemoryInstallOpen => memoryInstallGame != null && memoryInstallGame.IsOpen;
        public bool IsMemoryUpgradeOpen { get; private set; }
        public bool IsModalOpen => CompletionBlocksInput || IsMemoryInstallPending || IsMemoryInstallOpen || IsMemoryUpgradeOpen || IsTrainingOpen || (canvasView!=null&&canvasView.WindowLayers!=null&&canvasView.WindowLayers.IsBlockingInput);
        public bool CanPurchasePolicy => !GameEnded && !CompletionBlocksInput && !IsMemoryInstallPending && !IsMemoryInstallOpen
            && !IsMemoryUpgradeOpen && !IsTrainingOpen && (canvasView?.WindowLayers == null
                || !canvasView.WindowLayers.IsBlockingInput || canvasView.WindowLayers.ActiveWindow == "ProgressionOverlay");
        public IReadOnlyList<Cell> NextMemoryCells => NextMemoryUpgrade == null ? Array.Empty<Cell>()
            : MemoryOutline(NextMemoryUpgrade.TargetLevel);
        public string SelectedId { get; private set; }
        public TaskInstance SelectedTask => SelectedId != null && Simulation.Tasks.TryGetValue(SelectedId, out var task) ? task : null;
        public float PlaybackSpeed { get; private set; }
        public int MemoryLevel { get; private set; }
        public string ModelName => TrainingEnabled ? training.Session.ActiveModel?.Name ?? "尚无模型"
            : configuration.MemoryUpgrades.GetModelName(MemoryLevel, Simulation.Model.Id);
        public EconomyManager Economy { get; private set; }
        public int Funds => Economy.CurrentFunds;
        public bool DebugIgnoreCosts { get; private set; }
        public bool CanAfford(int cost) => DebugIgnoreCosts || Funds >= cost;
        public int CurrentMonth { get; private set; }
        public int DebtAmount => Economy.DebtAmount;
        public bool IsInGracePeriod => Economy.IsInGracePeriod;
        public bool GameEnded { get; private set; }
        public string EndingText { get; private set; }
        public int DaysPerMonth => configuration.DaysPerGameMonth;
        public double SecondsPerDay => configuration.SecondsPerGameDay;
        public int CurrentDay => GameEnded ? DaysPerMonth : Math.Min(DaysPerMonth, (int)(monthTimer / SecondsPerDay) + 1);
        public double DayTimeRemaining => GameEnded ? 0 : Math.Max(0, SecondsPerDay - monthTimer % SecondsPerDay);
        public double MonthTimeRemaining => GameEnded ? 0 : Math.Max(0, configuration.SecondsPerGameMonth - monthTimer);
        public int MonthlyMaintenanceFee => configuration.MonthlyMaintenanceFee;
        private double monthTimer;
        public int CompletedTasks { get; private set; }
        public MemoryUpgradeConfiguration.UpgradeStep NextMemoryUpgrade
            => MemoryLevel == 0 ? firstMemoryInstallation : configuration.MemoryUpgrades.TryGetNextLevel(MemoryLevel, out var step) ? step : null;
        public bool CanUpgradeMemory => isActiveAndEnabled && !GameEnded && Economy != null && !Economy.IsBankrupt
            && !IsMemoryInstallPending && !IsMemoryInstallOpen && NextMemoryUpgrade != null
            && (NextMemoryUpgrade.TaskMultipliers == null || NextMemoryUpgrade.TaskMultipliers.IsValid) && NextMemoryUpgrade.Cost >= 0
            && CanAfford(NextMemoryUpgrade.Cost) && CompletedTasks >= NextMemoryUpgrade.RequiredCompletedTasks;
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
        private readonly int[] fillerGenerated = new int[5];
        private System.Random requestRandom;
        private TaskDefinition[] requestTemplates;

        private void Awake() => ResetSimulation();
        private void Start()
        {
            if (canvasView != null && Simulation != null) canvasView.Initialize(this);
            string slot=SceneNavigation.ConsumeContinueSlot();
            if (slot != null) { ConfigureSaveSlot(slot); ContinueRun(); }
            InitializeSaveButtons();
            EnsureCompletionViews();
        }
        private void Update()
        {
            if (Simulation == null) return;
            // 看门狗：安装面板被外部关闭/禁用或引用丢失时，中止挂起的升级意图，不产生任何效果。
            if (pendingMemoryUpgrade != null && (memoryInstallGame == null || !memoryInstallGame.enabled
                || !memoryInstallGame.IsOpen || !memoryInstallGame.gameObject.activeInHierarchy))
                CancelPendingMemoryInstall();
            if (!ManualCommandClock) AdvanceGame(Time.unscaledDeltaTime * PlaybackSpeed);
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) systemMenu?.HandleEscape();
            if (Keyboard.current != null && !IsDebugShortcutEditing())
            {
                if (Keyboard.current[Key.Digit1].wasPressedThisFrame) SkipCurrentNarrative();
                if (Keyboard.current[Key.Digit2].wasPressedThisFrame) ToggleDebugIgnoreCosts();
                if (Keyboard.current[Key.Digit3].wasPressedThisFrame) FinishAllTasks();
            }
            if (!GameEnded && !IsModalOpen && !CallmeCatgirl.Debugging.InlineDebugInput.IsEditing && Keyboard.current != null)
            {
                if (Keyboard.current.rKey.wasPressedThisFrame) RotateSelected();
                if (Keyboard.current.spaceKey.wasPressedThisFrame) TogglePause();
            }
            ReadEvents();
            ProcessCompletion();
            UpdateEndingScene();
            if (canvasView != null) canvasView.Refresh();
        }

        public void ResetSimulation()
        {
            if (configuration == null) { Debug.LogError("CoreGameplayController requires a GameplayConfiguration.", this); enabled = false; return; }
            try
            {
                CancelPendingMemoryInstall();
                ResetEndingScene();
                BeginCompletionRun();
                IsMemoryUpgradeOpen = false;
                configuration.MemoryUpgrades.ValidateTaskMultipliers();
                configuration.TaskGeneration?.Validate(configuration.MemoryUpgrades.BoardWidth, configuration.MemoryUpgrades.BoardHeight);
                ManualCommandClock=false; runId=Guid.NewGuid().ToString("D"); saveService=null; saveContentRevision=null;
                int seed=configuration.TaskGeneration == null ? -1 : configuration.TaskGeneration.RandomSeed;
                requestRandom = new CallmeCatgirl.Persistence.SaveableRandom(seed < 0 ? Guid.NewGuid().GetHashCode() : seed);
                var ordinaryPool = OrdinaryStagePool.Load() ?? configuration.OrdinaryDialoguePool;
                Story = new ConversationStory(configuration.Story, configuration.TaskGeneration, requestRandom, ordinaryPool,
                    configuration.InitialAffinity, configuration.SuccessAffinity, configuration.FailureAffinity);
                Story.StageProvider = () => MemoryLevel;
                var configuredSimulation = configuration.BuildSimulation();
                Simulation = new TaskSimulation(configuredSimulation.Width, configuredSimulation.Height, Array.Empty<Cell>(),
                    new ModelDefinition(configuredSimulation.Model.Id, configuredSimulation.Model.Interval, new Dictionary<AbilityKind,double>()), configuration.MemoryUpgrades.GetCompartments());
                Simulation.Paused = true; definitions.Clear(); taskBases.Clear(); operatingTasks.Clear(); logs.Clear(); serial = 0;
                Array.Clear(fillerGenerated, 0, fillerGenerated.Length);
                MemoryLevel = 0; CompletedTasks = 0;
                if (configuration.DaysPerGameMonth < 1 || configuration.SecondsPerGameDay <= 0
                    || configuration.SecondsPerGameMonth <= 0 || float.IsNaN(configuration.SecondsPerGameMonth)
                    || float.IsInfinity(configuration.SecondsPerGameMonth)) throw new InvalidOperationException("每天游戏秒数必须为正数、维护周期天数至少为1，周期时长必须有限。");
                if (Economy != null) Economy.OnEconomyLog -= AddEconomyLog;
                Economy = configuration.BuildEconomyManager(); Economy.OnEconomyLog += AddEconomyLog;
                DebugIgnoreCosts = false; Economy.DebugIgnoreSpending = false;
                Economy.TrySpendFunds(Economy.CurrentFunds);
                Story.Economy = Economy;
                CurrentMonth = 1; monthTimer = 0; GameEnded = false; EndingText = string.Empty;
                lastAutoSaveDay = AbsoluteGameDay; endingLoadMessage = string.Empty;
                foreach (var source in configuration.Requests)
                {
                    var definition = source.Build(); definitions.Add(definition.Id, definition);
                }
                requestTemplates = definitions.Values.ToArray();
                configuration.Story?.Validate(definitions.Keys);
                ordinaryPool?.Validate(definitions.Keys);
                // The doctor introduction owns the first request. Normal arrivals unlock after that tutorial.
                SelectedId = Simulation.Tasks.Keys.FirstOrDefault(); PlaybackSpeed = configuration.PlaybackSpeed;
                if (training != null)
                {
                    training.Changed -= RefreshTraining;
                    trainingPanel?.Close();
                    training.Bind(Simulation, Economy,
                        p => Simulation.TryChangeExecutionProfile(p.Model, p.TokensPerSecond, p.OpenCells, p.Architecture),
                        rate => Simulation.TrySetTokenThroughput(rate),
                        () => CapacityForNarrative(Narrative, MemoryLevel),
                        () => !GameEnded && !IsMemoryUpgradeOpen && !IsMemoryInstallOpen,
                        compose: model => ComposePolicyAbilities(model,PolicyTree),
                        initialBaseDefinition: Simulation.Model,
                        policies: () => TrainingModifiers(PolicyTree), currentStage: () => Math.Max(1, MemoryLevel));
                    if (!Simulation.TrySetTokenThroughput(training.Session.TokensPerSecond)) throw new InvalidOperationException("Training speed initialization failed.");
                    training.Changed += RefreshTraining;
                }
                Message = "拖动请求到同一背包内的格子，不能跨隔板。R旋转，空格暂停，Esc取消拖拽；撤回不重置忍耐。";
                ReadEvents(); BindDebugCommands();
                canvasView?.SetManualPlanningVisual(false);
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
        public void TogglePause() { if (!GameEnded && !IsModalOpen) { Simulation.Paused = !Simulation.Paused; canvasView?.SetManualPlanningVisual(Simulation.Paused); canvasView?.Refresh(); } }
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
            bool trainingRequest=SelectedTask!=null && TrainingEnabled && training.IsTrainingRequest(SelectedId);
            if (GameEnded || (IsModalOpen && !(trainingRequest && training.CanManage))) return;
            if (SelectedTask != null && TrainingEnabled && training.IsTrainingRequest(SelectedId))
            {
                training.CompleteTraining(SelectedId); ReadEvents(); RefreshTraining(); return;
            }
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
        public bool IsFillerTask(string requestId) => requestId != null && Simulation != null
            && Simulation.Tasks.TryGetValue(requestId, out var task) && task.IsFiller;
        public string VisitorDisplayName(TaskInstance task) => task != null && task.IsFiller ? task.Visitor.DisplayName : null;
        public string AvatarConversationId(TaskInstance task) => task != null && task.IsFiller ? task.Visitor.SourceCustomerId : task?.ConversationId;
        public string VisitorSummary(TaskInstance task) => task == null || !task.IsFiller ? null
            : task.Visitor.DisplayName + " · 轻量来客\n" + StateText(task.State) + "\n\n" + task.Visitor.SourceText;
        // Story requests draw first; fillers only repair a concurrent deficit and never reserve story customers.
        private void EnsureVisitorQueue()
        {
            if (GameEnded || Simulation == null || configuration == null) return;
            // Daily mode has one shared arrival budget; do not top it up again, including after RAM upgrades.
            if (configuration.TaskGeneration?.RefreshByDay == true) return;
            if (Narrative != null && !Narrative.OrdinaryRequestsUnlocked) return;
            int stage = Math.Max(1, Math.Min(5, MemoryLevel));
            OrdinaryStagePool.StageRule rule;
            if (!OrdinaryStagePool.TryGetRule(stage, out rule)) return;
            int unfinished = VisitorQueuePolicy.UnfinishedCount(Simulation.Tasks.Values);
            int arrivals = VisitorQueuePolicy.ArrivalCount(stage, rule.TargetConcurrent, rule.TargetRequests,
                fillerGenerated[stage - 1], unfinished);
            if (arrivals <= 0) return;
            var sources = LightweightVisitorQueue.Sources(configuration.Story, configuration.OrdinaryDialoguePool,
                OrdinaryStagePool.Load(), stage);
            if (sources.Count == 0) return;
            for (int i = 0; i < arrivals; i++)
                if (CreateVisitorRequest(stage, rule, sources) == null) break;
        }
        // Fallback visits consume the same daily slot as an ordinary scheduled request.
        private TaskInstance CreateDailyVisitor()
        {
            int stage = Math.Max(1, Math.Min(5, MemoryLevel));
            if (!OrdinaryStagePool.TryGetRule(stage, out var rule)
                || VisitorQueuePolicy.ArrivalCount(stage, int.MaxValue, rule.TargetRequests, fillerGenerated[stage - 1], 0) <= 0) return null;
            var sources = LightweightVisitorQueue.Sources(configuration.Story, configuration.OrdinaryDialoguePool,
                OrdinaryStagePool.Load(), stage);
            return sources.Count == 0 ? null : CreateVisitorRequest(stage, rule, sources);
        }
        private TaskInstance CreateVisitorRequest(int stage, OrdinaryStagePool.StageRule rule, IReadOnlyList<VisitorSource> sources)
        {
            var source = sources[requestRandom.Next(sources.Count)];
            int serial = fillerGenerated[stage - 1] + 1;
            var shape = configuration.TaskGeneration != null ? configuration.TaskGeneration.PickShape(requestRandom)
                : new[] { new Cell(0, 0) };
            string requestId = VisitorQueuePolicy.RequestId(stage, serial);
            var definition = LightweightVisitorQueue.Build(requestId, source, rule, shape);
            var visitor = new VisitorIdentity(source.DisplayName, source.Text, source.RequestFamily, source.CustomerId);
            if (!Simulation.TryCreate(requestId, VisitorQueuePolicy.ConversationId, requestId, definition, visitor, out var task))
                return null;
            taskBases[requestId] = definition;
            fillerGenerated[stage - 1] = serial;
            return task;
        }
        private void RestoreFillerCounters(int[] saved, TaskSimulation simulation)
        {
            var merged = VisitorQueuePolicy.MergeCounters(saved, simulation?.Tasks.Values);
            Array.Copy(merged, fillerGenerated, fillerGenerated.Length);
        }
        // 有剧情时按角色派发；每个角色至多一个请求，候选不足不复制已完成会话凑数。
        private TaskInstance CreateScheduledRequest()
        {
            var profiles = configuration.Story?.Users;
            if (profiles == null || profiles.Length == 0) return CreateRandomRequest();
            if (requestTemplates.Length == 0) return null;
            var eligible = profiles.Where(p => Story.PrepareSlot(p.UserId))
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
            if (!operating && configuration.OpeningBalance != null && !Story.UsesStagePool) basis = configuration.OpeningBalance.Apply(user, Story.SessionNumber(user), basis);
            var scaled = configuration.MemoryUpgrades.ScaleTask(MemoryLevel, basis, requestRandom);
            if (!Simulation.TryCreate(requestId, user, round, scaled, out var task)) return null;
            if (operating) operatingTasks.Add(requestId);
            taskBases.Add(requestId, basis); // 保存未乘倍率的原始任务，下一会话不能拿已放大的值再乘。
            return task;
        }
        private void RefreshScheduledRequests(bool daily)
        {
            if (Narrative != null && !Narrative.OrdinaryRequestsUnlocked) return;
            var generation = configuration.TaskGeneration;
            if (generation == null || generation.RefreshByDay != daily) return;
            long day = (CurrentMonth - 1L) * DaysPerMonth + CurrentDay;
            if (daily ? !generation.ShouldRefreshDay(day) : !generation.ShouldRefresh(CurrentMonth)) return;
            int removed = daily ? DiscardPreviousDayCandidates() : 0;
            int count = 0;
            double expected = generation.RequestsPerRefresh * (daily ? 1 : PolicyTree?.Effects.CustomerArrivalMultiplier ?? 1);
            int arrivals = daily ? generation.RequestsPerRefresh : (int)Math.Min(1000, Math.Floor(expected));
            if (expected - Math.Floor(expected) > 0 && requestRandom.NextDouble() < expected - Math.Floor(expected)) arrivals++;
            for (int i = 0; i < arrivals; i++)
            {
                if (CreateScheduledRequest() == null && (!daily || CreateDailyVisitor() == null)) break;
                count++;
            }
            ReadEvents();
            Message = (daily ? $"第{CurrentMonth}月第{CurrentDay}天" : $"第{CurrentMonth}月") + $"：新增{count}个请求，"
                + (daily ? $"移除{removed}个上一天未处理候选；内存中的请求继续保留。" : "已有请求继续保留。");
            AddEconomyLog(Message);
        }
        private int DiscardPreviousDayCandidates()
        {
            ReadEvents();
            double dayStartedAt = Simulation.GameTime - monthTimer % SecondsPerDay;
            int removed = 0;
            foreach (var task in Simulation.Tasks.Values.Where(t => t.ArrivedAt < dayStartedAt
                && (t.State == RequestState.Waiting || t.State == RequestState.Withdrawn)).ToArray())
            {
                if ((TrainingEnabled && training.IsTrainingRequest(task.Id)) || IsNarrativeContact(task.ConversationId)
                    || task.Id.StartsWith("narrative-", StringComparison.Ordinal) || Story.AwaitingReply(task.Id)) continue;
                if (!Simulation.TryDiscardCandidate(task.Id)) continue;
                Story.ForgetCandidate(task);
                taskBases.Remove(task.Id);
                operatingTasks.Remove(task.Id);
                if (SelectedId == task.Id) SelectedId = null;
                removed++;
            }
            return removed;
        }
        public void NextRound()
        {
            if (GameEnded || IsModalOpen) return;
            var previous = SelectedTask;
            if (previous != null && TrainingEnabled && training.IsTrainingRequest(previous.Id)) return;
            if (previous == null || previous.IsFiller || IsOperatingTask(previous.Id) || Story.Finished(previous.ConversationId) || Story.AwaitingReply(previous.Id) || !Simulation.TryCloseFeedback(previous.Id)) return;
            var basis = taskBases.TryGetValue(previous.Id, out var original) ? original : previous.Definition;
            string round = "round-" + (++serial);
            var next = CreateTask(previous.ConversationId + "-" + round, previous.ConversationId, round, basis);
            ReadEvents();
            SelectedId = next?.Id;
        }

        public TaskInstance LatestConversationTask(string conversationId) => Simulation.Tasks.Values
            .Where(t => t.ConversationId == conversationId)
            .OrderByDescending(t => t.State != RequestState.Closed)
            .ThenByDescending(t => t.ArrivedAt).ThenByDescending(t => t.Id, StringComparer.Ordinal).FirstOrDefault();

        private void ContinueConversationAfterReply(string user, string request)
        {
            if (Story.Finished(user) || !Simulation.Tasks.TryGetValue(request, out var previous)) return;
            // Closing feedback is the once-only transition. Browsing history never calls this path.
            if (!Simulation.TryCloseFeedback(request)) return;
            var basis = taskBases.TryGetValue(request, out var original) ? original : previous.Definition;
            string round = "round-" + (++serial);
            var next = CreateTask(user + "-" + round, user, round, basis);
            ReadEvents();
            if (next != null && Story.UserId == user && !Story.IsReply) SelectedId = next.Id;
        }
        public void OpenMemoryUpgrade()
        {
            if (!isActiveAndEnabled || GameEnded || IsTrainingOpen || IsMemoryInstallOpen) return;
            IsMemoryUpgradeOpen = true; Message = "查看新增格子，确认后完成或跳过安装小游戏才会扣费。倍率只影响升级后新生成的请求。"; canvasView?.CancelDrag(); canvasView?.Refresh();
        }
        public void CloseMemoryUpgrade()
        {
            if (pendingMemoryUpgrade != null) CancelPendingMemoryInstall();
            else if (IsMemoryInstallOpen) return;
            IsMemoryUpgradeOpen = false; canvasView?.Refresh();
        }
        public void UpgradeMemory()
        {
            if (!isActiveAndEnabled || GameEnded || IsTrainingOpen || IsMemoryInstallOpen || pendingMemoryUpgrade != null) return;
            ReadEvents();
            var next = NextMemoryUpgrade;
            if (next == null) { Message = "内存已满级。"; return; }
            if (next.TaskMultipliers != null && !next.TaskMultipliers.IsValid)
            { Message = "新任务倍率范围配置无效，未升级或扣费。"; canvasView?.Refresh(); return; }
            if (!CanUpgradeMemory)
            { Message = CompletedTasks < next.RequiredCompletedTasks ? $"需要累计达标提交 {next.RequiredCompletedTasks} 个任务。" : "升级资金不足。"; return; }
            IReadOnlyList<Cell> outline;
            try { outline = MemoryOutline(next.TargetLevel); }
            catch (Exception error) { Message = "升级格子配置无效：" + error.Message; return; }
            if (!MemoryOutlineValid(outline)) { Message = "升级格子配置越界，未扣费。"; return; }
            if (memoryInstallGame == null) { Message = "缺少内存安装界面，未升级或扣费。"; canvasView?.Refresh(); return; }
            // 先打开小游戏再记录挂起意图：界面损坏抛异常时不会留下悬空意图。
            int attempt = ++installAttempt; // 不可变的本次尝试标识：过期回调不得消费新意图。
            UnbindInstallCompletion();
            try { memoryInstallGame.OpenGame(); }
            catch (Exception error)
            {
                memoryInstallGame.ResetGame();
                Message = "内存安装界面打开失败：" + error.Message + "，未扣费。"; canvasView?.Refresh(); return;
            }
            pendingMemoryUpgrade = next;
            installCompletionHandler = () => CommitMemoryUpgrade(attempt);
            memoryInstallGame.InstallCompleted += installCompletionHandler;
            Message = "完成内存安装小游戏，或点击跳过按钮，才会扣费并完成升级。";
            canvasView?.Refresh();
        }
        private bool MemoryOutlineValid(IReadOnlyList<Cell> cells)
        {
            if (cells == null) return false;
            foreach (var cell in cells)
                if (cell.X < 0 || cell.Y < 0 || cell.X >= Simulation.Width || cell.Y >= Simulation.Height) return false;
            return true;
        }
        // 完成与跳过共用同一事务：先校验尝试标识并清除挂起意图，重复或过期回调均为无操作。
        private void CommitMemoryUpgrade(int attempt)
        {
            if (attempt != installAttempt || pendingMemoryUpgrade == null || !isActiveAndEnabled
                || memoryInstallGame == null || !memoryInstallGame.IsComplete) return;
            var step = pendingMemoryUpgrade;
            pendingMemoryUpgrade = null;
            UnbindInstallCompletion();
            if (step == null || Simulation == null || Economy == null) return;
            string failure = null;
            if (GameEnded) failure = "经营已结束";
            else if (!ReferenceEquals(NextMemoryUpgrade, step)) failure = "升级目标已变化";
            else if (step.TaskMultipliers != null && !step.TaskMultipliers.IsValid) failure = "新任务倍率范围配置无效";
            else if (step.Cost < 0) failure = "升级价格配置无效";
            else if (CompletedTasks < step.RequiredCompletedTasks) failure = $"需要累计达标提交 {step.RequiredCompletedTasks} 个任务";
            else if (Economy.IsBankrupt || !CanAfford(step.Cost)) failure = "升级资金不足";
            IReadOnlyList<Cell> outline = null;
            bool geometryFailed = false;
            if (failure == null)
            {
                try { outline = MemoryOutline(step.TargetLevel); }
                catch (Exception error) { failure = "升级格子配置无效：" + error.Message; geometryFailed = true; }
                if (failure == null && !MemoryOutlineValid(outline)) { failure = "升级格子配置越界"; geometryFailed = true; }
            }
            int before = Simulation.OpenCellCount;
            if (failure == null && !Economy.TrySpendFunds(step.Cost)) failure = "升级资金不足";
            if (failure == null && !Simulation.TryUnlockCells(outline))
            {
                Economy.AddFunds(step.Cost); // 退款：绝不出现扣费却未升级。
                failure = "升级格子配置越界"; geometryFailed = true;
            }
            if (failure != null)
            {
                // 几何配置损坏时刷新视图会再次求值同一配置，保持原越界路径风格不刷新。
                Message = $"内存升级未完成：{failure}，未扣费，可再次尝试。";
                if (!geometryFailed) canvasView?.Refresh();
                return;
            }
            MemoryLevel = step.TargetLevel;
            Narrative?.ObserveMemoryCount(MemoryLevel);
            EnsureVisitorQueue();
            Message = $"内存升级至 Lv{MemoryLevel}：新增 {Simulation.OpenCellCount - before} 格，花费 {step.Cost}";
            logs.Insert(0, Message);
            if (logs.Count > 7) logs.RemoveAt(logs.Count - 1);
            canvasView?.Refresh();
        }
        // 重置、关闭、禁用、切场景或读档成功都会中止未完成的安装意图，不产生任何费用或升级。
        private void CancelPendingMemoryInstall()
        {
            bool wasPending = pendingMemoryUpgrade != null;
            pendingMemoryUpgrade = null;
            installAttempt++; // 使任何已捕获的旧完成回调立即失效。
            UnbindInstallCompletion();
            if (memoryInstallGame != null) memoryInstallGame.ResetGame();
            if (wasPending) Message = "内存安装已取消，未扣费升级。";
        }
        private void UnbindInstallCompletion()
        {
            if (installCompletionHandler != null && memoryInstallGame != null)
                memoryInstallGame.InstallCompleted -= installCompletionHandler;
            installCompletionHandler = null;
        }
        public TaskInstance ConversationTask => Story == null || string.IsNullOrEmpty(Story.UserId) ? null
            : LatestConversationTask(Story.UserId);
        public IReadOnlyList<StoryConfiguration.ReplyChoice> ConversationReplyChoices => Story.IsReply ? Story.Choices
            : Story.PreviewReplyChoices(ConversationTask);
        public void ChooseReply(int index) => ChooseReplyAtRevision(index, Story.Revision);
        public void ChooseReplyAtRevision(int index, int expectedRevision)
        {
            if (GameEnded || IsModalOpen || expectedRevision != Story.Revision) return;
            var options = ConversationReplyChoices;
            if (index < 0 || index >= options.Count) return;
            var chosen = options[index];
            if (!Story.IsReply)
            {
                var ready = ConversationTask;
                if (ready == null || ready.State != RequestState.Ready || !Simulation.TrySubmit(ready.Id)) return;
                ReadEvents(); // Normal quality, payment, lifecycle and reply preparation, exactly once.
                if (!Story.IsReply || Story.RequestId != ready.Id) return;
                index = Story.Choices.ToList().IndexOf(chosen);
                if (index < 0 && Story.Choices.Count == 1 && string.IsNullOrEmpty(chosen.Label)
                    && Story.Choices[0].NextNodeId == chosen.NextNodeId) index = 0;
                if (index < 0) return;
                expectedRevision = Story.Revision;
            }
            string user = Story.UserId, request = Story.RequestId;
            string node = Story.CurrentCorpusNodeId, choice = Story.CorpusChoiceId(Story.Choices[index]);
            if (!Story.ChooseAtRevision(index, expectedRevision)) return;
            bool continuingRequest = Story.IsReply && Story.RequestId == request;
            double corpusReward = 0;
            if (TrainingEnabled && !continuingRequest && !training.Session.Corpus.Any(r => r.RequestId == request)
                && training.RecordAcceptedReply(request, node, choice) == TrainingResult.Success)
                corpusReward = training.Session.Corpus.Single(r => r.RequestId == request).Scores.Values.Sum();
            if (!continuingRequest && Story.Finished(user)) { Simulation.TryCloseFeedback(request); ReadEvents(); }
            else if (!continuingRequest) ContinueConversationAfterReply(user, request);
            Message = continuingRequest ? "请继续选择，完成本次会话。" :
                Story.Finished(user) ? Story.Name(user) + "的对话已结束。" : "本次会话结束，可开始下一次会话。";
            if (corpusReward > 0) Message += " 对话语料 +" + corpusReward.ToString("0.###") + " 点。";
            canvasView?.Refresh();
        }
        public void OpenConversationHistory()
        {
            if (SelectedTask == null || GameEnded || IsModalOpen) return;
            if (IsNarrativeContact(SelectedTask.ConversationId)) { OpenNarrativeContact(SelectedTask.ConversationId); return; }
            if (SelectedTask.IsFiller)
            { Message = "轻量来客只做经营结算，不包含角色剧情或亲密度。"; canvasView?.Refresh(); return; }
            if (IsOperatingTask(SelectedId))
            { Message = "经营委托只做任务结算，不包含角色剧情或亲密度。"; canvasView?.Refresh(); return; }
            if (TrainingEnabled && training.IsTrainingRequest(SelectedId)) return;
            Story.OpenConversation(SelectedTask, Story.RequestId != null && Simulation.Tasks.TryGetValue(Story.RequestId, out var activeReply) ? activeReply : null); canvasView?.RevealConversation(); canvasView?.CancelDrag(); canvasView?.Refresh();
        }
        public void CloseConversationHistory() { Story.CloseHistory(); canvasView?.Refresh(); }
        public void AddFunds(int amount) { Economy.AddFunds(amount); canvasView?.Refresh(); }
        // 按天分段刷新任务；跨月底先结算维护费，破产时不再派发任务。
        public void AdvanceGame(double delta)
        {
            if (double.IsNaN(delta) || double.IsInfinity(delta) || delta < 0) throw new ArgumentOutOfRangeException(nameof(delta));
            if (Simulation == null || GameEnded || Simulation.Paused || IsModalOpen) return;
            while (delta > 0 && !GameEnded && !Simulation.Paused && !IsModalOpen)
            {
                int dayBefore = CurrentDay;
                double step = Math.Min(delta, Math.Min(DayTimeRemaining, configuration.SecondsPerGameMonth - monthTimer));
                Simulation.Advance(step); ReadEvents(); monthTimer += step; delta -= step;
                if (monthTimer + 1e-9 < configuration.SecondsPerGameMonth)
                {
                    if (CurrentDay != dayBefore) { RefreshScheduledRequests(true); EnsureVisitorQueue(); }
                    continue;
                }
                monthTimer = 0;
                if (Economy.SettleMonth(configuration.MonthlyMaintenanceFee)) { CurrentMonth++; RefreshScheduledRequests(false); RefreshScheduledRequests(true); EnsureVisitorQueue(); }
                else
                {
                    GameEnded = true; Simulation.Paused = true;
                        EndReason = CallmeCatgirl.Persistence.RunEndReasons.Bankruptcy;
                    EndingText = $"宽限期结束，无法偿还欠款 {DebtAmount} 和本月维护费 {configuration.MonthlyMaintenanceFee}。服务器停机，经营失败。";
                    AddEconomyLog(EndingText);
                }
            }
            TryAutoSaveDay();
            canvasView?.Refresh();
        }
        private void AddEconomyLog(string content)
        {
            logs.Insert(0, content); if (logs.Count > 7) logs.RemoveAt(logs.Count - 1);
        }
        private void ReadEvents()
        {
            var notifications = new List<Action>();
            foreach (var item in Simulation.DrainEvents())
            {
                if (TrainingEnabled && training.IsTrainingRequest(item.RequestId))
                {
                    notifications.Add(() => training.ObserveTaskEvent(item));
                    QueueTaskObservers(item, notifications); continue;
                }
                if (item.Kind == TaskEventKind.Submitted && item.Result != null && item.Result.Overall != Quality.BelowStandard) CompletedTasks++;
                int income = Economy.Settle(item);
                if (Simulation.Tasks.TryGetValue(item.RequestId, out var task))
                {
                    if (task.IsFiller)
                    {
                        // Lightweight visitors settle like operating work: no story, affinity, corpus or ending roll.
                        if (item.Kind == TaskEventKind.Submitted || item.Kind == TaskEventKind.TimedOut)
                            Simulation.TryCloseFeedback(task.Id);
                    }
                    else if (IsOperatingTask(task.Id))
                    {
                        // 经营任务直接完成反馈，不占剧情，不创建角色亲密度记录。
                        if (item.Kind == TaskEventKind.Submitted || item.Kind == TaskEventKind.TimedOut)
                            Simulation.TryCloseFeedback(task.Id);
                    }
                    else
                    {
                        if (item.Kind == TaskEventKind.Arrived) ObserveStory(() => Story.Arrived(task));
                        if (item.Kind == TaskEventKind.TimedOut) ObserveStory(() => Story.TimedOut(task));
                        if (item.Kind == TaskEventKind.Submitted)
                        {
                            IsMemoryUpgradeOpen = false;
                            ObserveStory(() => Story.Submitted(task));
                            notifications.Add(() => canvasView?.CancelDrag());
                        }
                    }
                }
                logs.Insert(0, $"{item.GameTime:0.0}s  {item.RequestId}  {EventText(item.Kind)}"
                    + (item.Kind == TaskEventKind.Submitted || item.Kind == TaskEventKind.TimedOut ? $"  token={item.BillableTokens:0.##}" : ""));
                if (logs.Count > 7) logs.RemoveAt(logs.Count - 1);
                if (item.Kind == TaskEventKind.Submitted || item.Kind == TaskEventKind.TimedOut) notifications.Add(() => canvasView?.ShowSettlement(item, income));
                QueueTaskObservers(item, notifications);
            }
            foreach (var notify in notifications)
                try { notify(); } catch (Exception error) { Debug.LogWarning("Task observer failed after settlement: " + error.Message); }
        }
        // Event draining must not stall because a story slot could not draw a customer.
        private static void ObserveStory(Action action)
        {
            try { action(); }
            catch (StagePoolExhaustedException error)
            { Debug.LogWarning("Stage pool binding failed after event: " + error.Message); }
        }
        private void QueueTaskObservers(TaskEvent item, List<Action> notifications)
        {
            if (!IsFillerTask(item.RequestId)) notifications.Add(() => ObserveCompletionTask(item));
            if (TaskEventRaised == null) return;
            foreach (Action<TaskEvent> observer in TaskEventRaised.GetInvocationList()) notifications.Add(() => observer(item));
        }
        public void OpenTraining()
        {
            if (!TrainingEnabled || GameEnded || IsModalOpen) return;
            if (!Simulation.Paused) { Message = "请先暂停，再打开训练 / 模型。"; return; }
            canvasView?.CancelDrag(); trainingPanel?.Open(); canvasView?.Refresh();
        }
        private void RefreshTraining() { canvasView?.CancelDrag(); canvasView?.Refresh(); }
        private IReadOnlyList<Cell> MemoryOutline(int level)
        {
            var cells = CapacityForNarrative(Narrative, level).ToArray();
            if (!TrainingEnabled) return cells;
            var model = training.Session.ActiveModel;
            if (model == null) return cells;
            return training.Session.Architectures.First(a => a.Id == model.ArchitectureId).Transform(cells, Simulation.Width, Simulation.Height);
        }
        private static bool IsDebugShortcutEditing()
        {
            if (CallmeCatgirl.Debugging.InlineDebugInput.IsEditing) return true;
            var selected = EventSystem.current == null ? null : EventSystem.current.currentSelectedGameObject;
            if (selected == null) return false;
            var input = selected.GetComponentInParent<InputField>();
            if (input != null && input.isActiveAndEnabled && input.isFocused) return true;
            var tmpInput = selected.GetComponentInParent<TMPro.TMP_InputField>();
            return tmpInput != null && tmpInput.isActiveAndEnabled && tmpInput.isFocused;
        }
        private void SkipCurrentNarrative()
        {
            if (endingSceneRequested) return;
            bool hadFrame = Narrative != null && Narrative.CurrentFrame != null;
            for (int step = 0; step < 64 && Narrative != null && Narrative.CurrentFrame != null; step++)
            {
                var frame = Narrative.CurrentFrame;
                if (frame.CanClose)
                {
                    if (!Narrative.CloseConversation()) break;
                    continue;
                }
                var choice = frame.Choices.FirstOrDefault();
                if (choice == null || !Narrative.Choose(choice.Id)) break;
            }
            if (canvasView != null && canvasView.IsConversationVisible) canvasView.DismissConversation();
            bool stillOpen = Narrative != null && Narrative.CurrentFrame != null;
            Message = !hadFrame ? "当前没有可跳过的剧情。"
                : stillOpen ? "已跳过部分剧情，仍有对话需要处理。"
                : "已跳过当前剧情对话。";
            canvasView?.Refresh();
        }
        private void ToggleDebugIgnoreCosts()
        {
            DebugIgnoreCosts = !DebugIgnoreCosts;
            if (Economy != null) Economy.DebugIgnoreSpending = DebugIgnoreCosts;
            Message = DebugIgnoreCosts ? "调试：已无视金币消耗。" : "调试：已恢复金币消耗。";
            canvasView?.Refresh();
        }
        private void FinishAllTasks()
        {
            int finished = 0;
            foreach (var task in Simulation.Tasks.Values.ToList())
                if ((task.State == RequestState.Waiting || task.State == RequestState.Withdrawn || task.State == RequestState.Running)
                    && Simulation.TryDebugFinishCalculation(task.Id)) finished++;
            ReadEvents();
            Message = finished > 0 ? $"已瞬间完成 {finished} 个任务，可提交。" : "没有可瞬间完成的任务。";
            canvasView?.Refresh();
        }
        public IReadOnlyList<Cell> ModelOutlineFor(string modelId)
        {
            var cells=CapacityForNarrative(Narrative,MemoryLevel).ToArray();
            if(!TrainingEnabled)return cells;
            var model=training.Session.Models.FirstOrDefault(m=>m.Id==modelId);
            if(model==null)return cells;
            return training.Session.Architectures.First(a=>a.Id==model.ArchitectureId).Transform(cells,Simulation.Width,Simulation.Height);
        }
        private void OnDisable()
        {
            // 禁用时同步取消挂起的安装意图；Update 在禁用期间不会运行，不能依赖看门狗。
            if (pendingMemoryUpgrade != null) CancelPendingMemoryInstall();
        }
        private void OnDestroy()
        {
            pendingMemoryUpgrade = null;
            installAttempt++;
            UnbindInstallCompletion();
            if (training != null) training.Changed -= RefreshTraining;
        }
        public static string AbilityText(AbilityKind kind) => kind == AbilityKind.Writing ? "写作" : kind == AbilityKind.Code ? "代码" : kind == AbilityKind.Reasoning ? "推理" : "检索";
        public static string QualityText(Quality quality) => quality == Quality.Excellent ? "优秀" : quality == Quality.Complete ? "完成" : "未达标";
        public static string StateText(RequestState state) => new[] { "等待摆放", "运行中", "已撤回", "算完待确认", "已提交待回复", "超时失败", "反馈结束" }[(int)state];
        public static string FailureText(PlacementFailure failure) => new[] { "无", "任务不存在", "当前状态不可摆放", "越界", "未开放格", "与其他任务重叠", "跨越隔板，任务必须完整放在同一背包内" }[(int)failure];
        private static string EventText(TaskEventKind kind) => new[] { "请求到达", "合法摆放", "撤回", "算完判质", "提交结算", "超时结算", "反馈结束" }[(int)kind];
    }
}
