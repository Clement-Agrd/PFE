// BuildVFX/BuildReveal : révélation du bâtiment de bas en haut (hauteur monde), bord incandescent couleur d'équipe,
// coupe intérieure sombre (faces arrière). URP, ombres reçues et projetées avec le même clip.
Shader "BuildVFX/BuildReveal"
{
    Properties
    {
        _BaseMap("Base Map", 2D) = "white" {}
        _BaseColor("Base Color", Color) = (1,1,1,1)
        [HDR] _EdgeColor("Edge Color", Color) = (1,0.55,0.12,1)
        _RevealY("Reveal Y (world)", Float) = -1000
        _EdgeWidth("Edge Width", Float) = 0.7
        _EdgeGlow("Edge Glow", Float) = 4
        _NoiseScale("Noise Scale", Float) = 1.6
        _NoiseAmp("Noise Amp", Float) = 0.35
        _Ambient("Ambient", Range(0,1)) = 1
        _BumpMap("Normal Map", 2D) = "bump" {}
        _BumpScale("Normal Scale", Float) = 1
        _EmissionMap("Emission Map", 2D) = "white" {}
        [HDR] _EmissionColor("Emission Color", Color) = (0,0,0,1)
        [Toggle] _AlphaClip("Alpha Clip", Float) = 0
        _Cutoff("Cutoff", Range(0,1)) = 0.5
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
        TEXTURE2D(_EmissionMap); SAMPLER(sampler_EmissionMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST; half4 _BaseColor; half4 _EdgeColor;
            float _RevealY, _EdgeWidth, _EdgeGlow, _NoiseScale, _NoiseAmp, _Ambient, _BumpScale, _AlphaClip, _Cutoff; half4 _EmissionColor;
        CBUFFER_END
        float h31(float3 p) { p = frac(p * 0.3183099 + 0.1); p *= 17.0; return frac(p.x * p.y * p.z * (p.x + p.y + p.z)); }
        float vnoise(float3 x)
        {
            float3 i = floor(x), f = frac(x); f = f * f * (3 - 2 * f);
            return lerp(lerp(lerp(h31(i), h31(i + float3(1,0,0)), f.x), lerp(h31(i + float3(0,1,0)), h31(i + float3(1,1,0)), f.x), f.y),
                        lerp(lerp(h31(i + float3(0,0,1)), h31(i + float3(1,0,1)), f.x), lerp(h31(i + float3(0,1,1)), h31(i + float3(1,1,1)), f.x), f.y), f.z);
        }
        // > 0 : visible. Distance verticale sous le front de révélation, bruitée.
        float RevealDepth(float3 wp) { return _RevealY + (vnoise(wp * _NoiseScale) * 2 - 1) * _NoiseAmp - wp.y; }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attr { float4 pos : POSITION; float3 nrm : NORMAL; float4 tan : TANGENT; float2 uv : TEXCOORD0; };
            struct Vary { float4 cs : SV_POSITION; float3 wp : TEXCOORD0; float3 wn : TEXCOORD1; float2 uv : TEXCOORD2; float4 wt : TEXCOORD3; };

            Vary vert(Attr i)
            {
                Vary o; VertexPositionInputs p = GetVertexPositionInputs(i.pos.xyz);
                o.cs = p.positionCS; o.wp = p.positionWS; o.wn = TransformObjectToWorldNormal(i.nrm); o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                o.wt = float4(TransformObjectToWorldDir(i.tan.xyz), i.tan.w * GetOddNegativeScale());
                return o;
            }
            half4 frag(Vary i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                float d = RevealDepth(i.wp); clip(d);
                float edge = 1 - saturate(d / max(_EdgeWidth, 0.001));
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                if (_AlphaClip > 0.5) clip(tex.a * _BaseColor.a - _Cutoff);
                half3 albedo = tex.rgb * _BaseColor.rgb;
                bool front = IS_FRONT_VFACE(face, true, false);
                half3 nt = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, i.uv), _BumpScale);
                float3 gn = normalize(i.wn), gt = normalize(i.wt.xyz), gb = cross(gn, gt) * i.wt.w;
                half3 n = normalize(nt.x * gt + nt.y * gb + nt.z * gn) * (front ? 1 : -1);
                Light l = GetMainLight(TransformWorldToShadowCoord(i.wp));
                half3 lit = l.color * l.shadowAttenuation * saturate(dot(n, l.direction)) + SampleSH(n) * _Ambient;
                half3 col = albedo * lit;
                half e = pow(edge, 2.5), hot = pow(edge, 8);
                half3 glow = lerp(_EdgeColor.rgb, half3(1,1,1), hot) * e * _EdgeGlow;
                if (!front) col = albedo * 0.12 + _EdgeColor.rgb * _EdgeGlow * 0.12 * (0.4 + edge);
                else col *= 1 - 0.45 * pow(edge, 1.5);
                half3 em = front ? SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, i.uv).rgb * _EmissionColor.rgb * (1 - edge) : 0;
                return half4(col + glow + em, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection; float3 _LightPosition;
            struct Attr { float4 pos : POSITION; float3 nrm : NORMAL; float2 uv : TEXCOORD0; };
            struct Vary { float4 cs : SV_POSITION; float3 wp : TEXCOORD0; float2 uv : TEXCOORD1; };
            Vary vert(Attr i)
            {
                Vary o; float3 wp = TransformObjectToWorld(i.pos.xyz); float3 wn = TransformObjectToWorldNormal(i.nrm);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 ld = normalize(_LightPosition - wp);
                #else
                    float3 ld = _LightDirection;
                #endif
                o.cs = TransformWorldToHClip(ApplyShadowBias(wp, wn, ld)); o.wp = wp; o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                #if UNITY_REVERSED_Z
                    o.cs.z = min(o.cs.z, o.cs.w * UNITY_NEAR_CLIP_VALUE);
                #else
                    o.cs.z = max(o.cs.z, o.cs.w * UNITY_NEAR_CLIP_VALUE);
                #endif
                return o;
            }
            half4 frag(Vary i) : SV_Target { clip(RevealDepth(i.wp)); if (_AlphaClip > 0.5) clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a * _BaseColor.a - _Cutoff); return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask 0 Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct Attr { float4 pos : POSITION; float2 uv : TEXCOORD0; };
            struct Vary { float4 cs : SV_POSITION; float3 wp : TEXCOORD0; float2 uv : TEXCOORD1; };
            Vary vert(Attr i) { Vary o; VertexPositionInputs p = GetVertexPositionInputs(i.pos.xyz); o.cs = p.positionCS; o.wp = p.positionWS; o.uv = TRANSFORM_TEX(i.uv, _BaseMap); return o; }
            half4 frag(Vary i) : SV_Target { clip(RevealDepth(i.wp)); if (_AlphaClip > 0.5) clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a * _BaseColor.a - _Cutoff); return 0; }
            ENDHLSL
        }
    }
    FallBack Off
}
