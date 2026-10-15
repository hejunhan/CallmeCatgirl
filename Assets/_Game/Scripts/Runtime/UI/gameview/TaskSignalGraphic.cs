using UnityEngine;
using UnityEngine.UI;

namespace CallmeCatgirl.UI
{
    // Prefab上的装饰图层，绘制任务外缘、扫描线和数据点，不参与点击和碰撞。
    public sealed class TaskSignalGraphic : MaskableGraphic
    {
        private int edges;
        private float thickness, phase, opacity, pulse;
        private Color signal;
        public void Show(Color border, Color data, int outerEdges, float width, float scanPhase, float scanOpacity, float flash)
        {
            raycastTarget = false;
            if (color == border && signal == data && edges == outerEdges && thickness == width
                && phase == scanPhase && opacity == scanOpacity && pulse == flash) return;
            color = border; signal = data; edges = outerEdges; thickness = width;
            phase = scanPhase; opacity = scanOpacity; pulse = flash; SetVerticesDirty();
        }
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); if (edges == 0 && opacity == 0 && pulse == 0) return;
            Rect r = rectTransform.rect; float t = Mathf.Min(thickness, Mathf.Min(r.width, r.height) / 2);
            Color border = Color.Lerp(color, Color.white, pulse * .6f);
            if ((edges & 1) != 0) Quad(mesh, new Rect(r.xMin,r.yMax-t,r.width,t),border);
            if ((edges & 2) != 0) Quad(mesh, new Rect(r.xMax-t,r.yMin,t,r.height),border);
            if ((edges & 4) != 0) Quad(mesh, new Rect(r.xMin,r.yMin,r.width,t),border);
            if ((edges & 8) != 0) Quad(mesh, new Rect(r.xMin,r.yMin,t,r.height),border);
            if (pulse > 0) { var c = signal; c.a *= pulse; Quad(mesh,r,c); }
            if (opacity <= 0) return;
            var scan = signal; scan.a *= opacity;
            float y = Mathf.Lerp(r.yMax-2,r.yMin+2,phase);
            Quad(mesh,new Rect(r.xMin+2,y-1,Mathf.Max(0,r.width-4),2),scan);
            scan.a = Mathf.Clamp01(scan.a * 3);
            // 小数据点沿边缘移动，跟随游戏时钟；无全屏闪烁。
            float x = Mathf.Lerp(r.xMin+3,r.xMax-6,phase);
            Quad(mesh,new Rect(x,r.yMin+3,3,2),scan);
        }
        private static void Quad(VertexHelper mesh, Rect r, Color c)
        {
            int i = mesh.currentVertCount;
            mesh.AddVert(new Vector3(r.xMin,r.yMin),c,Vector2.zero);
            mesh.AddVert(new Vector3(r.xMin,r.yMax),c,Vector2.zero);
            mesh.AddVert(new Vector3(r.xMax,r.yMax),c,Vector2.zero);
            mesh.AddVert(new Vector3(r.xMax,r.yMin),c,Vector2.zero);
            mesh.AddTriangle(i,i+1,i+2); mesh.AddTriangle(i+2,i+3,i);
        }
    }
}
