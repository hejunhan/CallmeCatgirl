using System;
using System.Collections.Generic;
using System.Linq;
using CallmeCatgirl.Gameplay.TaskGrid;

namespace CallmeCatgirl.Gameplay.Training
{
    /// <summary>Session inventory and commands. TaskSimulation alone owns time, occupancy and task state.</summary>
    public sealed partial class TrainingSession
    {
        private readonly TaskSimulation simulation;
        public TaskSimulation Simulation => simulation;
        private readonly Func<int, bool> spend;
        private readonly Action<int> refund;
        private readonly Func<ExecutionProfile, bool> applyProfile;
        private readonly Func<double, bool> applyThroughput;
        private readonly Func<IEnumerable<Cell>> capacityCells;
        private readonly Func<ModelDefinition, ModelDefinition> composeBonuses;
        private readonly Func<PolicyModifiers> policyModifiers;
        private readonly Func<int> currentStage;
        public ModelIdentityRegistry Identities { get; }
        private readonly Dictionary<string, TrainingRecipe> recipes;
        private readonly Dictionary<string, ArchitectureDefinition> architectures;
        private readonly ComputeTier[] compute;
        private readonly Dictionary<string, CorpusRecord> corpus = new Dictionary<string, CorpusRecord>(StringComparer.Ordinal);
        private readonly HashSet<Tuple<string, string>> recordedFragments = new HashSet<Tuple<string, string>>();
        private readonly HashSet<string> consumed = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, CatalogModel> models = new Dictionary<string, CatalogModel>(StringComparer.Ordinal);
        private readonly Dictionary<string, TrainingJob> jobs = new Dictionary<string, TrainingJob>(StringComparer.Ordinal);
        private bool busy;
        private int serial;
        public string ActiveModelId { get; private set; }
        public int ComputeIndex { get; private set; }
        public IReadOnlyList<CorpusRecord> Corpus => corpus.Values.ToArray();
        public IReadOnlyList<CatalogModel> Models => models.Values.ToArray();
        public IReadOnlyList<TrainingJob> Jobs => jobs.Values.ToArray();
        public IReadOnlyList<TrainingRecipe> Recipes => recipes.Values.ToArray();
        public IReadOnlyList<ArchitectureDefinition> Architectures => architectures.Values.ToArray();
        public int CurrentStage
        {
            get
            {
                int stage = currentStage();
                if (stage < 1 || stage > 5) throw new InvalidOperationException("Progression stage must be within 1..5.");
                return stage;
            }
        }
        public ComputeTier Compute => compute[ComputeIndex];
        public IReadOnlyList<ComputeTier> ComputeTiers => Array.AsReadOnly(compute);
        public ComputeTier NextCompute => ComputeIndex + 1 < compute.Length ? compute[ComputeIndex + 1] : null;
        public double TokensPerSecond => Throughput(models[ActiveModelId].ArchitectureId, ComputeIndex);
        public double PreviewTokensPerSecond(string modelId)
        {
            string id = Identities.ResolveKnown(modelId);
            if (id == null || !models.TryGetValue(id, out var model)) throw new ArgumentException("Unknown model.");
            return Throughput(model.ArchitectureId, ComputeIndex);
        }
        public event Action Changed;
        public bool IsConsumed(string id) => id != null && corpus.ContainsKey(id) && RemainingPoints(id) <= 1e-9;
        public bool IsTrainingRequest(string id) => id != null && jobs.ContainsKey(id);
        public bool TryGetJob(string id, out TrainingJob job) { job = null; return id != null && jobs.TryGetValue(id, out job); }

