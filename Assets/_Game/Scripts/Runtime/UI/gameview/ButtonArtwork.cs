using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CallmeCatgirl.UI
{
    // 只负责外观，点击事件与游戏状态仍由原控制器负责。
    [RequireComponent(typeof(Button), typeof(Image))]
    public sealed class ButtonArtwork : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        [SerializeField] private Sprite normalSprite;
        [SerializeField] private Sprite selectedSprite;
        [SerializeField] private Sprite disabledSprite;
        [SerializeField] private Sprite hoverSprite;
        [SerializeField] private Sprite pressedSprite;
        [SerializeField] private Text label;
        [SerializeField] private bool normalIncludesText;
        [SerializeField] private bool selectedIncludesText;
        [SerializeField] private bool hideLabel;
        private Button button;
        private Image image;
        private bool selected, lastInteractable, hovered, pressed;
        public bool IsSelected => selected;
        private void OnEnable() => Refresh();
        private void OnDisable() { hovered = pressed = false; }
        private void LateUpdate()
        {
            if (button != null && lastInteractable != button.IsInteractable()) Refresh();
        }
        public void SetSelected(bool value) { selected = value; Refresh(); }
        public void OnPointerEnter(PointerEventData data) { hovered = true; Refresh(); }
        public void OnPointerExit(PointerEventData data) { hovered = pressed = false; Refresh(); }
        public void OnPointerDown(PointerEventData data)
        { if (data.button == PointerEventData.InputButton.Left) { pressed = true; Refresh(); } }
        public void OnPointerUp(PointerEventData data)
        { if (data.button == PointerEventData.InputButton.Left) { pressed = false; Refresh(); } }
        private void Refresh()
        {
            if (button == null) { button = GetComponent<Button>(); image = GetComponent<Image>(); }
            lastInteractable = button.IsInteractable();
            bool down = pressed && pressedSprite != null;
            image.sprite = !lastInteractable ? normalSprite
                : down ? pressedSprite : selected && selectedSprite != null ? selectedSprite
                : hovered && hoverSprite != null ? hoverSprite : normalSprite;
            image.color = lastInteractable ? Color.white : new Color(.84f,.84f,.84f,1);
            if (label == null) return;
            bool includesText = image.sprite == normalSprite ? normalIncludesText
                : image.sprite == selectedSprite && selectedIncludesText;
            label.enabled = !hideLabel && !includesText;
            label.color = lastInteractable && (selected || down) ? Color.white : new Color(.086f, .086f, .086f, 1);
        }
    }
}
