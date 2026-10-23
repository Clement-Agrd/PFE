Shader "FireVFX/FireFlipbook"
{
    // Particules à flipbook (Texture Sheet Animation 8x8) avec frame blending.
    // Vertex streams requis : Position, Color, UV, UV2, AnimBlend  (posés par le builder).
    // Mode 0 = flamme / explosion : R = densité, G = température -> rampe corps noir HDR.
    // Mode 1 = fumée : RG = normale (espace vue), B = épaisseur, A = alpha ; éclairée par la lumière dominante,
    //                  l'ambiant et par la lueur du feu (_FireLightPos / _FireLightColor globales).
    // Mode 2 = étincelle : R = alpha, G = chaleur.
    // La couleur de particule : alpha = "vie" (enveloppe), rgb = teinte (albedo pour la fumée).
    Properties
    {
        _MainTex ("Flipbook", 2D) = "white" {}
        _Ramp ("Temperature Ramp", 2D) = "white" {}
        [KeywordEnum(Flame, Smoke, Spark)] _Mode ("Mode", Float) = 0
        _Intensity ("Intensity", Range(0, 8)) = 1
        _HDRBoost ("Core HDR Boost", Range(0, 12)) = 5
        _TempGain ("Temperature Gain", Range(0, 3)) = 1.15
        _TempBias ("Temperature Bias", Range(-1, 1)) = 0.0
        _Cut ("Alpha Cut", Range(0, 1)) = 0.12
        _Soft ("Softness", Range(0.01, 0.8)) = 0.35
        _Additive ("Additive Weight", Range(0, 1)) = 0.75
        _SoftDist ("Soft Particle Distance", Range(0.01, 3)) = 0.8
        _NearFade ("Near Camera Fade", Range(0, 5)) = 0.6
        [HDR] _SmokeAmbient ("Smoke Ambient", Color) = (0.17, 0.14, 0.13, 1)
        _SmokeLight ("Smoke Main Light", Range(0, 3)) = 0.8
        _SmokeFire ("Smoke Fire Light", Range(0, 8)) = 3.0
        _Tiles ("Sheet Tiles", Float) = 8
        _EdgeFade ("Frame Edge Fade", Range(0, 0.45)) = 0.2
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma shader_feature_local_fragment _MODE_FLAME _MODE_SMOKE _MODE_SPARK
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_Ramp); SAMPLER(sampler_Ramp);
            float4 _FireLightPos;     // xyz = position, w = portée
            float4 _FireLightColor;   // rgb = couleur * intensité

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half _Intensity, _HDRBoost, _TempGain, _TempBias, _Cut, _Soft, _Additive, _SoftDist, _NearFade;
                half4 _SmokeAmbient; half _SmokeLight, _SmokeFire, _Tiles, _EdgeFade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float4 uv : TEXCOORD0;      // xy = frame courante, zw = frame suivante
                float blend : TEXCOORD1;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float4 uv : TEXCOORD0;
                float blend : TEXCOORD1;
                float3 posWS : TEXCOORD2;
                float eye : TEXCOORD3;
            };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.posWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.posWS);
                o.color = i.color; o.uv = i.uv; o.blend = i.blend;
                o.eye = -TransformWorldToView(o.posWS).z;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv.xy);
                half4 b = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv.zw);
                // masque de bord de frame : la matière qui touche le bord du carré fond au lieu d être coupée net
                float2 la = frac(i.uv.xy * _Tiles), lb = frac(i.uv.zw * _Tiles);
                float ea = min(min(la.x, 1 - la.x), min(la.y, 1 - la.y)), eb = min(min(lb.x, 1 - lb.x), min(lb.y, 1 - lb.y));
                half edge = lerp(smoothstep(0, max(_EdgeFade, 1e-3), ea), smoothstep(0, max(_EdgeFade, 1e-3), eb), saturate(i.blend));
                half4 s = lerp(a, b, saturate(i.blend));
                half env = saturate(i.color.a);

                // fondu soft-particle + proximité caméra
                float2 suv = i.positionCS.xy / _ScaledScreenParams.xy;
                float sd = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                half fade = saturate((sd - i.eye) / _SoftDist) * saturate((i.eye - 0.15) / max(_NearFade, 0.01));

            #if defined(_MODE_SMOKE)
                half alpha = saturate(s.a * env * fade * edge);
                half3 nv = half3(s.r * 2 - 1, s.g * 2 - 1, 0); nv.z = sqrt(saturate(1 - dot(nv.xy, nv.xy)));
                float3 right = UNITY_MATRIX_V[0].xyz, up = UNITY_MATRIX_V[1].xyz, back = UNITY_MATRIX_V[2].xyz;
                float3 n = normalize(right * nv.x + up * nv.y + back * nv.z);

                Light ml = GetMainLight();
                half diffM = pow(saturate(dot(n, ml.direction) * 0.5 + 0.5), 1.6);
                float3 toF = _FireLightPos.xyz - i.posWS; float dF = length(toF) + 1e-3;
                half att = 1.0 / (1.0 + (dF * dF) / max(_FireLightPos.w * _FireLightPos.w * 0.12, 0.01));
                half diffF = saturate(dot(n, toF / dF) * 0.75 + 0.25) * att;
                half3 amb = SampleSH(n) * 0.6 + _SmokeAmbient.rgb;
                half3 lit = amb + ml.color * diffM * _SmokeLight + _FireLightColor.rgb * diffF * _SmokeFire;
                // ombre propre : la matière épaisse (B) s'assombrit côté opposé à la lumière
                lit *= lerp(1.0, 0.55, saturate(s.b) * (1 - diffM));
                half3 col = i.color.rgb * lit * _Intensity;
                return half4(col * alpha, alpha);
            #elif defined(_MODE_SPARK)
                half al = saturate(s.r * env * fade);
                half tI = saturate(s.g * 1.2 + _TempBias);
                half3 ramp = SAMPLE_TEXTURE2D(_Ramp, sampler_Ramp, float2(tI, 0.5)).rgb;
                half3 col = ramp * lerp(1.0, _HDRBoost, tI * tI) * i.color.rgb * _Intensity;
                return half4(col * al, 0);
            #else
                half dens = s.r, temp = s.g;
                // la vie fait baisser la température apparente : la flamme refroidit en mourant
                half heat = saturate(temp * _TempGain + dens * 0.2 + _TempBias - (1 - env) * 0.55);
                half cover = saturate(dens * (0.35 + env * 0.9));
                half al = smoothstep(_Cut, _Cut + _Soft, cover) * fade * lerp(1.0, edge, step(0.001, _EdgeFade));
                half3 ramp = SAMPLE_TEXTURE2D(_Ramp, sampler_Ramp, float2(heat, 0.5)).rgb;
                half3 col = ramp * lerp(0.8, _HDRBoost, heat * heat * heat) * i.color.rgb * _Intensity;
                // premultiplié : les bords sombres occultent un peu, le cœur est quasi additif
                return half4(col * al, al * (1 - _Additive));
            #endif
            }
            ENDHLSL
        }
    }
    Fallback Off
}
