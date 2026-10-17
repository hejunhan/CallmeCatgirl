using System;
using CallmeCatgirl.Gameplay.TaskGrid;

namespace CallmeCatgirl.Persistence
{
    [Serializable] public sealed class OrdinarySettlementState
    {
        public string RequestId, RoundId;
        public Quality Quality;
        public double EndProbability, Roll;
        public bool Ended;
    }
    [Serializable] public sealed class OrdinaryConversationState
    {
        public string ConversationId;
        public int CompletedRequestCount, ExcellentRequestCount;
        public double EndProbability;
        public bool Ended;
        // v1 request history has no random draws. Preserve its known IDs only; never invent old rolls.
        public string[] LegacySettledRequestIds = Array.Empty<string>();
        public OrdinarySettlementState[] Settlements = Array.Empty<OrdinarySettlementState>();
    }
    [Serializable] public sealed class OrdinaryConversationLifecycleState
    {
        public int Version = 1;
        public OrdinaryConversationState[] Conversations = Array.Empty<OrdinaryConversationState>();
    }
}
