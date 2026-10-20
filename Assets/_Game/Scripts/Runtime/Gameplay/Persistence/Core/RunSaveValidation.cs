using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using CallmeCatgirl.Gameplay.TaskGrid;

namespace CallmeCatgirl.Persistence
{
    public sealed class SaveCompatibilityException : Exception
    { public SaveCompatibilityException(string message) : base(message) { } }

    public static class RunSaveValidation
    {
        public const int CurrentSchema = 1;
        public static void Validate(RunSaveData d, string contentRevision, IEnumerable<IRunSaveExtension> extensions, string legacyStoryV1Revision = null,
            IEnumerable<string> compatiblePresentationRevisions = null)
        {
            StateRules.Require(d != null, "Missing run payload.");
            if (d.SchemaVersion != CurrentSchema) throw new SaveCompatibilityException("Unsupported run schema: " + d.SchemaVersion);
            bool legacyRevision = d.Story?.Version == 1 && !string.IsNullOrEmpty(legacyStoryV1Revision) && d.ContentRevision == legacyStoryV1Revision;
            bool presentationOnlyMigration = d.Host?.Version <= 2
                && (compatiblePresentationRevisions ?? Array.Empty<string>()).Contains(d.ContentRevision, StringComparer.Ordinal);
            if (d.ContentRevision != contentRevision && !legacyRevision && !presentationOnlyMigration) throw new SaveCompatibilityException("Save content revision differs from installed content.");
            if (d.Host?.Version > 3 || d.Economy?.Version > 1 || d.Story?.Version > 5 || d.Simulation?.Version > 1
                || d.Policies?.Version > 2 || d.Host?.GenerationRandom?.Version > 1 || d.Simulation?.ExecutionProfileVersion > 2)
                throw new SaveCompatibilityException("Unsupported component state version.");
            StateRules.Require(Guid.TryParse(d.RunId, out _) && DateTimeOffset.TryParse(d.SavedAtUtc, out _)
                && !string.IsNullOrWhiteSpace(d.ContentRevision) && d.Host != null && d.Economy != null && d.Story != null
                && d.Policies != null && (d.Policies.Version == 1 || d.Policies.Version == 2) && d.Policies.UnlockedIds != null && d.Extensions != null, "Incomplete run payload.");
            var nonCustomerIds = ValidateExtensions(d.Extensions, extensions);
            var s = TaskSimulation.RestoreState(d.Simulation); // The production simulator validates its own state.
            StateRules.Require(nonCustomerIds.All(s.Tasks.ContainsKey), "Extension refers to a missing non-customer task.");
            var customerTasks = s.Tasks.Values.Where(t => !nonCustomerIds.Contains(t.Id)).ToArray();
            var operating = d.Host.Version >= 2 ? Ids(d.Host.OperatingRequestIds) : new HashSet<string>(StringComparer.Ordinal);
            StateRules.Require(operating.All(id => s.Tasks.ContainsKey(id) && !nonCustomerIds.Contains(id)), "Invalid operating task role.");
            var fillerIds = new HashSet<string>(s.Tasks.Values.Where(t => t.IsFiller).Select(t => t.Id), StringComparer.Ordinal);
            var storyTasks = customerTasks.Where(t => !operating.Contains(t.Id) && !fillerIds.Contains(t.Id)).ToArray();
            var h = d.Host; var e = d.Economy; var story = d.Story;
            StateRules.Require((h.Version == 1 || h.Version == 2 || h.Version == 3) && h.CurrentMonth >= 1 && StateRules.Finite(h.MonthElapsed) && h.MonthElapsed >= 0
                && StateRules.Finite(h.SecondsPerMonth) && h.SecondsPerMonth > 0 && h.MonthElapsed < h.SecondsPerMonth
                && h.MonthlyMaintenanceFee >= 0 && StateRules.Finite(h.PlaybackSpeed) && h.PlaybackSpeed > 0
                && h.MemoryLevel >= (h.Version >= 3 ? 0 : 1) && h.MemoryLevel <= 5 && h.CompletedTasks >= 0 && h.AvailableAbilityPoints >= 0 && h.RequestSerial >= 0
                && h.TaskBases != null && h.GenerationRandom != null && h.GenerationRandom.Version == 1
                && !string.IsNullOrWhiteSpace(h.GenerationRandom.AlgorithmId) && !string.IsNullOrWhiteSpace(h.GenerationRandom.StateJson)
                && (string.IsNullOrEmpty(h.SelectedRequestId) || s.Tasks.ContainsKey(h.SelectedRequestId)), "Invalid host clock/memory/selection/random state.");
            if (h.FillerCounts != null)
                StateRules.Require(h.FillerCounts.Length == 5 && h.FillerCounts.All(count => count >= 0), "Invalid filler counters.");
            else
                StateRules.Require(fillerIds.Count == 0, "Filler tasks lack generated counters.");
            foreach (string fillerId in fillerIds)
            {
                int fillerStage, fillerSerial;
                StateRules.Require(VisitorQueuePolicy.TryParseRequestId(fillerId, out fillerStage, out fillerSerial)
                    && h.FillerCounts[fillerStage - 1] >= fillerSerial, "Filler counter is behind an existing visitor.");
            }
            StateRules.Require(e.Version == 1 && e.Funds >= 0 && e.TokenPricePerUnit >= 0 && e.Debt >= 0
                && (e.GracePeriod || (e.Debt == 0 && !e.Bankrupt)), "Invalid wallet state.");
            ValidateEndState(h, e, s.Paused);
            var settled = Ids(e.SettledRequestIds);
            StateRules.Require(settled.SetEquals(customerTasks.Where(t => t.State == RequestState.Submitted || t.State == RequestState.TimedOut || t.State == RequestState.Closed).Select(t => t.Id)), "Wallet settlement ledger differs from terminal customer tasks.");
            StateRules.Require(h.CompletedTasks == customerTasks.Count(t => (t.State == RequestState.Submitted || t.State == RequestState.Closed)
                && t.Result != null && t.Result.Overall != Quality.BelowStandard), "Completed-task count differs from processed tasks.");
            var bases = new HashSet<string>(StringComparer.Ordinal);
            foreach (var b in h.TaskBases)
            {
                StateRules.Require(b != null && b.RequestId != null && s.Tasks.ContainsKey(b.RequestId) && !nonCustomerIds.Contains(b.RequestId) && bases.Add(b.RequestId) && b.Definition != null, "Invalid task-basis map.");
                b.Definition.Build();
            }
            StateRules.Require(bases.SetEquals(customerTasks.Select(t => t.Id)), "Missing unscaled customer task bases would multiply a future round twice.");
            StateRules.Require((story.Version == 1 || story.Version == 2 || story.Version == 3 || story.Version == 4 || story.Version == 5) && story.Users != null && story.VisibleChoices != null
                && (story.Version == 1 || (story.VisibleCorpusChoiceIds != null
                    && story.VisibleCorpusChoiceIds.Length == story.VisibleChoices.Length && story.OrdinaryConversations != null))
                && (story.Version < 5 || story.UsedPoolEntryIds != null) && story.Revision >= 0
                && story.Revision < int.MaxValue && (!story.IsReply || story.IsOpen), "Invalid story header.");
            if (story.Version >= 5)
            {
                Ids(story.UsedPoolEntryIds);
                if (!story.UsesStagePool && story.UsedPoolEntryIds.Length > 0)
                    throw new SaveCompatibilityException("Legacy story cannot carry stage pool usage.");
            }
            StateRules.Require(Ids(story.ArrivedRequestIds).SetEquals(storyTasks.Select(t => t.Id)), "Arrived ledger differs from customer task state.");
            var submitted = Ids(story.SubmittedRequestIds);
            StateRules.Require(submitted.SetEquals(storyTasks.Where(t => t.Result != null
                && (t.State == RequestState.Submitted || t.State == RequestState.Closed)).Select(t => t.Id)), "Story submission ledger differs from task state.");
            var pending = Ids(story.PendingRequestIds);
            var awaiting = storyTasks.Where(t => t.State == RequestState.Submitted).Select(t => t.Id).ToHashSetOrdinal();
            if (story.IsReply)
            {
                StateRules.Require(story.RequestId != null && awaiting.Remove(story.RequestId), "Missing active reply task.");
                StateRules.Require(s.Tasks[story.RequestId].ConversationId == story.UserId, "Active reply belongs to a different user.");
            }
            // Submitted feedback can remain on a customer task after all replies were accepted, until NextRound.
            // The story's actual queue + active prompt are the authority; do not reopen completed feedback on load.
            StateRules.Require(pending.IsSubsetOf(awaiting) && (story.IsReply || pending.Count == 0), "Invalid pending reply queue.");
            var users = new HashSet<string>(StringComparer.Ordinal);
            foreach (var u in story.Users)
                StateRules.Require(u != null && !string.IsNullOrWhiteSpace(u.UserId) && users.Add(u.UserId)
                    && u.History != null && u.History.All(x => x != null), "Invalid story user/history.");
            StateRules.Require(storyTasks.All(t => users.Contains(t.ConversationId)), "Customer task has no story state.");
            if (story.Version >= 4)
                StateRules.Require(Ids(story.SettledRequestIds).SetEquals(storyTasks.Where(t =>
                    t.State == RequestState.Submitted || t.State == RequestState.TimedOut || t.State == RequestState.Closed).Select(t => t.Id)),
                    "Affinity settlement ledger differs from role tasks.");
            Ids(d.Policies.UnlockedIds);
        }

