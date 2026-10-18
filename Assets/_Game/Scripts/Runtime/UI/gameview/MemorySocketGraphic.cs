using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.UI;

namespace CallmeCatgirl.UI
{
    /// <summary>Tessellated authored slot art follows the actual three compliant nodes.</summary>
    public sealed class MemorySocketGraphic : BaseMeshEffect
    {
        private MemoryInstallGame owner;
        private Sprite sprite;
        public float CenterDeflection => owner?.PhysicsState?.NodeDeflection(1) ?? 0;
        public void Bind(MemoryInstallGame value, Sprite artwork, Color tint)
        { owner = value; sprite = artwork; graphic.color = tint; graphic.raycastTarget = false; graphic.SetVerticesDirty(); }
        public void SetVerticesDirty() => graphic.SetVerticesDirty();
        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || owner?.PhysicsState == null || sprite == null) return;
            vh.Clear();
            var r = graphic.rectTransform.rect; var uv = DataUtility.GetOuterUV(sprite);
            const int segments = 40;
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments, x = Mathf.Lerp(r.xMin, r.xMax, t);
                var world = transform.TransformPoint(new Vector2(x, 0));
                float frameX = owner.RamTransform.parent.InverseTransformPoint(world).x;
                float modelX = owner.FrameToSimulation(new Vector2(frameX, 0)).X;
                var sim = owner.PhysicsState;
                float blend = Mathf.Clamp(modelX / (sim.Tuning.RamLength * .5f), -1, 1);
                float deflection = blend < 0 ? Mathf.Lerp(sim.NodeDeflection(1), sim.NodeDeflection(0), -blend)
                    : Mathf.Lerp(sim.NodeDeflection(1), sim.NodeDeflection(2), blend);
                float dy = -deflection * owner.RamTransform.rect.width / sim.Tuning.RamLength;
                vh.AddVert(new Vector3(x, r.yMin + dy), graphic.color, new Vector2(Mathf.Lerp(uv.x, uv.z, t), uv.y));
                vh.AddVert(new Vector3(x, r.yMax + dy), graphic.color, new Vector2(Mathf.Lerp(uv.x, uv.z, t), uv.w));
                if (i == 0) continue;
                int k = (i - 1) * 2;
                vh.AddTriangle(k, k + 1, k + 2); vh.AddTriangle(k + 2, k + 1, k + 3);
            }
        }
    }
}
