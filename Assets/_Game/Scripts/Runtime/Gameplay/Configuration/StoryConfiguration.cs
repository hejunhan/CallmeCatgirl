using System;
using System.Collections.Generic;
using System.Linq;
using CallmeCatgirl.Gameplay.TaskGrid;
using UnityEngine;

namespace CallmeCatgirl.Gameplay
{
    [CreateAssetMenu(menuName = "CallmeCatgirl/Story Configuration")]
    public sealed class StoryConfiguration : ScriptableObject
    {
        [InspectorName("用户剧情")] public UserStory[] Users = Array.Empty<UserStory>();
        [InspectorName("默认回复选项")] public ReplyChoice[] DefaultChoices = { new ReplyChoice() };

        public void Validate(IEnumerable<string> requestIds)
        {
            var tasks = new HashSet<string>(requestIds);
            var ids = new HashSet<string>();
            foreach (var user in Users ?? Array.Empty<UserStory>())
            {
                if (user == null || string.IsNullOrWhiteSpace(user.UserId) || !ids.Add(user.UserId))
                    throw new InvalidOperationException("剧情用户ID为空或重复。");
                var sessions = user.Sessions ?? Array.Empty<ConversationSession>();
                if (sessions.Length == 0) throw new InvalidOperationException("用户 " + user.UserId + " 至少需要一次会话。");
                var sessionIds = new HashSet<string>();
                foreach (var session in sessions)
                    if (session == null || string.IsNullOrWhiteSpace(session.Id) || !sessionIds.Add(session.Id)
                        || session.Rounds == null || session.Rounds.Length == 0)
                        throw new InvalidOperationException("用户 " + user.UserId + " 的会话ID为空、重复或没有对话轮次。");
                var nodes = new HashSet<string>();
                foreach (var node in user.AllRounds)
                    if (node == null || string.IsNullOrWhiteSpace(node.Id) || !nodes.Add(node.Id))
                        throw new InvalidOperationException("用户 " + user.UserId + " 的剧情段落ID为空或重复。");
                foreach (var node in user.AllRounds)
                {
                    if (node.Choices == null || node.Choices.Length == 0)
                        throw new InvalidOperationException("用户 " + user.UserId + " 的对话轮次没有回复选项：" + node.Id);
                }
                foreach (var session in sessions)
                {
                    if (!string.IsNullOrEmpty(session.RequestId) && !tasks.Contains(session.RequestId))
                        throw new InvalidOperationException("用户 " + user.UserId + " 的任务模板不存在：" + session.RequestId);
                    session.Task?.Validate();
                }
                var choices = user.AllRounds.SelectMany(n => n.Choices ?? Array.Empty<ReplyChoice>())
                    .Concat(DefaultChoices ?? Array.Empty<ReplyChoice>());
                foreach (var choice in choices.Where(c => c != null))
                {
                    if (!string.IsNullOrEmpty(choice.NextNodeId) && !nodes.Contains(choice.NextNodeId))
                        throw new InvalidOperationException("用户 " + user.UserId + " 的下一段剧情不存在：" + choice.NextNodeId);
                }
            }
        }

