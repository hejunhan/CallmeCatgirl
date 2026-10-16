using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.IO;
using CallmeCatgirl.Gameplay.TaskGrid;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace CallmeCatgirl.Tests
{
    public sealed class SystemFeedbackTests
    {
        private static object Call(object o,string method,params object[] args) => o.GetType().GetMethod(method).Invoke(o,args);
        private static object Field(object o,string name) => o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(o);
        private static T Value<T>(object o,string name) => (T)o.GetType().GetProperty(name).GetValue(o);
        private static MonoBehaviour Named(string type) => Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).First(o=>o.GetType().Name==type);
        [UnityTest] public IEnumerator ConfigColorsContoursAndPausedScansPreserveInteraction()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity");
            yield return new EnterPlayMode(); yield return null;
            CheckVisuals(); yield return null;
            CheckRaycast();
            var controller = Named("CoreGameplayController"); var sim=Value<TaskSimulation>(controller,"Simulation");
            var module=Named("TaskModuleView"); var signal=module.GetComponentsInChildren<Graphic>().First(g=>g.GetType().Name=="TaskSignalGraphic");
            float frozen=(float)Field(signal,"phase"); double time=sim.GameTime;
            yield return new WaitForSecondsRealtime(.1f);
            Assert.That(sim.GameTime,Is.EqualTo(time)); Assert.That((float)Field(signal,"phase"),Is.EqualTo(frozen));
            sim.Paused=false; sim.Advance(1); sim.Paused=true; Call(Named("CoreGameplayCanvasView"),"Refresh");
            Assert.That((float)Field(signal,"phase"),Is.Not.EqualTo(frozen));
            AddBoardTasks(sim);
            Call(Named("CoreGameplayCanvasView"),"Refresh");
            Capture("Running.png");
            sim.Paused=false; sim.Advance(10); sim.Paused=true; Call(Named("CoreGameplayCanvasView"),"Refresh");
            Assert.That(sim.Tasks["visual"].State,Is.EqualTo(RequestState.Ready));
            Capture("Ready.png");
            Call(controller,"SelectRequest","visual"); Call(controller,"SubmitSelected");
            Assert.That(sim.Tasks["visual"].State,Is.EqualTo(RequestState.Submitted));
            Assert.That(Value<bool>(controller,"IsStoryOpen"),Is.True);
            yield return new ExitPlayMode();
        }
        private static void CheckVisuals()
        {
            var controller=Named("CoreGameplayController"); var view=Named("CoreGameplayCanvasView");
            var sim=Value<TaskSimulation>(controller,"Simulation"); var original=(ScriptableObject)Value<object>(view,"Feedback");
            var config=Object.Instantiate(original); var field=view.GetType().GetField("feedback",BindingFlags.Instance|BindingFlags.NonPublic);
            field.SetValue(view,config);
            // 未启用美术皮肤时仍保留原有的配置颜色回退。
            var artwork = new[]{"0.5SpeedButton","PauseButton"}.Select(n => GameObject.Find(n).GetComponents<MonoBehaviour>().FirstOrDefault(c => c.GetType().Name == "ButtonArtwork")).Where(c => c != null).ToArray();
            foreach(var skin in artwork) skin.enabled=false;
            try
            {
                Color custom=new Color(.8f,.2f,.7f);config.GetType().GetField("SpeedSelected").SetValue(config,custom);
                config.GetType().GetField("PauseSelected").SetValue(config,custom);Call(view,"Refresh");
                Assert.That(GameObject.Find("0.5SpeedButton").GetComponent<Button>().targetGraphic.color,Is.EqualTo(custom));
                Assert.That(GameObject.Find("PauseButton").GetComponent<Button>().targetGraphic.color,Is.EqualTo(custom));
                var def=new TaskDefinition("A","visual",new[]{new Cell(0,0),new Cell(1,0)},10,1,90,1,Array.Empty<AbilityRequirement>());
                Assert.That(sim.TryCreate("visual","visual-user","1",def,out _),Is.True);
                var origin=Enumerable.Range(0,sim.Width*sim.Height).Select(i=>new Cell(i%sim.Width,i/sim.Width)).First(c=>sim.Preview("visual",c,0)==PlacementFailure.None);
                Assert.That(sim.TryPlace("visual",origin,0,out _),Is.True);Call(controller,"SelectRequest","A-1");Call(view,"Refresh");
                var module=Named("TaskModuleView");var signals=module.GetComponentsInChildren<Graphic>().Where(g=>g.GetType().Name=="TaskSignalGraphic").ToArray();
                Assert.That(signals.Length,Is.EqualTo(2));Assert.That(signals.All(g=>!g.raycastTarget),Is.True);
                Assert.That((int)Field(signals[0],"edges"),Is.EqualTo(13));Assert.That((int)Field(signals[1],"edges"),Is.EqualTo(7));
                var expected=(Color)Call(config,"BorderFor","visual");Assert.That(signals[0].color,Is.EqualTo(expected));
                Call(controller,"SelectRequest","visual");Call(view,"Refresh");
                Assert.That(signals[0].transform.parent.GetComponent<Image>().color,Is.EqualTo((Color)config.GetType().GetField("TaskSelected").GetValue(config)));
                config.GetType().GetField("Animate").SetValue(config,false);Call(view,"Refresh");
                Assert.That((float)Field(signals[0],"opacity"),Is.Zero);Assert.That((float)Field(signals[0],"pulse"),Is.Zero);
                Assert.That(sim.Tasks["visual"].Progress,Is.Zero);Assert.That(sim.Paused,Is.True);
            }
            finally { field.SetValue(view,original);foreach(var skin in artwork) skin.enabled=true;Call(view,"Refresh");Object.DestroyImmediate(config); }
        }
        private static void AddBoardTasks(TaskSimulation sim)
        {
            foreach (string id in new[]{"A-1","B-1"})
                for (int i=0;i<sim.Width*sim.Height;i++)
                {
                    var c=new Cell(i%sim.Width,i/sim.Width);
                    if (sim.TryPlace(id,c,0,out _)) break;
                }
        }
        private static void CheckRaycast()
        {
            var module=Named("TaskModuleView");
            Canvas.ForceUpdateCanvases(); var rect=(RectTransform)module.transform;
            var data=new PointerEventData(EventSystem.current){position=RectTransformUtility.WorldToScreenPoint(null,rect.TransformPoint(new Vector3(10,-10,0))),button=PointerEventData.InputButton.Left};
            var hits=new System.Collections.Generic.List<RaycastResult>();EventSystem.current.RaycastAll(data,hits);
            Assert.That(hits.Count,Is.GreaterThan(0));Assert.That(ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject),Is.EqualTo(module.gameObject),"Top hit: "+hits[0].gameObject.name);
        }
        private static void Capture(string name)
        {
            var canvas=GameObject.Find("GameplayCanvas").GetComponent<Canvas>();var mode=canvas.renderMode;var oldCamera=canvas.worldCamera;var distance=canvas.planeDistance;
            var cameraObject=new GameObject("FeedbackCapture",typeof(Camera));var camera=cameraObject.GetComponent<Camera>();
            camera.orthographic=true;camera.orthographicSize=5;camera.nearClipPlane=.1f;camera.farClipPlane=200;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
            var target=new RenderTexture(1600,1000,24);camera.targetTexture=target;var active=RenderTexture.active;var texture=new Texture2D(1600,1000,TextureFormat.RGB24,false);
            try
            {
                canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=10;Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=target;
                texture.ReadPixels(new Rect(0,0,1600,1000),0,0);texture.Apply();Directory.CreateDirectory("Logs/SystemFeedback");File.WriteAllBytes("Logs/SystemFeedback/"+name,texture.EncodeToPNG());
            }
            finally { canvas.renderMode=mode;canvas.worldCamera=oldCamera;canvas.planeDistance=distance;RenderTexture.active=active;camera.targetTexture=null;Object.DestroyImmediate(texture);Object.DestroyImmediate(target);Object.DestroyImmediate(cameraObject);Canvas.ForceUpdateCanvases(); }
        }
        [UnityTearDown] public IEnumerator StopPlayMode(){if(EditorApplication.isPlaying)yield return new ExitPlayMode();}
    }
}
