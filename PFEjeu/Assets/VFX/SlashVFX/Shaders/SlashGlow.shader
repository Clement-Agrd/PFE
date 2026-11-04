// Procedural additive glow used for impact flash (star), shockwave ring, scorch mark, spark particles and trails.
// _Mode: 0 radial glow (+ optional cross star), 1 ring, 2 ground scorch (alpha blended).
Shader "VFX/SlashGlow"
{
    Properties
    {
        [HDR] _Color("Colour (HDR)", Color) = (4, 2, 1, 1)
        _Mode("Mode (0 glow, 1 ring, 2 scorch)", Float) = 0
        _Ring("Ring Radius", Range(0.1, 1)) = 0.8
        _Thick("Ring Thickness", Range(0.02, 0.6)) = 0.25
        _Star("Star Spikes", Range(0, 1)) = 0.6
        _Fade("Fade", Range(0, 1)) = 1
        [Enum(Additive, 1, Alpha, 10)] _DstBlend("Dst Blend", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+20" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "GlowForward"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha [_DstBlend]
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Mode;
                float _Ring;
                float _Thick;
                float _Star;
                float _Fade;
                float _DstBlend;
            CBUFFER_END

            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS);
                o.uv = i.uv;
                o.color = i.color;
                return o;
            }

            float hash(float n) { return frac(sin(n) * 43758.5453); }

            half4 frag(Varyings i) : SV_Target
            {
                float2 p = i.uv * 2.0 - 1.0;
                float r = length(p);
                float a;
                float3 rgb = _Color.rgb;

                if (_Mode < 0.5)
                {
                    float glow = exp(-r * r * 5.0);
                    float spikes = exp(-abs(p.x) * 16.0) * exp(-abs(p.y) * 1.6)
                                 + exp(-abs(p.y) * 16.0) * exp(-abs(p.x) * 1.6);
                    float2 q = float2(p.x + p.y, p.x - p.y) * 0.7071;
                    spikes += 0.6 * (exp(-abs(q.x) * 20.0) * exp(-abs(q.y) * 2.4) + exp(-abs(q.y) * 20.0) * exp(-abs(q.x) * 2.4));
                    a = saturate(glow + spikes * _Star) * (1.0 - smoothstep(0.7, 1.0, r));
                }
                else if (_Mode < 1.5)
                {
                    float x = saturate((r - (_Ring - _Thick)) / _Thick);
                    a = pow(x, 3.0) * (1.0 - smoothstep(0.92, 1.0, x));
                    float ang = atan2(p.y, p.x);
                    a *= 0.7 + 0.3 * hash(floor(ang * 14.0));
                }
                else
                {
                    float ang = atan2(p.y, p.x);
                    float rough = 0.75 + 0.25 * hash(floor(ang * 9.0));
                    float disc = 1.0 - smoothstep(0.45 * rough, 0.95 * rough, r);
                    float crack = pow(saturate(sin(ang * 7.0 + hash(floor(ang * 3.0)) * 6.0) * 0.5 + 0.5), 40.0) * (1.0 - smoothstep(0.05, 0.6, r)) * 0.55;
                    rgb = lerp(float3(0.02, 0.015, 0.012), _Color.rgb, saturate(crack * 1.5));
                    a = saturate(disc * 0.75 + crack) ;
                }

                a *= _Fade * i.color.a * _Color.a;
                return half4(rgb * i.color.rgb, a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
