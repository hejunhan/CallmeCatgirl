using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using CallmeCatgirl.Gameplay.TaskGrid;
namespace CallmeCatgirl.UI
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class CorpusShapeGraphic : MaskableGraphic
    {
        private Cell[] cells=System.Array.Empty<Cell>();
        public void SetCells(IEnumerable<Cell> value){cells=value.ToArray();SetVerticesDirty();}
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();if(cells.Length==0)return;
            var rect=rectTransform.rect;float size=Mathf.Min(rect.width/(cells.Max(c=>c.X)+1),rect.height/(cells.Max(c=>c.Y)+1));
            foreach(var c in cells)
            {
                int n=vh.currentVertCount;float x=rect.xMin+c.X*size,y=rect.yMax-c.Y*size;
                vh.AddVert(new Vector3(x+1,y-1,0),color,Vector2.zero);vh.AddVert(new Vector3(x+size-1,y-1,0),color,Vector2.zero);
                vh.AddVert(new Vector3(x+size-1,y-size+1,0),color,Vector2.zero);vh.AddVert(new Vector3(x+1,y-size+1,0),color,Vector2.zero);
                vh.AddTriangle(n,n+1,n+2);vh.AddTriangle(n,n+2,n+3);
            }
        }
    }
}
