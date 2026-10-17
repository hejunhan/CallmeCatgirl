using System;
using System.IO;
using System.Linq;
using CallmeCatgirl.Gameplay;
using CallmeCatgirl.Gameplay.Training;
using CallmeCatgirl.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CallmeCatgirl.EditorTools
{
    public static class SceneUILayoutBaker
    {
        [MenuItem("CallmeCatgirl/Save Current UI Layout To Scene")]
        public static void Bake()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before saving the authored UI layout.");
            const string path="Assets/_Game/Scenes/Prototypes/CoreGameplay.unity";
            var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if(scene.path!=path)
            {
                if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
                scene=EditorSceneManager.OpenScene(path);
            }
            var view=UnityEngine.Object.FindAnyObjectByType<CoreGameplayCanvasView>();
            var ready=new SerializedObject(view).FindProperty("sceneLayoutReady");
            if(ready.boolValue){Debug.Log("UI layout already lives in the scene; edit its objects directly.");return;}
            var owner=UnityEngine.Object.FindAnyObjectByType<CoreGameplayController>();
            owner.ResetSimulation();owner.PrepareSaveButtons();view.PrepareSceneLayout(owner);
            foreach(var popup in scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<ConversationPopupView>(true)))popup.PrepareSceneLayout();
            var training=owner.GetComponent<TrainingController>();var trainingPanel=owner.GetComponent<TrainingPanel>();
            var gallery=owner.GetComponent<ModelGalleryView>()??owner.gameObject.AddComponent<ModelGalleryView>();
            gallery.PrepareSceneLayout(training,trainingPanel);
            var settings=new SerializedObject(trainingPanel);settings.FindProperty("gallery").objectReferenceValue=gallery;settings.ApplyModifiedPropertiesWithoutUndo();
            view.Refresh();Canvas.ForceUpdateCanvases();
            foreach(var root in scene.GetRootGameObjects())
                foreach(var effects in root.GetComponentsInChildren<CallmeCatgirl.Presentation.PlanningPresentation>(true))effects.gameObject.SetActive(false);
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            Directory.CreateDirectory("Logs/SceneAuthoring");File.WriteAllText("Logs/SceneAuthoring/Baked.txt","Static UI saved in "+path);
            Debug.Log("Fixed UI, windows and data templates are now authored in the scene.");
        }
    }
}
