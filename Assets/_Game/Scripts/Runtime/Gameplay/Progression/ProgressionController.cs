using System;
using UnityEngine;
using CallmeCatgirl.Gameplay.TaskGrid;

namespace CallmeCatgirl.Gameplay.Progression
{
    /// <summary>Add to the same object as CoreGameplayController. No scene edits or event draining required.</summary>
    [RequireComponent(typeof(CoreGameplayController))]
    public sealed class ProgressionController : MonoBehaviour
    {
        [SerializeField] private ProgressionConfiguration configuration;
        private CoreGameplayController owner;
        private TaskSimulation boundSimulation;
        public ProgressionTree Tree { get; private set; }
        public event Action Changed;
        public ProgressionNode[] Nodes => configuration == null ? ProgressionTree.CreateDefaultNodes() : configuration.Nodes;
        public void BindRestoredTree(TaskSimulation simulation, ProgressionTree tree)
        {
            if (owner == null) owner = GetComponent<CoreGameplayController>();
            if (simulation == null || tree == null || !ReferenceEquals(owner.Simulation,simulation)) throw new ArgumentException("Restored graph must belong to owner.");
            boundSimulation=simulation; Tree=tree; Tree.Changed += () => Changed?.Invoke();
        }
        public int Funds { get { EnsureBound(); return owner.Funds; } }
        public bool CanPurchase { get { EnsureBound(); return !owner.TrainingEnabled && !owner.GameEnded && !owner.IsModalOpen; } }

        private void Awake() => owner = GetComponent<CoreGameplayController>();
        private void LateUpdate() => EnsureBound();
        public void EnsureBound()
        {
            if (owner == null) owner = GetComponent<CoreGameplayController>();
            if (owner.Simulation == null || ReferenceEquals(boundSimulation, owner.Simulation)) return;
            boundSimulation = owner.Simulation;
            Tree = new ProgressionTree(configuration == null ? ProgressionTree.CreateDefaultNodes() : configuration.Nodes,
                boundSimulation, amount => owner.Economy.TrySpendFunds(amount));
            Tree.Changed += () => Changed?.Invoke();
            Changed?.Invoke();
        }

        public PurchaseResult TryPurchase(string id)
        {
            EnsureBound();
            if (Tree == null || !CanPurchase) return PurchaseResult.Busy;
            var result = Tree.TryPurchase(id);
            owner.SetMessage(result == PurchaseResult.Success ? "方案已解锁，后续能力产出立即提升。" : "无法解锁：" + result);
            return result;
        }

        public string SaveJson() { EnsureBound(); return JsonUtility.ToJson(Tree.Capture()); }
        public bool LoadJson(string json)
        {
            EnsureBound();
            if (Tree == null || string.IsNullOrWhiteSpace(json)) return false;
            try { return Tree.TryRestore(JsonUtility.FromJson<ProgressionState>(json)); }
            catch (ArgumentException) { return false; }
        }
    }
}
