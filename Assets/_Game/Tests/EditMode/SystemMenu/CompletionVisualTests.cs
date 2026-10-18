// STAGING ONLY. Import into Assets/_Game/Tests/EditMode/SystemMenu for the lead's one batch Unity run.
// Requires CallmeCatgirl.SystemMenu + UnityEngine.UI + optionalUnityReferences TestAssemblies.
// Uses the authored Core scene and production views. No desktop input, Player launch, Screen or audio writes.
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
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using CallmeCatgirl.UI.SystemMenu;
using Object = UnityEngine.Object;

namespace CallmeCatgirl.Tests.CompletionVisuals
{
    public sealed class CompletionVisualTests
    {
        private const string CoreScene = "Assets/_Game/Scenes/Prototypes/CoreGameplay.unity";
        private const string Output = "Logs/CompletionVisuals";
        private static readonly Vector2Int[] Sizes = { new Vector2Int(1280,720), new Vector2Int(1920,1080) };
        private static readonly List<CaptureRecord> Records = new List<CaptureRecord>();
        private static PolicyFrameWait policyFrameWait;

        private sealed class FakeBackend : ISystemSettingsBackend
        {
            public DisplayConfiguration CurrentDisplay => new DisplayConfiguration(1280,720,GameWindowMode.Windowed);
            public IReadOnlyList<DisplayResolution> AvailableResolutions => new[] { new DisplayResolution(1280,720),new DisplayResolution(1920,1080) };
            public float MasterVolume { get; set; } = .65f;
            public int TargetFrameRate { get; set; } = 120;
            public int VSyncCount { get; set; } = 1;
            public int DisplayWrites;
            public void SetDisplay(DisplayConfiguration value) { DisplayWrites++; Assert.Fail("This visual suite must never request a display preview, even through its fake backend."); }
        }
        private sealed class FakeStore : ISystemSettingsStore
        {
            public int Writes;
            public bool TryLoad(out SystemSettingsSnapshot value) { value=null; return false; }
            public void Save(SystemSettingsSnapshot value) { Writes++; Assert.Fail("Visual capture must not save settings."); }
        }
        [Serializable] private sealed class CaptureRecord
        {
            public string file, phase, scene, provenance, renderer, gameSnapshot;
            public bool synthetic;
            public int width,height,activeGraphics;
            public int capturedFrame;
            public PolicyFrameWait policyWait;
            public FontObservation[] policyFonts;
            public string[] assertions;
        }
        [Serializable] private sealed class PolicyFrameWait
        {
            public string phase;
            public int createdFrame,readyFrame,advancedFrames,editorUpdates;
            public double realSeconds;
        }
        [Serializable] private sealed class FontObservation
        {
            public string objectPath,fontName,fontStyle;
            public int fontSize,atlasWidth,atlasHeight,observedFrame;
            public bool dynamic;
            public float pixelsPerUnit;
        }
        [Serializable] private sealed class CaptureIndex
        {
            public string renderer = "Unity Editor batch, temporary Camera + RenderTexture of the actual authored scene Canvas. This is not a desktop Player or actual Screen/FPS test.";
            public bool screenModeChanged=false,realAudioChanged=false,preferencesWritten=false,osInputUsed=false,playerLaunched=false;
            public CaptureRecord[] captures;
        }
        private sealed class CanvasState
        {
            public Canvas Canvas;
            public RenderMode Mode;
            public Camera Camera;
            public float Distance,Scale,ReferencePixels;
            public CanvasScaler Scaler;
            public bool ScalerEnabled;
        }

        // Keep this outer coroutine free of capturing lambdas. All post-domain-reload state is created below.
        [UnityTest] public IEnumerator CaptureActualCoreAndProductionPanelsAtBothSizes()
        {
            EditorSceneManager.OpenScene(CoreScene);
            FindComponent("CoreGameplayController").enabled=false;
            yield return new EnterPlayMode();
            yield return CaptureAfterDomainReload();
            yield return new ExitPlayMode();
        }

        [UnityTearDown] public IEnumerator LeavePlayModeAfterAnAssertionFailure()
        {
            if (EditorApplication.isPlaying) yield return new ExitPlayMode();
        }

