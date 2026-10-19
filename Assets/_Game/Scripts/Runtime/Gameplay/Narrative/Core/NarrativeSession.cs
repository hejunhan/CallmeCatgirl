using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CallmeCatgirl.Gameplay.Narrative
{
    /// <summary>Dialogue state only. Every game-world mutation is a retryable, acknowledged host effect.</summary>
    public sealed partial class NarrativeSession
    {
        public const string DoctorId = "narrative-doctor";
        public const string CompanionId = "narrative-lin";
        public const string StageContactId = "narrative-old-friend";
        public const string FirstDoctorRequestId = "narrative-doctor-first";
        public const string FirstOrdinaryRequestId = "narrative-ordinary-first";
        private NarrativeSaveData state;
        public event Action Changed;
        public int Revision { get; private set; }
        public TutorialStep Tutorial => state.Tutorial;
        public int MemoryCount => state.MemoryCount;
        public bool CenterCellRevealed => state.CenterCellRevealed;
        public int CompanionBeat => state.CompanionBeat;
        public bool CompanionMeetingReady => state.CompanionMeetingReady;
        public bool RomanceReady => state.RomanceReady;
        public bool EndingClosed => state.EndingClosed;
        public bool EndingRequested => state.EndingRequested;
        public NarrativeEndingReason EndingReason => state.EndingReason;
        public string Notice => state.Notice;
        public bool RequiresPause => state.ConversationVisible && !state.EndingClosed;
        public bool OrdinaryRequestsUnlocked => state.Tutorial >= TutorialStep.AwaitOrdinaryOrder;
        public IReadOnlyList<NarrativeEffect> PendingEffects => Array.AsReadOnly(state.PendingEffects.Select(e => e.Copy()).ToArray());
        public NarrativeFrame CurrentFrame => state.ConversationVisible ? BuildFrame(state.CurrentNode) : null;
        public IReadOnlyList<NarrativeContact> PinnedContacts
        {
            get
            {
                var contacts = new List<NarrativeContact> { new NarrativeContact(DoctorId, T("session.contact.doctor.name"), TutorialSummary(),
                    state.Tutorial == TutorialStep.Opening || state.PendingStageNodes.Any(n => StageNumber(n) <= 2)) };
                if (state.PendingStageNodes.Any(n => StageNumber(n) >= 3) || state.MemoryCount >= 3)
                    contacts.Add(new NarrativeContact(StageContactId, T("session.contact.old-friend.name"), T("session.contact.old-friend.summary"), state.PendingStageNodes.Any(n => StageNumber(n) >= 3)));
                if (state.CompanionUnlocked)
                    contacts.Add(new NarrativeContact(CompanionId, T("session.contact.companion.name"), state.CompanionMeetingReady ? T("session.contact.companion.ready") : state.CompanionBeat <= 7 ? T("session.contact.companion.working") : T("session.contact.companion.waiting"), state.CompanionMeetingReady));
                return contacts.AsReadOnly();
            }
        }

        private NarrativeSession(NarrativeSaveData data) { state = data; }
        public static NarrativeSession CreateNew() => new NarrativeSession(new NarrativeSaveData
        { Tutorial = TutorialStep.Opening, CurrentNode = "doctor.opening", ConversationVisible = true });
        public static NarrativeSession AdoptLegacy(int completedMemoryCount)
        {
            CheckMemory(completedMemoryCount);
            return new NarrativeSession(new NarrativeSaveData
            { Tutorial = TutorialStep.Legacy, MemoryCount = completedMemoryCount, CenterCellRevealed = true, CurrentNode = "doctor.legacy" });
        }
        public NarrativeSaveData Capture() => Copy(state);
        public static NarrativeSession Restore(NarrativeSaveData data)
        {
            if (!Validate(data, out var error)) throw new ArgumentException(error, nameof(data));
            return new NarrativeSession(Copy(data));
        }
        public bool AcknowledgeEffect(string eventId)
        {
            if (string.IsNullOrEmpty(eventId) || !state.PendingEffects.Any(e => e.EventId == eventId)) return false;
            state.PendingEffects = state.PendingEffects.Where(e => e.EventId != eventId).ToArray();
            state.AcknowledgedEventIds = state.AcknowledgedEventIds.Concat(new[] { eventId }).ToArray();
            Publish(); return true;
        }
        public bool ObserveMemoryCount(int completedMemoryCount)
        {
            CheckMemory(completedMemoryCount);
            if (completedMemoryCount <= state.MemoryCount || state.EndingRequested || state.Tutorial == TutorialStep.Opening) return false;
            for (int stage = state.MemoryCount + 1; stage <= completedMemoryCount; stage++)
            {
                state.PendingStageNodes = state.PendingStageNodes.Concat(new[] { "stage." + stage }).ToArray();
                if (stage == 1 || stage == 2 || stage == 4)
                    Enqueue(new NarrativeEffect { EventId = "narrative-architecture-" + stage,
                        Kind = NarrativeEffectKind.IntroduceArchitecture, MemoryCount = stage,
                        ArchitectureId = stage == 1 ? "baseline" : stage == 2 ? "alternate" : "architecture-slot-3",
                        Title = stage == 1 ? T("session.architecture.transformer.title") : stage == 2 ? T("session.architecture.moe.title") : T("session.architecture.coordinated.title"),
                        Description = stage == 1 ? T("session.architecture.transformer.description") : stage == 2 ? T("session.architecture.moe.description") : T("session.architecture.coordinated.description") });
            }
            state.MemoryCount = completedMemoryCount; Publish(); return true;
        }
        public bool ObserveCenterCellRevealed()
        {
            if (state.CenterCellRevealed || state.Tutorial == TutorialStep.Opening) return false;
            state.CenterCellRevealed = true; Publish(); return true;
        }
        public bool ObserveTrainingCompleted(string requestId)
        {
            if (state.Tutorial != TutorialStep.AwaitTraining || !state.CenterCellRevealed || string.IsNullOrWhiteSpace(requestId) || state.EndingRequested) return false;
            state.LastTrainingRequestId = requestId;
            state.Tutorial = TutorialStep.AwaitDoctorOrder;
            Enqueue(RequestEffect("narrative-queue-doctor-first", NarrativeEffectKind.QueueDoctorRequest,
                FirstDoctorRequestId, DoctorId, T("session.request.doctor-first.title"), T("session.request.doctor-first.description"), 1));
            Show("doctor.first-order"); Publish(); return true;
        }
        public bool ObserveRequestSettled(string requestId)
        {
            if (string.IsNullOrWhiteSpace(requestId) || requestId == state.LastTrainingRequestId || state.EndingRequested
                || state.ObservedCompletedRequestIds.Contains(requestId, StringComparer.Ordinal)) return false;
            bool tutorial = state.Tutorial == TutorialStep.AwaitDoctorOrder && requestId == FirstDoctorRequestId
                || state.Tutorial == TutorialStep.AwaitOrdinaryOrder && requestId == FirstOrdinaryRequestId;
            if (!tutorial && (requestId == FirstDoctorRequestId || requestId == FirstOrdinaryRequestId)) return false;
            bool meeting = state.CompanionUnlocked && !state.CompanionMeetingReady && state.CompanionBeat >= 2 && state.CompanionBeat <= 7
                && (state.CurrentNode == "companion.wait" || !(state.CurrentNode ?? "").StartsWith("companion.", StringComparison.Ordinal));
            // The host may report other genuine settlements; record them to make later polling/reloading harmless.
            state.ObservedCompletedRequestIds = state.ObservedCompletedRequestIds.Concat(new[] { requestId }).ToArray();
            if (state.Tutorial == TutorialStep.AwaitDoctorOrder && requestId == FirstDoctorRequestId)
            {
                state.Tutorial = TutorialStep.AwaitOrdinaryOrder;
                Enqueue(RequestEffect("narrative-queue-ordinary-first", NarrativeEffectKind.QueueOrdinaryRequest,
                    FirstOrdinaryRequestId, "narrative-first-visitor", T("session.request.ordinary-first.title"), T("session.request.ordinary-first.description"), 1));
                Show("doctor.first-visitor");
            }
            else if (state.Tutorial == TutorialStep.AwaitOrdinaryOrder && requestId == FirstOrdinaryRequestId)
            { state.Tutorial = TutorialStep.AwaitUpgradeFunds; Show("doctor.first-income"); }
            if (meeting) state.CompanionMeetingReady = true;
            Publish(); return tutorial || meeting;
        }
        public bool ObserveUpgradeFunds(int funds, int upgradeCost)
        {
            if (funds < 0 || upgradeCost <= 0 || state.Tutorial != TutorialStep.AwaitUpgradeFunds || funds < upgradeCost || state.EndingRequested) return false;
            state.Tutorial = TutorialStep.Complete;
            Enqueue(new NarrativeEffect { EventId = "narrative-first-upgrade-hint", Kind = NarrativeEffectKind.ShowUpgradeHint,
                Title = T("session.upgrade-hint.title"), Description = T("session.upgrade-hint.description") });
            Show("doctor.upgrade"); Publish(); return true;
        }
        public bool UnlockCompanionOpportunity()
        {
            if (state.CompanionUnlocked || state.EndingRequested) return false;
            state.CompanionUnlocked = true; state.CompanionBeat = 1; state.CompanionMeetingReady = true;
            Publish(); return true;
        }
        public bool OpenContact(string contactId)
        {
            if (state.EndingRequested) return false;
            if (CurrentFrame != null && !CurrentFrame.CanClose) return false;
            if (contactId == CompanionId)
            {
                if (!state.CompanionUnlocked) return false;
                if ((state.CurrentNode ?? "").StartsWith("companion.", StringComparison.Ordinal) && state.CurrentNode != "companion.wait") { state.ConversationVisible = true; Publish(); return true; }
                Show(state.CompanionMeetingReady && state.CompanionBeat <= 7 ? "companion." + state.CompanionBeat : "companion.wait");
            }
            else if (contactId == DoctorId || contactId == StageContactId)
            {
                string next = state.PendingStageNodes.FirstOrDefault(n => contactId == DoctorId ? StageNumber(n) <= 2 : StageNumber(n) >= 3);
                if (state.Tutorial != TutorialStep.Complete && state.Tutorial != TutorialStep.Legacy && contactId == DoctorId)
                    Show(TutorialNode());
                else if (next != null) Show(next);
                else if (contactId == DoctorId) Show("doctor.legacy");
                else if (state.MemoryCount >= 3) Show("friend.wait");
                else return false;
            }
            else return false;
            Publish(); return true;
        }
        public bool CloseConversation()
        {
            var frame = CurrentFrame;
            if (frame == null || !frame.CanClose || !string.IsNullOrEmpty(state.Notice)) return false;
            state.ConversationVisible = false; Publish(); return true;
        }
        public bool DismissNotice()
        {
            if (string.IsNullOrEmpty(state.Notice)) return false;
            state.Notice = null; Publish(); return true;
        }
        public bool RequestPolicyEnding(int reputation, int lowThreshold, int highThreshold)
        {
            if (lowThreshold >= highThreshold) throw new ArgumentException("Reputation thresholds must strictly increase.");
            if (state.EndingRequested) return false;
            state.EndingRequested = true;
            state.PolicyEnding = reputation <= lowThreshold ? NarrativeEndingReason.LowReputation
                : reputation < highThreshold ? NarrativeEndingReason.OrdinaryReputation : NarrativeEndingReason.HighReputation;
            if (state.RomanceReady) Show("ending.choose");
            else BeginEnding(state.PolicyEnding);
            Publish(); return true;
        }
        public bool Choose(string choiceId)
        {
            var frame = CurrentFrame;
            if (frame == null || state.EndingClosed || !string.IsNullOrEmpty(state.Notice)
                || !frame.Choices.Any(c => c.Id == choiceId)) return false;
            string node = state.CurrentNode;
            if (node == "doctor.opening")
            {
                Remember("opening", choiceId); state.Tutorial = TutorialStep.AwaitTraining;
                Enqueue(new NarrativeEffect { EventId = "narrative-opening-corpus", Kind = NarrativeEffectKind.GrantCorpus,
                    ConversationId = DoctorId, CorpusDomain = choiceId == "mew" ? NarrativeCorpusDomain.Writing : NarrativeCorpusDomain.Reasoning,
                    CorpusPoints = 1, Title = T("session.corpus.first.title"), Description = choiceId == "mew" ? T("session.corpus.first.mew") : T("session.corpus.first.accept") });
                Enqueue(new NarrativeEffect { EventId = "narrative-reveal-center", Kind = NarrativeEffectKind.RevealCenterMemory,
                    RequiredCells = 1, MemoryCount = 0, Title = T("session.center-cell.title"), Description = T("session.center-cell.description") });
                Show("doctor.train");
            }
            else if (node.StartsWith("stage.", StringComparison.Ordinal) && !node.EndsWith(".after", StringComparison.Ordinal))
            {
                int stage = StageNumber(node); Remember(node, choiceId);
                state.PendingStageNodes = state.PendingStageNodes.Where(n => n != node).ToArray();
                if (stage >= 2) Enqueue(RequestEffect("narrative-stage-doctor-" + stage, NarrativeEffectKind.QueueStageDoctorRequest,
                    "narrative-doctor-stage-" + stage, DoctorId, StageTaskTitle(stage), StageDoctorText(stage), Math.Min(stage, 3)));
                Show(node + ".after");
            }
            else if (node.StartsWith("companion.", StringComparison.Ordinal) && int.TryParse(node.Substring(10), out int beat))
            {
                Remember("companion." + beat, choiceId);
                if (beat == 1) Remember("lin.habit", choiceId == "first" ? "screwdriver" : "sunrise");
                if (beat == 3) Remember("lin.promise", choiceId == "first" ? "sunset" : "person");
                if (beat == 4) Remember("lin.boundary", choiceId == "first" ? "ask-first" : "offer-plan");
                if (beat == 5) Remember("lin.respect", "yes");
                if (beat == 7)
                { state.RomanceReady = choiceId == "accept-invitation"; state.CompanionDeclined = choiceId == "friends"; }
                state.CompanionMeetingReady = false;
                Show(node + ".after");
            }
            else if (node.StartsWith("companion.", StringComparison.Ordinal) && node.EndsWith(".after", StringComparison.Ordinal))
            {
                state.CompanionBeat = int.Parse(node.Split('.')[1], CultureInfo.InvariantCulture) + 1;
                state.CurrentNode = "companion.wait"; state.ConversationVisible = false;
            }
            else if (node == "ending.choose") BeginEnding(choiceId == "leave" ? NarrativeEndingReason.Companion : state.PolicyEnding);
            else if (node == "ending.low" && choiceId == "remember")
            { state.FalseMemoryErased = true; state.Notice = T("session.false-memory.notice"); }
            else if (node.StartsWith("ending.", StringComparison.Ordinal) && choiceId == "curtain")
            {
                state.EndingClosed = true; state.ConversationVisible = false;
                Enqueue(new NarrativeEffect { EventId = "narrative-end-run", Kind = NarrativeEffectKind.EndRun,
                    EndingReason = state.EndingReason, Title = EndingTitle(state.EndingReason), Description = T("session.ending.curtain") });
            }
            else if (node.StartsWith("ending.", StringComparison.Ordinal))
            { Remember(node, choiceId); Show(node + ".closed." + choiceId); }
            else
            { state.ConversationVisible = false; }
            Publish(); return true;
        }

        public string Remembered(string key) => state.Memories.FirstOrDefault(m => m.Key == key)?.Value;
        public static string HostEndReason(NarrativeEndingReason reason)
        {
            switch (reason)
            {
                case NarrativeEndingReason.LowReputation: return "policy.bad";
                case NarrativeEndingReason.OrdinaryReputation: return "policy.normal";
                case NarrativeEndingReason.HighReputation: return "policy.good";
                case NarrativeEndingReason.Companion: return "emotion";
                default: return null;
            }
        }
        private void BeginEnding(NarrativeEndingReason ending)
        {
            state.EndingReason = ending; state.Notice = null;
            Show(ending == NarrativeEndingReason.LowReputation ? "ending.low" : ending == NarrativeEndingReason.OrdinaryReputation
                ? "ending.normal" : ending == NarrativeEndingReason.HighReputation ? "ending.good" : "ending.companion");
        }
        private void Remember(string key, string value)
        {
            state.Memories = state.Memories.Where(m => m.Key != key)
                .Concat(new[] { new NarrativeChoiceMemory { Key = key, Value = value } }).ToArray();
        }
        private void Show(string node) { state.CurrentNode = node; state.ConversationVisible = true; }
        private void Publish() { Revision++; Changed?.Invoke(); }
        private void Enqueue(NarrativeEffect effect)
        {
            if (state.AcknowledgedEventIds.Contains(effect.EventId, StringComparer.Ordinal) || state.PendingEffects.Any(e => e.EventId == effect.EventId)) return;
            state.PendingEffects = state.PendingEffects.Concat(new[] { effect }).ToArray();
        }
        private static NarrativeEffect RequestEffect(string eventId, NarrativeEffectKind kind, string requestId, string conversationId, string title, string text, int cells)
            => new NarrativeEffect { EventId = eventId, Kind = kind, RequestId = requestId, ConversationId = conversationId,
                Title = title, Description = text, RequiredCells = cells };
        private static void CheckMemory(int count)
        { if (count < 0 || count > 5) throw new ArgumentOutOfRangeException(nameof(count), "Installed memory count must be within 0..5."); }
        private static int StageNumber(string node)
        { if (node == null || !node.StartsWith("stage.", StringComparison.Ordinal)) return 0; return int.TryParse(node.Split('.')[1], out int n) ? n : 0; }
        private string TutorialNode()
        {
            switch (state.Tutorial)
            {
                case TutorialStep.Opening: return "doctor.opening";
                case TutorialStep.AwaitTraining: return "doctor.train";
                case TutorialStep.AwaitDoctorOrder: return "doctor.first-order";
                case TutorialStep.AwaitOrdinaryOrder: return "doctor.first-visitor";
                case TutorialStep.AwaitUpgradeFunds: return "doctor.first-income";
                default: return "doctor.legacy";
            }
        }
        private string TutorialSummary()
        {
            switch (state.Tutorial)
            {
                case TutorialStep.Opening: return T("session.tutorial.opening");
                case TutorialStep.AwaitTraining: return T("session.tutorial.training");
                case TutorialStep.AwaitDoctorOrder: return T("session.tutorial.doctor-order");
                case TutorialStep.AwaitOrdinaryOrder: return T("session.tutorial.ordinary-order");
                case TutorialStep.AwaitUpgradeFunds: return T("session.tutorial.upgrade-funds");
                default: return T("session.tutorial.complete");
            }
        }
        private static NarrativeSaveData Copy(NarrativeSaveData s) => new NarrativeSaveData
        {
            Version = s.Version, Tutorial = s.Tutorial, MemoryCount = s.MemoryCount, CenterCellRevealed = s.CenterCellRevealed, CompanionBeat = s.CompanionBeat,
            CompanionUnlocked = s.CompanionUnlocked, CompanionMeetingReady = s.CompanionMeetingReady,
            RomanceReady = s.RomanceReady, CompanionDeclined = s.CompanionDeclined,
            EndingRequested = s.EndingRequested, PolicyEnding = s.PolicyEnding, EndingReason = s.EndingReason,
            EndingClosed = s.EndingClosed, FalseMemoryErased = s.FalseMemoryErased, CurrentNode = s.CurrentNode,
            ConversationVisible = s.ConversationVisible, Notice = s.Notice, LastTrainingRequestId = s.LastTrainingRequestId,
            PendingStageNodes = (string[])s.PendingStageNodes.Clone(), AcknowledgedEventIds = (string[])s.AcknowledgedEventIds.Clone(),
            ObservedCompletedRequestIds = (string[])s.ObservedCompletedRequestIds.Clone(),
            PendingEffects = s.PendingEffects.Select(e => e.Copy()).ToArray(),
            Memories = s.Memories.Select(m => new NarrativeChoiceMemory { Key = m.Key, Value = m.Value }).ToArray()
        };
    }
}
