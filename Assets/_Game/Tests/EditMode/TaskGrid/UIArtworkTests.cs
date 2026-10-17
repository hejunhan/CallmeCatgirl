using System.Collections;
using System.Linq;
using System.Reflection;
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
    public sealed class UIArtworkTests
    {
        private const string Scene = "Assets/_Game/Scenes/Prototypes/CoreGameplay.unity";
        private static MonoBehaviour Named(string type) => Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None).First(c => c.GetType().Name == type);
        private static Button Button(string name) => Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None).Single(b => b.name == name);
        private static Component Skin(Button button) => button.GetComponents<Component>().Single(c => c.GetType().Name == "ButtonArtwork");
        private static void Call(object target, string method, params object[] args) => target.GetType().GetMethod(method).Invoke(target, args);
        private static bool Selected(Button button) => (bool)Skin(button).GetType().GetProperty("IsSelected").GetValue(Skin(button));
        private static string Sprite(Button button) => button.GetComponent<Image>().sprite.name;
        private static void Capture(string name)
        {
            var capture = typeof(SystemFeedbackTests).GetMethod("Capture", BindingFlags.NonPublic | BindingFlags.Static);
            // 首次变更截图画布尺寸后先预热文字图集，第二次才保存最终预览。
            capture.Invoke(null, new object[] { name });
            capture.Invoke(null, new object[] { name });
        }

        [Test] public void EverySceneButtonHasValidMatchingSpritesAndKeepsItsClickHandler()
        {
            EditorSceneManager.OpenScene(Scene);
            var buttons = Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Assert.That(buttons.Length, Is.EqualTo(26));
            foreach (var button in buttons)
            {
                var settings = new SerializedObject(Skin(button));
                foreach (string field in new[] { "normalSprite", "selectedSprite", "disabledSprite" })
                    Assert.That(settings.FindProperty(field).objectReferenceValue, Is.Not.Null, button.name + ": " + field);
                Assert.That(button.GetComponent<Image>().sprite, Is.Not.Null, button.name);
                if (button.name != "ChoiceTemplate" && !button.name.StartsWith("Allocate") && button.name != "CloseHistory")
                    Assert.That(button.onClick.GetPersistentEventCount(), Is.GreaterThan(0), button.name);
            }
            var frames = Object.FindObjectsByType<Image>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(i => i.name == "WindowArtwork").ToArray();
            Assert.That(frames.Length, Is.EqualTo(2));
            foreach(var frame in frames)
            {
                Assert.That(frame.sprite.name, Is.EqualTo("im_window_1920x1080"));
                Assert.That(frame.raycastTarget, Is.False);
                Assert.That(frame.type, Is.EqualTo(Image.Type.Sliced));
                Assert.That(frame.transform.GetSiblingIndex(), Is.Zero);
                Assert.That(new[]{"MemoryUpgradePopup", "ConversationPopup"}, Does.Contain(frame.transform.parent.parent.name));
            }
            Assert.That(GameObject.Find("WorkbenchBackground").GetComponent<Image>().sprite.name, Is.EqualTo("background_1920x1080"));
            Assert.That(GameObject.Find("WorkbenchBackground").GetComponent<Image>().raycastTarget, Is.False);
            Assert.That(Sprite(Button("ResetButton")), Is.EqualTo("button_reset"));
            Assert.That(Sprite(Button("AddRequestButton")), Is.EqualTo("button_new_request"));
            Assert.That(Sprite(Button("CloseButton")), Is.EqualTo("button_blank"));
            var icon = Button("CloseButton").transform.Find("ArtworkIcon").GetComponent<Image>();
            Assert.That(icon.sprite.name, Is.EqualTo("icon_close"));
            Assert.That(icon.raycastTarget, Is.False);
        }

        [UnityTest] public IEnumerator SpeedAndPauseArtworkFollowGameplayAfterClicksFocusChangesAndReset()
        {
            EditorSceneManager.OpenScene(Scene); yield return new EnterPlayMode(); yield return null;
            var owner = Named("CoreGameplayController");
            var names = new[] { "0.5SpeedButton", "SpeedButton", "Speed2Button", "Speed4Button" };
            var values = new[] { .5f, 1f, 2f, 4f };
            Assert.That(Selected(Button(names[0])), Is.True);
            Assert.That(Selected(Button("PauseButton")), Is.True);
            Assert.That(Button("PauseButton").GetComponentInChildren<Text>().enabled, Is.True);
            Assert.That(Button("PauseButton").GetComponentInChildren<Text>().text, Is.EqualTo("开始 [空格]"));
            Capture("ArtworkInitial.png");
            for (int i = 0; i < names.Length; i++)
            {
                Button(names[i]).onClick.Invoke();
                EventSystem.current.SetSelectedGameObject(null);
                yield return null;
                Assert.That(owner.GetType().GetProperty("PlaybackSpeed").GetValue(owner), Is.EqualTo(values[i]));
                for (int j = 0; j < names.Length; j++) Assert.That(Selected(Button(names[j])), Is.EqualTo(j == i));
                Assert.That(Sprite(Button(names[i])), Is.EqualTo(i == 0 ? "button_speed_0.5x_selected" : "button_speed_" + (int)values[i] + "x_selected"));
            }
            Button("PauseButton").onClick.Invoke();
            Assert.That(Selected(Button("PauseButton")), Is.False);
            Assert.That(Button("PauseButton").GetComponentInChildren<Text>().enabled, Is.False, "Baked pause text must not overlap a second label.");
            Assert.That(Sprite(Button("PauseButton")), Is.EqualTo("button_pause"));
            Button("PauseButton").onClick.Invoke();
            Call(owner, "ResetSimulation"); yield return null;
            Assert.That(Selected(Button(names[0])), Is.True);
            Assert.That(Selected(Button("PauseButton")), Is.True);
            yield return new ExitPlayMode();
        }

        [UnityTest] public IEnumerator DisabledButtonsAndClonedReplyChoicesKeepTheirArtwork()
        {
            EditorSceneManager.OpenScene(Scene); yield return new EnterPlayMode(); yield return null;
            var owner = Named("CoreGameplayController");
            var sim = (TaskSimulation)owner.GetType().GetProperty("Simulation").GetValue(owner);
            var task = sim.Tasks.Values.First(); Call(owner, "SelectRequest", task.Id); Call(Named("CoreGameplayCanvasView"), "Refresh");
            yield return null;
            Assert.That(Button("SubmitButton").interactable, Is.False);
            Assert.That(Sprite(Button("SubmitButton")), Is.EqualTo("button_disabled"));
            Button("UpgradeMemoryButton").onClick.Invoke(); yield return null;
            Assert.That((bool)owner.GetType().GetProperty("IsMemoryUpgradeOpen").GetValue(owner), Is.True);
            Capture("ArtworkMemory.png");
            Button("CloseMemoryUpgradeButton").onClick.Invoke();
            bool placed = false;
            for (int rotation = 0; rotation < 4 && !placed; rotation++)
                for (int i = 0; i < sim.Width * sim.Height && !placed; i++)
                    placed = sim.TryPlace(task.Id, new Cell(i % sim.Width, i / sim.Width), rotation, out _);
            Assert.That(placed, Is.True);
            sim.Paused = false; sim.Advance(task.Definition.Calculation / task.Definition.Speed); sim.Paused = true;
            Call(Named("CoreGameplayCanvasView"), "Refresh"); yield return null;
            Assert.That(Sprite(Button("SubmitButton")), Is.EqualTo("button_normal"));
            Button("SubmitButton").onClick.Invoke(); yield return null;
            var choices = Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Where(b => b.name.StartsWith("ReplyChoice_")).ToArray();
            Assert.That(choices.Length, Is.GreaterThan(0));
            foreach (var choice in choices)
            {
                Assert.That(Sprite(choice), Is.EqualTo("reply_normal"));
                var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
                ExecuteEvents.Execute(choice.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
                Assert.That(Sprite(choice), Is.EqualTo("reply_hover"));
                ExecuteEvents.Execute(choice.gameObject, pointer, ExecuteEvents.pointerDownHandler);
                Assert.That(Sprite(choice), Is.EqualTo("reply_pressed"));
                Assert.That(choice.GetComponentInChildren<Text>().color, Is.EqualTo(Color.white));
                ExecuteEvents.Execute(choice.gameObject, pointer, ExecuteEvents.pointerUpHandler);
                ExecuteEvents.Execute(choice.gameObject, pointer, ExecuteEvents.pointerExitHandler);
                Assert.That(Sprite(choice), Is.EqualTo("reply_normal"));
                Call(Skin(choice), "SetSelected", true);
                Assert.That(Sprite(choice), Is.EqualTo("reply_selected"));
                choice.interactable = false; Call(Skin(choice), "SetSelected", true);
                Assert.That(Sprite(choice), Is.EqualTo("reply_disabled"));
                choice.interactable = true; Call(Skin(choice), "SetSelected", false);
                Assert.That(choice.GetComponentInChildren<Text>().enabled, Is.True);
                Assert.That(choice.GetComponentInChildren<Text>().text, Is.Not.Empty);
            }
            Capture("ArtworkConversation.png");
            yield return new ExitPlayMode();
        }
        [UnityTearDown] public IEnumerator StopPlayMode() { if (EditorApplication.isPlaying) yield return new ExitPlayMode(); }
    }
}
