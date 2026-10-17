using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
namespace CallmeCatgirl.UI
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class AssociationLineGraphic : MaskableGraphic
    {
        private List<Vector2[]> paths=new List<Vector2[]>();
        public void SetPaths(List<Vector2[]> value){paths=value;raycastTarget=false;SetVerticesDirty();}
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();float scale=canvas==null?1:canvas.scaleFactor;float dash=8/scale,gap=6/scale,width=1.5f/scale;
            foreach(var path in paths)for(int segment=1;segment<path.Length;segment++)
            {
                var a=path[segment-1];var b=path[segment];var direction=(b-a).normalized;var n=new Vector2(-direction.y,direction.x)*width*.5f;float length=Vector2.Distance(a,b);
                for(float offset=0;offset<length;offset+=dash+gap)
                {
                    var p=a+direction*offset;var q=a+direction*Mathf.Min(offset+dash,length);int i=mesh.currentVertCount;
                    foreach(var v in new[]{p-n,q-n,q+n,p+n})mesh.AddVert(v,new Color32(82,82,82,255),Vector2.zero);
                    mesh.AddTriangle(i,i+1,i+2);mesh.AddTriangle(i,i+2,i+3);
                }
            }
        }
    }
}
