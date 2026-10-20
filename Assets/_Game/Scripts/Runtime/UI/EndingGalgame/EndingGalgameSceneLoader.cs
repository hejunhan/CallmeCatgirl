using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CallmeCatgirl.UI.EndingGalgame
{
    /// <summary>Additive load/unload for EndingGalgameScene. The caller owns EndRun and keeps its own state loaded.</summary>
    public static class EndingGalgameSceneLoader
    {
        public const string SceneName = "EndingGalgameScene";

        /// <summary>Loads the ending scene additively and starts the ending. The host keeps its own audio listener and EndRun path.</summary>
        public static IEnumerator Load(string endingId, Action<EndingRunRequest> completed, Action<string> failed = null)
        {
            Scene scene = SceneManager.GetSceneByName(SceneName);
            if (scene.isLoaded) { failed?.Invoke(SceneName + " is already loaded."); yield break; }

            AsyncOperation load;
            try { load = SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Additive); }
            catch (Exception error) { failed?.Invoke(SceneName + " could not load: " + error.Message); yield break; }
            if (load == null) { failed?.Invoke(SceneName + " is not in Build Settings."); yield break; }
            yield return load;

            scene = SceneManager.GetSceneByName(SceneName);
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                EndingGalgamePanel panel = root.GetComponentInChildren<EndingGalgamePanel>(true);
                if (panel == null) continue;
                panel.Completed += completed;
                try { panel.Show(endingId); }
                catch (Exception error)
                {
                    panel.Completed -= completed;
                    failed?.Invoke("Ending " + endingId + " could not start: " + error.Message);
                    yield break;
                }
                yield break;
            }
            failed?.Invoke("No EndingGalgamePanel in " + SceneName + ".");
        }

        public static IEnumerator Unload()
        {
            Scene scene = SceneManager.GetSceneByName(SceneName);
            if (scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
        }
    }
}
