using System.Collections;
using System.Collections.Generic;
using CallmeCatgirl.UI.SystemMenu;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEditor.SceneManagement;
using UnityEngine.TestTools;

namespace CallmeCatgirl.Tests.SystemMenu
{
    /// <summary>In-memory UI only: never touches Screen, AudioListener, PlayerPrefs or a run save.</summary>
    public sealed class SystemMenuUxRegressionTests
    {
        private GameObject canvasObject, eventObject;
        private EventSystem previousEvents;
        private SystemMenuHost host;
        private SystemSettingsService settings;
        private Button previous;

        private sealed class Backend : ISystemSettingsBackend
        {
            public DisplayConfiguration CurrentDisplay { get; private set; } = new DisplayConfiguration(1280, 720, GameWindowMode.Windowed);
            public IReadOnlyList<DisplayResolution> AvailableResolutions => new[] { new DisplayResolution(1280, 720), new DisplayResolution(1920, 1080) };
            public float MasterVolume { get; set; } = .7f;
            public int TargetFrameRate { get; set; } = -1;
            public int VSyncCount { get; set; }
            public void SetDisplay(DisplayConfiguration value) { CurrentDisplay = value; }
        }
        private sealed class Store : ISystemSettingsStore
        {
            public bool TryLoad(out SystemSettingsSnapshot value) { value = null; return false; }
            public void Save(SystemSettingsSnapshot value) { }
        }

        [UnitySetUp]
        public IEnumerator CreateUi()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode();
            previousEvents = EventSystem.current;
            eventObject = new GameObject("UxRegressionEvents", typeof(EventSystem));
            EventSystem.current = eventObject.GetComponent<EventSystem>();
            canvasObject = new GameObject("UxRegressionCanvas", typeof(RectTransform), typeof(Canvas));
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var parent = canvasObject.GetComponent<RectTransform>(); parent.sizeDelta = new Vector2(1280, 720);
            var source = new GameObject("PreviousControl", typeof(RectTransform), typeof(Image), typeof(Button));
            source.transform.SetParent(parent, false); previous = source.GetComponent<Button>();
            ((RectTransform)source.transform).sizeDelta = new Vector2(120, 50);
            ((RectTransform)source.transform).anchoredPosition = new Vector2(0, -330);
            settings = new SystemSettingsService(new Backend(), new Store());
            // Explicit in-memory authored fixture; production still rejects missing saved UI at runtime.
            var fixture = new GameObject("AuthoredMenuFixture", typeof(RectTransform), typeof(SystemMenuHost));
            fixture.transform.SetParent(parent, false);
            var fixtureRect = fixture.GetComponent<RectTransform>(); fixtureRect.anchorMin = Vector2.zero; fixtureRect.anchorMax = Vector2.one; fixtureRect.offsetMin = fixtureRect.offsetMax = Vector2.zero;
            fixture.GetComponent<SystemMenuHost>().PrepareSceneLayout(UxOffscreenCapture.ChineseFont());
            host = SystemMenuHost.Create(parent, UxOffscreenCapture.ChineseFont(), true,
                service: settings, applySavedSettings: false);
            EventSystem.current.SetSelectedGameObject(previous.gameObject);
        }

        [UnityTearDown]
        public IEnumerator DestroyUi()
        {
            if (canvasObject != null) Object.DestroyImmediate(canvasObject);
            if (eventObject != null) Object.DestroyImmediate(eventObject);
            if (previousEvents != null) EventSystem.current = previousEvents;
            yield return new ExitPlayMode();
        }

        [Test]
        public void DisplayPreviewBlocksUnderlyingSettingsThenRestoresThem()
        {
            host.OpenSettings();
            var slider = host.GetComponentInChildren<Slider>(true);
            Assert.That(slider.IsInteractable(), Is.True);
            settings.PreviewDisplay(new DisplayConfiguration(1920, 1080, GameWindowMode.Windowed));
            Assert.That(slider.IsInteractable(), Is.False, "The confirmation modal must isolate keyboard input as well as pointer input.");
            UxOffscreenCapture.Save(canvasObject.GetComponent<RectTransform>(), "fixture_display-preview",
                "Production SystemMenuHost; in-memory settings backend; confirmation isolates the settings behind it.");
            settings.RevertDisplayPreview();
            Assert.That(slider.IsInteractable(), Is.True);
            Assert.That(host.IsSettingsOpen, Is.True);
            Assert.That(host.IsPauseClaimed, Is.True);
        }

        [Test]
        public void PreviewDirectionalNavigationStaysInsideConfirmation()
        {
            host.OpenSettings();
            settings.PreviewDisplay(new DisplayConfiguration(1920, 1080, GameWindowMode.Windowed));
            Canvas.ForceUpdateCanvases();
            var scope = host.transform.Find("SharedSystemSettings/DisplayPreviewConfirmation");
            foreach (var button in scope.GetComponentsInChildren<Button>())
            foreach (var direction in new[] { MoveDirection.Up, MoveDirection.Down, MoveDirection.Left, MoveDirection.Right })
            {
                EventSystem.current.SetSelectedGameObject(button.gameObject);
                var move = new AxisEventData(EventSystem.current) { moveDir = direction };
                ExecuteEvents.Execute(button.gameObject, move, ExecuteEvents.moveHandler);
                var selected = EventSystem.current.currentSelectedGameObject;
                Assert.That(selected != null && selected.transform.IsChildOf(scope), Is.True,
                    button.name + " / " + direction + " must not focus an obscured or external control.");
            }
        }

        [Test]
        public void ClosingMenuDoesNotRestoreDisabledPreviousSelection()
        {
            host.OpenSettings(); previous.interactable = false;
            host.CloseSettings();
            Assert.That(EventSystem.current.currentSelectedGameObject, Is.Null);
        }

        [Test]
        public void ClosingMenuRestoresUsablePreviousSelection()
        {
            host.OpenSettings(); host.CloseSettings();
            Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(previous.gameObject));
        }

        [Test]
        public void DisablingMenuRevertsDisplayAndReleasesItsPauseClaim()
        {
            host.OpenSettings();
            var original = settings.ConfirmedDisplay;
            settings.PreviewDisplay(new DisplayConfiguration(1920, 1080, GameWindowMode.Windowed));
            host.gameObject.SetActive(false);
            Assert.That(settings.IsDisplayPreviewPending, Is.False);
            Assert.That(settings.Current.Display, Is.EqualTo(original));
            Assert.That(host.IsPauseClaimed, Is.False);
        }

        [Test]
        public void SaveAndReturnRejectsReentryAndStillAllowsRetryAfterFailure()
        {
            int calls = 0; bool nestedAccepted = false;
            SystemMenuSession session = null;
            session = new SystemMenuSession(true, saveAndReturnToMenu: () => {
                calls++;
                if (calls == 1) { nestedAccepted = session.SaveAndReturn(); return MenuSaveResult.Failure("测试写入失败"); }
                return MenuSaveResult.Success;
            });
            session.OpenPause();
            Assert.That(session.SaveAndReturn(), Is.False);
            Assert.That(calls, Is.EqualTo(1), "A reentrant UI callback must not save or navigate twice.");
            Assert.That(nestedAccepted, Is.False);
            Assert.That(session.IsPauseOpen, Is.True);
            Assert.That(session.SaveError, Is.EqualTo("测试写入失败"));
            Assert.That(session.SaveAndReturn(), Is.True);
            Assert.That(calls, Is.EqualTo(2));
            Assert.That(session.IsPauseOpen, Is.False);
        }
    }
}
