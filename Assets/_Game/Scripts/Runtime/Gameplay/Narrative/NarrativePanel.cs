using System;
using System.Collections.Generic;
using System.Linq;
using CallmeCatgirl.UI.SystemMenu;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CallmeCatgirl.Gameplay.Narrative
{
    /// <summary>Reusable, host-independent narrative window. Create below the host's modal layer.</summary>
    public sealed class NarrativePanel : MonoBehaviour
    {
        private NarrativeSession session;
        private Font font;
        private RectTransform overlay, window, content, viewport, noticeRoot;
        private Text heading, body, noticeText;
        private Button close, dismiss;
        private ScrollRect scroll;
        private readonly List<Button> choiceButtons = new List<Button>();
        private string fingerprint;
        private Vector2 lastSize;
        public bool IsOpen => overlay != null && overlay.gameObject.activeSelf;
        public NarrativeSession Session => session;

        public static NarrativePanel Create(Transform parent, Font font, NarrativeSession session)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            var root = new GameObject("NarrativePanel", typeof(RectTransform), typeof(NarrativePanel));
            root.transform.SetParent(parent, false);
            Stretch(root.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var panel = root.GetComponent<NarrativePanel>();
            panel.Bind(session, font);
            return panel;
        }
        public void Bind(NarrativeSession source, Font textFont)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (session != null) session.Changed -= Refresh;
            session = source; font = textFont != null ? textFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (overlay == null) Build();
            session.Changed += Refresh; fingerprint = null; Refresh();
        }
        public bool HandleEscape()
        {
            if (!IsOpen || session == null) return false;
            if (!string.IsNullOrEmpty(session.Notice)) { session.DismissNotice(); return true; }
            session.CloseConversation();
            // A mandatory opening/ending owns Escape even if it cannot be dismissed.
            return true;
        }
        private void OnDestroy() { if (session != null) session.Changed -= Refresh; }
        private void LateUpdate()
        {
            if (!IsOpen) return;
            if (lastSize != window.rect.size) { lastSize = window.rect.size; FitChoices(); }
        }
        private void Build()
        {
            overlay = Rect("ScreenBlocker", transform);
            Stretch(overlay, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            overlay.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, .52f);
            window = Rect("StoryWindow", overlay);
            Stretch(window, new Vector2(.14f, .1f), new Vector2(.86f, .9f), Vector2.zero, Vector2.zero);
            window.gameObject.AddComponent<Image>().color = new Color32(244, 242, 235, 255);

            heading = Label("Heading", window, 26, TextAnchor.MiddleLeft);
            Stretch(heading.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(24, -76), new Vector2(-100, -12));
            heading.raycastTarget = true;
            heading.gameObject.AddComponent<NarrativeWindowGesture>().Bind(window, overlay, false);
            close = MakeButton("Close", window, "收起", () => session.CloseConversation());
            Stretch(close.GetComponent<RectTransform>(), Vector2.one, Vector2.one, new Vector2(-88, -58), new Vector2(-16, -16));

            viewport = Rect("ScrollViewport", window);
            Stretch(viewport, Vector2.zero, Vector2.one, new Vector2(24, 24), new Vector2(-24, -84));
            viewport.gameObject.AddComponent<Image>().color = new Color(1, 1, 1, .01f);
            viewport.gameObject.AddComponent<RectMask2D>();
            scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false; scroll.vertical = true; scroll.scrollSensitivity = 36;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            content = Rect("ReadableContent", viewport);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(.5f, 1);
            content.anchoredPosition = Vector2.zero; content.sizeDelta = Vector2.zero;
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 12; layout.childControlWidth = true; layout.childControlHeight = true;
            layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
            layout.padding = new RectOffset(0, 8, 0, 16);
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport; scroll.content = content;
            body = Label("StoryBody", content, 24, TextAnchor.UpperLeft);
            body.lineSpacing = 1.22f; body.gameObject.AddComponent<LayoutElement>().minHeight = 64;
            var resize = Rect("ResizeCorner", window);
            Stretch(resize, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-24, 0), new Vector2(0, 24));
            resize.gameObject.AddComponent<Image>().color = new Color32(130, 126, 115, 255);
            resize.gameObject.AddComponent<NarrativeWindowGesture>().Bind(window, overlay, true);

            noticeRoot = Rect("BlockingNotice", overlay);
            Stretch(noticeRoot, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            noticeRoot.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, .7f);
            var noticeBox = Rect("NoticeBox", noticeRoot);
            Stretch(noticeBox, new Vector2(.25f, .32f), new Vector2(.75f, .68f), Vector2.zero, Vector2.zero);
            noticeBox.gameObject.AddComponent<Image>().color = new Color32(40, 34, 34, 255);
            noticeText = Label("NoticeText", noticeBox, 34, TextAnchor.MiddleCenter);
            noticeText.color = new Color32(255, 222, 215, 255);
            Stretch(noticeText.rectTransform, Vector2.zero, Vector2.one, new Vector2(20, 70), new Vector2(-20, -12));
            dismiss = MakeButton("DismissNotice", noticeBox, "知道了", () => session.DismissNotice());
            Stretch(dismiss.GetComponent<RectTransform>(), new Vector2(.2f, 0), new Vector2(.8f, 0), new Vector2(0, 16), new Vector2(0, 64));
            noticeRoot.gameObject.SetActive(false);
        }
        public void Refresh()
        {
            if (session == null || overlay == null) return;
            var frame = session.CurrentFrame;
            overlay.gameObject.SetActive(frame != null);
            if (frame == null) { fingerprint = null; return; }
            string key = frame.Id + "\n" + frame.Body + "\n" + string.Join("|", frame.Choices.Select(c => c.Id)) + "\n" + session.Notice;
            if (key == fingerprint) return;
            bool hadNotice = noticeRoot.gameObject.activeSelf;
            fingerprint = key;
            // Remove selection before disabling the chosen control (especially the erased bad-ending option).
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            heading.text = frame.Speaker + "  /  " + frame.Title;
            body.text = frame.Body;
            close.gameObject.SetActive(frame.CanClose);
            foreach (var button in choiceButtons)
            {
                button.onClick.RemoveAllListeners(); button.interactable = false; button.gameObject.SetActive(false);
                Destroy(button.gameObject);
            }
            choiceButtons.Clear();
            foreach (var option in frame.Choices)
            {
                string id = option.Id;
                var button = MakeButton("Choice_" + id, content, option.Text, () => session.Choose(id));
                button.gameObject.AddComponent<ScrollSelectionFocus>();
                button.gameObject.AddComponent<LayoutElement>().preferredHeight = 72;
                choiceButtons.Add(button);
            }
            bool notice = !string.IsNullOrEmpty(session.Notice);
            noticeRoot.gameObject.SetActive(notice); noticeText.text = session.Notice ?? string.Empty;
            foreach (var button in choiceButtons) button.interactable = !notice;
            FitChoices();
            scroll.verticalNormalizedPosition = 1;
            if (notice && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(dismiss.gameObject);
            // After dismissing an erased-memory notice, keep selection empty rather than activating the remaining action.
            else if (!hadNotice && choiceButtons.Count > 0 && EventSystem.current != null)
            {
                // Keep long, dismissible stories at the reading start; keyboard navigation reveals choices later.
                var initial = frame.CanClose && content.rect.height > viewport.rect.height ? close : choiceButtons[0];
                EventSystem.current.SetSelectedGameObject(initial.gameObject);
            }
        }
        private void FitChoices()
        {
            if (content == null) return;
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            foreach (var button in choiceButtons)
            {
                var text = button.GetComponentInChildren<Text>();
                var layout = button.GetComponent<LayoutElement>();
                layout.preferredHeight = Mathf.Max(64, text.preferredHeight + 28);
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        }
        private Button MakeButton(string name, Transform parent, string caption, Action action)
        {
            var rect = Rect(name, parent);
            rect.gameObject.AddComponent<Image>().color = new Color32(224, 222, 213, 255);
            var button = rect.gameObject.AddComponent<Button>();
            var colors = button.colors; colors.highlightedColor = new Color32(239, 229, 201, 255); colors.selectedColor = colors.highlightedColor;
            colors.pressedColor = new Color32(200, 190, 164, 255); button.colors = colors;
            button.onClick.AddListener(() => action());
            var text = Label("Text", rect, 22, TextAnchor.MiddleLeft);
            Stretch(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(14, 10), new Vector2(-14, -10));
            text.text = caption;
            return button;
        }
        private Text Label(string name, Transform parent, int size, TextAnchor anchor)
        {
            var rect = Rect(name, parent); var text = rect.gameObject.AddComponent<Text>();
            text.font = font; text.fontSize = size; text.alignment = anchor;
            text.color = new Color32(27, 28, 28, 255); text.raycastTarget = false; text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }
        private static RectTransform Rect(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false); return rect;
        }
        private static void Stretch(RectTransform rect, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        { rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = offsetMin; rect.offsetMax = offsetMax; }
    }
}
