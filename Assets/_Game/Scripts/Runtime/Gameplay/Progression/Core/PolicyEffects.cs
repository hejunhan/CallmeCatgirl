using System;

namespace CallmeCatgirl.Gameplay.Progression
{
    public enum PolicyNodeKind { Ability, Policy, CorpusReward, Terminal }
    public enum PolicyRoute { None, Open, Closed }
    public enum PolicyEndingBand { Bad, Normal, Good }

    /// <summary>Independent inputs to host systems; no request completion or wallet logic lives here.</summary>
    [Serializable]
    public sealed class PolicyEffects
    {
        public double InferenceSpeedMultiplier = 1;
        public double TrainingScoreMultiplier = 1;
        public double TrainingCostMultiplier = 1;
        public double TrainingWorkMultiplier = 1;
        public double CustomerArrivalMultiplier = 1;
        public double ConversationRetentionMultiplier = 1;
        public double EndProbabilityMultiplier = 1;
        public bool AutoPlacement;
        public bool EmotionOpportunityUnlocked;
        public double RequestCountMultiplier => CustomerArrivalMultiplier;

        internal PolicyEffects Copy() => (PolicyEffects)MemberwiseClone();
        internal bool Valid => Positive(InferenceSpeedMultiplier) && Positive(TrainingScoreMultiplier)
            && Positive(TrainingCostMultiplier) && Positive(TrainingWorkMultiplier)
            && Positive(CustomerArrivalMultiplier) && Positive(ConversationRetentionMultiplier)
            && Positive(EndProbabilityMultiplier);
        private static bool Positive(double n) => !double.IsNaN(n) && !double.IsInfinity(n) && n > 0 && n <= 1000000;
        internal void Combine(PolicyEffects other)
        {
            InferenceSpeedMultiplier *= other.InferenceSpeedMultiplier;
            TrainingScoreMultiplier *= other.TrainingScoreMultiplier;
            TrainingCostMultiplier *= other.TrainingCostMultiplier;
            TrainingWorkMultiplier *= other.TrainingWorkMultiplier;
            CustomerArrivalMultiplier *= other.CustomerArrivalMultiplier;
            ConversationRetentionMultiplier *= other.ConversationRetentionMultiplier;
            EndProbabilityMultiplier *= other.EndProbabilityMultiplier;
            AutoPlacement |= other.AutoPlacement;
            EmotionOpportunityUnlocked |= other.EmotionOpportunityUnlocked;
            if (!Valid) throw new ArgumentException("Combined policy effects are not finite positive values.");
        }
    }

    [Serializable]
    public sealed class PolicyCorpusReward
    {
        public string EventId;
        public string NodeId;
        public string ConversationId;
        public int Stage = 3;
        public double FrozenPoints;
        public bool Acknowledged;
        internal PolicyCorpusReward Copy() => (PolicyCorpusReward)MemberwiseClone();
    }

    [Serializable]
    public sealed class PolicyReputationEvent
    {
        public string EventId;
        public int Delta;
        internal PolicyReputationEvent Copy() => (PolicyReputationEvent)MemberwiseClone();
    }

    public sealed class PolicyTerminalSignal
    {
        public string EventId { get; }
        public int Reputation { get; }
        public PolicyEndingBand Band { get; }
        internal PolicyTerminalSignal(string eventId, int reputation, PolicyEndingBand band)
        { EventId = eventId; Reputation = reputation; Band = band; }
    }
}
