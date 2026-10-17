Shader "CallmeCatgirl/UI/Signal Glow"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Color ("Signal", Color) = (0.753,0.914,0.161,1)
        _LinePixels ("Line width", Float) = 1
        _HaloPixels ("Halo width", Float) = 8
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha One
        ColorMask RGB
        Stencil
        {
            Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp]
            ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask]
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            struct Attributes { float4 vertex:POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct Varyings { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; float2 local:TEXCOORD1; };
            float4 _Color, _ClipRect;
            float _LinePixels, _HaloPixels;
            Varyings vert(Attributes input)
            {
                Varyings output; output.vertex=UnityObjectToClipPos(input.vertex);
                output.uv=input.uv; output.color=input.color*_Color; output.local=input.vertex.xy; return output;
            }
            float4 frag(Varyings input):SV_Target
            {
                float2 pixels=min(input.uv,1-input.uv)/max(fwidth(input.uv),float2(.00001,.00001));
                float d=min(pixels.x,pixels.y);
                float lineAlpha=1-saturate(abs(d-_HaloPixels)-_LinePixels*.5);
                float halo=exp2(-abs(d-_HaloPixels)*4/max(_HaloPixels,.01))*.28;
                float alpha=saturate(lineAlpha+halo)*input.color.a;
                #ifdef UNITY_UI_CLIP_RECT
                alpha*=UnityGet2DClipping(input.local,_ClipRect);
                #endif
                return float4(input.color.rgb, alpha);
            }
            ENDCG
        }
    }
}
