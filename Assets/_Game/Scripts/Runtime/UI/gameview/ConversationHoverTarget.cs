using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
namespace CallmeCatgirl.UI
{
    public sealed class ConversationHoverTarget : MonoBehaviour,IPointerEnterHandler,IPointerExitHandler,IPointerClickHandler
    {
        private ConversationWorkspaceView owner;private string id;
        public void Bind(ConversationWorkspaceView view,string conversation){owner=view;id=conversation;}
        public void OnPointerEnter(PointerEventData e)=>owner.Hover(id,this);
        public void OnPointerExit(PointerEventData e)=>owner.Clear(this);
        public void OnPointerClick(PointerEventData e){if(e.button==PointerEventData.InputButton.Left)owner.SelectConversation(id,e.clickCount>=2);}
    }
}
