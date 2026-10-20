using System;
using System.Collections.Generic;
using System.Linq;
using CallmeCatgirl.Gameplay.TaskGrid;

namespace CallmeCatgirl.Gameplay
{
    /// <summary>Authored customer identity reused by a lightweight filler request; no story state is created.</summary>
    public sealed class VisitorSource
    {
        public string DisplayName { get; }
        public string Text { get; }
        public string RequestFamily { get; }
        public string CustomerId { get; }
        public VisitorSource(string displayName, string text, string requestFamily, string customerId)
        {
            DisplayName = displayName;
            Text = text;
            RequestFamily = requestFamily;
            CustomerId = customerId;
        }
    }

    public static class LightweightVisitorQueue
    {
        private const int ShortTextLength = 24;

        /// <summary>Legacy A/B/C, the ordinary sample pool, and customers from earlier stages only.</summary>
        public static IReadOnlyList<VisitorSource> Sources(StoryConfiguration story, StoryConfiguration ordinaryPool,
            StoryConfiguration stagePool, int stage)
        {
            var sources = new List<VisitorSource>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            AddUsers(sources, seen, story?.Users);
            AddUsers(sources, seen, ordinaryPool?.Users);
            foreach (var user in stagePool?.Users ?? Array.Empty<StoryConfiguration.UserStory>())
            {
                int customerStage = OrdinaryStagePool.StageOf(user?.UserId);
                if (customerStage >= 1 && customerStage < stage) AddUser(sources, seen, user);
            }
            return sources.Count == 0 ? Array.Empty<VisitorSource>() : sources.ToArray();
        }

        public static TaskDefinition Build(string requestId, VisitorSource source, OrdinaryStagePool.StageRule rule, Cell[] shape)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (rule == null) throw new ArgumentNullException(nameof(rule));
            string family = source.RequestFamily == "A" ? "A" : source.RequestFamily == "B" ? "B" : "C";
            AbilityKind kind = family == "A" ? AbilityKind.Writing : family == "B" ? AbilityKind.Code : AbilityKind.Reasoning;
            double minimum = family == "A" ? rule.Writing : family == "B" ? rule.Code : rule.Reasoning;
            double excellent = Math.Max(minimum + 1, minimum * 1.2);
            return new TaskDefinition(requestId, ShortText(source.Text), shape, rule.Calculation, rule.Speed, rule.Patience,
                rule.FillerTokens, new[] { new AbilityRequirement(kind, minimum, excellent) });
        }

        private static void AddUsers(List<VisitorSource> sources, HashSet<string> seen, StoryConfiguration.UserStory[] users)
        {
            foreach (var user in users ?? Array.Empty<StoryConfiguration.UserStory>())
                if (user != null && user.Kind == StoryConfiguration.ConversationKind.Ordinary) AddUser(sources, seen, user);
        }

        private static void AddUser(List<VisitorSource> sources, HashSet<string> seen, StoryConfiguration.UserStory user)
        {
            if (user == null || string.IsNullOrWhiteSpace(user.UserId) || !seen.Add(user.UserId)) return;
            string name = string.IsNullOrWhiteSpace(user.DisplayName) ? "客户 " + user.UserId : user.DisplayName.Trim();
            string text = FirstText(user);
            sources.Add(new VisitorSource(name, text ?? name + "发来了一条委托。", Family(user), user.UserId));
        }

        private static string FirstText(StoryConfiguration.UserStory user)
            => user.AllRounds.Select(round => round?.UserMessage).FirstOrDefault(message => !string.IsNullOrWhiteSpace(message));

        private static string Family(StoryConfiguration.UserStory user)
        {
            string requestId = user.Sessions?.FirstOrDefault()?.RequestId;
            return requestId == "A" ? "A" : requestId == "B" ? "B" : "C";
        }

        private static string ShortText(string text)
            => text.Length <= ShortTextLength ? text : text.Substring(0, ShortTextLength) + "…";
    }
}
