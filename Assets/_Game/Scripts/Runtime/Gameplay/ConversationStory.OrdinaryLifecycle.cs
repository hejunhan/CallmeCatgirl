using CallmeCatgirl.Gameplay.TaskGrid;
using CallmeCatgirl.Persistence;

namespace CallmeCatgirl.Gameplay
{
    public sealed partial class ConversationStory
    {
        public System.Func<double> OrdinaryEndProbabilityMultiplier { get; set; }
        private OrdinaryConversationLifecycle ordinaryLifecycle;
        private OrdinaryConversationLifecycle OrdinaryLifecycle => ordinaryLifecycle
            ?? (ordinaryLifecycle = new OrdinaryConversationLifecycle(configuration?.OrdinaryLifecycle ?? new OrdinaryConversationPolicy()));
        private bool IsScriptedConversation(string id)
            => User(id).Profile?.Kind == StoryConfiguration.ConversationKind.Scripted;
        public StoryConfiguration.ConversationKind Kind(string id)
            => User(id).Profile?.Kind ?? StoryConfiguration.ConversationKind.Ordinary;

        /// <summary>Called by Submitted after actual settlement, once per request; no corpus mutation.</summary>
        public bool ObserveRequestSettlement(TaskInstance task)
            => OrdinaryLifecycle.ObserveSettlement(task, task != null && IsScriptedConversation(task.ConversationId), shapeRandom,
                OrdinaryEndProbabilityMultiplier?.Invoke() ?? 1);

        /// <summary>Detached query copy. Changing it cannot alter the running conversation.</summary>
        public OrdinaryConversationState GetOrdinaryConversation(string conversationId) => OrdinaryLifecycle.Get(conversationId);

        // Captured source IDs bind restored choices back to the exact authored instance/index.
        // Equal-looking authored choices may have different corpus scores, so value matching alone is insufficient.
        private StoryConfiguration.ReplyChoice RestoreCorpusChoice(string sourceId, ReplyChoiceState saved)
        {
            StoryConfiguration.ReplyChoice selected = null;
            var nodeChoices = User(UserId).Profile?.FindNode(User(UserId).NodeId)?.Choices;
            if (sourceId != null && sourceId.StartsWith("choice-") && int.TryParse(sourceId.Substring(7), out int nodeIndex)
                && nodeChoices != null && nodeIndex >= 0 && nodeIndex < nodeChoices.Length)
                selected = nodeChoices[nodeIndex];
            else if (sourceId != null && sourceId.StartsWith("default-") && int.TryParse(sourceId.Substring(8), out int defaultIndex)
                && (nodeChoices == null || nodeChoices.Length == 0) && configuration?.DefaultChoices != null
                && defaultIndex >= 0 && defaultIndex < configuration.DefaultChoices.Length)
                selected = configuration.DefaultChoices[defaultIndex];
            else if (sourceId == "__automatic__")
            {
                var automatic = new StoryConfiguration.ReplyChoice { NextNodeId = User(UserId).Profile?.SequentialNext(User(UserId).NodeId) };
                if (SameChoice(CaptureChoice(automatic), saved)) selected = automatic;
            }
            StateRules.Require(selected != null && SameChoice(CaptureChoice(selected), saved), "Saved corpus choice identity differs from story content.");
            return selected;
        }
    }
}
