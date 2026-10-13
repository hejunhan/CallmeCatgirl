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
                var nodes = new HashSet<string>();
                foreach (var node in user.Nodes ?? Array.Empty<StoryNode>())
                    if (node == null || string.IsNullOrWhiteSpace(node.Id) || !nodes.Add(node.Id))
                        throw new InvalidOperationException("用户 " + user.UserId + " 的剧情段落ID为空或重复。");
                if (!string.IsNullOrEmpty(user.StartNodeId) && !nodes.Contains(user.StartNodeId))
                    throw new InvalidOperationException("用户 " + user.UserId + " 的首段剧情不存在：" + user.StartNodeId);
                var choices = (user.Nodes ?? Array.Empty<StoryNode>()).SelectMany(n => n.Choices ?? Array.Empty<ReplyChoice>())
                    .Concat(DefaultChoices ?? Array.Empty<ReplyChoice>());
                foreach (var choice in choices.Where(c => c != null))
                {
                    if (!string.IsNullOrEmpty(choice.NextNodeId) && !nodes.Contains(choice.NextNodeId))
                        throw new InvalidOperationException("用户 " + user.UserId + " 的下一段剧情不存在：" + choice.NextNodeId);
                    if (!string.IsNullOrEmpty(choice.NextRequestId) && !tasks.Contains(choice.NextRequestId))
                        throw new InvalidOperationException("下一轮任务不存在：" + choice.NextRequestId);
                }
            }
        }

        [Serializable] public sealed class UserStory
        {
            [InspectorName("用户ID（对应请求ID）")] public string UserId;
            [InspectorName("显示姓名")] public string DisplayName;
            [TextArea(2, 6), InspectorName("开局对话记录")] public string InitialHistory;
            [InspectorName("首段剧情ID")] public string StartNodeId;
            [InspectorName("剧情段落")] public StoryNode[] Nodes = Array.Empty<StoryNode>();
        }
        [Serializable] public sealed class StoryNode
        {
            public string Id;
            [TextArea(2, 6), InspectorName("提交后用户发言")] public string UserMessage;
            [InspectorName("回复选项")] public ReplyChoice[] Choices = Array.Empty<ReplyChoice>();
        }
        public enum ReplyRequirement { Any, CompleteOrBetter, ExcellentOnly }
        [Serializable] public sealed class ReplyChoice
        {
            [InspectorName("按钮文字")] public string Label = "发送回复";
            [TextArea(2, 5), InspectorName("玩家回复")] public string PlayerReply = "回复已发送。";
            [TextArea(2, 5), InspectorName("用户回应")] public string UserReply;
            [InspectorName("质量条件")] public ReplyRequirement Requirement;
            [InspectorName("下一次提交的剧情ID（空为结束）")] public string NextNodeId;
            [InspectorName("下一轮任务ID（空为沿用）")] public string NextRequestId;
            public bool IsAvailable(Quality quality) => Requirement == ReplyRequirement.Any
                || (Requirement == ReplyRequirement.CompleteOrBetter && quality >= Quality.Complete)
                || (Requirement == ReplyRequirement.ExcellentOnly && quality == Quality.Excellent);
        }
    }
}
