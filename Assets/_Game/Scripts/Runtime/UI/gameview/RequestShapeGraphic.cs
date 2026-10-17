using System.Collections.Generic;
using CallmeCatgirl.Gameplay.TaskGrid;
using UnityEngine;
using UnityEngine.UI;

namespace CallmeCatgirl.UI
{
    [ExecuteAlways, RequireComponent(typeof(CanvasRenderer))]
    public sealed class RequestShapeGraphic : MaskableGraphic
    {
        [SerializeField, HideInInspector] private Vector2Int[] editorShape;
        [SerializeField, HideInInspector] private string editorConversation;
        private Cell[] cells=new Cell[0];
        protected override void OnEnable()
        {
            base.OnEnable();
            if(!Application.isPlaying && editorShape!=null && editorShape.Length>0)
                Show(System.Array.ConvertAll(editorShape,c=>new Cell(c.x,c.y)),editorConversation,0,false,false,false);
        }
        private readonly HashSet<Cell> occupied=new HashSet<Cell>();
        private Color fill;
        private Color strokeColor;
        private int pattern;
        private float step,progress;
        private bool emphasized,ready,selectedShape;
        public void Show(Cell[] shape,string conversation,float calculation,bool selected,bool linked,bool complete)
        {
#if UNITY_EDITOR
            if(!Application.isPlaying){editorShape=System.Array.ConvertAll(shape,c=>new Vector2Int(c.X,c.Y));editorConversation=conversation;}
#endif
            cells=shape;occupied.Clear();int columns=1,rows=1;
            foreach(var c in cells){occupied.Add(c);columns=Mathf.Max(columns,c.X+1);rows=Mathf.Max(rows,c.Y+1);}
            step=Mathf.Min(30,112f/Mathf.Max(columns,rows));
            rectTransform.sizeDelta=new Vector2(columns*step,rows*step+8);
            int key=0;foreach(char c in conversation??string.Empty)key=(key*31+c)&0x7fffffff;
            pattern=key%3;
            fill=new[]{new Color32(188,199,207,255),new Color32(211,197,167,255),new Color32(185,200,179,255)}[key%3];
            progress=Mathf.Clamp01(calculation);emphasized=selected||linked;selectedShape=selected;ready=complete;
            raycastTarget=true;SetVerticesDirty();
        }
        public override bool Raycast(Vector2 screen,Camera camera)
        {
            Vector2 p;RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform,screen,camera,out p);
            return occupied.Contains(new Cell(Mathf.FloorToInt(p.x/step),Mathf.FloorToInt(-p.y/step)));
        }
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();float stroke=(selectedShape?2f:1f)/(canvas==null?1:canvas.scaleFactor);
            strokeColor=emphasized||ready?new Color32(22,22,22,255):new Color32(82,82,82,255);
            foreach(var c in cells)
            {
                var p=new Vector2(c.X*step,-c.Y*step);
                Quad(mesh,p+new Vector2(1,-1),p+new Vector2(step-1,-step+1),emphasized?Color.Lerp(fill,Color.white,.08f):fill);
                Edge(mesh,c,new Cell(0,-1),p,p+Vector2.right*step,stroke);
                Edge(mesh,c,new Cell(1,0),p+Vector2.right*step,p+new Vector2(step,-step),stroke);
                Edge(mesh,c,new Cell(0,1),p+new Vector2(0,-step),p+new Vector2(step,-step),stroke);
                Edge(mesh,c,new Cell(-1,0),p,p+Vector2.down*step,stroke);
            }
            if(progress>0)Quad(mesh,new Vector2(0,-rectTransform.rect.height+3),new Vector2(rectTransform.rect.width*progress,-rectTransform.rect.height),new Color32(22,22,22,255));
        }
        private void Edge(VertexHelper mesh,Cell cell,Cell direction,Vector2 a,Vector2 b,float width)
        {
            if(occupied.Contains(new Cell(cell.X+direction.X,cell.Y+direction.Y)))return;
            var d=(b-a).normalized;float length=Vector2.Distance(a,b);
            // The neighbour direction points out of the cell; local Y is inverted.
            var inward=new Vector2(-direction.X,direction.Y);
            if(pattern==1){for(float x=0;x<length;x+=8)InnerStrip(mesh,a+d*x,a+d*Mathf.Min(x+5,length),inward,width);}
            else InnerStrip(mesh,a,b,inward,width);
            if(pattern==2){var offset=inward*(width+1/(canvas==null?1:canvas.scaleFactor));InnerStrip(mesh,a+offset,b+offset,inward,width*.55f);}
        }
        private void InnerStrip(VertexHelper mesh,Vector2 a,Vector2 b,Vector2 inward,float width)
        {
            var n=inward*width;int i=mesh.currentVertCount;
            foreach(var p in new[]{a,b,b+n,a+n})mesh.AddVert(p,strokeColor,Vector2.zero);
            mesh.AddTriangle(i,i+1,i+2);mesh.AddTriangle(i,i+2,i+3);
        }
        private static void Quad(VertexHelper mesh,Vector2 a,Vector2 b,Color color)
        {
            int i=mesh.currentVertCount;
            foreach(var p in new[]{a,new Vector2(b.x,a.y),b,new Vector2(a.x,b.y)})mesh.AddVert(p,color,Vector2.zero);
            mesh.AddTriangle(i,i+1,i+2);mesh.AddTriangle(i,i+2,i+3);
        }
    }
}
