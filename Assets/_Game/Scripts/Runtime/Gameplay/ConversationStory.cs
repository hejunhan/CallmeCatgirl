using System;
using System.Collections.Generic;
using System.Linq;
using CallmeCatgirl.Gameplay.TaskGrid;

namespace CallmeCatgirl.Gameplay
{
    // 剧情进度与历史仅属于本次经营；配置资源只读，不写回资产。
    public sealed class ConversationStory
    {
        private sealed class UserState
        {
            public StoryConfiguration.UserStory Profile;
            public string NodeId;
            public readonly List<string> History = new List<string>();
        }
        private readonly StoryConfiguration configuration;
        private readonly TaskGenerationConfiguration generation;
        private readonly System.Random shapeRandom;
        private readonly Dictionary<string, UserState> users = new Dictionary<string, UserState>();
        private readonly HashSet<string> submitted = new HashSet<string>();
        private readonly Queue<TaskInstance> pending = new Queue<TaskInstance>();
        public bool IsOpen { get; private set; }
        public bool IsReply { get; private set; }
        public string UserId { get; private set; }
        public string RequestId { get; private set; }
        public Quality Result { get; private set; }
        public int Revision { get; private set; }
        public IReadOnlyList<StoryConfiguration.ReplyChoice> Choices { get; private set; }
            = Array.Empty<StoryConfiguration.ReplyChoice>();
        public ConversationStory(StoryConfiguration config) => configuration = config;
        public ConversationStory(StoryConfiguration config, TaskGenerationConfiguration taskGeneration, System.Random random) : this(config)
        { generation = taskGeneration; shapeRandom = random; }
        private UserState User(string id)
        {
            if (users.TryGetValue(id, out var state)) return state;
            var profile = configuration?.Users?.FirstOrDefault(p => p != null && p.UserId == id);
            state = new UserState { Profile = profile, NodeId = profile?.FirstNodeId };
            if (!string.IsNullOrWhiteSpace(profile?.InitialHistory)) state.History.Add(profile.InitialHistory);
            users.Add(id, state); return state;
        }
        public string Name(string id) => User(id).Profile?.DisplayName is string name && !string.IsNullOrWhiteSpace(name) ? name : "用户 " + id;
        public string History(string id) => string.Join("\n\n", User(id).History);
        public string Latest(string id) => User(id).History.LastOrDefault() ?? "还没有对话记录。";
        public void Arrived(TaskInstance task)
        {
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
            if (!submitted.Add(task.Id)) return;
            if (IsReply) { pending.Enqueue(task); return; }
            OpenReply(task);
        }
        private void OpenReply(TaskInstance task)
        {
            UserId = task.ConversationId; RequestId = task.Id; Result = task.Result.Overall;
            IsOpen = IsReply = true;
            var state = User(UserId);
            state.History.Add($"[提交 {task.Id} · {CoreGameplayController.QualityText(Result)}]");
            var node = state.Profile?.FindNode(state.NodeId);
            LoadChoices(state, node);
        }
        private void LoadChoices(UserState state, StoryConfiguration.StoryNode node)
        {
            var options = node?.Choices;
            if (options == null || options.Length == 0) options = configuration?.DefaultChoices;
            Choices = (options ?? Array.Empty<StoryConfiguration.ReplyChoice>())
                .Where(c => c != null && c.IsAvailable(Result)).ToArray();
            // 配置漏填或全被质量条件挡住，也允许完成这次回复。
            if (Choices.Count == 0) Choices = new[] { new StoryConfiguration.ReplyChoice
            { NextNodeId = state.Profile?.SequentialNext(state.NodeId) } };
            Revision++;
        }
        public bool Choose(int index)
        {
            if (!IsReply || index < 0 || index >= Choices.Count) return false;
            var state = User(UserId); var choice = Choices[index];
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
            if (Finished(UserId)) state.History.Add("[" + Progress(UserId) + "]");
            Choices = Array.Empty<StoryConfiguration.ReplyChoice>(); IsOpen = IsReply = false; Revision++;
            if (pending.Count > 0) OpenReply(pending.Dequeue());
            return true;
        }
        public bool Finished(string id) => User(id).Profile?.HasSessions == true && string.IsNullOrEmpty(User(id).NodeId);
        public string Progress(string id) => User(id).Profile?.Progress(User(id).NodeId) ?? string.Empty;
        public TaskDefinition BuildNextTask(string id, IReadOnlyDictionary<string, TaskDefinition> templates, TaskDefinition fallback)
        {
            var state = User(id); var session = state.Profile?.FindSession(state.NodeId);
            string templateId = session?.RequestId;
            var basis = !string.IsNullOrEmpty(templateId) && templates.TryGetValue(templateId, out var template) ? template : fallback;
            if (session == null && generation == null) return basis;
            var difficulty = session?.Task;
            var requirements = difficulty?.Abilities != null && difficulty.Abilities.Length > 0
                ? difficulty.Abilities.Select(a => a.Build()) : basis.Requirements;
            return new TaskDefinition(basis.Id, string.IsNullOrWhiteSpace(session?.Title) ? basis.Title : session.Title,
                difficulty?.Shape != null && difficulty.Shape.Length > 0 ? ShapePattern.Parse(difficulty.Shape) : generation != null ? generation.PickShape(shapeRandom) : basis.Shape,
                difficulty != null && difficulty.Calculation > 0 ? difficulty.Calculation : basis.Calculation,
                difficulty != null && difficulty.Speed > 0 ? difficulty.Speed : basis.Speed,
                difficulty != null && difficulty.Patience > 0 ? difficulty.Patience : basis.Patience,
                difficulty != null && difficulty.Tokens >= 0 ? difficulty.Tokens : basis.Tokens, requirements);
        }
        public void OpenHistory(string id)
        {
            if (IsReply || string.IsNullOrEmpty(id)) return;
            UserId = id; IsOpen = true; IsReply = false; Revision++;
        }
        public void CloseHistory() { if (!IsReply) { IsOpen = false; Revision++; } }
    }
}
