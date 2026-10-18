using System;
using System.Linq;
using CallmeCatgirl.Gameplay;
using CallmeCatgirl.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace CallmeCatgirl.EditorTools
{
    public static class MemoryInstallIntegration
    {
        private const string Core = "Assets/_Game/Scenes/Prototypes/CoreGameplay.unity";
        private const string Standalone = "Assets/_Game/Scenes/Prototypes/MemoryInstallScene.unity";
        private const string Prefab = "Assets/_Game/Prefabs/UI/CoreGameplay/MemoryInstallGamePanel.prefab";
        [MenuItem("CallmeCatgirl/Integrate Memory Installation")]
        public static void Bake()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play first.");
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.OpenScene(Core);
            if (scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Any(t => t.name == "MemoryInstallPopup"))
            { Debug.Log("Memory installation already lives in the scene; edit its objects directly."); return; }
            scene = EditorSceneManager.OpenScene(Standalone);
            var source = Object.FindFirstObjectByType<StandaloneMemoryInstall>();
            var window = (RectTransform)source.transform;
            var scaler=source.GetComponentInParent<CanvasScaler>();
            scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution=new Vector2(1600,1000);scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
            Place(window, 0, 0, 1080, 800);
            window.GetComponent<Image>().color = Color.white;
            Place(window.Find("背景图"), 0, 0, 1080, 800);
            Place(window.Find("插槽底座"), 0, -8, 720, 256);
            Place(window.Find("内存条"), 0, 50, 700, 236);
            Place(window.Find("前沿遮挡"), 0, -80, 720, 327);
            Place(window.Find("左卡扣"), -330, -70, 120, 148, new Vector2(.5f, .163f));
            Place(window.Find("右卡扣"), 330, -70, 120, 148, new Vector2(.513f, .162f));
            var settings = new SerializedObject(source);
            PositionButton(settings, "pressLeftButton", "PressLeft", "按压左侧", -280, -300);
            PositionButton(settings, "pressRightButton", "PressRight", "按压右侧", 280, -300);
            PositionButton(settings, "skipButton", "SkipInstall", "跳过安装", 370, 330);
            PositionButton(settings, "finishButton", "FinishInstall", "完成", 0, -300);
            var texts = window.Cast<Transform>().Select(t => t.GetComponent<TMP_Text>()).Where(t => t != null).ToArray();
            var instruction = texts.First(t => t.text.Contains("先按"));
            Place(instruction.transform, 0, 250, 900, 72); instruction.fontSize = 30;
            instruction.alignment = TextAlignmentOptions.Center;
            foreach (var text in texts.Where(t => t != instruction)) text.gameObject.SetActive(false);
            settings.FindProperty("tipText").objectReferenceValue = instruction;
            settings.ApplyModifiedPropertiesWithoutUndo();
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Game/Art/Fonts/HarmonyOS_Sans/HarmonyOS_Sans_SC_Regular SDF.asset");
            foreach (var text in source.GetComponentsInChildren<TMP_Text>(true)) { text.font = font; text.color = new Color32(22,22,22,255); text.raycastTarget = false; }
            foreach (var image in source.GetComponentsInChildren<Image>())
                if (image.gameObject != source.gameObject && image.GetComponent<Button>() == null) image.raycastTarget = false;
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);

            var overlay = new GameObject("MemoryInstallPopup", typeof(RectTransform), typeof(Image));
            var r = (RectTransform)overlay.transform; r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
            overlay.GetComponent<Image>().color = new Color(0, 0, 0, .5f);
            var copy = Object.Instantiate(source, overlay.transform); copy.name = "Window";
            var copySettings = new SerializedObject(copy);
            copySettings.FindProperty("gamePanel").objectReferenceValue = overlay;
            copySettings.FindProperty("autoStart").boolValue = false;
            copySettings.ApplyModifiedPropertiesWithoutUndo();
            overlay.SetActive(false);
            var prefab = PrefabUtility.SaveAsPrefabAsset(overlay, Prefab);
            Object.DestroyImmediate(overlay);
            scene = EditorSceneManager.OpenScene(Core);
            var owner = Object.FindFirstObjectByType<CoreGameplayController>();
            var layers = Object.FindFirstObjectByType<UIWindowLayers>();
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, layers.WindowLayer);
            instance.SetActive(false);
            layers.Register(instance, (RectTransform)instance.transform.Find("Window"), UIWindowKind.ModalPage);
            var controller = new SerializedObject(owner);
            controller.FindProperty("memoryInstallGame").objectReferenceValue = instance.GetComponentInChildren<MemoryInstallGame>(true);
            controller.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            Debug.Log("Memory installation saved in the scene and bound to successful upgrades.");
        }
        private static void Place(Transform t, float x, float y, float width, float height, Vector2? pivot = null)
        {
            var r = (RectTransform)t; r.anchorMin = r.anchorMax = new Vector2(.5f, .5f);
            r.pivot = pivot ?? new Vector2(.5f, .5f); r.anchoredPosition = new Vector2(x, y);
            r.sizeDelta = new Vector2(width, height); r.localScale = Vector3.one; r.localRotation = Quaternion.identity;
        }
        private static void PositionButton(SerializedObject source, string field, string name, string label, float x, float y)
        {
            var button = (Button)source.FindProperty(field).objectReferenceValue;
            button.name = name; Place(button.transform, x, y, 220, 64);
            var text = button.GetComponentInChildren<TMP_Text>(true); text.text = label; text.fontSize = 28;
            var r = text.rectTransform; r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
            r.localScale = Vector3.one; text.alignment = TextAlignmentOptions.Center;
        }
    }
}
