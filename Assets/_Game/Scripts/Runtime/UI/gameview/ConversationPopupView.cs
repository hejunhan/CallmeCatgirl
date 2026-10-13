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
        public void Refresh(CoreGameplayController owner, Sprite portrait)
        {
            var story = owner.Story; panel.SetActive(story != null && story.IsOpen);
            if (story == null || !story.IsOpen || (story == displayed && revision == story.Revision)) return;
            displayed = story; revision = story.Revision;
            userLabel.text = story.Name(story.UserId);
            resultLabel.text = story.IsReply ? $"{story.RequestId} · {CoreGameplayController.QualityText(story.Result)} · 请选择回复（时间已冻结）" : "过往对话记录 · 时间已冻结";
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
