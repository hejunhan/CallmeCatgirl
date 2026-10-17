using System;
using System.Collections.Generic;
using System.Linq;
using CallmeCatgirl.Gameplay.TaskGrid;

namespace CallmeCatgirl.Gameplay
{
    // 剧情进度与历史仅属于本次经营；配置资源只读，不写回资产。
    public sealed partial class ConversationStory
    {
        private sealed class UserState
        {
            public StoryConfiguration.UserStory Profile;
            public string NodeId;
            public double Affinity;
            public readonly HashSet<string> CompletedSessions = new HashSet<string>();
            public string PoolEntryId;
            public readonly List<string> History = new List<string>();
        }
        private readonly double initialAffinity, successAffinity, failureAffinity;
        private readonly HashSet<string> settled = new HashSet<string>();
        private readonly StoryConfiguration configuration;
        private readonly TaskGenerationConfiguration generation;
        private readonly StoryConfiguration ordinaryPool;
        private readonly System.Random shapeRandom;
        private readonly Dictionary<string, UserState> users = new Dictionary<string, UserState>();
        private readonly HashSet<string> submitted = new HashSet<string>();
        private readonly HashSet<string> arrived = new HashSet<string>();
        private bool choosing;
        // Attach the controller's existing wallet after ResetSimulation creates it.
        public EconomyManager Economy { get; set; }
        private readonly Queue<TaskInstance> pending = new Queue<TaskInstance>();
        public bool IsOpen { get; private set; }
        public bool IsReply { get; private set; }
        public string UserId { get; private set; }
        public string RequestId { get; private set; }
        public Quality Result { get; private set; }
        public int Revision { get; private set; }
        public string CurrentCorpusNodeId => User(UserId).NodeId ?? "__default__";
        public int SessionNumber(string id)
        {
            var state = User(id); var session = state.Profile?.FindSession(state.NodeId);
            return session == null ? 0 : Array.IndexOf(state.Profile.Sessions, session) + 1;
        }
        public string CorpusChoiceId(StoryConfiguration.ReplyChoice choice)
        {
            var source = User(UserId).Profile?.FindNode(User(UserId).NodeId)?.Choices;
            int index = source == null ? -1 : Array.IndexOf(source, choice);
            if (index >= 0) return "choice-" + index;
            var defaults = configuration?.DefaultChoices;
            index = defaults == null ? -1 : Array.IndexOf(defaults, choice);
            return index >= 0 ? "default-" + index : "__automatic__";
        }
        public IReadOnlyList<StoryConfiguration.ReplyChoice> Choices { get; private set; }
            = Array.Empty<StoryConfiguration.ReplyChoice>();
        public ConversationStory(StoryConfiguration config) : this(config, null, null, null) { }
        public ConversationStory(StoryConfiguration config, TaskGenerationConfiguration taskGeneration, System.Random random)
            : this(config, taskGeneration, random, null) { }
        public ConversationStory(StoryConfiguration config, TaskGenerationConfiguration taskGeneration, System.Random random,
            double initial, double success, double failure) : this(config, taskGeneration, random, null, initial, success, failure) { }
        public ConversationStory(StoryConfiguration config, TaskGenerationConfiguration taskGeneration, System.Random random,
            StoryConfiguration ordinaryDialoguePool) : this(config, taskGeneration, random, ordinaryDialoguePool, 0, 1, -0.5) { }
        public ConversationStory(StoryConfiguration config, TaskGenerationConfiguration taskGeneration, System.Random random,
            StoryConfiguration ordinaryDialoguePool, double initial, double success, double failure)
        {
            if (new[] { initial, success, failure }.Any(n => double.IsNaN(n) || double.IsInfinity(n)))
                throw new ArgumentException("亲密度配置必须是有限数值。");
            configuration = config; generation = taskGeneration; shapeRandom = random; ordinaryPool = ordinaryDialoguePool;
            initialAffinity = initial; successAffinity = success; failureAffinity = failure;
            if (ordinaryPool != null && (ordinaryPool.Users == null || ordinaryPool.Users.Length == 0
                || ordinaryPool.Users.Any(p => p == null || p.Kind != StoryConfiguration.ConversationKind.Ordinary)))
                throw new ArgumentException("普通对话池必须包含明确标记为 Ordinary 的内容。");
        }
        private UserState User(string id)
        {
            if (users.TryGetValue(id, out var state)) return state;
            return BindUser(id, null, false);
        }
        public string Name(string id) => User(id).Profile?.DisplayName is string name && !string.IsNullOrWhiteSpace(name) ? name : "用户 " + id;
        public string History(string id) => string.Join("\n\n", User(id).History);
        public string Latest(string id) => User(id).History.LastOrDefault() ?? "还没有对话记录。";
        public double Affinity(string id) => User(id).Affinity;
        public bool AwaitingReply(string request) => (IsReply && RequestId == request) || pending.Any(t => t.Id == request);
        // 以请求ID去重；多轮选项、查看历史、关闭反馈均不重复结算。
        private void Settle(TaskInstance task, bool success)
        {
            if (!settled.Add(task.Id)) return;
            var state = User(task.ConversationId); double change = success ? successAffinity : failureAffinity;
            state.Affinity += change;
            state.History.Add($"[亲密度 {change:+0.##;-0.##;0} → {state.Affinity:0.##} · {(success ? "任务达标" : "任务失败，下次重试当前会话")}]");
            Revision++;
        }
        public void TimedOut(TaskInstance task)
        {
            if (task.State == RequestState.TimedOut) Settle(task, false);
        }
        public void Arrived(TaskInstance task)
        {
            if (task == null || !arrived.Add(task.Id)) return;
            var state = User(task.ConversationId); var node = state.Profile?.FindNode(state.NodeId);
            if (node != null)
            {
                state.History.Add("[" + Progress(task.ConversationId) + "]");
                state.History.Add(Name(task.ConversationId) + "：" + node.UserMessage);
            }
            else state.History.Add($"{Name(task.ConversationId)}：{task.Definition.Title}");
            Revision++;
        }
        public void Submitted(TaskInstance task)
        {
            if (task == null || task.State != RequestState.Submitted || task.Result == null) return;
            if (submitted.Contains(task.Id)) return;
            Settle(task, task.Result.Overall != Quality.BelowStandard);
            ObserveRequestSettlement(task);
            submitted.Add(task.Id);
            User(task.ConversationId).History.Add($"[提交 {task.Id} · {CoreGameplayController.QualityText(task.Result.Overall)}]");
            if (IsReply) { pending.Enqueue(task); return; }
            OpenReply(task);
        }
        private void OpenReply(TaskInstance task)
        {
            UserId = task.ConversationId; RequestId = task.Id; Result = task.Result.Overall;
            IsOpen = IsReply = true;
            var state = User(UserId);
            var node = state.Profile?.FindNode(state.NodeId);
            LoadChoices(state, node);
        }
        private void LoadChoices(UserState state, StoryConfiguration.StoryNode node)
        {
            var options = node?.Choices;
            if (options == null || options.Length == 0) options = configuration?.DefaultChoices;
            Choices = (options ?? Array.Empty<StoryConfiguration.ReplyChoice>())
                .Where(c => c != null && c.IsAvailable(Result) && CanApply(c)).ToArray();
            // 配置漏填或全被质量条件挡住，也允许完成这次回复。
            if (Choices.Count == 0) Choices = new[] { new StoryConfiguration.ReplyChoice
            { NextNodeId = state.Profile?.SequentialNext(state.NodeId) } };
            Revision++;
        }
        public bool Choose(int index)
        {
            if (choosing || !IsReply || index < 0 || index >= Choices.Count) return false;
            var state = User(UserId); var choice = Choices[index];
            // Recheck mutable wallet state; an external purchase must not bypass a condition.
            if (!CanApply(choice)) return false;
            choosing = true;
            try
            {
                if (choice.FundsReward > 0) Economy.AddFunds(choice.FundsReward);
                if (!string.IsNullOrWhiteSpace(choice.PlayerReply)) state.History.Add("我：" + choice.PlayerReply);
                if (!string.IsNullOrWhiteSpace(choice.UserReply)) state.History.Add(Name(UserId) + "：" + choice.UserReply);
                var session = state.Profile?.FindSession(state.NodeId);
                state.NodeId = choice.NextNodeId;
                if (session != null && state.Profile.FindSession(state.NodeId) == session)
                {
                    var next = state.Profile.FindNode(state.NodeId);
                    state.History.Add(Name(UserId) + "：" + next.UserMessage);
                    LoadChoices(state, next); return true;
                }
                if (session != null)
                {
                    if (Result == Quality.BelowStandard) state.NodeId = session.Rounds[0].Id;
                    else
                    {
                        state.CompletedSessions.Add(session.Id);
                        var target = state.Profile.FindSession(state.NodeId);
                        // 分支可以跳转，但已达标的会话不会作为新任务再次派发。
                        if (target != null && state.CompletedSessions.Contains(target.Id))
                            state.NodeId = state.Profile.Sessions.FirstOrDefault(s => !state.CompletedSessions.Contains(s.Id))?.Rounds[0].Id;
                    }
                }
                if (Finished(UserId)) state.History.Add("[" + Progress(UserId) + "]");
                Choices = Array.Empty<StoryConfiguration.ReplyChoice>(); IsOpen = IsReply = false; Revision++;
                if (pending.Count > 0) OpenReply(pending.Dequeue());
                return true;
            }
            finally { choosing = false; }
        }
        // UI callbacks should capture Revision when displaying the options. A stale click
        // cannot select a newly opened round (including another queued contact).
        public bool ChooseAtRevision(int index, int expectedRevision)
            => expectedRevision == Revision && Choose(index);
        private bool CanApply(StoryConfiguration.ReplyChoice choice)
            => choice.MinimumFunds >= 0 && choice.FundsReward >= 0
                && (Economy != null
                    ? (choice.FundsReward == 0 || !Economy.IsBankrupt) && Economy.CurrentFunds >= choice.MinimumFunds
                        && choice.FundsReward <= int.MaxValue - Economy.CurrentFunds
                    : choice.MinimumFunds == 0 && choice.FundsReward == 0);
        public bool Finished(string id)
        {
            var state = User(id);
            // A finite text sequence may finish naturally; ordinary requests still draw once at every settlement.
            return (state.Profile?.HasSessions == true && string.IsNullOrEmpty(state.NodeId))
                || (!IsScriptedConversation(id) && OrdinaryLifecycle.IsEnded(id));
        }
        public string Progress(string id) => IsScriptedConversation(id) ? User(id).Profile.Progress(User(id).NodeId)
            : Finished(id) ? "对话已结束" : User(id).Profile?.Progress(User(id).NodeId) ?? string.Empty;
        public TaskDefinition BuildNextTask(string id, IReadOnlyDictionary<string, TaskDefinition> templates, TaskDefinition fallback)
        {
            if (Finished(id)) return null;
            var state = User(id); var session = state.Profile?.FindSession(state.NodeId);
            string templateId = session?.RequestId;
            var basis = !string.IsNullOrEmpty(templateId) && templates.TryGetValue(templateId, out var template) ? template : fallback;
            if (session == null && generation == null) return basis;
            var difficulty = session?.Task;
            if (state.PoolEntryId != null && session != null)
            {
                // Dialogue replacement preserves the matching ABC ordinary request's existing difficulty curve.
                int index = Array.IndexOf(state.Profile.Sessions, session);
                var ordinaryTemplate = configuration?.Users?.FirstOrDefault(p => p != null && p.UserId == templateId
                    && p.Kind == StoryConfiguration.ConversationKind.Ordinary);
                var sourceTask = ordinaryTemplate?.Sessions?.ElementAtOrDefault(index)?.Task;
                if (sourceTask != null) difficulty = sourceTask;
            }
            var requirements = difficulty?.Abilities != null && difficulty.Abilities.Length > 0
                ? difficulty.Abilities.Select(a => a.Build()) : basis.Requirements;
            return new TaskDefinition(basis.Id, string.IsNullOrWhiteSpace(session?.Title) ? basis.Title : session.Title,
                difficulty?.Shape != null && difficulty.Shape.Length > 0 ? ShapePattern.Parse(difficulty.Shape) : generation != null ? generation.PickShape(shapeRandom) : basis.Shape,
                difficulty != null && difficulty.Calculation > 0 ? difficulty.Calculation : basis.Calculation,
                difficulty != null && difficulty.Speed > 0 ? difficulty.Speed : basis.Speed,
                difficulty != null && difficulty.Patience > 0 ? difficulty.Patience : basis.Patience,
                difficulty != null && difficulty.Tokens >= 0 ? difficulty.Tokens : basis.Tokens, requirements);
        }
        // A preview neither submits nor mutates the story, wallet, probability or corpus.
        public IReadOnlyList<StoryConfiguration.ReplyChoice> PreviewReplyChoices(TaskInstance task)
        {
            if (task == null || task.State != RequestState.Ready || task.Result == null)
                return Array.Empty<StoryConfiguration.ReplyChoice>();
            var state = User(task.ConversationId);
            var options = state.Profile?.FindNode(state.NodeId)?.Choices;
            if (options == null || options.Length == 0) options = configuration?.DefaultChoices;
            var visible = (options ?? Array.Empty<StoryConfiguration.ReplyChoice>())
                .Where(c => c != null && c.IsAvailable(task.Result.Overall) && CanApply(c)).ToArray();
            return visible.Length > 0 ? visible : new[] { new StoryConfiguration.ReplyChoice
                { NextNodeId = state.Profile?.SequentialNext(state.NodeId) } };
        }
        public void OpenConversation(TaskInstance task, TaskInstance currentReply)
        {
            if (task == null) return;
            if (IsReply && RequestId == task.Id) { IsOpen = true; return; }
            if (IsReply && currentReply != null && !pending.Any(t => t.Id == currentReply.Id)) pending.Enqueue(currentReply);
            // Switching windows retains each outstanding submitted reply in the existing queue.
            int count = pending.Count;
            for (int i = 0; i < count; i++) { var item = pending.Dequeue(); if (item.Id != task.Id) pending.Enqueue(item); }
            IsReply = false;
            if (task.State == RequestState.Submitted) OpenReply(task);
            else OpenHistory(task.ConversationId);
        }
        public void OpenHistory(string id)
        {
            if (IsReply || string.IsNullOrEmpty(id)) return;
            UserId = id; IsOpen = true; IsReply = false; Revision++;
        }
        public void CloseHistory() { if (!IsReply) { IsOpen = false; Revision++; } }
    }
}
