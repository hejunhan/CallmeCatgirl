using UnityEngine;
using UnityEngine.UI;

namespace CallmeCatgirl.UI
{
    /// <summary>Screen-aligned desk geometry; no background texture or text.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class WorkbenchGeometry : MaskableGraphic
    {
        private VertexHelper mesh;
        private Rect bounds;
        private Vector2 pixel;
        protected override void OnEnable() { base.OnEnable(); raycastTarget=false; }
        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear();mesh=helper;bounds=rectTransform.rect;
            pixel=new Vector2(bounds.width/Screen.width,bounds.height/Screen.height);
            Quad(new Vector2(0,0),new Vector2(Screen.width,0),new Vector2(Screen.width,Screen.height),new Vector2(0,Screen.height),new Color32(244,244,244,255));
            float rhythm=Mathf.Round(64*Mathf.Clamp(Screen.height/1080f,.75f,1.3f));
            var grid=new Color32(232,232,232,255);
            for(float x=rhythm;x<Screen.width;x+=rhythm)Line(new Vector2(x,0),new Vector2(x,Screen.height),1,grid);
            for(float y=rhythm;y<Screen.height;y+=rhythm)Line(new Vector2(0,y),new Vector2(Screen.width,y),1,grid);
            float inset=Mathf.Round(Screen.height*.025f);
            var rule=new Color32(185,185,185,255);
            Line(new Vector2(inset,Screen.height*.113f),new Vector2(Screen.width-inset,Screen.height*.113f),1,rule);
            Line(new Vector2(inset,Screen.height-inset),new Vector2(Screen.width-inset,Screen.height-inset),1,rule);
            foreach(float x in new[]{inset,Screen.width-inset})
            foreach(float y in new[]{inset,Screen.height-inset})
            {
                float dx=x<Screen.width/2?28:-28,dy=y<Screen.height/2?28:-28;
                Line(new Vector2(x,y),new Vector2(x+dx,y),2,new Color32(111,111,111,255));
                Line(new Vector2(x,y),new Vector2(x,y+dy),2,new Color32(111,111,111,255));
            }
            var centre=new Vector2(Screen.width-inset,inset);
            float unit=Screen.height/1080f;
            foreach(float radius in new[]{64f,112f,160f})
            {
                var previous=centre+Vector2.left*radius*unit;
                for(int i=1;i<=48;i++)
                {
                    float angle=(180-i*90f/48)*Mathf.Deg2Rad;
                    var next=centre+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*radius*unit;
                    Line(previous,next,1,rule);previous=next;
                }
            }
        }
        private void Line(Vector2 a,Vector2 b,float width,Color32 ink)
        {
            a=new Vector2(Mathf.Round(a.x)+.5f,Mathf.Round(a.y)+.5f);
            b=new Vector2(Mathf.Round(b.x)+.5f,Mathf.Round(b.y)+.5f);
            var d=(b-a).normalized;var n=new Vector2(-d.y,d.x)*width*.5f;
            Quad(a-n,b-n,b+n,a+n,ink);
        }
        private void Quad(Vector2 a,Vector2 b,Vector2 c,Vector2 d,Color32 ink)
        {
            int start=mesh.currentVertCount;
            foreach(var p in new[]{a,b,c,d})
                mesh.AddVert(new Vector3(bounds.xMin+p.x*pixel.x,bounds.yMax-p.y*pixel.y),ink,Vector2.zero);
            mesh.AddTriangle(start,start+1,start+2);mesh.AddTriangle(start+2,start+3,start);
        }
    }
}