        private static IEnumerator CaptureAfterDomainReload()
        {
            Records.Clear(); policyFrameWait=null; Directory.CreateDirectory(Output);
            var owner=FindComponent("CoreGameplayController");
            Assert.That(owner.enabled,Is.False,"Core Start must be held until the fake settings host is injected.");
            Assert.That(Object.FindObjectsByType<SystemMenuHost>(FindObjectsInactive.Include),Is.Empty,
                "No production settings host may load/apply preferences before injection.");
            var view=FindComponent("CoreGameplayCanvasView");
            var canvas=view.GetComponent<Canvas>(); Assert.That(canvas,Is.Not.Null);
            var font=FindChineseFont(view);
            var backend=new FakeBackend(); var store=new FakeStore();
            var menu=SystemMenuHost.Create((RectTransform)canvas.transform,font,true,service:new SystemSettingsService(backend,store),applySavedSettings:false);
            SetField(owner,"systemMenu",menu);
            CallStatic(FindType("CallmeCatgirl.UI.SceneNavigation"),"ConsumeContinueSlot");
            Call(owner,"Start");
            SetProperty(owner,"ManualCommandClock",true);
            Call(owner,"ConfigureSaveSlot","qa-completion-visuals-no-save");
            Call(owner,"ProcessCompletion");
            yield return null;

            var narrative=Get(owner,"Narrative");
            var narrativePanel=FindComponent("NarrativePanel");
            Assert.That(Get<int>(Get(owner,"Simulation"),"OpenCellCount"),Is.Zero);
            Assert.That(Get<int>(owner,"MemoryLevel"),Is.Zero);
            Assert.That(Get<string>(Get(narrative,"CurrentFrame"),"Body"),Does.Contain("假设你是一个猫娘"));
            Assert.That(Items(Get(Get(owner,"Training"),"Session"),"Corpus").Length,Is.Zero);
            CaptureBoth(owner,view,"01_doctor_actual_new_run",false,
                "Actual newly initialized Core run: zero memory cells, zero inventory, pinned Doctor, unchosen opening. Only settings backend is replaced with an in-memory fake.","narrative-opening");

            // UI-only edge fixture: show the real empty training view before the mandatory tutorial choice.
            // The ledger remains genuinely empty. This bypass is not claimed as an accessible normal-game route.
            var overlay=(RectTransform)GetField(narrativePanel,"overlay"); overlay.gameObject.SetActive(false);
            var trainingPanel=FindComponent("TrainingPanel"); var training=Get(owner,"Training");
            OpenActualTrainingWorkspace(trainingPanel);
            yield return null;
            var workspace=FindComponent("TrainingWorkspaceView");
            Assert.That(Items(Get(training,"Session"),"Corpus").Length,Is.Zero);
            CaptureBoth(owner,view,"02_training_ZERO_INVENTORY_UI_FIXTURE",true,
                "UI-only fixture: real production TrainingWorkspace exposed before the mandatory opening choice. Actual ledger is empty; normal tutorial gating is bypassed only for this layout edge case. No currency or materials granted.","training");
            Assert.That(((Button)GetField(workspace,"start")).interactable,Is.False);
            Call(workspace,"Cancel"); Call(narrativePanel,"Refresh");
            Assert.That((bool)Call(narrative,"Choose","accept"),Is.True);
            Call(owner,"ProcessCompletion");
            Assert.That((bool)Call(narrative,"Choose","continue"),Is.True);
            Call(owner,"ProcessCompletion");
            Assert.That(Get<int>(Get(owner,"Simulation"),"OpenCellCount"),Is.EqualTo(1));
            OpenActualTrainingWorkspace(trainingPanel); yield return null;
            workspace=FindComponent("TrainingWorkspaceView");
            Assert.That((bool)Call(workspace,"TryPlaceConversation","narrative-doctor",0),Is.True);
            CaptureBoth(owner,view,"03_training_actual_first_point_draft",false,
                "Actual opening choice accepted through Narrative. Core effect processing granted exactly one Reasoning corpus point and opened one compute cell. The real training view arranged that point via its public draft-placement API; Start was not committed.","training");
            Assert.That(((Button)GetField(workspace,"start")).interactable,Is.True);

            // Full-grid pressure fixture. Grants go through the real idempotent scripted-reward API.
            // They are explicitly synthetic and do not stand in for an earned economy/training run.
            Call(workspace,"Cancel");
            var session=Get(training,"Session");
            for(int i=0;i<9;i++) GrantFixtureCorpus(session,i);
            OpenActualTrainingWorkspace(trainingPanel); yield return null;
            workspace=FindComponent("TrainingWorkspaceView");
            var recipeChoices=(Array)GetField(workspace,"recipeOptions");
            int normalRecipe=-1;
            for(int i=0;i<recipeChoices.Length;i++) if(Get<string>(recipeChoices.GetValue(i),"Id")!="tutorial-first-model") { normalRecipe=i; break; }
            Assert.That(normalRecipe,Is.GreaterThanOrEqualTo(0));
            ((Dropdown)GetField(workspace,"recipes")).value=normalRecipe;
            for(int i=0;i<9;i++) Assert.That((bool)Call(workspace,"TryPlaceConversation","qa-visual-corpus-"+i,i),Is.True);
            Assert.That(Items(workspace,"Investments").Length,Is.EqualTo(9));
            CaptureBoth(owner,view,"04_training_NINE_GRID_SYNTHETIC_FIXTURE",true,
                "Synthetic stress fixture: nine explicit QA scripted corpus grants and a normal recipe selected in the real view. All nine arrangement slots are filled; real compute capacity remains one cell, so Start may correctly be disabled. This does not prove economy or a legal nine-cell training job.","training");
            int beforeCancel=Items(session,"Corpus").Length;
            Call(workspace,"Cancel");
            Assert.That(Items(workspace,"Investments").Length,Is.Zero);
            Assert.That(Items(session,"Corpus").Length,Is.EqualTo(beforeCancel),"Cancel must not consume QA or real tutorial materials.");
            // Cancel can return to the model catalogue while the training host stays open.
            // Close the real host explicitly before asking Core to open the policy overlay.
            Call(trainingPanel,"Close");
            Assert.That(Get<bool>(owner,"IsTrainingOpen"),Is.False,"Policy capture must begin after closing the training/catalogue host.");

            // Policy page access is exercised with real purchase methods, but synthetic wallet funding.
            Call(owner,"AddFunds",10000);
            var progression=FindComponent("ProgressionController");Call(progression,"EnsureBound");
            Assert.That(Call(progression,"TryPurchase","policy-foundations").ToString(),Is.EqualTo("Success"));
            Assert.That(Call(progression,"TryPurchase","route-open").ToString(),Is.EqualTo("Success"));
            Call(owner,"OpenPolicyTree"); yield return null;
            var policy=FindComponent("ProgressionPanel");
            Assert.That(policy.gameObject.activeInHierarchy,Is.True,"OpenPolicyTree must reveal the actual policy panel, not leave the catalogue on screen.");
            Call(policy,"Update");
            yield return WaitForPolicyPlayerFrames(policy,"policy-page1-created");
            CaptureBoth(owner,view,"05_policy_page1_FUNDED_FIXTURE",true,
                "Actual production policy graph and purchase APIs. Fixture wallet received 10000 coins; foundations and open route were purchased to expose state distinctions. No terminal was purchased; not an earned progression run.","policy");
            Call(policy,"ShowPage",1);Call(policy,"Update");
            yield return WaitForPolicyPlayerFrames(policy,"policy-page2-created");
            Assert.That(Get<int>(policy,"CurrentPage"),Is.EqualTo(1));
            CaptureBoth(owner,view,"06_policy_page2_UNLOCKED_FIXTURE",true,
                "Actual second policy page, unlocked by the real open-route purchase using the labeled synthetic wallet fixture. This page grants a conversation opportunity; no romance ending or affection reward is manufactured.","policy");
            Call(policy,"Close");yield return null;

            // Reuse the real narrative panel and real catalogue text, then deliberately repeat the view text.
            // The immutable narrative state/catalogue is not rewritten.
            Assert.That((bool)Call(narrative,"RequestPolicyEnding",0,-15,15),Is.True);
            Call(narrativePanel,"Refresh");
            var body=(Text)GetField(narrativePanel,"body");
            string catalogueBody=body.text;
            Assert.That(catalogueBody,Does.Contain("你是一个猫娘"));
            body.text="【QA长中文夹具：以下实际剧情段落重复六次，仅测试真实窗口滚动排版】\n\n"+string.Join("\n\n",Enumerable.Repeat(catalogueBody,6));
            foreach(var button in (IEnumerable)GetField(narrativePanel,"choiceButtons"))
            {
                var caption=((Button)button).GetComponentInChildren<Text>();
                caption.text="【QA长选项】"+string.Join(" / ",Enumerable.Repeat(caption.text,3));
            }
            Call(narrativePanel,"FitChoices");
            CaptureBoth(owner,view,"07_narrative_LONG_CHINESE_FIXTURE_top",true,
                "Actual NarrativePanel and actual ordinary-ending catalogue text. Layout-only stress fixture repeats body six times and each choice three times; the underlying session/catalogue remains unchanged. Ending trigger was called directly without terminal gameplay.","narrative-top");
            CaptureBoth(owner,view,"08_narrative_LONG_CHINESE_FIXTURE_choices",true,
                "Same labeled long-text view fixture scrolled to the bottom. Verifies the final option is reachable and visible, not a completed ending or normal economic flow.","narrative-bottom");

            ((RectTransform)GetField(narrativePanel,"overlay")).gameObject.SetActive(false);
            menu.OpenSettings();
            CaptureBoth(owner,view,"09_shared_settings_FAKE_BACKEND",true,
                "Production shared settings component inside the actual Core Canvas. Backend and store are in-memory QA fakes (65% volume, 120 FPS cap, VSync on). UI-only opening during a mandatory narrative fixture; no Screen, audio, display preview or preference writes.","settings");
            menu.CloseAll();
            Assert.That(backend.DisplayWrites,Is.Zero);Assert.That(store.Writes,Is.Zero);
            File.WriteAllText(Path.Combine(Output,"INDEX.json"),JsonUtility.ToJson(new CaptureIndex { captures=Records.ToArray() },true));
            TestContext.WriteLine("Completion visuals: "+Records.Count+" real-Canvas offscreen PNGs plus per-image provenance in "+Path.GetFullPath(Output));
        }

