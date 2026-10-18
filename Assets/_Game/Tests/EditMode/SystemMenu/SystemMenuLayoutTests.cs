using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using CallmeCatgirl.UI.SystemMenu;

namespace CallmeCatgirl.Tests.SystemMenu
{
    /// <summary>Fake-backed UGUI checks. These do not change Screen, AudioListener, QualitySettings or PlayerPrefs.</summary>
    public sealed class SystemMenuLayoutTests
    {
        private sealed class Backend : ISystemSettingsBackend
        {
            public DisplayConfiguration CurrentDisplay { get; private set; } = new DisplayConfiguration(1280,720,GameWindowMode.Windowed);
            public IReadOnlyList<DisplayResolution> AvailableResolutions => new[] { new DisplayResolution(1280,720),new DisplayResolution(1920,1080) };
            public float MasterVolume { get; set; } = 1;
            public int TargetFrameRate { get; set; } = -1;
            public int VSyncCount { get; set; }
            public void SetDisplay(DisplayConfiguration configuration) { CurrentDisplay = configuration; }
        }
        private sealed class Store : ISystemSettingsStore
        {
            public bool TryLoad(out SystemSettingsSnapshot snapshot) { snapshot=null; return false; }
            public void Save(SystemSettingsSnapshot snapshot) { }
        }

        [TestCase(1280,720)] [TestCase(1920,1080)]
        public void SharedSettingsKeepFixedBackButtonAndScrollableContentWithinTheCanvas(int width,int height)
        {
            var canvasObject = new GameObject("SettingsTestCanvas",typeof(RectTransform),typeof(Canvas));
            try
            {
                var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode=RenderMode.WorldSpace;
                var parent = canvasObject.GetComponent<RectTransform>(); parent.sizeDelta=new Vector2(width,height);
                var service=new SystemSettingsService(new Backend(),new Store());
                var host=SystemMenuHost.Create(parent,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),false,service:service,applySavedSettings:false);
                host.OpenSettings(); Canvas.ForceUpdateCanvases();
                var paper=(RectTransform)host.transform.Find("SharedSystemSettings/SettingsPaper");
                var scroll=paper.Find("SettingsScroll").GetComponent<ScrollRect>();
                var footer=(RectTransform)paper.Find("Footer");
                Assert.That(paper.rect.width,Is.LessThanOrEqualTo(width-39)); Assert.That(paper.rect.height,Is.LessThanOrEqualTo(height-39));
                Assert.That(scroll.horizontal,Is.False); Assert.That(scroll.vertical,Is.True);
                Assert.That(scroll.viewport.rect.height,Is.GreaterThan(0)); Assert.That(scroll.viewport.GetComponent<RectMask2D>(),Is.Not.Null);
                Assert.That(footer.anchorMin.y,Is.Zero); Assert.That(footer.anchorMax.y,Is.Zero); Assert.That(footer.gameObject.activeInHierarchy,Is.True);
                Assert.That(host.IsPauseClaimed,Is.False);
                // Overflow can belong to explanatory text below the last selectable control.
                // Selecting an already-visible control must not require or cause unnecessary scrolling.
                var controls=scroll.content.GetComponentsInChildren<Selectable>();
                foreach(var control in controls) AssertSelectionIsVisible(scroll,control);
                for(int i=controls.Length-1;i>=0;i--) AssertSelectionIsVisible(scroll,controls[i]);
            }
            finally { Object.DestroyImmediate(canvasObject); }
        }

        private static void AssertSelectionIsVisible(ScrollRect scroll,Selectable control)
        {
            var selected=(RectTransform)control.transform;
            var before=VerticalBounds(selected,scroll.viewport);
            var viewport=scroll.viewport.rect;
            bool alreadyVisible=before.x>=viewport.yMin-.1f&&before.y<=viewport.yMax+.1f;
            float previousPosition=scroll.content.anchoredPosition.y;
            control.GetComponent<SystemMenuScrollFocus>().OnSelect(null);
            Canvas.ForceUpdateCanvases();
            var after=VerticalBounds(selected,scroll.viewport);
            Assert.That(after.x,Is.GreaterThanOrEqualTo(viewport.yMin-.5f),control.name+" bottom is visible after keyboard selection.");
            Assert.That(after.y,Is.LessThanOrEqualTo(viewport.yMax+.5f),control.name+" top is visible after keyboard selection.");
            if(alreadyVisible)
                Assert.That(scroll.content.anchoredPosition.y,Is.EqualTo(previousPosition).Within(.1f),"Selecting visible "+control.name+" does not scroll.");
        }

        private static Vector2 VerticalBounds(RectTransform selected,RectTransform viewport)
        {
            var corners=new Vector3[4];selected.GetWorldCorners(corners);
            float minimum=float.PositiveInfinity,maximum=float.NegativeInfinity;
            foreach(var corner in corners)
            {
                float y=viewport.InverseTransformPoint(corner).y;
                minimum=Mathf.Min(minimum,y);maximum=Mathf.Max(maximum,y);
            }
            return new Vector2(minimum,maximum);
        }

        [Test] public void EscapeRevertsPreviewBeforeClosingSettingsOrReleasingTheDrawerClaim()
        {
            var canvasObject=new GameObject("SettingsTestCanvas",typeof(RectTransform),typeof(Canvas));
            try
            {
                canvasObject.GetComponent<Canvas>().renderMode=RenderMode.WorldSpace;
                var parent=canvasObject.GetComponent<RectTransform>();parent.sizeDelta=new Vector2(1280,720);
                var backend=new Backend();var service=new SystemSettingsService(backend,new Store());var claims=new List<bool>();
                var host=SystemMenuHost.Create(parent,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),true,claims.Add,service:service,applySavedSettings:false);
                host.OpenPause();host.OpenSettings();service.PreviewDisplay(new DisplayConfiguration(1920,1080,GameWindowMode.BorderlessFullscreen));
                host.HandleEscape();Assert.That(service.IsDisplayPreviewPending,Is.False);Assert.That(host.IsSettingsOpen,Is.True);Assert.That(host.IsPauseClaimed,Is.True);
                host.HandleEscape();Assert.That(host.IsSettingsOpen,Is.False);Assert.That(host.IsPauseOpen,Is.True);
                host.HandleEscape();CollectionAssert.AreEqual(new[]{true,false},claims);
            }
            finally { Object.DestroyImmediate(canvasObject); }
        }
    }
}
