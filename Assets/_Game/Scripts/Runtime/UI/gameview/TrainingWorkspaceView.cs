using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using CallmeCatgirl.Gameplay;
using CallmeCatgirl.Gameplay.Training;
using CallmeCatgirl.Gameplay.TaskGrid;

namespace CallmeCatgirl.UI
{
    // Inventory is a view of the ledger. The arrangement is a draft, independent of compute occupancy.
    public sealed class TrainingWorkspaceView : MonoBehaviour
    {
        [SerializeField] private Font font;
        [SerializeField,Min(1)] private int arrangementColumns=3,arrangementRows=3;
        [SerializeField] private Color selectedSlotColor=new Color32(179,208,202,255), idleSlotColor=new Color32(248,249,246,255);
        private readonly CorpusArrangement draft=new CorpusArrangement();
        private TrainingController controller;
        private TrainingSession session;
        private CoreGameplayController owner;
        [SerializeField] private RectTransform page,inventory,board,inventoryTemplate;
        [SerializeField] private InputField query,modelName;
        [SerializeField] private Dropdown filter,recipes,architectures;
        [SerializeField] private Slider amount;
        [SerializeField] private Text summary,detail,message,selection;
        [SerializeField] private Button start,remove,modelsButton,closeButton,cancelButton;
        [SerializeField] private List<Button> slots=new List<Button>();
        [SerializeField] private List<Button> recommendations=new List<Button>();
        private bool actionsBound;
        private TrainingRecipe[] recipeOptions;
        private ArchitectureDefinition[] architectureOptions;
        private string selected,recipe,architecture,search="";
        private AbilityKind? domain;
        private bool submitting;
        private Action returnToModels;
        public IReadOnlyList<ConversationInvestment> Investments=>draft.Investments;
        public string SelectedConversation=>selected;
        public double InvestedPoints=>Investments.FirstOrDefault(i=>i.ConversationId==selected)?.Points??0;
        public event Action TrainingStarted;
        public void ConfigureNavigation(Action models)=>returnToModels=models;
        private void OnEnable(){if(query!=null)EventSystem.current?.SetSelectedGameObject(query.gameObject);}
        private void OnDisable(){draft.Clear();selected=null;}

