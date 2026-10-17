using System.Collections.Generic;
using System.Linq;
using CallmeCatgirl.Presentation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using CallmeCatgirl.Gameplay;

namespace CallmeCatgirl.UI
{
    public enum UIWindowKind { Floating, ModalPage, Ending }

    // The only owner of Canvas ordering, window placement and workspace input gating.
    [DisallowMultipleComponent,DefaultExecutionOrder(9000)]
    public sealed class UIWindowLayers : MonoBehaviour
    {
        public const int GameplayOrder=0,PauseEffectOrder=100;
        [System.Serializable] private sealed class Window
        {
            public GameObject Root;
            public RectTransform Surface;
            public CanvasGroup Input;
            public UIWindowKind Kind;
            public bool WasVisible;
            public int Opened;
        }
        [SerializeField] private List<Window> windows=new List<Window>();
        private readonly List<RectTransform> surfaces=new List<RectTransform>();
        [SerializeField] private RectTransform workspace,windowLayer,hints;
        private RectTransform compute;
        [SerializeField] private CanvasGroup workspaceInput;
        [SerializeField] private GameObject blocker;
        private PlanningPresentation presentation;
        private CoreGameplayController owner;
        private Window top;
        private int opened;
        public bool IsBlockingInput => windows.Any(w=>w.Root!=null&&w.Root.activeInHierarchy&&(w.Kind!=UIWindowKind.Floating));
        public string ActiveWindow => top==null?null:top.Root.name;
        public RectTransform WindowLayer => windowLayer;
        public bool CoversPointer(Vector2 point,Camera camera) => windows.Any(w=>w.Root!=null&&w.Root.activeInHierarchy&&w.Surface!=null&&RectTransformUtility.RectangleContainsScreenPoint(w.Surface,point,camera));

        public void Configure(RectTransform computeSurface,PlanningPresentation pause,CoreGameplayController source)
        {
            compute=computeSurface;presentation=pause;owner=source;
            if (!Application.isPlaying)
            { GetComponent<Canvas>().sortingOrder=GameplayOrder; if(pause!=null)pause.GetComponent<Canvas>().sortingOrder=PauseEffectOrder; }
            if(workspace==null)
            {
                workspace=Layer("BaseInteractionRoot");workspaceInput=workspace.gameObject.AddComponent<CanvasGroup>();
                hints=Layer("DragAndHints");var hintInput=hints.gameObject.AddComponent<CanvasGroup>();hintInput.blocksRaycasts=hintInput.interactable=false;
                windowLayer=Layer("WindowLayer");
                var r=Layer("ModalInputBlocker",windowLayer);var image=r.gameObject.AddComponent<Image>();image.color=Color.clear;image.raycastTarget=true;image.canvasRenderer.cullTransparentMesh=false;blocker=r.gameObject;
            }
            if (!Application.isPlaying) AdoptChildren();
            Synchronize();
        }
        private RectTransform Layer(string name,Transform parent=null)
        {
            var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();r.SetParent(parent==null?transform:parent,false);Stretch(r);return r;
        }
        private static void Stretch(RectTransform r){r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;}
        private void AdoptChildren()
        {
            foreach(var child in transform.Cast<Transform>().ToArray())
            {
                if(child==workspace||child==windowLayer||child==hints)continue;
                if(child.name=="ConversationPopup"||child.name=="MemoryUpgradePopup"||child.name=="ProgressionOverlay"||child.name=="EndingPanel")
                {
                    var frame=child.Find("Window")??child.Find("Content")??child;
                    Register(child.gameObject,(RectTransform)frame,child.name=="EndingPanel"?UIWindowKind.Ending:child.name=="ConversationPopup"?UIWindowKind.Floating:UIWindowKind.ModalPage);
                }
                else child.SetParent(child.name=="DragLayer"||child.name=="SettlementFeedback"||child.name=="ConversationAssociationLines"?hints:workspace,false);
            }
            // Navigation inherits the same Canvas and raycaster as the rest of the workspace.
            var navigation=workspace.Find("FixedNavigation");
            if(navigation!=null)
            {
                var raycaster=navigation.GetComponent<GraphicRaycaster>();if(raycaster!=null)DestroyImmediate(raycaster);
                var canvas=navigation.GetComponent<Canvas>();if(canvas!=null)DestroyImmediate(canvas);
            }
            workspace.SetSiblingIndex(0);hints.SetSiblingIndex(1);windowLayer.SetAsLastSibling();
        }
        public void Register(GameObject root,RectTransform surface,UIWindowKind kind)
        {
            if(windowLayer==null)throw new System.InvalidOperationException("Configure the shared UI layers before registering a window.");
            var entry=windows.FirstOrDefault(w=>w.Root==root);
            if(entry==null)
            {
                var input=root.GetComponent<CanvasGroup>();if(input==null)input=root.AddComponent<CanvasGroup>();
                entry=new Window{Root=root,Input=input};windows.Add(entry);
            }
            entry.Surface=surface;entry.Kind=kind;
            if(root.transform.parent!=windowLayer){root.transform.SetParent(windowLayer,false);Stretch((RectTransform)root.transform);}
        }
        private void LateUpdate(){if(workspace!=null)Synchronize();}
        public void Synchronize()
        {
            if(workspace==null)return;
            windows.RemoveAll(w=>w.Root==null);surfaces.Clear();
            foreach(var w in windows)
            {
                bool visible=w.Root.activeInHierarchy;if(visible&&!w.WasVisible)w.Opened=++opened;w.WasVisible=visible;
                if(visible&&w.Surface!=null)surfaces.Add(w.Surface);
            }
            var ordered=windows.Where(w=>w.WasVisible).OrderBy(w=>w.Kind).ThenBy(w=>w.Opened).ToArray();
            var next=ordered.LastOrDefault();
            if(next!=top&&EventSystem.current!=null)EventSystem.current.SetSelectedGameObject(null);
            top=next;bool blocking=IsBlockingInput;blocker.SetActive(blocking);blocker.transform.SetAsFirstSibling();
            foreach(var w in ordered)w.Root.transform.SetAsLastSibling();
            foreach(var w in windows){w.Input.interactable=w.Input.blocksRaycasts=w==top;w.Input.ignoreParentGroups=false;}
            workspaceInput.interactable=workspaceInput.blocksRaycasts=!blocking;
            presentation?.SetColorCoverage(compute,surfaces);
        }
    }
}