        public static string ResolveEndReason(HostState host)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            return host.Version < 3 ? (host.GameEnded ? RunEndReasons.Bankruptcy : string.Empty)
                : host.EndReason ?? string.Empty;
        }

        /// <summary>Validate authored endings without manufacturing a bankrupt wallet.</summary>
        public static void ValidateEndState(HostState host, EconomyState economy, bool simulationPaused)
        {
            StateRules.Require(host != null && economy != null && host.Version >= 1 && host.Version <= 3,
                "Invalid ending header.");
            string reason = ResolveEndReason(host);
            if (!host.GameEnded)
            {
                StateRules.Require(!economy.Bankrupt && string.IsNullOrEmpty(reason), "Unfinished run has an ending or bankrupt wallet.");
                return;
            }
            StateRules.Require(simulationPaused && !string.IsNullOrWhiteSpace(host.EndingText), "Finished run must be paused with an ending.");
            StateRules.Require(reason == RunEndReasons.Bankruptcy || RunEndReasons.IsNarrative(reason), "Unknown ending reason.");
            StateRules.Require(economy.Bankrupt == (reason == RunEndReasons.Bankruptcy), "Ending reason differs from wallet bankruptcy.");
        }
        // Story v1 fingerprint predates this one newly appended, flat configuration field.
        // Remove only it; all existing story/configuration values keep their original fingerprint.
        public static string LegacyStoryV1ConfigurationJson(string json)
            => Regex.Replace(json, @",\s*""OrdinaryLifecycle""\s*:\s*(\{[^{}]*\}|null)", string.Empty);

