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
        [InspectorName("按最高能力要求发放语料（旧模式）"), Tooltip("开启后只奖励要求最高的一项能力；关闭后每项能力都会获得语料。")]
        public bool UseHighestRequirement;
        [InspectorName("旧模式语料比例"), Min(0), Tooltip("仅在旧模式开启时使用。")]
        public double HighestRequirementRatio = 0.1;
        [InspectorName("每项能力要求的最低语料比例"), Min(0)] public double RequirementRatioMin = 0.05;
        [InspectorName("每项能力要求的最高语料比例"), Min(0), Tooltip("关闭旧模式后，每项能力要求都会在最小值和最大值之间独立抽取比例。")]
        public double RequirementRatioMax = 0.1;
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
            if(task==null)return false;
            if(UseHighestRequirement)
            {
                scores=CorpusRewards.FromRequirements(task.Definition.Requirements,HighestRequirementRatio);
                source="highest-requirement-ratio";return scores.Count>0;
            }
            if (explicitStoryScores != null) { scores = explicitStoryScores; source = "story-explicit"; return true; }
            var matches = (Entries ?? Array.Empty<DialogueScores>()).Where(e => e != null && e.UserId == task.ConversationId
                && e.NodeId == nodeId && e.ChoiceId == choiceId).ToArray();
            if (matches.Length > 1) throw new InvalidOperationException("Duplicate dialogue corpus source.");
            if (matches.Length == 1)
            {
                scores = matches[0].Scores.ToDictionary(p => p.Kind, p => p.Value); source = "dialogue-sidecar"; return true;
            }
            if (double.IsNaN(RequirementRatioMin) || double.IsInfinity(RequirementRatioMin)
                || double.IsNaN(RequirementRatioMax) || double.IsInfinity(RequirementRatioMax)
                || RequirementRatioMin < 0 || RequirementRatioMax < RequirementRatioMin)
                throw new InvalidOperationException("Dialogue corpus ratio range is invalid.");
            if (RequirementRatioMax > 0)
            {
                scores = CorpusRewards.FromAllRequirements(task.Definition.Requirements,
                    _ => RequirementRatioMin + (RequirementRatioMax - RequirementRatioMin) * UnityEngine.Random.value);
                if (scores.Count > 0)
                {
                    source = "all-requirements-random-range"; return true;
                }
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
