using UnityEngine;

namespace CallmeCatgirl.UI
{
    /// <summary>Shows the debug-hotkey hint in demo builds as well as Editor.</summary>
    public sealed class DebugHotkeyHint : MonoBehaviour
    {
        private void Awake()
        {
            gameObject.SetActive(true);
        }
    }
}
