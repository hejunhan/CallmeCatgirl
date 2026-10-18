using CallmeCatgirl.Gameplay.TaskGrid;
using UnityEngine;
using UnityEngine.UI;

namespace CallmeCatgirl.UI
{
    /// <summary>Socket support arms and cursor tether derive from the same model coordinates.</summary>
    public sealed class MemorySpringGraphic : MaskableGraphic
    {
        private MemoryInstallGame owner;
        private Vector2 cursor;
        public void Bind(MemoryInstallGame value) { owner = value; raycastTarget = false; }
        public void SetCursor(Vector2 value) { cursor = value; SetVerticesDirty(); }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear(); if (owner?.PhysicsState == null) return;
            var sim = owner.PhysicsState; Color dark = new Color(.31f, .33f, .32f, .85f);
            for (int node = 0; node < 3; node++)
            {
                var top = owner.SimulationToFrame(sim.NodePosition(node));
                var foot = owner.SimulationToFrame(new Vec2(sim.NodeRestX(node), 34));
                var previous = top;
                for (int j = 1; j <= 10; j++)
                {
                    var next = Vector2.Lerp(top, foot, j / 10f);
                    if (j < 10) next.x += j % 2 == 0 ? -8 : 8;
                    Line(vh, previous, next, 2, dark); previous = next;
                }
                Line(vh, foot + Vector2.left * 20, foot + Vector2.right * 20, 3, dark);
            }
            if (sim.IsGrabbed) Line(vh, owner.SimulationToFrame(sim.GripWorld), cursor, 2, new Color(.64f, .45f, .16f, .8f));
        }
        private static void Line(VertexHelper vh, Vector2 a, Vector2 b, float width, Color tint)
        {
            if ((b - a).sqrMagnitude < .01f) return;
            var d = (b - a).normalized; var n = new Vector2(-d.y, d.x) * width * .5f;
            int i = vh.currentVertCount;
            vh.AddVert(a - n, tint, Vector2.zero); vh.AddVert(a + n, tint, Vector2.zero);
            vh.AddVert(b + n, tint, Vector2.zero); vh.AddVert(b - n, tint, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i, i + 2, i + 3);
        }
    }
}
