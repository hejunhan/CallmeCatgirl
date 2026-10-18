using System;
using System.Collections.Generic;
using System.Linq;
using CallmeCatgirl.Gameplay.Narrative;
using CallmeCatgirl.Gameplay.Progression;
using CallmeCatgirl.Gameplay.TaskGrid;
using CallmeCatgirl.Gameplay.Training;
using CallmeCatgirl.Persistence;
using CallmeCatgirl.UI;
using CallmeCatgirl.UI.SystemMenu;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CallmeCatgirl.Gameplay
{
    /// <summary>Only integration effects live here; each feature retains its own state and ledger.</summary>
    public sealed partial class CoreGameplayController
    {
        private const string NarrativeExtensionId = "callmecatgirl.narrative";
        public NarrativeSession Narrative { get; private set; }
        public string EndReason { get; private set; } = string.Empty;
        private NarrativePanel narrativePanel;
        private SystemMenuHost systemMenu;
        private StateSaveExtension<NarrativeSaveData> narrativeSave;
        private IRunSaveExtension[] runExtensions;
        private bool processingCompletion;
        private double appliedPolicySpeed = -1;
        private bool policyViewPrepared;
        private bool narrativeViewRegistered;
        private readonly MemoryUpgradeConfiguration.UpgradeStep firstMemoryInstallation = new MemoryUpgradeConfiguration.UpgradeStep
            { TargetLevel = 1, Cost = 100, RequiredCompletedTasks = 2, AbilityPoints = 0, ModelName = "Transformer" };
        public bool HasSystemPauseClaim => systemMenu != null && systemMenu.IsPauseClaimed;
        public bool IsNarrativeContact(string conversationId) => Narrative != null
            && Narrative.PinnedContacts.Any(c => c.Id == conversationId);
        public string ConversationDisplayName(string conversationId)
        {
            var contact = Narrative?.PinnedContacts.FirstOrDefault(c => c.Id == conversationId);
            if (contact != null) return contact.Name;
            if (conversationId == "policy-book-scan") return "绝版书籍扫描";
            var reward = TrainingEnabled ? training.Session.Corpus.FirstOrDefault(r => r.ConversationId == conversationId
                && r.SourceKind == CorpusSourceKind.ScriptedReward) : null;
            if (reward != null && !Simulation.Tasks.Values.Any(t => t.ConversationId == conversationId)) return reward.ScoreSource;
            return Story.Name(conversationId);
        }
        public bool OpenNarrativeContact(string conversationId)
        {
            if (!IsNarrativeContact(conversationId) || GameEnded || IsModalOpen) return false;
            if (!Narrative.OpenContact(conversationId)) return false;
            canvasView?.CancelDrag(); canvasView?.Refresh(); return true;
        }
        private bool CompletionBlocksInput => (systemMenu != null && systemMenu.IsBlockingInput)
            || (Narrative != null && Narrative.RequiresPause);

        private ProgressionTree PolicyTree
        {
            get
            {
                var component = GetComponent<ProgressionController>();
                if (component == null) return null;
                component.EnsureBound(); return component.Tree;
            }
        }

        private static PolicyModifiers TrainingModifiers(ProgressionTree tree)
        {
            var effects = tree?.Effects;
            return effects == null ? PolicyModifiers.Neutral : new PolicyModifiers(effects.InferenceSpeedMultiplier,
                effects.TrainingScoreMultiplier, effects.TrainingCostMultiplier, effects.TrainingWorkMultiplier);
        }

        private static ModelDefinition ComposePolicyAbilities(ModelDefinition model, ProgressionTree tree)
        {
            if (tree == null) return model;
            var points = model.Points.ToDictionary(p => p.Key,p => p.Value);
            foreach (var node in tree.Nodes.Where(n => n.Kind == PolicyNodeKind.Ability && tree.IsUnlocked(n.Id)))
            {
                points.TryGetValue(node.Ability,out var value); points[node.Ability] = value + node.PointsPerTick;
            }
            return new ModelDefinition(model.Id,model.Interval,points);
        }

        private Cell CenterMemoryCell => new Cell((Simulation.Width - 1) / 2, (Simulation.Height - 1) / 2);
        private IEnumerable<Cell> CapacityForNarrative(NarrativeSession story, int level)
        {
            if (story != null && story.Tutorial != TutorialStep.Legacy && level == 0)
                return !story.CenterCellRevealed ? Array.Empty<Cell>() : new[] { CenterMemoryCell };
            return configuration.MemoryUpgrades.GetOpenCells(level);
        }

        private void BeginCompletionRun()
        {
            Narrative = NarrativeSession.CreateNew(); EndReason = string.Empty; appliedPolicySpeed = -1;
            systemMenu?.CloseAll();
            if (narrativePanel != null) narrativePanel.Bind(Narrative, CompletionFont());
        }

        private Font CompletionFont() => canvasView == null ? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
            : canvasView.GetComponentsInChildren<Text>(true).Select(t => t.font).FirstOrDefault(f => f != null)
                ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        private void EnsureCompletionViews()
        {
            if (canvasView == null || Narrative == null) return;
            var parent = canvasView.WindowLayers?.WindowLayer ?? (RectTransform)canvasView.transform;
            if (narrativePanel == null) narrativePanel = NarrativePanel.Create(parent, CompletionFont(), Narrative);
            if (!narrativeViewRegistered && canvasView.WindowLayers != null)
            {
                // The always-alive component is not a modal. Its visible screen blocker is.
                var overlay = narrativePanel.transform.Find("ScreenBlocker") as RectTransform;
                var surface = overlay == null ? null : overlay.Find("StoryWindow") as RectTransform;
                if (overlay != null && surface != null)
                {
                    canvasView.WindowLayers.Register(overlay.gameObject,surface,UIWindowKind.Ending);
                    narrativeViewRegistered = true;
                }
            }
            if (systemMenu == null)
                systemMenu = SystemMenuHost.Create((RectTransform)canvasView.transform, CompletionFont(), true,
                    _ => { }, ConsumeEscapeBeforeSystemMenu, SaveAndReturnFromSystemMenu);
            if (!policyViewPrepared)
            {
                var policyPanel = canvasView.GetComponentInChildren<ProgressionPanel>(true);
                var overlay = canvasView.GetComponentsInChildren<RectTransform>(true).FirstOrDefault(r => r.name == "ProgressionOverlay");
                if (policyPanel != null && overlay != null)
                {
                    var surface = (RectTransform)policyPanel.transform;
                    surface.SetParent(overlay, false);
                    surface.anchorMin = Vector2.zero; surface.anchorMax = Vector2.one;
                    surface.offsetMin = new Vector2(24,24); surface.offsetMax = new Vector2(-24,-24);
                    foreach (Transform child in overlay) if (child != surface) child.gameObject.SetActive(false);
                    policyPanel.Bind(GetComponent<ProgressionController>(), CompletionFont());
                    policyPanel.Closed += () => overlay.gameObject.SetActive(false);
                    canvasView.WindowLayers?.Register(overlay.gameObject,surface,UIWindowKind.ModalPage);
                    var entry = canvasView.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == "OpenProgression");
                    if (entry != null)
                    {
                        entry.gameObject.SetActive(true);
                        // Replace the old serialized SetActive listener with the normal host gate.
                        entry.onClick = new Button.ButtonClickedEvent();
                        entry.onClick.AddListener(OpenPolicyTree);
                        var label = entry.GetComponentInChildren<Text>(true);
                        if (label != null) { label.text = "政策"; label.alignment = TextAnchor.MiddleCenter; }
                        ((RectTransform)entry.transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,44);
                    }
                    policyViewPrepared = true;
                }
            }
        }

        public void OpenPolicyTree()
        {
            if (GameEnded || CompletionBlocksInput || IsMemoryInstallPending || IsMemoryInstallOpen || IsMemoryUpgradeOpen || IsTrainingOpen) return;
            var overlay = canvasView == null ? null : canvasView.GetComponentsInChildren<RectTransform>(true)
                .FirstOrDefault(r => r.name == "ProgressionOverlay");
            var panel = overlay == null ? null : overlay.GetComponentInChildren<ProgressionPanel>(true);
            if (overlay == null || panel == null) { Message = "政策界面暂不可用。"; return; }
            panel.gameObject.SetActive(true); overlay.gameObject.SetActive(true);
            canvasView.CancelDrag(); canvasView.WindowLayers?.Synchronize(); canvasView.Refresh();
        }

        private MenuSaveResult SaveAndReturnFromSystemMenu()
        {
            if (!SaveRun()) return MenuSaveResult.Failure(Message);
            var navigation = GetComponent<SceneNavigation>() ?? FindFirstObjectByType<SceneNavigation>();
            if (navigation == null) return MenuSaveResult.Failure("没有找到主菜单导航，游戏已保存。");
            navigation.ReturnToMenuAfterSave(); return MenuSaveResult.Success;
        }

        private bool ConsumeEscapeBeforeSystemMenu()
        {
            var selected = EventSystem.current?.currentSelectedGameObject;
            var input = selected == null ? null : selected.GetComponent<InputField>();
            var tmpInput = selected == null ? null : selected.GetComponent<TMPro.TMP_InputField>();
            if (input != null && input.isFocused) { input.DeactivateInputField(); EventSystem.current.SetSelectedGameObject(null); return true; }
            if (tmpInput != null && tmpInput.isFocused) { tmpInput.DeactivateInputField(); EventSystem.current.SetSelectedGameObject(null); return true; }
            if (canvasView != null && canvasView.IsDragging) { canvasView.CancelDrag(); return true; }
            if (IsMemoryInstallPending || IsMemoryInstallOpen) { CancelPendingMemoryInstall(); return true; }
            if (IsMemoryUpgradeOpen) { CloseMemoryUpgrade(); return true; }
            if (IsTrainingOpen) { trainingPanel.Close(); return true; }
            var policyPanel = canvasView == null ? null : canvasView.GetComponentInChildren<ProgressionPanel>();
            if (policyPanel != null && policyPanel.gameObject.activeInHierarchy) { policyPanel.Close(); return true; }
            if (Narrative?.RequiresPause == true) { narrativePanel?.HandleEscape(); return true; }
            if (canvasView != null && canvasView.IsConversationVisible) { canvasView.DismissConversation(); return true; }
            return false;
        }

        private void ProcessCompletion()
        {
            if (processingCompletion || Narrative == null || !TrainingEnabled) return;
            processingCompletion = true;
            try
            {
                EnsureCompletionViews();
                var tree = PolicyTree;
                if (tree != null)
                {
                    tree.BestAvailableCorpusPoints = () => training.Session.BestAvailableCorpusPoints;
                    var effects = tree.Effects;
                    Story.OrdinaryEndProbabilityMultiplier = () => PolicyTree?.Effects.EndProbabilityMultiplier ?? 1;
                    if (effects.InferenceSpeedMultiplier != appliedPolicySpeed)
                    {
                        bool paused = Simulation.Paused;
                        try { Simulation.Paused = true; training.Session.RefreshPolicyThroughput(); }
                        finally { Simulation.Paused = paused; }
                        appliedPolicySpeed = effects.InferenceSpeedMultiplier;
                    }
                    foreach (var reward in tree.PendingCorpusRewards)
                    {
                        bool accepted = true;
                        for (int i = 0; i < 3; i++)
                        {
                            var result = training.Session.GrantScriptedCorpus(reward.EventId + ":" + i, reward.ConversationId,
                                new Dictionary<AbilityKind, double> { { AbilityKind.Writing, reward.FrozenPoints } }, "绝版书籍扫描");
                            if (result != TrainingResult.Success && result != TrainingResult.AlreadyRecorded) accepted = false;
                        }
                        if (accepted) tree.AcknowledgeCorpusReward(reward.EventId);
                    }
                    if (effects.EmotionOpportunityUnlocked) Narrative.UnlockCompanionOpportunity();
                    var terminal = tree.PendingTerminal;
                    if (terminal != null)
                    {
                        Narrative.RequestPolicyEnding(terminal.Reputation, ProgressionTree.BadReputationThreshold, ProgressionTree.GoodReputationThreshold);
                        if (Narrative.EndingRequested) tree.AcknowledgeTerminal();
                    }
                    if (effects.AutoPlacement && !GameEnded && !IsModalOpen) AutoPlaceCandidates();
                }
                Narrative.ObserveUpgradeFunds(Funds, NextMemoryUpgrade?.Cost ?? 0);
                foreach (var effect in Narrative.PendingEffects)
                    if (ApplyNarrativeEffect(effect)) Narrative.AcknowledgeEffect(effect.EventId);
            }
            finally { processingCompletion = false; }
        }

        private bool ApplyNarrativeEffect(NarrativeEffect effect)
        {
            switch (effect.Kind)
            {
                case NarrativeEffectKind.GrantCorpus:
                    var ability = effect.CorpusDomain == NarrativeCorpusDomain.Writing ? AbilityKind.Writing : AbilityKind.Reasoning;
                    var result = training.Session.GrantScriptedCorpus(effect.EventId, effect.ConversationId,
                        new Dictionary<AbilityKind, double> { { ability, effect.CorpusPoints } }, effect.Title);
                    return result == TrainingResult.Success || result == TrainingResult.AlreadyRecorded;
                case NarrativeEffectKind.RevealCenterMemory:
                    if (!Simulation.TryUnlockCells(new[] { CenterMemoryCell })) return false;
                    Narrative.ObserveCenterCellRevealed(); Message = "中心运算格已开放。打开训练，投入刚得到的 1 点语料。";
                    return true;
                case NarrativeEffectKind.QueueDoctorRequest:
                case NarrativeEffectKind.QueueOrdinaryRequest:
                case NarrativeEffectKind.QueueStageDoctorRequest:
                    return CreateNarrativeRequest(effect);
                case NarrativeEffectKind.ShowUpgradeHint:
                case NarrativeEffectKind.IntroduceArchitecture:
                    Message = effect.Title + "：" + effect.Description; return true;
                case NarrativeEffectKind.EndRun:
                    string reason = NarrativeSession.HostEndReason(effect.EndingReason);
                    if (!RunEndReasons.IsNarrative(reason)) return false;
                    if (GameEnded) return EndReason == reason;
                    EndReason = reason; EndingText = effect.Title + "\n" + effect.Description;
                    GameEnded = true; Simulation.Paused = true; return true;
                default: return false;
            }
        }

        private bool CreateNarrativeRequest(NarrativeEffect effect)
        {
            if (Simulation.Tasks.ContainsKey(effect.RequestId)) return true;
            if (Simulation.Tasks.Values.Any(t => t.ConversationId == effect.ConversationId && t.State != RequestState.Closed)) return false;
            var best = Simulation.Model.Points.OrderByDescending(p => p.Value).FirstOrDefault();
            var requirement = new AbilityRequirement(best.Key, Math.Max(0.1, best.Value), Math.Max(0.2, best.Value * 2));
            var shape = Enumerable.Range(0, Math.Max(1, effect.RequiredCells)).Select(x => new Cell(x, 0)).ToArray();
            var definition = new TaskDefinition(effect.RequestId + ":definition", effect.Title, shape,
                20, 1, 600, 60, new[] { requirement },
                usesCalculationBudget: effect.Kind != NarrativeEffectKind.QueueOrdinaryRequest);
            if (!Simulation.TryCreate(effect.RequestId, effect.ConversationId, effect.RequestId, definition, out var task)) return false;
            taskBases.Add(task.Id, definition);
            if (effect.Kind != NarrativeEffectKind.QueueOrdinaryRequest) operatingTasks.Add(task.Id);
            SelectedId = task.Id; return true;
        }

        private void ObserveCompletionTask(TaskEvent item)
        {
            if (Narrative == null || item.Kind != TaskEventKind.Submitted) return;
            if (TrainingEnabled && training.IsTrainingRequest(item.RequestId))
            {
                if (training.Session.TryGetJob(item.RequestId, out var job) && job.Completed
                    && Narrative.ObserveTrainingCompleted(item.RequestId))
                {
                    bool paused = Simulation.Paused;
                    try { Simulation.Paused = true; training.Session.SelectModel(job.Output.Id); }
                    finally { Simulation.Paused = paused; }
                }
            }
            else if (item.Result?.Overall >= Quality.Complete) Narrative.ObserveRequestSettled(item.RequestId);
        }

        private void AutoPlaceCandidates()
        {
            foreach (var task in Simulation.Tasks.Values.Where(t => t.State == RequestState.Waiting || t.State == RequestState.Withdrawn)
                .OrderByDescending(t => training.IsTrainingRequest(t.Id)).Take(16).ToArray())
            {
                bool placed = false;
                for (int rotation = 0; rotation < 4 && !placed; rotation++)
                    for (int y = 0; y < Simulation.Height && !placed; y++)
                        for (int x = 0; x < Simulation.Width && !placed; x++)
                            placed = Simulation.TryPlace(task.Id, new Cell(x, y), rotation, out _);
            }
        }
    }
}