        private static void OpenActualTrainingWorkspace(MonoBehaviour panel)
        {
            Call(panel,"Open");
            var gallery=FindComponent("ModelGalleryView");SetField(gallery,"trainingPage",true);Call(gallery,"Redraw");
        }
        private static IEnumerator WaitForPolicyPlayerFrames(MonoBehaviour panel,string phase)
        {
            const double maximumSeconds=15;
            var clock=System.Diagnostics.Stopwatch.StartNew();
            while(GetField(panel,"heading")==null&&clock.Elapsed.TotalSeconds<maximumSeconds)
            {
                EditorApplication.QueuePlayerLoopUpdate();
                yield return null;
            }
            Assert.That(GetField(panel,"heading"),Is.Not.Null,"Actual policy Start must finish before observing card creation.");
            Call(panel,"Update");
            Assert.That(((IDictionary)GetField(panel,"cards")).Count,Is.GreaterThan(0),"Actual policy cards must exist before font-settling wait.");
            int createdFrame=Time.frameCount,editorUpdates=0;
            clock.Restart();
            // A nested EditMode null yield can advance only the editor iterator. Require actual PlayerLoop frames.
            while(Time.frameCount-createdFrame<2&&clock.Elapsed.TotalSeconds<maximumSeconds)
            {
                EditorApplication.QueuePlayerLoopUpdate();editorUpdates++;
                yield return null;
            }
            policyFrameWait=new PolicyFrameWait { phase=phase,createdFrame=createdFrame,readyFrame=Time.frameCount,
                advancedFrames=Time.frameCount-createdFrame,editorUpdates=editorUpdates,realSeconds=clock.Elapsed.TotalSeconds };
            clock.Stop();
            string observation=phase+": createdFrame="+createdFrame+", readyFrame="+policyFrameWait.readyFrame
                +", playerFrames="+policyFrameWait.advancedFrames+", editorUpdates="+editorUpdates+", realSeconds="+policyFrameWait.realSeconds.ToString("0.###");
            TestContext.WriteLine(observation);
            Assert.That(policyFrameWait.advancedFrames,Is.GreaterThanOrEqualTo(2),"Policy capture did not receive two real PlayerLoop frames within "+maximumSeconds+" seconds. "+observation);
        }
        private static void GrantFixtureCorpus(object session,int index)
        {
            var ability=FindType("CallmeCatgirl.Gameplay.TaskGrid.AbilityKind");
            var values=(IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(ability,typeof(double)));
            values.Add(Enum.ToObject(ability,index%4),1d);
            var result=Call(session,"GrantScriptedCorpus","qa-visual-event-"+index,"qa-visual-corpus-"+index,values,
                "【QA合成库存】长中文语料来源：山间观测站与模型如何理解同一句话，第"+(index+1)+"份");
            Assert.That(result.ToString(),Is.EqualTo("Success"));
        }

