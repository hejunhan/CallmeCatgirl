using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace CallmeCatgirl.UI
{
    // The card is a conversation, never a request or a single dialogue fragment.
    public sealed class ConversationCorpusDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public TrainingWorkspaceView View { get; private set; }
        public string ConversationId { get; private set; }
        private RectTransform ghost;
        public void Bind(TrainingWorkspaceView view,string id){View=view;ConversationId=id;}
        public void OnBeginDrag(PointerEventData e)
        {
            if(!GetComponent<Button>().interactable){e.pointerDrag=null;return;}
            var canvas=GetComponentInParent<Canvas>().rootCanvas;
            var source=GetComponentInChildren<Text>();
            ghost=new GameObject("DraggedConversation",typeof(RectTransform),typeof(CanvasGroup)).GetComponent<RectTransform>();
            ghost.SetParent(canvas.transform,false);ghost.sizeDelta=new Vector2(260,42);
            ghost.gameObject.GetComponent<CanvasGroup>().blocksRaycasts=false;
            var image=ghost.gameObject.AddComponent<Image>();image.color=new Color(.8f,.93f,.94f,.95f);image.raycastTarget=false;
            var label=new GameObject("Label",typeof(RectTransform)).AddComponent<Text>();label.transform.SetParent(ghost,false);
            label.rectTransform.anchorMin=Vector2.zero;label.rectTransform.anchorMax=Vector2.one;label.rectTransform.sizeDelta=Vector2.zero;
            label.font=source.font;label.fontSize=20;label.color=Color.black;label.text="拖入训练："+ConversationId;label.raycastTarget=false;
            OnDrag(e);
        }
        public void OnDrag(PointerEventData e)
        {
            if(ghost==null)return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)ghost.parent,e.position,e.pressEventCamera,out var point);
            ghost.anchoredPosition=point;
        }
        public void OnEndDrag(PointerEventData e){if(ghost!=null)Destroy(ghost.gameObject);ghost=null;}
        private void OnDisable(){if(ghost!=null)Destroy(ghost.gameObject);ghost=null;}
    }
}
