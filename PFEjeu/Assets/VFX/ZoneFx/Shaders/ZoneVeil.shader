// Voile de lumière qui monte de l'anneau (paroi cylindrique ouverte, additive, double face).
// Tout est calculé : colonnes verticales de bruit qui défilent vers le haut, hauteur irrégulière par colonne, fondu doux en haut et en bas.
// Les bruits sont périodiques sur le tour du cylindre : aucune couture visible.
Shader "ZoneFx/Veil"
{
    Properties
    {
        [HDR] _Color ("Couleur (HDR)", Color) = (0.4, 1.0, 0.25, 1)
        [HDR] _CoreColor ("Couleur du pied (HDR)", Color) = (0.8, 1.0, 0.7, 1)
        _Intensity ("Intensité", Range(0, 3)) = 1
        _FadePower ("Fondu vers le haut", Range(0.5, 4)) = 1.6
        _FineColumns ("Colonnes fines (entier)", Range(8, 80)) = 40
        _CoarseColumns ("Colonnes larges (pair)", Range(4, 40)) = 14
        _Scroll ("Vitesse de montée", Range(0, 2)) = 0.6
        _Visible ("Visibilité (script)", Range(0, 1)) = 1
        _Boost ("Éclat (script)", Range(0, 3)) = 1
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ZoneVeil"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _CoreColor;
                float  _Intensity;
                float  _FadePower;
                float  _FineColumns;
                float  _CoarseColumns;
                float  _Scroll;
                float  _Visible;
                float  _Boost;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            // Bruit de valeur périodique en x (period = nombre entier de cellules sur le tour complet).
            float PNoise(float2 p, float period)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float x0 = fmod(i.x, period);
                float x1 = fmod(i.x + 1.0, period);
                return lerp(lerp(Hash(float2(x0, i.y)), Hash(float2(x1, i.y)), f.x),
                            lerp(Hash(float2(x0, i.y + 1.0)), Hash(float2(x1, i.y + 1.0)), f.x), f.y);
            }

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = input.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float u = i.uv.x;
                float v = i.uv.y;
                float t = _Time.y * _Scroll;

                float fineCols = round(_FineColumns);
                float coarseCols = max(2.0, 2.0 * round(_CoarseColumns * 0.5));      // pair, pour que la moitié reste entière
                float fine = PNoise(float2(u * fineCols, v * 3.0 - t * 1.2), fineCols);
                float coarse = PNoise(float2(u * coarseCols, v * 1.6 - t * 0.6), coarseCols);
                float streak = smoothstep(0.2, 0.85, lerp(fine, coarse, 0.5));

                // Chaque colonne a sa propre hauteur : le sommet du voile est irrégulier mais reste doux.
                float halfCols = coarseCols * 0.5;
                float columnHeight = 0.4 + 0.6 * PNoise(float2(u * halfCols + 3.0, _Time.y * 0.15 * _Scroll), halfCols);
                float top = 1.0 - smoothstep(columnHeight * 0.3, columnHeight, v);

                float bottom = smoothstep(0.0, 0.05, v);
                float fade = pow(saturate(1.0 - v), _FadePower);
                float mask = bottom * fade * top * (0.1 + 0.9 * streak);

                float3 color = lerp(_Color.rgb, _CoreColor.rgb, saturate(1.0 - v * 2.0) * 0.6);
                return half4(color * (mask * _Intensity * _Visible * _Boost), 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
