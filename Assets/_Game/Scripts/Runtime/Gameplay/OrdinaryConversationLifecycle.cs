using System;
using System.Collections.Generic;
using System.Linq;
using CallmeCatgirl.Gameplay.TaskGrid;
using CallmeCatgirl.Persistence;

namespace CallmeCatgirl.Gameplay
{
    /// <summary>Temporary tuning defaults, not confirmed balance. First settled round uses Base + Growth.</summary>
    [Serializable] public sealed class OrdinaryConversationPolicy
    {
        public double Base = .02;
        public double Growth = .05;
        public double ExcellentGrowthMultiplier = .5;
        public double Max = .8;

        public void Validate()
        {
            if (!Finite(Base) || !Finite(Growth) || !Finite(ExcellentGrowthMultiplier) || !Finite(Max)
                || Base < 0 || Max < Base || Max > 1 || Growth < 0
                || ExcellentGrowthMultiplier < 0 || ExcellentGrowthMultiplier > 1)
                throw new ArgumentException("Invalid ordinary conversation probability configuration.");
        }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    /// <summary>One draw after each completed request is submitted. No clock, corpus inventory or private RNG.</summary>
    public sealed class OrdinaryConversationLifecycle
    {
        private readonly double initial, growth, excellentMultiplier, maximum;
        private readonly Dictionary<string, OrdinaryConversationState> conversations = new Dictionary<string, OrdinaryConversationState>(StringComparer.Ordinal);
        private readonly HashSet<string> settled = new HashSet<string>(StringComparer.Ordinal);

        public OrdinaryConversationLifecycle(OrdinaryConversationPolicy policy)
        {
            if (policy == null) throw new ArgumentNullException(nameof(policy));
            policy.Validate(); initial = policy.Base; growth = policy.Growth;
            excellentMultiplier = policy.ExcellentGrowthMultiplier; maximum = policy.Max;
        }
        private double NextProbability(double current, Quality quality)
            => Math.Min(maximum, current + growth * (quality == Quality.Excellent ? excellentMultiplier : 1));

        /// <returns>True only when a new ordinary settlement was recorded. Scripted/repeated calls never draw.</returns>
        public bool ObserveSettlement(TaskInstance task, bool authoredStory, Random existingRandom, double endProbabilityMultiplier = 1)
        {
            if (authoredStory || task == null || task.Result == null
                || (task.State != RequestState.Submitted && task.State != RequestState.Closed)) return false;
            if (settled.Contains(task.Id)) return false;
            conversations.TryGetValue(task.ConversationId, out var prior);
            if (prior?.Ended == true) throw new InvalidOperationException("An ended ordinary conversation cannot settle a new request.");
            if (existingRandom == null) throw new ArgumentNullException(nameof(existingRandom), "Use the host's saved requestRandom stream.");
            Require(!double.IsNaN(endProbabilityMultiplier) && !double.IsInfinity(endProbabilityMultiplier)
                && endProbabilityMultiplier > 0 && endProbabilityMultiplier <= 1000000, "policy multiplier");
            int count = checked((prior?.CompletedRequestCount ?? 0) + 1);
            var quality = task.Result.Overall;
            double probability = NextProbability(prior?.EndProbability ?? initial, quality);
            double roll = existingRandom.NextDouble(); // Exactly one draw, including probability 0 or 1.
            if (double.IsNaN(roll) || roll < 0 || roll >= 1) throw new ArgumentException("Random.NextDouble must return [0,1).");
            double effectiveProbability = Math.Min(1, probability * endProbabilityMultiplier);
            bool ended = roll < effectiveProbability;
            var entry = new OrdinarySettlementState { RequestId = task.Id, RoundId = task.RoundId, Quality = quality,
                EndProbability = effectiveProbability, Roll = roll, Ended = ended, PolicyEndMultiplier = endProbabilityMultiplier };
            conversations[task.ConversationId] = new OrdinaryConversationState {
                ConversationId = task.ConversationId, CompletedRequestCount = count,
                ExcellentRequestCount = (prior?.ExcellentRequestCount ?? 0) + (quality == Quality.Excellent ? 1 : 0),
                EndProbability = probability, Ended = ended,
                LegacySettledRequestIds = prior?.LegacySettledRequestIds ?? Array.Empty<string>(),
                Settlements = (prior?.Settlements ?? Array.Empty<OrdinarySettlementState>()).Concat(new[] { entry }).ToArray()
            };
            settled.Add(task.Id); return true;
        }

        public bool IsEnded(string conversationId) => conversationId != null && conversations.TryGetValue(conversationId, out var state) && state.Ended;
        public OrdinaryConversationState Get(string conversationId)
        {
            if (string.IsNullOrWhiteSpace(conversationId)) throw new ArgumentException("Conversation ID required.");
            return conversations.TryGetValue(conversationId, out var state) ? Copy(state)
                : new OrdinaryConversationState { ConversationId = conversationId, EndProbability = initial };
        }
        public OrdinaryConversationLifecycleState CaptureState() => new OrdinaryConversationLifecycleState {
            Conversations = conversations.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => Copy(p.Value)).ToArray()
        };
        private static OrdinaryConversationState Copy(OrdinaryConversationState state) => new OrdinaryConversationState {
            ConversationId = state.ConversationId, CompletedRequestCount = state.CompletedRequestCount,
            ExcellentRequestCount = state.ExcellentRequestCount, EndProbability = state.EndProbability, Ended = state.Ended,
            LegacySettledRequestIds = (state.LegacySettledRequestIds ?? Array.Empty<string>()).ToArray(),
            Settlements = state.Settlements.Select(r => new OrdinarySettlementState {
                RequestId = r.RequestId, RoundId = r.RoundId, Quality = r.Quality,
                EndProbability = r.EndProbability, Roll = r.Roll, Ended = r.Ended, PolicyEndMultiplier = r.PolicyEndMultiplier
            }).ToArray()
        };

        /// <summary>Read existing v1 submitted-task facts without inventing historical draws, probabilities or endings.
        /// Known counts/IDs are preserved; probability starts at Base, ended is false, and missing history defaults to zero.</summary>
        public static OrdinaryConversationLifecycle RestoreLegacyV1(OrdinaryConversationPolicy policy,
            IEnumerable<TaskInstance> submittedRequests, Func<string, bool> isAuthoredStory)
        {
            var restored = new OrdinaryConversationLifecycle(policy);
            var sources = (submittedRequests ?? throw new ArgumentNullException(nameof(submittedRequests))).ToArray();
            foreach (var task in sources)
                Require(task != null && task.Result != null
                    && (task.State == RequestState.Submitted || task.State == RequestState.Closed), "legacy submitted source");
            foreach (var group in sources.Where(t => !isAuthoredStory(t.ConversationId)).GroupBy(t => t.ConversationId))
            {
                var ids = group.Select(t => t.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray();
                foreach (var id in ids) Require(restored.settled.Add(id), "legacy duplicate request");
                restored.conversations.Add(group.Key, new OrdinaryConversationState {
                    ConversationId = group.Key, CompletedRequestCount = ids.Length,
                    ExcellentRequestCount = group.Count(t => t.Result.Overall == Quality.Excellent),
                    EndProbability = restored.initial, Ended = false, LegacySettledRequestIds = ids
                });
            }
            return restored;
        }

        /// <summary>Detached restore validates sources and replays only arithmetic, never random draws or gameplay commands.</summary>
        public static OrdinaryConversationLifecycle RestoreState(OrdinaryConversationLifecycleState saved,
            OrdinaryConversationPolicy policy, IEnumerable<TaskInstance> submittedRequests, Func<string, bool> isAuthoredStory)
        {
            if (saved == null || saved.Version != 1 || saved.Conversations == null || saved.Conversations.Length > 100000)
                throw new ArgumentException("Missing/unsupported ordinary conversation lifecycle state.");
            var restored = new OrdinaryConversationLifecycle(policy);
            var sources = (submittedRequests ?? throw new ArgumentNullException(nameof(submittedRequests)))
                .Where(t => !isAuthoredStory(t.ConversationId)).ToDictionary(t => t.Id, StringComparer.Ordinal);
            foreach (var state in saved.Conversations)
            {
                Require(state != null && !string.IsNullOrWhiteSpace(state.ConversationId)
                    && !isAuthoredStory(state.ConversationId) && !restored.conversations.ContainsKey(state.ConversationId)
                    && state.Settlements != null, "conversation header");
                var legacyIds = state.LegacySettledRequestIds ?? Array.Empty<string>();
                Require(legacyIds.Length + state.Settlements.Length > 0
                    && legacyIds.Length + state.Settlements.Length <= sources.Count, "request history length");
                double probability = restored.initial; bool ended = false; int excellent = 0;
                foreach (var id in legacyIds)
                {
                    Require(id != null && restored.settled.Add(id) && sources.TryGetValue(id, out var oldTask)
                        && oldTask.ConversationId == state.ConversationId && oldTask.Result != null
                        && (oldTask.State == RequestState.Submitted || oldTask.State == RequestState.Closed), "legacy request source/duplicate");
                    if (sources[id].Result.Overall == Quality.Excellent) excellent++;
                }
                foreach (var item in state.Settlements)
                {
                    Require(item != null && item.RequestId != null && !ended && restored.settled.Add(item.RequestId)
                        && sources.TryGetValue(item.RequestId, out var task) && task.ConversationId == state.ConversationId
                        && task.RoundId == item.RoundId && task.Result != null && task.Result.Overall == item.Quality
                        && (task.State == RequestState.Submitted || task.State == RequestState.Closed), "request source/duplicate/order");
                    probability = restored.NextProbability(probability, item.Quality);
                    double multiplier = item.PolicyEndMultiplier == 0 ? 1 : item.PolicyEndMultiplier;
                    Require(!double.IsNaN(multiplier) && !double.IsInfinity(multiplier) && multiplier > 0 && multiplier <= 1000000, "saved policy multiplier");
                    double effectiveProbability = Math.Min(1, probability * multiplier);
                    Require(!double.IsNaN(item.Roll) && item.Roll >= 0 && item.Roll < 1
                        && item.EndProbability == effectiveProbability && item.Ended == (item.Roll < effectiveProbability), "probability/draw");
                    ended = item.Ended; if (item.Quality == Quality.Excellent) excellent++;
                }
                Require(state.CompletedRequestCount == legacyIds.Length + state.Settlements.Length && state.ExcellentRequestCount == excellent
                    && state.EndProbability == probability && state.Ended == ended, "count/terminal state");
                restored.conversations.Add(state.ConversationId, Copy(state));
            }
            Require(restored.settled.SetEquals(sources.Keys), "missing ordinary request ledger (cannot safely redraw)");
            return restored;
        }
        private static void Require(bool condition, string message)
        { if (!condition) throw new ArgumentException("Invalid ordinary conversation state: " + message); }
    }
}
