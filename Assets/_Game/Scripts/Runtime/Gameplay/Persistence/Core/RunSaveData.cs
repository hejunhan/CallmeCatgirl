using System;
using CallmeCatgirl.Gameplay.TaskGrid;
using CallmeCatgirl.Gameplay.Progression;

namespace CallmeCatgirl.Persistence
{
    [Serializable] public sealed class EconomyState
    {
        public int Version;
        public int Funds;
        public int TokenPricePerUnit;
        public int Debt;
        public bool GracePeriod;
        public bool Bankrupt;
        public string[] SettledRequestIds;
    }
    [Serializable] public sealed class StoryUserState
    {
        public string UserId, NodeId, PoolEntryId;
        public int ConversationKind;
        public double Affinity;
        public string[] CompletedSessions;
        public string[] History;
    }
    [Serializable] public sealed class ReplyChoiceState
    {
        public int MinimumFunds;
        public int FundsReward;
        public string Label;
        public string PlayerReply;
        public string UserReply;
        public int Requirement;
        public string NextNodeId;
    }
    [Serializable] public sealed class StoryState
    {
        public int Version;
        public StoryUserState[] Users;
        public string[] ArrivedRequestIds;
        public string[] SubmittedRequestIds;
        public string[] SettledRequestIds;
        public string[] PendingRequestIds;
        public bool IsOpen;
        public bool IsReply;
        public string UserId;
        public string RequestId;
        public Quality Result;
        public int Revision;
        public ReplyChoiceState[] VisibleChoices;
        public string[] VisibleCorpusChoiceIds;
        public OrdinaryConversationLifecycleState OrdinaryConversations;
    }
    [Serializable] public sealed class TaskBasisState { public string RequestId; public DefinitionState Definition; }
    [Serializable] public sealed class RandomStreamState
    {
        public int Version;
        public string AlgorithmId;
        public int Seed;
        // Exact state owned by a serializable System.Random subclass; do not serialize private runtime fields.
        public string StateJson;
    }
    public static class RunEndReasons
    {
        public const string Bankruptcy = "bankruptcy";
        public const string PolicyBad = "policy.bad";
        public const string PolicyNormal = "policy.normal";
        public const string PolicyGood = "policy.good";
        public const string Emotion = "emotion";

        public static bool IsNarrative(string reason) => reason == PolicyBad || reason == PolicyNormal
            || reason == PolicyGood || reason == Emotion;
    }

    [Serializable] public sealed class HostState
    {
        public int Version;
        public int CurrentMonth;
        public double MonthElapsed;
        public double SecondsPerMonth;
        public int MonthlyMaintenanceFee;
        public float PlaybackSpeed;
        public int MemoryLevel;
        public int CompletedTasks;
        public int AvailableAbilityPoints;
        public int RequestSerial;
        public string SelectedRequestId;
        public bool GameEnded;
        public string EndingText;
        // Host v3 distinguishes an authored ending from the wallet's bankruptcy state.
        // In v1/v2 a finished run could only mean bankruptcy.
        public string EndReason;
        public bool MemoryUpgradeOpen;
        public TaskBasisState[] TaskBases;
        public string[] OperatingRequestIds;
        public RandomStreamState GenerationRandom;
    }
    [Serializable] public sealed class SaveExtensionState
    {
        public string Id;
        public int Version;
        public bool Required;
        public string PayloadJson;
    }
    [Serializable] public sealed class RunSaveData
    {
        public int SchemaVersion;
        public string RunId;
        public string ContentRevision;
        public string SavedAtUtc;
        public HostState Host;
        public EconomyState Economy;
        public TaskSimulationState Simulation;
        public ProgressionState Policies;
        public StoryState Story;
        // Training owns corpus inventory, jobs, trained attributes, compute upgrade level and architecture ID.
        // Its DTO lives in its own module. Token/s and attribute points are separate there.
        public SaveExtensionState[] Extensions;
    }

    public interface ISaveJsonCodec
    {
        string Encode<T>(T value);
        T Decode<T>(string json);
    }
    public interface IRunSaveExtension
    {
        string Id { get; }
        bool Required { get; }
        SaveExtensionState Capture();
        // Pure validation; host later builds detached state from this payload. Must reject unsupported versions.
        void Validate(SaveExtensionState state);
    }
    // Implement on a registered extension, using its own saved job IDs. No request-prefix heuristics.
    public interface IRunTaskRoleProvider
    {
        string[] NonCustomerRequestIds(SaveExtensionState state);
    }
    public interface IPreparedRunRestore : IDisposable
    {
        // Main-thread synchronous reference swap; all potentially failing work belongs in PrepareRestore.
        void Commit();
    }
    public interface IRunSaveHost
    {
        string ContentRevision { get; }
        // Capture on main thread at the owner's fully handled event boundary; never drain events here.
        RunSaveData CaptureRun();
        // Build detached TaskSimulation, Economy, Story, Tree and extensions; no live object mutation.
        IPreparedRunRestore PrepareRestore(RunSaveData data);
    }
}
