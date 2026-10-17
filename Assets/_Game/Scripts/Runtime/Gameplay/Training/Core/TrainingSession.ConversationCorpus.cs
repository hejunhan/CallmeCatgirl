using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CallmeCatgirl.Gameplay.TaskGrid;

namespace CallmeCatgirl.Gameplay.Training
{
    /// <summary>A read-only snapshot of one conversation's accumulated dialogue fragments.
    /// Scores are raw corpus scores, before any training recipe or policy multiplier.</summary>
    public sealed class ConversationCorpusSummary
    {
        public string ConversationId { get; }
        public IReadOnlyList<CorpusRecord> Records { get; }
        public IReadOnlyList<string> RequestIds { get; }
        public IReadOnlyList<string> AvailableCorpusIds { get; }
        public IReadOnlyDictionary<AbilityKind, double> TotalScores { get; }
        public IReadOnlyDictionary<AbilityKind, double> AvailableScores { get; }
        public int RecordCount => Records.Count;
        public int RequestCount => RequestIds.Count;
        public int AvailableRecordCount => AvailableCorpusIds.Count;
        public int ConsumedRecordCount => RecordCount - AvailableRecordCount;

        internal ConversationCorpusSummary(string conversationId, IEnumerable<CorpusRecord> source,
            Func<string, bool> isConsumed)
        {
            ConversationId = conversationId;
            var records = source.ToArray();
            var available = records.Where(r => !isConsumed(r.Id)).ToArray();
            Records = Array.AsReadOnly(records);
            RequestIds = Array.AsReadOnly(records.Select(r => r.RequestId).Distinct(StringComparer.Ordinal).ToArray());
            AvailableCorpusIds = Array.AsReadOnly(available.Select(r => r.Id).ToArray());
            TotalScores = SumScores(records);
            AvailableScores = SumScores(available);
        }

        private static IReadOnlyDictionary<AbilityKind, double> SumScores(IEnumerable<CorpusRecord> records)
        {
            var totals = new Dictionary<AbilityKind, double>();
            foreach (var record in records)
                foreach (var score in record.Scores)
                {
                    totals.TryGetValue(score.Key, out var before);
                    totals[score.Key] = before + score.Value;
                }
            return new ReadOnlyDictionary<AbilityKind, double>(totals);
        }
    }

    public sealed partial class TrainingSession
    {
        /// <summary>Derived from the existing records and consumption ledger; no second inventory or save schema.
        /// Includes consumed history. Conversations without corpus are queried with GetConversationCorpus instead.</summary>
        public IReadOnlyList<ConversationCorpusSummary> ConversationCorpora => Array.AsReadOnly(corpus.Values
            .GroupBy(r => r.ConversationId, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new ConversationCorpusSummary(g.Key, g, IsConsumed)).ToArray());

        /// <summary>Returns an empty snapshot for a known conversation with no accepted corpus yet.
        /// Keep the stable TaskInstance.ConversationId; never derive it from a request ID or display name.
        /// Requery after Changed. Accumulation still uses RecordDialogue, and training uses StartTraining.</summary>
        public ConversationCorpusSummary GetConversationCorpus(string conversationId)
        {
            Check.Id(conversationId);
            return new ConversationCorpusSummary(conversationId,
                corpus.Values.Where(r => string.Equals(r.ConversationId, conversationId, StringComparison.Ordinal)), IsConsumed);
        }
    }
}
