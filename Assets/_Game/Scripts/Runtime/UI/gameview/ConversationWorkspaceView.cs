using System.Collections.Generic;
using CallmeCatgirl.Gameplay.Narrative;
using System.Linq;
using CallmeCatgirl.Gameplay;
using CallmeCatgirl.Gameplay.TaskGrid;
using CallmeCatgirl.Gameplay.Training;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CallmeCatgirl.UI
{
    public sealed class ConversationWorkspaceView : MonoBehaviour,IScrollHandler
    {
        private sealed class Row
        {
            public RectTransform Rect;
            public InnerBorderGraphic Border;
            public Image Corpus;
            public InnerBorderGraphic CorpusBorder;
            public Button Button;
            public Text Name, Summary;
            public string Conversation, RequestId;
            public int Records;
        }
        private const float RowSpacing = 8;
        private CoreGameplayController owner;
        private CoreGameplayCanvasView view;
        [SerializeField] private RectTransform list;
        [SerializeField] private RectTransform pinnedContent;
        [SerializeField] private ScrollRect scroll;
        [SerializeField] private RectTransform contactTemplate;
        [SerializeField] private Button conversationsButton, modelsButton;
        [SerializeField] private Font font;
        private string fingerprint,hovered,hoveredKey;
        private Object hoverSource;
        [SerializeField] private AssociationLineGraphic lines;
        private readonly Dictionary<string,Row> rows=new Dictionary<string,Row>();
        public bool IsHighlighted(string id)=>id!=null&&id==hovered;
        public void Hover(string id,Object source)=>Hover(id,null,source);
        public void Hover(string id,string key,Object source){hovered=id;hoveredKey=key;hoverSource=source;}
        public void HoverRequest(string requestId,Object source)
        {
            string key=null;
            if(requestId!=null)foreach(var pair in rows)if(pair.Value.RequestId==requestId){key=pair.Key;break;}
            Hover(requestId,key,source);
        }
        public void Clear(Object source){if(hoverSource==source){hovered=null;hoveredKey=null;hoverSource=null;}}
        public void OnScroll(PointerEventData data)=>ScrollWheel(data);
        public void ScrollWheel(PointerEventData data)
        {
            if(scroll==null||data==null)return;
            var host=scroll.transform.parent as RectTransform;
            if(host!=null&&!RectTransformUtility.RectangleContainsScreenPoint(host,data.position,data.pressEventCamera))return;
            scroll.OnScroll(data);
        }
        public void Bind(CoreGameplayController source,CoreGameplayCanvasView canvasView,RectTransform panel,RectTransform candidates,Font textFont)
        {
            owner=source;view=canvasView;if(font==null)font=textFont;fingerprint=null;hovered=null;hoveredKey=null;
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
                var host=Rect("ConversationList",panel,12,64,296,740);host.anchorMin=Vector2.zero;host.anchorMax=Vector2.one;host.offsetMin=new Vector2(12,12);host.offsetMax=new Vector2(-12,-64);
                pinnedContent=Rect("PinnedContacts",host,0,0,296,0);pinnedContent.anchorMin=new Vector2(0,1);pinnedContent.anchorMax=Vector2.one;pinnedContent.pivot=new Vector2(0,1);pinnedContent.sizeDelta=Vector2.zero;
                var pinnedLayout=pinnedContent.gameObject.AddComponent<VerticalLayoutGroup>();pinnedLayout.padding=new RectOffset(0,0,0,8);pinnedLayout.spacing=RowSpacing;pinnedLayout.childControlWidth=true;pinnedLayout.childControlHeight=true;pinnedLayout.childForceExpandHeight=false;
                pinnedContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit=ContentSizeFitter.FitMode.PreferredSize;
                var divider=Rect("Separator",pinnedContent,0,0,296,1);divider.anchorMin=new Vector2(0,0);divider.anchorMax=new Vector2(1,0);divider.pivot=new Vector2(0,0);divider.anchoredPosition=Vector2.zero;divider.sizeDelta=new Vector2(0,1);divider.gameObject.AddComponent<Image>().color=new Color32(111,111,111,255);divider.gameObject.AddComponent<LayoutElement>().ignoreLayout=true;
                var scrollBody=Rect("ConversationScroll",host,0,0,296,740);scrollBody.anchorMin=Vector2.zero;scrollBody.anchorMax=Vector2.one;scrollBody.offsetMin=scrollBody.offsetMax=Vector2.zero;
                scrollBody.gameObject.AddComponent<RectMask2D>();scrollBody.gameObject.AddComponent<Image>().color=Color.clear;
                scroll=scrollBody.gameObject.AddComponent<ScrollRect>();scroll.horizontal=false;scroll.vertical=true;scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=32;
                list=Rect("ConversationRows",scrollBody,0,0,296,0);list.anchorMin=new Vector2(0,1);list.anchorMax=Vector2.one;list.pivot=new Vector2(0,1);list.sizeDelta=Vector2.zero;
                var layout=list.gameObject.AddComponent<VerticalLayoutGroup>();layout.spacing=RowSpacing;layout.childControlWidth=true;layout.childControlHeight=true;layout.childForceExpandHeight=false;
                list.gameObject.AddComponent<ContentSizeFitter>().verticalFit=ContentSizeFitter.FitMode.PreferredSize;scroll.viewport=scrollBody;scroll.content=list;
                var stack=candidates.GetComponent<VerticalLayoutGroup>();if(stack!=null){stack.enabled=false;DestroyImmediate(stack);}
                var grid=candidates.gameObject.AddComponent<GridLayoutGroup>();grid.cellSize=new Vector2(140,140);grid.spacing=new Vector2(8,16);grid.padding=new RectOffset(8,8,12,12);grid.constraint=GridLayoutGroup.Constraint.FixedColumnCount;grid.constraintCount=2;
                var graphic=new GameObject("ConversationAssociationLines",typeof(RectTransform),typeof(AssociationLineGraphic));graphic.transform.SetParent(transform,false);
                var r=graphic.GetComponent<RectTransform>();r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;
                lines=graphic.GetComponent<AssociationLineGraphic>();lines.raycastTarget=false;
            }
            else
            {
                if(scroll==null)scroll=list.GetComponentInParent<ScrollRect>();
                if(scroll!=null){scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=32;}
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
            var tasks=owner.Simulation.Tasks.Values.Where(t=>!owner.IsOperatingTask(t.Id) && (!owner.TrainingEnabled||!owner.Training.IsTrainingRequest(t.Id)))
                .OrderByDescending(t=>t.ArrivedAt).ThenBy(t=>t.Id).ToArray();
            var corpus=owner.TrainingEnabled?owner.Training.Session.ConversationCorpora.ToArray():new ConversationCorpusSummary[0];
            var contacts=owner.Narrative==null?new NarrativeContact[0]:owner.Narrative.PinnedContacts.ToArray();
            var pinnedIds=new HashSet<string>(contacts.Select(c=>c.Id));
            string key=Fingerprint(tasks,corpus,contacts,pinnedIds);
            if(key!=fingerprint){Rebuild(tasks,corpus,contacts,pinnedIds);fingerprint=key;}
            UpdateRows(tasks,corpus,contacts);
        }
        private string Fingerprint(TaskInstance[] tasks,ConversationCorpusSummary[] corpus,NarrativeContact[] contacts,HashSet<string> pinnedIds)
        {
            var builder=new System.Text.StringBuilder();
            foreach(var contact in contacts)builder.Append("P:").Append(contact.Id).Append('#').Append(LatestTaskId(contact.Id)??"-").Append('|');
            var seen=new HashSet<string>();
            foreach(var task in tasks)
            {
                if(pinnedIds.Contains(task.ConversationId))continue;
                string id=task.IsFiller?"F:"+task.Id:"C:"+task.ConversationId;
                if(!seen.Add(id))continue;
                builder.Append(id);
                if(!task.IsFiller)builder.Append('#').Append(task.Id);
                builder.Append('|');
            }
            foreach(var summary in corpus)
            {
                if(pinnedIds.Contains(summary.ConversationId))continue;
                if(seen.Add("C:"+summary.ConversationId))builder.Append("C:").Append(summary.ConversationId).Append('|');
            }
            return builder.ToString();
        }
        private void Rebuild(TaskInstance[] tasks,ConversationCorpusSummary[] corpus,NarrativeContact[] contacts,HashSet<string> pinnedIds)
        {
            bool hadOrdinary=rows.Keys.Any(k=>!k.StartsWith("P:"));
            float keep=hadOrdinary&&scroll!=null?scroll.verticalNormalizedPosition:1;
            if(scroll!=null)scroll.StopMovement();
            foreach(var pair in rows)
            {
                if(pair.Value.Rect==null)continue;
                pair.Value.Rect.gameObject.SetActive(false);
                Destroy(pair.Value.Rect.gameObject);
            }
            rows.Clear();
            var pinnedHost=pinnedContent!=null?pinnedContent:list;
            foreach(var contact in contacts)CreateRow(contact.Id,LatestTaskId(contact.Id),contact.Id,pinnedHost,true);
            var seen=new HashSet<string>();
            foreach(var task in tasks)
            {
                if(pinnedIds.Contains(task.ConversationId))continue;
                string id=task.IsFiller?"F:"+task.Id:"C:"+task.ConversationId;
                if(!seen.Add(id))continue;
                CreateRow(id,task.Id,task.ConversationId,list);
            }
            foreach(var summary in corpus)
            {
                if(pinnedIds.Contains(summary.ConversationId))continue;
                string id="C:"+summary.ConversationId;
                if(!seen.Add(id))continue;
                CreateRow(id,null,summary.ConversationId,list);
            }
            if(pinnedContent!=null)
            {
                var divider=pinnedContent.Find("Separator");
                if(divider!=null)divider.SetAsLastSibling();
            }
            LayoutPinned();
            if(scroll!=null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(list);
                Canvas.ForceUpdateCanvases();
                scroll.verticalNormalizedPosition=Mathf.Clamp01(keep);
            }
        }
        private void CreateRow(string key,string requestId,string conversation,RectTransform host,bool pinned=false)
        {
            RectTransform r;InnerBorderGraphic border,corpusBorder;Image badge;Button button;Text name,info;
            string rowName="Conversation_"+(pinned?conversation:(requestId??conversation));
            if(contactTemplate!=null)
            {
                r=Instantiate(contactTemplate,host);r.name=rowName;r.gameObject.SetActive(true);
                name=r.Find("Name").GetComponent<Text>();info=r.Find("Summary").GetComponent<Text>();badge=r.Find("Corpus").GetComponent<Image>();
                border=r.Find("InnerBorder").GetComponent<InnerBorderGraphic>();corpusBorder=badge.transform.Find("InnerBorder").GetComponent<InnerBorderGraphic>();
                button=r.GetComponent<Button>();
            }
            else
            {
                r=Rect(rowName,host,0,0,296,112);r.gameObject.AddComponent<LayoutElement>().preferredHeight=112;
                r.gameObject.AddComponent<Image>().color=new Color32(244,244,244,255);
                border=InnerBorderGraphic.Attach(r,1,BorderEdges.Bottom,new Color32(111,111,111,255));
                button=r.gameObject.AddComponent<Button>();
                name=Label(r,"",12,10,220,58,26);name.name="Name";
                info=Label(r,"",12,76,272,42,20);info.color=new Color32(82,82,82,255);info.name="Summary";
                badge=Rect("Corpus",r,244,18,36,36).gameObject.AddComponent<Image>();badge.color=new Color32(211,197,167,255);
                corpusBorder=InnerBorderGraphic.Attach(badge.rectTransform,1,BorderEdges.All,new Color32(82,82,82,255));
            }
            button.onClick.RemoveAllListeners();button.onClick.AddListener(()=>SelectRow(conversation,requestId,false));
            AddHover(r.gameObject,conversation,requestId,key);AddHover(badge.gameObject,conversation,requestId,key);
            rows.Add(key,new Row{Rect=r,Border=border,Corpus=badge,CorpusBorder=corpusBorder,Button=button,Name=name,Summary=info,Conversation=conversation,RequestId=requestId});
        }
        private void UpdateRows(TaskInstance[] tasks,ConversationCorpusSummary[] corpus,NarrativeContact[] contacts)
        {
            var contactMap=contacts.ToDictionary(c=>c.Id);
            foreach(var pair in rows)
            {
                var row=pair.Value;
                bool pinned=contactMap.TryGetValue(row.Conversation,out var contact);
                int requests=pinned?owner.Simulation.Tasks.Values.Count(t=>t.ConversationId==row.Conversation)
                    :!pair.Key.StartsWith("F:")?tasks.Count(t=>t.ConversationId==row.Conversation):1;
                double affinity=owner.Story==null?0:owner.Story.Affinity(row.Conversation);
                double available=owner.TrainingEnabled?owner.Training.Session.GetConversationCorpus(row.Conversation).AvailablePoints:0;
                var summary=corpus.FirstOrDefault(c=>c.ConversationId==row.Conversation);
                row.Records=summary==null?0:summary.RecordCount;
                row.Name.text=pinned?contact.Name:(row.RequestId!=null?DisplayName(row.RequestId,row.Conversation):owner.ConversationDisplayName(row.Conversation));
                string stats=$"亲密度 {affinity:0.##} · {requests} 请求";
                if(pinned)row.Summary.text=(contact.HasUnread?"● ":"")+stats+"\n"+contact.Summary;
                else if(row.RequestId!=null&&owner.Simulation.Tasks.TryGetValue(row.RequestId,out var task))
                    row.Summary.text=stats+"\n"+CoreGameplayController.StateText(task.State)+(task.IsFiller?" · 轻量来客":"");
                else row.Summary.text=stats+$"\n语料 {row.Records} 条 · {available:0.###} 点";
                bool linked=row.RequestId!=null&&row.RequestId==hovered;
                bool selected=owner.SelectedTask!=null&&(row.RequestId!=null?owner.SelectedTask.Id==row.RequestId:owner.SelectedTask.ConversationId==row.Conversation);
                row.Border.Configure(selected?2:1,selected||linked?BorderEdges.All:BorderEdges.Bottom,selected||linked?new Color32(22,22,22,255):new Color32(111,111,111,255));
                row.Corpus.gameObject.SetActive(row.Records>0);
                row.Corpus.color=linked?Color.white:new Color32(211,197,167,255);
                row.CorpusBorder.Configure(1,BorderEdges.All,linked?new Color32(22,22,22,255):new Color32(82,82,82,255));
                row.Button.interactable=!owner.IsModalOpen;
            }
        }
        private string DisplayName(string requestId,string conversation)
            => owner.Simulation.Tasks.TryGetValue(requestId,out var task)&&task.Visitor!=null?task.Visitor.DisplayName:owner.ConversationDisplayName(conversation);
        private string LatestTaskId(string conversation)
            => owner.Simulation.Tasks.Values.Where(t=>t.ConversationId==conversation)
                .OrderByDescending(t=>t.ArrivedAt).ThenBy(t=>t.Id).Select(t=>t.Id).FirstOrDefault();
        private void LayoutPinned()
        {
            if(pinnedContent==null||pinnedContent==list||scroll==null)return;
            LayoutRebuilder.ForceRebuildLayoutImmediate(pinnedContent);
            var rect=(RectTransform)scroll.transform;
            rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=Vector2.zero;rect.offsetMax=new Vector2(0,-pinnedContent.rect.height);
        }
        public void SelectRow(string conversation,string requestId,bool open)
        {
            if(owner==null||owner.IsModalOpen)return;
            if(owner.IsNarrativeContact(conversation)){SelectConversation(conversation,open);return;}
            if(requestId!=null&&owner.Simulation.Tasks.TryGetValue(requestId,out _))
            {
                owner.SelectRequest(requestId);
                if(open)owner.OpenConversationHistory();
                return;
            }
            SelectConversation(conversation,open);
        }
        public void SelectConversation(string id,bool open)
        {
            if(owner.IsModalOpen)return;
            if(owner.IsNarrativeContact(id)){owner.OpenNarrativeContact(id);return;}
            var task=owner.LatestConversationTask(id);
            if(task==null)return;
            owner.SelectRequest(task.Id);if(open)owner.OpenConversationHistory();
        }
        private void LateUpdate()
        {
            if(lines==null||owner==null)return;
            var paths=new List<Vector2[]>();
            Row row=null;
            if(hovered!=null&&!owner.IsModalOpen)
            {
                if(hoveredKey==null||!rows.TryGetValue(hoveredKey,out row))
                    row=rows.Values.FirstOrDefault(r=>r.RequestId==hovered);
            }
            if(row!=null)
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
        private void AddHover(GameObject go,string id,string requestId,string key){var h=go.AddComponent<ConversationHoverTarget>();h.Bind(this,id,requestId,key);}
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
