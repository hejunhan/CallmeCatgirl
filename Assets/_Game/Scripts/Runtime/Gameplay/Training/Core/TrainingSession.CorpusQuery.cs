using System;
using System.Collections.Generic;
using System.Linq;
using CallmeCatgirl.Gameplay.TaskGrid;

namespace CallmeCatgirl.Gameplay.Training
{
    public sealed partial class TrainingSession
    {
        /// <summary>Read-only, deterministic inventory query over the current corpus snapshots.
        /// The domain filter uses remaining scores, so unfinished jobs hide reserved points.
        /// Nothing is reserved, spent or mutated; callers requery after Changed.</summary>
        public IReadOnlyList<ConversationCorpusSummary> QueryConversationCorpora(
            AbilityKind? domain = null, string text = null,
            double minimumAvailablePoints = 0,
            Func<string,string> displayName = null)
        {
            if (double.IsNaN(minimumAvailablePoints) || double.IsInfinity(minimumAvailablePoints)
                || minimumAvailablePoints < 0)
                throw new ArgumentOutOfRangeException(nameof(minimumAvailablePoints), minimumAvailablePoints,
                    "Minimum available points must be finite and non-negative.");
            if (domain.HasValue && !Enum.IsDefined(typeof(AbilityKind), domain.Value))
                throw new ArgumentException("Unknown ability domain.", nameof(domain));

            string query = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
            var matches = new List<(ConversationCorpusSummary Summary, double Rank)>();
            foreach (var summary in ConversationCorpora)
            {
                if (!(summary.AvailablePoints > 1e-9)) continue;
                if (summary.AvailablePoints < minimumAvailablePoints) continue;

                double rank = summary.AvailablePoints;
                if (domain.HasValue
                    && (!summary.AvailableScores.TryGetValue(domain.Value, out rank) || !(rank > 1e-9))) continue;

                if (query != null)
                {
                    bool found = summary.ConversationId.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!found && displayName != null)
                    {
                        string name = displayName(summary.ConversationId);
                        found = !string.IsNullOrEmpty(name)
                            && name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
                    }
                    if (!found) continue;
                }
                matches.Add((summary, rank));
            }
            return Array.AsReadOnly(matches
                .OrderByDescending(m => m.Rank)
                .ThenBy(m => m.Summary.ConversationId, StringComparer.Ordinal)
                .Select(m => m.Summary)
                .ToArray());
        }
    }
}
