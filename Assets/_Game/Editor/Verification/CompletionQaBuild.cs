#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace CallmeCatgirl.Verification
{
    /// <summary>Explicit root-invoked QA build; ordinary build settings and identity are restored even on failure.</summary>
    public static class CompletionQaBuild
    {
        public static void BuildWindows64()
        {
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string output = Path.GetFullPath(Path.Combine(project, "..", "game-completion-20261018", "player", "CallmeCatgirlQA.exe"));
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0 || !scenes.Contains(CallmeCatgirl.UI.SceneNavigation.MenuPath)
                || !scenes.Contains(CallmeCatgirl.UI.SceneNavigation.GameplayPath)) throw new BuildFailedException("QA requires the existing enabled menu and gameplay scenes.");
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
                throw new BuildFailedException("The installed Unity Editor has no Windows64 build support.");
            string company = PlayerSettings.companyName, product = PlayerSettings.productName;
            int width = PlayerSettings.defaultScreenWidth, height = PlayerSettings.defaultScreenHeight;
            bool native = PlayerSettings.defaultIsNativeResolution, background = PlayerSettings.runInBackground;
            var mode = PlayerSettings.fullScreenMode;
            try
            {
                PlayerSettings.companyName = CompletionQaPlayer.QaCompany;
                PlayerSettings.productName = CompletionQaPlayer.QaProduct;
                PlayerSettings.defaultScreenWidth = 1280; PlayerSettings.defaultScreenHeight = 720;
                PlayerSettings.defaultIsNativeResolution = false;
                PlayerSettings.fullScreenMode = FullScreenMode.Windowed; PlayerSettings.runInBackground = true;
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes, locationPathName = output, target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.Development
                });
                if (report.summary.result != BuildResult.Succeeded)
                    throw new BuildFailedException("QA Windows64 build result: " + report.summary.result + ", errors=" + report.summary.totalErrors);
                Debug.Log("[CompletionQA] Windows64 development Player built successfully.");
            }
            finally
            {
                PlayerSettings.companyName = company; PlayerSettings.productName = product;
                PlayerSettings.defaultScreenWidth = width; PlayerSettings.defaultScreenHeight = height;
                PlayerSettings.defaultIsNativeResolution = native; PlayerSettings.fullScreenMode = mode;
                PlayerSettings.runInBackground = background;
            }
        }
    }
}
#endif
