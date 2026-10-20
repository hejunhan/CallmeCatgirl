using System;
using System.Linq;
using CallmeCatgirl.Gameplay;
using CallmeCatgirl.Gameplay.Narrative;
using CallmeCatgirl.Gameplay.Progression;
using CallmeCatgirl.Gameplay.Training;
using CallmeCatgirl.UI;
using CallmeCatgirl.UI.SystemMenu;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace CallmeCatgirl.EditorTools
{
    /// <summary>Explicit incremental authoring; never runs on import or entering Play.</summary>
    public static class PersistentUIAuthoring
    {
        public static void Bake()
        {
            RequireEditMode();
            var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if(scene.path!=SceneNavigation.GameplayPath)throw new InvalidOperationException("Open CoreGameplay before authoring its UI.");
            var authored=UnityEngine.Object.FindAnyObjectByType<CoreGameplayCanvasView>();
            if(authored!=null && new SerializedObject(authored).FindProperty("sceneLayoutReady").boolValue)
            {
                Debug.LogWarning("CoreGameplay UI is already authored; legacy Bake skipped to protect the saved layout.");
                return;
            }
            var owner=UnityEngine.Object.FindAnyObjectByType<CoreGameplayController>();
            var view=UnityEngine.Object.FindAnyObjectByType<CoreGameplayCanvasView>();
            if(!new SerializedObject(view).FindProperty("sceneLayoutReady").boolValue)
            {owner.ResetSimulation();owner.PrepareSaveButtons();view.PrepareSceneLayout(owner);}
            var font=view.GetComponentsInChildren<Text>(true).Select(t=>t.font).First(f=>f!=null);
            foreach(var popup in scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<ConversationPopupView>(true)))popup.PrepareSceneLayout();
            var training=owner.GetComponent<TrainingController>();var panel=owner.GetComponent<TrainingPanel>();
            owner.GetComponent<ModelGalleryView>().PrepareSceneLayout(training,panel);
            var parent=view.GetComponent<UIWindowLayers>().WindowLayer;
            var narrative=parent.GetComponentInChildren<NarrativePanel>(true);
            if(narrative==null)
            {
                var root=new GameObject("NarrativePanel",typeof(RectTransform),typeof(NarrativePanel));
                root.transform.SetParent(parent,false);Stretch((RectTransform)root.transform,0);narrative=root.GetComponent<NarrativePanel>();
            }
            narrative.PrepareSceneLayout(font);
            var storyOverlay=(RectTransform)narrative.transform.Find("ScreenBlocker");
            if(storyOverlay.GetComponent<CanvasGroup>()==null)storyOverlay.gameObject.AddComponent<CanvasGroup>();
            var menu=view.GetComponentInChildren<SystemMenuHost>(true);
            if(menu==null)
            {
                var root=new GameObject("SystemMenuHost",typeof(RectTransform),typeof(SystemMenuHost));
                root.transform.SetParent(view.transform,false);Stretch((RectTransform)root.transform,0);menu=root.GetComponent<SystemMenuHost>();
            }
            menu.PrepareSceneLayout(font);
            var policy=view.GetComponentInChildren<ProgressionPanel>(true);
            var overlay=view.GetComponentsInChildren<RectTransform>(true).First(r=>r.name=="ProgressionOverlay");
            var surface=(RectTransform)policy.transform;
            if(surface.parent!=overlay)surface.SetParent(overlay,false);
            var authoredPolicy=new SerializedObject(policy);var version=authoredPolicy.FindProperty("authoredLayoutVersion");
            if(version.intValue<1){Stretch(surface,24);version.intValue=1;authoredPolicy.ApplyModifiedPropertiesWithoutUndo();}
            foreach(Transform child in overlay)if(child!=surface)child.gameObject.SetActive(false);
            policy.Bind(owner.GetComponent<ProgressionController>(),font);policy.PrepareSceneLayout(font);
            if(overlay.GetComponent<CanvasGroup>()==null)overlay.gameObject.AddComponent<CanvasGroup>();
            var entry=view.GetComponentsInChildren<Button>(true).First(b=>b.name=="OpenProgression");
            var label=entry.GetComponentInChildren<Text>(true);label.text="政策";label.alignment=TextAnchor.MiddleCenter;
            ((RectTransform)entry.transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,44);
            Canvas.ForceUpdateCanvases();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            Debug.Log("Authored UI saved: training, policy, narrative, chat and settings. No runtime session was serialized.");
        }

        [MenuItem("CallmeCatgirl/Save Main Menu Settings Layout")]
        public static void BakeMenu()
        {
            RequireEditMode();var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if(scene.path!=SceneNavigation.MenuPath)throw new InvalidOperationException("Open MenuScene first.");
            var font=AssetDatabase.FindAssets("HarmonyOS_Sans_SC_Regular t:Font")
                .Select(id=>AssetDatabase.LoadAssetAtPath<Font>(AssetDatabase.GUIDToAssetPath(id))).FirstOrDefault(f=>f!=null)
                ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            foreach(var nav in scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<SceneNavigation>(true)))nav.PrepareSceneLayout(font);
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        }

        // Explicit batch entry for migrating an existing checkout before a normal build.
        public static void BakeAllScenes()
        {
            RequireEditMode();
            EditorSceneManager.OpenScene(SceneNavigation.GameplayPath);Bake();
            EditorSceneManager.OpenScene(SceneNavigation.MenuPath);BakeMenu();
            EditorSceneManager.OpenScene(SceneNavigation.GameplayPath);
        }

        private static void RequireEditMode(){if(EditorApplication.isPlaying)throw new InvalidOperationException("Leave Play before authoring UI.");}
        private static void Stretch(RectTransform rect,float inset)
        {rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=Vector2.one*inset;rect.offsetMax=-Vector2.one*inset;}
    }
}
