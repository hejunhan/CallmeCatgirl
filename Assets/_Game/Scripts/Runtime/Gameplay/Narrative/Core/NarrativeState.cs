using System;
using System.Collections.Generic;

namespace CallmeCatgirl.Gameplay.Narrative
{
    // New tutorial steps are appended so every existing serialized value keeps its meaning.
    public enum TutorialStep { Opening, AwaitTraining, AwaitDoctorOrder, AwaitOrdinaryOrder, AwaitUpgradeFunds, Complete, Legacy, AwaitDoctorSecond, AwaitDoctorThird }
    public enum NarrativeEndingReason { None, LowReputation, OrdinaryReputation, HighReputation, Companion }
    public enum NarrativeCorpusDomain { Writing, Reasoning }
    public enum NarrativeEffectKind
    {
        GrantCorpus, RevealCenterMemory, QueueDoctorRequest, QueueOrdinaryRequest,
        ShowUpgradeHint, IntroduceArchitecture, QueueStageDoctorRequest, EndRun, GrantFunds
    }

    [Serializable]
    public sealed class NarrativeEffect
    {
        public string EventId;
        public NarrativeEffectKind Kind;
        public string RequestId;
        public string ConversationId;
        public string Title;
        public string Description;
        public NarrativeCorpusDomain CorpusDomain;
        public double CorpusPoints;
        public int RequiredCells;
        // Explicit tutorial request rules. Zero keeps the legacy model-derived threshold.
        public double Calculation;
        public double MinimumAbility;
        public double ExcellentAbility;
        public double RewardTokens;
        public int FundsAmount;
        public int MemoryCount;
        public string ArchitectureId;
        public NarrativeEndingReason EndingReason;
        public NarrativeEffect Copy() => (NarrativeEffect)MemberwiseClone();
    }

    [Serializable]
    public sealed class NarrativeChoiceMemory
    {
        public string Key;
        public string Value;
    }

    [Serializable]
    public sealed class NarrativeSaveData
    {
        public int Version = 1;
        public TutorialStep Tutorial;
        public int MemoryCount;
        public bool CenterCellRevealed;
        public int CompanionBeat;
        public bool CompanionUnlocked;
        public bool CompanionMeetingReady;
        public bool RomanceReady;
        public bool CompanionDeclined;
        public bool EndingRequested;
        public NarrativeEndingReason PolicyEnding;
        public NarrativeEndingReason EndingReason;
        public bool EndingClosed;
        public bool FalseMemoryErased;
        public string CurrentNode;
        public bool ConversationVisible;
        public string Notice;
        public string LastTrainingRequestId;
        public string[] PendingStageNodes = Array.Empty<string>();
        public string[] AcknowledgedEventIds = Array.Empty<string>();
        public string[] ObservedCompletedRequestIds = Array.Empty<string>();
        public NarrativeEffect[] PendingEffects = Array.Empty<NarrativeEffect>();
        public NarrativeChoiceMemory[] Memories = Array.Empty<NarrativeChoiceMemory>();
    }

    public sealed class NarrativeChoice
    {
        public string Id { get; }
        public string Text { get; }
        public NarrativeChoice(string id, string text) { Id = id; Text = text; }
    }

    public sealed class NarrativeFrame
    {
        public string Id { get; }
        public string Speaker { get; }
        public string Title { get; }
        public string Body { get; }
        public IReadOnlyList<NarrativeChoice> Choices { get; }
        public bool CanClose { get; }
        public NarrativeFrame(string id, string speaker, string title, string body, bool canClose, params NarrativeChoice[] choices)
        {
            Id = id; Speaker = speaker; Title = title; Body = body; CanClose = canClose;
            Choices = Array.AsReadOnly(choices ?? Array.Empty<NarrativeChoice>());
        }
    }

    public sealed class NarrativeContact
    {
        public string Id { get; }
        public string Name { get; }
        public string Summary { get; }
        public bool HasUnread { get; }
        public NarrativeContact(string id, string name, string summary, bool unread)
        { Id = id; Name = name; Summary = summary; HasUnread = unread; }
    }
}
