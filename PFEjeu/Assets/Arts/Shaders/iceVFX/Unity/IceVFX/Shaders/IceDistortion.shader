Shader "IceVFX/IceDistortion"
{
    // Distorsion de particule URP : déforme _CameraOpaqueTexture avec une normal map animée.
    // Force = alpha de la particule (vertex color) x alpha du masque. Requiert Opaque Texture.
    Properties
    {
        _MainTex ("Mask (alpha)", 2D) = "white" {}
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _Strength ("Strength", Range(0, 0.3)) = 0.03
        _Tiling ("Normal Tiling", Float) = 1.5
        _Scroll ("Scroll Speed (xy)", Vector) = (0.12, -0.2, 0, 0)
        _Radial ("Radial Push (0 normal map, 1 ring)", Range(0, 1)) = 0
        _Tint ("Tint", Color) = (0.93, 0.98, 1, 1)
    }

    SubShader
    {
        // dessiné avant les autres transparents : ne déforme que l'opaque, sans effacer la brume
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-50" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

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
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half _Strength; half _Tiling; float4 _Scroll; half _Radial; half4 _Tint;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.uv = i.uv;
                o.color = i.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half mask = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a * i.color.a;
                float2 nuv = i.uv * _Tiling + _Time.y * _Scroll.xy;
                half3 nrm = UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, nuv));

                // poussée radiale (anneau) mêlée au bruit de la normal map
                float2 rad = (i.uv - 0.5) * 2.0;
                float2 dir = lerp(nrm.xy, normalize(rad + 1e-4) * (0.7 + 0.5 * nrm.x), _Radial);

                float2 suv = i.positionCS.xy / _ScaledScreenParams.xy;
                float2 duv = dir * _Strength * mask;
                half3 scene = SampleSceneColor(clamp(suv + duv, 0.001, 0.999));
                // léger voile froid proportionnel à l'intensité
                scene = lerp(scene, scene * _Tint.rgb, saturate(mask * 2.0));
                return half4(scene, saturate(mask * 4.0));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
