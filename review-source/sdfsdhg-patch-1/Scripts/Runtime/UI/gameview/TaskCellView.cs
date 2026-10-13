using UnityEngine;
using UnityEngine.UI;

namespace CallmeCatgirl.UI
{
    public sealed class TaskCellView : MonoBehaviour
    {
        [SerializeField] private Image background;
        [SerializeField] private Text label;
        public void Show(Color color, string text = "")
        { if (background != null) background.color = color; if (label != null) label.text = text; }
    }
}
