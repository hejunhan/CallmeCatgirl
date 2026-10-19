using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using System.IO;
using System.Linq;
using CallmeCatgirl.Gameplay;
using CallmeCatgirl.UI.SystemMenu;
using UnityEngine.InputSystem;

namespace CallmeCatgirl.UI
{
    /// <summary>Scene-owned navigation; a new game always creates a fresh session.</summary>
    public sealed class SceneNavigation : MonoBehaviour
    {
        public const string MenuPath = "Assets/_Game/Scenes/Gameplay/MenuScene.unity";
        public const string GameplayPath = "Assets/_Game/Scenes/Prototypes/CoreGameplay.unity";
        public bool IsTransitioning { get; private set; }
        [SerializeField] private Button continueButton,settingsButton;
        [SerializeField] private SystemMenuHost authoredMenuSettings;
        private static string pendingContinueSlot;
        private SystemMenuHost menuSettings;
        public static string ConsumeContinueSlot() { string value=pendingContinueSlot; pendingContinueSlot=null; return value; }
        public void ContinueGame() => ContinueSlot("main");
        public void ContinueSlot(string slot)
        {
            if(!File.Exists(Path.Combine(CoreGameplayController.SlotDirectory(slot),"run.json"))) return;
            pendingContinueSlot=slot; Load(GameplayPath);
        }
        private void Start()
        {
            if(gameObject.scene.path != MenuPath) return;
            CreateMenuSettings();
            if(continueButton==null) return;
            continueButton.interactable=File.Exists(Path.Combine(CoreGameplayController.SlotDirectory("main"),"run.json"));
            continueButton.onClick.AddListener(ContinueGame);
            var caption=continueButton.GetComponentInChildren<TMP_Text>();
            if(caption!=null)caption.color=continueButton.interactable?new Color32(37,39,41,255):new Color32(82,82,82,255);
        }
        private void Update()
        {
            if (gameObject.scene.path == MenuPath && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                menuSettings?.HandleEscape();
        }
        private void CreateMenuSettings()
        {
            var canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null) return;
            var font = Resources.FindObjectsOfTypeAll<Font>().FirstOrDefault(f => f.name.Contains("Harmony"))
                ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            menuSettings = SystemMenuHost.Create((RectTransform)canvas.transform, font, false, null, null, null);
            if(settingsButton==null)throw new System.InvalidOperationException("Save the main-menu settings button in the scene before Play.");
            settingsButton.onClick.RemoveAllListeners();settingsButton.onClick.AddListener(menuSettings.OpenSettings);
        }
#if UNITY_EDITOR
        public void PrepareSceneLayout(Font font)
        {
            var canvas=gameObject.scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Canvas>(true)).First();
            authoredMenuSettings=canvas.GetComponentInChildren<SystemMenuHost>(true);
            if(authoredMenuSettings==null){var host=new GameObject("SystemMenuHost",typeof(RectTransform),typeof(SystemMenuHost));host.transform.SetParent(canvas.transform,false);var h=(RectTransform)host.transform;h.anchorMin=Vector2.zero;h.anchorMax=Vector2.one;h.offsetMin=h.offsetMax=Vector2.zero;authoredMenuSettings=host.GetComponent<SystemMenuHost>();}
            authoredMenuSettings.PrepareSceneLayout(font);
            if(settingsButton!=null)return;
            var go = new GameObject("OpenSystemSettings", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(canvas.transform, false);
            var rect = (RectTransform)go.transform; rect.anchorMin = rect.anchorMax = new Vector2(1, 0);
            rect.pivot = new Vector2(1, 0); rect.anchoredPosition = new Vector2(-32, 28); rect.sizeDelta = new Vector2(180, 48);
            go.GetComponent<Image>().color = new Color32(232,232,232,255);
            settingsButton=go.GetComponent<Button>();
            var label = new GameObject("Label", typeof(RectTransform), typeof(Text)); label.transform.SetParent(go.transform, false);
            var text = label.GetComponent<Text>(); text.font = font; text.text = "系统设置"; text.fontSize = 22;
            text.alignment = TextAnchor.MiddleCenter; text.color = Color.black; text.raycastTarget = false;
            text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
        }
#endif
        public void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying=false;
#else
            Application.Quit();
#endif
        }

        public void StartNewGame() { pendingContinueSlot=null; Load(GameplayPath); }
        public void ReturnToMenu()
        {
            if (IsTransitioning) return;
            var game = FindFirstObjectByType<CoreGameplayController>();
            if (game != null && !game.SaveRun()) return;
            ReturnToMenuAfterSave();
        }
        public void ReturnToMenuAfterSave() => Load(MenuPath);

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
