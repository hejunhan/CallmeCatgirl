using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace CallmeCatgirl.Tests
{
    public sealed class MemoryUpgradeTransactionRegressionTests
    {
        private static MonoBehaviour Find(string name) => Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None).Single(o => o != null && o.GetType().Name == name);
        private static T Get<T>(object target, string name) => (T)target.GetType().GetProperty(name).GetValue(target);
        private static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name).Invoke(target, args);
        private static Action CompletionSnapshot(object game)
        {
            for (var type = game.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField("InstallCompleted", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null) return (Action)field.GetValue(game);
            }
            throw new InvalidOperationException("Missing completion event backing field.");
        }
        private static void StartInstall(MonoBehaviour owner)
        {
            owner.GetType().GetProperty("ManualCommandClock").SetValue(owner, true);
            Call(owner, "OpenMemoryUpgrade"); Call(owner, "UpgradeMemory");
            Assert.That(Get<bool>(owner, "IsMemoryInstallOpen"), Is.True);
        }

        [UnityTest] public IEnumerator OldCompletionCannotCommitAReopenedInstallation()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity");
            yield return new EnterPlayMode(); yield return null;
            var owner = Find("CoreGameplayController"); var game = Find("StandaloneMemoryInstall");
            int funds = Get<int>(owner, "Funds"), level = Get<int>(owner, "MemoryLevel");
            StartInstall(owner); var oldCompletion = CompletionSnapshot(game);
            Assert.That(oldCompletion, Is.Not.Null);
            oldCompletion();
            Assert.That(Get<int>(owner, "Funds"), Is.EqualTo(funds), "A callback alone cannot substitute for finishing or explicitly skipping the minigame.");
            Assert.That(Get<bool>(owner, "IsMemoryInstallPending"), Is.True);
            Call(owner, "CloseMemoryUpgrade"); StartInstall(owner);
            oldCompletion();
            Assert.That(Get<int>(owner, "Funds"), Is.EqualTo(funds), "A callback from the cancelled intent must not charge the new intent.");
            Assert.That(Get<int>(owner, "MemoryLevel"), Is.EqualTo(level));
            Assert.That(Get<bool>(owner, "IsMemoryInstallOpen"), Is.True);
            Call(game, "SkipInstall");
            Assert.That(Get<int>(owner, "MemoryLevel"), Is.EqualTo(level + 1));
            yield return new ExitPlayMode();
        }

        [UnityTest] public IEnumerator DisablingOwnerCancelsInstallationWithoutCharging()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity");
            yield return new EnterPlayMode(); yield return null;
            var owner = Find("CoreGameplayController"); var game = Find("StandaloneMemoryInstall");
            int funds = Get<int>(owner, "Funds"), level = Get<int>(owner, "MemoryLevel");
            StartInstall(owner); owner.enabled = false;
            Assert.That(Get<bool>(game, "IsOpen"), Is.False);
            Call(owner, "UpgradeMemory");
            Assert.That(Get<bool>(game, "IsOpen"), Is.False, "Disabled owners must not reopen a new transaction through a UI callback.");
            Call(game, "SkipInstall");
            Assert.That(Get<int>(owner, "Funds"), Is.EqualTo(funds)); Assert.That(Get<int>(owner, "MemoryLevel"), Is.EqualTo(level));
            owner.enabled = true; StartInstall(owner); Call(game, "SkipInstall");
            Assert.That(Get<int>(owner, "MemoryLevel"), Is.EqualTo(level + 1));
            yield return new ExitPlayMode();
        }

        [UnityTest] public IEnumerator DisablingMinigameCancelsInstallationWithoutCharging()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity");
            yield return new EnterPlayMode(); yield return null;
            var owner = Find("CoreGameplayController"); var game = Find("StandaloneMemoryInstall");
            int funds = Get<int>(owner, "Funds"), level = Get<int>(owner, "MemoryLevel");
            StartInstall(owner); var abortedCompletion = CompletionSnapshot(game);
            game.enabled = false; abortedCompletion();
            Assert.That(Get<int>(owner, "Funds"), Is.EqualTo(funds), "Cancellation must be safe before the next controller update.");
            yield return null;
            Assert.That(Get<bool>(game, "IsOpen"), Is.False);
            Call(owner, "UpgradeMemory");
            Assert.That(Get<bool>(game, "IsOpen"), Is.False, "A disabled minigame must fail without leaving an open installation.");
            Assert.That(Get<bool>(owner, "IsMemoryInstallPending"), Is.False);
            Call(game, "SkipInstall");
            Assert.That(Get<int>(owner, "Funds"), Is.EqualTo(funds)); Assert.That(Get<int>(owner, "MemoryLevel"), Is.EqualTo(level));
            game.enabled = true; StartInstall(owner); Call(game, "SkipInstall");
            Assert.That(Get<int>(owner, "MemoryLevel"), Is.EqualTo(level + 1));
            yield return new ExitPlayMode();
        }

        [UnityTest] public IEnumerator SceneSwitchAbortsPendingUpgradeAndInvalidatesOldCompletion()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity");
            yield return new EnterPlayMode(); yield return null;
            var owner = Find("CoreGameplayController"); var game = Find("StandaloneMemoryInstall");
            var economy = Get<object>(owner, "Economy"); var simulation = Get<object>(owner, "Simulation");
            int funds = Get<int>(economy, "CurrentFunds"), cells = Get<int>(simulation, "OpenCellCount");
            StartInstall(owner); var oldCompletion = CompletionSnapshot(game);
            EditorSceneManager.LoadSceneInPlayMode("Assets/_Game/Scenes/Prototypes/MemoryInstallScene.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            oldCompletion();
            Assert.That(Get<int>(economy, "CurrentFunds"), Is.EqualTo(funds));
            Assert.That(Get<int>(simulation, "OpenCellCount"), Is.EqualTo(cells));
            Assert.That(Find("StandaloneMemoryInstall"), Is.Not.SameAs(game));
            Assert.That(Get<bool>(Find("StandaloneMemoryInstall"), "IsOpen"), Is.True);
            yield return new ExitPlayMode();
        }

        [UnityTest] public IEnumerator MissingInstallControlDoesNotStrandIntentOrDuplicateBindingsOnRetry()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity");
            yield return new EnterPlayMode(); yield return null;
            var owner = Find("CoreGameplayController"); var game = Find("StandaloneMemoryInstall");
            FieldInfo control = null;
            for (var type = game.GetType(); type != null && control == null; type = type.BaseType)
                control = type.GetField("pressRightButton", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            Assert.That(control, Is.Not.Null); var original = control.GetValue(game);
            int funds = Get<int>(owner, "Funds"), level = Get<int>(owner, "MemoryLevel");
            try
            {
                control.SetValue(game, null);
                Call(owner, "OpenMemoryUpgrade"); Call(owner, "UpgradeMemory");
                Assert.That(Get<bool>(game, "IsOpen"), Is.False);
                Assert.That(Get<bool>(owner, "IsMemoryInstallPending"), Is.False);
                Assert.That(Get<int>(owner, "Funds"), Is.EqualTo(funds)); Assert.That(Get<int>(owner, "MemoryLevel"), Is.EqualTo(level));
            }
            finally { control.SetValue(game, original); }
            StartInstall(owner); Call(game, "SkipInstall"); Call(game, "SkipInstall");
            Assert.That(Get<int>(owner, "MemoryLevel"), Is.EqualTo(level + 1));
            int charged = Get<int>(owner, "Funds"); Assert.That(charged, Is.LessThan(funds));
            Call(game, "SkipInstall"); Assert.That(Get<int>(owner, "Funds"), Is.EqualTo(charged));
            yield return new ExitPlayMode();
        }

        [UnityTearDown] public IEnumerator Cleanup()
        {
            if (EditorApplication.isPlaying) yield return new ExitPlayMode();
        }
    }
}