        public TrainingSession(TaskSimulation simulation, CatalogModel initial,
            IEnumerable<TrainingRecipe> recipes, IEnumerable<ArchitectureDefinition> architectures,
            IEnumerable<ComputeTier> compute, Func<int, bool> spend, Action<int> refund,
            Func<ExecutionProfile, bool> applyProfile, Func<double, bool> applyThroughput,
            Func<IEnumerable<Cell>> capacityCells, Func<ModelDefinition, ModelDefinition> composeBonuses = null,
            Func<PolicyModifiers> policyModifiers = null, Func<int> currentStage = null, ModelIdentityRegistry identities = null)
            : this(simulation, initial, recipes, architectures, compute, spend, refund, applyProfile, applyThroughput,
                capacityCells, composeBonuses, policyModifiers, currentStage, identities, false) { }

        private TrainingSession(TaskSimulation simulation, CatalogModel initial,
            IEnumerable<TrainingRecipe> recipes, IEnumerable<ArchitectureDefinition> architectures,
            IEnumerable<ComputeTier> compute, Func<int, bool> spend, Action<int> refund,
            Func<ExecutionProfile, bool> applyProfile, Func<double, bool> applyThroughput,
            Func<IEnumerable<Cell>> capacityCells, Func<ModelDefinition, ModelDefinition> composeBonuses,
            Func<PolicyModifiers> policyModifiers, Func<int> currentStage, ModelIdentityRegistry identities, bool restoring)
        {
            this.simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
            this.spend = spend ?? throw new ArgumentNullException(nameof(spend));
            this.refund = refund ?? throw new ArgumentNullException(nameof(refund));
            this.applyProfile = applyProfile ?? throw new ArgumentNullException(nameof(applyProfile));
            this.applyThroughput = applyThroughput ?? throw new ArgumentNullException(nameof(applyThroughput));
            this.capacityCells = capacityCells ?? throw new ArgumentNullException(nameof(capacityCells));
            this.composeBonuses = composeBonuses ?? (model => model);
            this.policyModifiers = policyModifiers ?? (() => PolicyModifiers.Neutral);
            this.currentStage = currentStage ?? (() => 1);
            Identities = identities ?? new ModelIdentityRegistry();
            this.recipes = recipes?.ToDictionary(r => r.Id, StringComparer.Ordinal) ?? throw new ArgumentNullException(nameof(recipes));
            this.architectures = architectures?.ToDictionary(a => a.Id, StringComparer.Ordinal) ?? throw new ArgumentNullException(nameof(architectures));
            this.compute = compute?.ToArray() ?? throw new ArgumentNullException(nameof(compute));
            if (this.recipes.Count == 0 || this.compute.Length == 0 || this.architectures.Count == 0)
                throw new ArgumentException("Incomplete training configuration.");
            AddStarterRecipe(this.recipes);
            for (int i = 0; i < this.compute.Length; i++)
                if (this.compute[i] == null || (i > 0 && this.compute[i].TokensPerSecond <= this.compute[i-1].TokensPerSecond))
                    throw new ArgumentException("Compute tiers must strictly increase.");
            foreach (var architecture in this.architectures.Keys)
                for (int i = 0; i < this.compute.Length; i++) Throughput(architecture, i);
            if (restoring) return; // Detached restore never applies profiles, UUID migration, money or task commands.
            if (initial == null || !this.architectures.ContainsKey(initial.ArchitectureId)) throw new ArgumentException("Missing initial model.");
            if (initial.Id != simulation.Model.Id && initial.LegacyId != simulation.Model.Id)
                throw new ArgumentException("Initial catalog must match the live model ID or its migration alias.");
            if (ArchitectureAvailability(initial.ArchitectureId) != TrainingResult.Success)
                throw new ArgumentException("Initial architecture must be defined and unlocked at the current stage.");
            models.Add(initial.Id, initial); ActiveModelId = initial.Id;
            if (!Identities.Register(initial.LegacyId, initial.Id)) throw new ArgumentException("Conflicting initial model migration.");
            if (!simulation.TrySetArchitecture(this.architectures[initial.ArchitectureId].Runtime))
                throw new ArgumentException("Architecture initialization requires a paused simulation.");
            if (simulation.Model.Id != initial.Id)
            {
                // Identity-only migration while paused: preserve the CURRENT effective points, interval and all task state.
                var migrated = new ModelDefinition(initial.Id, simulation.Model.Interval, simulation.Model.Points.ToDictionary(p => p.Key, p => p.Value));
                if (!simulation.TryChangeModel(migrated)) throw new ArgumentException("Initial UUID migration requires a paused simulation.");
            }
        }

