using System;
using CallmeCatgirl.Persistence;

namespace CallmeCatgirl.UI.EndingGalgame
{
    /// <summary>Ending trigger node IDs are the contract; host run-end reasons stay in RunEndReasons.</summary>
    public static class EndingRunReasons
    {
        public const string Low = "ending.low";
        public const string Normal = "ending.normal";
        public const string Good = "ending.good";
        public const string Companion = "ending.companion";

        public static bool TryResolve(string triggerNodeId, out string runEndReason)
        {
            switch (EndingScriptLibrary.NormalizeNodeId(triggerNodeId))
            {
                case Low: runEndReason = RunEndReasons.PolicyBad; return true;
                case Normal: runEndReason = RunEndReasons.PolicyNormal; return true;
                case Good: runEndReason = RunEndReasons.PolicyGood; return true;
                case Companion: runEndReason = RunEndReasons.Emotion; return true;
                default: runEndReason = null; return false;
            }
        }

        public static string Resolve(string triggerNodeId)
        {
            if (!TryResolve(triggerNodeId, out var runEndReason))
                throw new ArgumentException("Unsupported ending trigger: " + (triggerNodeId ?? "<null>"), nameof(triggerNodeId));
            return runEndReason;
        }
    }

    public sealed class EndingRunRequest
    {
        public string EndingId;
        public string TriggerNodeId;
        public string RunEndReason;
    }

    /// <summary>Raises the run-end callback at most once per shown ending and rejects non-narrative reasons.</summary>
    public sealed class EndingCompletionGate
    {
        private bool raised;

        public bool IsRaised => raised;

        public void Reset() => raised = false;

        public bool TryRaise(EndingRunRequest request, Action<EndingRunRequest> sink)
        {
            if (raised) return false;
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (!RunEndReasons.IsNarrative(request.RunEndReason))
                throw new ArgumentException("Ending run reason must be narrative: " + (request.RunEndReason ?? "<null>"), nameof(request));
            raised = true;
            sink?.Invoke(request);
            return true;
        }
    }
}
