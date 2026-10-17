using CallmeCatgirl.Gameplay;
using CallmeCatgirl.Gameplay.Progression;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace CallmeCatgirl.EditorTools
{
    public static class GameJamSceneIntegration
    {
        [InitializeOnLoadMethod]
        private static void ReconnectExistingService()
        {
            if (!Application.isBatchMode)
                EditorApplication.delayCall += ConnectExistingService;
        }

        private static async void ConnectExistingService()
        {
            var configuration = MCPForUnity.Editor.Services.EditorConfigurationCache.Instance;
            configuration.SetUseHttpTransport(true);
            configuration.SetHttpTransportScope("local");
            configuration.SetHttpBaseUrl("http://127.0.0.1:8765");
            if (!MCPForUnity.Editor.Services.MCPServiceLocator.Bridge.IsRunning)
                Debug.Log("Game Jam existing MCP bridge connected: " + await MCPForUnity.Editor.Services.MCPServiceLocator.Bridge.StartAsync());
        }

        public static async void ConnectAndOpen()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity");
            var configuration = MCPForUnity.Editor.Services.EditorConfigurationCache.Instance;
            configuration.SetUseHttpTransport(true);
            configuration.SetHttpTransportScope("local");
            configuration.SetHttpBaseUrl("http://127.0.0.1:8765");
            bool connected = await MCPForUnity.Editor.Services.MCPServiceLocator.Bridge.StartAsync();
            Debug.Log("Game Jam existing MCP bridge connected: " + connected);
        }

        public static void InstallAndPlay()
        {
            Install();
            EditorApplication.delayCall += () => EditorApplication.isPlaying = true;
        }

        [MenuItem("CallmeCatgirl/Integrate Progression Panel")]
        public static void Install()
        {
            var scene = EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity");
            var owner = Object.FindAnyObjectByType<CoreGameplayController>();
            var canvas = GameObject.Find("GameplayCanvas").GetComponent<Canvas>();
            var controller = owner.GetComponent<ProgressionController>() ?? owner.gameObject.AddComponent<ProgressionController>();
            var properties = new SerializedObject(controller);
            properties.FindProperty("configuration").objectReferenceValue = AssetDatabase.LoadAssetAtPath<ProgressionConfiguration>("Assets/_Game/Data/Progression/DefaultProgression.asset");
            properties.ApplyModifiedPropertiesWithoutUndo();
            if (canvas.transform.Find("ProgressionOverlay") != null) return;
            var font = AssetDatabase.LoadAssetAtPath<Font>("Assets/_Game/Art/Fonts/HarmonyOS_Sans/HarmonyOS_Sans_SC_Regular.ttf");
            var overlay = Rect("ProgressionOverlay", canvas.transform, new Vector2(1040, 410));
            overlay.gameObject.AddComponent<Image>().color = new Color(.035f, .045f, .055f, .99f);
            var body = Rect("ProgressionBody", overlay, new Vector2(1000, 340));
            body.anchoredPosition = new Vector2(0, -10);
            var panel = body.gameObject.AddComponent<ProgressionPanel>();
            properties = new SerializedObject(panel);
            properties.FindProperty("controller").objectReferenceValue = controller;
            properties.FindProperty("labelFont").objectReferenceValue = font;
            properties.ApplyModifiedPropertiesWithoutUndo();
            var close = Button("CloseProgression", overlay, "关闭 ×", font, new Vector2(130, 36));
            close.GetComponent<RectTransform>().anchoredPosition = new Vector2(435, 181);
            UnityEventTools.AddBoolPersistentListener(close.onClick, overlay.gameObject.SetActive, false);
            var open = Button("OpenProgression", canvas.transform, "政策 / 能力专精", font, new Vector2(170, 38));
            var entry = open.GetComponent<RectTransform>();
            entry.anchorMin = entry.anchorMax = new Vector2(1, 0);
            entry.anchoredPosition = new Vector2(-105, 25);
            UnityEventTools.AddBoolPersistentListener(open.onClick, overlay.gameObject.SetActive, true);
            overlay.SetAsLastSibling(); overlay.gameObject.SetActive(false);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("Game Jam progression panel integrated into CoreGameplay.");
        }

        [MenuItem("CallmeCatgirl/Integrate Planning Presentation")]
        public static void InstallPresentation()
        {
            var scene = EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity");
            if (GameObject.Find("PlanningEffectsCanvas") != null)
            {
                var existing = GameObject.Find("PlanningEffectsCanvas").transform.Find("DifferenceWipe");
                if (existing.GetComponent<CanvasRenderer>() == null) existing.gameObject.AddComponent<CanvasRenderer>();
                RebindPlanningPapers(GameObject.Find("PlanningEffectsCanvas").GetComponent<CallmeCatgirl.Presentation.PlanningPresentation>());
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
                return;
            }
            var root = new GameObject("PlanningEffectsCanvas", typeof(RectTransform));
            root.SetActive(false);
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = CallmeCatgirl.UI.UIWindowLayers.PauseEffectOrder;
            var graphicRect = Rect("DifferenceWipe", root.transform, Vector2.zero);
            graphicRect.anchorMin = Vector2.zero; graphicRect.anchorMax = Vector2.one;
            graphicRect.offsetMin = graphicRect.offsetMax = Vector2.zero;
            graphicRect.gameObject.AddComponent<CanvasRenderer>();
            var graphic = graphicRect.gameObject.AddComponent<CallmeCatgirl.Presentation.DifferenceWipeGraphic>();
            graphic.raycastTarget = false;
            var presentation = root.AddComponent<CallmeCatgirl.Presentation.PlanningPresentation>();
            var settings = new SerializedObject(presentation);
            settings.FindProperty("overlay").objectReferenceValue = graphic;
            settings.FindProperty("materialTemplate").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>("Assets/CallmeCatgirl/Presentation/Materials/DifferenceWipe.mat");
            var targets = new[] { "MemoryPanel", "CandidatePanel", "ConversationPanel", "LogArea" };
            var papers = settings.FindProperty("papers"); papers.arraySize = targets.Length;
            for (int i = 0; i < targets.Length; i++)
            {
                GameObject target = null;
                foreach (var candidate in Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (candidate.name == targets[i]) { target = candidate.gameObject; break; }
                if (target == null) throw new System.InvalidOperationException("Missing planning paper: " + targets[i]);
                papers.GetArrayElementAtIndex(i).objectReferenceValue = target.AddComponent<CallmeCatgirl.Presentation.PlanningPaper>();
            }
            settings.ApplyModifiedPropertiesWithoutUndo();
            var view = Object.FindAnyObjectByType<CallmeCatgirl.UI.CoreGameplayCanvasView>();
            settings = new SerializedObject(view);
            settings.FindProperty("planningPresentation").objectReferenceValue = presentation;
            settings.ApplyModifiedPropertiesWithoutUndo();
            root.SetActive(true);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            Debug.Log("Game Jam planning presentation integrated; board transform remains fixed.");
        }

        private static void RebindPlanningPapers(CallmeCatgirl.Presentation.PlanningPresentation presentation)
        {
            foreach (var paper in Object.FindObjectsByType<CallmeCatgirl.Presentation.PlanningPaper>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(paper);
            var targets = new[] { "MemoryPanel", "CandidatePanel", "ConversationPanel", "LogArea" };
            var settings = new SerializedObject(presentation);
            var papers = settings.FindProperty("papers"); papers.arraySize = targets.Length;
            for (int i = 0; i < targets.Length; i++)
                foreach (var candidate in Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (candidate.name == targets[i])
                    {
                        papers.GetArrayElementAtIndex(i).objectReferenceValue = candidate.gameObject.AddComponent<CallmeCatgirl.Presentation.PlanningPaper>();
                        break;
                    }
            settings.ApplyModifiedPropertiesWithoutUndo();
        }

        [MenuItem("CallmeCatgirl/Integrate Main Menu")]
        public static void InstallMainMenu()
        {
            if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Stop Play before scene integration.");
            var menu = EditorSceneManager.OpenScene(CallmeCatgirl.UI.SceneNavigation.MenuPath);
            var canvas = GameObject.Find("Canvas");
            var navigation = canvas.GetComponent<CallmeCatgirl.UI.SceneNavigation>() ?? canvas.AddComponent<CallmeCatgirl.UI.SceneNavigation>();
            var start = canvas.GetComponentInChildren<Button>(true);
            if (start == null) throw new System.InvalidOperationException("Menu has no Start button.");
            for (int i = start.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
                UnityEventTools.RemovePersistentListener(start.onClick, i);
            start.name = "StartNewGame";
            UnityEventTools.AddPersistentListener(start.onClick, navigation.StartNewGame);
            EditorSceneManager.MarkSceneDirty(menu); EditorSceneManager.SaveScene(menu);

            var game = EditorSceneManager.OpenScene(CallmeCatgirl.UI.SceneNavigation.GameplayPath);
            var gameplayCanvas = GameObject.Find("GameplayCanvas");
            navigation = gameplayCanvas.GetComponent<CallmeCatgirl.UI.SceneNavigation>() ?? gameplayCanvas.AddComponent<CallmeCatgirl.UI.SceneNavigation>();
            var existing = gameplayCanvas.transform.Find("ReturnToMenu");
            Button back;
            if (existing == null)
            {
                var font = AssetDatabase.LoadAssetAtPath<Font>("Assets/_Game/Art/Fonts/HarmonyOS_Sans/HarmonyOS_Sans_SC_Regular.ttf");
                back = Button("ReturnToMenu", gameplayCanvas.transform, "返回主菜单", font, new Vector2(150, 38));
                var rect = back.GetComponent<RectTransform>();
                rect.anchorMin = rect.anchorMax = new Vector2(0, 0); rect.anchoredPosition = new Vector2(95, 25);
            }
            else back = existing.GetComponent<Button>();
            for (int i = back.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
                UnityEventTools.RemovePersistentListener(back.onClick, i);
            UnityEventTools.AddPersistentListener(back.onClick, navigation.ReturnToMenu);
            EditorSceneManager.MarkSceneDirty(game); EditorSceneManager.SaveScene(game);

            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene(CallmeCatgirl.UI.SceneNavigation.MenuPath, true),
                new EditorBuildSettingsScene(CallmeCatgirl.UI.SceneNavigation.GameplayPath, true)
            };
            foreach (var item in EditorBuildSettings.scenes)
                if (item.path != CallmeCatgirl.UI.SceneNavigation.MenuPath && item.path != CallmeCatgirl.UI.SceneNavigation.GameplayPath)
                    scenes.Add(item);
            EditorBuildSettings.scenes = scenes.ToArray();
            EditorSceneManager.OpenScene(CallmeCatgirl.UI.SceneNavigation.MenuPath);
            Debug.Log("Main menu Start and Return wired; MenuScene is the startup scene.");
        }

        [MenuItem("CallmeCatgirl/Integrate Training")]
        public static void InstallTraining()
        {
            if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Stop Play first.");
            var scene = EditorSceneManager.OpenScene(CallmeCatgirl.UI.SceneNavigation.GameplayPath);
            var owner = Object.FindAnyObjectByType<CoreGameplayController>();
            var training = owner.GetComponent<CallmeCatgirl.Gameplay.Training.TrainingController>() ?? owner.gameObject.AddComponent<CallmeCatgirl.Gameplay.Training.TrainingController>();
            var panel = owner.GetComponent<CallmeCatgirl.Gameplay.Training.TrainingPanel>() ?? owner.gameObject.AddComponent<CallmeCatgirl.Gameplay.Training.TrainingPanel>();
            var settings = new SerializedObject(training);
            settings.FindProperty("configuration").objectReferenceValue = AssetDatabase.LoadAssetAtPath<CallmeCatgirl.Gameplay.Training.TrainingConfiguration>("Assets/_Game/Data/Training/TrainingDefaults.asset");
            settings.FindProperty("dialogueCorpus").objectReferenceValue = AssetDatabase.LoadAssetAtPath<CallmeCatgirl.Gameplay.Training.DialogueCorpusConfiguration>("Assets/_Game/Data/Training/DialogueCorpusTunableFallback.asset");
            settings.ApplyModifiedPropertiesWithoutUndo();
            settings = new SerializedObject(panel);
            settings.FindProperty("controller").objectReferenceValue = training;
            settings.FindProperty("showFallbackEntry").boolValue = false; settings.ApplyModifiedPropertiesWithoutUndo();
            settings = new SerializedObject(owner);
            settings.FindProperty("training").objectReferenceValue = training;
            settings.FindProperty("trainingPanel").objectReferenceValue = panel; settings.ApplyModifiedPropertiesWithoutUndo();
            var canvas = GameObject.Find("GameplayCanvas");
            var existing = canvas.transform.Find("OpenTraining");
            var button = existing == null ? Button("OpenTraining", canvas.transform, "服务器 / 训练模型", AssetDatabase.LoadAssetAtPath<Font>("Assets/_Game/Art/Fonts/HarmonyOS_Sans/HarmonyOS_Sans_SC_Regular.ttf"), new Vector2(180, 38)) : existing.GetComponent<Button>();
            var rect = button.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = Vector2.zero; rect.anchoredPosition = new Vector2(285, 25);
            for (int i = button.onClick.GetPersistentEventCount() - 1; i >= 0; i--) UnityEventTools.RemovePersistentListener(button.onClick, i);
            UnityEventTools.AddPersistentListener(button.onClick, owner.OpenTraining);
            var legacy = canvas.transform.Find("OpenProgression"); if (legacy != null) legacy.gameObject.SetActive(false);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            Debug.Log("Training scene integration installed; legacy direct attribute purchase disabled.");
        }

        [MenuItem("CallmeCatgirl/Integrate Opening Balance")]
        public static void InstallOpeningBalance()
        {
            const string path = "Assets/_Game/Data/TaskGrid/OpeningBalance.asset";
            var balance = AssetDatabase.LoadAssetAtPath<OpeningBalanceConfiguration>(path);
            if (balance == null) { balance = ScriptableObject.CreateInstance<OpeningBalanceConfiguration>(); AssetDatabase.CreateAsset(balance, path); }
            var config = AssetDatabase.LoadAssetAtPath<GameplayConfiguration>("Assets/_Game/Data/TaskGrid/CoreGameplayConfiguration.asset");
            config.OpeningBalance = balance; EditorUtility.SetDirty(config); AssetDatabase.SaveAssets();
            Debug.Log("Opening balance attached; original story and model attributes unchanged.");
        }

        private static RectTransform Rect(string name, Transform parent, Vector2 size)
        {
            var result = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            result.SetParent(parent, false); result.anchorMin = result.anchorMax = result.pivot = new Vector2(.5f, .5f);
            result.sizeDelta = size; return result;
        }
        private static Button Button(string name, Transform parent, string title, Font font, Vector2 size)
        {
            var rect = Rect(name, parent, size);
            rect.gameObject.AddComponent<Image>().color = new Color(.16f, .23f, .25f);
            var button = rect.gameObject.AddComponent<Button>();
            var label = Rect("Label", rect, size).gameObject.AddComponent<Text>();
            label.font = font; label.fontSize = 17; label.color = Color.white;
            label.alignment = TextAnchor.MiddleCenter; label.text = title; label.raycastTarget = false;
            return button;
        }
    }
}
