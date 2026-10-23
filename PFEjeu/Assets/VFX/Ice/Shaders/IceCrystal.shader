Shader "IceVFX/IceCrystal"
{
    // URP. Requis : URP Asset > Opaque Texture activée (réfraction).
    // Le mesh porte des coordonnées barycentriques dans COLOR.rgb (liseré d'arêtes) ;
    // COLOR.a pilote un dissolve supplémentaire (particules mesh : alpha over lifetime = fonte).
    Properties
    {
        [Header(Body)]
        _DeepColor ("Deep Color", Color) = (0.05, 0.32, 0.72, 1)
        _Opacity ("Opacity", Range(0, 1)) = 0.8
        _Refraction ("Refraction", Range(0, 0.25)) = 0.11
        _Chromatic ("Chromatic Split", Range(0, 1)) = 0.35
        _Absorption ("Depth Absorption", Range(0, 2)) = 0.9
        [Header(Fresnel and Reflection)]
        [HDR] _RimColor ("Rim Color", Color) = (0.3, 0.85, 1.5, 1)
        _Fresnel ("Fresnel Power", Range(0.5, 8)) = 2.4
        _EnvStrength ("Environment Reflection", Range(0, 3)) = 1.0
        _SpecPower ("Specular Power", Range(8, 1024)) = 400
        _SpecStrength ("Specular Strength", Range(0, 12)) = 3.2
        [Header(Edges)]
        [HDR] _EdgeColor ("Edge Glow Color", Color) = (0.5, 1.3, 2.2, 1)
        _EdgeWidth ("Edge Width (px)", Range(0.3, 6)) = 1.1
        _EdgeIntensity ("Edge Intensity", Range(0, 6)) = 0.9
        [Header(Inner Cracks)]
        [HDR] _CrackColor ("Crack Color", Color) = (0.55, 1.4, 2.0, 1)
        _CrackScale ("Crack Scale", Range(1, 24)) = 4.5
        _CrackDepth ("Crack Parallax Depth", Range(0, 1)) = 0.45
        _CrackWidth ("Crack Width", Range(0.02, 0.3)) = 0.07
        _CrackIntensity ("Crack Intensity", Range(0, 6)) = 1.9
        [Header(Micro Scratches)]
        _ScratchStrength ("Scratch Strength", Range(0, 1)) = 0.35
        _ScratchScale ("Scratch Scale", Range(10, 120)) = 55
        [Header(Core)]
        _CoreGlow ("Core Glow", Range(0, 6)) = 1.1
        [Header(Dissolve)]
        _Dissolve ("Dissolve", Range(0, 1)) = 0
        _DissolveWidth ("Edge Width", Range(0.005, 0.3)) = 0.08
        [HDR] _DissolveEdge ("Edge Color", Color) = (0.5, 1.6, 2.2, 1)
        _NoiseScale ("Dissolve Noise Scale", Range(0.5, 12)) = 4
        _RadialMix ("Radial Dissolve (0 noise, 1 core-out)", Range(0, 1)) = 0
        _RadialOrigin ("Radial Origin (object space)", Vector) = (0, 0, 0.1, 0)
        _RadialExtent ("Radial Extent", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _DeepColor; half _Opacity; half _Refraction; half _Chromatic; half _Absorption;
                half4 _RimColor; half _Fresnel; half _EnvStrength; half _SpecPower; half _SpecStrength;
                half4 _EdgeColor; half _EdgeWidth; half _EdgeIntensity;
                half4 _CrackColor; half _CrackScale; half _CrackDepth; half _CrackWidth; half _CrackIntensity;
                half _ScratchStrength; half _ScratchScale;
                half _CoreGlow;
                half _Dissolve; half _DissolveWidth; half4 _DissolveEdge; half _NoiseScale;
                half _RadialMix; float4 _RadialOrigin; float _RadialExtent;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 positionOS : TEXCOORD1;
                float4 color : COLOR;
            };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.positionOS = i.positionOS.xyz;
                o.color = i.color;
                return o;
            }

            float3 hash33(float3 p)
            {
                p = frac(p * float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yxz + 33.33);
                return frac((p.xxy + p.yxx) * p.zyx);
            }
            float hash13(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.zyx + 31.32);
                return frac((p.x + p.y) * p.z);
            }
            float vnoise(float3 p)
            {
                float3 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = lerp(lerp(hash13(i), hash13(i + float3(1,0,0)), f.x), lerp(hash13(i + float3(0,1,0)), hash13(i + float3(1,1,0)), f.x), f.y);
                float b = lerp(lerp(hash13(i + float3(0,0,1)), hash13(i + float3(1,0,1)), f.x), lerp(hash13(i + float3(0,1,1)), hash13(i + float3(1,1,1)), f.x), f.y);
                return lerp(a, b, f.z);
            }
            float fbm(float3 p) { return 0.55 * vnoise(p) + 0.3 * vnoise(p * 2.1) + 0.15 * vnoise(p * 4.3); }

            // Fissures : arêtes de cellules de Voronoi 3D (F2 - F1)
            float cracks(float3 p)
            {
                float3 ip = floor(p), fp = frac(p);
                float f1 = 8.0, f2 = 8.0;
                [unroll] for (int z = -1; z <= 1; z++)
                [unroll] for (int y = -1; y <= 1; y++)
                [unroll] for (int x = -1; x <= 1; x++)
                {
                    float3 g = float3(x, y, z);
                    float3 d = g + hash33(ip + g) - fp;
                    float dd = dot(d, d);
                    if (dd < f1) { f2 = f1; f1 = dd; } else if (dd < f2) { f2 = dd; }
                }
                float e = sqrt(f2) - sqrt(f1);
                return 1.0 - smoothstep(0.0, _CrackWidth, e);
            }

            half3 SceneRefract(float2 uv) { return SampleSceneColor(saturate(uv)); }

            half4 frag(Varyings i) : SV_Target
            {
                // ---- dissolve : bruit, ou radial du noyau vers l'extérieur
                float n = fbm(i.positionOS * _NoiseScale);
                float radial = 1.0 - saturate(length(i.positionOS - _RadialOrigin.xyz) / max(_RadialExtent, 0.001));
                float dn = lerp(n, saturate(radial * 0.85 + n * 0.15), _RadialMix);
                float dis = max(_Dissolve, 1.0 - i.color.a);
                float edge = 0;
                if (dis > 0.0001)
                {
                    clip(dn - dis * 1.05);
                    edge = 1.0 - saturate((dn - dis) / _DissolveWidth);
                }

                float3 V = normalize(GetWorldSpaceViewDir(i.positionWS));
                // facettes plates via dérivées
                float3 N = normalize(cross(ddy(i.positionWS), ddx(i.positionWS)));
                if (dot(N, V) < 0) N = -N;
                float faceId = hash13(round(N * 40.0));       // variation par facette

                // micro-rayures : perturbent légèrement la normale
                float3 sp = i.positionOS * _ScratchScale;
                float sA = vnoise(sp * float3(1.0, 0.07, 1.0) + 3.1);
                float sB = vnoise(sp.zxy * float3(1.0, 0.07, 1.0) + 9.7);
                float scr = smoothstep(0.7, 0.92, max(sA, sB));
                N = normalize(N + (hash33(floor(sp * 0.5)) - 0.5) * scr * _ScratchStrength * 0.6);

                float ndv = saturate(dot(N, V));
                float fres = pow(1.0 - ndv, _Fresnel);
                float thick = 0.35 + 0.65 * (1.0 - ndv);      // épaisseur apparente

                // ---- réfraction avec dispersion chromatique
                float2 suv = i.positionCS.xy / _ScaledScreenParams.xy;
                float3 nVS = TransformWorldToViewDir(N);
                float2 off = nVS.xy * _Refraction * (0.5 + thick);
                half3 behind;
                behind.r = SceneRefract(suv + off * (1.0 + _Chromatic)).r;
                behind.g = SceneRefract(suv + off).g;
                behind.b = SceneRefract(suv + off * (1.0 - _Chromatic)).b;
                // absorption : plus c'est épais, plus ça vire au bleu profond
                half3 absorb = exp(-(1.0 - _DeepColor.rgb) * thick * _Absorption * 1.8);
                half3 body = behind * absorb + _DeepColor.rgb * 0.18 * thick;

                // ---- fissures internes avec parallaxe (2 couches)
                float3 vOS = normalize(TransformWorldToObjectDir(V));
                float c = 0;
                [unroll] for (int k = 0; k < 2; k++)
                {
                    float3 p = i.positionOS * _CrackScale - vOS * (k + 1) * _CrackDepth * 1.6 + k * 17.3;
                    c += cracks(p) / (k + 1.0);
                }
                c = saturate(c);

                // ---- spéculaire net : lumière principale + 2 reflets "studio" fixes liés à la caméra
                Light L = GetMainLight();
                float3 H = normalize(L.direction + V);
                half spec = pow(saturate(dot(N, H)), _SpecPower) * _SpecStrength;
                float3 camR = UNITY_MATRIX_V[0].xyz, camU = UNITY_MATRIX_V[1].xyz, camB = UNITY_MATRIX_V[2].xyz;
                float3 L2 = normalize(-camR * 0.55 + camU * 0.8 + camB * 0.55);
                float3 L3 = normalize(camR * 0.7 - camU * 0.25 + camB * 0.65);
                half spec2 = pow(saturate(dot(N, normalize(L2 + V))), _SpecPower * 0.8) * _SpecStrength * 0.7;
                half spec3 = pow(saturate(dot(N, normalize(L3 + V))), _SpecPower * 0.5) * _SpecStrength * 0.35;
                half sparkle = (spec2 + spec3) * (0.55 + 0.9 * faceId) + scr * pow(saturate(dot(N, H)), 24.0) * 1.5;

                float3 R = reflect(-V, N);
                half4 env = SAMPLE_TEXTURECUBE_LOD(unity_SpecCube0, samplerunity_SpecCube0, R, 1.0);
                half3 envC = DecodeHDREnvironment(env, unity_SpecCube0_HDR);

                // ---- liseré d'arêtes (barycentriques)
                float3 bc = i.color.rgb;
                float3 dpx = bc / max(fwidth(bc), 1e-5);
                float edgeLine = 1.0 - saturate(min(dpx.x, min(dpx.y, dpx.z)) / _EdgeWidth);

                half3 col = body;
                col += _RimColor.rgb * fres;
                col += envC * (0.3 + fres * 1.2) * _EnvStrength * 0.6;
                col += _CrackColor.rgb * c * _CrackIntensity * (0.35 + ndv);
                col += _DeepColor.rgb * _CoreGlow * pow(ndv, 3.0) * (0.6 + 0.4 * fbm(i.positionOS * 6.0 + _Time.y * 0.6)) * 2.0;
                col += (spec * L.color + sparkle);
                col += _EdgeColor.rgb * edgeLine * _EdgeIntensity * (0.45 + 0.55 * fres + 0.2);
                col += _DissolveEdge.rgb * edge;

                half a = saturate(_Opacity + fres * 0.5 + spec + sparkle * 0.5 + edge + edgeLine * 0.4 + c * 0.25);
                return half4(col, a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
