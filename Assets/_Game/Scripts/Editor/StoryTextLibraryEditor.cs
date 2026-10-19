using System;
using System.Linq;
using CallmeCatgirl.Gameplay;
using CallmeCatgirl.Gameplay.Narrative;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace CallmeCatgirl.EditorTools
{
    [InitializeOnLoad]
    public static class StoryTextLibraryEditor
    {
        private static bool reloadPending;
        static StoryTextLibraryEditor()
        {
            NarrativeTextCatalog.Bind(StoryTextLibrary.Get);
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode && reloadPending) Reload();
            };
        }

        [MenuItem("CallmeCatgirl/Text Library/Validate And Reload")]
        public static void Reload()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                reloadPending = true;
                Debug.LogWarning("Narrative JSON changed during Play. Text reload is deferred until Play stops; this run keeps its current text and save content revision.");
                return;
            }
            StoryTextLibrary.Reload();
            foreach (var config in Resources.FindObjectsOfTypeAll<StoryConfiguration>())
                config.RefreshTextCache();
            reloadPending = false;
            Debug.Log("Narrative JSON validated: " + StoryTextLibrary.Count + " stable text IDs. Loaded dialogue caches refreshed; no asset or save written.");
        }
    }

    public sealed class StoryTextImportRefresh : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (imported.Concat(deleted).Concat(moved).Concat(movedFrom).Any(IsTextSource))
                EditorApplication.delayCall += () =>
                {
                    try { StoryTextLibraryEditor.Reload(); }
                    catch (Exception error) { Debug.LogError(error); }
                };
        }

        private static bool IsTextSource(string path)
            => path.Contains("/Resources/NarrativeText/") || path.EndsWith("/Resources/NarrativeTextBindings.json", StringComparison.Ordinal);
    }

    public sealed class StoryTextBuildValidation : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;
        public void OnPreprocessBuild(BuildReport report)
        {
            var main = AssetDatabase.LoadAssetAtPath<GameplayConfiguration>("Assets/_Game/Data/TaskGrid/CoreGameplayConfiguration.asset");
            if (main == null || main.Story == null || main.OrdinaryDialoguePool == null
                || main.Story.name != "StoryConfiguration" || main.OrdinaryDialoguePool.name != "OrdinaryDialoguePool")
                throw new BuildFailedException("The two formal story assets must retain their registered NarrativeTextBindings.json assetName. Restore the references/name or explicitly migrate the bindings before building.");
            StoryTextLibrary.Reload();
            main.Story.RefreshTextCache();
            main.OrdinaryDialoguePool.RefreshTextCache();
        }
    }
}
