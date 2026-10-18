using System.Collections.Generic;
using CallmeCatgirl.Gameplay;
using CallmeCatgirl.Gameplay.TaskGrid;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CallmeCatgirl.UI
{
    public sealed class TaskCardView : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private string requestId;
        [SerializeField] private Text titleLabel;
        [SerializeField] private Text statusLabel;
        [SerializeField] private Text progressLabel;
        [SerializeField] private RectTransform shapeRoot;
        [SerializeField] private TaskCellView cellPrefab;
        [SerializeField] private Image selectionBorder;
        [SerializeField] private Image avatar;
        [SerializeField] private Color shapeColor = new Color(0.7f, 0.8f, 0.95f);
        private CoreGameplayCanvasView owner;
        private RequestShapeGraphic shape;
        private string conversationId;
        private bool trainingCard;
        private Text trainingLabel;
        public string RequestId => requestId;
        public RectTransform ShapeRect => shapeRoot;
        public void PlayRejected() { }
        public void Bind(CoreGameplayCanvasView view, TaskInstance task)
        {
            owner=view;requestId=task.Id;conversationId=task.ConversationId;
            trainingCard=task.Definition.Id.StartsWith("training:",System.StringComparison.Ordinal);
            GetComponent<LayoutElement>().preferredHeight=140;
            foreach(var text in new[]{titleLabel,statusLabel,progressLabel})text.gameObject.SetActive(false);
            if(avatar!=null)avatar.gameObject.SetActive(false);
            if(selectionBorder!=null)selectionBorder.gameObject.SetActive(false);
            var backing=GetComponent<Image>();if(backing!=null){backing.enabled=trainingCard;backing.color=new Color32(174,207,196,255);backing.raycastTarget=false;}
            foreach(Transform child in shapeRoot)child.gameObject.SetActive(false);
            shape=shapeRoot.GetComponent<RequestShapeGraphic>();if(shape==null)shape=shapeRoot.gameObject.AddComponent<RequestShapeGraphic>();
            shapeRoot.anchorMin=shapeRoot.anchorMax=new Vector2(.5f,.5f);shapeRoot.pivot=new Vector2(0,1);
            if(trainingCard)
            {
                var existingPattern=transform.Find("TrainingPattern");
                var pattern=existingPattern==null?new GameObject("TrainingPattern",typeof(RectTransform),typeof(CanvasRenderer)).AddComponent<TrainingCardPattern>():existingPattern.GetComponent<TrainingCardPattern>();
                pattern.transform.SetParent(transform,false);pattern.transform.SetAsFirstSibling();
                pattern.rectTransform.anchorMin=Vector2.zero;pattern.rectTransform.anchorMax=Vector2.one;pattern.rectTransform.offsetMin=pattern.rectTransform.offsetMax=Vector2.zero;pattern.raycastTarget=false;
                var existingLabel=transform.Find("TrainingLabel");
                var label=existingLabel==null?new GameObject("TrainingLabel",typeof(RectTransform)).AddComponent<Text>():existingLabel.GetComponent<Text>();label.transform.SetParent(transform,false);
                label.rectTransform.anchorMin=new Vector2(0,1);label.rectTransform.anchorMax=Vector2.one;label.rectTransform.pivot=new Vector2(.5f,1);label.rectTransform.sizeDelta=new Vector2(-4,38);label.rectTransform.anchoredPosition=new Vector2(0,-2);
                label.font=titleLabel.font;label.fontSize=15;label.alignment=TextAnchor.UpperCenter;label.color=new Color32(20,43,37,255);label.text="训练 / TRAIN";label.raycastTarget=false;trainingLabel=label;
            }
        }
        public void Refresh(TaskInstance task,double now,bool selected)
        {
            shape.Show(task.Definition.RotatedShape(task.Rotation),task.ConversationId,(float)(task.Progress/task.Definition.Calculation),selected,owner.IsAssociationHighlighted(task.ConversationId),task.State==RequestState.Ready);
            shapeRoot.anchoredPosition=new Vector2(-shapeRoot.rect.width*.5f,shapeRoot.rect.height*.5f-(trainingCard?12:0));
            if(trainingLabel!=null)trainingLabel.text=task.State==RequestState.Ready?"训练已完成":"训练 / TRAIN";
        }
        public void OnPointerEnter(PointerEventData data)=>owner?.HoverConversation(conversationId,this);
        public void OnPointerExit(PointerEventData data)=>owner?.ClearAssociationHover(this);
        public void OnPointerClick(PointerEventData data)
        {
            if (data.button != PointerEventData.InputButton.Left || data.dragging || !data.eligibleForClick) return;
            owner?.SelectRequest(requestId);
            if (data.clickCount >= 2) owner?.OpenRequestConversation(requestId);
        }
        public void OnBeginDrag(PointerEventData data) { if (data.button == PointerEventData.InputButton.Left) owner?.BeginDrag(requestId, data, false); }
        public void OnDrag(PointerEventData data) => owner?.UpdateDrag(data);
        public void OnEndDrag(PointerEventData data) => owner?.EndDrag(data);
    }
}
