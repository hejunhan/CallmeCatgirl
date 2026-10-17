#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CATGIRL_DISABLE_DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using CallmeCatgirl.Gameplay.TaskGrid;

namespace CallmeCatgirl.Gameplay.Training
{
    // Task-12 is already partial. The older main-project TrainingSession needs a one-line partial declaration.
    public sealed partial class TrainingSession
    {
        /// <summary>Prevalidate the whole command, then use the normal completion path. No clock or event draining.</summary>
        public TrainingResult TryDebugCompleteTraining(string requestId)
        {
            if (busy) return TrainingResult.Busy;
            if (requestId == null || !jobs.TryGetValue(requestId, out var job)) return TrainingResult.InvalidInput;
            if (job.Completed) return TrainingResult.AlreadyCompleted;
            if (models.ContainsKey(job.Output.Id) || !simulation.Tasks.TryGetValue(requestId, out var task)) return TrainingResult.HostRejected;
            if (task.State != RequestState.Ready && task.State != RequestState.Waiting
                && task.State != RequestState.Withdrawn && task.State != RequestState.Running) return TrainingResult.NotReady;
            if (task.State != RequestState.Ready && !simulation.TryDebugFinishCalculation(requestId)) return TrainingResult.HostRejected;
            return CompleteTraining(requestId);
        }

        public bool TryDebugSetCorpusScore(string corpusId, AbilityKind ability, double value)
        {
            if (busy || corpusId == null || !Enum.IsDefined(typeof(AbilityKind), ability)
                || double.IsNaN(value) || double.IsInfinity(value) || value < 0
                || !corpus.TryGetValue(corpusId, out var record) || consumed.Contains(corpusId)) return false;
            var scores = record.Scores.ToDictionary(p => p.Key, p => p.Value);
            scores[ability] = value;
            if (!scores.Values.Any(v => v > 0)) return false;
            // A new record keeps provenance and identity; active jobs already froze their input and output.
            // Use the task-12 provenance constructor so restored corpus does not require an old live task.
            var replacement = new CorpusRecord(record.Id, record.RequestId, record.ConversationId, record.RoundId,
                record.NodeId, record.ChoiceId, record.ScoreSource, record.Quality, scores);
            corpus[corpusId] = replacement; Changed?.Invoke(); return true;
        }
    }
}
#endif
