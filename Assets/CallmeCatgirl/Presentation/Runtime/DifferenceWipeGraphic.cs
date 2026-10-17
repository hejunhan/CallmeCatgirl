using UnityEngine;
using UnityEngine.UI;

namespace CallmeCatgirl.Presentation
{
    [AddComponentMenu("CallmeCatgirl/Presentation/Difference Wipe Graphic")]
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class DifferenceWipeGraphic : Graphic
    {
        protected override void OnEnable()
        {
            base.OnEnable();
            raycastTarget = false;
            canvasRenderer.cullTransparentMesh = false;
        }
        public override bool Raycast(Vector2 screenPoint, Camera eventCamera) => false;
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect r = rectTransform.rect;
            mesh.AddVert(new Vector3(r.xMin, r.yMin), Color.white, Vector2.zero);
            mesh.AddVert(new Vector3(r.xMin, r.yMax), Color.white, Vector2.up);
            mesh.AddVert(new Vector3(r.xMax, r.yMax), Color.white, Vector2.one);
            mesh.AddVert(new Vector3(r.xMax, r.yMin), Color.white, Vector2.right);
            mesh.AddTriangle(0, 1, 2); mesh.AddTriangle(2, 3, 0);
        }
    }
}
