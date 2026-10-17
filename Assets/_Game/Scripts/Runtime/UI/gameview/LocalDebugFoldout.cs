using UnityEngine;
using UnityEngine.UI;
using CallmeCatgirl.Debugging;
namespace CallmeCatgirl.UI
{
    public sealed class LocalDebugFoldout : MonoBehaviour
    {
        private InlineDebugControls widget; private string target;
        public void SetTarget(string id){target=id;if(widget!=null)widget.SetTarget(id);}
        public static LocalDebugFoldout Attach(RectTransform slot,DebugCommandService service,string kind,string id,Font font)
        {
            if(!DebugBuild.Enabled || service==null)return null;
            var view=slot.gameObject.AddComponent<LocalDebugFoldout>(); view.target=id;
            var size=slot.gameObject.AddComponent<LayoutElement>();size.preferredHeight=28;
            var toggle=new GameObject("DeveloperControls",typeof(RectTransform),typeof(Image),typeof(Button));toggle.transform.SetParent(slot,false);
            var r=toggle.GetComponent<RectTransform>();r.anchorMin=new Vector2(0,1);r.anchorMax=new Vector2(1,1);r.pivot=new Vector2(.5f,1);r.sizeDelta=new Vector2(0,26);
            toggle.GetComponent<Image>().color=new Color(.9f,.88f,.81f);
            var label=new GameObject("Label",typeof(RectTransform),typeof(Text));label.transform.SetParent(toggle.transform,false);
            var tr=label.GetComponent<RectTransform>();tr.anchorMin=Vector2.zero;tr.anchorMax=Vector2.one;tr.offsetMin=tr.offsetMax=Vector2.zero;
            var text=label.GetComponent<Text>();text.font=font;text.fontSize=13;text.color=Color.black;text.text="开发调试 ▸";text.alignment=TextAnchor.MiddleLeft;
            var content=new GameObject("LocalControls",typeof(RectTransform));content.transform.SetParent(slot,false);
            var cr=content.GetComponent<RectTransform>();cr.anchorMin=new Vector2(0,1);cr.anchorMax=new Vector2(1,1);cr.pivot=new Vector2(.5f,1);cr.anchoredPosition=new Vector2(0,-30);cr.sizeDelta=new Vector2(0,230);content.SetActive(false);
            toggle.GetComponent<Button>().onClick.AddListener(()=>{
                bool expanded=!content.activeSelf;
                if(expanded && view.widget==null)
                {
                    view.widget=kind=="corpus"?(InlineDebugControls)InlineCorpusDebugControls.Attach(cr,service,view.target,font):
                        kind=="training"?InlineTrainingDebugControls.Attach(cr,service,view.target,font):InlineTaskDebugControls.Attach(cr,service,view.target,font);
                    if(view.widget!=null){var wr=(RectTransform)view.widget.transform;wr.anchorMin=Vector2.zero;wr.anchorMax=Vector2.one;wr.offsetMin=wr.offsetMax=Vector2.zero;}
                }
                content.SetActive(expanded);size.preferredHeight=expanded?265:28;text.text=expanded?"开发调试 ▾":"开发调试 ▸";
            });return view;
        }
    }
}
