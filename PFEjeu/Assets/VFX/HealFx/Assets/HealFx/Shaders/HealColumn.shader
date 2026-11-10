// Colonne de lumière douce de la zone de soin (URP, additif).
// Mesh = tube ouvert de rayon 1 et de hauteur 1 (base en y = 0), mis à l'échelle par HealZoneFx.
// Fondu : vertical (vif en bas, s'efface en hauteur), sur les bords (Fresnel inversé, pas de contour dur), et contre la géométrie (soft depth).
Shader "HealFx/Column"
{
    Properties
    {
        _ColorA ("Couleur principale (haut)", Color) = (0.30, 1.00, 0.50, 1)
        [HDR] _ColorCore ("Couleur du coeur (base, HDR)", Color) = (2.2, 1.9, 1.1, 1)
        _Intensity ("Intensité", Range(0, 4)) = 1.2
        _Fade ("Fondu global 0..1 - piloté par script", Range(0, 1.5)) = 1
        _HealTime ("Temps de l'effet (s) - piloté par script", Float) = 0
        _SoftDistance ("Distance de fondu à la géométrie (m)", Range(0.05, 3)) = 0.8
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+1" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "HealColumn"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ColorA;
                float4 _ColorCore;
                float  _Intensity;
                float  _Fade;
                float  _HealTime;
                float  _SoftDistance;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                float4 screenPos  : TEXCOORD3;
                float  eyeDepth   : TEXCOORD4;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.uv = input.uv;
                o.screenPos = ComputeScreenPos(o.positionCS);
                o.eyeDepth = -TransformWorldToView(o.positionWS).z;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float u = i.uv.x;
                float v = i.uv.y;
                float t = _HealTime;

                // bords transparents : on ne voit jamais le contour du tube
                float3 viewDir = normalize(GetWorldSpaceViewDir(i.positionWS));
                float edge = pow(abs(dot(normalize(i.normalWS), viewDir)), 1.6);

                // vif à la base, s'efface en hauteur
                float vertical = pow(saturate(1.0 - v), 1.8) * smoothstep(0.0, 0.04, v);

                // filets de lumière qui montent, à deux fréquences
                float streakA = 0.62 + 0.38 * sin(u * 6.2831853 * 9.0 + t * 0.9 + sin(v * 7.0 - t * 1.3) * 1.6);
                float streakB = 0.70 + 0.30 * sin(u * 6.2831853 * 5.0 - t * 0.5 + v * 4.0);

                float a = edge * vertical * streakA * streakB;

                // soft depth : pas d'arête là où la colonne coupe le sol ou un mur
                float2 screenUV = i.screenPos.xy / i.screenPos.w;
                float sceneEye = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                a *= saturate((sceneEye - i.eyeDepth) / _SoftDistance);

                float3 col = lerp(_ColorCore.rgb, _ColorA.rgb, saturate(v * 1.4)) * a;
                return half4(col * _Intensity * _Fade, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
