using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using CallmeCatgirl.Gameplay.Narrative;
using CallmeCatgirl.Gameplay.TaskGrid;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace CallmeCatgirl.Tests
{
    // Exercises authored buttons and their production callbacks in the gameplay scene.
    // This is a programmatic scene integration test, not physical mouse input.
    public sealed class GameplayPauseResumeTests
    {
        [UnityTest] public IEnumerator ContinueButtonResumesManualPauseAndIsIdempotent() => Scenario(0);
        [UnityTest] public IEnumerator EscapeAndCloseAllPreserveManualPause() => Scenario(1);
        [UnityTest] public IEnumerator ContinueFromRunningPreservesPlaybackSpeed() => Scenario(2);
        [UnityTest] public IEnumerator SettingsBackKeepsPauseUntilExplicitContinue() => Scenario(3);
        [UnityTest] public IEnumerator ContinueDoesNotReleaseAnotherModalPause() => Scenario(4);

        private static IEnumerator Scenario(int scenario)
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity");
            yield return new EnterPlayMode(); yield return null;
            var owner=UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                .Single(c=>c.GetType().Name=="CoreGameplayController");
            Call(owner,"ConfigureSaveSlot","pause-regression-"+Guid.NewGuid().ToString("N"));
            var commandType=owner.GetType().Assembly.GetType("CallmeCatgirl.Gameplay.GameCommand",true);
            Call(owner,"ExecuteCommand",JsonUtility.FromJson("{\"kind\":\"clock\",\"paused\":true}",commandType));
            var narrative=Get<NarrativeSession>(owner,"Narrative");
            Assert.That(narrative.Choose("mew"),Is.True);
            yield return null;
            for(int i=0;i<8 && narrative.CurrentFrame!=null;i++)
            {
                if(narrative.CurrentFrame.CanClose && narrative.CloseConversation()) break;
                Assert.That(narrative.CurrentFrame.Choices.Count,Is.GreaterThan(0));
                Assert.That(narrative.Choose(narrative.CurrentFrame.Choices[0].Id),Is.True);
            }
            yield return null;
            var simulation=Get<TaskSimulation>(owner,"Simulation");
            var view=Field<object>(owner,"canvasView");
            var pause=Field<Text>(view,"pauseLabel").GetComponentInParent<Button>();
            var menu=Field<object>(owner,"systemMenu");
            var resume=Field<Button>(menu,"resumeButton");
            Call(owner,"SetSpeed",0.5f);
            Assert.That(Get<bool>(owner,"IsModalOpen"),Is.False);
            Assert.That(simulation.Paused,Is.True);
            // Establish the reported path using the serialized main pause button twice:
            // initial planning -> running -> manually paused.
            pause.onClick.Invoke(); Assert.That(simulation.Paused,Is.False);
            if(scenario!=2) { pause.onClick.Invoke(); Assert.That(simulation.Paused,Is.True); }

            if(scenario==4)
            {
                Call(owner,"OpenTraining"); yield return null;
                Assert.That(Get<bool>(owner,"IsTrainingOpen"),Is.True);
            }
            Call(menu,"OpenPause");
            Assert.That(Get<bool>(owner,"HasSystemPauseClaim"),Is.True);
            double before=simulation.GameTime;
            Call(owner,"AdvanceGame",1d);
            Assert.That(simulation.GameTime,Is.EqualTo(before),"The open menu blocks the clock.");

            if(scenario==1)
            {
                Call(menu,"HandleEscape");
                Assert.That(simulation.Paused,Is.True,"Escape is a close operation, not explicit resume.");
                Call(menu,"OpenPause"); Call(menu,"CloseAll");
                Assert.That(simulation.Paused,Is.True,"A generic close must preserve manual planning.");
            }
            else
            {
                if(scenario==3)
                {
                    Field<Button>(menu,"settingsButton").onClick.Invoke();
                    Assert.That(Get<bool>(menu,"IsSettingsOpen"),Is.True);
                    Field<Button>(menu,"settingsBackButton").onClick.Invoke();
                    Assert.That(Get<bool>(menu,"IsSettingsOpen"),Is.False);
                    Assert.That(Get<bool>(menu,"IsPauseOpen"),Is.True);
                    Assert.That(simulation.Paused,Is.True,"Settings Back must not resume gameplay.");
                }
                resume.onClick.Invoke();
                Assert.That(Get<bool>(owner,"HasSystemPauseClaim"),Is.False);
                if(scenario==4)
                {
                    Assert.That(Get<bool>(owner,"IsTrainingOpen"),Is.True);
                    Assert.That(simulation.Paused,Is.True,"Continue cannot release the training modal's pause.");
                }
                else
                {
                    Assert.That(simulation.Paused,Is.False,
                        "Explicit Continue must release manual pause, not require a second click on Pause.");
                    resume.onClick.Invoke();
                    Assert.That(simulation.Paused,Is.False,"Repeated Continue must never toggle back to paused.");
                }
            }
            Assert.That(Get<float>(owner,"PlaybackSpeed"),Is.EqualTo(0.5f));
            Call(owner,"AdvanceGame",1d);
            Assert.That(simulation.GameTime,Is.EqualTo(before+((scenario==1||scenario==4)?0:1)));
            if(scenario==4)
            {
                Call(Field<object>(owner,"trainingPanel"),"Close"); yield return null;
                Assert.That(Get<bool>(owner,"IsTrainingOpen"),Is.False);
                Assert.That(simulation.Paused,Is.True,"Closing training preserves its original manual planning state.");
                pause.onClick.Invoke();
                Assert.That(simulation.Paused,Is.False,"The main Continue button still resumes after the other modal closes.");
                Call(owner,"AdvanceGame",1d);
                Assert.That(simulation.GameTime,Is.EqualTo(before+1));
            }
            yield return new ExitPlayMode();
        }
        private static T Get<T>(object target,string name)=>(T)target.GetType().GetProperty(name).GetValue(target);
        private static T Field<T>(object target,string name)=>(T)target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);
        private static object Call(object target,string name,params object[] args)=>target.GetType()
            .GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
            .Single(m=>m.Name==name && m.GetParameters().Length==args.Length).Invoke(target,args);
        [UnityTearDown] public IEnumerator Cleanup(){if(EditorApplication.isPlaying)yield return new ExitPlayMode();}
    }
}
