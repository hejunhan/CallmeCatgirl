using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using CallmeCatgirl.Gameplay;
using CallmeCatgirl.Gameplay.Training;
namespace CallmeCatgirl.UI
{
    public sealed class TrainingWorkspaceView : MonoBehaviour
    {
        private const float DesignScale=1000f/720f;
        private const float DesignWidthScale=1600f/1280f;
        [SerializeField] private Font font;
        [SerializeField] private RectTransform records,jobs,architectures,recordTemplate,jobTemplate;
        [SerializeField] private Button architectureTemplate,trainButton,computeButton;
        [SerializeField] private InputField modelName;
        [SerializeField] private Text recipeLabel,emptyHint;
        private CoreGameplayController owner;
        private TrainingController controller;
        [SerializeField] private Slider corpusAmount;
        [SerializeField] private Text investmentLabel, investmentScores;
        [SerializeField] private CorpusShapeGraphic shapePreview;
        private string selectedConversation;
        private double investedPoints;
        private string sessionModel;
        public string SelectedConversation => selectedConversation;
        public double InvestedPoints => investedPoints;
        private string recipe,architecture;
        public void Bind(TrainingController source)
        {
            controller=source;owner=source.GetComponent<CoreGameplayController>();
            var session=source.Session;recipe=session.Recipes[0].Id;
            if(sessionModel!=session.ActiveModelId){selectedConversation=null;investedPoints=0;sessionModel=session.ActiveModelId;}
            if(selectedConversation!=null && session.GetConversationCorpus(selectedConversation).AvailablePoints<=1e-9)
            {selectedConversation=null;investedPoints=0;}
            if(architecture==null||!session.Architectures.Any(a=>a.Id==architecture&&a.ContentReady&&a.UnlockStage<=session.CurrentStage))architecture=session.Architectures[0].Id;
            recipeLabel.text="配方："+session.Recipes[0].Title+" · 费用 "+session.Recipes[0].Cost+" · 计算量 "+session.Recipes[0].WorkTokens;
            trainButton.onClick.RemoveAllListeners();trainButton.onClick.AddListener(()=>{
                var result=owner.ExecuteCommand(new GameCommand{kind="train",recipe=recipe,architecture=architecture,displayName=modelName.text,conversationId=selectedConversation,corpusPoints=investedPoints});
                if(result.ok){selectedConversation=null;investedPoints=0;Bind(controller);}
            });
            computeButton.onClick.RemoveAllListeners();computeButton.onClick.AddListener(()=>owner.ExecuteCommand(new GameCommand{kind="upgradeCompute"}));
            trainButton.interactable=controller.CanManage;computeButton.interactable=controller.CanManage&&session.NextCompute!=null;
            Clear(records,emptyHint.transform);Clear(jobs);Clear(architectures);
            emptyHint.gameObject.SetActive(session.ConversationCorpora.Count==0);
            foreach(var arch in session.Architectures)
            {
                var a=arch;var button=Instantiate(architectureTemplate,architectures);button.name=a.Id;button.gameObject.SetActive(true);
                button.GetComponent<Image>().color=a.Id==architecture?new Color(.65f,.82f,.82f):new Color(.87f,.9f,.91f);
                var label=button.GetComponentInChildren<Text>(true);
                label.text=a.Title+" · 内存"+a.UnlockStage+"级\n语料×"+a.CorpusMultiplier.ToString("0.###")+" / 速度×"+a.SpeedMultiplier.ToString("0.###");
                label.resizeTextForBestFit=true;label.resizeTextMinSize=11;label.resizeTextMaxSize=18;button.interactable=controller.CanManage&&a.ContentReady&&a.UnlockStage<=session.CurrentStage;
                button.onClick.RemoveAllListeners();button.onClick.AddListener(()=>{architecture=a.Id;Bind(controller);});
            }
            foreach(var conversation in session.ConversationCorpora)
            {
                var id=conversation.ConversationId;
                var row=Instantiate(recordTemplate,records);row.name="ConversationCorpus_"+id;row.gameObject.SetActive(true);
                bool used=conversation.AvailablePoints<=1e-9;
                row.Find("Title").GetComponent<Text>().text=owner.Story.Name(id)+" · 可用 "+conversation.AvailablePoints.ToString("0.###")+" / 累计 "+conversation.TotalPoints.ToString("0.###")+" 点";
                row.Find("Scores").GetComponent<Text>().text=string.Join("  ",conversation.AvailableScores.Select(pair=>CoreGameplayController.AbilityText(pair.Key)+" "+pair.Value.ToString("0.###")));
                var button=row.Find("Action").GetComponent<Button>();button.interactable=!used&&controller.CanManage;
                button.GetComponentInChildren<Text>(true).text=selectedConversation==id?"已投入此对话":"拖入训练 / 选择";
                button.onClick.RemoveAllListeners();button.onClick.AddListener(()=>SelectConversation(id));
                var drag=button.gameObject.AddComponent<ConversationCorpusDrag>();drag.Bind(this,id);
                var debugRecord=conversation.Records.FirstOrDefault(r=>!session.IsConsumed(r.Id))??conversation.Records[0];
                DebugDock(row,"corpus",debugRecord.Id);
            }
            RefreshInvestment();
            foreach(var job in session.Jobs)
            {
                var row=Instantiate(jobTemplate,jobs);row.name="Training_"+job.RequestId;row.gameObject.SetActive(true);var task=controller.Simulation.Tasks[job.RequestId];
                row.Find("Title").GetComponent<Text>().text=job.Output.Name+" · "+CoreGameplayController.StateText(task.State)+" · "+(task.Progress/task.Definition.Calculation*100).ToString("0")+"%";
                var button=row.Find("Action").GetComponent<Button>();button.interactable=controller.CanManage&&!job.Completed&&task.State==CallmeCatgirl.Gameplay.TaskGrid.RequestState.Ready;
                button.onClick.RemoveAllListeners();button.onClick.AddListener(()=>owner.ExecuteCommand(new GameCommand{kind="submit",id=job.RequestId}));DebugDock(row,"training",job.RequestId);
            }
        }
        public void SelectConversation(string id)
        {
            if(controller==null||!controller.CanManage)return;
            double available=controller.Session.GetConversationCorpus(id).AvailablePoints;
            if(available<=1e-9)return;
            selectedConversation=id;investedPoints=Math.Min(available,controller.Session.Recipes[0].MaximumCorpusPoints);
            RefreshInvestment();
        }
        private void RefreshInvestment()
        {
            if(corpusAmount==null||investmentLabel==null)return;
            var session=controller.Session;var rule=session.Recipes[0];
            double available=selectedConversation==null?0:session.GetConversationCorpus(selectedConversation).AvailablePoints;
            investedPoints=Math.Min(investedPoints,Math.Min(available,rule.MaximumCorpusPoints));
            corpusAmount.onValueChanged.RemoveAllListeners();
            corpusAmount.minValue=0;corpusAmount.maxValue=(float)Math.Min(available,rule.MaximumCorpusPoints);
            corpusAmount.SetValueWithoutNotify((float)investedPoints);corpusAmount.interactable=available>0&&controller.CanManage;
            corpusAmount.onValueChanged.AddListener(value=>{
                double amount=value>=corpusAmount.maxValue-1e-6f?Math.Min(available,rule.MaximumCorpusPoints):Math.Round(value,1);
                investedPoints=Math.Min(available,amount);RefreshInvestment();
            });
            investmentLabel.text=selectedConversation==null?"将左侧对话拖到这里，选择投入点数":owner.Story.Name(selectedConversation)+" · 投入 "+investedPoints.ToString("0.###")+" / 可用 "+available.ToString("0.###")+" 点";
            bool valid=investedPoints>1e-9&&selectedConversation!=null;
            if(valid)
            {
                try
                {
                    var cells=TrainingSession.GrowShape(rule,investedPoints,controller.Simulation.Width,controller.Simulation.Height);
                    shapePreview.SetCells(cells);
                    investmentScores.text="占用 "+cells.Length+" 格 · 每 "+rule.PointsPerExtraCell+" 点 +1格\n"+
                        string.Join("  ",session.PreviewTrainingAbilities(recipe,selectedConversation,investedPoints).Select(pair=>CoreGameplayController.AbilityText(pair.Key)+" "+pair.Value.ToString("0.###")));
                }
                catch(ArgumentException){valid=false;investmentScores.text="投入过多，形状超出棋盘；请减少点数。";shapePreview.SetCells(Array.Empty<CallmeCatgirl.Gameplay.TaskGrid.Cell>());}
            }
            else{investmentScores.text="确认生成时扣除语料，剩余点数可留到下次。";shapePreview.SetCells(Array.Empty<CallmeCatgirl.Gameplay.TaskGrid.Cell>());}
            trainButton.interactable=controller.CanManage&&valid;
        }
        private static void Clear(RectTransform parent,Transform keep=null)
        {
            foreach(var child in parent.Cast<Transform>().Where(c=>c!=keep).ToArray())
            {child.gameObject.SetActive(false);if(Application.isPlaying)Destroy(child.gameObject);else DestroyImmediate(child.gameObject);}
        }
        private void DebugDock(RectTransform row,string kind,string id)
        {
            if(!Application.isPlaying||!CallmeCatgirl.Debugging.DebugBuild.Enabled)return;
            var slot=(RectTransform)row.Find("InlineDebugSlot");
            var fold=LocalDebugFoldout.Attach(slot,owner.DebugCommands,kind,id,font);
            if(fold!=null)slot.GetComponentInChildren<Button>().onClick.AddListener(()=>row.GetComponent<LayoutElement>().preferredHeight=-slot.anchoredPosition.y+slot.GetComponent<LayoutElement>().preferredHeight);
        }
#if UNITY_EDITOR
        public void PrepareSceneLayout(Font sourceFont)
        {
            if(modelName!=null)return;font=sourceFont;
            Text(transform,"语料库存",28,126,520,30,22);
            records=List(transform,"Corpus",28,166,550,460);
            emptyHint=Text(records,"完成订单并接受回复后，合格语料会出现在这里。",0,0,510,80);
            emptyHint.gameObject.AddComponent<LayoutElement>().preferredHeight=80*DesignScale;
            Text(transform,"训练新模型",610,126,550,30,22);Text(transform,"新模型名称",610,166,500,24);
            var input=Rect(transform,"NewModelName",610,194,550,34);input.gameObject.AddComponent<Image>().color=new Color(.94f,.94f,.94f);
            modelName=input.gameObject.AddComponent<InputField>();modelName.textComponent=Text(input,"我的模型",8,5,530,28);modelName.characterLimit=40;modelName.text="我的模型";
            recipeLabel=Text(transform,"配方与费用",610,238,550,36);
            architectures=Rect(transform,"ArchitectureOptions",610,278,550,32);var horizontal=architectures.gameObject.AddComponent<HorizontalLayoutGroup>();horizontal.spacing=6*DesignScale;horizontal.childControlWidth=false;horizontal.childControlHeight=false;horizontal.childForceExpandWidth=false;
            trainButton=Button(transform,"生成训练模块",610,322,240);computeButton=Button(transform,"升级算力",880,322,200);
            Text(transform,"将训练模块放入棋盘运行",610,362,550,28);jobs=List(transform,"TrainingJobs",610,398,550,228);
            var templates=Rect(transform,"TrainingTemplates",0,0,550,200);templates.gameObject.SetActive(false);
            architectureTemplate=Button(templates,"ArchitectureButtonTemplate",0,0,178);
            recordTemplate=Rect(templates,"CorpusRowTemplate",0,0,520,138);recordTemplate.gameObject.AddComponent<LayoutElement>().preferredHeight=138*DesignScale;
            Text(recordTemplate,"联系人 / 对话轮次",0,0,500,28).name="Title";Text(recordTemplate,"能力分数",0,30,500,28).name="Scores";
            Button(recordTemplate,"Action",0,62,160).GetComponentInChildren<Text>().text="选择此语料";DebugSlot(recordTemplate,102);
            jobTemplate=Rect(templates,"TrainingJobTemplate",0,0,520,128);jobTemplate.gameObject.AddComponent<LayoutElement>().preferredHeight=128*DesignScale;
            Text(jobTemplate,"训练进度",0,0,500,50).name="Title";Button(jobTemplate,"Action",0,54,220).GetComponentInChildren<Text>().text="确认训练产物";DebugSlot(jobTemplate,96);
        }
        public void PrepareCorpusInvestment()
        {
            if(corpusAmount!=null)
            {
                if(shapePreview!=null&&shapePreview.GetComponent<CanvasRenderer>()==null)shapePreview.gameObject.AddComponent<CanvasRenderer>();
                if(corpusAmount.handleRect!=null)corpusAmount.handleRect.sizeDelta=new Vector2(corpusAmount.handleRect.sizeDelta.x,0);
                return;
            }
            var drop=Rect(transform,"ConversationCorpusInvestment",610,318,550,152);
            drop.gameObject.AddComponent<Image>().color=new Color(.91f,.93f,.93f);
            drop.gameObject.AddComponent<ConversationCorpusDrop>().View=this;
            investmentLabel=Text(drop,"将左侧对话拖到这里，选择投入点数",8,4,530,30,15);
            var sliderRect=Rect(drop,"CorpusPoints",12,40,500,26);
            sliderRect.gameObject.AddComponent<Image>().color=new Color(.75f,.78f,.79f);
            corpusAmount=sliderRect.gameObject.AddComponent<Slider>();
            var handle=Rect(sliderRect,"Handle",0,0,20,26);handle.gameObject.AddComponent<Image>().color=new Color(.2f,.35f,.4f);
            corpusAmount.handleRect=handle;handle.sizeDelta=new Vector2(20*DesignWidthScale,0);corpusAmount.targetGraphic=handle.GetComponent<Image>();
            investmentScores=Text(drop,"投入预览",8,78,400,68,15);
            shapePreview=Rect(drop,"ShapePreview",420,78,120,68).gameObject.AddComponent<CorpusShapeGraphic>();
            shapePreview.color=new Color(.22f,.48f,.48f);shapePreview.raycastTarget=false;
            trainButton.GetComponent<RectTransform>().anchoredPosition=new Vector2(610*DesignWidthScale,-480*DesignScale);
            computeButton.GetComponent<RectTransform>().anchoredPosition=new Vector2(880*DesignWidthScale,-480*DesignScale);
            var tip=transform.Cast<Transform>().FirstOrDefault(t=>t.GetComponent<Text>()?.text=="将训练模块放入棋盘运行");
            if(tip!=null)((RectTransform)tip).anchoredPosition=new Vector2(610*DesignWidthScale,-520*DesignScale);
            var viewport=(RectTransform)jobs.parent;viewport.anchoredPosition=new Vector2(610*DesignWidthScale,-554*DesignScale);viewport.sizeDelta=new Vector2(550*DesignWidthScale,85*DesignScale);
            emptyHint.text="完成合格请求的全部对白后，语料点数会计入对应对话。拖入对话后选择本次投入量。";
        }
        private void DebugSlot(RectTransform row,float y){var slot=Rect(row,"InlineDebugSlot",0,y,500,270);slot.sizeDelta=new Vector2(500*DesignWidthScale,270*DesignScale);}
        private RectTransform Rect(Transform parent,string name,float x,float y,float w,float h)
        {var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();r.SetParent(parent,false);r.anchorMin=r.anchorMax=r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(x*DesignWidthScale,-y*DesignScale);r.sizeDelta=new Vector2(w*DesignWidthScale,h*DesignScale);return r;}
        private Text Text(Transform parent,string value,float x,float y,float w,float h,int size=16)
        {var t=Rect(parent,"Text",x,y,w,h).gameObject.AddComponent<Text>();t.font=font;t.fontSize=Mathf.RoundToInt(size*DesignScale);t.color=Color.black;t.text=value;t.raycastTarget=false;return t;}
        private Button Button(Transform parent,string title,float x,float y,float width)
        {var r=Rect(parent,title,x,y,width,32);r.gameObject.AddComponent<Image>().color=new Color(.87f,.9f,.91f);var b=r.gameObject.AddComponent<Button>();Text(r,title,8,5,width-12,28);return b;}
        private RectTransform List(Transform parent,string name,float x,float y,float w,float h)
        {
            var viewport=Rect(parent,name,x,y,w,h);viewport.gameObject.AddComponent<Image>().color=new Color(.96f,.96f,.96f);viewport.gameObject.AddComponent<RectMask2D>();
            var scroll=viewport.gameObject.AddComponent<ScrollRect>();scroll.horizontal=false;
            var list=Rect(viewport,"Rows",0,0,w,0);list.anchorMin=new Vector2(0,1);list.anchorMax=new Vector2(1,1);list.sizeDelta=Vector2.zero;
            var layout=list.gameObject.AddComponent<VerticalLayoutGroup>();layout.spacing=14;layout.padding=new RectOffset(11,11,11,11);layout.childControlWidth=layout.childControlHeight=true;layout.childForceExpandHeight=false;
            list.gameObject.AddComponent<ContentSizeFitter>().verticalFit=ContentSizeFitter.FitMode.PreferredSize;scroll.viewport=viewport;scroll.content=list;return list;
        }
#endif
    }
}
