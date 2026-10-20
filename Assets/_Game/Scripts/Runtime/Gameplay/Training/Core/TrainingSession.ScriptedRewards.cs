using System;
using System.Collections.Generic;
using System.Linq;
using CallmeCatgirl.Gameplay.TaskGrid;

namespace CallmeCatgirl.Gameplay.Training
{
    public sealed partial class TrainingSession
    {
        public const string StarterRecipeId = "tutorial-first-model";
        public const string TutorialCorpusRecipeId = "tutorial-corpus-model";
        public bool StarterTrainingAvailable => jobs.Count == 0 && models.Values.All(m=>m.CorpusIds.Count==0);
        // The second tutorial training receives 9 corpus points before any memory install.
        // It stays one cell wide and is only offered between model 1 and model 9.
        public bool CompactTrainingAvailable => models.Count == 1 && jobs.Count == 1;
        public double BestAvailableCorpusPoints => corpus.Values.Select(r=>RemainingPoints(r.Id)).DefaultIfEmpty(0).Max();

        private static void AddStarterRecipe(IDictionary<string,TrainingRecipe> rules)
        {
            if (!rules.ContainsKey(StarterRecipeId))
                rules.Add(StarterRecipeId,new TrainingRecipe(StarterRecipeId,"第一次训练 · 1点 / 1格 / 免费",0,1,10,1,1,
                    new[]{new Cell(0,0)},2,1));
        }

        private static void AddTutorialCorpusRecipe(IDictionary<string,TrainingRecipe> rules)
        {
            if (!rules.ContainsKey(TutorialCorpusRecipeId))
                rules.Add(TutorialCorpusRecipeId,new TrainingRecipe(TutorialCorpusRecipeId,"第二次训练 · 1格 / 20金币",20,1,10,1,1,
                    new[]{new Cell(0,0)},100,100,0));
        }

        /// <summary>Story/policy reward only. The stable event ID is stored with the immutable record;
        /// loading or replaying the same event cannot award twice. Does not fabricate a task or accepted dialogue.</summary>
        public TrainingResult GrantScriptedCorpus(string eventId,string conversationId,
            IDictionary<AbilityKind,double> scores,string sourceLabel = "剧情奖励")
        {
            if (busy) return TrainingResult.Busy;
            if (string.IsNullOrWhiteSpace(eventId) || string.IsNullOrWhiteSpace(conversationId)) return TrainingResult.InvalidInput;
            if (corpus.Values.Any(r=>r.SourceKind==CorpusSourceKind.ScriptedReward && r.EventId==eventId))
                return TrainingResult.AlreadyRecorded;
            CorpusRecord record;
            try
            {
                record=new CorpusRecord("corpus-"+(corpus.Count+1),"scripted:"+eventId,conversationId,eventId,
                    eventId,"grant",sourceLabel,Quality.Complete,scores,CorpusSourceKind.ScriptedReward,eventId);
            }
            catch(ArgumentException){return TrainingResult.InvalidInput;}
            corpus.Add(record.Id,record);Changed?.Invoke();return TrainingResult.Success;
        }

        /// <summary>Freeze the best material at purchase time, improve its point values and grant a larger pool.
        /// Zero-inventory fallback is four Writing points before multipliers. Repeat calls never recompute or award.</summary>
        public TrainingResult GrantBestCorpusReward(string eventId,string conversationId,
            double quantityMultiplier=3,double improvementMultiplier=1.25,double minimumPoints=4)
        {
            if (busy) return TrainingResult.Busy;
            if (corpus.Values.Any(r=>r.SourceKind==CorpusSourceKind.ScriptedReward && r.EventId==eventId))
                return TrainingResult.AlreadyRecorded;
            try
            {
                Check.Number(quantityMultiplier,true);Check.Number(improvementMultiplier,true);Check.Number(minimumPoints,true);
                if (quantityMultiplier<1 || improvementMultiplier<=1) return TrainingResult.InvalidInput;
                var best=corpus.Values.Where(r=>RemainingPoints(r.Id)>1e-9).OrderByDescending(r=>RemainingPoints(r.Id)).ThenBy(r=>r.Id,StringComparer.Ordinal).FirstOrDefault();
                var basis=best==null?new Dictionary<AbilityKind,double>{{AbilityKind.Writing,minimumPoints}}
                    :best.Scores.ToDictionary(p=>p.Key,p=>p.Value*RemainingPoints(best.Id)/best.Scores.Values.Sum());
                double scale=Math.Max(1,minimumPoints/basis.Values.Sum())*quantityMultiplier*improvementMultiplier;
                var reward=basis.ToDictionary(p=>p.Key,p=>p.Value*scale);
                foreach(var value in reward.Values)Check.Number(value);
                return GrantScriptedCorpus(eventId,conversationId,reward,"绝版书籍 · 购入时最佳语料提升");
            }
            catch(ArgumentException){return TrainingResult.InvalidInput;}
        }
    }
}
