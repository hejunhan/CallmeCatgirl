using System;
using System.Collections.Generic;
using System.Linq;
using CallmeCatgirl.Gameplay;
using CallmeCatgirl.Gameplay.TaskGrid;
using CallmeCatgirl.Gameplay.Progression;

namespace CallmeCatgirl.Persistence
{
    public sealed class RestoredRunState
    {
        public TaskSimulation Simulation { get; internal set; }
        public EconomyManager Economy { get; internal set; }
        public ConversationStory Story { get; internal set; }
        public ProgressionTree Policies { get; internal set; }
        public System.Random GenerationRandom { get; internal set; }
        public Dictionary<string, TaskDefinition> TaskBases { get; internal set; }
        public HostState Host { get; internal set; }
    }
    public static class RunRestoreBuilder
    {
        /// <summary>Construct the production object graph off to the side; host validates configuration and extensions before committing it.</summary>
        public static RestoredRunState Build(RunSaveData data, string contentRevision, StoryConfiguration storyConfiguration,
            TaskGenerationConfiguration generation, IEnumerable<ProgressionNode> policyNodes,
            Func<RandomStreamState, System.Random> restoreRandom, int previousStoryRevision,
            IEnumerable<IRunSaveExtension> extensions = null, string legacyStoryV1Revision = null,
            StoryConfiguration ordinaryDialoguePool = null, double initialAffinity = 0, double successAffinity = 1, double failureAffinity = -0.5,
            IEnumerable<string> compatiblePresentationRevisions = null, Func<int> stageProvider = null)
        {
            RunSaveValidation.Validate(data, contentRevision, extensions, legacyStoryV1Revision, compatiblePresentationRevisions);
            var simulation = TaskSimulation.RestoreState(data.Simulation);
            var economy = EconomyManager.RestoreState(data.Economy);
            if (restoreRandom == null) throw new ArgumentNullException(nameof(restoreRandom));
            var random = restoreRandom(data.Host.GenerationRandom) ?? throw new ArgumentException("Random state is not supported.");
            var nodes = (policyNodes ?? throw new ArgumentNullException(nameof(policyNodes))).ToArray();
            bool paused = simulation.Paused;
            // Tree.TryRestore adds policy deltas. Restore against a neutral model, then apply saved final attributes once.
            simulation.Paused = true;
            simulation.TryChangeModel(new ModelDefinition(simulation.Model.Id, simulation.Model.Interval, new Dictionary<AbilityKind, double>()));
            var policies = new ProgressionTree(nodes, simulation, economy.TrySpendFunds);
            if (!policies.TryRestore(data.Policies)) throw new ArgumentException("Invalid policy unlock set/configuration.");
            var finalModel = data.Simulation.Model.Build();
            foreach (var point in simulation.Model.Points)
            {
                finalModel.Points.TryGetValue(point.Key, out var final);
                StateRules.Require(final >= point.Value, "Saved attributes do not include purchased policy effects.");
            }
            simulation.TryChangeModel(finalModel); simulation.Paused = paused;
            return new RestoredRunState
            {
                Simulation = simulation, Economy = economy, Policies = policies, GenerationRandom = random,
                Story = ConversationStory.RestoreState(data.Story, simulation, storyConfiguration, generation, random, economy, previousStoryRevision, ordinaryDialoguePool, initialAffinity, successAffinity, failureAffinity, stageProvider),
                TaskBases = data.Host.TaskBases.ToDictionary(b => b.RequestId, b => b.Definition.Build(), StringComparer.Ordinal), Host = data.Host
            };
        }
    }
}
