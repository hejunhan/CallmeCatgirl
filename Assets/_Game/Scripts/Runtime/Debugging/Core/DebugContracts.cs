using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CallmeCatgirl.Gameplay.TaskGrid;

namespace CallmeCatgirl.Debugging
{
    public static class DebugBuild
    {
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CATGIRL_DISABLE_DEBUG
        public const bool Enabled = true;
#else
        public const bool Enabled = false;
#endif
    }

    public enum DebugAction { SetCorpusScore, AddCorpusScore, SetQualityOverride, FinishCalculation, FinishTraining, AdvanceStage }
    [Flags]
    public enum DebugCapabilities { None = 0, Corpus = 1, Quality = 2, Calculation = 4, Training = 8, Stage = 16, All = 31 }
    public enum DebugStatus { Applied, Disabled, Unsupported, InvalidInput, UnknownTarget, InvalidState, Rejected, Busy }

    /// <summary>A command always names an object. A null quality clears the runtime override.</summary>
    public sealed class DebugCommand
    {
        public DebugAction Action { get; }
        public string TargetId { get; }
        public AbilityKind Ability { get; }
        public double Amount { get; }
        public Quality? Quality { get; }
        private DebugCommand(DebugAction action, string target, AbilityKind ability = AbilityKind.Writing,
            double amount = 0, Quality? quality = null)
        { Action = action; TargetId = target; Ability = ability; Amount = amount; Quality = quality; }
        public static DebugCommand SetCorpus(string id, AbilityKind ability, double value) => new DebugCommand(DebugAction.SetCorpusScore, id, ability, value);
        public static DebugCommand AddCorpus(string id, AbilityKind ability, double delta) => new DebugCommand(DebugAction.AddCorpusScore, id, ability, delta);
        public static DebugCommand OverrideQuality(string id, Quality? quality) => new DebugCommand(DebugAction.SetQualityOverride, id, quality: quality);
        public static DebugCommand FinishCalculation(string id) => new DebugCommand(DebugAction.FinishCalculation, id);
        public static DebugCommand FinishTraining(string id) => new DebugCommand(DebugAction.FinishTraining, id);
        // The displayed current stage is part of the command: stale clicks cannot skip another stage.
        public static DebugCommand AdvanceStage(string stageId, int expectedStage) => new DebugCommand(DebugAction.AdvanceStage, stageId, amount: expectedStage);
    }

    public sealed class DebugCorpusSnapshot
    {
        public string Id { get; }
        public bool Consumed { get; }
        public IReadOnlyDictionary<AbilityKind, double> Scores { get; }
        public DebugCorpusSnapshot(string id, bool consumed, IDictionary<AbilityKind, double> scores)
        { Id = id; Consumed = consumed; Scores = new ReadOnlyDictionary<AbilityKind, double>(new Dictionary<AbilityKind, double>(scores)); }
        public double Score(AbilityKind kind) => Scores.TryGetValue(kind, out var value) ? value : 0;
    }

    public sealed class DebugTaskSnapshot
    {
        public string Id { get; }
        public bool IsTraining { get; }
        public bool TrainingCompleted { get; }
        public RequestState State { get; }
        public double Progress { get; }
        public double Calculation { get; }
        public Quality? Result { get; }
        public Quality? Override { get; }
        public DebugTaskSnapshot(string id, bool isTraining, bool trainingCompleted, RequestState state,
            double progress, double calculation, Quality? result, Quality? qualityOverride)
        { Id = id; IsTraining = isTraining; TrainingCompleted = trainingCompleted; State = state; Progress = progress;
            Calculation = calculation; Result = result; Override = qualityOverride; }
        public bool CanCalculate => State == RequestState.Waiting || State == RequestState.Withdrawn || State == RequestState.Running;
        public bool CanOverride => !IsTraining && (CanCalculate || State == RequestState.Ready);
        public bool CanFinishTraining => IsTraining && !TrainingCompleted && (CanCalculate || State == RequestState.Ready);
    }

    /// <summary>Immutable observation. Capture never advances time or consumes simulation events.</summary>
    public sealed class DebugSnapshot
    {
        public string SessionId { get; }
        public bool Enabled { get; }
        public DebugCapabilities Capabilities { get; }
        public bool Paused { get; }
        public double GameTime { get; }
        public string StageId { get; }
        public int Stage { get; }
        public int MaxStage { get; }
        public IReadOnlyDictionary<string, DebugCorpusSnapshot> Corpus { get; }
        public IReadOnlyDictionary<string, DebugTaskSnapshot> Tasks { get; }
        public DebugSnapshot(string sessionId, bool enabled, DebugCapabilities capabilities, bool paused, double gameTime,
            IEnumerable<DebugCorpusSnapshot> corpus, IEnumerable<DebugTaskSnapshot> tasks,
            string stageId = "progression", int stage = 1, int maxStage = 1)
        {
            SessionId = sessionId; Enabled = enabled; Capabilities = capabilities; Paused = paused; GameTime = gameTime;
            StageId = stageId; Stage = stage; MaxStage = maxStage;
            Corpus = new ReadOnlyDictionary<string, DebugCorpusSnapshot>(corpus.ToDictionary(x => x.Id, StringComparer.Ordinal));
            Tasks = new ReadOnlyDictionary<string, DebugTaskSnapshot>(tasks.ToDictionary(x => x.Id, StringComparer.Ordinal));
        }
    }

    public sealed class DebugMutation
    {
        public bool Applied { get; }
        public string Detail { get; }
        private DebugMutation(bool applied, string detail) { Applied = applied; Detail = detail; }
        public static DebugMutation Success(string detail = "") => new DebugMutation(true, detail);
        // Host rejects must be atomic. Do not report rejection after partially mutating state.
        public static DebugMutation Reject(string reason) => new DebugMutation(false, reason);
    }

    public interface IDebugGameplayHost
    {
        DebugSnapshot Capture();
        // Called synchronously on Unity's main thread. The gameplay owner routes its own event batch.
        DebugMutation Apply(DebugCommand command);
    }

    /// <summary>Useful when the owner binds local methods, without inheritance or reflection.</summary>
    public sealed class DelegateDebugGameplayHost : IDebugGameplayHost
    {
        private readonly Func<DebugSnapshot> capture;
        private readonly Func<DebugCommand, DebugMutation> apply;
        public DelegateDebugGameplayHost(Func<DebugSnapshot> capture, Func<DebugCommand, DebugMutation> apply)
        { this.capture = capture ?? throw new ArgumentNullException(nameof(capture)); this.apply = apply ?? throw new ArgumentNullException(nameof(apply)); }
        public DebugSnapshot Capture() => capture();
        public DebugMutation Apply(DebugCommand command) => apply(command);
    }

    public sealed class DebugReceipt
    {
        public long Sequence { get; }
        public DebugStatus Status { get; }
        public DebugCommand Command { get; }
        public string SessionId { get; }
        public string Before { get; }
        public string After { get; }
        public string Detail { get; }
        public string Display => $"[调试 #{Sequence}] {Command?.TargetId ?? "无对象"} · {Command?.Action.ToString() ?? "无命令"}: {Before} → {After} · {Status} {Detail}";
        internal DebugReceipt(long sequence, DebugStatus status, DebugCommand command, string sessionId,
            string before, string after, string detail)
        { Sequence = sequence; Status = status; Command = command; SessionId = sessionId; Before = before; After = after; Detail = detail; }
    }
}
