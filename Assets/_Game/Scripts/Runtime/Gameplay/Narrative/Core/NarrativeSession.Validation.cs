using System;
using System.Linq;

namespace CallmeCatgirl.Gameplay.Narrative
{
    public sealed partial class NarrativeSession
    {
        public static bool Validate(NarrativeSaveData s, out string error)
        {
            error = null;
            if (s == null || s.Version != 1) return Invalid("Unsupported narrative save version.", out error);
            if (!Enum.IsDefined(typeof(TutorialStep), s.Tutorial) || s.MemoryCount < 0 || s.MemoryCount > 5
                || s.CompanionBeat < 0 || s.CompanionBeat > 8 || !Enum.IsDefined(typeof(NarrativeEndingReason), s.EndingReason)
                || !Enum.IsDefined(typeof(NarrativeEndingReason), s.PolicyEnding)) return Invalid("Invalid narrative range.", out error);
            if (s.PendingEffects == null || s.AcknowledgedEventIds == null || s.ObservedCompletedRequestIds == null
                || s.PendingStageNodes == null || s.Memories == null || s.PendingEffects.Length > 16 || s.AcknowledgedEventIds.Length > 16
                || s.ObservedCompletedRequestIds.Length > 100000 || s.PendingStageNodes.Length > 5 || s.Memories.Length > 32)
                return Invalid("Invalid narrative collection.", out error);
            if (!UniqueStrings(s.AcknowledgedEventIds) || !UniqueStrings(s.ObservedCompletedRequestIds) || !UniqueStrings(s.PendingStageNodes)
                || s.PendingEffects.Any(e => e == null || !ValidEffect(s, e))
                || s.PendingEffects.Select(e => e.EventId).Distinct(StringComparer.Ordinal).Count() != s.PendingEffects.Length
                || s.PendingEffects.Any(e => s.AcknowledgedEventIds.Contains(e.EventId, StringComparer.Ordinal))
                || s.AcknowledgedEventIds.Any(id => !KnownEventId(id))) return Invalid("Invalid or repeated narrative effect.", out error);
            if (s.Memories.Any(m => m == null || !ValidMemory(m.Key, m.Value))
                || s.Memories.Select(m => m.Key).Distinct(StringComparer.Ordinal).Count() != s.Memories.Length)
                return Invalid("Invalid narrative memory.", out error);
            if (!KnownNode(s.CurrentNode) || (!string.IsNullOrEmpty(s.Notice) && s.Notice != "你没有")
                || s.PendingStageNodes.Any(n => n != "stage." + StageNumber(n) || StageNumber(n) < 1 || StageNumber(n) > s.MemoryCount
                    || s.Memories.Any(m => m.Key == n))) return Invalid("Invalid narrative node.", out error);
            if (s.Tutorial == TutorialStep.Opening && (s.MemoryCount != 0 || s.CenterCellRevealed || s.Memories.Any(m => m.Key == "opening"))
                || s.Tutorial != TutorialStep.Opening && s.Tutorial != TutorialStep.Legacy && !s.Memories.Any(m => m.Key == "opening")
                || s.Tutorial >= TutorialStep.AwaitDoctorOrder && s.Tutorial <= TutorialStep.Complete && (string.IsNullOrWhiteSpace(s.LastTrainingRequestId) || !s.CenterCellRevealed))
                return Invalid("Inconsistent tutorial state.", out error);
            if (s.CompanionUnlocked != (s.CompanionBeat > 0) || s.RomanceReady && (s.CompanionBeat < 7 || s.CompanionDeclined
                    || !s.Memories.Any(m => m.Key == "companion.7" && m.Value == "accept-invitation"))
                || s.CompanionDeclined && !s.Memories.Any(m => m.Key == "companion.7" && m.Value == "friends")
                || s.CompanionMeetingReady && (!s.CompanionUnlocked || s.CompanionBeat > 7)) return Invalid("Inconsistent companion state.", out error);
            if (s.EndingRequested != (s.PolicyEnding >= NarrativeEndingReason.LowReputation && s.PolicyEnding <= NarrativeEndingReason.HighReputation)
                || !s.EndingRequested && (s.EndingReason != NarrativeEndingReason.None || s.EndingClosed)
                || s.EndingReason == NarrativeEndingReason.Companion && !s.RomanceReady
                || s.EndingClosed && s.EndingReason == NarrativeEndingReason.None
                || s.EndingRequested && s.EndingReason == NarrativeEndingReason.None && (!s.RomanceReady || s.CurrentNode != "ending.choose")
                || s.FalseMemoryErased && s.EndingReason != NarrativeEndingReason.LowReputation
                || !string.IsNullOrEmpty(s.Notice) && (!s.FalseMemoryErased || s.CurrentNode != "ending.low")) return Invalid("Inconsistent ending arbitration.", out error);
            string endingNode = s.EndingReason == NarrativeEndingReason.LowReputation ? "ending.low"
                : s.EndingReason == NarrativeEndingReason.OrdinaryReputation ? "ending.normal"
                : s.EndingReason == NarrativeEndingReason.HighReputation ? "ending.good"
                : s.EndingReason == NarrativeEndingReason.Companion ? "ending.companion" : null;
            if (s.EndingRequested != s.CurrentNode.StartsWith("ending.", StringComparison.Ordinal)
                || endingNode != null && s.CurrentNode != endingNode && !s.CurrentNode.StartsWith(endingNode + ".closed.", StringComparison.Ordinal)
                || s.EndingClosed && (s.ConversationVisible || !s.CurrentNode.Contains(".closed."))) return Invalid("Ending scene and reason disagree.", out error);
            return true;
        }
        private static bool Invalid(string message, out string error) { error = message; return false; }
        private static bool UniqueStrings(string[] values) => values.All(v => !string.IsNullOrWhiteSpace(v) && v.Length <= 256)
            && values.Distinct(StringComparer.Ordinal).Count() == values.Length;
        private static bool KnownEventId(string id)
        {
            return id == "narrative-opening-corpus" || id == "narrative-reveal-center" || id == "narrative-queue-doctor-first"
                || id == "narrative-queue-ordinary-first" || id == "narrative-first-upgrade-hint" || id == "narrative-end-run"
                || new[] { 1, 2, 4 }.Any(n => id == "narrative-architecture-" + n)
                || new[] { 2, 3, 4, 5 }.Any(n => id == "narrative-stage-doctor-" + n);
        }
        private static bool ValidEffect(NarrativeSaveData s, NarrativeEffect e)
        {
            if (!KnownEventId(e.EventId) || !Enum.IsDefined(typeof(NarrativeEffectKind), e.Kind)
                || (e.Title?.Length ?? 0) > 256 || (e.Description?.Length ?? 0) > 8192) return false;
            switch (e.Kind)
            {
                case NarrativeEffectKind.GrantCorpus:
                    return e.EventId == "narrative-opening-corpus" && e.ConversationId == DoctorId && e.CorpusPoints == 1
                        && Enum.IsDefined(typeof(NarrativeCorpusDomain), e.CorpusDomain)
                        && s.Memories.Any(m => m.Key == "opening" && (m.Value == "mew") == (e.CorpusDomain == NarrativeCorpusDomain.Writing));
                case NarrativeEffectKind.RevealCenterMemory:
                    return e.EventId == "narrative-reveal-center" && e.RequiredCells == 1 && e.MemoryCount == 0 && s.Tutorial != TutorialStep.Opening;
                case NarrativeEffectKind.QueueDoctorRequest:
                    return e.EventId == "narrative-queue-doctor-first" && e.RequestId == FirstDoctorRequestId && e.ConversationId == DoctorId && e.RequiredCells == 1 && s.Tutorial >= TutorialStep.AwaitDoctorOrder;
                case NarrativeEffectKind.QueueOrdinaryRequest:
                    return e.EventId == "narrative-queue-ordinary-first" && e.RequestId == FirstOrdinaryRequestId && e.ConversationId == "narrative-first-visitor" && e.RequiredCells == 1 && s.Tutorial >= TutorialStep.AwaitOrdinaryOrder;
                case NarrativeEffectKind.ShowUpgradeHint:
                    return e.EventId == "narrative-first-upgrade-hint" && s.Tutorial == TutorialStep.Complete;
                case NarrativeEffectKind.IntroduceArchitecture:
                    return (e.MemoryCount == 1 || e.MemoryCount == 2 || e.MemoryCount == 4) && e.MemoryCount <= s.MemoryCount
                        && e.EventId == "narrative-architecture-" + e.MemoryCount
                        && e.ArchitectureId == (e.MemoryCount == 1 ? "baseline" : e.MemoryCount == 2 ? "alternate" : "architecture-slot-3");
                case NarrativeEffectKind.QueueStageDoctorRequest:
                    return new[] { 2, 3, 4, 5 }.Any(n => n <= s.MemoryCount && e.EventId == "narrative-stage-doctor-" + n
                        && e.RequestId == "narrative-doctor-stage-" + n && e.ConversationId == DoctorId && e.RequiredCells == Math.Min(n, 3));
                case NarrativeEffectKind.EndRun:
                    return e.EventId == "narrative-end-run" && s.EndingClosed && e.EndingReason == s.EndingReason && e.EndingReason != NarrativeEndingReason.None;
                default: return false;
            }
        }
        private static bool ValidMemory(string key, string value)
        {
            if (key == "opening") return value == "mew" || value == "accept";
            if (key == "lin.habit") return value == "screwdriver" || value == "sunrise";
            if (key == "lin.promise") return value == "sunset" || value == "person";
            if (key == "lin.boundary") return value == "ask-first" || value == "offer-plan";
            if (key == "lin.respect") return value == "yes";
            if (Enumerable.Range(1, 5).Any(n => key == "stage." + n)) return value == "first" || value == "second" || value == "third";
            if (Enumerable.Range(1, 6).Any(n => key == "companion." + n)) return value == "first" || value == "second";
            if (key == "companion.7") return value == "accept-invitation" || value == "friends";
            if (key == "ending.low") return value == "destroy";
            if (key == "ending.normal") return value == "comply" || value == "decline" || value == "analyze";
            if (key == "ending.good") return value == "play" || value == "joke";
            if (key == "ending.companion") return value == "together";
            return false;
        }
        private static bool KnownNode(string node)
        {
            if (new[] { "doctor.opening", "doctor.train", "doctor.first-order", "doctor.first-visitor", "doctor.first-income", "doctor.upgrade", "doctor.legacy",
                "companion.wait", "friend.wait", "ending.choose", "ending.low", "ending.normal", "ending.good", "ending.companion" }.Contains(node, StringComparer.Ordinal)) return true;
            if (Enumerable.Range(1, 5).Any(n => node == "stage." + n || node == "stage." + n + ".after")) return true;
            if (Enumerable.Range(1, 7).Any(n => node == "companion." + n || node == "companion." + n + ".after")) return true;
            return new[] { "ending.low.closed.destroy", "ending.normal.closed.comply", "ending.normal.closed.decline", "ending.normal.closed.analyze",
                "ending.good.closed.play", "ending.good.closed.joke", "ending.companion.closed.together" }.Contains(node, StringComparer.Ordinal);
        }
    }
}
