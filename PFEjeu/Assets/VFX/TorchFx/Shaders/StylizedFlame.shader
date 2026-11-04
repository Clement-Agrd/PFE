// Flamme stylisée pour URP (Unlit, billboard cylindrique, bandes toon HDR).
// UV.x = largeur du quad, UV.y = hauteur (0 = base posée sur la torche, 1 = pointe).
// Le quad vient de TorchFlame (pivot à la base). Aucune texture requise : forme, bruit et bandes sont procéduraux.
//
// Propriétés "par instance" (écrites par TorchFlame via MaterialPropertyBlock) :
//   _Seed, _Tint, _SpeedMul, _Flicker, _Brightness, _WindWS
Shader "TorchFx/StylizedFlame"
{
    Properties
    {
        [HDR] _ColorCore ("Core (HDR)", Color) = (6.0, 4.2, 1.6, 1)
        [HDR] _ColorMid ("Mid (HDR)", Color) = (3.2, 1.1, 0.12, 1)
        [HDR] _ColorEdge ("Edge (HDR)", Color) = (1.4, 0.18, 0.02, 1)

        _CutEdge ("Outline Threshold", Range(0, 0.6)) = 0.05
        _CutMid ("Mid Threshold", Range(0, 1)) = 0.32
        _CutCore ("Core Threshold", Range(0, 1)) = 0.62
        _Softness ("Band Softness (petit = toon)", Range(0.005, 0.4)) = 0.03

        _Taper ("Tip Taper", Range(0.5, 4)) = 1.25
        _BaseWidth ("Base Width", Range(0.2, 1)) = 0.7
        _NoiseScale ("Noise Scale (X, Y)", Vector) = (2.2, 1.6, 0, 0)
        _ScrollSpeed ("Rise Speed", Float) = 1.2
        _Distort ("Side Distortion", Range(0, 1.5)) = 0.55
        _Erode ("Tip Erosion", Range(0, 1.5)) = 0.7
        _WindBend ("Wind Bend", Range(0, 2)) = 0.8
        _Additive ("Additive Blend", Range(0, 1)) = 0.35

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
            Name "StylizedFlame"

            // Alpha prémultiplié : la sortie alpha est réduite par _Additive, ce qui mélange alpha classique et additif.
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
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

                // Deux couches de bruit qui montent : l'une déforme les flancs, l'autre ronge la pointe.
                float2 noiseUV = float2(IN.uv.x * _NoiseScale.x + _Seed * 3.7, v * _NoiseScale.y - t * _ScrollSpeed);
                float n1 = ValueNoise(noiseUV);
                float n2 = ValueNoise(noiseUV * 2.3 + float2(7.1, -t * 0.7));

                // Balancement (plus fort vers la pointe) + courbure due au vent.
                float sway = (n1 - 0.5) * _Distort * v + sin(t * 2.1 + v * 3.0) * 0.06 * v;
                float bend = IN.windX * _WindBend * v * v;
                float xs = x - sway - bend;

                // Profil en goutte : large à la base, pointu en haut.
                float baseWidth = lerp(_BaseWidth, 1.0, smoothstep(0.0, 0.22, v));
                float width = max(baseWidth * (1.0 - pow(v, _Taper)), 0.0001);
                float inside = 1.0 - abs(xs) / width;

                float f = inside - v * n2 * _Erode + _Flicker * 0.06;
                f = saturate(f);

                // Trois bandes toon (bord, milieu, cœur). _Softness petit = cel-shading net, grand = flamme douce.
                float edgeMask = smoothstep(_CutEdge, _CutEdge + _Softness, f);
                float midMask = smoothstep(_CutMid, _CutMid + _Softness, f);
                float coreMask = smoothstep(_CutCore, _CutCore + _Softness, f);

                half3 color = lerp(_ColorEdge.rgb, _ColorMid.rgb, midMask);
                color = lerp(color, _ColorCore.rgb, coreMask);
                color *= 1.0 + (1.0 - v) * 0.35; // la base est plus chaude

                float baseFade = smoothstep(0.0, 0.07, v); // fondu doux là où la flamme touche la torche
                float alpha = edgeMask * baseFade * _Tint.a;

                color *= _Tint.rgb * _Brightness;

                return half4(color * alpha, alpha * (1.0 - _Additive));
            }
            ENDHLSL
        }
    }
}