        private double Throughput(string architectureId, int tier)
        {
            double rate = compute[tier].TokensPerSecond * architectures[architectureId].SpeedMultiplier
                * this.policyModifiers().InferenceSpeedMultiplier;
            Check.Number(rate, true); return rate;
        }

        public TrainingResult ArchitectureAvailability(string id)
        {
            if (id == null || !architectures.TryGetValue(id, out var architecture)) return TrainingResult.UnknownArchitecture;
            if (!architecture.ContentReady) return TrainingResult.ArchitecturePending;
            return CurrentStage < architecture.UnlockStage ? TrainingResult.ArchitectureLocked : TrainingResult.Success;
        }

        /// <summary>Call only AFTER Story.ChooseAtRevision succeeds, with captured stable source IDs and explicit dialogue scores.
        /// Records one fragment per request/node even when a branch revisits it. Never converts funds or purchase points.</summary>
        public TrainingResult RecordDialogue(string requestId, string nodeId, string choiceId,
            IDictionary<AbilityKind, double> scores, string scoreSource)
        {
            if (busy) return TrainingResult.Busy;
            if (string.IsNullOrWhiteSpace(requestId) || string.IsNullOrWhiteSpace(nodeId) || string.IsNullOrWhiteSpace(choiceId))
                return TrainingResult.InvalidInput;
            if (!simulation.Tasks.TryGetValue(requestId, out var task) || IsTrainingRequest(requestId)
                || (task.State != RequestState.Submitted && task.State != RequestState.Closed) || task.Result == null)
                return TrainingResult.NotSubmitted;
            if (task.Result.Overall < Quality.Complete) return TrainingResult.LowQuality;
            var key = Tuple.Create(requestId, nodeId);
            if (recordedFragments.Contains(key)) return TrainingResult.AlreadyRecorded;
            CorpusRecord record;
            try
            {
                double multiplier = (task.Architecture ?? simulation.Architecture).CorpusMultiplier;
                record = new CorpusRecord("corpus-" + (corpus.Count + 1), task, nodeId, choiceId, scoreSource,
                    scores?.ToDictionary(p=>p.Key,p=>p.Value*multiplier));
            }
            catch (ArgumentException) { return TrainingResult.InvalidInput; }
            recordedFragments.Add(key); corpus.Add(record.Id, record); Changed?.Invoke(); return TrainingResult.Success;
        }

        /// <summary>Pay once and consume each chosen fragment once; inputs and output are frozen at creation.
        /// Queued training does not run until placed on the SAME task board.</summary>
        public TrainingResult StartTraining(string recipeId, string architectureId, string name,
            IEnumerable<string> corpusIds, out TrainingJob job)
        {
            job = null;
            if (busy) return TrainingResult.Busy;
            if (recipeId == null || !recipes.TryGetValue(recipeId, out var recipe)) return TrainingResult.UnknownRecipe;
            var availability = ArchitectureAvailability(architectureId);
            if (availability != TrainingResult.Success) return availability;
            if (!ModelNames.TryNormalize(name, out name) || corpusIds == null) return TrainingResult.InvalidInput;
            var ids = corpusIds.ToArray();
            if (ids.Length < recipe.MinimumRecords || ids.Any(string.IsNullOrWhiteSpace)
                || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length) return TrainingResult.InvalidInput;
            if (ids.Any(id => !corpus.ContainsKey(id))) return TrainingResult.MissingCorpus;
            if (ids.Any(IsConsumed)) return TrainingResult.CorpusConsumed;
            var uses = ids.Select(id => new CorpusUse { CorpusId=id, Points=RemainingPoints(id) }).ToArray();
            try
            {
                var shape = recipe.WorkGrowthCoefficient > 0 ? GrowShape(recipe, uses.Sum(u=>u.Points), simulation.Width, simulation.Height) : recipe.Shape;
                return StartTrainingCore(recipeId, architectureId, name, uses, shape, out job);
            }
            catch (ArgumentException) { return TrainingResult.InvalidInput; }
            catch (OverflowException) { return TrainingResult.InvalidInput; }
        }

