// Shader de ruban de slash pour URP (Unlit, additif, HDR → compatible Bloom).
// UV.x = âge du ruban (0 = neuf, 1 = expiré)   UV.y = position sur la lame (0 = intérieur, 1 = pointe)
// Dissolve procédural (aucune texture requise) : la queue s'érode avec un bord lumineux, à la façon Piloto Studio.
Shader "Ally/FX/SlashTrail"
{
    Properties
    {
        [HDR] _ColorCore ("Core Color (HDR)", Color) = (2.5, 4.0, 8.0, 1)
        [HDR] _ColorEdge ("Edge Color (HDR)", Color) = (0.2, 0.6, 5.0, 1)
        _NoiseScale ("Noise Scale (X = along, Y = across)", Vector) = (3, 9, 0, 0)
        _ScrollSpeed ("Noise Scroll Speed", Float) = 2.0
        _NoiseAmount ("Noise Amount", Range(0, 1)) = 0.6
        _Softness ("Dissolve Softness", Range(0.01, 0.5)) = 0.12
        _EdgeWidth ("Edge Width", Range(0.01, 0.5)) = 0.18
        _InnerFade ("Inner Fade (garde)", Range(0.01, 1)) = 0.55
        _HeadSoft ("Head Softness (age)", Range(0.0, 0.5)) = 0.18
        _TipSoft ("Tip Edge Softness", Range(0.0, 0.6)) = 0.3
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "SlashTrail"

            Blend SrcAlpha One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ColorCore;
                half4 _ColorEdge;
                float4 _NoiseScale;
                float _ScrollSpeed;
                float _NoiseAmount;
                float _Softness;
                float _EdgeWidth;
                float _InnerFade;
                float _HeadSoft;
                float _TipSoft;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 cell = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float a = Hash21(cell);
                float b = Hash21(cell + float2(1.0, 0.0));
                float c = Hash21(cell + float2(0.0, 1.0));
                float d = Hash21(cell + float2(1.0, 1.0));

                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float age = saturate(IN.uv.x);
                float across = saturate(IN.uv.y);

                // Stries le long du ruban : peu de variation en X, beaucoup en Y.
                float2 noiseUV = float2(IN.uv.x * _NoiseScale.x - _Time.y * _ScrollSpeed, across * _NoiseScale.y);
                float noise = ValueNoise(noiseUV);

                // Seuil de dissolve qui monte avec l'âge : la queue s'érode de façon irrégulière.
                float d = (noise * _NoiseAmount + (1.0 - _NoiseAmount)) - age;

                float alpha = smoothstep(0.0, _Softness, d);
                float edge = 1.0 - smoothstep(0.0, _EdgeWidth, d);
                float body = smoothstep(0.0, _InnerFade, across);
                body *= 1.0 - smoothstep(1.0 - _TipSoft, 1.0, across);   // bord pointe adouci (plus de bord carré)
                body *= smoothstep(0.0, max(_HeadSoft, 0.0001), age);       // tête du ruban qui s allume en fondu
                body *= saturate(1.0 - age);                                  // queue qui s effile sans coupure

                half3 color = lerp(_ColorCore.rgb, _ColorEdge.rgb, edge);
                return half4(color * body, alpha * body * _ColorCore.a);
            }
            ENDHLSL
        }
    }
}
