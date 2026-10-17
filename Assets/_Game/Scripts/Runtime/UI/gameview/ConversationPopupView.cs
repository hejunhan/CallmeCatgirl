using System.Collections.Generic;
using CallmeCatgirl.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace CallmeCatgirl.UI
{
    public sealed class ConversationPopupView : MonoBehaviour
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private Text userLabel;
        [SerializeField] private Text resultLabel;
        [SerializeField] private Text historyLabel;
        [SerializeField] private Image avatar;
        [SerializeField] private ScrollRect historyScroll;
        [SerializeField] private RectTransform choiceContent;
        [SerializeField] private Button choiceTemplate;
        [SerializeField] private ScrollRect choiceScroll;
        [SerializeField] private Button closeButton;
        private readonly List<Button> buttons = new List<Button>();
        private ConversationStory displayed;
        private int revision = -1;
        private float openedAt;
        private Color resultIdle;
        private bool colorCaptured;
        public void Refresh(CoreGameplayController owner, Sprite portrait, UIFeedbackConfiguration feedback = null)
        {
            var story = owner.Story; panel.SetActive(story != null && story.IsOpen);
            if (story == null || !story.IsOpen) return;
            if (!colorCaptured) { resultIdle = resultLabel.color; colorCaptured = true; }
            bool changed = story != displayed || revision != story.Revision;
            if (changed) openedAt = Time.unscaledTime;
            float flash = feedback != null && feedback.Animate && story.IsReply
                ? Mathf.Clamp01(1 - (Time.unscaledTime - openedAt) / Mathf.Max(.05f,feedback.PulseSeconds)) : 0;
            resultLabel.color = Color.Lerp(resultIdle,feedback == null ? resultIdle : feedback.ResultColor(story.Result),flash);
            if (!changed) return;
            displayed = story; revision = story.Revision;
            userLabel.text = story.Name(story.UserId) + $" · 亲密度 {story.Affinity(story.UserId):0.##}";
            resultLabel.text = story.IsReply ? $"{story.RequestId} · {CoreGameplayController.QualityText(story.Result)} · 请选择回复（时间已冻结）" : "过往对话记录 · 时间已冻结";
            string progress = story.Progress(story.UserId);
            if (!string.IsNullOrEmpty(progress)) resultLabel.text += "\n" + progress;
            resultLabel.resizeTextForBestFit = true; resultLabel.resizeTextMinSize = 16; resultLabel.resizeTextMaxSize = resultLabel.fontSize;
            historyLabel.text = story.History(story.UserId);
            avatar.sprite = portrait; avatar.enabled = portrait != null;
            closeButton.gameObject.SetActive(!story.IsReply);
            closeButton.onClick.RemoveAllListeners(); closeButton.onClick.AddListener(owner.CloseConversationHistory);
            foreach (var button in buttons) { button.gameObject.SetActive(false); Destroy(button.gameObject); }
            buttons.Clear(); choiceTemplate.gameObject.SetActive(false);
            if (story.IsReply)
                for (int i = 0; i < story.Choices.Count; i++)
                {
                    int index = i;
                    var button = Instantiate(choiceTemplate, choiceContent); button.name = "ReplyChoice_" + i;
                    button.GetComponentInChildren<Text>(true).text = story.Choices[i].Label;
                    button.onClick.RemoveAllListeners(); button.onClick.AddListener(() => owner.ChooseReply(index));
                    button.gameObject.SetActive(true); buttons.Add(button);
                }
            Canvas.ForceUpdateCanvases(); historyScroll.verticalNormalizedPosition = 0;
            choiceScroll.verticalNormalizedPosition = 1;
        }
    }
}
