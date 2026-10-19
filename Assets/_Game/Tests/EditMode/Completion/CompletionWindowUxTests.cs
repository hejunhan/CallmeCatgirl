using System;
using System.Linq;
using System.Reflection;
using CallmeCatgirl.Gameplay.Narrative;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace CallmeCatgirl.Tests
{
    /// <summary>Production UGUI geometry and directly dispatched events; no desktop or economic-flow claims.</summary>
    public sealed class CompletionWindowUxTests
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        [Test]
        public void NarrowConversationKeepsItsCloseButtonAndHeaderInsideTheWindow()
        {
            try
            {
                var popup = OpenConversationFixture(out var window);
                window.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 420);
                window.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 340);
                Canvas.ForceUpdateCanvases(); Call(popup, "LateUpdate");
                AssertInside((RectTransform)Get<Button>(popup, "closeButton").transform, window, "Close button");
                AssertInside(Get<Text>(popup, "userLabel").rectTransform, window, "Conversation title");
                AssertInside(Get<Text>(popup, "resultLabel").rectTransform, window, "Result label");
                Capture(window, "fixture_narrow-conversation", "Production conversation UI from the isolated scene, resized to 420x340.");
            }
            finally { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); }
        }

        [Test]
        public void ConversationFitsAgainWhenItsViewportShrinksWithoutAWindowResize()
        {
            try
            {
                var popup = OpenConversationFixture(out var window);
                Call(popup, "LateUpdate");
                var parent = (RectTransform)window.parent;
                parent.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 800);
                parent.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 450);
                Canvas.ForceUpdateCanvases(); Call(popup, "LateUpdate");
                AssertInside(window, parent, "Restored conversation window");
                AssertInside((RectTransform)Get<Button>(popup, "closeButton").transform, window, "Close button after viewport shrink");
                Capture(parent, "fixture_shrunken-conversation-viewport", "Production conversation UI after its viewport shrinks to 800x450.");
            }
            finally { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); }
        }

        [Test]
        public void FloatingDragIgnoresAnUnownedPointer()
        {
            var root = new GameObject("PointerFixture", typeof(RectTransform));
            try
            {
                var bounds = root.GetComponent<RectTransform>(); bounds.sizeDelta = new Vector2(1000, 800);
                var child = new GameObject("Window", typeof(RectTransform)); child.transform.SetParent(bounds, false);
                var window = child.GetComponent<RectTransform>(); window.sizeDelta = new Vector2(420, 340);
                var gesture = child.AddComponent(FindType("CallmeCatgirl.UI.FloatingWindowGesture"));
                Call(gesture, "Bind", window, false);
                var owner = Pointer(7, new Vector2(20, 20));
                ((IBeginDragHandler)gesture).OnBeginDrag(owner);
                var before = window.anchoredPosition;
                ((IDragHandler)gesture).OnDrag(Pointer(8, new Vector2(100, 100)));
                Assert.That(window.anchoredPosition, Is.EqualTo(before), "Another touch/pointer cannot take over an active title drag.");
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void FloatingDragStopsAfterEndDrag()
        {
            var root = new GameObject("EndedPointerFixture", typeof(RectTransform));
            try
            {
                var bounds = root.GetComponent<RectTransform>(); bounds.sizeDelta = new Vector2(1000, 800);
                var child = new GameObject("Window", typeof(RectTransform)); child.transform.SetParent(bounds, false);
                var window = child.GetComponent<RectTransform>(); window.sizeDelta = new Vector2(420, 340);
                var gesture = child.AddComponent(FindType("CallmeCatgirl.UI.FloatingWindowGesture"));
                Call(gesture, "Bind", window, false);
                var owner = Pointer(7, new Vector2(20, 20));
                ((IBeginDragHandler)gesture).OnBeginDrag(owner);
                ExecuteEvents.Execute(child, owner, ExecuteEvents.endDragHandler);
                var before = window.anchoredPosition;
                ((IDragHandler)gesture).OnDrag(Pointer(7, new Vector2(100, 100)));
                Assert.That(window.anchoredPosition, Is.EqualTo(before), "A released pointer cannot resume its previous drag.");
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void OwningPointerStillMovesAndResizesFromTheSameTopLeft()
        {
            var root = new GameObject("OwnedPointerFixture", typeof(RectTransform));
            try
            {
                var bounds = root.GetComponent<RectTransform>(); bounds.sizeDelta = new Vector2(1000, 800);
                var child = new GameObject("Window", typeof(RectTransform)); child.transform.SetParent(bounds, false);
                var window = child.GetComponent<RectTransform>(); window.sizeDelta = new Vector2(420, 340);
                var gesture = child.AddComponent(FindType("CallmeCatgirl.UI.FloatingWindowGesture"));
                Call(gesture, "Bind", window, false);
                ((IBeginDragHandler)gesture).OnBeginDrag(Pointer(7, new Vector2(20, 20)));
                ((IDragHandler)gesture).OnDrag(Pointer(7, new Vector2(80, 50)));
                Assert.That(Vector2.Distance(window.anchoredPosition, new Vector2(60, 30)), Is.LessThan(.01f));
                ExecuteEvents.Execute(child, Pointer(7, new Vector2(80, 50)), ExecuteEvents.endDragHandler);
                var corners = new Vector3[4]; window.GetWorldCorners(corners); var topLeft = corners[1];
                Call(gesture, "Bind", window, true);
                ((IBeginDragHandler)gesture).OnBeginDrag(Pointer(7, new Vector2(20, 20)));
                ((IDragHandler)gesture).OnDrag(Pointer(7, new Vector2(80, -20)));
                Assert.That(Vector2.Distance(window.rect.size, new Vector2(480, 380)), Is.LessThan(.01f));
                window.GetWorldCorners(corners);
                Assert.That(Vector3.Distance(corners[1], topLeft), Is.LessThan(.01f));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void SelectingAChoiceBelowLongNarrativeScrollsItIntoView()
        {
            var root = new GameObject("LongNarrativeFixture", typeof(RectTransform), typeof(Canvas));
            try
            {
                root.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
                var bounds = root.GetComponent<RectTransform>(); bounds.sizeDelta = new Vector2(1280, 720);
                var session = NarrativeSession.AdoptLegacy(5); session.OpenContact(NarrativeSession.DoctorId);
                var type = FindType("CallmeCatgirl.Gameplay.Narrative.NarrativePanel");
                var panel = (Component)type.GetMethod("Create", BindingFlags.Public | BindingFlags.Static).Invoke(null,
                    new object[] { bounds, CaptureFont(), session });
                var body = panel.GetComponentsInChildren<Text>().Single(t => t.name == "StoryBody");
                body.text = string.Join("\n", Enumerable.Repeat("Synthetic long narrative line for scroll geometry.", 40));
                Call(panel, "FitChoices");
                var scroll = panel.GetComponentInChildren<ScrollRect>(); scroll.verticalNormalizedPosition = 1;
                Canvas.ForceUpdateCanvases();
                var choice = panel.GetComponentsInChildren<Button>().Single(b => b.name == "Choice_continue");
                var before = RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport, choice.transform);
                Assert.That(before.max.y, Is.LessThan(scroll.viewport.rect.yMin), "Fixture must really place the choice below the viewport.");
                ExecuteEvents.Execute(choice.gameObject, new BaseEventData(null), ExecuteEvents.selectHandler);
                Canvas.ForceUpdateCanvases();
                AssertInside((RectTransform)choice.transform, scroll.viewport, "Keyboard-selected narrative choice");
                Capture(bounds, "fixture_long-narrative-selected-choice", "Production narrative panel; synthetic forty-line body; normal select event reveals its choice.");
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static Component OpenConversationFixture(out RectTransform window)
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity");
            var canvas = GameObject.Find("GameplayCanvas").GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(1920, 1080);
            var popup = Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Single(x => x.GetType().Name == "ConversationPopupView");
            Get<GameObject>(popup, "panel").SetActive(true);
            window = (RectTransform)Get<Text>(popup, "userLabel").transform.parent;
            Canvas.ForceUpdateCanvases(); Call(popup, "EnsureWindowInteraction"); Canvas.ForceUpdateCanvases();
            return popup;
        }
        private static PointerEventData Pointer(int id, Vector2 position) => new PointerEventData(null)
        { pointerId = id, position = position, button = PointerEventData.InputButton.Left };
        private static Type FindType(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name, false)).First(t => t != null);
        private static Font CaptureFont() => (Font)FindType("CallmeCatgirl.Tests.SystemMenu.UxOffscreenCapture").GetMethod("ChineseFont").Invoke(null, null);
        private static void Capture(RectTransform viewport, string name, string provenance) =>
            FindType("CallmeCatgirl.Tests.SystemMenu.UxOffscreenCapture").GetMethod("Save").Invoke(null, new object[] { viewport, name, provenance });
        private static T Get<T>(object source, string field) => (T)source.GetType().GetField(field, Instance).GetValue(source);
        private static void Call(object source, string method, params object[] args) => source.GetType().GetMethod(method, Instance).Invoke(source, args);
        private static void AssertInside(RectTransform element, RectTransform container, string label)
        {
            var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(container, element);
            Assert.That(bounds.min.x, Is.GreaterThanOrEqualTo(container.rect.xMin - .5f), label + " left");
            Assert.That(bounds.max.x, Is.LessThanOrEqualTo(container.rect.xMax + .5f), label + " right");
            Assert.That(bounds.min.y, Is.GreaterThanOrEqualTo(container.rect.yMin - .5f), label + " bottom");
            Assert.That(bounds.max.y, Is.LessThanOrEqualTo(container.rect.yMax + .5f), label + " top");
        }
    }
}
