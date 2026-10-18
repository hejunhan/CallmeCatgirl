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

        // Add adjacent cells row by row inside the base shape's width. Preserve the authored base cells.
        public static Cell[] GrowShape(TrainingRecipe recipe, double points, int boardWidth, int boardHeight)
        {
            Check.Number(points, true);
            if (points > recipe.MaximumCorpusPoints + 1e-9) throw new ArgumentException("Corpus exceeds recipe limit.");
            int extra = checked((int)Math.Floor((points + 1e-9) / recipe.PointsPerExtraCell));
            var cells = new HashSet<Cell>(recipe.Shape);
            int width = recipe.Shape.Max(c => c.X) + 1;
            if (width > boardWidth || recipe.Shape.Max(c => c.Y) >= boardHeight)
                throw new ArgumentException("Base shape exceeds board.");
            for (int y = 0; y < boardHeight && extra > 0; y++)
                for (int x = 0; x < width && extra > 0; x++)
                {
                    var c = new Cell(x,y);
                    if (cells.Contains(c) || !new[] {new Cell(x-1,y),new Cell(x+1,y),new Cell(x,y-1),new Cell(x,y+1)}.Any(cells.Contains)) continue;
                    cells.Add(c); extra--;
                }
            if (extra > 0) throw new ArgumentException("Training shape exceeds board.");
            return cells.OrderBy(c => c.Y).ThenBy(c => c.X).ToArray();
        }

        public TrainingResult StartConversationTraining(string recipeId, string architectureId, string name,
            string conversationId, double points, out TrainingJob job)
        {
            job = null;
            if (busy) return TrainingResult.Busy;
            if (recipeId == null || !recipes.TryGetValue(recipeId, out var recipe)) return TrainingResult.UnknownRecipe;
            try
            {
                var uses = Allocate(conversationId, points);
                var shape = GrowShape(recipe, points, simulation.Width, simulation.Height);
                return StartTrainingCore(recipeId, architectureId, name, uses, shape, out job);
            }
            catch (ArgumentException) { return TrainingResult.InvalidInput; }
            catch (OverflowException) { return TrainingResult.InvalidInput; }
        }
    }
}
