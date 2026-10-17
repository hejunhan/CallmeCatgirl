using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using CallmeCatgirl.Gameplay.Training;
using CallmeCatgirl.Presentation;

namespace CallmeCatgirl.UI
{
    /// <summary>Dedicated model catalogue; all actions retain the full model key.</summary>
    public sealed class ModelGalleryView : MonoBehaviour
    {
        private TrainingController controller;
        private TrainingPanel panel;
        [SerializeField] private GameObject root;
        [SerializeField] private RectTransform body;
        [SerializeField] private ModelPageReveal reveal;
        [SerializeField] private Font font;
        private const float DesignScale=1000f/720f;
        private const float DesignWidthScale=1600f/1280f;
        [SerializeField] private GameObject cataloguePage;
        [SerializeField] private TrainingWorkspaceView trainingView;
        [SerializeField] private Text titleLabel,messageLabel;
        [SerializeField] private Button pageButton,closeButton,filterButton,currentButton;
        [SerializeField] private InputField query;
        [SerializeField] private RectTransform modelTemplate,outlineCellTemplate;
        private bool trainingPage;
        public bool IsVisible { get; private set; }
        public void Open(TrainingController source, TrainingPanel owner)
        {
            controller = source; panel = owner;
            if (root == null) throw new InvalidOperationException("Model gallery must be saved in the scene first.");
            root.SetActive(true); IsVisible = true; Redraw(); reveal.Open();
            controller.Changed -= Redraw; controller.Changed += Redraw;
        }
        public void Close()
        {
            IsVisible = false;
            if (controller != null) controller.Changed -= Redraw;
            if (root != null) root.SetActive(false);
        }
        private void OnDestroy() { Close(); }
        private RectTransform Rect(string name, Transform parent, float x, float y, float w, float h)
        {
            var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            r.SetParent(parent, false); r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, 1);
            r.anchoredPosition = new Vector2(x*DesignWidthScale, -y*DesignScale); r.sizeDelta = new Vector2(w*DesignWidthScale, h*DesignScale); return r;
        }
        private Text Label(Transform parent, string text, float x, float y, float w, float h, int size, Color? color = null)
        {
            var t = Rect("Label", parent, x, y, w, Mathf.Max(h,30)).gameObject.AddComponent<Text>();
            t.font = font; t.text = text; t.fontSize = Mathf.RoundToInt(Mathf.Max(size,22)*DesignScale); t.color = color ?? new Color32(22,22,22,255);
            t.raycastTarget = false; t.horizontalOverflow = HorizontalWrapMode.Wrap; return t;
        }
        private Button Button(Transform parent, string title, float x, float y, float w, Action click)
        {
            var r = Rect(title, parent, x, y, w, 38); var image = r.gameObject.AddComponent<Image>();
            image.color = new Color(.88f,.9f,.91f); var b = r.gameObject.AddComponent<Button>();
            b.onClick.AddListener(() => click()); Label(r,title,10,8,w-16,26,16); return b;
        }
        private void Build()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            // Reuse the project's Chinese font when available.
            foreach (var text in FindObjectsByType<Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (text.font != null && text.font.name != "LegacyRuntime") { font = text.font; break; }
            root = new GameObject("ModelGallery", typeof(RectTransform));
            var layers=FindFirstObjectByType<UIWindowLayers>();
            if(layers==null)throw new InvalidOperationException("Model gallery requires the shared UI window layers.");
            layers.Register(root,(RectTransform)root.transform,UIWindowKind.ModalPage);
            var layout=Rect("GalleryLayout",root.transform,0,0,1280,720);
            layout.anchorMin=layout.anchorMax=layout.pivot=new Vector2(.5f,.5f);layout.anchoredPosition=Vector2.zero;
            var area = Rect("Viewport",layout,24,24,1232,672);
            var source = Rect("RevealOrigin",layout,36,580,280,52);
            var frame = Rect("WhiteFrame",layout,24,24,1232,672);
            frame.gameObject.AddComponent<Image>(); frame.gameObject.AddComponent<RectMask2D>();
            layers.Register(root,frame,UIWindowKind.ModalPage);
            body = Rect("Content",frame,0,0,1232,672); var group = body.gameObject.AddComponent<CanvasGroup>();
            var driver = new GameObject("RevealDriver"); driver.SetActive(false); driver.transform.SetParent(layout,false);
            reveal = driver.AddComponent<ModelPageReveal>(); reveal.Configure(source,area,frame,group); driver.SetActive(true);
        }
        private string search="",architectureFilter;
        private bool onlyCurrent;
        [SerializeField] private RectTransform cards;
        private InputField Input(Transform parent,string name,string value,float x,float y,float width,int limit)
        {
            var r=Rect(name,parent,x,y,width,36);r.gameObject.AddComponent<Image>().color=Color.white;
            var border=r.gameObject.AddComponent<Outline>();border.effectColor=new Color(.65f,.65f,.65f);border.effectDistance=Vector2.one;
            var input=r.gameObject.AddComponent<InputField>();input.textComponent=Label(r,"",10,6,width-20,28,16);input.characterLimit=limit;input.text=value;return input;
        }
        private void Redraw()
        {
            if(!IsVisible || controller==null || !controller.IsBound)return;
            if(architectureFilter!=null&&!controller.Session.Architectures.Any(a=>a.Id==architectureFilter))architectureFilter=null;
            titleLabel.text=trainingPage?"服务器 / 语料训练":"模型广场";messageLabel.text=controller.Message;
            pageButton.GetComponentInChildren<Text>(true).text=trainingPage?"模型广场":"语料与训练";
            pageButton.onClick.RemoveAllListeners();pageButton.onClick.AddListener(()=>{trainingPage=!trainingPage;Redraw();});
            closeButton.onClick.RemoveAllListeners();closeButton.onClick.AddListener(()=>panel.Close());
            cataloguePage.SetActive(!trainingPage);trainingView.gameObject.SetActive(trainingPage);
            if(trainingPage){trainingView.Bind(controller);return;}
            query.SetTextWithoutNotify(search);query.onValueChanged.RemoveAllListeners();query.onValueChanged.AddListener(value=>{search=value;RebuildCards();});
            filterButton.GetComponentInChildren<Text>(true).text="架构："+FilterTitle();
            filterButton.onClick.RemoveAllListeners();filterButton.onClick.AddListener(()=>{
                var ids=controller.Session.Architectures.Select(a=>a.Id).ToArray();int index=Array.IndexOf(ids,architectureFilter);
                architectureFilter=index+1<ids.Length?ids[index+1]:null;filterButton.GetComponentInChildren<Text>(true).text="架构："+FilterTitle();RebuildCards();
            });
            currentButton.GetComponentInChildren<Text>(true).text=onlyCurrent?"仅当前模型 ✓":"全部模型";
            currentButton.onClick.RemoveAllListeners();currentButton.onClick.AddListener(()=>{onlyCurrent=!onlyCurrent;currentButton.GetComponentInChildren<Text>(true).text=onlyCurrent?"仅当前模型 ✓":"全部模型";RebuildCards();});
            RebuildCards();
        }
        private string FilterTitle()=>architectureFilter==null?"全部":controller.Session.Architectures.First(a=>a.Id==architectureFilter).Title;
        private void RebuildCards()
        {
            if(cards==null || trainingPage)return;
            foreach(var child in cards.Cast<Transform>().ToArray())
            {child.gameObject.SetActive(false);if(Application.isPlaying)Destroy(child.gameObject);else DestroyImmediate(child.gameObject);}
            var models=controller.Session.Models.Where(m=>(!onlyCurrent || m.Id==controller.Session.ActiveModelId)
                && (architectureFilter==null || m.ArchitectureId==architectureFilter)
                && (string.IsNullOrWhiteSpace(search) || (m.Name+" "+m.Id+" "+controller.Session.Architectures.First(a=>a.Id==m.ArchitectureId).Title).IndexOf(search,StringComparison.OrdinalIgnoreCase)>=0))
                .OrderByDescending(m=>m.Id==controller.Session.ActiveModelId);
            foreach(var model in models)
            {
                var card=Instantiate(modelTemplate,cards);card.name="ModelCard";card.gameObject.SetActive(true);BindCard(card,model);
            }
        }
        private void BindCard(RectTransform card,CatalogModel model)
        {
            string id=model.Id;bool current=id==controller.Session.ActiveModelId;var arch=controller.Session.Architectures.First(a=>a.Id==model.ArchitectureId);
            card.GetComponent<Outline>().effectColor=current?Color.black:new Color32(111,111,111,255);
            card.Find("ArchitectureIcon/Label").GetComponent<Text>().text=model.ArchitectureId=="baseline"?"T":model.ArchitectureId=="alternate"?"M":"S";
            SetCardText(card,"ModelName",model.Name);SetCardText(card,"ArchitectureTitle",arch.Title);
            SetCardText(card,"Throughput",controller.Session.PreviewTokensPerSecond(id).ToString("0.##")+" token/s");
            foreach(var ability in Enum.GetValues(typeof(CallmeCatgirl.Gameplay.TaskGrid.AbilityKind)).Cast<CallmeCatgirl.Gameplay.TaskGrid.AbilityKind>())
            {model.Definition.Points.TryGetValue(ability,out double value);SetCardText(card,"Ability_"+ability,CallmeCatgirl.Gameplay.CoreGameplayController.AbilityText(ability)+"  "+value.ToString("0.###"));}
            var owner=controller.GetComponent<CallmeCatgirl.Gameplay.CoreGameplayController>();var cells=owner.ModelOutlineFor(id);var outline=(RectTransform)card.Find("Outline");
            float cellSize=Mathf.Min(outline.rect.width/controller.Simulation.Width,outline.rect.height/controller.Simulation.Height);
            foreach(var cell in cells)
            {
                var square=Instantiate(outlineCellTemplate,outline);square.gameObject.SetActive(true);square.name="OutlineCell";
                square.anchoredPosition=new Vector2(cell.X*cellSize,-cell.Y*cellSize);square.sizeDelta=Vector2.one*Mathf.Max(1,cellSize-1);
            }
            string shape=arch.Outline==OutlineTransform.Keep?"标准":arch.Outline==OutlineTransform.Transpose?"转置":"镜像";
            SetCardText(card,"OutlineSummary","模型轮廓："+shape+" · "+cells.Count+"格");SetCardText(card,"Interval","能力产出间隔："+model.Definition.Interval.ToString("0.###")+"s");
            SetCardText(card,"ModelState",(current?"● 当前使用":"○ 可切换")+"   训练语料 "+model.CorpusIds.Count+"条");SetCardText(card,"ModelUuid",id);
            var rename=card.Find("Rename").GetComponent<InputField>();rename.SetTextWithoutNotify(model.Name);
            var renameButton=card.Find("改名").GetComponent<Button>();renameButton.onClick.RemoveAllListeners();renameButton.onClick.AddListener(()=>owner.ExecuteCommand(new CallmeCatgirl.Gameplay.GameCommand{kind="renameModel",id=id,displayName=rename.text}));
            var select=card.Find("SelectModel").GetComponent<Button>();select.GetComponentInChildren<Text>(true).text=current?"当前使用":"切换到此模型";select.interactable=!current&&controller.CanManage;
            select.onClick.RemoveAllListeners();select.onClick.AddListener(()=>owner.ExecuteCommand(new CallmeCatgirl.Gameplay.GameCommand{kind="selectModel",id=id}));
        }
        private static void SetCardText(RectTransform card,string name,string value)=>card.Find(name).GetComponent<Text>().text=value;
#if UNITY_EDITOR
        public void PrepareSceneLayout(TrainingController source,TrainingPanel owner)
        {
            if(root!=null)return;controller=source;panel=owner;Build();
            titleLabel=Label(body,"模型广场",28,20,800,48,32);titleLabel.name="PageTitle";
            pageButton=Button(body,"语料与训练",900,24,160,()=>{});closeButton=Button(body,"返回",1080,24,110,()=>{});
            messageLabel=Label(body,"",28,78,1160,28,15,new Color32(82,82,82,255));messageLabel.name="PageStatus";
            cataloguePage=Rect("CataloguePage",body,0,0,1232,672).gameObject;var page=(RectTransform)cataloguePage.transform;page.anchorMin=Vector2.zero;page.anchorMax=Vector2.one;page.offsetMin=page.offsetMax=Vector2.zero;
            query=Input(page,"ModelSearch","",28,120,610,80);query.placeholder=Label(query.transform,"搜索模型名称、ID或架构",10,6,590,28,16,new Color(.55f,.55f,.55f));
            filterButton=Button(page,"架构：全部",662,120,290,()=>{});currentButton=Button(page,"全部模型",976,120,216,()=>{});
            var viewport=Rect("Catalogue",page,28,178,1176,450);viewport.gameObject.AddComponent<RectMask2D>();
            var scroll=viewport.gameObject.AddComponent<ScrollRect>();scroll.horizontal=false;scroll.scrollSensitivity=35;
            cards=Rect("Cards",viewport,0,0,1176,450);scroll.viewport=viewport;scroll.content=cards;
            MarketCard(source.Session.Models.First(),0,0,576,440);modelTemplate=(RectTransform)cards.GetChild(0);modelTemplate.name="ModelCardTemplate";
            var labels=modelTemplate.Cast<Transform>().Select(t=>t.GetComponent<Text>()).Where(t=>t!=null).ToArray();
            string[] names={"ModelName","ArchitectureTitle","Throughput","Ability_Writing","Ability_Code","Ability_Reasoning","Ability_Retrieval","OutlineSummary","Interval","ModelState","ModelUuid"};
            for(int i=0;i<names.Length;i++)labels[i].name=names[i];
            modelTemplate.GetComponentsInChildren<Button>(true).Last().name="SelectModel";
            var templates=Rect("ModelTemplates",body,0,0,576,440);templates.gameObject.SetActive(false);modelTemplate.SetParent(templates,false);
            var oldCells=modelTemplate.Cast<Transform>().Where(t=>t.name=="OutlineCell").ToArray();
            outlineCellTemplate=Instantiate((RectTransform)oldCells[0],templates);outlineCellTemplate.name="OutlineCellTemplate";outlineCellTemplate.anchoredPosition=Vector2.zero;
            foreach(var cell in oldCells)DestroyImmediate(cell.gameObject);Rect("Outline",modelTemplate,20,232,150,72);
            var grid=cards.gameObject.AddComponent<GridLayoutGroup>();grid.cellSize=modelTemplate.sizeDelta;grid.spacing=new Vector2(24*DesignWidthScale,20*DesignScale);grid.constraint=GridLayoutGroup.Constraint.FixedColumnCount;grid.constraintCount=2;
            cards.gameObject.AddComponent<ContentSizeFitter>().verticalFit=ContentSizeFitter.FitMode.PreferredSize;
            var trainingRoot=Rect("TrainingWorkspace",body,0,0,1232,672);trainingRoot.anchorMin=Vector2.zero;trainingRoot.anchorMax=Vector2.one;trainingRoot.offsetMin=trainingRoot.offsetMax=Vector2.zero;
            trainingView=trainingRoot.gameObject.AddComponent<TrainingWorkspaceView>();trainingView.PrepareSceneLayout(font);trainingRoot.gameObject.SetActive(false);
            IsVisible=true;Redraw();IsVisible=false;root.SetActive(false);
        }
        private void MarketCard(CatalogModel model,float x,float y,float width,float height)
        {
            string id=model.Id;bool current=id==controller.Session.ActiveModelId;
            var card=Rect("ModelCard",cards,x,y,width,height);card.gameObject.AddComponent<Image>().color=new Color(.965f,.965f,.965f);
            var border=card.gameObject.AddComponent<Outline>();border.effectColor=current?Color.black:new Color32(111,111,111,255);border.effectDistance=Vector2.one;
            var icon=Rect("ArchitectureIcon",card,20,22,34,34);icon.gameObject.AddComponent<Image>().color=Color.black;
            Label(icon,model.ArchitectureId=="baseline"?"T":model.ArchitectureId=="alternate"?"M":"S",7,4,28,30,22,Color.white);
            Label(card,model.Name,70,16,width-90,94,26).name="ModelName";
            var arch=controller.Session.Architectures.First(a=>a.Id==model.ArchitectureId);
            Label(card,arch.Title,20,122,330,30,17);Label(card,controller.Session.PreviewTokensPerSecond(id).ToString("0.##")+" token/s",362,122,194,30,20);
            var abilities=Enum.GetValues(typeof(CallmeCatgirl.Gameplay.TaskGrid.AbilityKind)).Cast<CallmeCatgirl.Gameplay.TaskGrid.AbilityKind>().ToArray();
            for(int i=0;i<abilities.Length;i++)
            {
                model.Definition.Points.TryGetValue(abilities[i],out double value);
                Label(card,CallmeCatgirl.Gameplay.CoreGameplayController.AbilityText(abilities[i])+"  "+value.ToString("0.###"),20+(i%2)*278,164+(i/2)*28,258,28,19);
            }
            var owner=controller.GetComponent<CallmeCatgirl.Gameplay.CoreGameplayController>();var cells=owner.ModelOutlineFor(id);
            float cellSize=Mathf.Min(150f/controller.Simulation.Width,72f/controller.Simulation.Height);
            foreach(var cell in cells)
            {
                var square=Rect("OutlineCell",card,20+cell.X*cellSize,232+cell.Y*cellSize,Mathf.Max(1,cellSize-1),Mathf.Max(1,cellSize-1));square.gameObject.AddComponent<Image>().color=Color.black;
            }
            string outline=arch.Outline==OutlineTransform.Keep?"标准":arch.Outline==OutlineTransform.Transpose?"转置":"镜像";
            Label(card,"模型轮廓："+outline+" · "+cells.Count+"格",200,232,356,28,16);
            Label(card,"能力产出间隔："+model.Definition.Interval.ToString("0.###")+"s",200,260,356,28,16);
            Label(card,(current?"● 当前使用":"○ 可切换")+"   训练语料 "+model.CorpusIds.Count+"条",200,288,356,26,15);
            var rename=Input(card,"Rename",model.Name,20,322,422,40);
            Button(card,"改名",454,322,102,()=>owner.ExecuteCommand(new CallmeCatgirl.Gameplay.GameCommand{kind="renameModel",id=id,displayName=rename.text}));
            var select=Button(card,current?"当前使用":"切换到此模型",20,370,width-40,()=>owner.ExecuteCommand(new CallmeCatgirl.Gameplay.GameCommand{kind="selectModel",id=id}));select.interactable=!current && controller.CanManage;
            var uuid=Label(card,id,20,414,width-40,22,13,new Color32(82,82,82,255));
            uuid.rectTransform.sizeDelta=new Vector2((width-40)*DesignWidthScale,22*DesignScale);uuid.fontSize=Mathf.RoundToInt(13*DesignScale);uuid.name="ModelUuid";uuid.alignment=TextAnchor.LowerRight;

        }

#endif
    }
}
