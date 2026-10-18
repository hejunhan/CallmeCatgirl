using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace CallmeCatgirl.UI
{
    public sealed class ConversationCorpusDrop : MonoBehaviour, IDropHandler,IPointerEnterHandler,IPointerExitHandler
    {
        public TrainingWorkspaceView View;
        public int Slot=-1;
        private Color normal;
        private bool highlighted;
        private string normalText;
        public void OnPointerEnter(PointerEventData e)
        {
            var drag=e.pointerDrag==null?null:e.pointerDrag.GetComponent<ConversationCorpusDrag>();
            var image=GetComponent<Image>();if(drag==null||drag.View!=View||image==null)return;
            normal=image.color;highlighted=true;
            bool valid=Slot<0||View.CanDropConversation(drag.ConversationId,Slot);
            image.color=valid?new Color32(141,202,182,255):new Color32(226,163,143,255);
            var label=GetComponentInChildren<Text>();if(label!=null){normalText=label.text;label.text=valid?"↓ 可放置":"× 已占用 / 超出上限";}
        }
        public void OnPointerExit(PointerEventData e)=>RestoreColor();
        private void RestoreColor()
        {
            if(highlighted)
            {if(GetComponent<Image>()!=null)GetComponent<Image>().color=normal;var label=GetComponentInChildren<Text>();if(label!=null)label.text=normalText;}
            highlighted=false;
        }
        public void OnDrop(PointerEventData e)
        {
            var drag=e.pointerDrag==null?null:e.pointerDrag.GetComponent<ConversationCorpusDrag>();
            RestoreColor();
            if(drag!=null&&drag.View==View)
            {if(Slot<0)View.SelectConversation(drag.ConversationId);else View.TryPlaceConversation(drag.ConversationId,Slot);}
        }
        private void OnDisable()=>RestoreColor();
    }
}
