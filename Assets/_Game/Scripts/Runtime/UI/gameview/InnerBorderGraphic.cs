using UnityEngine;
using UnityEngine.UI;

namespace CallmeCatgirl.UI
{
    [System.Flags] public enum BorderEdges { Top=1,Right=2,Bottom=4,Left=8,All=15 }

    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class InnerBorderGraphic : MaskableGraphic
    {
        [SerializeField] private float pixels=1;
        [SerializeField] private BorderEdges edges=BorderEdges.All;
        public static InnerBorderGraphic Attach(RectTransform target,float width,BorderEdges sides,Color tint)
        {
            var child=target.Find("InnerBorder");
            var border=child==null?null:child.GetComponent<InnerBorderGraphic>();
            if(border==null)
            {
                var go=new GameObject("InnerBorder",typeof(RectTransform),typeof(InnerBorderGraphic));go.transform.SetParent(target,false);
                border=go.GetComponent<InnerBorderGraphic>();var r=border.rectTransform;r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;
                go.AddComponent<LayoutElement>().ignoreLayout=true;
            }
            border.Configure(width,sides,tint);return border;
        }
        public void Configure(float width,BorderEdges sides,Color tint)
        {
            raycastTarget=false;
            if(pixels==width&&edges==sides&&color==tint)return;
            pixels=width;edges=sides;color=tint;SetVerticesDirty();
        }
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();var r=rectTransform.rect;float w=Mathf.Min(pixels/(canvas==null?1:canvas.scaleFactor),r.width*.5f,r.height*.5f);
            if((edges&BorderEdges.Top)!=0)Quad(mesh,r.xMin,r.yMax-w,r.xMax,r.yMax);
            if((edges&BorderEdges.Bottom)!=0)Quad(mesh,r.xMin,r.yMin,r.xMax,r.yMin+w);
            float bottom=r.yMin+((edges&BorderEdges.Bottom)!=0?w:0),top=r.yMax-((edges&BorderEdges.Top)!=0?w:0);
            if((edges&BorderEdges.Left)!=0)Quad(mesh,r.xMin,bottom,r.xMin+w,top);
            if((edges&BorderEdges.Right)!=0)Quad(mesh,r.xMax-w,bottom,r.xMax,top);
        }
        private void Quad(VertexHelper mesh,float left,float bottom,float right,float top)
        {
            int i=mesh.currentVertCount;
            foreach(var p in new[]{new Vector2(left,bottom),new Vector2(right,bottom),new Vector2(right,top),new Vector2(left,top)})mesh.AddVert(p,color,Vector2.zero);
            mesh.AddTriangle(i,i+1,i+2);mesh.AddTriangle(i,i+2,i+3);
        }
    }
}
