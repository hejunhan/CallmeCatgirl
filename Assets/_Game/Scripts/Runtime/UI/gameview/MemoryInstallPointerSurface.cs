using UnityEngine;
using UnityEngine.EventSystems;

namespace CallmeCatgirl.UI
{
    /// <summary>The whole image accepts a drag; the owner holds one fixed RAM-local grip.</summary>
    public sealed class MemoryInstallPointerSurface : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler, IInitializePotentialDragHandler
    {
        private MemoryInstallGame owner;
        public void Bind(MemoryInstallGame value) => owner = value;
        public void OnInitializePotentialDrag(PointerEventData data) => data.useDragThreshold = false;
        public void OnPointerDown(PointerEventData data) => owner?.PointerDown(data);
        public void OnDrag(PointerEventData data) => owner?.PointerDrag(data);
        public void OnPointerUp(PointerEventData data) => owner?.PointerUp(data);
        private void OnDisable() => owner?.ReleasePointer();
    }
}
