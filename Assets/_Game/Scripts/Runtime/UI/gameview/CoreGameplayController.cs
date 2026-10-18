using System;
using System.Collections.Generic;
using System.Linq;
using CallmeCatgirl.Gameplay.TaskGrid;
using CallmeCatgirl.UI;
using CallmeCatgirl.Gameplay.Training;
using UnityEngine;
using UnityEngine.InputSystem;

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
        public string ModelName => TrainingEnabled ? training.Session.Models.First(m => m.Id == training.Session.ActiveModelId).Name
            : configuration.MemoryUpgrades.GetModelName(MemoryLevel, Simulation.Model.Id);
        public EconomyManager Economy { get; private set; }
        public int Funds => Economy.CurrentFunds;
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
        public int AvailableAbilityPoints { get; private set; }
        public bool CanAllocateAbility => !TrainingEnabled && !GameEnded && !IsMemoryInstallPending && !IsMemoryInstallOpen && IsMemoryUpgradeOpen && AvailableAbilityPoints > 0;
        public MemoryUpgradeConfiguration.UpgradeStep NextMemoryUpgrade
            => MemoryLevel == 0 ? firstMemoryInstallation : configuration.MemoryUpgrades.TryGetNextLevel(MemoryLevel, out var step) ? step : null;
        public bool CanUpgradeMemory => isActiveAndEnabled && !GameEnded && Economy != null && !Economy.IsBankrupt
            && !IsMemoryInstallPending && !IsMemoryInstallOpen && NextMemoryUpgrade != null
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
            if (!GameEnded && !IsModalOpen && !CallmeCatgirl.Debugging.InlineDebugInput.IsEditing && Keyboard.current != null)
            {
                if (Keyboard.current.rKey.wasPressedThisFrame) RotateSelected();
                if (Keyboard.current.spaceKey.wasPressedThisFrame) TogglePause();
            }
            ReadEvents();
            ProcessCompletion();
            if (canvasView != null) canvasView.Refresh();
        }

        public void ResetSimulation()
        {
            if (configuration == null) { Debug.LogError("CoreGameplayController requires a GameplayConfiguration.", this); enabled = false; return; }
            try
            {
                CancelPendingMemoryInstall();
                BeginCompletionRun();
                IsMemoryUpgradeOpen = false;
                configuration.MemoryUpgrades.ValidateTaskMultipliers();
                configuration.TaskGeneration?.Validate(configuration.MemoryUpgrades.BoardWidth, configuration.MemoryUpgrades.BoardHeight);
                ManualCommandClock=false; runId=Guid.NewGuid().ToString("D"); saveService=null; saveContentRevision=null;
                int seed=configuration.TaskGeneration == null ? -1 : configuration.TaskGeneration.RandomSeed;
                requestRandom = new CallmeCatgirl.Persistence.SaveableRandom(seed < 0 ? Guid.NewGuid().GetHashCode() : seed);
                Story = new ConversationStory(configuration.Story, configuration.TaskGeneration, requestRandom, configuration.OrdinaryDialoguePool,
                    configuration.InitialAffinity, configuration.SuccessAffinity, configuration.FailureAffinity);
                var configuredSimulation = configuration.BuildSimulation();
                Simulation = new TaskSimulation(configuredSimulation.Width, configuredSimulation.Height, Array.Empty<Cell>(),
                    new ModelDefinition(configuredSimulation.Model.Id, configuredSimulation.Model.Interval, new Dictionary<AbilityKind,double>()), configuration.MemoryUpgrades.GetCompartments());
                Simulation.Paused = true; definitions.Clear(); taskBases.Clear(); operatingTasks.Clear(); logs.Clear(); serial = 0;
                MemoryLevel = 0; CompletedTasks = 0; AvailableAbilityPoints = 0;
                if (configuration.DaysPerGameMonth < 1 || configuration.SecondsPerGameDay <= 0
                    || configuration.SecondsPerGameMonth <= 0 || float.IsNaN(configuration.SecondsPerGameMonth)
                    || float.IsInfinity(configuration.SecondsPerGameMonth)) throw new InvalidOperationException("每天游戏秒数必须为正数、每月天数至少为1，月份时长必须有限。");
                if (Economy != null) Economy.OnEconomyLog -= AddEconomyLog;
                Economy = configuration.BuildEconomyManager(); Economy.OnEconomyLog += AddEconomyLog;
                Economy.TrySpendFunds(Economy.CurrentFunds);
                Story.Economy = Economy;
                CurrentMonth = 1; monthTimer = 0; GameEnded = false; EndingText = string.Empty;
                foreach (var source in configuration.Requests)
                {
                    var definition = source.Build(); definitions.Add(definition.Id, definition);
                }
                requestTemplates = definitions.Values.ToArray();
                configuration.Story?.Validate(definitions.Keys);
                configuration.OrdinaryDialoguePool?.Validate(definitions.Keys);
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
        // 有剧情时按角色派发；每个角色至多一个请求，候选不足不复制已完成会话凑数。
        private TaskInstance CreateScheduledRequest()
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
            if (!operating && configuration.OpeningBalance != null) basis = configuration.OpeningBalance.Apply(user, Story.SessionNumber(user), basis);
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
            int count = 0;
            double expected = generation.RequestsPerRefresh * (PolicyTree?.Effects.CustomerArrivalMultiplier ?? 1);
            int arrivals = (int)Math.Min(1000, Math.Floor(expected));
            if (expected - Math.Floor(expected) > 0 && requestRandom.NextDouble() < expected - Math.Floor(expected)) arrivals++;
            for (int i = 0; i < arrivals; i++)
            {
                if (CreateScheduledRequest() == null) break;
                count++;
            }
            ReadEvents();
            Message = (daily ? $"第{CurrentMonth}月第{CurrentDay}天" : $"第{CurrentMonth}月") + $"：新增{count}个请求，已有请求继续保留。";
            AddEconomyLog(Message);
        }
        public void NextRound()
        {
            if (GameEnded || IsModalOpen) return;
            var previous = SelectedTask;
            if (previous != null && TrainingEnabled && training.IsTrainingRequest(previous.Id)) return;
            if (previous == null || IsOperatingTask(previous.Id) || Story.Finished(previous.ConversationId) || Story.AwaitingReply(previous.Id) || !Simulation.TryCloseFeedback(previous.Id)) return;
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
            if (next.AbilityPoints < 0 || AvailableAbilityPoints > int.MaxValue - next.AbilityPoints)
            { Message = "奖励点数配置无效或超过上限，未升级或扣费。"; canvasView?.Refresh(); return; }
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
            else if (step.AbilityPoints < 0 || AvailableAbilityPoints > int.MaxValue - step.AbilityPoints) failure = "奖励点数配置无效或超过上限";
            else if (step.Cost < 0) failure = "升级价格配置无效";
            else if (CompletedTasks < step.RequiredCompletedTasks) failure = $"需要累计达标提交 {step.RequiredCompletedTasks} 个任务";
            else if (Economy.IsBankrupt || Funds < step.Cost) failure = "升级资金不足";
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
            MemoryLevel = step.TargetLevel; AvailableAbilityPoints += step.AbilityPoints;
            Narrative?.ObserveMemoryCount(MemoryLevel);
            Message = $"内存升级至 Lv{MemoryLevel}：新增 {Simulation.OpenCellCount - before} 格，花费 {step.Cost}，获得 {step.AbilityPoints} 点（剩余 {AvailableAbilityPoints} 点）";
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
                    if (CurrentDay != dayBefore) RefreshScheduledRequests(true);
                    continue;
                }
                monthTimer = 0;
                if (Economy.SettleMonth(configuration.MonthlyMaintenanceFee)) { CurrentMonth++; RefreshScheduledRequests(false); RefreshScheduledRequests(true); }
                else
                {
                    GameEnded = true; Simulation.Paused = true;
                        EndReason = CallmeCatgirl.Persistence.RunEndReasons.Bankruptcy;
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
                        if (item.Kind == TaskEventKind.Submitted) { IsMemoryUpgradeOpen = false; Story.Submitted(task); notifications.Add(() => canvasView?.CancelDrag()); }
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
        private void QueueTaskObservers(TaskEvent item, List<Action> notifications)
        {
            notifications.Add(() => ObserveCompletionTask(item));
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
            var model = training.Session.Models.First(m => m.Id == training.Session.ActiveModelId);
            return training.Session.Architectures.First(a => a.Id == model.ArchitectureId).Transform(cells, Simulation.Width, Simulation.Height);
        }
        public IReadOnlyList<Cell> ModelOutlineFor(string modelId)
        {
            if(!TrainingEnabled)return CapacityForNarrative(Narrative,MemoryLevel).ToArray();
            var model=training.Session.Models.First(m=>m.Id==modelId);
            return training.Session.Architectures.First(a=>a.Id==model.ArchitectureId).Transform(CapacityForNarrative(Narrative,MemoryLevel),Simulation.Width,Simulation.Height);
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