        private static HashSet<string> ValidateExtensions(SaveExtensionState[] states, IEnumerable<IRunSaveExtension> extensions)
        {
            var handlers = (extensions ?? Array.Empty<IRunSaveExtension>()).ToDictionary(x => x.Id, StringComparer.Ordinal);
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var nonCustomerIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var extension in states)
            {
                StateRules.Require(extension != null && !string.IsNullOrWhiteSpace(extension.Id) && keys.Add(extension.Id)
                    && extension.Version > 0 && extension.PayloadJson != null, "Invalid/duplicate save extension.");
                if (handlers.TryGetValue(extension.Id, out var handler))
                {
                    if (handler.Required != extension.Required) throw new SaveCompatibilityException("Extension requirement differs from installed module: " + extension.Id);
                    handler.Validate(extension);
                    if (handler is IRunTaskRoleProvider roles)
                        foreach (var id in Ids(roles.NonCustomerRequestIds(extension)))
                            StateRules.Require(nonCustomerIds.Add(id), "Two extensions claim the same non-customer task.");
                }
                else if (extension.Required) throw new SaveCompatibilityException("Required save extension is not installed: " + extension.Id);
            }
            foreach (var handler in handlers.Values)
                if (handler.Required && !keys.Contains(handler.Id)) throw new SaveCompatibilityException("Save lacks required extension: " + handler.Id);
            return nonCustomerIds;
        }
        private static HashSet<string> Ids(string[] values)
        {
            StateRules.Require(values != null && values.All(x => !string.IsNullOrWhiteSpace(x)), "Missing/empty ID ledger.");
            var set = new HashSet<string>(values, StringComparer.Ordinal);
            StateRules.Require(set.Count == values.Length, "Duplicate ID ledger entry."); return set;
        }
        private static HashSet<string> ToHashSetOrdinal(this IEnumerable<string> values) => new HashSet<string>(values, StringComparer.Ordinal);
    }
}
