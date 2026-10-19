using System;
using System.Linq;
using System.Reflection;
using CallmeCatgirl.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CallmeCatgirl.EditorTools
{
    /// <summary>Renders a copy of the saved UI in an isolated preview scene. No game session or save store.</summary>
    public sealed class AuthoredUIPreview : EditorWindow
    {
        private static readonly string[] Pages={"TrainingFullPage","ProgressionOverlay","NarrativePanel","SharedSystemSettings","ConversationPopup"};
        private int page;
        private Scene previewScene;
        private Camera camera;
        private RenderTexture texture;
        private GameObject source;
        private string failure;

        [MenuItem("CallmeCatgirl/Preview Authored UI")]
        public static void Open(){GetWindow<AuthoredUIPreview>("Authored UI · Edit").Show();}
        public void ShowTraining(){page=0;Rebuild();Repaint();}
        public void ShowPage(int index){page=index;Rebuild();Repaint();}
        private void OnEnable(){EditorApplication.playModeStateChanged+=OnPlayModeChanged;}
        private void OnDisable(){EditorApplication.playModeStateChanged-=OnPlayModeChanged;DisposePreview();}
        private void OnPlayModeChanged(PlayModeStateChange state)
        {if(state==PlayModeStateChange.ExitingEditMode)Close();}
        private void OnGUI()
        {
            EditorGUILayout.HelpBox("Edit preview · 1280 × 720 · saved UI copies only. Dynamic session contents are absent. Select Source edits the actual scene object.",MessageType.Info);
            EditorGUILayout.BeginHorizontal();
            int next=EditorGUILayout.Popup(page,new[]{"Training","Policy","Narrative","Settings","Chat"});
            if(next!=page){page=next;Rebuild();}
            if(GUILayout.Button("Refresh",GUILayout.Width(80)))Rebuild();
            if(GUILayout.Button("Select Source",GUILayout.Width(110))&&source!=null){Selection.activeGameObject=source;EditorGUIUtility.PingObject(source);}
            EditorGUILayout.EndHorizontal();
            if(texture==null&&failure==null)Rebuild();
            if(failure!=null){EditorGUILayout.HelpBox(failure,MessageType.Warning);return;}
            var rect=GUILayoutUtility.GetAspectRect(1280f/720f);
            if(Event.current.type==EventType.Repaint){camera.Render();GUI.DrawTexture(rect,texture,ScaleMode.ScaleToFit,false);}
        }
        public void Capture(string path)
        {
            if(texture==null)Rebuild();if(camera==null)throw new InvalidOperationException(failure);
            camera.Render();var previous=RenderTexture.active;RenderTexture.active=texture;
            var image=new Texture2D(1280,720,TextureFormat.RGB24,false);
            try{image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();System.IO.File.WriteAllBytes(path,image.EncodeToPNG());}
            finally{RenderTexture.active=previous;DestroyImmediate(image);}
        }
        private void Rebuild()
        {
            DisposePreview();failure=null;
            if(EditorApplication.isPlayingOrWillChangePlaymode){failure="Leave Play to inspect authored Edit UI.";return;}
            var scene=SceneManager.GetActiveScene();
            source=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<RectTransform>(true))
                .Where(r=>r.name==Pages[page]).Select(r=>r.gameObject).FirstOrDefault();
            if(page==4)
            {
                var chat=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<ConversationPopupView>(true)).FirstOrDefault();
                if(chat!=null)source=(GameObject)new SerializedObject(chat).FindProperty("panel").objectReferenceValue;
            }
            if(source==null){failure="Open CoreGameplay and save the authored UI first. Missing: "+Pages[page];return;}
            var flags=BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public;
            var target=typeof(EditorSceneManager).GetMethod("SetTargetSceneForNewGameObjects",flags,null,new[]{typeof(Scene)},null);
            var clear=typeof(EditorSceneManager).GetMethod("ClearTargetSceneForNewGameObjects",flags);
            var current=typeof(EditorSceneManager).GetMethod("GetTargetSceneForNewGameObjects",flags);
            if(target==null||clear==null||current==null){failure="This Unity version cannot isolate preview creation.";return;}
            var previousTarget=(Scene)current.Invoke(null,null);
            previewScene=EditorSceneManager.NewPreviewScene();
            // Unity's own preview construction scope prevents even a temporary object
            // from being created in (and dirtying) the user's active scene.
            target.Invoke(null,new object[]{previewScene});
            try
            {
            var cameraRoot=new GameObject("PreviewCamera",typeof(Camera));SceneManager.MoveGameObjectToScene(cameraRoot,previewScene);
            camera=cameraRoot.GetComponent<Camera>();camera.scene=previewScene;camera.clearFlags=CameraClearFlags.SolidColor;
            camera.backgroundColor=new Color(.18f,.19f,.20f);camera.orthographic=true;camera.orthographicSize=5;camera.nearClipPlane=.1f;camera.farClipPlane=100;
            texture=new RenderTexture(1280,720,24);texture.Create();camera.targetTexture=texture;
            var canvasRoot=new GameObject("PreviewCanvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));SceneManager.MoveGameObjectToScene(canvasRoot,previewScene);
            var canvas=canvasRoot.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
            var original=source.GetComponentInParent<Canvas>().rootCanvas.GetComponent<CanvasScaler>();
            if(original!=null)EditorUtility.CopySerialized(original,canvasRoot.GetComponent<CanvasScaler>());
            var copy=Instantiate(source,canvasRoot.transform,false);copy.name=source.name+" · Preview Copy";copy.SetActive(true);
            var policy=copy.GetComponentInChildren<CallmeCatgirl.Gameplay.Progression.ProgressionPanel>(true);
            if(policy!=null)policy.PreviewConfiguration();
            if(page==2)
            {
                var blocker=copy.transform.Find("ScreenBlocker");blocker.gameObject.SetActive(true);
                blocker.Find("StoryWindow/Heading").GetComponent<Text>().text="剧情 · 编辑预览";
                blocker.Find("StoryWindow/ScrollViewport/ReadableContent/StoryBody").GetComponent<Text>().text="正文与选项来自运行时会话。此处仅预览已保存窗口、字体、滚动区及选项模板。";
                var template=copy.transform.Find("NarrativeTemplates/ChoiceTemplate").GetComponent<Button>();
                var choice=Instantiate(template,blocker.Find("StoryWindow/ScrollViewport/ReadableContent"));choice.gameObject.SetActive(true);
            }
            var rect=(RectTransform)copy.transform;
            rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
            // Rebuild only this copy. A global Canvas update also rewrites driven
            // RectTransforms in the editable scene when its Game view size changes.
            LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
            }
            finally
            {
                if(previousTarget.IsValid())target.Invoke(null,new object[]{previousTarget});else clear.Invoke(null,null);
            }
        }
        private void DisposePreview()
        {
            if(previewScene.IsValid())EditorSceneManager.ClosePreviewScene(previewScene);
            if(texture!=null){texture.Release();DestroyImmediate(texture);texture=null;}camera=null;
        }
    }
}
