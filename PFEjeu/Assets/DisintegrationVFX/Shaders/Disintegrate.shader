Shader "VFX/Disintegrate"
{
    Properties
    {
        [Header(Base)]
        [MainTexture] _BaseMap ("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor ("Color", Color) = (1,1,1,1)
        _Smoothness ("Smoothness", Range(0,1)) = 0.3
        _Metallic ("Metallic", Range(0,1)) = 0
        [HDR] _EmissionColor ("Original Emission", Color) = (0,0,0,0)

        [Header(Burn)]
        _Progress ("Progress", Range(0,1)) = 0
        _EdgeWidth ("Edge Width", Range(0.01,0.3)) = 0.08
        _CharWidth ("Char Width", Range(0.01,0.5)) = 0.24
        _NoiseAmp ("Noise Amplitude", Range(0,0.6)) = 0.22
        _NoiseScale ("Noise Scale", Float) = 1.6
        _NoiseScroll ("Noise Scroll", Float) = 0.15
        _CurlAmount ("Curl Amount", Range(0,0.3)) = 0.05
        _EdgeIntensity ("Edge Emission", Range(0,12)) = 6
        _CharColor ("Char Color", Color) = (0.03,0.02,0.015,1)
        _CrackScale ("Crack Scale", Float) = 1.3
        _FlashAmount ("Flash", Range(0,1)) = 0
        [HDR] _FlashColor ("Flash Color", Color) = (4,2.2,1,1)
        _BurnOrigin ("Origin (xyz)", Vector) = (0,0,0,0)
        _BurnDir ("Dir (xyz) Radial (w)", Vector) = (0,1,0,0)
        _BurnRange ("Range", Float) = 1.8

        [NoScaleOffset] _BurnNoise ("Burn Noise", 2D) = "gray" {}
        [NoScaleOffset] _BurnCracks ("Burn Cracks", 2D) = "white" {}
        [NoScaleOffset] _BurnRamp ("Burn Ramp", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_BaseMap);   SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BurnNoise); SAMPLER(sampler_BurnNoise);
        TEXTURE2D(_BurnCracks);SAMPLER(sampler_BurnCracks);
        TEXTURE2D(_BurnRamp);  SAMPLER(sampler_BurnRamp);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half4 _EmissionColor;
            half _Smoothness;
            half _Metallic;
            float _Progress;
            float _EdgeWidth;
            float _CharWidth;
            float _NoiseAmp;
            float _NoiseScale;
            float _NoiseScroll;
            float _CurlAmount;
            half _EdgeIntensity;
            half4 _CharColor;
            float _CrackScale;
            half _FlashAmount;
            half4 _FlashColor;
            float4 _BurnOrigin;
            float4 _BurnDir;
            float _BurnRange;
        CBUFFER_END

        // Trois echantillons projetes (jamais purement horizontaux : pas de stries verticales),
        // scroll lent pour que le front respire.
        float3 NoiseP(float3 wp) { return wp * _NoiseScale; }

        float BurnNoiseLOD(float3 wp)
        {
            float t = _Time.y * _NoiseScroll;
            float3 p = NoiseP(wp);
            float a = SAMPLE_TEXTURE2D_LOD(_BurnNoise, sampler_BurnNoise, float2(p.x + p.z * 0.5, p.y) + float2(t, 0), 0).r;
            float b = SAMPLE_TEXTURE2D_LOD(_BurnNoise, sampler_BurnNoise, float2(p.z - p.x * 0.5, p.y * 1.3 + 0.37) + float2(0, t), 0).r;
            float c = SAMPLE_TEXTURE2D_LOD(_BurnNoise, sampler_BurnNoise, float2(p.x + p.z, p.y * 1.1) * 2.3 - t, 0).g;
            return (a + b) * 0.35 + c * 0.3;
        }

        float BurnNoise(float3 wp)
        {
            float t = _Time.y * _NoiseScroll;
            float3 p = NoiseP(wp);
            float a = SAMPLE_TEXTURE2D(_BurnNoise, sampler_BurnNoise, float2(p.x + p.z * 0.5, p.y) + float2(t, 0)).r;
            float b = SAMPLE_TEXTURE2D(_BurnNoise, sampler_BurnNoise, float2(p.z - p.x * 0.5, p.y * 1.3 + 0.37) + float2(0, t)).r;
            float c = SAMPLE_TEXTURE2D(_BurnNoise, sampler_BurnNoise, float2(p.x + p.z, p.y * 1.1) * 2.3 - t).g;
            return (a + b) * 0.35 + c * 0.3;
        }

        float BurnBase(float3 wp)
        {
            float3 d = wp - _BurnOrigin.xyz;
            float v = _BurnDir.w > 0.5 ? length(d) : dot(d, _BurnDir.xyz);
            return v / max(_BurnRange, 0.001);
        }

        // s > 0 : derriere le front. Progress=1 consume tout le mesh.
        float FrontPos()
        {
            float endv = 1.0 + _NoiseAmp + _EdgeWidth + _CharWidth * 1.4 + 0.02;
            return lerp(-_NoiseAmp - 0.02, endv, _Progress);
        }

        float BurnS(float3 wp, float n)
        {
            return FrontPos() - (BurnBase(wp) + (n - 0.5) * _NoiseAmp * 2.0);
        }

        float HoleLimit(float n2)
        {
            return _EdgeWidth + _CharWidth * (0.45 + 0.9 * n2);
        }

        float Cracks(float3 wp)
        {
            return saturate(SAMPLE_TEXTURE2D(_BurnCracks, sampler_BurnCracks, float2(wp.x + wp.z * 0.6, wp.y) * _CrackScale).r * 2.6);
        }

        float N2(float3 wp)
        {
            return SAMPLE_TEXTURE2D(_BurnNoise, sampler_BurnNoise, wp.xy * _NoiseScale * 3.1 + wp.z).g;
        }

        float3 Curl(float3 wp, float3 nw)
        {
            float s = BurnS(wp, BurnNoiseLOD(wp));
            return nw * saturate((s - _EdgeWidth * 0.5) / max(_CharWidth, 0.01)) * _CurlAmount;
        }

        void BurnClip(float3 wp)
        {
            float s = BurnS(wp, BurnNoise(wp));
            float crackV = Cracks(wp);
            clip(HoleLimit(N2(wp) - (1.0 - crackV) * 0.2) - s);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                float3 wp = TransformObjectToWorld(IN.positionOS.xyz);
                float3 nw = TransformObjectToWorldNormal(IN.normalOS);
                wp += Curl(wp, nw);
                OUT.positionWS = wp;
                OUT.normalWS = nw;
                OUT.positionCS = TransformWorldToHClip(wp);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                float3 wp = IN.positionWS;
                float n = BurnNoise(wp);
                float n2 = N2(wp);
                float s = BurnS(wp, n);
                float crackV = Cracks(wp);
                clip(HoleLimit(n2 - (1.0 - crackV) * 0.2) - s);

                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;
                half3 albedo = tex.rgb;

                float charT = saturate((s - _EdgeWidth * 0.35) / max(_EdgeWidth * 1.2, 0.001));
                float charDeep = saturate((s - _EdgeWidth) / max(_CharWidth, 0.001));
                half3 charCol = _CharColor.rgb * (0.6 + 0.8 * n2);
                charCol = lerp(charCol * half3(2.2, 1.4, 1.0), charCol * 0.4, charDeep);
                charCol *= lerp(0.35, 1.0, crackV);
                albedo = lerp(albedo, charCol, charT);

                float u = saturate(s / max(_EdgeWidth, 0.001));
                half3 ramp = SAMPLE_TEXTURE2D(_BurnRamp, sampler_BurnRamp, float2(pow(u, 0.7), 0.5)).rgb;
                float onEdge = step(0.0, s);
                float flicker = 0.85 + 0.3 * SAMPLE_TEXTURE2D(_BurnNoise, sampler_BurnNoise, wp.xz * 5.0 + _Time.y * 0.7).b;
                half3 emission = ramp * _EdgeIntensity * onEdge * flicker * (1.0 - saturate(charDeep * 1.5));
                emission += half3(1.0, 0.25, 0.04) * (1.0 - crackV) * (1.0 - charDeep) * charT * 0.5 * step(0.0, s);
                emission += _EmissionColor.rgb * (1.0 - charT);
                emission += _FlashColor.rgb * _FlashAmount;
                albedo = lerp(albedo, _FlashColor.rgb * 0.25, _FlashAmount * 0.6);

                float3 nWS = normalize(IN.normalWS);
                nWS = lerp(nWS, normalize(nWS + (crackV - 0.5) * 0.6), charT);

                InputData inputData = (InputData)0;
                inputData.positionWS = wp;
                inputData.normalWS = nWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(wp);
                inputData.shadowCoord = TransformWorldToShadowCoord(wp);
                inputData.fogCoord = InitializeInputDataFog(float4(wp, 1.0), 0);
                inputData.bakedGI = SampleSH(nWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData sd = (SurfaceData)0;
                sd.albedo = albedo;
                sd.metallic = _Metallic * (1.0 - charT);
                sd.smoothness = lerp(_Smoothness, 0.05, charT);
                sd.occlusion = 1;
                sd.emission = emission;
                sd.alpha = 1;
                half4 col = UniversalFragmentPBR(inputData, sd);
                col.rgb = MixFog(col.rgb, inputData.fogCoord);
                col.a = 1;
                return col;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                float3 wp = TransformObjectToWorld(IN.positionOS.xyz);
                float3 nw = TransformObjectToWorldNormal(IN.normalOS);
                wp += Curl(wp, nw);
                OUT.positionWS = wp;
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDir = normalize(_LightPosition - wp);
            #else
                float3 lightDir = _LightDirection;
            #endif
                float4 cs = TransformWorldToHClip(ApplyShadowBias(wp, nw, lightDir));
            #if UNITY_REVERSED_Z
                cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE * cs.w);
            #else
                cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE * cs.w);
            #endif
                OUT.positionCS = cs;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                BurnClip(IN.positionWS);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                float3 wp = TransformObjectToWorld(IN.positionOS.xyz);
                float3 nw = TransformObjectToWorldNormal(IN.normalOS);
                wp += Curl(wp, nw);
                OUT.positionWS = wp;
                OUT.positionCS = TransformWorldToHClip(wp);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                BurnClip(IN.positionWS);
                return 0;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
