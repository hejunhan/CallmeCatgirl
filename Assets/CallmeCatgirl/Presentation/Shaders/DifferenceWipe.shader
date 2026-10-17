Shader "CallmeCatgirl/UI/Difference Wipe"
{
    Properties { _Progress ("Progress", Range(0,1)) = 0 }
    SubShader
    {
        Tags { "Queue"="Overlay" "IgnoreProjector"="True" "RenderType"="Transparent" }
        Cull Off ZWrite Off ZTest Always
        // S + D - 2*S*D = lerp(D, 1-D, S). Preserve destination alpha.
        Blend OneMinusDstColor OneMinusSrcColor, Zero One
        ColorMask RGB
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            struct Attributes { float4 vertex : POSITION; };
            struct Varyings { float4 vertex : SV_POSITION; float4 screen : TEXCOORD0; };
            float _Progress;
            float4 _ViewportSize;
            int _PaperCount;
            float4 _Paper01[16], _Paper23[16], _PaperStrength[16];
            int _HasCompute, _WindowCount;
            float4 _Compute01, _Compute23;
            float4 _Window01[8], _Window23[8];
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.screen = ComputeScreenPos(output.vertex);
                return output;
            }
            float cross2(float2 a, float2 b) { return a.x*b.y-a.y*b.x; }
            float edgeDistance(float2 a, float2 b, float2 p)
            {
                float2 edge = b-a;
                return cross2(edge,p-a) / max(length(edge), .0001);
            }
            float paperCoverage(float2 p, float4 ab, float4 cd)
            {
                float area = cross2(ab.zw-ab.xy, cd.xy-ab.xy);
                if (abs(area) < .001) return 0;
                float winding = area < 0 ? -1 : 1;
                // Compute each edge after its winding is applied (including mirrored UI).
                float d = min(min(edgeDistance(ab.xy,ab.zw,p)*winding,edgeDistance(ab.zw,cd.xy,p)*winding),
                        min(edgeDistance(cd.xy,cd.zw,p)*winding,edgeDistance(cd.zw,ab.xy,p)*winding));
                return saturate(d + .5); // one physical pixel of anti-aliasing
            }
            float4 frag(Varyings input) : SV_Target
            {
                float2 p = input.screen.xy / input.screen.w * _ViewportSize.xy;
                float distance = _ViewportSize.x-p.x+p.y;
                float sweep = saturate(_Progress*(_ViewportSize.x+_ViewportSize.y)-distance+.5);
                if (_Progress <= 0) sweep = 0;
                if (_Progress >= 1) sweep = 1;
                float compute = _HasCompute != 0 ? paperCoverage(p,_Compute01,_Compute23) : 0;
                float window = 0;
                [loop] for (int i=0; i<_WindowCount; i++)
                    window = max(window,paperCoverage(p,_Window01[i],_Window23[i]));
                // Only visible compute content is exempt. Any window painted over it still inverts.
                float amount = sweep*(1-compute*(1-window));
                return float4(amount,amount,amount,1);
            }
            ENDCG
        }
    }
}
