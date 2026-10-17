using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using System.IO;
using CallmeCatgirl.Gameplay;

namespace CallmeCatgirl.UI
{
    /// <summary>Scene-owned navigation; a new game always creates a fresh session.</summary>
    public sealed class SceneNavigation : MonoBehaviour
    {
        public const string MenuPath = "Assets/_Game/Scenes/Gameplay/MenuScene.unity";
        public const string GameplayPath = "Assets/_Game/Scenes/Prototypes/CoreGameplay.unity";
        public bool IsTransitioning { get; private set; }
        [SerializeField] private Button continueButton;
        private static string pendingContinueSlot;
        public static string ConsumeContinueSlot() { string value=pendingContinueSlot; pendingContinueSlot=null; return value; }
        public void ContinueGame() => ContinueSlot("main");
        public void ContinueSlot(string slot)
        {
            if(!File.Exists(Path.Combine(CoreGameplayController.SlotDirectory(slot),"run.json"))) return;
            pendingContinueSlot=slot; Load(GameplayPath);
        }
        private void Start()
        {
            if(gameObject.scene.path != MenuPath || continueButton==null) return;
            continueButton.interactable=File.Exists(Path.Combine(CoreGameplayController.SlotDirectory("main"),"run.json"));
            continueButton.onClick.AddListener(ContinueGame);
            var caption=continueButton.GetComponentInChildren<TMP_Text>();
            if(caption!=null)caption.color=continueButton.interactable?new Color32(37,39,41,255):new Color32(82,82,82,255);
        }
        public void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying=false;
#else
            Application.Quit();
#endif
        }

        public void StartNewGame() { pendingContinueSlot=null; Load(GameplayPath); }
        public void ReturnToMenu() => Load(MenuPath);

        private void Load(string path)
        {
            if (IsTransitioning) return;
            if (!Application.CanStreamedLevelBeLoaded(path))
            {
                Debug.LogError("Scene is not enabled in Build Settings: " + path, this);
                return;
            }
            IsTransitioning = true;
            // Single unloads the previous controller, wallet, EventSystem and UI.
            // No session object is static or marked DontDestroyOnLoad.
            if (SceneManager.LoadSceneAsync(path, LoadSceneMode.Single) == null)
                IsTransitioning = false;
        }
    }
}
