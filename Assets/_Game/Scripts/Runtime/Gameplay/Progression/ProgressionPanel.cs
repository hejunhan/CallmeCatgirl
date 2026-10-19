using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CallmeCatgirl.Gameplay.Progression
{
    /// <summary>The host owns opening, closing and pause. Locked graph nodes remain inspectable.</summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class ProgressionPanel : MonoBehaviour
    {
        [SerializeField] private ProgressionController controller;
        [SerializeField] private Font labelFont;
        [SerializeField,HideInInspector] private int authoredLayoutVersion;
        [SerializeField] private Color ownedColor=new Color(.12f,.35f,.30f), excludedColor=new Color(.25f,.15f,.18f), availableColor=new Color(.14f,.22f,.28f), lockedColor=new Color(.13f,.14f,.16f);
        private readonly Dictionary<string, Button> cards = new Dictionary<string, Button>();
        private readonly Dictionary<string, Text> statuses = new Dictionary<string, Text>();
        [SerializeField] private Font font;
        [SerializeField] private Text heading, footer, detailText, detailStatus;
        [SerializeField] private Button firstPage, closeButton, secondPage, buy, detailBack;
        [SerializeField] private ScrollRect graphScroll;
        [SerializeField] private RectTransform content, nodeTemplate, connectionTemplate;
        private ProgressionTree displayed;
        private ProgressionNode[] displayedNodes = Array.Empty<ProgressionNode>();
        [SerializeField] private GameObject detail;
        private string selected;
        private int page;
        public event Action Closed;
        public int CurrentPage => page;

        public void Bind(ProgressionController policyController, Font preferredFont = null)
        {
            controller = policyController;
            if (labelFont == null && preferredFont != null) labelFont = preferredFont;
            displayed = null;
        }
        private void Start()
        {
            if (controller == null) controller = FindAnyObjectByType<ProgressionController>();
            if(heading==null)throw new InvalidOperationException("Save the authored policy layout before Play.");
            PrepareSceneLayout(labelFont);
            Wire(closeButton,Close);Wire(firstPage,()=>ShowPage(0));Wire(secondPage,()=>ShowPage(1));
            Wire(detailBack,()=>CloseDetail());Wire(buy,()=>{if(selected!=null)controller.TryPurchase(selected);});
        }
        private static void Wire(Button button,Action action){button.onClick.RemoveAllListeners();button.onClick.AddListener(()=>action());}
        public void PrepareSceneLayout(Font preferredFont)
        {
            if(heading!=null)return;
            if(labelFont==null)labelFont=preferredFont;
            font = labelFont != null ? labelFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            (GetComponent<Image>() ?? gameObject.AddComponent<Image>()).color = new Color(.055f, .07f, .085f, .99f);
            heading = Label("政策", transform, 22);
            Place(heading.rectTransform, new Vector2(0,1), Vector2.one, new Vector2(20,-44), new Vector2(-170,-4));
            var close = Button("关闭 ×", transform, Close);closeButton=close;
            Place((RectTransform)close.transform, Vector2.one, Vector2.one, new Vector2(-150,-42), new Vector2(-16,-8));
            var first = Button("第一页 · 路线与材料", transform, () => ShowPage(0));firstPage=first;
            Place((RectTransform)first.transform, new Vector2(0,1), new Vector2(0,1), new Vector2(20,-82), new Vector2(270,-46));
            secondPage = Button("第二页 · 回答以外", transform, () => ShowPage(1));
            Place((RectTransform)secondPage.transform, new Vector2(0,1), new Vector2(0,1), new Vector2(280,-82), new Vector2(530,-46));
            graphScroll = ScrollArea("PolicyGraph", transform, new Vector2(16,50), new Vector2(-16,-92), true);
            content = graphScroll.content;
            footer = Label("点击查看完整说明；拖动空白、滚轮或滚动条查看连线。", transform, 16);
            Place(footer.rectTransform, Vector2.zero, new Vector2(1,0), new Vector2(20,5), new Vector2(-20,44));
            BuildDetail();BuildTemplates();
        }
        private void BuildTemplates()
        {
            var templates=new GameObject("PolicyTemplates",typeof(RectTransform));templates.transform.SetParent(transform,false);
            var card=Button(string.Empty,templates.transform,()=>{});card.name="PolicyNodeTemplate";nodeTemplate=(RectTransform)card.transform;
            DestroyImmediate(card.GetComponentInChildren<Text>().gameObject);
            nodeTemplate.anchorMin=nodeTemplate.anchorMax=new Vector2(0,1);nodeTemplate.pivot=new Vector2(0,1);nodeTemplate.sizeDelta=new Vector2(304,196);
            var title=Label("政策标题",card.transform,20);title.name="Title";
            Place(title.rectTransform,Vector2.zero,Vector2.one,new Vector2(12,150),new Vector2(-12,-8));
            var mechanics=Label("配置资源中的数值说明",card.transform,16);mechanics.name="Mechanics";mechanics.alignment=TextAnchor.UpperLeft;
            Place(mechanics.rectTransform,Vector2.zero,Vector2.one,new Vector2(12,48),new Vector2(-12,-50));
            var status=Label("价格 · 当前可用状态",card.transform,16);status.name="Status";
            Place(status.rectTransform,Vector2.zero,new Vector2(1,0),new Vector2(12,7),new Vector2(-12,46));
            connectionTemplate=new GameObject("ConnectionTemplate",typeof(RectTransform),typeof(Image)).GetComponent<RectTransform>();
            connectionTemplate.SetParent(templates.transform,false);connectionTemplate.sizeDelta=new Vector2(100,3);
            var image=connectionTemplate.GetComponent<Image>();image.color=new Color(.38f,.50f,.53f);image.raycastTarget=false;
            templates.SetActive(false);
        }
        public void ShowPage(int index)
        {
            if ((index != 0 && index != 1) || (index == 1 && !SecondPageAvailable())) return;
            page = index; displayed = null; CloseDetail();
        }
        private bool SecondPageAvailable() => controller?.Tree != null
            && displayedNodes.Any(n => n.Route != PolicyRoute.None && controller.Tree.IsUnlocked(n.Id));
        private void Update()
        {
            if (controller == null || heading == null) return;
            controller.EnsureBound(); var tree = controller.Tree; if (tree == null) return;
            if (!ReferenceEquals(tree, displayed)) Rebuild(tree);
            heading.text = "政策 · 金币 " + controller.Funds + "    声望 " + tree.Reputation + "（" + Band(tree.ReputationBand) + "）";
            secondPage.interactable = SecondPageAvailable();
            footer.text = "第 " + (page + 1) + " 页 · 开放 / 封闭互斥 · 全部仅收金币 · 节点可点击查看完整说明";
            foreach (var n in displayedNodes.Where(n => n.Page == page)) {
                var state = tree.Availability(n.Id); bool owned = tree.IsUnlocked(n.Id);
                cards[n.Id].GetComponent<Image>().color = owned ? ownedColor
                    : state == PurchaseResult.ExcludedByChoice ? excludedColor
                    : state == PurchaseResult.Success ? availableColor : lockedColor;
                statuses[n.Id].text = owned ? "✓ 已购买" : n.Cost + " 金币 · " + ProgressionController.PurchaseResultText(state);
            }
            if (detail.activeSelf) UpdateDetail();
        }
        private void Rebuild(ProgressionTree tree)
        {
            displayed=tree;RebuildNodes(tree.Nodes.ToArray());
        }
#if UNITY_EDITOR
        public void PreviewConfiguration()
        {
            if(controller==null)return;
            RebuildNodes(controller.Nodes);heading.text="政策 · 配置预览（无运行中存档）";
            foreach(var node in displayedNodes.Where(n=>n.Page==page))statuses[node.Id].text=node.Cost+" 金币 · 运行时判定条件";
        }
#endif
        private void RebuildNodes(ProgressionNode[] definitions)
        {
            foreach (var child in content.Cast<Transform>().ToArray())
            {child.gameObject.SetActive(false);if(Application.isPlaying)Destroy(child.gameObject);else DestroyImmediate(child.gameObject);}
            cards.Clear(); statuses.Clear();displayedNodes=definitions;
            var nodes = displayedNodes.Where(n => n.Page == page).ToArray();
            int columns = nodes.Length == 0 ? 1 : nodes.Max(n => n.Column) + 1;
            int rows = nodes.Length == 0 ? 1 : nodes.Max(n => n.Row) + 1;
            content.sizeDelta = new Vector2(Mathf.Max(graphScroll.viewport.rect.width, columns * 350 + 32), rows * 218 + 32);
            var all = displayedNodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
            foreach (var node in nodes)
                foreach (var id in node.Prerequisites.Concat(node.AnyPrerequisites)) {
                    var source = all[id]; if (source.Page != page) continue;
                    var a = Position(source) + new Vector2(304,-96); var b = Position(node) + new Vector2(0,-96);
                    float middle = (a.x + b.x) / 2;
                    Line(a, new Vector2(middle,a.y)); Line(new Vector2(middle,a.y), new Vector2(middle,b.y)); Line(new Vector2(middle,b.y),b);
                }
            foreach (var n in nodes) {
                string id=n.Id;var r=Instantiate(nodeTemplate,content);r.name="Policy-"+id;r.gameObject.SetActive(true);
                var card=r.GetComponent<Button>();Wire(card,()=>OpenDetail(id));r.anchoredPosition=Position(n);
                r.Find("Title").GetComponent<Text>().text=n.Title;
                r.Find("Mechanics").GetComponent<Text>().text=n.MechanicalText;
                var status=r.Find("Status").GetComponent<Text>();
                cards.Add(id,card); statuses.Add(id,status);
            }
            graphScroll.horizontalNormalizedPosition = 0; graphScroll.verticalNormalizedPosition = 1;
        }
        private static Vector2 Position(ProgressionNode n) => new Vector2(16+n.Column*350,-16-n.Row*218);
        private void Line(Vector2 a, Vector2 b)
        {
            var r=Instantiate(connectionTemplate,content);r.name="Connection";r.gameObject.SetActive(true);
            var go=r.gameObject;r.anchorMin = r.anchorMax = new Vector2(0,1); r.anchoredPosition = (a+b)/2;
            r.sizeDelta = new Vector2(Vector2.Distance(a,b),connectionTemplate.sizeDelta.y); r.localEulerAngles = new Vector3(0,0,Mathf.Atan2(b.y-a.y,b.x-a.x)*Mathf.Rad2Deg);
        }
        private void BuildDetail()
        {
            detail = new GameObject("PolicyDetail",typeof(RectTransform),typeof(Image)); detail.transform.SetParent(transform,false);
            Place((RectTransform)detail.transform,Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero);
            detail.GetComponent<Image>().color = new Color(.03f,.04f,.05f,.99f);
            var sr = ScrollArea("Description",detail.transform,new Vector2(28,120),new Vector2(-28,-20),false);
            sr.content.anchorMin = new Vector2(0,1); sr.content.anchorMax = Vector2.one; sr.content.pivot = new Vector2(.5f,1);
            sr.content.offsetMin = sr.content.offsetMax = Vector2.zero;
            detailText = Label(string.Empty,sr.content,20); detailText.alignment = TextAnchor.UpperLeft;
            detailText.verticalOverflow = VerticalWrapMode.Overflow; detailText.supportRichText = false;
            Place(detailText.rectTransform,new Vector2(0,1),Vector2.one,new Vector2(14,0),new Vector2(-14,0));
            var layout = sr.content.gameObject.AddComponent<VerticalLayoutGroup>(); layout.padding = new RectOffset(14,14,14,14);
            layout.childControlHeight = true; layout.childControlWidth = true; layout.childForceExpandHeight = false;
            sr.content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            detailStatus = Label(string.Empty,detail.transform,18);
            Place(detailStatus.rectTransform,Vector2.zero,new Vector2(1,0),new Vector2(28,64),new Vector2(-28,112));
            var back = Button("返回政策树",detail.transform,() => CloseDetail()); detailBack = back;
            Place((RectTransform)back.transform,Vector2.zero,new Vector2(.45f,0),new Vector2(28,14),new Vector2(-8,58));
            buy = Button("购买",detail.transform,() => { if (selected != null) controller.TryPurchase(selected); });
            Place((RectTransform)buy.transform,new Vector2(.55f,0),new Vector2(1,0),new Vector2(8,14),new Vector2(-28,58));
            detail.SetActive(false);
        }
        private void OpenDetail(string id) {
            selected = id; detail.SetActive(true); UpdateDetail();
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(detailBack.gameObject);
        }
        public bool CloseDetail() {
            if (detail == null || !detail.activeSelf) return false;
            detail.SetActive(false);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(
                selected != null && cards.TryGetValue(selected, out var card) ? card.gameObject : null);
            selected = null; return true;
        }
        public void Close() { if (CloseDetail()) return; gameObject.SetActive(false); Closed?.Invoke(); }
        private void UpdateDetail()
        {
            if (controller?.Tree == null || selected == null) return;
            var tree = controller.Tree; var n = displayedNodes.First(x => x.Id == selected); var state = tree.Availability(selected);
            var titles = displayedNodes.ToDictionary(x => x.Id,x => x.Title);
            string prerequisites = n.Prerequisites.Length == 0 ? "无" : string.Join("、",n.Prerequisites.Select(x => titles[x]));
            if (n.AnyPrerequisites.Length > 0) prerequisites += "；任选其一：" + string.Join(" / ",n.AnyPrerequisites.Select(x => titles[x]));
            detailText.text = n.Title + "\n\n数值与价格\n" + n.Cost + " 金币\n" + n.MechanicalText
                + "\n前置：" + prerequisites + (n.ReputationDelta == 0 ? "" : "\n声望 " + (n.ReputationDelta > 0 ? "+" : "") + n.ReputationDelta)
                + "\n\n故事\n" + n.FlavorText;
            detailStatus.text = ProgressionController.PurchaseResultText(state)
                + (state == PurchaseResult.Success && controller.Funds < n.Cost ? " · 金币不足" : "");
            buy.interactable = controller.CanPurchase && state == PurchaseResult.Success && controller.Funds >= n.Cost;
            buy.GetComponentInChildren<Text>().text = (n.Kind == PolicyNodeKind.Terminal ? "确认进入终局 · " : "购买 · ") + n.Cost + " 金币";
        }
        private ScrollRect ScrollArea(string name,Transform parent,Vector2 minimum,Vector2 maximum,bool horizontal)
        {
            var go = new GameObject(name,typeof(RectTransform),typeof(ScrollRect)); go.transform.SetParent(parent,false);
            Place((RectTransform)go.transform,Vector2.zero,Vector2.one,minimum,maximum);
            var view = new GameObject("Viewport",typeof(RectTransform),typeof(Image),typeof(RectMask2D)); view.transform.SetParent(go.transform,false);
            Place((RectTransform)view.transform,Vector2.zero,Vector2.one,Vector2.zero,new Vector2(-14,horizontal?-14:0));
            view.GetComponent<Image>().color = new Color(.035f,.045f,.055f);
            var body = new GameObject("Content",typeof(RectTransform)); body.transform.SetParent(view.transform,false);
            var r = (RectTransform)body.transform; r.anchorMin = r.anchorMax = new Vector2(0,1); r.pivot = new Vector2(0,1);
            var sr = go.GetComponent<ScrollRect>(); sr.viewport = (RectTransform)view.transform; sr.content = r;
            sr.horizontal = horizontal; sr.vertical = true; sr.movementType = ScrollRect.MovementType.Clamped; sr.scrollSensitivity = 45;
            AddBar(sr,true); if (horizontal) AddBar(sr,false); return sr;
        }
        private void AddBar(ScrollRect sr,bool vertical)
        {
            var go = new GameObject(vertical?"VerticalScrollbar":"HorizontalScrollbar",typeof(RectTransform),typeof(Image),typeof(Scrollbar)); go.transform.SetParent(sr.transform,false);
            if (vertical) Place((RectTransform)go.transform,new Vector2(1,0),Vector2.one,new Vector2(-12,14),Vector2.zero);
            else Place((RectTransform)go.transform,Vector2.zero,new Vector2(1,0),Vector2.zero,new Vector2(-14,12));
            go.GetComponent<Image>().color = new Color(.13f,.16f,.19f);
            var handle = new GameObject("Handle",typeof(RectTransform),typeof(Image)); handle.transform.SetParent(go.transform,false);
            Place((RectTransform)handle.transform,Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero); handle.GetComponent<Image>().color = new Color(.50f,.65f,.68f);
            var bar = go.GetComponent<Scrollbar>(); bar.handleRect = (RectTransform)handle.transform; bar.targetGraphic = handle.GetComponent<Image>();
            bar.direction = vertical?Scrollbar.Direction.BottomToTop:Scrollbar.Direction.LeftToRight;
            if (vertical) sr.verticalScrollbar = bar; else sr.horizontalScrollbar = bar;
        }
        private Text Label(string value,Transform parent,int size)
        {
            var go = new GameObject("Label",typeof(RectTransform),typeof(Text)); go.transform.SetParent(parent,false);
            var text = go.GetComponent<Text>(); text.font = font; text.fontSize = size; text.text = value; text.color = Color.white;
            text.alignment = TextAnchor.MiddleLeft; text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate; text.raycastTarget = false; return text;
        }
        private Button Button(string caption,Transform parent,Action action)
        {
            var go = new GameObject("Button",typeof(RectTransform),typeof(Image),typeof(Button)); go.transform.SetParent(parent,false);
            go.GetComponent<Image>().color = new Color(.16f,.25f,.29f); var button = go.GetComponent<Button>(); button.onClick.AddListener(() => action());
            var label = Label(caption,go.transform,17); label.alignment = TextAnchor.MiddleCenter;
            Place(label.rectTransform,Vector2.zero,Vector2.one,new Vector2(8,2),new Vector2(-8,-2)); return button;
        }
        private static string Band(PolicyEndingBand band) => band == PolicyEndingBand.Bad ? "低声望" : band == PolicyEndingBand.Good ? "高声望" : "中等声望";
        private static void Place(RectTransform rect,Vector2 min,Vector2 max,Vector2 low,Vector2 high)
        { rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = low; rect.offsetMax = high; }
    }
}
