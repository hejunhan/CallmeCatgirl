using System;
using System.Collections.Generic;
using System.Linq;

namespace CallmeCatgirl.Gameplay.TaskGrid
{
    /// <summary>Pure arrival arithmetic for lightweight visitors; story requests keep their own six slots.</summary>
    public static class VisitorQueuePolicy
    {
        public const int StoryRequestSlots = 6;
        public const string ConversationId = "filler";

        public static int UnfinishedCount(IEnumerable<TaskInstance> tasks)
            => tasks == null ? 0 : tasks.Count(task => task != null
                && (task.State == RequestState.Waiting || task.State == RequestState.Running || task.State == RequestState.Ready));

        public static int ArrivalCount(int stage, int targetConcurrent, int targetRequests, int generated, int unfinished)
        {
            if (stage <= 1 || targetConcurrent < 1 || targetRequests < 0) return 0;
            int remaining = targetRequests - StoryRequestSlots - generated;
            if (remaining <= 0) return 0;
            int deficit = targetConcurrent - unfinished;
            return deficit <= 0 ? 0 : Math.Min(deficit, remaining);
        }

        public static string RequestId(int stage, int serial) => ConversationId + "-" + stage + "-" + serial;

        /// <summary>Saved per-stage counters never fall behind a surviving filler task after restore.</summary>
        public static int[] MergeCounters(int[] saved, IEnumerable<TaskInstance> tasks)
        {
            var counts = new int[5];
            if (saved != null)
                for (int i = 0; i < counts.Length && i < saved.Length; i++) counts[i] = Math.Max(0, saved[i]);
            foreach (var task in tasks ?? Array.Empty<TaskInstance>())
            {
                int stage, serial;
                if (task != null && task.IsFiller && TryParseRequestId(task.Id, out stage, out serial))
                    counts[stage - 1] = Math.Max(counts[stage - 1], serial);
            }
            return counts;
        }

        public static bool TryParseRequestId(string requestId, out int stage, out int serial)
        {
            stage = 0; serial = 0;
            if (string.IsNullOrEmpty(requestId) || !requestId.StartsWith(ConversationId + "-", StringComparison.Ordinal)) return false;
            var parts = requestId.Substring(ConversationId.Length + 1).Split('-');
            return parts.Length == 2 && int.TryParse(parts[0], out stage) && int.TryParse(parts[1], out serial)
                && stage >= 1 && stage <= 5 && serial >= 1;
        }
    }
}
