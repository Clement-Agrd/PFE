// UpgradeVFX/UpgradeDust : bouffée de poussière érodée par le bruit, ombrée "éclairée par le haut", soft particles.
// Texture : R = bruit, G = masque de bouffée. Couleur de vertex : RGB = teinte, A = vie (érosion vers la fin).
Shader "UpgradeVFX/UpgradeDust"
{
    Properties
    {
        _MainTex("Noise (R) / Puff (G)", 2D) = "white" {}
        _Cut("Cut", Range(0,1)) = 0.22
        _Soft("Edge Softness", Range(0.02,0.8)) = 0.35
        _Erode("Erosion over life", Range(0,1)) = 0.45
        _Shade("Underside shade", Range(0,1)) = 0.55
        _SoftDist("Soft particle distance", Float) = 1.2
        _Density("Density", Range(0,2)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Dust"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST; float _Cut, _Soft, _Erode, _Shade, _SoftDist, _Density;
            CBUFFER_END
            struct Attr { float4 pos : POSITION; half4 col : COLOR; float2 uv : TEXCOORD0; };
            struct Vary { float4 cs : SV_POSITION; half4 col : COLOR; float2 uv : TEXCOORD0; float4 sp : TEXCOORD1; float ed : TEXCOORD2; };
            Vary vert(Attr i)
            {
                Vary o; VertexPositionInputs p = GetVertexPositionInputs(i.pos.xyz);
                o.cs = p.positionCS; o.col = i.col; o.uv = TRANSFORM_TEX(i.uv, _MainTex);
                o.sp = ComputeScreenPos(p.positionCS); o.ed = -p.positionVS.z;
                return o;
            }
            half4 frag(Vary i) : SV_Target
            {
                half4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                float life = saturate(i.col.a);
                float d = t.g * (0.5 + 0.95 * t.r);
                float th = _Cut + (1 - life) * _Erode;
                float a = smoothstep(th, th + _Soft, d) * pow(life, 0.6) * _Density;
                float2 suv = i.sp.xy / i.sp.w;
                float scene = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                a *= saturate((scene - i.ed) / _SoftDist);
                float lit = saturate(i.uv.y * 0.9 + 0.1 + (t.r - 0.5) * 0.7);
                half3 rgb = i.col.rgb * lerp(_Shade, 1.12, lit);
                return half4(rgb, saturate(a));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
