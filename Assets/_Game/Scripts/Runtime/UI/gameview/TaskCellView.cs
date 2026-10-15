using UnityEngine;
using UnityEngine.UI;

namespace CallmeCatgirl.UI
{
    public sealed class TaskCellView : MonoBehaviour
    {
        [SerializeField] private Image background;
        [SerializeField] private Text label;
        [SerializeField] private TaskSignalGraphic signal;
        public void ShowSignal(Color border, Color data, int edges, float width, float phase, float opacity, float pulse)
        { if (signal != null) signal.Show(border, data, edges, width, phase, opacity, pulse); }
        public void Show(Color color, string text = "")
        { if (background != null) background.color = color; if (label != null) label.text = text; }
    }
}
