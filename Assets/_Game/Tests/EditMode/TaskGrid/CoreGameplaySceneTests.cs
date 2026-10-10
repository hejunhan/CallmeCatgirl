using System;
using System.Collections;
using System.Linq;
using CallmeCatgirl.Gameplay.TaskGrid;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace CallmeCatgirl.Tests
{
    public sealed class CoreGameplaySceneTests
    {
        [UnityTest]
        public IEnumerator SavedSceneStartsAndUsesRealConfiguration()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity");
            yield return new EnterPlayMode();
            {
                var controller = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                    .Single(b => b.GetType().FullName == "CallmeCatgirl.Gameplay.CoreGameplayController");
                var sim = (TaskSimulation)controller.GetType().GetProperty("Simulation").GetValue(controller);
                Assert.That(sim, Is.Not.Null); Assert.That(sim.Tasks.Count, Is.EqualTo(3));
                Assert.That(sim.IsOpen(new Cell(0, 0)), Is.False);
                Assert.That(sim.TryPlace("A-1", new Cell(2, 0), 0, out _), Is.True);
                Assert.That(sim.TryPlace("B-1", new Cell(2, 3), 0, out _), Is.True);
                sim.Paused = false; sim.Advance(10); sim.Paused = true;
                Assert.That(sim.Tasks["A-1"].State, Is.EqualTo(RequestState.Ready));
                Assert.That(sim.Tasks["A-1"].GetAbility(AbilityKind.Writing), Is.EqualTo(12));
                Assert.That(sim.Tasks["A-1"].GetAbility(AbilityKind.Code), Is.EqualTo(3));
                Assert.That(sim.Tasks["A-1"].Result.Overall, Is.EqualTo(Quality.Complete));
                Assert.That(sim.Tasks["B-1"].Result.Overall, Is.EqualTo(Quality.BelowStandard));
                Assert.That(sim.TrySubmit("A-1"), Is.True);
                Assert.That(sim.TrySubmit("A-1"), Is.False);
            }
            yield return new ExitPlayMode();
        }

        [UnityTearDown]
        public IEnumerator EnsurePlayModeIsClosed()
        {
            if (UnityEditor.EditorApplication.isPlaying) yield return new ExitPlayMode();
        }
    }
}
