using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CallmeCatgirl.Debugging
{
    public static class InlineDebugInput
    {
        // Gate the existing gameplay R/Space/Esc handlers with this property. No new global shortcut is registered.
        public static bool IsEditing
        {
            get
            {
                var selected = EventSystem.current == null ? null : EventSystem.current.currentSelectedGameObject;
                var field = selected == null ? null : selected.GetComponentInParent<InputField>();
                return field != null && field.isActiveAndEnabled && field.isFocused;
            }
        }
    }

    /// <summary>Local layout child. No Canvas, overlay, modal, timeScale write, or global input handler.</summary>
    public abstract class InlineDebugControls : MonoBehaviour
    {
        protected DebugCommandService Service { get; private set; }
        protected Font Font { get; private set; }
        protected Text Status;
        protected string TargetId;
        private float nextRefresh;
        private string boundSession;
        private bool built;

        protected virtual void Awake()
        {
            if (!DebugBuild.Enabled) gameObject.SetActive(false);
        }

        public void Bind(DebugCommandService service, string targetId, Font font = null)
        {
            Unsubscribe();
            Service = service ?? throw new ArgumentNullException(nameof(service)); TargetId = targetId;
            if (!DebugBuild.Enabled) { gameObject.SetActive(false); return; }
            Font = font;
            if (Font == null)
            {
                try { Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
                catch (ArgumentException) { Font = Resources.GetBuiltinResource<Font>("Arial.ttf"); }
            }
            if (!built)
            {
                var layout = gameObject.AddComponent<VerticalLayoutGroup>();
                layout.spacing = 3; layout.childControlWidth = true; layout.childControlHeight = true;
                layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
                var size = gameObject.AddComponent<LayoutElement>(); size.flexibleWidth = 1;
                Build(); Status = Label(transform, "操作结果显示在这里", 58);
                Status.color = new Color(.75f, .57f, .30f); built = true;
            }
            Service.Executed += OnExecuted;
            gameObject.SetActive(true); boundSession = Service.Capture().SessionId;
            ResetDraft(); RefreshNow();
        }

        // Call when task selection changes; drafts never migrate to a different request accidentally.
        public void SetTarget(string targetId)
        {
            if (TargetId == targetId) return;
            TargetId = targetId;
            if (Status != null) Status.text = "对象已切换；请检查当前值后操作";
            ResetDraft(); RefreshNow();
        }
        private void Update()
        {
            if (Service == null || Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + .15f; RefreshNow();
        }
        public void RefreshNow()
        {
            if (Service == null || !built) return;
            var snapshot = Service.Capture();
            if (boundSession != snapshot.SessionId)
            {
                boundSession = snapshot.SessionId; TargetId = null;
                Status.text = "会话已重置，需重新绑定对象"; ResetDraft();
            }
            // A disabled host remains locally visible with disabled controls in development.
            Refresh(snapshot, DebugBuild.Enabled && snapshot.Enabled);
        }
        protected void Run(DebugCommand command)
        {
            var receipt = Service.Execute(command);
            Status.text = receipt.Display; RefreshNow();
        }
        protected void InvalidNumber() { Status.text = "[调试] 未执行：请输入有限数值；分数设置不能为负"; }
        protected static bool TryNumber(InputField field, out double number)
        {
            bool valid = double.TryParse(field.text, NumberStyles.Float, CultureInfo.InvariantCulture, out number)
                || double.TryParse(field.text, NumberStyles.Float, CultureInfo.CurrentCulture, out number);
            return valid && !double.IsNaN(number) && !double.IsInfinity(number);
        }
        private void OnExecuted(DebugReceipt receipt)
        {
            if (receipt.Command != null && receipt.Command.TargetId == TargetId) Status.text = receipt.Display;
            RefreshNow();
        }
        private void Unsubscribe() { if (Service != null) Service.Executed -= OnExecuted; }
        private void OnDestroy() => Unsubscribe();
        protected abstract void Build();
        protected abstract void Refresh(DebugSnapshot snapshot, bool enabled);
        protected virtual void ResetDraft() { }

        protected RectTransform Row()
        {
            var row = Child(transform, "InlineRow");
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>(); layout.spacing = 4;
            layout.childControlWidth = true; layout.childControlHeight = true;
            layout.childForceExpandWidth = false; layout.childForceExpandHeight = false;
            var size = row.gameObject.AddComponent<LayoutElement>(); size.minHeight = 27; size.preferredHeight = 27;
            return row;
        }
        protected Text Label(Transform parent, string text, float height = 27, float width = -1)
        {
            var rect = Child(parent, "Text"); var label = rect.gameObject.AddComponent<Text>();
            label.font = Font; label.fontSize = 12; label.text = text; label.color = new Color(.90f, .78f, .48f);
            label.alignment = TextAnchor.MiddleLeft; label.raycastTarget = false;
            var size = rect.gameObject.AddComponent<LayoutElement>(); size.minHeight = height; size.preferredHeight = height;
            if (width >= 0) size.preferredWidth = width; else size.flexibleWidth = 1;
            return label;
        }
        protected Button Button(Transform parent, string caption, Action action, float width = 78)
        {
            var rect = Child(parent, "Button_" + caption); var background = rect.gameObject.AddComponent<Image>();
            background.color = new Color(.26f, .21f, .13f, .96f);
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = background;
            button.onClick.AddListener(() => action());
            var text = Label(rect, caption); text.alignment = TextAnchor.MiddleCenter; Stretch(text.rectTransform);
            var size = rect.gameObject.AddComponent<LayoutElement>(); size.preferredWidth = width; size.preferredHeight = 27;
            return button;
        }
        protected InputField NumberField(Transform parent)
        {
            var rect = Child(parent, "Number"); var background = rect.gameObject.AddComponent<Image>();
            background.color = new Color(.10f, .10f, .12f, .95f);
            var field = rect.gameObject.AddComponent<InputField>(); field.targetGraphic = background;
            var text = Label(rect, "0"); text.color = Color.white; Stretch(text.rectTransform);
            text.rectTransform.offsetMin = new Vector2(4, 0); text.rectTransform.offsetMax = new Vector2(-4, 0);
            field.textComponent = text; field.text = "1"; field.characterLimit = 18;
            field.lineType = InputField.LineType.SingleLine;
            // Navigation remains local; pressing Space/R only edits text while this field is focused.
            var size = rect.gameObject.AddComponent<LayoutElement>(); size.preferredWidth = 62; size.preferredHeight = 27;
            return field;
        }
        internal static RectTransform Child(Transform parent, string name)
        {
            var item = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)item.transform; rect.SetParent(parent, false); return rect;
        }
        private static void Stretch(RectTransform rect)
        { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero; }
    }
}
