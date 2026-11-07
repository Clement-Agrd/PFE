// TEST : ruban d'aurore boreale. Additif, sans eclairage, anime par du bruit de valeur (rayons verticaux qui ondulent).
// Les UV du ruban : u = position le long du ruban, v = 0 en bas (bord brillant) -> 1 en haut (fondu).
Shader "Test/AuroraRibbon"
{
    Properties
    {
        _ColorBottom ("Couleur bas", Color) = (0.25, 1, 0.55, 1)
        _ColorTop ("Couleur haut", Color) = (0.45, 0.35, 1, 1)
        _Intensity ("Intensite", Range(0, 3)) = 0.4
        _Speed ("Vitesse", Float) = 0.05
        _RayScale ("Echelle des rayons", Float) = 18
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+20" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Aurora"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ColorBottom;
                half4 _ColorTop;
                float _Intensity;
                float _Speed;
                float _RayScale;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x),
                            lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), f.x), f.y);
            }

            half4 frag(Varyings input) : SV_Target
            {
                float t = _Time.y * _Speed;
                float v = input.uv.y;

                // Rayons verticaux : bruit le long du ruban, qui derive lentement.
                float rays = ValueNoise(float2(input.uv.x * _RayScale + t * 3.0, t));
                rays = pow(saturate(rays * 1.5 - 0.25), 1.4);

                // Respiration lente a plus grande echelle.
                float shimmer = ValueNoise(float2(input.uv.x * _RayScale * 0.25 - t * 2.0, v * 3.0 + t));

                // Bord bas brillant, fondu progressif vers le haut.
                float fade = pow(saturate(1.0 - v), 1.6) * smoothstep(0.0, 0.07, v);

                float alpha = rays * fade * (0.55 + 0.9 * shimmer) * _Intensity;
                half3 color = lerp(_ColorBottom.rgb, _ColorTop.rgb, saturate(v * 1.25));
                return half4(color, saturate(alpha));
            }
            ENDHLSL
        }
    }
}
