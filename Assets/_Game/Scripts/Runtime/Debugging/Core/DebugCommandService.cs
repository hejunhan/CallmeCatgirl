using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CallmeCatgirl.Gameplay.TaskGrid;

namespace CallmeCatgirl.Debugging
{
    /// <summary>No clock, wallet, save, simulation setters, or event drain. All mutation belongs to the bound host.</summary>
    public sealed class DebugCommandService
    {
        private readonly IDebugGameplayHost host;
        private readonly Queue<DebugReceipt> history = new Queue<DebugReceipt>();
        private bool executing;
        private long sequence;
        public event Action<DebugReceipt> Executed;
        public IReadOnlyList<DebugReceipt> History => history.ToArray();
        public DebugReceipt LastReceipt { get; private set; }
        public DebugCommandService(IDebugGameplayHost host) { this.host = host ?? throw new ArgumentNullException(nameof(host)); }
        public DebugSnapshot Capture() => host.Capture();

        public DebugReceipt Execute(DebugCommand command)
        {
            if (executing) return new DebugReceipt(0, DebugStatus.Busy, command, null, "未执行", "未执行", "已有调试命令执行中");
            executing = true;
            DebugReceipt receipt;
            try
            {
                var before = Capture();
                var status = Validate(before, command, out var reason);
                var oldValue = Describe(before, command);
                if (status != DebugStatus.Applied)
                    receipt = new DebugReceipt(++sequence, status, command, before.SessionId, oldValue, oldValue, reason);
                else
                {
                    var mutation = host.Apply(command) ?? throw new InvalidOperationException("Debug host returned no result.");
                    var after = Capture();
                    receipt = new DebugReceipt(++sequence, mutation.Applied ? DebugStatus.Applied : DebugStatus.Rejected,
                        command, before.SessionId, oldValue, Describe(after, command), mutation.Detail);
                }
                LastReceipt = receipt;
                history.Enqueue(receipt);
                while (history.Count > 32) history.Dequeue();
            }
            finally { executing = false; }
            Executed?.Invoke(receipt);
            return receipt;
        }

        public static DebugStatus Validate(DebugSnapshot snapshot, DebugCommand command, out string reason)
        {
            reason = "";
            if (!DebugBuild.Enabled || !snapshot.Enabled) { reason = "此环境未启用开发调试"; return DebugStatus.Disabled; }
            if (command == null || string.IsNullOrWhiteSpace(command.TargetId)) { reason = "缺少稳定对象 ID"; return DebugStatus.InvalidInput; }
            var capability = command.Action == DebugAction.SetCorpusScore || command.Action == DebugAction.AddCorpusScore ? DebugCapabilities.Corpus
                : command.Action == DebugAction.SetQualityOverride ? DebugCapabilities.Quality
                : command.Action == DebugAction.FinishCalculation ? DebugCapabilities.Calculation
                : command.Action == DebugAction.FinishTraining ? DebugCapabilities.Training : DebugCapabilities.Stage;
            if ((snapshot.Capabilities & capability) == 0) { reason = "主控制器尚未接入此能力"; return DebugStatus.Unsupported; }
            if (capability == DebugCapabilities.Corpus)
            {
                if (!snapshot.Corpus.TryGetValue(command.TargetId, out var corpus)) return Unknown(out reason);
                if (corpus.Consumed) { reason = "语料已用于训练，冻结输入不可修改"; return DebugStatus.InvalidState; }
                if (!Enum.IsDefined(typeof(AbilityKind), command.Ability) || !Finite(command.Amount)) return Invalid(out reason);
                double next = command.Action == DebugAction.SetCorpusScore ? command.Amount : corpus.Score(command.Ability) + command.Amount;
                if (!Finite(next) || next < 0 || !corpus.Scores.Any(p => p.Key != command.Ability && p.Value > 0) && next == 0)
                { reason = "分数必须为非负有限数，且至少保留一个正维度"; return DebugStatus.InvalidInput; }
                return DebugStatus.Applied;
            }
            if (capability == DebugCapabilities.Stage)
            {
                if (command.TargetId != snapshot.StageId) return Unknown(out reason);
                if (command.Amount != snapshot.Stage || snapshot.Stage >= snapshot.MaxStage)
                { reason = "阶段已变化或已达上限，请查看当前阶段"; return DebugStatus.InvalidState; }
                return DebugStatus.Applied;
            }
            if (!snapshot.Tasks.TryGetValue(command.TargetId, out var task)) return Unknown(out reason);
            bool allowed = capability == DebugCapabilities.Quality ? task.CanOverride
                : capability == DebugCapabilities.Training ? task.CanFinishTraining : !task.IsTraining && task.CanCalculate;
            if (!allowed) { reason = "此对象当前状态不可执行该操作"; return DebugStatus.InvalidState; }
            if (command.Quality.HasValue && !Enum.IsDefined(typeof(Quality), command.Quality.Value)) return Invalid(out reason);
            return DebugStatus.Applied;
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static DebugStatus Unknown(out string reason) { reason = "对象不存在或已重置"; return DebugStatus.UnknownTarget; }
        private static DebugStatus Invalid(out string reason) { reason = "数值或枚举无效"; return DebugStatus.InvalidInput; }
        private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
        public static string Describe(DebugSnapshot snapshot, DebugCommand command)
        {
            if (command == null) return "无命令";
            if (command.Action == DebugAction.AdvanceStage)
                return $"阶段 {snapshot.Stage}/{snapshot.MaxStage} · 时钟 {Number(snapshot.GameTime)}s · {(snapshot.Paused ? "暂停" : "运行")}";
            if (command.Action == DebugAction.SetCorpusScore || command.Action == DebugAction.AddCorpusScore)
                return snapshot.Corpus.TryGetValue(command.TargetId, out var corpus) ? $"{command.Ability}={Number(corpus.Score(command.Ability))}{(corpus.Consumed ? " 已消耗" : "")}" : "对象不存在";
            if (!snapshot.Tasks.TryGetValue(command.TargetId, out var task)) return "对象不存在";
            return $"{task.State} {Number(task.Progress)}/{Number(task.Calculation)} · 质量 {task.Result?.ToString() ?? "待判定"} · 覆盖 {task.Override?.ToString() ?? "无"}"
                + (task.IsTraining ? $" · 训练{(task.TrainingCompleted ? "已完成" : "未完成")}" : "");
        }
    }
}
