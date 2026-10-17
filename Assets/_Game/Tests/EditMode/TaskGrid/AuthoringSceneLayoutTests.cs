using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace CallmeCatgirl.Tests
{
    public sealed class AuthoringSceneLayoutTests
    {
        private const string Scene = "Assets/_Game/Scenes/Prototypes/CoreGameplay.unity";
        private const string Snapshot = "Logs/SceneAuthoring/AuthoredLayout.json";
        [Serializable] private sealed class Layout { public Item[] items; }
        [Serializable] private sealed class Item
        {
            public string path;
            public Vector2 min, max, pivot, position, size;
            public Vector3 scale;
            public int fontSize;
            public string fontName,spriteName;
        }
        private static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;
        private static RectTransform[] Frames() => Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(r => new[] { "BoardPanel", "CandidatePanel", "Toolbar", "FixedNavigation", "ModelPanel", "MemoryPanel", "SaveRun", "LoadRun", "Window", "GalleryLayout" }.Contains(r.name)).ToArray();
        private static Item Capture(RectTransform r)
        {
            var text=r.GetComponent<Text>();var image=r.GetComponent<Image>();var sprite=image!=null?image.sprite:null;var font=text!=null?text.font:null;return new Item {path=PathOf(r),min=r.anchorMin,max=r.anchorMax,pivot=r.pivot,position=r.anchoredPosition,size=r.sizeDelta,scale=r.localScale,fontSize=text==null?0:text.fontSize,fontName=font!=null?font.name:string.Empty,spriteName=sprite!=null?sprite.name:string.Empty};
        }
        private static RectTransform[] Observed()
        {
            var view=Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single(o=>o.GetType().Name=="CoreGameplayCanvasView");
            return Frames().Concat(new[]{"clockLabel","modelLabel","memoryUpgradeLabel","infoTitle","infoStatus","abilitiesLabel","qualityLabel"}
                .Select(name=>(Text)view.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(view)).Select(t=>t.rectTransform)).Distinct().ToArray();
        }
        private static object Call(object o,string name,params object[] args) => o.GetType().GetMethod(name).Invoke(o,args);
        [UnityTest] public IEnumerator StartingAndResettingUseTheAuthoredSceneLayout()
        {
            EditorSceneManager.OpenScene(Scene); Canvas.ForceUpdateCanvases();
            Directory.CreateDirectory("Logs/SceneAuthoring");
            File.WriteAllText(Snapshot,JsonUtility.ToJson(new Layout {items=Observed().Select(Capture).ToArray()},true));
            Capture("SceneMainPreview.png");
            var gallery=Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single(t=>t.name=="ModelGallery");
            gallery.gameObject.SetActive(true);CheckGalleryBounds(gallery);Capture("SceneGalleryPreview.png");gallery.gameObject.SetActive(false);
            yield return new EnterPlayMode(); yield return null;
            CheckLayout();
            yield return new ExitPlayMode();
        }
        private static void CheckLayout()
        {
            var authored=JsonUtility.FromJson<Layout>(File.ReadAllText(Snapshot));
            var current=Observed().ToDictionary(PathOf);
            foreach(var expected in authored.items)
            {
                Assert.That(current.ContainsKey(expected.path),Is.True,"Runtime reparented or replaced "+expected.path);
                var actual=Capture(current[expected.path]);
                Assert.That(actual.min,Is.EqualTo(expected.min),expected.path+" anchors");
                Assert.That(actual.max,Is.EqualTo(expected.max),expected.path+" anchors");
                Assert.That(actual.pivot,Is.EqualTo(expected.pivot),expected.path+" pivot");
                Assert.That(Vector2.Distance(actual.position,expected.position),Is.LessThan(.1f),expected.path+" position");
                Assert.That(Vector2.Distance(actual.size,expected.size),Is.LessThan(.1f),expected.path+" size");
                Assert.That(actual.scale,Is.EqualTo(expected.scale),expected.path+" scale");
                Assert.That(actual.fontSize,Is.EqualTo(expected.fontSize),expected.path+" font size");
                Assert.That(actual.fontName,Is.EqualTo(expected.fontName),expected.path+" font");
                Assert.That(actual.spriteName,Is.EqualTo(expected.spriteName),expected.path+" sprite");
            }
            var owner=Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Single(o=>o.GetType().Name=="CoreGameplayController");
            var board=Frames().Single(r=>r.name=="BoardPanel");
            board.anchoredPosition+=new Vector2(13,-9);var changed=Capture(board);
            var view=Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Single(o=>o.GetType().Name=="CoreGameplayCanvasView");
            var clock=(Text)view.GetType().GetField("clockLabel",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(view);
            var paper=board.GetComponent<Image>();var art=Object.FindObjectsByType<Image>(FindObjectsInactive.Include,FindObjectsSortMode.None).First(i=>i.sprite!=null).sprite;
            var previousArt=paper.sprite;paper.sprite=art;
            clock.fontSize+=2;int changedFont=clock.fontSize;
            Call(owner,"ResetSimulation");Call(owner,"ResetSimulation");
            Assert.That(clock.fontSize,Is.EqualTo(changedFont),"Reset overwrote the scene-configured font size.");
            Assert.That(paper.sprite,Is.SameAs(art),"Reset overwrote the authored artwork.");
            Assert.That(board.anchoredPosition,Is.EqualTo(changed.position),"Reset overwrote an authored/user-adjusted position.");
            Assert.That(Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None).Count(t=>t.name=="FixedNavigation"),Is.EqualTo(1));
            Assert.That(Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None).Count(t=>t.name=="SaveRun"),Is.EqualTo(1));
            var effects=Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single(t=>t.name=="PlanningEffectsCanvas");
            Assert.That(effects.gameObject.activeSelf,Is.True,"Effect canvas must still enable during Play.");
            paper.sprite=previousArt;clock.fontSize-=2;board.anchoredPosition-=new Vector2(13,-9);
            Capture("RuntimeMain.png");Call(owner,"OpenTraining");
            var reveal=Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single(o=>o.GetType().Name=="ModelPageReveal");
            reveal.GetType().GetProperty("ReducedMotion").SetValue(reveal,true);
            reveal.GetType().GetMethod("OnApplicationFocus",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(reveal,new object[]{true});
            reveal.GetType().GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(reveal,null);
            Capture("RuntimeGallery.png");
            var panel=owner.GetComponents<MonoBehaviour>().Single(o=>o.GetType().Name=="TrainingPanel");Call(panel,"Close");
        }
        private static void CheckGalleryBounds(Transform gallery)
        {
            var canvas=gallery.GetComponentInParent<Canvas>().rootCanvas;
            var area=(RectTransform)gallery.Find("GalleryLayout/Viewport");
            var container=(RectTransform)canvas.transform;var corners=new Vector3[4];area.GetWorldCorners(corners);
            foreach(var corner in corners)
            {
                var point=container.InverseTransformPoint(corner);
                Assert.That(point.x,Is.InRange(container.rect.xMin,container.rect.xMax),"Model page extends outside the canvas horizontally.");
                Assert.That(point.y,Is.InRange(container.rect.yMin,container.rect.yMax),"Model page extends outside the canvas vertically.");
            }
        }
        private static void Capture(string name)
        {
            var method=typeof(SystemFeedbackTests).GetMethod("Capture",BindingFlags.NonPublic|BindingFlags.Static);
            method.Invoke(null,new object[]{name});method.Invoke(null,new object[]{name});
        }
        [UnityTearDown] public IEnumerator StopPlay() { if(EditorApplication.isPlaying)yield return new ExitPlayMode(); }
    }
}
