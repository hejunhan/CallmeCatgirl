using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CallmeCatgirl.Gameplay.TaskGrid;

namespace CallmeCatgirl.Gameplay.Training
{
    internal static class Check
    {
        internal static void Number(double value, bool positive = false)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || (positive ? value <= 0 : value < 0))
                throw new ArgumentOutOfRangeException(nameof(value));
        }
        internal static string Id(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("ID is required.");
            return value;
        }
        internal static IReadOnlyDictionary<AbilityKind, double> Scores(IDictionary<AbilityKind, double> input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            var result = new Dictionary<AbilityKind, double>();
            foreach (var pair in input)
            {
                if (!Enum.IsDefined(typeof(AbilityKind), pair.Key)) throw new ArgumentException("Unknown ability.");
                Number(pair.Value); result.Add(pair.Key, pair.Value);
            }
            return new ReadOnlyDictionary<AbilityKind, double>(result);
        }
    }

    public enum CorpusSourceKind { Dialogue = 0, ScriptedReward = 1 }

    /// <summary>An immutable accepted dialogue fragment or explicitly identified story reward.</summary>
    public sealed class CorpusRecord
    {
        public string Id { get; }
        public string RequestId { get; }
        public string ConversationId { get; }
        public string RoundId { get; }
        public string NodeId { get; }
        public string ChoiceId { get; }
        public string ScoreSource { get; }
        public CorpusSourceKind SourceKind { get; }
        public string EventId { get; }
        public Quality Quality { get; }
        public IReadOnlyDictionary<AbilityKind, double> Scores { get; }
        public CorpusRecord(string id, TaskInstance task, string nodeId, string choiceId,
            string scoreSource, IDictionary<AbilityKind, double> scores)
        {
            Id = Check.Id(id); NodeId = Check.Id(nodeId); ChoiceId = Check.Id(choiceId);
            if (task == null || task.Result == null) throw new ArgumentException("Completed request required.");
            RequestId = task.Id; ConversationId = task.ConversationId; RoundId = task.RoundId;
            Quality = task.Result.Overall; ScoreSource = Check.Id(scoreSource); Scores = Check.Scores(scores);
            if (!Scores.Values.Any(v => v > 0)) throw new ArgumentException("Corpus must contain a positive score.");
        }
        internal CorpusRecord(string id, string requestId, string conversationId, string roundId, string nodeId,
            string choiceId, string scoreSource, Quality quality, IDictionary<AbilityKind,double> scores,
            CorpusSourceKind sourceKind = CorpusSourceKind.Dialogue, string eventId = null)
        {
            Id=Check.Id(id);RequestId=Check.Id(requestId);ConversationId=Check.Id(conversationId);RoundId=Check.Id(roundId);
            NodeId=Check.Id(nodeId);ChoiceId=Check.Id(choiceId);ScoreSource=Check.Id(scoreSource);
            if (!Enum.IsDefined(typeof(Quality),quality) || quality<Quality.Complete) throw new ArgumentException("Ineligible corpus quality.");
            if (!Enum.IsDefined(typeof(CorpusSourceKind),sourceKind)) throw new ArgumentException("Unknown corpus source.");
            SourceKind=sourceKind;EventId=sourceKind==CorpusSourceKind.ScriptedReward?Check.Id(eventId):null;
            Quality=quality;Scores=Check.Scores(scores);
            if (!Scores.Values.Any(v=>v>0)) throw new ArgumentException("Corpus must have positive scores.");
        }
    }

    public enum OutlineTransform { Keep, Transpose, MirrorX }
    public sealed class ArchitectureDefinition
    {
        public string Id { get; }
        public string Title { get; }
        public double SpeedMultiplier { get; }
        public double CorpusMultiplier => Runtime.CorpusMultiplier;
        public ArchitectureRuntime Runtime { get; }
        public OutlineTransform Outline { get; }
        public int UnlockStage { get; }
        public bool ContentReady { get; }
        public string TechnicalDescription { get; }
        public ArchitectureDefinition(string id, string title, double speedMultiplier, OutlineTransform outline,
            int unlockStage = 1, bool contentReady = true, string technicalDescription = "",
            double corpusMultiplier = 1, double abilityRateMultiplier = 1, bool freezeOnPlacement = false)
        {
            Id = Check.Id(id); Title = Check.Id(title); Check.Number(speedMultiplier, true);
            if (!Enum.IsDefined(typeof(OutlineTransform), outline)) throw new ArgumentException("Invalid outline.");
            if (unlockStage < 1 || unlockStage > 5) throw new ArgumentOutOfRangeException(nameof(unlockStage));
            SpeedMultiplier = speedMultiplier; Outline = outline;
            Runtime = new ArchitectureRuntime(id,speedMultiplier,abilityRateMultiplier,corpusMultiplier,freezeOnPlacement);
            UnlockStage = unlockStage; ContentReady = contentReady;
            TechnicalDescription = technicalDescription ?? string.Empty;
        }
        public Cell[] Transform(IEnumerable<Cell> capacityCells, int width, int height)
        {
            if (width <= 0 || height <= 0 || (Outline == OutlineTransform.Transpose && width != height))
                throw new ArgumentException("Transpose requires a square board.");
            var source = capacityCells?.ToArray() ?? throw new ArgumentNullException(nameof(capacityCells));
            if (source.Distinct().Count() != source.Length
                || source.Any(c => c.X < 0 || c.Y < 0 || c.X >= width || c.Y >= height))
                throw new ArgumentException("Invalid capacity cells.");
            return source.Select(c => Outline == OutlineTransform.Transpose ? new Cell(c.Y, c.X)
                : Outline == OutlineTransform.MirrorX ? new Cell(width - 1 - c.X, c.Y) : c).ToArray();
        }
    }

    public sealed class ComputeTier
    {
        public string Id { get; }
        public double TokensPerSecond { get; }
        public int UpgradeCost { get; }
        public ComputeTier(string id, double tokensPerSecond, int upgradeCost)
        {
            Id = Check.Id(id); Check.Number(tokensPerSecond, true);
            if (upgradeCost < 0) throw new ArgumentOutOfRangeException(nameof(upgradeCost));
            TokensPerSecond = tokensPerSecond; UpgradeCost = upgradeCost;
        }
    }

    /// <summary>Permanent policy effects are independent inputs. Neutral by default; training effects never imply inference speed.</summary>
    public sealed class PolicyModifiers
    {
        public static readonly PolicyModifiers Neutral = new PolicyModifiers();
        public double InferenceSpeedMultiplier { get; }
        public double TrainingScoreMultiplier { get; }
        public double TrainingCostMultiplier { get; }
        public double TrainingWorkMultiplier { get; }
        public PolicyModifiers(double inferenceSpeedMultiplier = 1, double trainingScoreMultiplier = 1,
            double trainingCostMultiplier = 1, double trainingWorkMultiplier = 1)
        {
            Check.Number(inferenceSpeedMultiplier, true); Check.Number(trainingScoreMultiplier, true);
            Check.Number(trainingCostMultiplier); Check.Number(trainingWorkMultiplier, true);
            InferenceSpeedMultiplier = inferenceSpeedMultiplier; TrainingScoreMultiplier = trainingScoreMultiplier;
            TrainingCostMultiplier = trainingCostMultiplier; TrainingWorkMultiplier = trainingWorkMultiplier;
        }
    }

    public sealed class TrainingRecipe
    {
        public string Id { get; }
        public string Title { get; }
        public int Cost { get; }
        public int MinimumRecords { get; }
        public double WorkTokens { get; }
        public double AbilityInterval { get; }
        public double PointsPerScore { get; }
        public IReadOnlyList<Cell> Shape { get; }
        public double PointsPerExtraCell { get; }
        public double MaximumCorpusPoints { get; }
        public double WorkGrowthCoefficient { get; }
        public double WorkGrowthExponent { get; }
        public double WorkForPoints(double points)
        {
            Check.Number(points, true);
            double work = WorkGrowthCoefficient == 0 ? WorkTokens : WorkTokens + WorkGrowthCoefficient * Math.Pow(points, WorkGrowthExponent);
            Check.Number(work, true); return work;
        }
        public TrainingRecipe(string id, string title, int cost, int minimumRecords, double workTokens,
            double abilityInterval, double pointsPerScore, IEnumerable<Cell> shape, double pointsPerExtraCell = 2, double maximumCorpusPoints = 20, double workGrowthCoefficient = 0, double workGrowthExponent = 1.5)
        {
            Id = Check.Id(id); Title = Check.Id(title);
            if (cost < 0 || minimumRecords < 1) throw new ArgumentOutOfRangeException(nameof(cost));
            Check.Number(workTokens, true); Check.Number(abilityInterval, true); Check.Number(pointsPerScore, true);
            var cells = shape?.ToArray() ?? throw new ArgumentNullException(nameof(shape));
            // TaskDefinition owns normalization and shape validation.
            Shape = new TaskDefinition(id, title, cells, workTokens, 1, double.MaxValue / 4, 0,
                Array.Empty<AbilityRequirement>()).Shape;
            Check.Number(pointsPerExtraCell, true); Check.Number(maximumCorpusPoints, true);
            Check.Number(workGrowthCoefficient); Check.Number(workGrowthExponent, true);
            if (workGrowthExponent < 1) throw new ArgumentOutOfRangeException(nameof(workGrowthExponent));
            WorkGrowthCoefficient = workGrowthCoefficient; WorkGrowthExponent = workGrowthExponent;
            PointsPerExtraCell = pointsPerExtraCell; MaximumCorpusPoints = maximumCorpusPoints;
            Cost = cost; MinimumRecords = minimumRecords; WorkTokens = workTokens;
            AbilityInterval = abilityInterval; PointsPerScore = pointsPerScore;
        }
        public ModelDefinition Build(string id, IEnumerable<CorpusRecord> records)
        {
            var points = new Dictionary<AbilityKind, double>();
            foreach (var record in records)
                foreach (var pair in record.Scores)
                {
                    points.TryGetValue(pair.Key, out var before);
                    double next = before + pair.Value * PointsPerScore; Check.Number(next); points[pair.Key] = next;
                }
            return new ModelDefinition(id, AbilityInterval, points);
        }
    }

    public static class ModelNames
    {
        // UI and API use the same limit: 40 UTF-16 code units (an emoji may occupy two).
        public const int MaximumLength = 40;
        public static bool TryNormalize(string value, out string normalized)
        {
            normalized = value?.Trim();
            return !string.IsNullOrWhiteSpace(normalized) && normalized.Length <= MaximumLength
                && !normalized.Any(char.IsControl);
        }
    }

    public sealed class CatalogModel
    {
        public string Id { get; }
        public string ModelId => Id;
        public string LegacyId { get; }
        public string ShortModelId
        {
            get { var compact = Guid.Parse(Id).ToString("N").Substring(0, 8).ToUpperInvariant(); return compact.Substring(0, 4) + "-" + compact.Substring(4); }
        }
        public string DisplayName { get; private set; }
        public string Name => DisplayName; // Compatibility alias for the first integration delivery.
        public string ArchitectureId { get; }
        public ModelDefinition Definition { get; }
        public IReadOnlyList<string> CorpusIds { get; }
        public CatalogModel(string name, string architectureId, ModelDefinition definition, IEnumerable<string> corpusIds,
            string modelId = null, string legacyId = null)
        {
            if (!ModelNames.TryNormalize(name, out var normalized)) throw new ArgumentException("Invalid model display name.");
            DisplayName = normalized; ArchitectureId = Check.Id(architectureId);
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            Id = modelId != null ? ModelIdentityRegistry.Canonical(modelId)
                : Guid.TryParse(definition.Id, out var uuid) && uuid != Guid.Empty ? uuid.ToString("D") : Guid.NewGuid().ToString("D");
            LegacyId = legacyId ?? (definition.Id == Id ? null : definition.Id);
            Definition = definition.Id == Id ? definition : new ModelDefinition(Id, definition.Interval, definition.Points.ToDictionary(p => p.Key, p => p.Value));
            CorpusIds = Array.AsReadOnly((corpusIds ?? Array.Empty<string>()).ToArray());
        }
        internal void Rename(string normalizedName) => DisplayName = normalizedName;
    }

    /// <summary>One host transaction, prevalidated and side-effect-free on false. No event draining.</summary>
    public sealed class ExecutionProfile
    {
        public ModelDefinition Model { get; }
        public double TokensPerSecond { get; }
        public IReadOnlyList<Cell> OpenCells { get; }
        public ArchitectureRuntime Architecture { get; }
        public ExecutionProfile(ModelDefinition model, double tokensPerSecond, IEnumerable<Cell> openCells, ArchitectureRuntime architecture = null)
        {
            Model = model ?? throw new ArgumentNullException(nameof(model)); Check.Number(tokensPerSecond, true);
            TokensPerSecond = tokensPerSecond; Architecture = architecture ?? ArchitectureRuntime.Neutral;
            OpenCells = Array.AsReadOnly((openCells ?? throw new ArgumentNullException(nameof(openCells))).ToArray());
            if (OpenCells.Distinct().Count() != OpenCells.Count) throw new ArgumentException("Invalid outline.");
        }
    }

    public enum TrainingResult
    {
        Success, Busy, InvalidInput, AlreadyRecorded, NotSubmitted, LowQuality, MissingCorpus,
        CorpusConsumed, InsufficientFunds, UnknownRecipe, UnknownArchitecture, UnknownModel,
        MustPause, HostRejected, NotReady, AlreadyCompleted, NoUpgrade, ArchitectureLocked, ArchitecturePending
    }
    public sealed class TrainingJob
    {
        public string RequestId { get; }
        public CatalogModel Output { get; }
        public IReadOnlyList<string> CorpusIds => Output.CorpusIds;
        public bool Completed { get; internal set; }
        public string RecipeId { get; }
        public int PaidCost { get; }
        public double WorkTokens { get; }
        // Null denotes the original whole-record consumption format.
        private CorpusUse[] uses;
        public IReadOnlyList<CorpusUse> Uses
        {
            get => uses==null ? null : Array.AsReadOnly(uses.Select(u=>new CorpusUse{CorpusId=u.CorpusId,Points=u.Points}).ToArray());
            internal set => uses=value?.Select(u=>new CorpusUse{CorpusId=u.CorpusId,Points=u.Points}).ToArray();
        }
        internal TrainingJob(string requestId, CatalogModel output, string recipeId, int paidCost, double workTokens)
        { RequestId=requestId;Output=output;RecipeId=recipeId;PaidCost=paidCost;WorkTokens=workTokens; }
    }
}
