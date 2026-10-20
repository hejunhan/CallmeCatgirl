using UnityEngine;
using UnityEngine.EventSystems;
namespace CallmeCatgirl.UI
{
    public sealed class ConversationHoverTarget : MonoBehaviour,IPointerEnterHandler,IPointerExitHandler,IPointerClickHandler,IScrollHandler
    {
        private ConversationWorkspaceView owner;private string id,request,key;
        public void Bind(ConversationWorkspaceView view,string conversation,string requestId,string rowKey){owner=view;id=conversation;request=requestId;key=rowKey;}
        public void OnPointerEnter(PointerEventData e)=>owner.Hover(request??id,key,this);
        public void OnPointerExit(PointerEventData e)=>owner.Clear(this);
        public void OnPointerClick(PointerEventData e){if(e.button==PointerEventData.InputButton.Left)owner.SelectRow(id,request,e.clickCount>=2);}
        public void OnScroll(PointerEventData e)=>owner.ScrollWheel(e);
    }
}
