Shader "FireVFX/FireBall"
{
    // URP, unlit. Sphère de feu procédurale : bruit 3D à domain-warp, rampe de chaleur, queue étirée (-Z local), dissolve.
    // Le mesh doit avoir son avant en +Z (rayon 0.5 pour la sphère Unity).
    Properties
    {
        [HDR] _CoreColor ("Core (hot)", Color) = (2.6, 1.7, 0.8, 1)
        [HDR] _MidColor ("Mid", Color) = (2.0, 0.7, 0.1, 1)
        [HDR] _EdgeColor ("Edge (cool)", Color) = (0.9, 0.12, 0.02, 1)
        _Intensity ("Intensity", Range(0, 4)) = 1
        _NoiseScale ("Noise Scale", Range(0.5, 8)) = 2.4
        _Speed ("Flow Speed", Range(0, 8)) = 2.2
        _Warp ("Domain Warp", Range(0, 2)) = 0.9
        _Stretch ("Tail Stretch", Range(0, 3)) = 0.9
        _Displace ("Vertex Displace", Range(0, 0.4)) = 0.12
        _CoreBias ("Core Bias", Range(0, 2)) = 1.0
        _Cut ("Alpha Cut", Range(0, 1)) = 0.3
        _Soft ("Edge Softness", Range(0.01, 0.6)) = 0.2
        _Dissolve ("Dissolve", Range(0, 1)) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _CoreColor; half4 _MidColor; half4 _EdgeColor;
                half _Intensity; half _NoiseScale; half _Speed; half _Warp; half _Stretch;
                half _Displace; half _CoreBias; half _Cut; half _Soft; half _Dissolve;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 posOS : TEXCOORD0;
                float3 nrmWS : TEXCOORD1;
                float3 viewWS : TEXCOORD2;
                float back : TEXCOORD3;
            };

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
            float fbm(float3 p) { return 0.5 * vnoise(p) + 0.27 * vnoise(p * 2.03 + 5.2) + 0.15 * vnoise(p * 4.1 + 9.7) + 0.08 * vnoise(p * 8.3 + 1.3); }

            Varyings vert(Attributes i)
            {
                Varyings o;
                float3 p = i.positionOS.xyz;
                float back = saturate(0.5 - p.z);
                float t = _Time.y * _Speed;
                float n = fbm(p * _NoiseScale * 2.0 + float3(0, 0, t));
                p += i.normalOS * (n - 0.5) * _Displace * 2.0;
                p.z -= back * back * _Stretch * (0.5 + n);
                p.xy *= 1.0 - back * 0.35;
                o.posOS = i.positionOS.xyz;
                o.back = back;
                float3 wp = TransformObjectToWorld(p);
                o.positionCS = TransformWorldToHClip(wp);
                o.nrmWS = TransformObjectToWorldNormal(i.normalOS);
                o.viewWS = GetWorldSpaceViewDir(wp);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 q = i.posOS * _NoiseScale * 2.0;
                q.z += _Time.y * _Speed;
                float3 w = float3(vnoise(q * 0.8 + 3.1), vnoise(q * 0.8 + 7.7), vnoise(q * 0.8 + 11.3)) - 0.5;
                float n = fbm(q + w * _Warp * 2.0);
                float ndv = saturate(dot(normalize(i.nrmWS), normalize(i.viewWS)));
                float heat = n * 0.95 + pow(ndv, 2.0) * _CoreBias * 0.55 - i.back * 0.28 - _Dissolve * 1.3;

                half3 col = lerp(_EdgeColor.rgb, _MidColor.rgb, smoothstep(0.22, 0.55, heat));
                col = lerp(col, _CoreColor.rgb, smoothstep(0.55, 0.98, heat));
                float a = smoothstep(_Cut, _Cut + _Soft, heat);
                a *= saturate(1.0 - i.back * 0.55 * (1.0 - n));
                return half4(col * _Intensity, a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
