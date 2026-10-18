using UnityEngine;
using UnityEngine.UI;

namespace CallmeCatgirl.UI
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class TrainingCardPattern : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();var r=rectTransform.rect;
            var ink=new Color32(56,103,86,75);
            for(float y=r.yMin+6;y<r.yMax;y+=12)
            {
                int i=mesh.currentVertCount;
                mesh.AddVert(new Vector2(r.xMin,y),ink,Vector2.zero);mesh.AddVert(new Vector2(r.xMax,y),ink,Vector2.zero);
                mesh.AddVert(new Vector2(r.xMax,y+1),ink,Vector2.zero);mesh.AddVert(new Vector2(r.xMin,y+1),ink,Vector2.zero);
                mesh.AddTriangle(i,i+1,i+2);mesh.AddTriangle(i,i+2,i+3);
            }
        }
    }
}
