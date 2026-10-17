#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CATGIRL_DISABLE_DEBUG
using System;
using System.Collections.Generic;
using System.Linq;

namespace CallmeCatgirl.Gameplay.TaskGrid
{
    // Copy beside TaskSimulation.cs only after applying Lifecycle.patch.
    public sealed partial class TaskSimulation
    {
        private readonly Dictionary<string, Quality> debugQualityOverrides = new Dictionary<string, Quality>(StringComparer.Ordinal);
        public Quality? GetDebugQualityOverride(string requestId) => requestId != null && debugQualityOverrides.TryGetValue(requestId, out var value) ? value : (Quality?)null;

        public bool TryDebugSetQualityOverride(string requestId, Quality? quality)
        {
            if (requestId == null || !tasks.TryGetValue(requestId, out var task)
                || (quality.HasValue && !Enum.IsDefined(typeof(Quality), quality.Value))
                || (task.State != RequestState.Waiting && task.State != RequestState.Withdrawn
                    && task.State != RequestState.Running && task.State != RequestState.Ready)) return false;
            if (quality.HasValue) debugQualityOverrides[requestId] = quality.Value;
            else debugQualityOverrides.Remove(requestId);
            if (task.State == RequestState.Ready) task.Result = DebugResult(task);
            return true;
        }

        private QualityResult DebugResult(TaskInstance task)
        {
            var natural = QualityResult.Evaluate(task.Definition, task.Abilities);
            return debugQualityOverrides.TryGetValue(task.Id, out var quality) ? QualityResult.WithDebugOverride(natural, quality) : natural;
        }

        /// <summary>Finish only this request at the current game time. Keep earned abilities; no artificial ability ticks.
        /// Ready modules keep their old pose; unplaced requests become Ready in the candidate list.</summary>
        public bool TryDebugFinishCalculation(string requestId)
        {
            if (requestId == null || !tasks.TryGetValue(requestId, out var task)
                || (task.State != RequestState.Waiting && task.State != RequestState.Withdrawn && task.State != RequestState.Running)) return false;
            task.ReadyInCandidate = task.State != RequestState.Running;
            MarkReady(task, GameTime);
            return true;
        }
    }
}
#endif