        public void ConfigureArrangement(int columns,int rows)
        {
            draft.Configure(columns,rows);arrangementColumns=columns;arrangementRows=rows;
            if(page!=null){board.GetComponent<AspectRatioFitter>().aspectRatio=(float)columns/rows;BuildSlots();RefreshDraft();}
        }
        public void Bind(TrainingController source)
        {
            if(source==null||!source.IsBound)return;
            controller=source;owner=source.GetComponent<CoreGameplayController>();
            if(!ReferenceEquals(session,source.Session))
            {session=source.Session;draft.Clear();selected=null;recipe=null;architecture=null;}
            if(page==null||inventoryTemplate==null)throw new InvalidOperationException("Save the authored training page and corpus template before binding a session.");
            BindActions();
            foreach(var item in Investments)
            {
                double available=session.GetConversationCorpus(item.ConversationId).AvailablePoints;
                if(available<=1e-9)draft.Remove(item.ConversationId);
                else draft.SetPoints(item.ConversationId,Math.Min(item.Points,available));
            }
            if(recipe==null||!session.Recipes.Any(r=>r.Id==recipe)||(recipe==TrainingSession.StarterRecipeId&&!session.StarterTrainingAvailable))
                recipe=session.StarterTrainingAvailable?TrainingSession.StarterRecipeId:session.Recipes.First(r=>r.Id!=TrainingSession.StarterRecipeId).Id;
            if(architecture==null||session.ArchitectureAvailability(architecture)!=TrainingResult.Success)
                architecture=session.Architectures.First(a=>session.ArchitectureAvailability(a.Id)==TrainingResult.Success).Id;
            RefreshPickers();RefreshInventory();RefreshDraft();
        }
        private void EnsurePage()
        {
            if(page!=null)return;
            if(font==null)font=GetComponentsInChildren<Text>(true).Select(t=>t.font).FirstOrDefault(f=>f!=null);
            if(font==null)font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            foreach(Transform child in transform)child.gameObject.SetActive(false);
            draft.Configure(arrangementColumns,arrangementRows);
            page=Area(transform,"TrainingFullPage",0,0,1,1);Background(page,new Color32(239,239,234,255));
            Label(Area(page,"Title",.025f,.935f,.60f,.99f),"语料与训练 / CORPUS LAB",26);
            modelsButton=MakeButton(Area(page,"Models",.63f,.935f,.825f,.99f),"模型目录",()=>{if(returnToModels!=null)returnToModels();else Cancel();});
            closeButton=MakeButton(Area(page,"Close",.85f,.935f,.975f,.99f),"取消并返回",Cancel);
            var left=Area(page,"InventoryPanel",.025f,.155f,.485f,.92f);Background(left,Color.white);
            Label(Area(left,"InventoryTitle",.025f,.92f,.975f,.99f),"库存 · 筛选不改变已选投入",20);
            query=MakeInput(Area(left,"Search",.025f,.83f,.61f,.91f),"搜索联系人或来源");
            query.onValueChanged.AddListener(value=>{search=value;RefreshInventory();});
            filter=MakeDropdown(Area(left,"Filter",.63f,.83f,.975f,.91f),new[]{"全部领域","写作","代码","推理","检索"});
            filter.onValueChanged.AddListener(value=>{domain=value==0?(AbilityKind?)null:(AbilityKind)(value-1);RefreshInventory();});
            Label(Area(left,"RecommendationTitle",.025f,.77f,.975f,.825f),"四域推荐 · 仅取当前剩余库存",16);
            for(int i=0;i<4;i++)
            {
                var kind=(AbilityKind)i;
                recommendations.Add(MakeButton(Area(left,"Recommend_"+kind,.025f+i*.24f,.70f,.245f+i*.24f,.765f),CoreGameplayController.AbilityText(kind),()=>Recommend(kind)));
            }
            inventory=MakeList(Area(left,"InventoryScroll",.025f,.02f,.975f,.685f));
            var right=Area(page,"ArrangementPanel",.51f,.155f,.975f,.92f);Background(right,new Color32(225,229,225,255));
            Label(Area(right,"BoardTitle",.025f,.925f,.975f,.99f),"训练编排 · 点击添加 / 按住拖放",20);
            Label(Area(right,"BoardHint",.025f,.865f,.975f,.925f),"编排格整理投入；实际运算占格见底部。",15);
            modelName=MakeInput(Area(right,"ModelName",.025f,.80f,.48f,.86f),"模型名称");modelName.text="我的模型";modelName.characterLimit=40;
            modelName.onValueChanged.AddListener(_=>RefreshDraft());
            recipes=MakeDropdown(Area(right,"Recipe",.50f,.80f,.975f,.86f),Array.Empty<string>());
            recipes.onValueChanged.AddListener(index=>{if(recipeOptions==null||index>=recipeOptions.Length)return;recipe=recipeOptions[index].Id;RefreshDraft();});
            architectures=MakeDropdown(Area(right,"Architecture",.025f,.73f,.975f,.79f),Array.Empty<string>());
            architectures.onValueChanged.AddListener(index=>{if(architectureOptions==null||index>=architectureOptions.Length)return;architecture=architectureOptions[index].Id;RefreshDraft();});
            var boardArea=Area(right,"ArrangementGridArea",.025f,.42f,.975f,.72f);
            board=Area(boardArea,"ArrangementGrid",0,0,1,1);var aspect=board.gameObject.AddComponent<AspectRatioFitter>();
            aspect.aspectMode=AspectRatioFitter.AspectMode.FitInParent;aspect.aspectRatio=(float)draft.Columns/draft.Rows;BuildSlots();
            selection=Label(Area(right,"Selected",.025f,.355f,.79f,.415f),"先添加一份语料",16);
            remove=MakeButton(Area(right,"Remove",.80f,.355f,.975f,.415f),"移除",RemoveSelectedConversation);
            amount=MakeSlider(Area(right,"Amount",.04f,.295f,.96f,.345f));amount.onValueChanged.AddListener(ChangeAmount);
            var details=MakeList(Area(right,"DetailsScroll",.025f,.025f,.975f,.285f));
            detail=Label(Area(details,"Detail",0,0,1,1),"悬停、键盘聚焦或点击库存卡片查看数值。",17);
            detail.gameObject.AddComponent<LayoutElement>().preferredHeight=160;
            var footer=Area(page,"Footer",.025f,.015f,.975f,.105f);Background(footer,new Color32(30,38,39,255));
            summary=Label(Area(footer,"Summary",.015f,.05f,.63f,.95f),"尚未投入语料",17,Color.white);
            start=MakeButton(Area(footer,"Start",.66f,.18f,.835f,.82f),"开始训练",StartTraining);
            cancelButton=MakeButton(Area(footer,"Cancel",.85f,.18f,.985f,.82f),"取消",Cancel);
            message=Label(Area(page,"Message",.025f,.105f,.975f,.145f),"",15);
        }
        private void BindActions()
        {
            if(actionsBound)return; actionsBound=true;
            if(draft.Columns!=arrangementColumns||draft.Rows!=arrangementRows)draft.Configure(arrangementColumns,arrangementRows);
            Wire(modelsButton,()=>{if(returnToModels!=null)returnToModels();else Cancel();});
            Wire(closeButton,Cancel);Wire(cancelButton,Cancel);Wire(start,StartTraining);Wire(remove,RemoveSelectedConversation);
            for(int i=0;i<recommendations.Count;i++){var kind=(AbilityKind)i;Wire(recommendations[i],()=>Recommend(kind));}
            for(int i=0;i<slots.Count;i++){int index=i;Wire(slots[i],()=>SelectSlot(index));}
            query.onValueChanged.RemoveAllListeners();query.onValueChanged.AddListener(value=>{search=value;RefreshInventory();});
            filter.onValueChanged.RemoveAllListeners();filter.onValueChanged.AddListener(value=>{domain=value==0?(AbilityKind?)null:(AbilityKind)(value-1);RefreshInventory();});
            modelName.onValueChanged.RemoveAllListeners();modelName.onValueChanged.AddListener(_=>RefreshDraft());
            recipes.onValueChanged.RemoveAllListeners();recipes.onValueChanged.AddListener(index=>{if(recipeOptions==null||index>=recipeOptions.Length)return;recipe=recipeOptions[index].Id;RefreshDraft();});
            architectures.onValueChanged.RemoveAllListeners();architectures.onValueChanged.AddListener(index=>{if(architectureOptions==null||index>=architectureOptions.Length)return;architecture=architectureOptions[index].Id;RefreshDraft();});
            amount.onValueChanged.RemoveAllListeners();amount.onValueChanged.AddListener(ChangeAmount);
        }
        private static void Wire(Button button,Action action){button.onClick.RemoveAllListeners();button.onClick.AddListener(()=>action());}
        private void SelectSlot(int index){if(draft.Slots.TryGetValue(index,out var item)){selected=item.ConversationId;ShowCorpusDetails(selected);RefreshDraft();}}
        private void RefreshPickers()
        {
            recipeOptions=session.Recipes.Where(r=>r.Id!=TrainingSession.StarterRecipeId||session.StarterTrainingAvailable).ToArray();
            recipes.ClearOptions();recipes.AddOptions(recipeOptions.Select(r=>r.Title).ToList());recipes.SetValueWithoutNotify(Array.FindIndex(recipeOptions,r=>r.Id==recipe));
            architectureOptions=session.Architectures.ToArray();architectures.ClearOptions();
            architectures.AddOptions(architectureOptions.Select(a=>a.Title+" · "+(session.ArchitectureAvailability(a.Id)==TrainingResult.Success?"可用":"需"+a.UnlockStage+"条内存")+" · 语料×"+a.CorpusMultiplier.ToString("0.##")+" / 速度×"+a.SpeedMultiplier.ToString("0.##")).ToList());
            architectures.SetValueWithoutNotify(Array.FindIndex(architectureOptions,a=>a.Id==architecture));
        }
        public void SetInventoryFilter(AbilityKind? ability,string text)
        {domain=ability;search=text??"";if(page==null)return;query.SetTextWithoutNotify(search);filter.SetValueWithoutNotify(ability.HasValue?(int)ability.Value+1:0);RefreshInventory();}
        private string CorpusName(string id)
        {
            var record=session.GetConversationCorpus(id).Records.FirstOrDefault();
            return owner!=null?owner.ConversationDisplayName(id):record!=null&&record.SourceKind==CorpusSourceKind.ScriptedReward?record.ScoreSource:id;
        }
        private void RefreshInventory()
        {
            if(inventory==null||session==null)return;Clear(inventory);
            var entries=session.QueryConversationCorpora(domain,search,displayName:CorpusName);
            if(entries.Count==0){ListText(inventory,"没有符合筛选的剩余语料。\n已选配方仍保留在右侧。",100);return;}
            foreach(var entry in entries)
            {
                string id=entry.ConversationId;
                var card=Instantiate(inventoryTemplate,inventory);card.name="Corpus_"+id;card.gameObject.SetActive(true);
                card.Find("Title").GetComponent<Text>().text=CorpusName(id)+" · 可用 "+entry.AvailablePoints.ToString("0.###")+" 点";
                card.Find("Scores").GetComponent<Text>().text=Scores(entry.AvailableScores);
                var button=card.Find("Action").GetComponent<Button>();
                button.GetComponentInChildren<Text>(true).text=draft.Find(id)>=0?"已编排 · 查看 / 调整":"点击添加 / 按住拖入右侧";
                Wire(button,()=>SelectConversation(id));button.interactable=controller.CanManage;
                foreach(var drag in card.GetComponentsInChildren<ConversationCorpusDrag>(true))drag.Bind(this,id);
                foreach(var info in card.GetComponentsInChildren<CorpusCardDetails>(true))info.Bind(this,id);
            }
        }
        private void Recommend(AbilityKind kind)
        {
            var best=session.QueryConversationCorpora(kind).FirstOrDefault();
            if(best==null){message.text="当前没有可用的"+CoreGameplayController.AbilityText(kind)+"语料。";return;}
            SetInventoryFilter(kind,"");SelectConversation(best.ConversationId);
        }
        private TrainingRecipe Rule=>session.Recipes.First(r=>r.Id==recipe);
        public void SelectConversation(string id)
        {
            if(controller==null||!controller.CanManage)return;
            int slot=draft.Find(id);if(slot<0)slot=draft.FirstFree;
            if(slot<0){ShowCorpusDetails(id);message.text="编排板已满，请先移除一份。";return;}
            TryPlaceConversation(id,slot);ShowCorpusDetails(id);
        }
        public bool CanDropConversation(string id,int slot)
            =>controller!=null&&controller.CanManage&&draft.CanPlace(id,slot)&&session.GetConversationCorpus(id).AvailablePoints>1e-9
                && Rule.MaximumCorpusPoints-Investments.Where(i=>i.ConversationId!=id).Sum(i=>i.Points)>1e-9;
        public bool TryPlaceConversation(string id,int slot)
        {
            if(!CanDropConversation(id,slot))return false;
            double available=session.GetConversationCorpus(id).AvailablePoints;
            double other=Investments.Where(i=>i.ConversationId!=id).Sum(i=>i.Points);
            double limit=Math.Min(available,Math.Max(0,Rule.MaximumCorpusPoints-other));
            double existing=Investments.FirstOrDefault(i=>i.ConversationId==id)?.Points??Math.Min(1,limit);
            if(limit<=1e-9){message.text="已达到当前配方点数上限。";return false;}
            if(!draft.TryPlace(id,slot,Math.Min(existing,limit)))return false;
            selected=id;message.text="已放入草稿，尚未消耗语料。";RefreshDraft();return true;
        }
        public void ShowCorpusDetails(string id)
        {
            if(detail==null||session==null)return;var entry=session.GetConversationCorpus(id);
            detail.text=CorpusName(id)+"\n"+Scores(entry.AvailableScores)+"\n可用 "+entry.AvailablePoints.ToString("0.###")+" / 累计 "+entry.TotalPoints.ToString("0.###")+" 点\n"+
                string.Join("\n",entry.Records.Select(r=>r.ScoreSource+" · "+(r.SourceKind==CorpusSourceKind.ScriptedReward?"剧情奖励":"已完成对话")+" · 剩余 "+session.RemainingPoints(r.Id).ToString("0.###")));
            detail.GetComponent<LayoutElement>().preferredHeight=Math.Max(160,detail.preferredHeight+20);
        }
        public void RemoveSelectedConversation()
        {if(!controller.CanManage||selected==null)return;draft.Remove(selected);selected=Investments.FirstOrDefault()?.ConversationId;RefreshDraft();}
        private void ChangeAmount(float value)
        {
            if(selected==null)return;if(value<=.001f){RemoveSelectedConversation();return;}
            draft.SetPoints(selected,Math.Round(value,3));RefreshDraft();
        }
        private void RefreshDraft()
        {
            if(session==null||page==null||recipe==null||start==null)return;
            if(selected!=null&&draft.Find(selected)<0)selected=Investments.FirstOrDefault()?.ConversationId;
            for(int i=0;i<slots.Count;i++)
            {
                draft.Slots.TryGetValue(i,out var item);
                var name=item==null?"":CorpusName(item.ConversationId);
                slots[i].GetComponentInChildren<Text>().text=item==null?"＋":(name.Length>8?name.Substring(0,8)+"…":name)+"\n"+item.Points.ToString("0.###")+" 点";
                slots[i].GetComponent<Image>().color=item?.ConversationId==selected?selectedSlotColor:idleSlotColor;
            }
            double available=selected==null?0:session.GetConversationCorpus(selected).AvailablePoints;
            double other=Investments.Where(i=>i.ConversationId!=selected).Sum(i=>i.Points);
            double limit=Math.Min(available,Math.Max(0,Rule.MaximumCorpusPoints-other));
            if(selected!=null&&InvestedPoints>limit+1e-9)
            {if(limit<=1e-9)draft.Remove(selected);else draft.SetPoints(selected,limit);}
            amount.minValue=0;amount.maxValue=(float)limit;amount.SetValueWithoutNotify((float)InvestedPoints);amount.interactable=controller.CanManage&&limit>0;
            remove.interactable=controller.CanManage&&selected!=null;
            selection.text=selected==null?"点击库存卡片添加，或拖入空位":CorpusName(selected)+" · 投入 "+InvestedPoints.ToString("0.###")+" / "+available.ToString("0.###")+" 点";
            bool valid=false;
            try
            {
                var preview=session.PreviewTraining(recipe,Investments);
                valid=session.ArchitectureAvailability(architecture)==TrainingResult.Success&&preview.Shape.Count<=controller.Simulation.OpenCellCount;
                summary.text="共 "+Investments.Count+" 份 / "+preview.TotalPoints.ToString("0.###")+" 点 · 费用 "+preview.Cost+" · 实际占 "+preview.Shape.Count+" 格\n"+Scores(preview.Abilities)+" · 约 "+preview.EstimatedSeconds.ToString("0.#")+" 游戏秒";
                if(!valid)message.text=session.ArchitectureAvailability(architecture)!=TrainingResult.Success?"此架构尚未解锁。":"实际运算区不足；请减少投入或升级内存。";
            }
            catch(ArgumentException){summary.text="尚未形成有效配方 · 草稿不扣款、不消耗\n首训选免费配方并投入准确 1 点；普通训练可调节。";}
            catch(OverflowException){summary.text="投入过大，请减少语料点数。";}
            start.interactable=valid&&controller.CanManage&&!submitting&&ModelNames.TryNormalize(modelName.text,out _);
        }
        private void StartTraining()
        {
            if(submitting||!start.interactable)return;submitting=true;start.interactable=false;var frozen=Investments.ToArray();
            bool ok=owner!=null?owner.ExecuteCommand(new GameCommand{kind="train",recipe=recipe,architecture=architecture,displayName=modelName.text,investments=frozen}).ok
                :controller.StartConversationsTraining(recipe,architecture,modelName.text,frozen)==TrainingResult.Success;
            submitting=false;if(!ok){message.text=controller.Message;RefreshDraft();return;}
            draft.Clear();selected=null;TrainingStarted?.Invoke();controller.GetComponent<TrainingPanel>()?.Close();
        }
        public void Cancel(){if(submitting)return;draft.Clear();selected=null;controller?.GetComponent<TrainingPanel>()?.Close();}
        private void BuildSlots()
        {
            Clear(board);slots.Clear();
            for(int i=0;i<draft.Columns*draft.Rows;i++)
            {
                int index=i,x=i%draft.Columns,y=i/draft.Columns;
                var cell=Area(board,"Slot_"+i,(float)x/draft.Columns,1f-(float)(y+1)/draft.Rows,(float)(x+1)/draft.Columns,1f-(float)y/draft.Rows,3);
                var button=MakeButton(cell,"＋",()=>{if(draft.Slots.TryGetValue(index,out var item)){selected=item.ConversationId;ShowCorpusDetails(selected);RefreshDraft();}});
                button.GetComponentInChildren<Text>().fontSize=16;
                var drop=cell.gameObject.AddComponent<ConversationCorpusDrop>();drop.View=this;drop.Slot=index;slots.Add(button);
            }
        }
        private static string Scores(IReadOnlyDictionary<AbilityKind,double> scores)
            =>string.Join("  ",Enum.GetValues(typeof(AbilityKind)).Cast<AbilityKind>().Select(k=>CoreGameplayController.AbilityText(k)+" "+(scores.TryGetValue(k,out var value)?value:0).ToString("0.###")));
        private static void Clear(Transform parent)
        {if(parent==null)return;foreach(var child in parent.Cast<Transform>().ToArray()){child.gameObject.SetActive(false);if(Application.isPlaying)Destroy(child.gameObject);else DestroyImmediate(child.gameObject);}}
        private static RectTransform Area(Transform parent,string name,float x0,float y0,float x1,float y1,float inset=0)
        {
            var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();r.SetParent(parent,false);r.anchorMin=new Vector2(x0,y0);r.anchorMax=new Vector2(x1,y1);r.offsetMin=Vector2.one*inset;r.offsetMax=-Vector2.one*inset;return r;
        }
        private static Image Background(RectTransform r,Color color){var image=r.gameObject.AddComponent<Image>();image.color=color;return image;}
        private Text Label(RectTransform r,string value,int size,Color? color=null)
        {var t=r.gameObject.AddComponent<Text>();t.font=font;t.text=value;t.fontSize=size;t.color=color??new Color32(23,32,33,255);t.raycastTarget=false;t.horizontalOverflow=HorizontalWrapMode.Wrap;t.verticalOverflow=VerticalWrapMode.Overflow;return t;}
        private Button MakeButton(RectTransform r,string title,Action click)
        {Background(r,new Color32(223,230,226,255));var b=r.gameObject.AddComponent<Button>();Label(Area(r,"Label",0,0,1,1,4),title,18).alignment=TextAnchor.MiddleCenter;b.onClick.AddListener(()=>click());return b;}
        private InputField MakeInput(RectTransform r,string placeholder)
        {Background(r,Color.white);var input=r.gameObject.AddComponent<InputField>();input.textComponent=Label(Area(r,"Value",0,0,1,1,6),"",18);input.placeholder=Label(Area(r,"Placeholder",0,0,1,1,6),placeholder,16,new Color(.35f,.4f,.4f));return input;}
        private Dropdown MakeDropdown(RectTransform r,IEnumerable<string> options)
        {
            Background(r,new Color32(223,230,226,255));var dropdown=r.gameObject.AddComponent<Dropdown>();dropdown.captionText=Label(Area(r,"Caption",0,0,1,1,5),"",16);
            var template=Area(r,"Template",0,0,1,0);template.pivot=new Vector2(.5f,1);template.sizeDelta=new Vector2(0,200);Background(template,Color.white);
            var viewport=Area(template,"Viewport",0,0,1,1,3);viewport.gameObject.AddComponent<RectMask2D>();
            var content=Area(viewport,"Content",0,1,1,1);content.pivot=new Vector2(.5f,1);content.sizeDelta=new Vector2(0,42);
            var item=Area(content,"Item",0,0,1,1);Background(item,new Color32(230,236,231,255));var toggle=item.gameObject.AddComponent<Toggle>();toggle.targetGraphic=item.GetComponent<Image>();
            dropdown.itemText=Label(Area(item,"Label",0,0,1,1,6),"",16);
            var scroll=template.gameObject.AddComponent<ScrollRect>();scroll.viewport=viewport;scroll.content=content;scroll.horizontal=false;
            dropdown.template=template;dropdown.AddOptions(options.ToList());template.gameObject.SetActive(false);return dropdown;
        }
        private static RectTransform MakeList(RectTransform viewport)
        {
            Background(viewport,new Color32(249,249,245,255));viewport.gameObject.AddComponent<RectMask2D>();var content=Area(viewport,"Content",0,1,1,1);content.pivot=new Vector2(.5f,1);content.sizeDelta=Vector2.zero;
            var layout=content.gameObject.AddComponent<VerticalLayoutGroup>();layout.padding=new RectOffset(8,8,8,8);layout.spacing=8;layout.childControlHeight=true;layout.childControlWidth=true;layout.childForceExpandHeight=false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit=ContentSizeFitter.FitMode.PreferredSize;var scroll=viewport.gameObject.AddComponent<ScrollRect>();scroll.horizontal=false;scroll.viewport=viewport;scroll.content=content;return content;
        }
        private void ListText(RectTransform parent,string text,float height){var label=Label(Area(parent,"Empty",0,0,1,1),text,26);label.gameObject.AddComponent<LayoutElement>().preferredHeight=height;}
        private static Slider MakeSlider(RectTransform r)
        {Background(r,new Color32(182,195,190,255));var s=r.gameObject.AddComponent<Slider>();var handle=Area(r,"Handle",0,0,0,1);handle.sizeDelta=new Vector2(20,0);Background(handle,new Color32(38,78,73,255));s.handleRect=handle;s.targetGraphic=handle.GetComponent<Image>();return s;}
#if UNITY_EDITOR
        public void PrepareSceneLayout(Font sourceFont)
        {
            if(font==null)font=sourceFont;EnsurePage();
            if(inventoryTemplate!=null)return;
            var templates=Area(transform,"CorpusTemplates",0,0,1,1);
            var card=Area(templates,"CorpusCardTemplate",0,0,1,1);inventoryTemplate=card;
            Background(card,new Color32(242,244,240,255));card.gameObject.AddComponent<LayoutElement>().minHeight=146;
            var layout=card.gameObject.AddComponent<VerticalLayoutGroup>();layout.padding=new RectOffset(8,8,8,8);layout.spacing=6;
            layout.childControlWidth=layout.childControlHeight=true;layout.childForceExpandHeight=false;
            Label(Area(card,"Title",0,0,1,1),"语料来源 · 可用点数",18).gameObject.AddComponent<LayoutElement>().minHeight=42;
            Label(Area(card,"Scores",0,0,1,1),"写作 / 代码 / 推理 / 检索",16).gameObject.AddComponent<LayoutElement>().minHeight=40;
            var button=MakeButton(Area(card,"Action",0,0,1,1),"点击添加 / 按住拖入右侧",()=>{});
            button.gameObject.AddComponent<LayoutElement>().preferredHeight=36;
            button.gameObject.AddComponent<ConversationCorpusDrag>();card.gameObject.AddComponent<ConversationCorpusDrag>();
            card.gameObject.AddComponent<CorpusCardDetails>();button.gameObject.AddComponent<CorpusCardDetails>();
            templates.gameObject.SetActive(false);
        }
        public void PrepareCorpusInvestment(){ }
#endif
    }
}
