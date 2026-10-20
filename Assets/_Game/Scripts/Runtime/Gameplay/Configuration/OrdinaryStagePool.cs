using System;
using System.Collections.Generic;
using System.Linq;
using CallmeCatgirl.Gameplay.TaskGrid;
using UnityEngine;

namespace CallmeCatgirl.Gameplay
{
    /// <summary>Authoring JSON for the 1-5 stage ordinary customer pool. Text stays in NarrativeText.</summary>
    public sealed class StagePoolExhaustedException : InvalidOperationException
    {
        public StagePoolExhaustedException(string message) : base(message) { }
    }

    public static class OrdinaryStagePool
    {
        public const string ResourcePath = "OrdinaryPool/ordinary-pool-stages";
        public const string DifficultyPath = "OrdinaryPool/ordinary-pool-difficulty";
        private static StoryConfiguration cached;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlay() => cached = null;

        public static bool IsStageCustomer(string id)
            => !string.IsNullOrEmpty(id) && id.StartsWith("stage", StringComparison.Ordinal) && StageOf(id) > 0;

        public static int StageOf(string id)
        {
            if (string.IsNullOrEmpty(id) || !id.StartsWith("stage", StringComparison.Ordinal)) return 0;
            int dash = id.IndexOf('-', 5);
            int stage;
            return dash > 5 && int.TryParse(id.Substring(5, dash - 5), out stage) && stage >= 1 && stage <= 5 ? stage : 0;
        }

        public static StoryConfiguration Load()
        {
            if (cached != null) return cached;
            var asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null) return null;
            PoolDocument document;
            try { document = JsonUtility.FromJson<PoolDocument>(asset.text.TrimStart('\uFEFF')); }
            catch (Exception error) { throw new InvalidOperationException("Invalid " + ResourcePath + ".json: " + error.Message, error); }
            if (document == null || document.schemaVersion != 1 || document.stages == null || document.stages.Length == 0)
                throw new InvalidOperationException("ordinary-pool-stages.json requires schemaVersion 1 and stages.");

            var difficulty = LoadDifficulty();
            var users = new List<StoryConfiguration.UserStory>();
            foreach (var stage in document.stages)
            {
                if (stage == null || stage.stage < 1 || stage.stage > 5 || stage.customers == null)
                    throw new InvalidOperationException("Invalid stage entry in ordinary-pool-stages.json.");
                foreach (var customer in stage.customers)
                    users.Add(BuildUser(customer, stage.stage, difficulty));
            }
            var pool = ScriptableObject.CreateInstance<StoryConfiguration>();
            pool.name = "OrdinaryPoolStages";
            pool.Users = users.ToArray();
            cached = pool;
            return cached;
        }

        private static StoryConfiguration.UserStory BuildUser(Customer customer, int stage, IReadOnlyDictionary<int, DifficultyStage> difficulty)
        {
            if (customer == null || !IsStageCustomer(customer.userId) || StageOf(customer.userId) != stage)
                throw new InvalidOperationException("Stage customer ID does not match its stage: " + (customer?.userId ?? "<null>"));
            if (customer.sessions == null || customer.sessions.Length == 0)
                throw new InvalidOperationException("Stage customer has no sessions: " + customer.userId);
            DifficultyStage rule;
            if (!difficulty.TryGetValue(stage, out rule)) throw new InvalidOperationException("Missing stage difficulty: " + stage);
            var sessions = customer.sessions.Select(session => BuildSession(customer.userId, session, rule)).ToArray();
            return new StoryConfiguration.UserStory
            {
                Kind = StoryConfiguration.ConversationKind.Ordinary,
                UserId = customer.userId,
                DisplayName = Text(customer.displayNameTextId),
                InitialHistory = Text(customer.initialHistoryTextId),
                Sessions = sessions
            };
        }

        private static StoryConfiguration.ConversationSession BuildSession(string userId, Session session, DifficultyStage rule)
        {
            if (session == null || string.IsNullOrWhiteSpace(session.id) || session.rounds == null || session.rounds.Length == 0)
                throw new InvalidOperationException("Invalid session for " + userId);
            return new StoryConfiguration.ConversationSession
            {
                Id = session.id,
                Title = Text(session.titleTextId),
                RequestId = session.requestId,
                Task = BuildDifficulty(session.requestId, rule),
                Rounds = session.rounds.Select(round => BuildRound(userId, round)).ToArray()
            };
        }

        private static StoryConfiguration.RequestDifficulty BuildDifficulty(string requestId, DifficultyStage rule)
        {
            AbilityKind kind = requestId == "A" ? AbilityKind.Writing
                : requestId == "B" ? AbilityKind.Code : AbilityKind.Reasoning;
            float minimum = requestId == "A" ? rule.writing : requestId == "B" ? rule.code : rule.reasoning;
            float excellent = Math.Max(minimum + 1f, minimum * 1.2f);
            return new StoryConfiguration.RequestDifficulty
            {
                Calculation = rule.calculation,
                Patience = rule.patience,
                Speed = rule.speed,
                Tokens = rule.tokens,
                Abilities = new[]
                {
                    new StoryConfiguration.AbilityTarget { Kind = kind, Minimum = minimum, Excellent = excellent }
                }
            };
        }

        private static StoryConfiguration.StoryNode BuildRound(string userId, Round round)
        {
            if (round == null || string.IsNullOrWhiteSpace(round.id) || round.choices == null || round.choices.Length != 3)
                throw new InvalidOperationException("Each stage round needs exactly three choices: " + userId);
            return new StoryConfiguration.StoryNode
            {
                Id = round.id,
                UserMessage = Text(round.userMessageTextId),
                Choices = round.choices.Select(choice => BuildChoice(userId, round.id, choice)).ToArray()
            };
        }