        [Serializable] public sealed class UserStory
        {
            [InspectorName("用户ID（对应请求ID）")] public string UserId;
            [InspectorName("显示姓名")] public string DisplayName;
            [TextArea(2, 6), InspectorName("开局对话记录")] public string InitialHistory;
            [InspectorName("会话列表（次数和轮数按各角色独立设置）")] public ConversationSession[] Sessions = Array.Empty<ConversationSession>();
            public bool HasSessions => Sessions != null && Sessions.Length > 0;
            public IEnumerable<StoryNode> AllRounds => (Sessions ?? Array.Empty<ConversationSession>()).SelectMany(s => s.Rounds ?? Array.Empty<StoryNode>());
            public StoryNode FindNode(string id) => AllRounds.FirstOrDefault(n => n != null && n.Id == id);
            public ConversationSession FindSession(string id) => Sessions?.FirstOrDefault(s => s.Rounds.Any(n => n.Id == id));
            public string FirstNodeId => Sessions?.FirstOrDefault()?.Rounds?.FirstOrDefault()?.Id;
            public string SequentialNext(string id)
            {
                var nodes = AllRounds.ToArray(); int index = Array.FindIndex(nodes, n => n.Id == id);
                return index >= 0 && index + 1 < nodes.Length ? nodes[index + 1].Id : null;
            }
            public string Progress(string id)
            {
                if (!HasSessions) return string.Empty;
                for (int i = 0; i < Sessions.Length; i++)
                {
                    int round = Array.FindIndex(Sessions[i].Rounds, n => n.Id == id);
                    if (round >= 0) return $"第{i + 1}/{Sessions.Length}次会话 · {Sessions[i].Title} · 第{round + 1}/{Sessions[i].Rounds.Length}轮";
                }
                return $"该角色剧情已完成（共{Sessions.Length}次会话）";
            }
        }
        [Serializable] public sealed class ConversationSession
        {
            public string Id;
            [InspectorName("会话标题")] public string Title;
            [InspectorName("本次会话任务模板ID")] public string RequestId;
            [InspectorName("整次会话计算难度（可留空）")] public RequestDifficulty Task;
            [InspectorName("对话轮次")] public StoryNode[] Rounds = Array.Empty<StoryNode>();
        }
        [Serializable] public sealed class StoryNode
        {
            public string Id;
            [TextArea(2, 6), InspectorName("用户发言")] public string UserMessage;
            [InspectorName("回复选项")] public ReplyChoice[] Choices = Array.Empty<ReplyChoice>();
        }
        [Serializable] public sealed class RequestDifficulty
        {
            [Min(0), Tooltip("0沿用任务模板")] public float Calculation;
            [Min(0), Tooltip("0沿用任务模板")] public float Patience;
            [Min(0), Tooltip("0沿用任务模板")] public float Speed;
            [Tooltip("-1沿用任务模板，0为无Token收入")] public float Tokens = -1;
            [InspectorName("方块图案（空为从独立形状库随机）")] public string[] Shape;
            [InspectorName("能力门槛（空为沿用模板）")] public AbilityTarget[] Abilities;
            public void Validate()
            {
                if (float.IsNaN(Calculation) || float.IsInfinity(Calculation) || Calculation < 0
                    || float.IsNaN(Patience) || float.IsInfinity(Patience) || Patience < 0)
                    throw new InvalidOperationException("剧情任务的计算量和忍耐必须是非负有限数值。");
                if (float.IsNaN(Speed) || float.IsInfinity(Speed) || Speed < 0
                    || float.IsNaN(Tokens) || float.IsInfinity(Tokens) || (Tokens < 0 && Tokens != -1))
                    throw new InvalidOperationException("计算速度必须非负，Token收入必须非负或为-1沿用模板。");
                if (Shape != null && Shape.Length > 0) ShapePattern.Parse(Shape);
                var kinds = new HashSet<AbilityKind>();
                foreach (var target in Abilities ?? Array.Empty<AbilityTarget>())
                {
                    if (target == null || !Enum.IsDefined(typeof(AbilityKind), target.Kind) || !kinds.Add(target.Kind))
                        throw new InvalidOperationException("剧情任务的能力类型为空、无效或重复。");
                    target.Build();
                }
            }
        }
        [Serializable] public sealed class AbilityTarget
        {
            public AbilityKind Kind;
            public float Minimum;
            public float Excellent;
            public AbilityRequirement Build() => new AbilityRequirement(Kind, Minimum, Excellent);
        }
        public enum ReplyRequirement { Any, CompleteOrBetter, ExcellentOnly }
        [Serializable] public sealed class ReplyChoice
        {
            [InspectorName("按钮文字")] public string Label = "发送回复";
            [TextArea(2, 5), InspectorName("玩家回复")] public string PlayerReply = "回复已发送。";
            [TextArea(2, 5), InspectorName("用户回应")] public string UserReply;
            [InspectorName("质量条件")] public ReplyRequirement Requirement;
            [InspectorName("下一段对话ID（空为角色剧情结束）")] public string NextNodeId;
            public bool IsAvailable(Quality quality) => Requirement == ReplyRequirement.Any
                || (Requirement == ReplyRequirement.CompleteOrBetter && quality >= Quality.Complete)
                || (Requirement == ReplyRequirement.ExcellentOnly && quality == Quality.Excellent);
        }
    }
}
