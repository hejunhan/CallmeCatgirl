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
            if (Tree != null) Tree.Changed -= NotifyChanged;
            boundSimulation=simulation; Tree=tree; Tree.Changed += NotifyChanged;
            Tree.MemoryLevelProvider = () => owner.MemoryLevel;
        }
        public int Funds { get { EnsureBound(); return owner.Funds; } }
        public bool CanPurchase { get { EnsureBound(); return owner.CanPurchasePolicy; } }

        private void Awake() => owner = GetComponent<CoreGameplayController>();
        private void OnDestroy() { if (Tree != null) Tree.Changed -= NotifyChanged; }
        private void NotifyChanged() => Changed?.Invoke();
        private void LateUpdate() => EnsureBound();
        public void EnsureBound()
        {
            if (owner == null) owner = GetComponent<CoreGameplayController>();
            if (owner.Simulation == null || ReferenceEquals(boundSimulation, owner.Simulation)) return;
            if (Tree != null) Tree.Changed -= NotifyChanged;
            boundSimulation = owner.Simulation;
            Tree = new ProgressionTree(configuration == null ? ProgressionTree.CreateDefaultNodes() : configuration.Nodes,
                boundSimulation, amount => owner.Economy.TrySpendFunds(amount));
            Tree.MemoryLevelProvider = () => owner.MemoryLevel;
            Tree.Changed += NotifyChanged;
            Changed?.Invoke();
        }

        public PurchaseResult TryPurchase(string id)
        {
            EnsureBound();
            if (Tree == null || !CanPurchase) return PurchaseResult.Busy;
            var result = Tree.TryPurchase(id);
            owner.SetMessage(result == PurchaseResult.Success ? "政策已生效。数值效果与声望已更新。" : "无法购买：" + PurchaseResultText(result));
            return result;
        }

        public static string PurchaseResultText(PurchaseResult result)
        {
            switch (result) {
                case PurchaseResult.Success: return "可购买";
                case PurchaseResult.AlreadyUnlocked: return "已购买";
                case PurchaseResult.MissingPrerequisite: return "需先购买连线前置";
                case PurchaseResult.MemoryLocked: return "内存等级不足";
                case PurchaseResult.InsufficientFunds: return "金币不足";
                case PurchaseResult.ExcludedByChoice: return "互斥路线已选择";
                case PurchaseResult.Busy: return "当前不可购买";
                default: return "未知节点";
            }
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
