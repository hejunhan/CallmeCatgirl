using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using CallmeCatgirl.Gameplay.TaskGrid;

namespace CallmeCatgirl.Gameplay.Training
{
    /// <summary>Sidecar until Story.ReplyChoice gains explicit corpus scores. Exact stable IDs; no text keyword guessing.</summary>
    [CreateAssetMenu(menuName = "CallmeCatgirl/Training/Dialogue Corpus Configuration")]
    public sealed class DialogueCorpusConfiguration : ScriptableObject
    {
        public DialogueScores[] Entries = Array.Empty<DialogueScores>();
        [Tooltip("正式剧情缺数值时是否启用暂定补齐。优先使用传入的剧情显式数值，其次精确对话侧表。")]
        public bool AllowRequirementFallback;
        public double CompleteScore = 1, ExcellentScore = 2;
        [Serializable] public sealed class Score
        {
            public AbilityKind Kind;
            public double Value = 1;
        }
        [Serializable] public sealed class DialogueScores
        {
            public string UserId, NodeId, ChoiceId;
            public Score[] Scores = Array.Empty<Score>();
        }
        public bool TryResolve(TaskInstance task, string nodeId, string choiceId,
            IDictionary<AbilityKind, double> explicitStoryScores, out IDictionary<AbilityKind, double> scores, out string source)
        {
            scores = null; source = null;
            if (explicitStoryScores != null) { scores = explicitStoryScores; source = "story-explicit"; return true; }
            var matches = (Entries ?? Array.Empty<DialogueScores>()).Where(e => e != null && e.UserId == task.ConversationId
                && e.NodeId == nodeId && e.ChoiceId == choiceId).ToArray();
            if (matches.Length > 1) throw new InvalidOperationException("Duplicate dialogue corpus source.");
            if (matches.Length == 1)
            {
                scores = matches[0].Scores.ToDictionary(p => p.Kind, p => p.Value); source = "dialogue-sidecar"; return true;
            }
            if (!AllowRequirementFallback) return false;
            double amount = task.Result != null && task.Result.Overall == Quality.Excellent ? ExcellentScore : CompleteScore;
            scores = task.Definition.Requirements.Count == 0
                ? new Dictionary<AbilityKind, double> { { AbilityKind.Writing, amount } }
                : task.Definition.Requirements.ToDictionary(r => r.Kind, r => amount);
            source = "tunable-requirement-fallback"; return true;
        }
    }
}
