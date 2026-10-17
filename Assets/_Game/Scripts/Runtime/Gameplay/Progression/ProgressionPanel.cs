using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CallmeCatgirl.Gameplay.Progression
{
    /// <summary>Optional runtime uGUI adapter. Attach to a Canvas child and assign the controller.</summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class ProgressionPanel : MonoBehaviour
    {
        [SerializeField] private ProgressionController controller;
        [SerializeField] private Font labelFont;
        private readonly List<Button> buttons = new List<Button>();
        private readonly List<Text> labels = new List<Text>();
        private ProgressionTree displayed;
        private Text heading;
        private Font font;
        private GridLayoutGroup grid;

        private void Start()
        {
            if (controller == null) controller = FindFirstObjectByType<ProgressionController>();
            font = labelFont != null ? labelFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var image = gameObject.GetComponent<Image>() ?? gameObject.AddComponent<Image>();
            image.color = new Color(0.055f, 0.07f, 0.085f, 0.97f);
            var layout = gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(16, 16, 12, 12); layout.spacing = 6;
            layout.childControlHeight = true; layout.childForceExpandHeight = false;
            heading = MakeText("政策 / 能力专精", transform);
            var content = new GameObject("Branches", typeof(RectTransform), typeof(GridLayoutGroup), typeof(LayoutElement));
            content.transform.SetParent(transform, false);
            content.GetComponent<LayoutElement>().preferredHeight = 240;
            grid = content.GetComponent<GridLayoutGroup>();
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount = 3;
            grid.spacing = new Vector2(8, 12);
        }

        private Text MakeText(string value, Transform parent)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>(); text.font = font; text.fontSize = 16;
            text.color = Color.white; text.text = value; text.alignment = TextAnchor.MiddleLeft;
            text.raycastTarget = false;
            go.GetComponent<LayoutElement>().preferredHeight = 36;
            return text;
        }

        private void Update()
        {
            if (controller == null || heading == null) return;
            controller.EnsureBound();
            var tree = controller.Tree;
            if (tree == null) return;
            grid.cellSize = new Vector2(Mathf.Max(80, (((RectTransform)transform).rect.width - 48) / 3), 64);
            if (!ReferenceEquals(tree, displayed))
            {
                foreach (var button in buttons) Destroy(button.gameObject);
                buttons.Clear(); labels.Clear(); displayed = tree;
                foreach (var node in tree.Nodes)
                {
                    var go = new GameObject(node.Id, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
                    go.transform.SetParent(grid.transform, false); go.GetComponent<LayoutElement>().preferredHeight = 64;
                    go.GetComponent<Image>().color = new Color(0.12f, 0.17f, 0.19f);
                    var button = go.GetComponent<Button>();
                    string id = node.Id; button.onClick.AddListener(() => controller.TryPurchase(id));
                    buttons.Add(button);
                    var label = MakeText(string.Empty, go.transform);
                    var rect = (RectTransform)label.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
                    rect.offsetMin = new Vector2(10, 0); rect.offsetMax = new Vector2(-10, 0); labels.Add(label);
                }
            }
            heading.text = "政策 / 能力专精    余额 " + controller.Funds;
            var nodes = tree.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                var n = nodes[i]; bool bought = tree.IsUnlocked(n.Id);
                var status = tree.Availability(n.Id);
                buttons[i].interactable = controller.CanPurchase && status == PurchaseResult.Success && controller.Funds >= n.Cost;
                labels[i].text = n.Title + "  +" + n.PointsPerTick + "/轮\n" + (bought ? "已解锁" : n.Cost + " 金币")
                    + (status == PurchaseResult.MissingPrerequisite ? " · 需前置" : string.Empty);
            }
        }
    }
}