        private static void CaptureBoth(MonoBehaviour owner,MonoBehaviour view,string phase,bool synthetic,string provenance,string checks)
        {
            foreach(var size in Sizes) Capture(owner,view,phase,size.x,size.y,synthetic,provenance,checks);
        }
        private static void Capture(MonoBehaviour owner,MonoBehaviour view,string phase,int width,int height,bool synthetic,string provenance,string checks)
        {
            var cameraObject=new GameObject("CompletionVisuals_OFFSCREEN_ONLY",typeof(Camera));
            var camera=cameraObject.GetComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.orthographicSize=5;
            camera.nearClipPlane=.1f;camera.farClipPlane=200;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.09f,.10f,.11f);
            var target=new RenderTexture(width,height,24);target.Create();camera.targetTexture=target;
            var texture=new Texture2D(width,height,TextureFormat.RGB24,false);var oldActive=RenderTexture.active;
            var states=new List<CanvasState>();GameObject badge=null;
            try
            {
                foreach(var canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude))
                {
                    if(!canvas.isRootCanvas||canvas.gameObject.scene.path!=CoreScene)continue;
                    var scaler=canvas.GetComponent<CanvasScaler>();
                    float originalScale=canvas.scaleFactor,originalReferencePixels=canvas.referencePixelsPerUnit;
                    states.Add(new CanvasState { Canvas=canvas,Mode=canvas.renderMode,Camera=canvas.worldCamera,Distance=canvas.planeDistance,Scale=originalScale,ReferencePixels=originalReferencePixels,Scaler=scaler,ScalerEnabled=scaler!=null&&scaler.enabled });
                    if(scaler!=null)scaler.enabled=false;
                    canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=10;
                    canvas.scaleFactor=TargetScale(scaler,width,height,originalScale);canvas.referencePixelsPerUnit=originalReferencePixels;
                }
                Assert.That(states.Count,Is.GreaterThan(0),"Capture must render actual scene root Canvases.");
                Canvas.ForceUpdateCanvases();
                var layers=view.GetComponent(view.GetType().Assembly.GetType("CallmeCatgirl.UI.UIWindowLayers"));if(layers!=null)Call(layers,"Synchronize");
                if(checks=="policy")Call(FindComponent("ProgressionPanel"),"Update");
                if(checks.StartsWith("narrative",StringComparison.Ordinal))Call(FindComponent("NarrativePanel"),"FitChoices");
                foreach(var menu in Object.FindObjectsByType<SystemMenuHost>(FindObjectsInactive.Exclude))Call(menu,"Layout");
                Canvas.ForceUpdateCanvases();Canvas.ForceUpdateCanvases();
                var assertions=new List<string>();
                Exception assertionFailure=null;
                try
                {
                if(checks=="training")
                {
                    var page=FindComponent("TrainingWorkspaceView").transform.Find("TrainingFullPage");
                    Assert.That(page,Is.Not.Null);
                    AssertVisible((RectTransform)page.Find("Footer/Start"),camera,"Training Start",assertions);
                    AssertVisible((RectTransform)page.Find("Footer/Cancel"),camera,"Training Cancel",assertions);
                    AssertVisible((RectTransform)page.Find("Close"),camera,"Training header Cancel",assertions);
                }
                if(checks=="settings")
                    AssertVisible((RectTransform)Object.FindAnyObjectByType<SystemMenuHost>().transform.Find("SharedSystemSettings/SettingsPaper/Footer"),camera,"Settings fixed Back",assertions);
                if(checks=="policy")
                {
                    var panel=FindComponent("ProgressionPanel");
                    foreach(var button in panel.GetComponentsInChildren<Button>())
                        if(button.name.StartsWith("关闭",StringComparison.Ordinal)||button.name.StartsWith("第一页",StringComparison.Ordinal)||button.name.StartsWith("第二页",StringComparison.Ordinal))
                            AssertVisible((RectTransform)button.transform,camera,"Policy fixed "+button.name,assertions);
                }
                if(checks=="narrative-top"||checks=="narrative-bottom")
                {
                    var panel=FindComponent("NarrativePanel");var scroll=(ScrollRect)GetField(panel,"scroll");
                    LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);Canvas.ForceUpdateCanvases();
                    Assert.That(scroll.content.rect.height,Is.GreaterThan(scroll.viewport.rect.height),"Labeled long-text fixture must really overflow.");
                    scroll.verticalNormalizedPosition=checks=="narrative-bottom"?0:1;Canvas.ForceUpdateCanvases();
                    if(checks=="narrative-bottom")
                    {
                        Button last=null;foreach(Button button in (IEnumerable)GetField(panel,"choiceButtons"))last=button;
                        Assert.That(last,Is.Not.Null);AssertInside((RectTransform)last.transform,scroll.viewport,"Last long narrative choice",assertions);
                    }
                }
                if(checks=="narrative-opening")
                {
                    var panel=FindComponent("NarrativePanel");
                    Assert.That(Items(Get(Get(owner,"Narrative"),"CurrentFrame"),"Choices").Length,Is.EqualTo(2),"Fresh Doctor state must have exactly two choices.");
                    var legacyPopup=FindComponent("ConversationPopupView");
                    Assert.That(((GameObject)GetField(legacyPopup,"panel")).activeInHierarchy,Is.False,"Old conversation popup must not cover the new Doctor opening.");
                    int visibleChoices=0;
                    foreach(Button button in (IEnumerable)GetField(panel,"choiceButtons"))
                    {
                        visibleChoices++;AssertVisible((RectTransform)button.transform,camera,"Doctor opening "+button.name,assertions);
                        AssertTopClick(button,camera,assertions);
                    }
                    Assert.That(visibleChoices,Is.EqualTo(2),"Production NarrativePanel must render exactly two Doctor choice buttons.");
                    assertions.Add("Exactly two actual Doctor choices; legacy ConversationPopup hidden; top raycast belongs to each real choice.");
                }
                }
                catch(Exception error) { assertionFailure=error;assertions.Add("FAILED geometry/input assertion: "+error.Message); }
                badge=AddProvenanceBadge((RectTransform)view.transform,FindChineseFont(view),synthetic,width,height,phase);
                Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=target;
                texture.ReadPixels(new Rect(0,0,width,height),0,0);texture.Apply();
                string filename=phase+"_"+width+"x"+height+".png";File.WriteAllBytes(Path.Combine(Output,filename),texture.EncodeToPNG());
                var record=new CaptureRecord { file=filename,phase=phase,scene=CoreScene,provenance=provenance,synthetic=synthetic,width=width,height=height,
                    renderer="Editor batch Camera.Render + RenderTexture; authored Core Canvas plus production dynamic panels; target CanvasScaler computed for render target, actual Screen unchanged.",
                    gameSnapshot=(string)Call(owner,"QueryGameJson"),activeGraphics=view.GetComponentsInChildren<Graphic>().Length,
                    capturedFrame=Time.frameCount,policyWait=checks=="policy"?policyFrameWait:null,
                    policyFonts=checks=="policy"?ObservePolicyFonts(FindComponent("ProgressionPanel")):Array.Empty<FontObservation>(),assertions=assertions.ToArray() };
                Records.Add(record);File.WriteAllText(Path.Combine(Output,filename+".json"),JsonUtility.ToJson(record,true));
                TestContext.WriteLine(filename+" synthetic="+synthetic+"; "+string.Join("; ",assertions));
                if(assertionFailure!=null)Assert.Fail("Saved the real rendered failure frame to "+Path.Combine(Output,filename)+" before reporting: "+assertionFailure);
            }
            finally
            {
                if(badge!=null)Object.DestroyImmediate(badge);
                foreach(var state in states)
                {
                    if(state.Canvas==null)continue;state.Canvas.renderMode=state.Mode;state.Canvas.worldCamera=state.Camera;state.Canvas.planeDistance=state.Distance;state.Canvas.scaleFactor=state.Scale;state.Canvas.referencePixelsPerUnit=state.ReferencePixels;
                    if(state.Scaler!=null)state.Scaler.enabled=state.ScalerEnabled;
                }
                RenderTexture.active=oldActive;camera.targetTexture=null;
                Object.DestroyImmediate(texture);target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(cameraObject);Canvas.ForceUpdateCanvases();
            }
        }

        private static float TargetScale(CanvasScaler scaler,int width,int height,float fallback)
        {
            if(scaler==null||scaler.uiScaleMode!=CanvasScaler.ScaleMode.ScaleWithScreenSize)return fallback;
            var reference=scaler.referenceResolution;float x=width/reference.x,y=height/reference.y;
            if(scaler.screenMatchMode==CanvasScaler.ScreenMatchMode.Expand)return Mathf.Min(x,y);
            if(scaler.screenMatchMode==CanvasScaler.ScreenMatchMode.Shrink)return Mathf.Max(x,y);
            return Mathf.Pow(2,Mathf.Lerp(Mathf.Log(x,2),Mathf.Log(y,2),scaler.matchWidthOrHeight));
        }
        private static FontObservation[] ObservePolicyFonts(MonoBehaviour panel)
        {
            var observations=new List<FontObservation>();
            foreach(var text in panel.GetComponentsInChildren<Text>())
            {
                var font=text.font;Texture atlas=font!=null&&font.material!=null?font.material.mainTexture:null;
                string path=text.name;for(var parent=text.transform.parent;parent!=null&&parent!=panel.transform;parent=parent.parent)path=parent.name+"/"+path;
                var observation=new FontObservation { objectPath=path,fontName=font==null?"<null>":font.name,
                    fontSize=text.fontSize,fontStyle=text.fontStyle.ToString(),dynamic=font!=null&&font.dynamic,pixelsPerUnit=text.pixelsPerUnit,
                    atlasWidth=atlas==null?0:atlas.width,atlasHeight=atlas==null?0:atlas.height,observedFrame=Time.frameCount };
                observations.Add(observation);
                TestContext.WriteLine("Policy font: "+path+", font="+observation.fontName+", size="+observation.fontSize
                    +", dynamic="+observation.dynamic+", style="+observation.fontStyle+", frame="+observation.observedFrame
                    +", pixelsPerUnit="+observation.pixelsPerUnit.ToString("0.###")+", atlas="+observation.atlasWidth+"x"+observation.atlasHeight);
            }
            return observations.ToArray();
        }
        private static GameObject AddProvenanceBadge(RectTransform parent,Font font,bool synthetic,int width,int height,string phase)
        {
            var go=new GameObject("QA_PROVENANCE_NOT_GAME_UI",typeof(RectTransform),typeof(Canvas),typeof(Image));go.transform.SetParent(parent,false);
            var canvas=go.GetComponent<Canvas>();canvas.overrideSorting=true;canvas.sortingOrder=32000;
            var r=(RectTransform)go.transform;r.anchorMin=Vector2.zero;r.anchorMax=new Vector2(1,0);r.pivot=new Vector2(.5f,0);
            float scale=parent.GetComponentInParent<Canvas>().scaleFactor;r.anchoredPosition=Vector2.zero;r.sizeDelta=new Vector2(0,18/scale);
            go.GetComponent<Image>().color=synthetic?new Color32(255,218,82,255):new Color32(135,220,184,255);
            go.GetComponent<Image>().raycastTarget=false;
            var label=new GameObject("Provenance",typeof(RectTransform),typeof(Text));label.transform.SetParent(go.transform,false);
            var rect=(RectTransform)label.transform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=new Vector2(4,0);rect.offsetMax=new Vector2(-4,0);
            var text=label.GetComponent<Text>();text.font=font;text.fontSize=Mathf.Max(1,Mathf.RoundToInt(13/scale));text.color=Color.black;text.alignment=TextAnchor.MiddleLeft;text.raycastTarget=false;
            text.text=(synthetic?"QA 合成夹具 / 非正常经济流程":"QA 真实新局/对话草稿状态")+" · Editor离屏Canvas · "+width+"×"+height+" · "+phase;
            return go;
        }
        private static void AssertVisible(RectTransform control,Camera camera,string name,List<string> assertions)
        {
            Assert.That(control,Is.Not.Null,name);Assert.That(control.gameObject.activeInHierarchy,Is.True,name);
            var corners=new Vector3[4];control.GetWorldCorners(corners);
            foreach(var corner in corners)
            {
                var p=camera.WorldToViewportPoint(corner);
                Assert.That(p.z,Is.GreaterThan(0),name+" in front of capture camera");
                Assert.That(p.x,Is.InRange(-.002f,1.002f),name+" horizontal viewport");Assert.That(p.y,Is.InRange(-.002f,1.002f),name+" vertical viewport");
            }
            assertions.Add(name+" fully inside capture viewport");
        }
        private static void AssertInside(RectTransform control,RectTransform viewport,string name,List<string> assertions)
        {
            var corners=new Vector3[4];control.GetWorldCorners(corners);
            foreach(var corner in corners)
            {
                var p=viewport.InverseTransformPoint(corner);
                Assert.That(p.x,Is.InRange(viewport.rect.xMin-.5f,viewport.rect.xMax+.5f),name+" horizontal scroll viewport");
                Assert.That(p.y,Is.InRange(viewport.rect.yMin-.5f,viewport.rect.yMax+.5f),name+" vertical scroll viewport");
            }
            assertions.Add(name+" fully inside scroll viewport after scrolling to bottom");
        }
        private static void AssertTopClick(Button button,Camera camera,List<string> assertions)
        {
            Assert.That(EventSystem.current,Is.Not.Null,"Use the authored scene EventSystem.");
            var rect=(RectTransform)button.transform;
            var pointer=new PointerEventData(EventSystem.current) { position=RectTransformUtility.WorldToScreenPoint(camera,rect.TransformPoint(rect.rect.center)) };
            var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(pointer,hits);
            Assert.That(hits.Count,Is.GreaterThan(0),button.name+" has a UI raycast hit.");
            var click=ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject);
            Assert.That(click,Is.EqualTo(button.gameObject),button.name+" top hit belongs to actual choice, got "+hits[0].gameObject.name);
            assertions.Add(button.name+" owns top click raycast ("+hits[0].gameObject.name+")");
        }
        private static Font FindChineseFont(Component source)
        {
            foreach(var text in source.GetComponentsInChildren<Text>(true))if(text.font!=null&&text.font.name!="LegacyRuntime"&&text.font.HasCharacter('猫'))return text.font;
            Assert.Fail("The authored Core Canvas must provide its actual Chinese font.");return null;
        }
        private static MonoBehaviour FindComponent(string typeName)
        {
            foreach(var component in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
                if(component!=null&&component.GetType().Name==typeName)return component;
            Assert.Fail("Missing actual scene component: "+typeName);return null;
        }
        private static Type FindType(string name)
        {
            foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies()) { var type=assembly.GetType(name);if(type!=null)return type; }
            throw new InvalidOperationException("Missing runtime type: "+name);
        }
        private static object[] Items(object target,string property)
        { var list=new List<object>();foreach(var value in (IEnumerable)Get(target,property))list.Add(value);return list.ToArray(); }
        private static object Get(object target,string name)=>target.GetType().GetProperty(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(target);
        private static T Get<T>(object target,string name)=>(T)Get(target,name);
        private static void SetProperty(object target,string name,object value)=>target.GetType().GetProperty(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).SetValue(target,value);
        private static object GetField(object target,string name)=>Field(target.GetType(),name).GetValue(target);
        private static void SetField(object target,string name,object value)=>Field(target.GetType(),name).SetValue(target,value);
        private static FieldInfo Field(Type type,string name)
        { for(;type!=null;type=type.BaseType) { var field=type.GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly);if(field!=null)return field; }throw new MissingFieldException(name); }
        private static object Call(object target,string name,params object[] args)
        { try{return Method(target.GetType(),name,args.Length).Invoke(target,args);}catch(TargetInvocationException error){throw error.InnerException??error;} }
        private static object CallStatic(Type type,string name,params object[] args)
        { try{return Method(type,name,args.Length).Invoke(null,args);}catch(TargetInvocationException error){throw error.InnerException??error;} }
        private static MethodInfo Method(Type type,string name,int arity)
        {
            foreach(var method in type.GetMethods(BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic))if(method.Name==name&&method.GetParameters().Length==arity)return method;
            throw new MissingMethodException(type.FullName,name+"/"+arity);
        }
    }
}
