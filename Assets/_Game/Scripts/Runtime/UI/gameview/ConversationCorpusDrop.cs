using UnityEngine;
using UnityEngine.EventSystems;
namespace CallmeCatgirl.UI
{
    public sealed class ConversationCorpusDrop : MonoBehaviour, IDropHandler
    {
        public TrainingWorkspaceView View;
        public void OnDrop(PointerEventData e)
        {
            var drag=e.pointerDrag==null?null:e.pointerDrag.GetComponent<ConversationCorpusDrag>();
            if(drag!=null&&drag.View==View)View.SelectConversation(drag.ConversationId);
        }
    }
}
