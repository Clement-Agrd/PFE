// Particule additive pour les croix / étincelles / anneaux / volutes de soin (URP).
// Couleur = couleur de particule (vertex color) ; forme = canal R de la texture ; _Intensity piloté par HealZoneFx / HealTargetFx.
// v2 : soft particles (Depth Texture, _SoftDistance) et érosion avec l'âge pour les volutes (_Erosion, bruit dans le canal G).
//      Le flipbook 8x8 est géré par le module Texture Sheet Animation du Particle System.
Shader "HealFx/Particle"
{
    Properties
    {
        _MainTex ("Masque (R = forme, G = bruit d'érosion)", 2D) = "white" {}
        _Intensity ("Intensité", Range(0, 4)) = 1.2
        _SoftDistance ("Distance de fondu à la géométrie (m, 0 = désactivé)", Range(0, 3)) = 0
        _Erosion ("Érosion avec l'âge (0 = aucune)", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+2" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "HealParticle"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float  _Intensity;
                float  _SoftDistance;
                float  _Erosion;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
                float4 screenPos  : TEXCOORD1;
                float  eyeDepth   : TEXCOORD2;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(positionWS);
                o.color = input.color;
                o.uv = TRANSFORM_TEX(input.uv, _MainTex);
                o.screenPos = ComputeScreenPos(o.positionCS);
                o.eyeDepth = -TransformWorldToView(positionWS).z;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                float mask = tex.r;

                // Érosion : le seuil monte avec l'âge (alpha vertex color qui descend) -> la volute se déchire au lieu de simplement s'estomper.
                if (_Erosion > 0.001)
                {
                    float age = 1.0 - i.color.a;
                    float threshold = _Erosion * age;
                    float broken = saturate(mask * 1.6) * lerp(1.0, tex.g * 1.8, saturate(age * 1.5));
                    mask = smoothstep(threshold, threshold + 0.35, broken);
                }

                float alpha = mask * i.color.a;

                // Soft particles : fondu progressif là où la particule rencontre le sol ou un mur.
                if (_SoftDistance > 0.001)
                {
                    float2 screenUV = i.screenPos.xy / i.screenPos.w;
                    float sceneEye = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                    alpha *= saturate((sceneEye - i.eyeDepth) / _SoftDistance);
                }

                return half4(i.color.rgb * _Intensity, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
