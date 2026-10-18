using System;
using System.Collections.Generic;
using System.Linq;
using CallmeCatgirl.Gameplay.TaskGrid;
using CallmeCatgirl.Gameplay.Training;

namespace CallmeCatgirl.Persistence.Training
{
    /// <summary>Optional native TrainingSession v1 bridge. No separate inventory, model catalog, wallet, or task clock.</summary>
    public sealed class TrainingSaveExtension : IRunSaveExtension, IRunTaskRoleProvider
    {
        public const string ExtensionId = "callmecatgirl.training";
        private readonly StateSaveExtension<TrainingSessionState> bridge;
        private readonly TrainingRecipe[] recipes;
        private readonly ArchitectureDefinition[] architectures;
        private readonly ComputeTier[] compute;
        public string Id => ExtensionId;
        public bool Required => true;

        public TrainingSaveExtension(ISaveJsonCodec codec, Func<TrainingSession> currentSession,
            IEnumerable<TrainingRecipe> recipes, IEnumerable<ArchitectureDefinition> architectures, IEnumerable<ComputeTier> compute)
        {
            if (currentSession == null) throw new ArgumentNullException(nameof(currentSession));
            this.recipes = recipes?.ToArray() ?? throw new ArgumentNullException(nameof(recipes));
            this.architectures = architectures?.ToArray() ?? throw new ArgumentNullException(nameof(architectures));
            this.compute = compute?.ToArray() ?? throw new ArgumentNullException(nameof(compute));
            bridge = new StateSaveExtension<TrainingSessionState>(Id, TrainingSession.StateVersion, true, codec,
                () => (currentSession() ?? throw new InvalidOperationException("Training session is not bound.")).CaptureState(),
                ValidateNative, saved => saved.Jobs.Select(job => job.RequestId).ToArray());
        }
        private void ValidateNative(TrainingSessionState state)
        {
            // A future native schema is incompatible, not corruption that should roll the run back to an older backup.
            if (state != null && (state.Version > TrainingSession.StateVersion || state.Identities?.Version > 1
                || state.Models?.Any(m => m != null && m.Version > 1) == true
                || state.Jobs?.Any(j => j?.Output != null && j.Output.Version > 1) == true))
                throw new SaveCompatibilityException("Unsupported native training state version.");
            TrainingSession.ValidateState(state, recipes, architectures, compute);
        }
        public SaveExtensionState Capture() => bridge.Capture();
        public void Validate(SaveExtensionState state) => bridge.Validate(state);
        public string[] NonCustomerRequestIds(SaveExtensionState state) => bridge.NonCustomerRequestIds(state);
        public TrainingSessionState DecodeForRestore(SaveExtensionState state) => bridge.DecodeForRestore(state);

        /// <summary>Prepare beside the restored TaskSimulation and wallet. Returns a detached native session, never binds the live controller.</summary>
        public TrainingSession PrepareRestore(SaveExtensionState state, TaskSimulation simulation,
            Func<int, bool> spend, Action<int> refund, Func<IEnumerable<Cell>> canonicalCapacity,
            Func<ModelDefinition, ModelDefinition> composePermanentBonuses = null, Func<PolicyModifiers> policies = null,
            Func<int> restoredStage = null)
        {
            if (simulation == null) throw new ArgumentNullException(nameof(simulation));
            var saved = DecodeForRestore(state);
            return TrainingSession.RestoreState(saved, simulation, recipes, architectures, compute,
                spend, refund,
                profile => simulation.TryChangeExecutionProfile(profile.Model, profile.TokensPerSecond, profile.OpenCells, profile.Architecture),
                rate => simulation.TrySetTokenThroughput(rate), canonicalCapacity,
                composePermanentBonuses, policies, restoredStage, restoredTokensPerSecond: simulation.TokenThroughput);
        }
    }
}
