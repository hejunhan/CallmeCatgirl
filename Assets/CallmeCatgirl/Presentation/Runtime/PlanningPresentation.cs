using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace CallmeCatgirl.Presentation
{
    [DisallowMultipleComponent, RequireComponent(typeof(Canvas)), DefaultExecutionOrder(10000)]
    [AddComponentMenu("CallmeCatgirl/Presentation/Planning Presentation")]
    public sealed class PlanningPresentation : MonoBehaviour
    {
        public const int MaxPapers = 8;
        private const int MaxSheets = MaxPapers * 2;
        private static readonly Dictionary<int, PlanningPresentation> DisplayOwners = new Dictionary<int, PlanningPresentation>();
        private static readonly int ProgressId = Shader.PropertyToID("_Progress");
        private static readonly int ViewportId = Shader.PropertyToID("_ViewportSize");
        private static readonly int CountId = Shader.PropertyToID("_PaperCount");
        private static readonly int Pair01Id = Shader.PropertyToID("_Paper01");
        private static readonly int Pair23Id = Shader.PropertyToID("_Paper23");
        private static readonly int StrengthId = Shader.PropertyToID("_PaperStrength");
        [SerializeField] private DifferenceWipeGraphic overlay = null;
        [SerializeField] private Material materialTemplate = null;
        [SerializeField] private PlanningPaper[] papers = new PlanningPaper[0];
        [SerializeField] private bool reducedMotion;
        private readonly Vector4[] pair01 = new Vector4[MaxSheets], pair23 = new Vector4[MaxSheets], strengths = new Vector4[MaxSheets];
        private readonly Vector4[] front01 = new Vector4[MaxPapers], front23 = new Vector4[MaxPapers];
        private readonly Vector4[] rear01 = new Vector4[MaxPapers], rear23 = new Vector4[MaxPapers];
        private readonly float[] distances = new float[MaxPapers];
        private ReversibleProgress wipe;
        private Material instance;
        private Canvas effectCanvas;
        private int registeredDisplay, sampledFrame = -1;
        private bool registered, focused = true, appPaused, blocked, skipDelta = true;
        private float frameDelta;
        private RectTransform computeSurface;
        private readonly List<RectTransform> windowSurfaces=new List<RectTransform>();
        private readonly Vector3[] coverageCorners=new Vector3[4];
        private readonly Vector4[] window01=new Vector4[8],window23=new Vector4[8];
        public bool PlanningRequested => wipe.Requested;
        public float WipeProgress => wipe.Value;
        public bool ReducedMotion { get => reducedMotion; set => reducedMotion = value; }
        public void SetColorCoverage(RectTransform compute,IReadOnlyList<RectTransform> windows)
        {
            computeSurface=compute;windowSurfaces.Clear();
            for(int i=0;i<windows.Count&&i<window01.Length;i++)windowSurfaces.Add(windows[i]);
        }

        private void OnEnable()
        {
            effectCanvas = GetComponent<Canvas>();
            if (overlay == null || materialTemplate == null || materialTemplate.shader == null ||
                materialTemplate.shader.name != "CallmeCatgirl/UI/Difference Wipe" || papers == null || papers.Length > MaxPapers ||
                effectCanvas.renderMode != RenderMode.ScreenSpaceOverlay || effectCanvas != effectCanvas.rootCanvas)
            { Fail("Assign the supplied material, graphic, up to eight papers, and a dedicated root Screen Space Overlay Canvas."); return; }
            for (int i = 0; i < papers.Length; i++)
                for (int j = i + 1; j < papers.Length; j++)
                    if (papers[i] != null && papers[i] == papers[j])
                    { Fail("A paper may only appear once in the papers array."); return; }
            registeredDisplay = effectCanvas.targetDisplay;
            if (DisplayOwners.TryGetValue(registeredDisplay, out var other) && other != this)
            { Fail("Only one planning difference layer may render on a display; two layers would cancel each other."); return; }
            DisplayOwners[registeredDisplay] = this; registered = true;
            instance = new Material(materialTemplate) { name = materialTemplate.name + " (runtime)", hideFlags = HideFlags.DontSave };
            overlay.material = instance; overlay.raycastTarget = false;
            overlay.enabled = false; // First render callback supplies the complete uniform set.
            foreach (var paper in papers) if (paper != null) paper.Request(wipe.Requested);
            focused = Application.isFocused; skipDelta = true;
            Canvas.willRenderCanvases += BeforeRender;
        }
        private void Fail(string reason)
        {
            Debug.LogError("PlanningPresentation: " + reason, this);
            if (overlay != null) overlay.enabled = false;
            enabled = false;
        }
        private void OnDisable()
        {
            Canvas.willRenderCanvases -= BeforeRender;
            if (registered && DisplayOwners.TryGetValue(registeredDisplay, out var owner) && owner == this)
                DisplayOwners.Remove(registeredDisplay);
            registered = false;
            if (overlay != null) { overlay.enabled = false; overlay.material = null; }
            if (instance != null) { Destroy(instance); instance = null; }
        }
        // Call ONLY for manual pause changes. Modal/presence changes have their own input.
        public void SetPlanning(bool requested)
        {
            if (!wipe.Request(requested)) return;
            foreach (var paper in papers) if (paper != null) paper.Request(requested);
        }
        public void SetPresentationBlocked(bool value)
        {
            if (blocked == value) return;
            blocked = value; skipDelta = true;
        }
        private void OnApplicationFocus(bool value) { focused = value; skipDelta = true; }
        private void OnApplicationPause(bool value) { appPaused = value; skipDelta = true; }
        private bool Available => focused && !appPaused;
        private void LateUpdate()
        {
            if (instance == null) return;
            float wipeDelta=Available&&!skipDelta?Time.unscaledDeltaTime:0;
            frameDelta = !blocked?wipeDelta:0;
            skipDelta = false;
            wipe.Tick(wipeDelta, .6f, reducedMotion && Available);
            var selectionObject = EventSystem.current == null ? null : EventSystem.current.currentSelectedGameObject;
            var selection = selectionObject == null ? null : selectionObject.transform;
            foreach (var paper in papers)
                if (paper != null) paper.Advance(frameDelta, reducedMotion && Available && !blocked, selection);
        }
        private void BeforeRender()
        {
            if (instance == null || sampledFrame == Time.frameCount) return;
            sampledFrame = Time.frameCount; // Unity may invoke willRenderCanvases several times.
            Vector2 viewport = new Vector2(Screen.width, Screen.height);
            int count = 0;
            for (int i = 0; i < papers.Length; i++)
            {
                var paper = papers[i];
                if (paper == null || !paper.HasGeometry) continue;
                distances[i] = paper.SampleFront(viewport, out front01[i], out front23[i]);
                bool rear = paper.RearSheet != null && paper.RearSheet.gameObject.activeInHierarchy;
                if (rear)
                    distances[i] = Mathf.Max(distances[i], paper.Sample(paper.RearSheet, viewport, out rear01[i], out rear23[i]));
                bool covered = MotionMath.IsCovered(wipe.Value, distances[i], viewport.x, viewport.y);
                paper.ObserveCoverage(frameDelta, wipe.Requested, covered, reducedMotion && Available && !blocked);
                float amount = paper.Restoration;
                if (amount <= 0) continue;
                AddSheet(ref count, front01[i], front23[i], amount);
                if (rear) AddSheet(ref count, rear01[i], rear23[i], amount);
            }
            instance.SetFloat(ProgressId, wipe.Value);
            instance.SetVector(ViewportId, new Vector4(viewport.x, viewport.y, 0, 0));
            instance.SetInt(CountId, count);
            // Fixed-size arrays are never resized, including on the first upload.
            instance.SetVectorArray(Pair01Id, pair01); instance.SetVectorArray(Pair23Id, pair23);
            instance.SetVectorArray(StrengthId, strengths);
            Vector4 compute01,compute23;
            bool hasCompute=SampleSurface(computeSurface,out compute01,out compute23);
            instance.SetInt("_HasCompute",hasCompute?1:0);instance.SetVector("_Compute01",compute01);instance.SetVector("_Compute23",compute23);
            int windows=0;
            foreach(var surface in windowSurfaces)
                if(SampleSurface(surface,out window01[windows],out window23[windows])&&++windows==window01.Length)break;
            instance.SetInt("_WindowCount",windows);instance.SetVectorArray("_Window01",window01);instance.SetVectorArray("_Window23",window23);
            overlay.enabled = wipe.Value > 0;
        }
        private bool SampleSurface(RectTransform surface,out Vector4 a,out Vector4 b)
        {
            a=b=Vector4.zero;if(surface==null||!surface.gameObject.activeInHierarchy)return false;
            surface.GetWorldCorners(coverageCorners);
            var canvas=surface.GetComponentInParent<Canvas>();var camera=canvas==null||canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera;
            var p0=RectTransformUtility.WorldToScreenPoint(camera,coverageCorners[0]);var p1=RectTransformUtility.WorldToScreenPoint(camera,coverageCorners[1]);
            var p2=RectTransformUtility.WorldToScreenPoint(camera,coverageCorners[2]);var p3=RectTransformUtility.WorldToScreenPoint(camera,coverageCorners[3]);
            a=new Vector4(p0.x,p0.y,p1.x,p1.y);b=new Vector4(p2.x,p2.y,p3.x,p3.y);return true;
        }
        private void AddSheet(ref int index, Vector4 a, Vector4 b, float amount)
        {
            pair01[index] = a; pair23[index] = b; strengths[index] = new Vector4(amount, 0, 0, 0); index++;
        }
    }
}
