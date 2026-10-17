using UnityEngine;

namespace CallmeCatgirl.Gameplay.Progression
{
    [CreateAssetMenu(menuName = "CallmeCatgirl/Progression Tree")]
    public sealed class ProgressionConfiguration : ScriptableObject
    {
        public ProgressionNode[] Nodes = ProgressionTree.CreateDefaultNodes();
    }
}
