// Rayons de soleil (god rays) en espace écran, pour URP — à brancher dans un
// Fullscreen Pass Renderer Feature. Marche radiale depuis la position écran du
// soleil : seul le ciel (depth = far) émet de la lumière, tout le reste occulte,
// donc les arbres / bâtiments découpent des faisceaux dans la brume.
// Direction et couleur viennent de la lumière principale URP (aucun script).
Shader "Hidden/Village/SunShafts"
{
    Properties
    {
        _Intensity ("Intensité", Range(0, 4)) = 1.2
        _Threshold ("Seuil de luminosité du ciel", Range(0, 2)) = 0.35
        _Density ("Longueur des rayons", Range(0.1, 1)) = 0.9
        _Decay ("Atténuation", Range(0.8, 1)) = 0.965
        _Tint ("Teinte", Color) = (1, 0.86, 0.62, 1)
        _FogBoost ("Influence de la brume", Range(0, 1)) = 0.6
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "SunShafts"
            ZTest Always ZWrite Off Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            #define SAMPLES 96

            float _Intensity;
            float _Threshold;
            float _Density;
            float _Decay;
            float4 _Tint;
            float _FogBoost;

            float IsSky(float2 uv)
            {
                float d = SampleSceneDepth(uv);
            #if UNITY_REVERSED_Z
                return d <= 0.00001 ? 1.0 : 0.0;
            #else
                return d >= 0.99999 ? 1.0 : 0.0;
            #endif
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 scene = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

                // Position écran du soleil (la lumière principale est directionnelle).
                float3 sunWorld = _WorldSpaceCameraPos + _MainLightPosition.xyz * 1000.0;
                float4 sunClip = TransformWorldToHClip(sunWorld);
                if (sunClip.w <= 0.0) return scene; // soleil derrière la caméra

                float2 sunUV = sunClip.xy / sunClip.w * 0.5 + 0.5;
            #if UNITY_UV_STARTS_AT_TOP
                sunUV.y = 1.0 - sunUV.y;
            #endif

                // Marche radiale du pixel vers le soleil, en accumulant la lumière du ciel visible.
                float2 delta = (uv - sunUV) * (_Density / SAMPLES);
                // Décalage aléatoire par pixel : casse le banding des 56 pas réguliers.
                // Bruit à gradient entrelacé : bien plus fin et régulier qu'un hash aléatoire.
                float jitter = frac(52.9829189 * frac(dot(floor(uv * _ScreenParams.xy), float2(0.06711056, 0.00583715))));
                float2 suv = uv - delta * jitter;
                float illumination = 1.0;
                float accum = 0.0;

                [loop]
                for (int i = 0; i < SAMPLES; i++)
                {
                    suv -= delta;
                    float sky = IsSky(suv);
                    float3 c = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, suv).rgb;
                    float lum = max(0.0, dot(c, float3(0.299, 0.587, 0.114)) - _Threshold);
                    accum += lum * sky * illumination;
                    illumination *= _Decay;
                }
                accum /= SAMPLES;

                // Les rayons s'estompent quand le soleil sort du cadre.
                float edge = saturate(1.8 - length(sunUV - 0.5) * 1.6);

                // Plus un pixel est lointain / dans la brume, plus le faisceau y est visible.
                float d = SampleSceneDepth(uv);
                float linearDepth = Linear01Depth(d, _ZBufferParams);
                float haze = lerp(1.0, saturate(linearDepth * 6.0), _FogBoost);

                float3 shafts = accum * _Intensity * edge * haze * _MainLightColor.rgb * _Tint.rgb;
                return half4(scene.rgb + shafts, scene.a);
            }
            ENDHLSL
        }
    }
}
