using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace CallmeCatgirl.UI
{
    /// <summary>
    /// Lightweight state-machine prototype for the memory installation feel.
    /// It deliberately uses UI silhouettes instead of rigidbody physics.
    /// </summary>
    public sealed class MemoryInstallChoicePrototype : MonoBehaviour
    {
        private enum Stage { Empty, LeftIn, RightIn, Seated }

        private Stage stage;
        private int chance;
        private int? lastSide;
        private Coroutine shake;

        private Image stateView;
        private Sprite[] stateSprites;
        private bool middleBlocked;

        private Image ram;
        private Image leftClip;
        private Image rightClip;
        private Image slotInner;
        private Text chanceText;
        private Text stateText;
        private Button leftButton;
        private Button middleButton;
        private Button rightButton;

        private void Awake()
        {
            BuildUi();
            ResetState();
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame) ResetState();
        }

        private void BuildUi()
        {
            var canvasObject = new GameObject("MemoryInstallChoiceCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;

            if (FindFirstObjectByType<EventSystem>() == null)
            {
                var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                eventSystem.transform.SetParent(transform, false);
            }

            var background = MakeImage("Background", canvasObject.transform, new Vector2(1920, 1080), Vector2.zero,
                new Color32(242, 240, 233, 255));

            var title = MakeText("Title", canvasObject.transform, "内存安装", 34, new Vector2(0, 430),
                new Vector2(900, 60), new Color32(54, 51, 47, 255));
            title.alignment = TextAnchor.MiddleCenter;

            // Slot silhouette.
            MakeImage("SlotOuter", canvasObject.transform, new Vector2(860, 90), new Vector2(0, -85),
                new Color32(196, 190, 179, 255));
            slotInner = MakeImage("SlotInner", canvasObject.transform, new Vector2(790, 26), new Vector2(0, -57),
                new Color32(92, 88, 80, 255));
            MakeImage("CenterPeg", canvasObject.transform, new Vector2(64, 66), new Vector2(0, -96),
                new Color32(169, 163, 151, 255));

            // RAM silhouette. Pivot is centered so left/right insertion tilts naturally.
            ram = MakeImage("RamModule", canvasObject.transform, new Vector2(680, 160), new Vector2(0, 120),
                new Color32(49, 52, 57, 255));
            var notch = MakeImage("RamNotch", ram.transform, new Vector2(78, 26), new Vector2(0, -66),
                new Color32(242, 240, 233, 255));

            leftClip = MakeImage("LeftClip", canvasObject.transform, new Vector2(96, 64), new Vector2(-355, -66),
                new Color32(151, 143, 129, 255));
            rightClip = MakeImage("RightClip", canvasObject.transform, new Vector2(96, 64), new Vector2(355, -66),
                new Color32(151, 143, 129, 255));

            stateSprites = new[]
            {
                Resources.Load<Sprite>("MemoryInstall/States/empty"),
                Resources.Load<Sprite>("MemoryInstall/States/aligned"),
                Resources.Load<Sprite>("MemoryInstall/States/left"),
                Resources.Load<Sprite>("MemoryInstall/States/right"),
                Resources.Load<Sprite>("MemoryInstall/States/seated"),
                Resources.Load<Sprite>("MemoryInstall/States/middle")
            };
            stateView = MakeImage("StateView", canvasObject.transform, new Vector2(1080, 800), new Vector2(0, 20),
                Color.white);
            stateView.preserveAspect = true;
            stateView.sprite = stateSprites[1];
            foreach (var oldName in new[] { "SlotOuter", "SlotInner", "CenterPeg", "RamModule", "LeftClip", "RightClip" })
            {
                var old = canvasObject.transform.Find(oldName);
                if (old != null) old.gameObject.SetActive(false);
            }

            leftButton = MakeButton("LeftButton", canvasObject.transform, "左边用力", new Vector2(-300, -330),
                new Color32(214, 198, 166, 255), () => PressSide(0));
            middleButton = MakeButton("MiddleButton", canvasObject.transform, "中间用力", new Vector2(0, -330),
                new Color32(206, 142, 118, 255), PressMiddle);
            rightButton = MakeButton("RightButton", canvasObject.transform, "右边用力", new Vector2(300, -330),
                new Color32(214, 198, 166, 255), () => PressSide(1));

            chanceText = MakeText("Chance", canvasObject.transform, "", 26, new Vector2(0, -245),
                new Vector2(800, 44), new Color32(63, 58, 52, 255));
            chanceText.alignment = TextAnchor.MiddleCenter;

            stateText = MakeText("State", canvasObject.transform, "", 24, new Vector2(0, 340),
                new Vector2(1000, 46), new Color32(63, 58, 52, 255));
            stateText.alignment = TextAnchor.MiddleCenter;
        }

        private void PressSide(int side)
        {
            if (stage == Stage.Seated) return;

            if (lastSide.HasValue && lastSide.Value != side)
                chance = Mathf.Min(100, chance + 2);
            lastSide = side;
            stage = side == 0 ? Stage.LeftIn : Stage.RightIn;
            middleBlocked = false;
            ApplyVisual();
            stateText.color = new Color32(63, 58, 52, 255);
            stateText.text = side == 0 ? "左端进入" : "右端进入";
        }

        private void PressMiddle()
        {
            if (stage == Stage.Seated) return;

            if (chance <= 0)
            {
                FailMiddle("中间压不动");
                return;
            }

            if (Random.value * 100f < chance)
            {
                stage = Stage.Seated;
                middleBlocked = false;
                ApplyVisual();
                stateText.color = new Color32(54, 120, 74, 255);
                stateText.text = "已插入";
                leftButton.interactable = false;
                middleButton.interactable = false;
                rightButton.interactable = false;
            }
            else
            {
                FailMiddle("中间压不动");
            }
        }

        private void FailMiddle(string message)
        {
            chance = Mathf.Max(0, chance - 10);
            middleBlocked = true;
            ApplyVisual();
            stateText.color = new Color32(155, 62, 48, 255);
            stateText.text = $"{message} · {chance}%";
            if (shake != null) StopCoroutine(shake);
            shake = StartCoroutine(ShakeRam());
        }

        private IEnumerator ShakeRam()
        {
            var start = stateView.rectTransform.anchoredPosition;
            for (int i = 0; i < 6; i++)
            {
                stateView.rectTransform.anchoredPosition = start + new Vector2(i % 2 == 0 ? 8 : -8, 0);
                yield return new WaitForSecondsRealtime(.035f);
            }
            stateView.rectTransform.anchoredPosition = start;
            shake = null;
        }

        private void ResetState()
        {
            stage = Stage.Empty;
            chance = 0;
            lastSide = null;
            middleBlocked = false;
            if (shake != null) { StopCoroutine(shake); shake = null; }
            if (leftButton != null) leftButton.interactable = true;
            if (middleButton != null) middleButton.interactable = true;
            if (rightButton != null) rightButton.interactable = true;
            ApplyVisual();
            if (stateText != null)
            {
                stateText.color = new Color32(63, 58, 52, 255);
                stateText.text = "未插入";
            }
        }

        private void ApplyVisual()
        {
            if (stateView == null) return;

            int index;
            if (middleBlocked) index = 5;
            else if (stage == Stage.Seated) index = 4;
            else if (stage == Stage.LeftIn) index = 2;
            else if (stage == Stage.RightIn) index = 3;
            else index = 1;

            if (stateSprites != null && index < stateSprites.Length && stateSprites[index] != null)
                stateView.sprite = stateSprites[index];
            if (chanceText != null) chanceText.text = $"中间成功率：{chance}%";
        }

        private static Image MakeImage(string name, Transform parent, Vector2 size, Vector2 position, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static Text MakeText(string name, Transform parent, string content, int size, Vector2 position,
            Vector2 box, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = box;
            rect.anchoredPosition = position;
            var text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.text = content;
            text.color = color;
            text.raycastTarget = false;
            return text;
        }

        private static Button MakeButton(string name, Transform parent, string label, Vector2 position,
            Color color, UnityEngine.Events.UnityAction action)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = new Vector2(250, 86);
            rect.anchoredPosition = position;
            var image = go.GetComponent<Image>();
            image.color = color;
            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(action);

            var text = MakeText("Label", go.transform, label, 26, Vector2.zero, new Vector2(240, 80),
                new Color32(48, 43, 38, 255));
            text.alignment = TextAnchor.MiddleCenter;
            return button;
        }
    }
}
