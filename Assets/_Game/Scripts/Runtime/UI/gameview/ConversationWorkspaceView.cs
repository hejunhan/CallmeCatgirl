using System.Collections.Generic;
using System.Linq;
using CallmeCatgirl.Gameplay;
using CallmeCatgirl.Gameplay.TaskGrid;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CallmeCatgirl.UI
{
    public sealed class ConversationWorkspaceView : MonoBehaviour
    {
        private sealed class Row
        {
            public RectTransform Rect;
            public InnerBorderGraphic Border;
            public Image Corpus;
            public InnerBorderGraphic CorpusBorder;
            public Button Button;
            public Text Summary;
            public int Requests, Records;
        }
        private CoreGameplayController owner;
        private CoreGameplayCanvasView view;
        [SerializeField] private RectTransform list;
        [SerializeField] private RectTransform contactTemplate;
        [SerializeField] private Button conversationsButton, modelsButton;
        [SerializeField] private Font font;
        private string fingerprint,hovered;
        private Object hoverSource;
        [SerializeField] private AssociationLineGraphic lines;
        private readonly Dictionary<string,Row> rows=new Dictionary<string,Row>();
        public bool IsHighlighted(string id)=>id!=null&&id==hovered;
        public void Hover(string id,Object source){hovered=id;hoverSource=source;}
        public void Clear(Object source){if(hoverSource==source){hovered=null;hoverSource=null;}}
        public void Bind(CoreGameplayController source,CoreGameplayCanvasView canvasView,RectTransform panel,RectTransform candidates,Font textFont)
        {
            owner=source;view=canvasView;if(font==null)font=textFont;fingerprint=null;hovered=null;
            if(list==null)
            {
                if(Application.isPlaying)throw new System.InvalidOperationException("Contact layout is missing from scene.");
                foreach(Transform child in panel)child.gameObject.SetActive(false);
                var navigation=Rect("FixedNavigation",transform,0,0,0,0);
                navigation.anchorMin=Vector2.zero;navigation.anchorMax=Vector2.one;navigation.offsetMin=navigation.offsetMax=Vector2.zero;
                var rail=Rect("NavigationRail",navigation,0,120,64,0);rail.anchorMin=new Vector2(0,0);rail.anchorMax=new Vector2(0,1);rail.offsetMin=new Vector2(0,60);rail.offsetMax=new Vector2(64,-120);
                rail.gameObject.AddComponent<Image>().color=new Color32(232,232,232,255);
                var rule=Rect("Separator",rail,63,0,1,0);rule.anchorMin=new Vector2(0,0);rule.anchorMax=new Vector2(0,1);rule.offsetMin=new Vector2(63,0);rule.offsetMax=new Vector2(64,0);rule.gameObject.AddComponent<Image>().color=new Color32(111,111,111,255);
                conversationsButton=NavigationButton(rail,"Conversations",16,()=>{if(!owner.Story.IsReply)owner.CloseConversationHistory();},false);
                modelsButton=NavigationButton(rail,"Models",88,()=>{if(owner.IsModalOpen)return;var button=transform.GetComponentsInChildren<Button>(true).FirstOrDefault(b=>b.name=="OpenTraining");button?.onClick.Invoke();},true);
                panel.SetParent(navigation,false);
                panel.anchorMin=new Vector2(0,0);panel.anchorMax=new Vector2(0,1);panel.pivot=new Vector2(0,1);panel.offsetMin=new Vector2(80,60);panel.offsetMax=new Vector2(400,-120);
                var motion=panel.GetComponent<CallmeCatgirl.Presentation.PlanningPaper>();if(motion!=null)motion.enabled=false;
                var paper=panel.GetComponent<Image>();paper.sprite=null;paper.color=new Color32(244,244,244,255);
                Label(panel,"对话",16,14,280,40,28);
                var viewport=Rect("ConversationList",panel,12,64,296,740);viewport.anchorMin=Vector2.zero;viewport.anchorMax=Vector2.one;viewport.offsetMin=new Vector2(12,12);viewport.offsetMax=new Vector2(-12,-64);viewport.gameObject.AddComponent<RectMask2D>();viewport.gameObject.AddComponent<Image>().color=Color.clear;
                var scroll=viewport.gameObject.AddComponent<ScrollRect>();scroll.horizontal=false;scroll.scrollSensitivity=32;
                list=Rect("ConversationRows",viewport,0,0,296,0);
                var layout=list.gameObject.AddComponent<VerticalLayoutGroup>();layout.spacing=8;layout.childControlWidth=true;layout.childControlHeight=true;layout.childForceExpandHeight=false;
                list.gameObject.AddComponent<ContentSizeFitter>().verticalFit=ContentSizeFitter.FitMode.PreferredSize;scroll.viewport=viewport;scroll.content=list;
                var stack=candidates.GetComponent<VerticalLayoutGroup>();if(stack!=null){stack.enabled=false;DestroyImmediate(stack);}
                var grid=candidates.gameObject.AddComponent<GridLayoutGroup>();grid.cellSize=new Vector2(140,140);grid.spacing=new Vector2(8,16);grid.padding=new RectOffset(8,8,12,12);grid.constraint=GridLayoutGroup.Constraint.FixedColumnCount;grid.constraintCount=2;
                var graphic=new GameObject("ConversationAssociationLines",typeof(RectTransform),typeof(AssociationLineGraphic));graphic.transform.SetParent(transform,false);
                var r=graphic.GetComponent<RectTransform>();r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;
                lines=graphic.GetComponent<AssociationLineGraphic>();lines.raycastTarget=false;
            }
            conversationsButton.onClick.RemoveAllListeners();
            conversationsButton.onClick.AddListener(()=>{if(!owner.Story.IsReply)owner.CloseConversationHistory();});
            modelsButton.onClick.RemoveAllListeners();modelsButton.onClick.AddListener(()=>owner.OpenTraining());
            Refresh();
        }
#if UNITY_EDITOR
        public void PrepareTemplate()
        {
            if(contactTemplate!=null||list.childCount==0)return;
            contactTemplate=Instantiate((RectTransform)list.GetChild(0),list.parent);
            contactTemplate.name="ContactTemplate";contactTemplate.gameObject.SetActive(false);
            foreach(var hover in contactTemplate.GetComponentsInChildren<ConversationHoverTarget>(true))DestroyImmediate(hover);
        }
#endif
        public void Refresh()
        {
            if(owner==null||list==null)return;
            var tasks=owner.Simulation.Tasks.Values.Where(t=>!owner.IsOperatingTask(t.Id) && (!owner.TrainingEnabled||!owner.Training.IsTrainingRequest(t.Id))).ToArray();
            var corpus=owner.TrainingEnabled?owner.Training.Session.ConversationCorpora.ToArray():new CallmeCatgirl.Gameplay.Training.ConversationCorpusSummary[0];
            string key=string.Join("|",tasks.Select(t=>t.ConversationId+":"+t.Id))+"#"+string.Join("|",corpus.Select(c=>c.ConversationId+":"+c.RecordCount+":"+c.AvailablePoints));
            if(key!=fingerprint)
            {
                fingerprint=key;hovered=null;hoverSource=null;
                foreach(Transform child in list){child.gameObject.SetActive(false);Destroy(child.gameObject);}rows.Clear();
                var ids=tasks.Select(t=>t.ConversationId).Concat(corpus.Select(c=>c.ConversationId)).Distinct();
                foreach(var id in ids)
                {
                    var summary=corpus.FirstOrDefault(c=>c.ConversationId==id);
                    string conversation=id;int requests=tasks.Where(t=>t.ConversationId==id).Select(t=>t.Id).Concat(summary==null?new string[0]:summary.RequestIds).Distinct().Count(),records=summary==null?0:summary.RecordCount;
                    RectTransform r; InnerBorderGraphic border,corpusBorder; Image badge; Button button; Text info;
                    if(contactTemplate!=null)
                    {
                        r=Instantiate(contactTemplate,list);r.name="Conversation_"+id;r.gameObject.SetActive(true);
                        border=r.Find("InnerBorder").GetComponent<InnerBorderGraphic>();button=r.GetComponent<Button>();
                        r.Find("Name").GetComponent<Text>().text=owner.Story.Name(id);
                        info=r.Find("Summary").GetComponent<Text>();badge=r.Find("Corpus").GetComponent<Image>();
                        corpusBorder=badge.transform.Find("InnerBorder").GetComponent<InnerBorderGraphic>();
                    }
                    else
                    {
                    r=Rect("Conversation_"+id,list,0,0,296,112);r.gameObject.AddComponent<LayoutElement>().preferredHeight=112;
                    r.gameObject.AddComponent<Image>().color=new Color32(244,244,244,255);
                    border=InnerBorderGraphic.Attach(r,1,BorderEdges.Bottom,new Color32(111,111,111,255));
                    button=r.gameObject.AddComponent<Button>();button.onClick.AddListener(()=>SelectConversation(conversation,false));
                    Label(r,owner.Story.Name(id),12,10,220,58,26).name="Name";
                    info=Label(r,"",12,76,272,30,20);info.color=new Color32(82,82,82,255);
                    badge=Rect("Corpus_"+id,r,244,18,36,36).gameObject.AddComponent<Image>();badge.color=new Color32(211,197,167,255);badge.gameObject.SetActive(records>0);
                    corpusBorder=InnerBorderGraphic.Attach(badge.rectTransform,1,BorderEdges.All,new Color32(82,82,82,255));
                        info.name="Summary";badge.name="Corpus";
                    }
                    badge.gameObject.SetActive(records>0);
                    button.onClick.RemoveAllListeners();button.onClick.AddListener(()=>SelectConversation(conversation,false));
                    AddHover(r.gameObject,id);AddHover(badge.gameObject,id);
                    rows.Add(id,new Row{Rect=r,Border=border,Corpus=badge,CorpusBorder=corpusBorder,Button=button,Summary=info,Requests=requests,Records=records});
                }
            }
            foreach(var pair in rows)
            {
                pair.Value.Summary.text=$"亲密度 {owner.Story.Affinity(pair.Key):0.##} · {pair.Value.Requests} 请求 · {(owner.TrainingEnabled?owner.Training.Session.GetConversationCorpus(pair.Key).AvailablePoints:0):0.##} 语料点";
                bool linked=pair.Key==hovered;
                bool selected=owner.SelectedTask?.ConversationId==pair.Key;
                pair.Value.Border.Configure(selected?2:1,selected||linked?BorderEdges.All:BorderEdges.Bottom,selected||linked?new Color32(22,22,22,255):new Color32(111,111,111,255));
                pair.Value.Corpus.color=linked?Color.white:new Color32(211,197,167,255);
                pair.Value.CorpusBorder.Configure(1,BorderEdges.All,linked?new Color32(22,22,22,255):new Color32(82,82,82,255));
                pair.Value.Button.interactable=!owner.IsModalOpen;
            }
        }
        public void SelectConversation(string id,bool open)
        {
            if(owner.IsModalOpen)return;
            var task=owner.SelectedTask?.ConversationId==id?owner.SelectedTask:owner.Simulation.Tasks.Values.FirstOrDefault(t=>t.ConversationId==id);
            if(task==null)return;
            owner.SelectRequest(task.Id);if(open)owner.OpenConversationHistory();
        }
        private void LateUpdate()
        {
            if(lines==null||owner==null)return;
            var paths=new List<Vector2[]>();
            Row row;
            if(hovered!=null&&!owner.IsModalOpen&&rows.TryGetValue(hovered,out row))
            {
                var a=LocalPoint(row.Rect,new Vector2(row.Rect.rect.xMax,row.Rect.rect.center.y));
                foreach(var target in view.AssociationTargets(hovered))
                {
                    var b=LocalPoint(target,new Vector2(target.rect.xMin,target.rect.center.y));
                    paths.Add(new[]{a,b});
                }
            }
            lines.SetPaths(paths);
        }
        private Vector2 LocalPoint(RectTransform rect,Vector2 point)=>lines.rectTransform.InverseTransformPoint(rect.TransformPoint(point));
        private void AddHover(GameObject go,string id){var h=go.AddComponent<ConversationHoverTarget>();h.Bind(this,id);}
        private Button NavigationButton(RectTransform rail,string name,float y,UnityEngine.Events.UnityAction action,bool grid)
        {
            var r=Rect(name,rail,8,y,48,48);r.gameObject.AddComponent<Image>().color=new Color32(244,244,244,255);
            var button=r.gameObject.AddComponent<Button>();button.onClick.AddListener(action);
            if(grid)for(int i=0;i<4;i++){var cell=Rect("ModelCell",r,12+(i%2)*13,12+(i/2)*13,10,10).gameObject.AddComponent<Image>();cell.color=new Color32(82,82,82,255);cell.raycastTarget=false;}
            else
            {
                var bubble=Rect("ConversationIcon",r,10,12,28,22).gameObject.AddComponent<Image>();bubble.color=new Color32(82,82,82,255);bubble.raycastTarget=false;
                var inset=Rect("Inset",bubble.transform,2,2,24,18).gameObject.AddComponent<Image>();inset.color=new Color32(244,244,244,255);inset.raycastTarget=false;
                var tail=Rect("Tail",r,12,34,6,5).gameObject.AddComponent<Image>();tail.color=new Color32(82,82,82,255);tail.raycastTarget=false;
            }
            return button;
        }
        private static RectTransform Rect(string name,Transform parent,float x,float y,float width,float height)
        {
            var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();r.SetParent(parent,false);r.anchorMin=r.anchorMax=r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(width,height);return r;
        }
        private Text Label(Transform parent,string text,float x,float y,float width,float height,int size)
        {
            var t=Rect("Label",parent,x,y,width,height).gameObject.AddComponent<Text>();t.font=font;t.text=text;t.fontSize=size;t.color=new Color32(22,22,22,255);t.raycastTarget=false;return t;
        }
    }
}
