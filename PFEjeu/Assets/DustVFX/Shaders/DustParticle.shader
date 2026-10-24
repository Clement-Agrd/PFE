// Flipbook dust: frame blending, soft particles, near-camera fade, noise-threshold torn edges,
// main-light + ambient lighting from the flipbook normal, surface tint via vertex colour.
// Vertex streams (set by DustStep): Position, Normal, Tangent, Color, UV, UV2, AnimBlend, AgePercent, StableRandomX.
Shader "VFX/DustParticle"
{
    Properties
    {
        [MainTexture] _MainTex("Flipbook (R dens, G dissipation, BA normal)", 2D) = "white" {}
        _BaseColor("Tint", Color) = (1, 1, 1, 1)
        _Alpha("Max Alpha", Range(0, 1)) = 0.85
        _Density("Density Gain", Range(0, 3)) = 1
        _Erosion("Edge Tear (by age)", Range(0, 2)) = 0.9
        _EdgeSoft("Edge Softness", Range(0.02, 0.6)) = 0.18
        _FadeIn("Fade In (age)", Range(0.005, 0.5)) = 0.05
        _FadeOut("Fade Out Start (age)", Range(0, 1)) = 0.45
        _NormalStrength("Normal Strength", Range(0, 1.5)) = 1
        _LightMul("Main Light", Range(0, 2)) = 1
        _AmbientMul("Ambient", Range(0, 2)) = 1
        _Scatter("Forward Scatter", Range(0, 3)) = 1
        _SoftDistance("Soft Particle Distance", Range(0.02, 3)) = 0.5
        _NearFadeStart("Near Fade Start", Range(0, 3)) = 0.35
        _NearFadeEnd("Near Fade End", Range(0, 5)) = 1.1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        Pass
        {
            Name "DustForward"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _BaseColor;
                float _Alpha;
                float _Density;
                float _Erosion;
                float _EdgeSoft;
                float _FadeIn;
                float _FadeOut;
                float _NormalStrength;
                float _LightMul;
                float _AmbientMul;
                float _Scatter;
                float _SoftDistance;
                float _NearFadeStart;
                float _NearFadeEnd;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float4 color      : COLOR;
                float4 uv         : TEXCOORD0; // xy frame A, zw frame B
                float3 misc       : TEXCOORD1; // x blend, y age, z random
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color      : COLOR;
                float4 uv         : TEXCOORD0;
                float3 misc       : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float3 normalWS   : TEXCOORD3;
                float4 tangentWS  : TEXCOORD4;
                float  eyeDepth   : TEXCOORD5;
            };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(i.positionOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldDir(i.normalOS);
                o.tangentWS = float4(TransformObjectToWorldDir(i.tangentOS.xyz), i.tangentOS.w);
                o.color = i.color;
                o.uv = i.uv;
                o.misc = i.misc;
                o.eyeDepth = -TransformWorldToView(o.positionWS).z;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float4 t = lerp(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv.xy),
                                SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv.zw), saturate(i.misc.x));
                float age = saturate(i.misc.y);
                float rnd = frac(i.misc.z * 7.31 + 0.17);

                // torn edges: low-density / high-dissipation pixels vanish first as the particle ages
                float dens = t.r * _Density;
                float threshold = age * _Erosion * (0.25 + t.g * 0.9) + rnd * 0.04;
                float cover = saturate((dens - threshold) / _EdgeSoft);

                float fadeIn = saturate(age / _FadeIn);
                float fadeOut = 1.0 - smoothstep(_FadeOut, 1.0, age);

                // soft particles + near-camera fade
                float2 suv = i.positionCS.xy / _ScaledScreenParams.xy;
                float sceneDepth = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float soft = saturate((sceneDepth - i.eyeDepth) / _SoftDistance);
                float nearFade = saturate((i.eyeDepth - _NearFadeStart) / max(0.001, _NearFadeEnd - _NearFadeStart));

                float alpha = cover * i.color.a * _Alpha * fadeIn * fadeOut * soft * nearFade;

                // lighting from the baked normal expressed in the billboard frame
                float3 N = normalize(i.normalWS);
                float3 T = normalize(i.tangentWS.xyz);
                float3 B = cross(N, T) * i.tangentWS.w;
                float2 nxy = (t.ba * 2.0 - 1.0) * _NormalStrength;
                float nz = sqrt(saturate(1.0 - dot(nxy, nxy)));
                float3 n = normalize(T * nxy.x + B * nxy.y + N * nz);

                Light l = GetMainLight();
                float3 V = normalize(GetCameraPositionWS() - i.positionWS);
                float ndl = dot(n, l.direction);
                float diffuse = saturate(ndl * 0.5 + 0.5);
                diffuse *= diffuse;
                // light shining through the cloud toward the camera (grazing / back light glow)
                float scatter = pow(saturate(dot(V, -l.direction)), 3.0) * (0.4 + 0.6 * (1.0 - t.r));
                float3 ambient = SampleSH(n) * _AmbientMul;
                float3 light = ambient + l.color * (diffuse + scatter * _Scatter) * _LightMul;

                // thick cores are a little darker (self shadowing)
                float core = lerp(1.0, 0.8, saturate(t.r));
                float3 rgb = i.color.rgb * _BaseColor.rgb * light * core;
                return half4(rgb, alpha * _BaseColor.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