        private static StoryConfiguration.ReplyChoice BuildChoice(string userId, string nodeId, Choice choice)
        {
            if (choice == null || string.IsNullOrWhiteSpace(choice.key))
                throw new InvalidOperationException("Invalid choice for " + userId + "/" + nodeId);
            return new StoryConfiguration.ReplyChoice
            {
                MinimumFunds = choice.minimumFunds,
                FundsReward = choice.fundsReward,
                Label = Text(choice.labelTextId),
                PlayerReply = Text(choice.playerReplyTextId),
                UserReply = Text(choice.userReplyTextId),
                Requirement = ParseRequirement(choice.requirement),
                NextNodeId = choice.nextNodeId ?? string.Empty
            };
        }

        private static StoryConfiguration.ReplyRequirement ParseRequirement(string value)
        {
            switch (value)
            {
                case "CompleteOrBetter": return StoryConfiguration.ReplyRequirement.CompleteOrBetter;
                case "ExcellentOnly": return StoryConfiguration.ReplyRequirement.ExcellentOnly;
                default: return StoryConfiguration.ReplyRequirement.Any;
            }
        }

        private static string Text(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new InvalidOperationException("Missing textId in ordinary-pool-stages.json.");
            return StoryTextLibrary.Get(id);
        }

        private static IReadOnlyDictionary<int, DifficultyStage> LoadDifficulty()
        {
            var asset = Resources.Load<TextAsset>(DifficultyPath);
            if (asset == null) return DefaultDifficulty();
            DifficultyDocument document;
            try { document = JsonUtility.FromJson<DifficultyDocument>(asset.text.TrimStart('\uFEFF')); }
            catch (Exception error) { throw new InvalidOperationException("Invalid " + DifficultyPath + ".json: " + error.Message, error); }
            if (document == null || document.schemaVersion != 1 || document.stages == null || document.stages.Length == 0)
                throw new InvalidOperationException("ordinary-pool-difficulty.json requires schemaVersion 1 and stages.");
            return document.stages.ToDictionary(stage => stage.stage);
        }

        private static IReadOnlyDictionary<int, DifficultyStage> DefaultDifficulty()
        {
            return new[]
            {
                new DifficultyStage { stage = 1, calculation = 100, patience = 120, speed = 10, tokens = 120, fillerTokens = 12, writing = 10, code = 10, reasoning = 10, targetConcurrent = 3, targetRequests = 0 },
                new DifficultyStage { stage = 2, calculation = 900, patience = 120, speed = 10, tokens = 120, fillerTokens = 12, writing = 90, code = 90, reasoning = 90, targetConcurrent = 6, targetRequests = 60 },
                new DifficultyStage { stage = 3, calculation = 900, patience = 120, speed = 10, tokens = 120, fillerTokens = 12, writing = 144, code = 144, reasoning = 144, targetConcurrent = 9, targetRequests = 150 },
                new DifficultyStage { stage = 4, calculation = 900, patience = 120, speed = 10, tokens = 120, fillerTokens = 12, writing = 216, code = 216, reasoning = 216, targetConcurrent = 11, targetRequests = 220 },
                new DifficultyStage { stage = 5, calculation = 900, patience = 120, speed = 10, tokens = 120, fillerTokens = 12, writing = 306, code = 306, reasoning = 306, targetConcurrent = 12, targetRequests = 120 }
            }.ToDictionary(stage => stage.stage);
        }

        [Serializable] private sealed class PoolDocument { public int schemaVersion; public Stage[] stages; }
        [Serializable] private sealed class Stage { public int stage; public Customer[] customers; }
        [Serializable] private sealed class Customer
        {
            public string userId, displayNameTextId, initialHistoryTextId;
            public Session[] sessions;
        }
        [Serializable] private sealed class Session
        {
            public string id, titleTextId, requestId;
            public Round[] rounds;
        }
        [Serializable] private sealed class Round
        {
            public string id, userMessageTextId;
            public Choice[] choices;
        }
        [Serializable] private sealed class Choice
        {
            public string key, labelTextId, playerReplyTextId, userReplyTextId, requirement, nextNodeId;
            public int minimumFunds, fundsReward;
        }
        [Serializable] private sealed class DifficultyDocument { public int schemaVersion; public DifficultyStage[] stages; }
        [Serializable] private sealed class DifficultyStage
        {
            public int stage;
            public float calculation, patience, speed, tokens, fillerTokens, writing, code, reasoning;
            public int targetConcurrent, targetRequests;
        }

        /// <summary>Read-only copy of one authored stage row for the visitor scheduler.</summary>
        public sealed class StageRule
        {
            public int Stage { get; internal set; }
            public double Calculation { get; internal set; }
            public double Patience { get; internal set; }
            public double Speed { get; internal set; }
            public double StoryTokens { get; internal set; }
            public double FillerTokens { get; internal set; }
            public double Writing { get; internal set; }
            public double Code { get; internal set; }
            public double Reasoning { get; internal set; }
            public int TargetConcurrent { get; internal set; }
            public int TargetRequests { get; internal set; }
        }

        public static bool TryGetRule(int stage, out StageRule rule)
        {
            rule = null;
            if (stage < 1 || stage > 5) return false;
            DifficultyStage source;
            if (!LoadDifficulty().TryGetValue(stage, out source)) return false;
            rule = new StageRule
            {
                Stage = stage, Calculation = source.calculation, Patience = source.patience, Speed = source.speed,
                StoryTokens = source.tokens, FillerTokens = source.fillerTokens, Writing = source.writing,
                Code = source.code, Reasoning = source.reasoning, TargetConcurrent = source.targetConcurrent,
                TargetRequests = source.targetRequests
            };
            return true;
        }
    }
}
