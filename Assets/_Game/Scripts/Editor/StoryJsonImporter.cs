using System;
using System.Collections.Generic;
using System.Linq;
using CallmeCatgirl.Gameplay;
using CallmeCatgirl.Gameplay.TaskGrid;
using UnityEngine;

namespace CallmeCatgirl.EditorTools
{
    // Editor-only import: no dependency or file loading is added to the player.
    public static class StoryJsonImporter
    {
        [Serializable] private sealed class Document { public int version; public Character[] characters; }
        [Serializable] private sealed class Character
        { public string id, name, initialHistory, kind; public Session[] sessions; }
        [Serializable] private sealed class Session
        { public string id, title, requestId; public Difficulty task; public Round[] rounds; }
        [Serializable] private sealed class Round { public string id, message; public Choice[] choices; }
        [Serializable] private sealed class Choice
        { public string label, reply, response, requirement, nextRoundId; public int minimumFunds, fundsReward; }
        [Serializable] private sealed class Difficulty { public float calculation, patience; public string[] shape; public Ability[] abilities; }
        [Serializable] private sealed class Ability { public string kind; public float minimum, excellent; }

        public static StoryConfiguration Parse(string json, IEnumerable<string> requestIds)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("JSON内容为空。");
            Document doc;
            try { doc = JsonUtility.FromJson<Document>(json.TrimStart('\uFEFF')); }
            catch (Exception e) { throw new ArgumentException("JSON语法错误：" + e.Message, e); }
            if (doc == null || doc.version != 1) throw new ArgumentException("JSON必须包含 version: 1。");
            if (doc.characters == null || doc.characters.Length == 0) throw new ArgumentException("characters 必须至少包含一个角色。");
            var known = new HashSet<string>(requestIds ?? throw new ArgumentNullException(nameof(requestIds)));
            var config = ScriptableObject.CreateInstance<StoryConfiguration>();
            try
            {
                config.Users = doc.characters.Select(c => Convert(c, known)).ToArray();
                config.Validate(known); return config;
            }
            catch { UnityEngine.Object.DestroyImmediate(config); throw; }
        }
        // Parse and validate the whole result before modifying the selected asset.
        public static void Apply(string json, StoryConfiguration target, IEnumerable<string> requestIds, bool replaceAll)
        {
            var imported = Parse(json, requestIds);
            try { ApplyParsed(imported, target, requestIds, replaceAll); }
            finally { UnityEngine.Object.DestroyImmediate(imported); }
        }
        internal static void ApplyParsed(StoryConfiguration imported, StoryConfiguration target, IEnumerable<string> requestIds, bool replaceAll)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            var combined = ScriptableObject.CreateInstance<StoryConfiguration>();
            try
            {
                var users = replaceAll ? new List<StoryConfiguration.UserStory>()
                    : new List<StoryConfiguration.UserStory>(target.Users ?? Array.Empty<StoryConfiguration.UserStory>());
                foreach (var user in imported.Users)
                {
                    int index = users.FindIndex(u => u != null && u.UserId == user.UserId);
                    if (index < 0) users.Add(user); else users[index] = user;
                }
                combined.Users = users.ToArray(); combined.DefaultChoices = target.DefaultChoices;
                combined.Validate(requestIds); target.Users = combined.Users;
            }
            finally { UnityEngine.Object.DestroyImmediate(combined); }
        }
        private static StoryConfiguration.UserStory Convert(Character data, HashSet<string> known)
        {
            if (data == null) throw new ArgumentException("characters 不能包含 null。");
            Id(data.id, "角色id");
            var kind = string.IsNullOrEmpty(data.kind) ? StoryConfiguration.ConversationKind.Ordinary
                : ParseEnum<StoryConfiguration.ConversationKind>(data.kind, "characters.kind");
            if (data.sessions == null || data.sessions.Length == 0) throw new ArgumentException("角色 " + data.id + " 的 sessions 至少需要一次会话。");
            var result = new StoryConfiguration.UserStory
            {
                Kind = kind, UserId = data.id, DisplayName = string.IsNullOrWhiteSpace(data.name) ? data.id : data.name,
                InitialHistory = data.initialHistory, Sessions = new StoryConfiguration.ConversationSession[data.sessions.Length]
            };
            for (int s = 0; s < data.sessions.Length; s++)
            {
                var session = data.sessions[s]; string path = "角色 " + data.id + " / 第" + (s + 1) + "次会话";
                if (session == null) throw new ArgumentException(path + " 不能为null。");
                Id(session.id, path + " id");
                if (session.rounds == null || session.rounds.Length == 0) throw new ArgumentException(path + " rounds 至少需要一轮。");
                var converted = new StoryConfiguration.ConversationSession
                {
                    Id = session.id, Title = string.IsNullOrWhiteSpace(session.title) ? session.id : session.title,
                    RequestId = string.IsNullOrEmpty(session.requestId) ? data.id : session.requestId,
                    Task = Convert(session.task), Rounds = new StoryConfiguration.StoryNode[session.rounds.Length]
                };
                result.Sessions[s] = converted;
                for (int r = 0; r < session.rounds.Length; r++)
                {
                    var round = session.rounds[r]; string location = path + " / 第" + (r + 1) + "轮";
                    if (round == null) throw new ArgumentException(location + " 不能为null。");
                    Id(round.id, location + " id");
                    if (string.IsNullOrWhiteSpace(round.message)) throw new ArgumentException(location + " 缺少 message 用户发言。");
                    if (round.choices == null || round.choices.Length == 0) throw new ArgumentException(location + " choices 至少需要一个选项。");
                    converted.Rounds[r] = new StoryConfiguration.StoryNode
                    {
                        Id = session.id + "/" + round.id, UserMessage = round.message,
                        Choices = round.choices.Select(c => Convert(c, data, s, r, location)).ToArray()
                    };
                }
            }
            return result;
        }
        private static StoryConfiguration.ReplyChoice Convert(Choice choice, Character user, int session, int round, string location)
        {
            if (choice == null || string.IsNullOrWhiteSpace(choice.label)) throw new ArgumentException(location + " 的选项必须填写 label。");
            string next = choice.nextRoundId;
            if (string.IsNullOrEmpty(next) || next == "NEXT")
            {
                if (round + 1 < user.sessions[session].rounds.Length)
                    next = user.sessions[session].id + "/" + user.sessions[session].rounds[round + 1]?.id;
                else next = NextSession(user, session);
            }
            else if (next == "END") next = NextSession(user, session);
            else if (!next.Contains("/")) next = user.sessions[session].id + "/" + next;
            // Explicit jumps must stay in the current session. END handles the next one.
            if (!string.IsNullOrEmpty(choice.nextRoundId) && choice.nextRoundId != "NEXT" && choice.nextRoundId != "END"
                && !next.StartsWith(user.sessions[session].id + "/", StringComparison.Ordinal))
                throw new ArgumentException(location + " 不能跳到另一场会话的中间；请用 END 结束本次会话。");
            return new StoryConfiguration.ReplyChoice
            {
                Label = choice.label, PlayerReply = string.IsNullOrEmpty(choice.reply) ? choice.label : choice.reply,
                MinimumFunds = choice.minimumFunds, FundsReward = choice.fundsReward,
                UserReply = choice.response, Requirement = string.IsNullOrEmpty(choice.requirement) ? StoryConfiguration.ReplyRequirement.Any
                    : ParseEnum<StoryConfiguration.ReplyRequirement>(choice.requirement, location + " requirement"), NextNodeId = next
            };
        }
        private static string NextSession(Character user, int index)
        {
            if (index + 1 >= user.sessions.Length) return null;
            var next = user.sessions[index + 1];
            if (next?.rounds == null || next.rounds.Length == 0) throw new ArgumentException("角色 " + user.id + " 的下一场会话没有轮次。");
            return next.id + "/" + next.rounds[0]?.id;
        }
        private static StoryConfiguration.RequestDifficulty Convert(Difficulty data)
        {
            if (data == null) return null;
            return new StoryConfiguration.RequestDifficulty
            {
                Calculation = data.calculation, Patience = data.patience, Shape = data.shape,
                Abilities = data.abilities?.Select(a =>
                {
                    if (a == null) throw new ArgumentException("task.abilities 不能包含null。");
                    return new StoryConfiguration.AbilityTarget
                    { Kind = ParseEnum<AbilityKind>(a.kind, "task.abilities.kind"), Minimum = a.minimum, Excellent = a.excellent };
                }).ToArray()
            };
        }
        private static T ParseEnum<T>(string text, string field) where T : struct, Enum
        {
            if (!Enum.GetNames(typeof(T)).Any(n => string.Equals(n, text, StringComparison.OrdinalIgnoreCase))
                || !Enum.TryParse(text, true, out T result)) throw new ArgumentException(field + " 无效：" + text);
            return result;
        }
        private static void Id(string value, string field)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Contains("/") || value == "NEXT" || value == "END")
                throw new ArgumentException(field + " 不能为空、包含/或使用保留字NEXT/END。");
        }
    }
}