        private TrainingResult StartTrainingCore(string recipeId, string architectureId, string name,
            CorpusUse[] uses, IEnumerable<Cell> shape, out TrainingJob job)
        {
            job = null;
            var recipe = recipes[recipeId];
            if (recipeId == StarterRecipeId && (!StarterTrainingAvailable || Math.Abs(uses.Sum(u=>u.Points)-1)>1e-9))
                return TrainingResult.InvalidInput;
            var availability = ArchitectureAvailability(architectureId);
            if (availability != TrainingResult.Success) return availability;
            if (!ModelNames.TryNormalize(name, out name) || uses.Length == 0) return TrainingResult.InvalidInput;
            var ids = uses.Select(u => u.CorpusId).ToArray();
            if (ids.Length < recipe.MinimumRecords) return TrainingResult.InvalidInput;
            int next = serial;
            string requestId, outputId;
            do { next = checked(next + 1); requestId = "training-" + next; outputId = Guid.NewGuid().ToString("D"); }
            while (simulation.Tasks.ContainsKey(requestId) || models.ContainsKey(outputId)
                || jobs.Values.Any(j => j.Output.Id == outputId) || simulation.Tasks.Values.Any(t => t.ConversationId == requestId));
            CatalogModel output;
            TrainingRecipe effectiveRecipe;
            try
            {
                var modifiers = policyModifiers();
                var effectiveCost = checked((int)Math.Ceiling(recipe.Cost * modifiers.TrainingCostMultiplier));
                effectiveRecipe = new TrainingRecipe(recipe.Id, recipe.Title, effectiveCost, recipe.MinimumRecords,
                    recipe.WorkForPoints(uses.Sum(u => u.Points)) * modifiers.TrainingWorkMultiplier, recipe.AbilityInterval,
                    recipe.PointsPerScore * modifiers.TrainingScoreMultiplier, shape, recipe.PointsPerExtraCell, recipe.MaximumCorpusPoints);
                output = new CatalogModel(name, architectureId, effectiveRecipe.Build(outputId, uses.Select(Portion)), ids);
            }
            catch (ArgumentException) { return TrainingResult.InvalidInput; }
            catch (OverflowException) { return TrainingResult.InvalidInput; }
            var definition = new TaskDefinition("training:" + recipe.Id, "训练 · " + name.Trim(), shape,
                effectiveRecipe.WorkTokens, 1, double.MaxValue / 4, 0, Array.Empty<AbilityRequirement>());
            busy = true;
            try
            {
                if (!spend(effectiveRecipe.Cost)) return TrainingResult.InsufficientFunds;
                if (!simulation.TryCreate(requestId, requestId, "training", definition, out _))
                { refund(effectiveRecipe.Cost); return TrainingResult.HostRejected; }
                serial = next; job = new TrainingJob(requestId, output, recipe.Id, effectiveRecipe.Cost, effectiveRecipe.WorkTokens); jobs.Add(requestId, job);
                job.Uses = Array.AsReadOnly(uses);
                consumed.UnionWith(ids.Where(IsConsumed));
            }
            finally { busy = false; }
            Changed?.Invoke(); return TrainingResult.Success;
        }

