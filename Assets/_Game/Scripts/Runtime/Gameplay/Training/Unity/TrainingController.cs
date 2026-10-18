using System;
using System.Collections.Generic;
using UnityEngine;
using CallmeCatgirl.Gameplay.TaskGrid;

namespace CallmeCatgirl.Gameplay.Training
{
    /// <summary>Explicitly bound by the one gameplay owner. No Update, second clock, event drain, or shared controller edits.</summary>
    public sealed class TrainingController : MonoBehaviour
    {
        [SerializeField] private TrainingConfiguration configuration;
        [SerializeField] private DialogueCorpusConfiguration dialogueCorpus;
        private Func<bool> canManage;
        public TaskSimulation Simulation { get; private set; }
        public TrainingSession Session { get; private set; }
        public string Message { get; private set; } = "尚未绑定训练系统。";
        public bool IsBound => Session != null;
        public TrainingConfiguration Configuration => configuration;
        public bool CanManage => IsBound && (canManage == null || canManage());
        public event Action Changed;
        public void Bind(TaskSimulation simulation, EconomyManager wallet, Func<ExecutionProfile, bool> apply,
            Func<double, bool> updateRate, Func<IEnumerable<Cell>> capacity, Func<bool> canManage,
            Func<ModelDefinition, ModelDefinition> compose = null, Func<PolicyModifiers> policies = null,
            ModelDefinition initialBaseDefinition = null, Func<int> currentStage = null, ModelIdentityRegistry identities = null)
        {
            if (configuration == null) throw new InvalidOperationException("Training configuration is required.");
            if (Session != null) Session.Changed -= ForwardChanged;
            Simulation = simulation; this.canManage = canManage;
            Session = configuration.Build(simulation, wallet, apply, updateRate, capacity, compose, policies, initialBaseDefinition, currentStage, identities);
            Session.Changed += ForwardChanged; Message = "语料决定能力；算力与架构决定每任务 token/s。"; ForwardChanged();
        }
        private void ForwardChanged() => Changed?.Invoke();
        public void BindRestoredSession(TaskSimulation simulation,TrainingSession restored,Func<bool> canManage)
        {
            if (restored==null || !ReferenceEquals(restored.Simulation,simulation)) throw new ArgumentException("Restored training must use the committed simulation.");
            if (Session!=null)Session.Changed-=ForwardChanged;
            Simulation=simulation;Session=restored;this.canManage=canManage;
            Session.Changed+=ForwardChanged;Message="训练状态已恢复。";ForwardChanged();
        }
        public bool IsTrainingRequest(string id) => IsBound && Session.IsTrainingRequest(id);
        public void ObserveTaskEvent(TaskEvent item)
        {
            // Forwarded AFTER the owner routes its single event batch; never settles it here.
            if (item != null && IsTrainingRequest(item.RequestId)) ForwardChanged();
        }
        public TrainingResult RecordAcceptedReply(string requestId, string stableNodeId, string stableChoiceId,
            IDictionary<AbilityKind, double> explicitStoryScores = null)
        {
            if (!IsBound || !Simulation.Tasks.TryGetValue(requestId, out var task)) return TrainingResult.NotSubmitted;
            IDictionary<AbilityKind, double> scores = explicitStoryScores; string source = "story-explicit";
            if (scores == null && (dialogueCorpus == null || !dialogueCorpus.TryResolve(task, stableNodeId, stableChoiceId,
                null, out scores, out source))) { Message = "此对话未配置语料分数。"; return TrainingResult.InvalidInput; }
            return Report(Session.RecordDialogue(requestId, stableNodeId, stableChoiceId, scores, source));
        }
        public TrainingResult StartTraining(string recipe, string architecture, string name, IEnumerable<string> ids)
        {
            if (!CanManage) return Report(TrainingResult.Busy);
            return Report(Session.StartTraining(recipe, architecture, name, ids, out _));
        }
        public TrainingResult StartConversationTraining(string recipe, string architecture, string name, string conversationId, double points)
            => Report(CanManage ? Session.StartConversationTraining(recipe, architecture, name, conversationId, points, out _) : TrainingResult.Busy);
        public TrainingResult StartConversationsTraining(string recipe, string architecture, string name, IEnumerable<ConversationInvestment> investments)
            => Report(CanManage ? Session.StartConversationsTraining(recipe,architecture,name,investments,out _) : TrainingResult.Busy);
        public TrainingResult CompleteTraining(string id) => Report(CanManage ? Session.CompleteTraining(id) : TrainingResult.Busy);
        public TrainingResult SelectModel(string id) => Report(CanManage ? Session.SelectModel(id) : TrainingResult.Busy);
        public TrainingResult RenameModel(string id, string displayName) => Report(CanManage ? Session.RenameModel(id, displayName) : TrainingResult.Busy);
        public TrainingResult UpgradeCompute() => Report(CanManage ? Session.UpgradeCompute() : TrainingResult.Busy);
        private TrainingResult Report(TrainingResult result)
        {
            Message = result == TrainingResult.Success ? "操作完成。" : "未执行：" + result; ForwardChanged(); return result;
        }
    }
}
