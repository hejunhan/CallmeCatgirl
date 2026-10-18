using System;
using System.Collections.Generic;
using System.Linq;
using CallmeCatgirl.Gameplay.TaskGrid;

namespace CallmeCatgirl.Gameplay.Training
{
    [Serializable] public sealed class CorpusUse
    {
        public string CorpusId;
        public double Points;
    }

    [Serializable] public sealed class ConversationInvestment
    {
        public string ConversationId;
        public double Points;
    }

    public sealed class TrainingPreview
    {
        public double TotalPoints { get; internal set; }
        public double WorkTokens { get; internal set; }
        public double EstimatedSeconds { get; internal set; }
        public int Cost { get; internal set; }
        public IReadOnlyList<Cell> Shape { get; internal set; }
        public IReadOnlyDictionary<AbilityKind,double> Abilities { get; internal set; }
    }

    public sealed partial class TrainingSession
    {
        // Raw corpus is immutable. Jobs are the only spending ledger, including unfinished jobs.
        public double RemainingPoints(string id)
        {
            if (id == null || !corpus.TryGetValue(id, out var record)) return 0;
            double spent = jobs.Values.Sum(j => j.Uses == null
                ? (j.CorpusIds.Contains(id) ? record.Scores.Values.Sum() : 0)
                : j.Uses.Where(u => u.CorpusId == id).Sum(u => u.Points));
            return Math.Max(0, record.Scores.Values.Sum() - spent);
        }

        private CorpusUse[] Allocate(string conversationId, double points)
        {
            Check.Id(conversationId); Check.Number(points, true);
            var result = new List<CorpusUse>();
            foreach (var r in corpus.Values.Where(r => r.ConversationId == conversationId))
            {
                double take = Math.Min(points, RemainingPoints(r.Id));
                if (take > 1e-9) { result.Add(new CorpusUse { CorpusId = r.Id, Points = take }); points -= take; }
                if (points <= 1e-9) break;
            }
            if (points > 1e-9) throw new ArgumentException("Insufficient conversation corpus.");
            return result.ToArray();
        }

        private CorpusRecord Portion(CorpusUse use)
        {
            var r = corpus[use.CorpusId]; double ratio = use.Points / r.Scores.Values.Sum();
            return new CorpusRecord(r.Id, r.RequestId, r.ConversationId, r.RoundId, r.NodeId, r.ChoiceId,
                r.ScoreSource, r.Quality, r.Scores.ToDictionary(p => p.Key, p => p.Value * ratio));
        }

        public IReadOnlyDictionary<AbilityKind,double> PreviewCorpus(string conversationId, double points)
            => Allocate(conversationId, points).Select(Portion).SelectMany(r => r.Scores)
                .GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.Sum(p => p.Value));

        public IReadOnlyDictionary<AbilityKind,double> PreviewTrainingAbilities(string recipeId,string conversationId,double points)
        {
            var ratio=recipes[recipeId].PointsPerScore*policyModifiers().TrainingScoreMultiplier;
            return PreviewCorpus(conversationId,points).ToDictionary(p=>p.Key,p=>p.Value*ratio);
        }

        // Compact deterministic growth; authored base cells stay intact, then adjacent rings expand.
        public static Cell[] GrowShape(TrainingRecipe recipe, double points, int boardWidth, int boardHeight)
        {
            Check.Number(points, true);
            if (points > recipe.MaximumCorpusPoints + 1e-9) throw new ArgumentException("Corpus exceeds recipe limit.");
            int extra = checked((int)Math.Floor((points + 1e-9) / recipe.PointsPerExtraCell));
            var cells = new HashSet<Cell>(recipe.Shape);
            if (boardWidth <= 0 || boardHeight <= 0 || cells.Any(c=>c.X>=boardWidth || c.Y>=boardHeight)
                || (long)cells.Count + extra > (long)boardWidth * boardHeight)
                throw new ArgumentException("Training shape exceeds board.");
            while (extra-- > 0)
            {
                var next = cells.SelectMany(c=>new[]{new Cell(c.X-1,c.Y),new Cell(c.X+1,c.Y),new Cell(c.X,c.Y-1),new Cell(c.X,c.Y+1)})
                    .Where(c=>c.X>=0 && c.Y>=0 && c.X<boardWidth && c.Y<boardHeight && !cells.Contains(c))
                    .Distinct().OrderBy(c=>Math.Max(c.X,c.Y)).ThenBy(c=>c.Y).ThenBy(c=>c.X).First();
                cells.Add(next);
            }
            return cells.OrderBy(c=>c.Y).ThenBy(c=>c.X).ToArray();
        }

        private CorpusUse[] Allocate(IEnumerable<ConversationInvestment> investments)
        {
            var rows = investments?.ToArray();
            if (rows == null || rows.Length == 0 || rows.Any(r=>r==null || string.IsNullOrWhiteSpace(r.ConversationId))
                || rows.Select(r=>r.ConversationId).Distinct(StringComparer.Ordinal).Count()!=rows.Length)
                throw new ArgumentException("Choose each conversation once.");
            return rows.SelectMany(r=>Allocate(r.ConversationId,r.Points)).ToArray();
        }

        public TrainingPreview PreviewTraining(string recipeId, IEnumerable<ConversationInvestment> investments)
        {
            var rule=recipes[recipeId]; var uses=Allocate(investments);
            return BuildPreview(rule,uses);
        }

        private TrainingPreview BuildPreview(TrainingRecipe rule, CorpusUse[] uses)
        {
            if (uses.Length < rule.MinimumRecords) throw new ArgumentException("Insufficient corpus records.");
            double total=uses.Sum(u=>u.Points); Check.Number(total,true);
            var modifiers=policyModifiers();
            double work=rule.WorkForPoints(total)*modifiers.TrainingWorkMultiplier; Check.Number(work,true);
            double speed=simulation.TokenThroughput>0?simulation.TokenThroughput:simulation.Architecture.SpeedMultiplier;
            var abilities=uses.Select(Portion).SelectMany(r=>r.Scores).GroupBy(p=>p.Key)
                .ToDictionary(g=>g.Key,g=>g.Sum(p=>p.Value)*rule.PointsPerScore*modifiers.TrainingScoreMultiplier);
            foreach(double value in abilities.Values) Check.Number(value);
            return new TrainingPreview { TotalPoints=total,WorkTokens=work,EstimatedSeconds=work/speed,
                Cost=checked((int)Math.Ceiling(rule.Cost*modifiers.TrainingCostMultiplier)),
                Shape=Array.AsReadOnly(GrowShape(rule,total,simulation.Width,simulation.Height)),Abilities=abilities };
        }

        public TrainingResult StartConversationsTraining(string recipeId, string architectureId, string name,
            IEnumerable<ConversationInvestment> investments, out TrainingJob job)
        {
            job=null;
            if(busy)return TrainingResult.Busy;
            if(recipeId==null || !recipes.TryGetValue(recipeId,out var rule))return TrainingResult.UnknownRecipe;
            try
            {
                var uses=Allocate(investments); var preview=BuildPreview(rule,uses);
                return StartTrainingCore(recipeId,architectureId,name,uses,preview.Shape,out job);
            }
            catch(ArgumentException){return TrainingResult.InvalidInput;}
            catch(OverflowException){return TrainingResult.InvalidInput;}
        }

        public TrainingResult StartConversationTraining(string recipeId, string architectureId, string name,
            string conversationId, double points, out TrainingJob job)
            => StartConversationsTraining(recipeId,architectureId,name,
                new[]{new ConversationInvestment {ConversationId=conversationId,Points=points}},out job);
    }
}
