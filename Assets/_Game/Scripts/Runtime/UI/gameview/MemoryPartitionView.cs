using System.Collections.Generic;
using CallmeCatgirl.Gameplay.TaskGrid;
using UnityEngine;
using UnityEngine.UI;

namespace CallmeCatgirl.UI
{
    // 场景中的可替换隔板图层：仅绘制边界，不生成按钮、不拦截拖拽。
    public sealed class MemoryPartitionView : MaskableGraphic
    {
        [SerializeField, Min(1)] private float thickness = 4;
        private int width, height;
        private int[] bags;
        private bool[] open;

        public void Show(TaskSimulation sim, ISet<Cell> preview = null)
        {
            bool dirty = bags == null || width != sim.Width || height != sim.Height;
            if (dirty) { width = sim.Width; height = sim.Height; bags = new int[width * height]; open = new bool[bags.Length]; }
            for (int i = 0; i < bags.Length; i++)
            {
                var cell = new Cell(i % width, i / width);
                int bag = sim.CompartmentAt(cell); bool available = sim.IsOpen(cell) || (preview != null && preview.Contains(cell));
                dirty |= bags[i] != bag || open[i] != available;
                bags[i] = bag; open[i] = available;
            }
            raycastTarget = false;
            if (dirty) SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            if (bags == null) return;
            var rect = rectTransform.rect;
            float cw = rect.width / width, ch = rect.height / height;
            // 画每条边一次。未开放格会形成边框，分区编号不同则形成内部隔板。
            for (int y = 0; y < height; y++) for (int x = 0; x <= width; x++)
            {
                int a = x > 0 ? x - 1 + y * width : -1, b = x < width ? x + y * width : -1;
                if (Boundary(a, b)) Quad(mesh, new Rect(rect.xMin + x * cw - thickness / 2, rect.yMax - (y + 1) * ch, thickness, ch));
            }
            for (int y = 0; y <= height; y++) for (int x = 0; x < width; x++)
            {
                int a = y > 0 ? x + (y - 1) * width : -1, b = y < height ? x + y * width : -1;
                if (Boundary(a, b)) Quad(mesh, new Rect(rect.xMin + x * cw, rect.yMax - y * ch - thickness / 2, cw, thickness));
            }
        }

        private bool Boundary(int a, int b)
        {
            bool left = a >= 0 && open[a], right = b >= 0 && open[b];
            return left != right || (left && right && bags[a] != bags[b]);
        }
        private void Quad(VertexHelper mesh, Rect rect)
        {
            int first = mesh.currentVertCount;
            mesh.AddVert(new Vector3(rect.xMin, rect.yMin), color, Vector2.zero);
            mesh.AddVert(new Vector3(rect.xMin, rect.yMax), color, Vector2.zero);
            mesh.AddVert(new Vector3(rect.xMax, rect.yMax), color, Vector2.zero);
            mesh.AddVert(new Vector3(rect.xMax, rect.yMin), color, Vector2.zero);
            mesh.AddTriangle(first, first + 1, first + 2); mesh.AddTriangle(first + 2, first + 3, first);
        }
    }
}
