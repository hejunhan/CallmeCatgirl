using UnityEngine;
using UnityEngine.EventSystems;

namespace CallmeCatgirl.UI
{
    // Persistent inspector in the workspace: the same numbers are accessible by hover, focus and click.
    public sealed class CorpusCardDetails : MonoBehaviour,IPointerEnterHandler,ISelectHandler,IPointerClickHandler
    {
        private TrainingWorkspaceView view;
        private string conversationId;
        public void Bind(TrainingWorkspaceView workspace,string id){view=workspace;conversationId=id;}
        public void OnPointerEnter(PointerEventData data)=>view?.ShowCorpusDetails(conversationId);
        public void OnSelect(BaseEventData data)=>view?.ShowCorpusDetails(conversationId);
        public void OnPointerClick(PointerEventData data)
        {
            view?.ShowCorpusDetails(conversationId);
            if(data.button==PointerEventData.InputButton.Left&&!data.dragging&&data.eligibleForClick)
                view?.SelectConversation(conversationId);
        }
    }
}
