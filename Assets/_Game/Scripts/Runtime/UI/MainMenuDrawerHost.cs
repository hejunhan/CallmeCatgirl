using UnityEngine;

namespace CallmeCatgirl.UI
{
    /// <summary>
    /// Opens the scene-authored main menu drawer through its Animator state machine and forwards
    /// the authored drawer buttons to the existing settings and scene navigation.
    /// </summary>
    public sealed class MainMenuDrawerHost : MonoBehaviour
    {
        private static readonly int OpenParameter = Animator.StringToHash("Open");

        [SerializeField] private Animator drawerAnimator;
        [SerializeField] private CanvasGroup drawerGroup;
        [SerializeField] private CallmeCatgirl.UI.SystemMenu.SystemMenuHost systemMenu;
        [SerializeField] private SceneNavigation navigation;

        private void Awake()
        {
            if (drawerAnimator != null && drawerGroup != null) return;
            Debug.LogError("MainMenuDrawerHost requires the authored drawer Animator and CanvasGroup.", this);
            enabled = false;
        }

        public void Open()
        {
            if (!enabled) return;
            drawerAnimator.gameObject.SetActive(true);
            drawerGroup.interactable = true;
            drawerGroup.blocksRaycasts = true;
            drawerAnimator.SetBool(OpenParameter, true);
        }

        public void Close()
        {
            if (!enabled) return;
            drawerGroup.interactable = false;
            drawerGroup.blocksRaycasts = false;
            drawerAnimator.SetBool(OpenParameter, false);
        }

        public void OpenSettings()
        {
            Close();
            if (systemMenu == null) { Debug.LogError("MainMenuDrawerHost is missing the SystemMenuHost reference.", this); return; }
            systemMenu.OpenSettings();
        }

        public void ReturnToMenu()
        {
            Close();
            if (navigation == null) { Debug.LogError("MainMenuDrawerHost is missing the SceneNavigation reference.", this); return; }
            navigation.ReturnToMenu();
        }
    }
}
