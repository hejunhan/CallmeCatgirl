using System.Linq;
using CallmeCatgirl.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace CallmeCatgirl.EditorTools
{
    public static class CorpusInvestmentIntegration
    {
        [MenuItem("CallmeCatgirl/Integrate Conversation Corpus")]
        public static void Bake()
        {
            var scene=EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity");
            foreach(var view in scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<TrainingWorkspaceView>(true)))
                view.PrepareCorpusInvestment();
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
            Debug.Log("Conversation corpus investment controls saved in scene.");
        }
    }
}
