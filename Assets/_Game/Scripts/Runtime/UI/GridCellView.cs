using UnityEngine;
using UnityEngine.UI;

namespace CallmeCatgirl.UI
{
    public sealed class GridCellView : MonoBehaviour
    {
        [SerializeField] private Image background;
        [SerializeField] private GameObject lockedMarker;
        [SerializeField] private Color openColor = new Color(0.17f, 0.21f, 0.27f);
        [SerializeField] private Color lockedColor = new Color(0.08f, 0.10f, 0.13f);
        public void Show(bool open)
        { background.color = open ? openColor : lockedColor; if (lockedMarker != null) lockedMarker.SetActive(!open); }
    }
}
