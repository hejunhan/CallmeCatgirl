using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CallmeCatgirl.Tests.SystemMenu
{
    /// <summary>World-space fixture capture only. Never changes Screen or opens a Player window.</summary>
    public static class UxOffscreenCapture
    {
        public static Font ChineseFont()
        {
            foreach (var id in AssetDatabase.FindAssets("HarmonyOS t:Font"))
            {
                var font = AssetDatabase.LoadAssetAtPath<Font>(AssetDatabase.GUIDToAssetPath(id));
                if (font != null) return font;
            }
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        public static void Save(RectTransform viewport, string name, string provenance)
        {
            int width = Mathf.RoundToInt(viewport.rect.width), height = Mathf.RoundToInt(viewport.rect.height);
            var cameraObject = new GameObject("UX_OFFSCREEN_ONLY", typeof(Camera));
            var camera = cameraObject.GetComponent<Camera>();
            camera.enabled = false; camera.orthographic = true;
            camera.orthographicSize = height * Mathf.Abs(viewport.lossyScale.y) * .5f;
            camera.aspect = (float)width / height;
            camera.transform.rotation = viewport.rotation;
            camera.transform.position = viewport.TransformPoint(viewport.rect.center) - viewport.forward * 100;
            camera.nearClipPlane = .1f; camera.farClipPlane = 200;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.1f, .11f, .12f);
            var target = new RenderTexture(width, height, 24); target.Create(); camera.targetTexture = target;
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0); texture.Apply();
                string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/UiUxAudit"));
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder, name + ".png"), texture.EncodeToPNG());
                File.WriteAllText(Path.Combine(folder, name + ".txt"),
                    "SYNTHETIC UI FIXTURE / EDITOR OFFSCREEN ONLY\n" + width + "x" + height + "\n" + provenance
                    + "\nCamera.Render + RenderTexture; no desktop input, actual display changes or economic-flow claim.\n");
            }
            finally
            {
                RenderTexture.active = previous; camera.targetTexture = null;
                Object.DestroyImmediate(texture); target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(cameraObject);
            }
        }
    }
}