        public TrainingResult CompleteTraining(string requestId)
        {
            if (busy) return TrainingResult.Busy;
            if (requestId == null || !jobs.TryGetValue(requestId, out var job)) return TrainingResult.InvalidInput;
            if (job.Completed) return TrainingResult.AlreadyCompleted;
            if (!simulation.Tasks.TryGetValue(requestId, out var task) || task.State != RequestState.Ready)
                return TrainingResult.NotReady;
            busy = true;
            try
            {
                if (!simulation.TrySubmit(requestId)) return TrainingResult.HostRejected;
                simulation.TryCloseFeedback(requestId);
                models.Add(job.Output.Id, job.Output); job.Completed = true;
            }
            finally { busy = false; }
            Changed?.Invoke(); return TrainingResult.Success;
        }

        public TrainingResult SelectModel(string id)
        {
            if (busy) return TrainingResult.Busy;
            if (!simulation.Paused) return TrainingResult.MustPause;
            id = Identities.ResolveKnown(id);
            if (id == null || !models.TryGetValue(id, out var selected)) return TrainingResult.UnknownModel;
            var availability = ArchitectureAvailability(selected.ArchitectureId);
            if (availability != TrainingResult.Success) return availability;
            var architecture = architectures[selected.ArchitectureId];
            ExecutionProfile profile;
            try
            {
                profile = new ExecutionProfile(composeBonuses(selected.Definition), Throughput(architecture.Id, ComputeIndex),
                    architecture.Transform(capacityCells(), simulation.Width, simulation.Height), architecture.Runtime);
                if (profile.Model.Id != selected.Id) return TrainingResult.HostRejected;
            }
            catch (ArgumentException) { return TrainingResult.HostRejected; }
            busy = true;
            try
            {
                if (!applyProfile(profile)) return TrainingResult.HostRejected;
                ActiveModelId = selected.Id;
            }
            finally { busy = false; }
            Changed?.Invoke(); return TrainingResult.Success;
        }

        /// <summary>Rename a saved instance by stable ID, including the starter and active model.
        /// Duplicate display names are allowed; UI and saves must identify instances by ID. No gameplay command is issued.</summary>
        public TrainingResult RenameModel(string modelId, string displayName)
        {
            if (busy) return TrainingResult.Busy;
            modelId = Identities.ResolveKnown(modelId);
            if (modelId == null || !models.TryGetValue(modelId, out var model)) return TrainingResult.UnknownModel;
            if (!ModelNames.TryNormalize(displayName, out var normalized)) return TrainingResult.InvalidInput;
            if (model.DisplayName == normalized) return TrainingResult.Success;
            model.Rename(normalized); Changed?.Invoke(); return TrainingResult.Success;
        }

        public TrainingResult UpgradeCompute()
        {
            if (busy) return TrainingResult.Busy;
            if (!simulation.Paused) return TrainingResult.MustPause;
            if (NextCompute == null) return TrainingResult.NoUpgrade;
            double rate = Throughput(models[ActiveModelId].ArchitectureId, ComputeIndex + 1);
            int cost = NextCompute.UpgradeCost;
            busy = true;
            try
            {
                if (!spend(cost)) return TrainingResult.InsufficientFunds;
                if (!applyThroughput(rate)) { refund(cost); return TrainingResult.HostRejected; }
                ComputeIndex++;
            }
            finally { busy = false; }
            Changed?.Invoke(); return TrainingResult.Success;
        }

        /// <summary>After a permanent inference policy changes, refresh only throughput. Does not switch model or withdraw tasks.</summary>
        public TrainingResult RefreshPolicyThroughput()
        {
            if (busy) return TrainingResult.Busy;
            if (!simulation.Paused) return TrainingResult.MustPause;
            busy = true;
            try { return applyThroughput(TokensPerSecond) ? TrainingResult.Success : TrainingResult.HostRejected; }
            finally { busy = false; }
        }
    }
}
