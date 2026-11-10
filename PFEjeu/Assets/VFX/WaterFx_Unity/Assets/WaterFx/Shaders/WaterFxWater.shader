// Eau réaliste tous usages (URP) : vagues de Gerstner, normales détaillées avec courant à deux phases, absorption de Beer-Lambert,
// réfraction, reflets (sonde de réflexion ou planaire), Fresnel, reflet du soleil, diffusion sous la surface, écume de rivage et de crêtes.
// Les maths pures vivent dans WaterCore.hlsl (partagé avec l'aperçu web).
// Réglages URP nécessaires : Depth Texture + Opaque Texture (asset URP). Voir README.
Shader "WaterFx/Water"
{
    Properties
    {
        [Header(Couleur et profondeur)]
        _DeepColor ("Couleur de diffusion (eau profonde)", Color) = (0.01, 0.07, 0.13, 1)
        _Absorption ("Absorption par metre (R, V, B)", Vector) = (0.30, 0.07, 0.035, 0)
        _Turbidity ("Turbidite (x absorption)", Range(0, 4)) = 1

        [Header(Vagues (dir x, dir z, raideur, longueur d onde))]
        _WaveA ("Vague A", Vector) = (1, 0.25, 0.16, 58)
        _WaveB ("Vague B", Vector) = (0.6, 0.8, 0.12, 31)
        _WaveC ("Vague C", Vector) = (-0.3, 1, 0.09, 17)
        _WaveD ("Vague D", Vector) = (0.9, -0.4, 0.06, 8.5)
        _WaveScale ("Amplitude globale", Range(0, 2)) = 1
        _WaveSpeed ("Vitesse globale", Range(0, 3)) = 1

        [Header(Detail de surface)]
        [NoScaleOffset] _NormalMap ("Normale (tileable)", 2D) = "bump" {}
        _NormalTilingA ("Taille couche A (m)", Float) = 9
        _NormalTilingB ("Taille couche B (m)", Float) = 3.4
        _NormalStrength ("Force des normales", Range(0, 2)) = 0.55
        _ScrollA ("Defilement A (xy, m/s)", Vector) = (0.02, 0.01, 0, 0)
        _ScrollB ("Defilement B (xy, m/s)", Vector) = (-0.015, 0.02, 0, 0)

        [Header(Courant)]
        _FlowDirection ("Direction du courant (xz)", Vector) = (1, 0.3, 0, 0)
        _FlowSpeed ("Vitesse du courant (m/s)", Range(0, 2)) = 0.02
        _FlowPeriod ("Periode de la boucle (s)", Range(0.3, 6)) = 3

        [Header(Reflets et lumiere)]
        [Toggle(_PLANAR_REFLECTION)] _UsePlanar ("Reflexion planaire (script optionnel)", Float) = 0
        _ReflectionStrength ("Force des reflets", Range(0, 1.5)) = 1
        _Roughness ("Rugosite", Range(0.005, 0.5)) = 0.04
        _SpecularStrength ("Reflet du soleil", Range(0, 3)) = 1
        _ReflectionDistortion ("Distorsion des reflets", Range(0, 0.2)) = 0.03
        [HDR] _SssColor ("Couleur sous la surface (cretes)", Color) = (0.06, 0.45, 0.42, 1)
        _SssPower ("Concentration SSS", Range(1, 12)) = 4
        _SssDistortion ("Distorsion SSS", Range(0, 2)) = 0.6

        [Header(Refraction)]
        _RefractionStrength ("Force de la refraction", Range(0, 0.15)) = 0.04

        [Header(Ecume)]
        [NoScaleOffset] _FoamMap ("Ecume (tileable)", 2D) = "black" {}
        _FoamColor ("Couleur de l ecume", Color) = (0.95, 0.97, 1, 1)
        _FoamTiling ("Taille de l ecume (m)", Float) = 7
        _FoamShoreWidth ("Largeur au rivage (m)", Range(0.02, 3)) = 0.9
        _FoamCrest ("Seuil des cretes (1 = aucune)", Range(0.2, 1.2)) = 0.62
        _FoamAmount ("Quantite d ecume", Range(0, 1.5)) = 0.9
        _EdgeFade ("Fondu au rivage (m)", Range(0.01, 2)) = 0.5
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Water"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma shader_feature_local _PLANAR_REFLECTION

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "WaterCore.hlsl"

            TEXTURE2D(_NormalMap);  SAMPLER(sampler_NormalMap);
            TEXTURE2D(_FoamMap);    SAMPLER(sampler_FoamMap);
            TEXTURE2D(_WaterPlanarReflection);  SAMPLER(sampler_WaterPlanarReflection);   // global, fourni par le script optionnel

            CBUFFER_START(UnityPerMaterial)
                float4 _DeepColor;
                float4 _Absorption;
                float  _Turbidity;
                float4 _WaveA;
                float4 _WaveB;
                float4 _WaveC;
                float4 _WaveD;
                float  _WaveScale;
                float  _WaveSpeed;
                float  _NormalTilingA;
                float  _NormalTilingB;
                float  _NormalStrength;
                float4 _ScrollA;
                float4 _ScrollB;
                float4 _FlowDirection;
                float  _FlowSpeed;
                float  _FlowPeriod;
                float  _UsePlanar;
                float  _ReflectionStrength;
                float  _Roughness;
                float  _SpecularStrength;
                float  _ReflectionDistortion;
                float4 _SssColor;
                float  _SssPower;
                float  _SssDistortion;
                float  _RefractionStrength;
                float4 _FoamColor;
                float  _FoamTiling;
                float  _FoamShoreWidth;
                float  _FoamCrest;
                float  _FoamAmount;
                float  _EdgeFade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float4 ndc        : TEXCOORD2;   // xy/w = UV écran ; w = profondeur oeil
                float  height01   : TEXCOORD3;   // hauteur de vague normalisée (écume de crêtes, SSS)
            };

            // Somme des amplitudes : sert à normaliser la hauteur de vague.
            float WaveAmplitudeSum()
            {
                return (_WaveA.z * _WaveA.w + _WaveB.z * _WaveB.w + _WaveC.z * _WaveC.w + _WaveD.z * _WaveD.w) * _WaveScale / 6.2831853;
            }

            Varyings vert(Attributes input)
            {
                Varyings o;
                float3 baseWS = TransformObjectToWorld(input.positionOS.xyz);
                float t = _Time.y * _WaveSpeed;
                float3 tangent = float3(1.0, 0.0, 0.0);
                float3 binormal = float3(0.0, 0.0, 1.0);
                float3 disp = float3(0.0, 0.0, 0.0);
                WaterGerstnerAdd(_WaveA, baseWS.xz, t, _WaveScale, tangent, binormal, disp);
                WaterGerstnerAdd(_WaveB, baseWS.xz, t, _WaveScale, tangent, binormal, disp);
                WaterGerstnerAdd(_WaveC, baseWS.xz, t, _WaveScale, tangent, binormal, disp);
                WaterGerstnerAdd(_WaveD, baseWS.xz, t, _WaveScale, tangent, binormal, disp);

                float3 posWS = baseWS + disp;
                o.positionWS = posWS;
                o.normalWS = normalize(cross(binormal, tangent));
                o.positionCS = TransformWorldToHClip(posWS);

                float4 ndc = o.positionCS * 0.5;
                ndc.xy = float2(ndc.x, ndc.y * _ProjectionParams.x) + ndc.w;
                ndc.zw = o.positionCS.zw;
                o.ndc = ndc;

                float ampSum = WaveAmplitudeSum();
                o.height01 = saturate(0.5 + 0.5 * disp.y / max(ampSum, 0.001));
                return o;
            }

            // Normale de détail avec courant à deux phases (pas de coupure visible au saut de phase).
            float3 SampleFlowNormal(float2 wp, float tiling, float2 scroll, float2 flow, float speedMul, float t)
            {
                float p0 = WaterFlowPhase(t * speedMul, _FlowPeriod, 0.0);
                float p1 = WaterFlowPhase(t * speedMul, _FlowPeriod, 0.5);
                float w0 = WaterFlowWeight(p0);
                float2 baseUV = wp / tiling + scroll * t;
                float2 off0 = flow * p0 * _FlowPeriod * speedMul / tiling;
                float2 off1 = flow * p1 * _FlowPeriod * speedMul / tiling;
                float3 n0 = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, baseUV - off0), 1.0);
                float3 n1 = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, baseUV - off1 + 0.37), 1.0);
                return normalize(lerp(n1, n0, w0));
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 posWS = i.positionWS;
                float3 camToPos = posWS - _WorldSpaceCameraPos;
                float camDist = length(camToPos);
                float3 rayDir = camToPos / max(camDist, 0.0001);
                float3 viewDir = -rayDir;
                float t = _Time.y;
                float2 wp = posWS.xz;
                float2 flow = _FlowDirection.xy * _FlowSpeed;

                // --- normale : vagues + détail (atténué avec la distance pour éviter le scintillement)
                float3 nA = SampleFlowNormal(wp, _NormalTilingA, _ScrollA.xy, flow, 1.0, t);
                float3 nB = SampleFlowNormal(wp, _NormalTilingB, _ScrollB.xy, flow, 0.7, t);
                float3 detail = WaterBlendNormals(nA, nB);
                float2 dxz = detail.xy * _NormalStrength * lerp(1.0, 0.35, saturate(camDist / 60.0));
                float3 N = normalize(float3(i.normalWS.x + dxz.x, i.normalWS.y, i.normalWS.z + dxz.y));

                // --- profondeur et réfraction
                float2 screenUV = i.ndc.xy / i.ndc.w;
                float waterEye = i.ndc.w;
                float2 refrUV = screenUV + N.xz * _RefractionStrength;
                float sceneEyeR = LinearEyeDepth(SampleSceneDepth(refrUV), _ZBufferParams);
                refrUV = lerp(refrUV, screenUV, step(sceneEyeR, waterEye));      // objet devant l'eau : pas de décalage
                float sceneEye = LinearEyeDepth(SampleSceneDepth(refrUV), _ZBufferParams);

                float3 camFwd = -UNITY_MATRIX_V[2].xyz;
                float rayLen = sceneEye / max(dot(rayDir, camFwd), 0.05);        // longueur du rayon jusqu'au fond
                float thick = max(rayLen - camDist, 0.0);                        // épaisseur d'eau traversée
                float vDepth = thick * saturate(-rayDir.y);                      // profondeur verticale

                // --- lumière
                float3 L = normalize(_MainLightPosition.xyz);
                float3 lightCol = _MainLightColor.rgb;
                float3 ambient = SampleSH(float3(0.0, 1.0, 0.0));

                // --- corps de l'eau : fond vu à travers (Beer-Lambert) + diffusion de l'eau profonde
                float3 refr = SampleSceneColor(refrUV);
                float3 trans = WaterBeer(_Absorption.rgb * _Turbidity, thick);
                float3 scatter = _DeepColor.rgb * (ambient + lightCol * saturate(L.y) * 0.6);
                float3 body = refr * trans + scatter * (1.0 - trans);

                // --- reflets
                float3 R = reflect(rayDir, N);
                R.y = abs(R.y);                                                  // pas de reflet du sous-sol
            #if defined(_PLANAR_REFLECTION)
                float2 ruv = screenUV + N.xz * _ReflectionDistortion;
                float3 refl = SAMPLE_TEXTURE2D(_WaterPlanarReflection, sampler_WaterPlanarReflection, ruv).rgb;
            #else
                half4 enc = SAMPLE_TEXTURECUBE_LOD(unity_SpecCube0, samplerunity_SpecCube0, R, PerceptualRoughnessToMipmapLevel(_Roughness));
                float3 refl = DecodeHDREnvironment(enc, unity_SpecCube0_HDR);
            #endif
                float fres = WaterFresnel(saturate(dot(N, viewDir)), 0.02) * _ReflectionStrength;
                float3 specCol = lightCol * WaterSpecular(N, viewDir, L, _Roughness) * _SpecularStrength;
                float3 sssCol = _SssColor.rgb * lightCol * WaterSss(N, viewDir, L, i.height01, _SssDistortion, _SssPower);

                float3 col = lerp(body, refl, saturate(fres)) + specCol + sssCol;

                // --- écume : rivage + crêtes
                float2 fuvA = wp / _FoamTiling + float2(0.013, 0.008) * t - flow * t / _FoamTiling;
                float2 fuvB = wp / (_FoamTiling * 0.63) - float2(0.009, 0.014) * t - flow * t / (_FoamTiling * 0.63);
                float foamTex = saturate(SAMPLE_TEXTURE2D(_FoamMap, sampler_FoamMap, fuvA).r
                                       + 0.5 * SAMPLE_TEXTURE2D(_FoamMap, sampler_FoamMap, fuvB).r - 0.15);
                float foam = saturate(WaterShoreFoam(vDepth, _FoamShoreWidth, foamTex) + WaterCrestFoam(i.height01, _FoamCrest, foamTex)) * _FoamAmount;
                float3 foamCol = _FoamColor.rgb * (ambient + lightCol * saturate(L.y) * 0.8 + 0.03);
                col = lerp(col, foamCol, saturate(foam));

                float alpha = saturate(max(vDepth / max(_EdgeFade, 0.001), foam));
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
