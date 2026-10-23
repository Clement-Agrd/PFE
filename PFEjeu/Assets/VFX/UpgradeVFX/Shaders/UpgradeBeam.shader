// UpgradeVFX/UpgradeBeam : additif. _Mode 0 = traîne du front (vive en haut du mur), 1 = pilier de lumière (vif en bas), 2 = anneau plat.
// Mesh : UpgradeBand (uv = angle, hauteur) ou UpgradeFrontRing (uv = angle, rayon). Piloté par MaterialPropertyBlock (_Color, _Fade).
Shader "UpgradeVFX/UpgradeBeam"
{
    Properties
    {
        [HDR] _Color("Color", Color) = (2.2,1.1,0.25,1)
        _Fade("Fade", Range(0,1)) = 1
        _Mode("Mode (0 trail, 1 pillar, 2 flat ring)", Float) = 0
        _Streaks("Streaks", Float) = 36
        _Speed("Scroll speed", Float) = 1.5
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Beam"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha One
            ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color; float _Fade, _Mode, _Streaks, _Speed;
            CBUFFER_END
            struct Attr { float4 pos : POSITION; float2 uv : TEXCOORD0; };
            struct Vary { float4 cs : SV_POSITION; float2 uv : TEXCOORD0; };
            float h11(float x) { return frac(sin(x * 127.1) * 43758.5453); }
            float n1(float x) { float i = floor(x), f = frac(x); f = f * f * (3 - 2 * f); return lerp(h11(i), h11(i + 1), f); }
            Vary vert(Attr i) { Vary o; o.cs = TransformObjectToHClip(i.pos.xyz); o.uv = i.uv; return o; }
            half4 frag(Vary i) : SV_Target
            {
                float u = i.uv.x, v = i.uv.y;
                float trail = pow(saturate(v), 3);
                float pillar = pow(saturate(1 - v), 1.4) * smoothstep(0, 0.03, v);
                float ring = pow(sin(3.14159 * saturate(v)), 1.6);
                float g = _Mode < 0.5 ? trail : (_Mode < 1.5 ? pillar : ring);
                float sv = _Mode > 0.5 && _Mode < 1.5 ? -v * 5 : 0;
                float s = 0.45 + 0.85 * n1(u * _Streaks + sv + _Time.y * _Speed * (0.6 + _Mode * 0.5));
                float a = saturate(g * (_Mode > 1.5 ? 0.6 + 0.4 * s : s)) * _Fade;
                return half4(_Color.rgb * (0.7 + g), a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
