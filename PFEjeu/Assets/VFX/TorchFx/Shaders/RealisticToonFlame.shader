// Flamme semi-réaliste "cartoon" pour URP (Unlit, billboard cylindrique).
// Forme réaliste : bruit fBm avec domain warping (langues de feu qui se tordent en montant).
// Look cartoon : palette quantifiée en paliers adoucis (_Steps / _Stylize).
// Mêmes propriétés par instance que TorchFx/StylizedFlame : compatible TorchFlame / FlameProfile tel quel.
// UV.x = largeur du quad, UV.y = hauteur (0 = base posée sur la torche, 1 = pointe).
Shader "TorchFx/RealisticToonFlame"
{
    Properties
    {
        [HDR] _ColorWhite ("White-hot (HDR)", Color) = (9, 7.5, 4.5, 1)
        [HDR] _ColorCore ("Core (HDR)", Color) = (6.0, 3.6, 1.0, 1)
        [HDR] _ColorMid ("Mid (HDR)", Color) = (3.4, 1.2, 0.12, 1)
        [HDR] _ColorEdge ("Edge (HDR)", Color) = (1.2, 0.14, 0.02, 1)

        _CutEdge ("Edge Threshold", Range(0, 0.6)) = 0.04
        _CutMid ("Mid Threshold", Range(0, 1)) = 0.3
        _CutCore ("Core Threshold", Range(0, 1)) = 0.55
        _Softness ("Edge Softness", Range(0.005, 0.4)) = 0.12

        _Taper ("Tip Taper", Range(0.5, 4)) = 1.5
        _BaseWidth ("Base Width", Range(0.2, 1)) = 0.7
        _NoiseScale ("Noise Scale (X, Y)", Vector) = (2.4, 1.8, 0, 0)
        _ScrollSpeed ("Rise Speed", Float) = 1.8
        _Distort ("Side Distortion", Range(0, 1.5)) = 0.8
        _Erode ("Tip Erosion", Range(0, 1.5)) = 0.9
        _WindBend ("Wind Bend", Range(0, 2)) = 1.0
        _Additive ("Additive Blend", Range(0, 1)) = 0.6
        _Warp ("Turbulence (domain warp)", Range(0, 2)) = 0.9
        _Steps ("Colour Steps (cartoon)", Range(2, 8)) = 5
        _Stylize ("Stylize (0 realiste, 1 cartoon)", Range(0, 1)) = 0.45

        [Header(Per Instance)]
        _Seed ("Seed", Float) = 0
        _Tint ("Tint (HDR)", Color) = (1, 1, 1, 1)
        _SpeedMul ("Speed Multiplier", Float) = 1
        _Flicker ("Flicker (-1..1)", Float) = 0
        _Brightness ("Brightness", Float) = 1
        _WindWS ("Wind (world)", Vector) = (0, 0, 0, 0)
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
            Name "RealisticToonFlame"

            // Alpha prémultiplié : la sortie alpha est réduite par _Additive, ce qui mélange alpha classique et additif.
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ColorWhite;
                half4 _ColorCore;
                half4 _ColorMid;
                half4 _ColorEdge;
                float _CutEdge;
                float _CutMid;
                float _CutCore;
                float _Softness;
                float _Taper;
                float _BaseWidth;
                float4 _NoiseScale;
                float _ScrollSpeed;
                float _Distort;
                float _Erode;
                float _WindBend;
                float _Additive;
                float _Warp;
                float _Steps;
                float _Stylize;
                float _Seed;
                half4 _Tint;
                float _SpeedMul;
                float _Flicker;
                float _Brightness;
                float4 _WindWS;
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
                float windX : TEXCOORD1; // vent projeté sur l'axe horizontal du quad
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

            // 4 octaves, normalisé dans [0,1].
            float Fbm(float2 p)
            {
                float a = 0.5;
                float sum = 0.0;
                [unroll] for (int i = 0; i < 4; i++)
                {
                    sum += a * ValueNoise(p);
                    p = p * 2.03 + float2(5.2, 1.3);
                    a *= 0.5;
                }
                return sum / 0.9375;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                // Billboard cylindrique : le quad reste vertical et pivote autour de l'axe Y monde pour faire face à la caméra.
                float3 originWS = float3(unity_ObjectToWorld._m03, unity_ObjectToWorld._m13, unity_ObjectToWorld._m23);
                float2 scaleXY = float2(
                    length(float3(unity_ObjectToWorld._m00, unity_ObjectToWorld._m10, unity_ObjectToWorld._m20)),
                    length(float3(unity_ObjectToWorld._m01, unity_ObjectToWorld._m11, unity_ObjectToWorld._m21)));

                float3 toCamera = _WorldSpaceCameraPos - originWS;
                float2 horizontalDir = toCamera.xz;
                float horizontalLength = max(length(horizontalDir), 1e-4);
                float2 forward = horizontalDir / horizontalLength;
                float3 right = float3(forward.y, 0.0, -forward.x);

                float3 positionWS = originWS
                    + right * (IN.positionOS.x * scaleXY.x)
                    + float3(0.0, 1.0, 0.0) * (IN.positionOS.y * scaleXY.y);

                OUT.positionHCS = TransformWorldToHClip(positionWS);
                OUT.uv = IN.uv;
                OUT.windX = dot(_WindWS.xyz, right);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float v = saturate(IN.uv.y);
                // Facteur 1.35 : la flamme n'occupe qu'environ 74 % du quad, le reste est de la marge pour le balancement.
                float x = (IN.uv.x - 0.5) * 2.0 * 1.35;
                float t = _Time.y * _SpeedMul + _Seed;

                // Domain warping : q déforme p, puis w déforme encore -> langues de feu qui se tordent en montant.
                float2 p = float2(IN.uv.x * _NoiseScale.x + _Seed * 3.7, v * _NoiseScale.y - t * _ScrollSpeed);
                float2 q = float2(Fbm(p + float2(0.0, t * 0.3)), Fbm(p + float2(5.2, 1.3 - t * 0.4)));
                float w = Fbm(p + q * (1.6 * _Warp) + float2(1.7, -t * 0.5));

                float sway = (w - 0.5) * _Distort * 1.3 * v + sin(t * 2.3 + v * 3.5 + q.x * 2.0) * 0.08 * v;
                float bend = IN.windX * _WindBend * v * v;
                float xs = x - sway - bend;

                // Profil en goutte : large à la base, pointu en haut.
                float baseWidth = lerp(_BaseWidth, 1.0, smoothstep(0.0, 0.2, v));
                float width = max(baseWidth * (1.0 - pow(v, _Taper)), 0.0001);
                float inside = 1.0 - abs(xs) / width;

                // Densité : centre dense, bords et pointe rongés par le bruit (aspect "léché").
                float d = inside - v * (0.35 + 0.65 * w) * _Erode + (q.y - 0.5) * 0.25 + _Flicker * 0.06;
                d = saturate(d);

                // Température : chaude au cœur et à la base, froide aux bords et à la pointe.
                float temp = saturate(d * 1.15 * (1.0 - v * 0.45) + (1.0 - v) * 0.12);

                // Quantification adoucie (cartoon) mélangée au dégradé continu (réaliste).
                float s = temp * _Steps;
                float stepped = (floor(s) + smoothstep(0.35, 0.65, frac(s))) / _Steps;
                float tq = lerp(temp, stepped, _Stylize);

                half3 col = lerp(_ColorEdge.rgb, _ColorMid.rgb, smoothstep(_CutEdge, _CutMid + 0.15, tq));
                col = lerp(col, _ColorCore.rgb, smoothstep(_CutMid, _CutCore + 0.1, tq));
                col = lerp(col, _ColorWhite.rgb, smoothstep(_CutCore + 0.1, 1.0, tq));
                col *= 1.0 + (1.0 - v) * 0.3; // la base est plus chaude

                float edgeA = smoothstep(_CutEdge, _CutEdge + _Softness, d);
                float baseFade = smoothstep(0.0, 0.07, v); // fondu doux là où la flamme touche la torche
                float alpha = edgeA * baseFade * _Tint.a;

                col *= _Tint.rgb * _Brightness;
                return half4(col * alpha, alpha * (1.0 - _Additive));
            }
            ENDHLSL
        }
    }
}
