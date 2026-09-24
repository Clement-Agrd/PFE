Shader "Skybox/Blended"
{
    Properties
    {
        [NoScaleOffset] _Tex ("Skybox 1 (Jour)", Cube) = "grey" {}
        [NoScaleOffset] _Tex2 ("Skybox 2 (Nuit)", Cube) = "grey" {}
        _Blend ("Blend (0 = Jour, 1 = Nuit)", Range(0, 1)) = 0
        _Exposure ("Exposure", Range(0, 8)) = 1
        _Rotation ("Rotation", Range(0, 360)) = 0
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURECUBE(_Tex);
            SAMPLER(sampler_Tex);
            TEXTURECUBE(_Tex2);
            SAMPLER(sampler_Tex2);

            CBUFFER_START(UnityPerMaterial)
                half _Blend;
                half _Exposure;
                float _Rotation;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 texcoord : TEXCOORD0;
            };

            float3 RotateAroundYInDegrees(float3 vertex, float degrees)
            {
                float alpha = degrees * PI / 180.0;
                float sina, cosa;
                sincos(alpha, sina, cosa);
                float2x2 m = float2x2(cosa, -sina, sina, cosa);
                return float3(mul(m, vertex.xz), vertex.y).xzy;
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 rotated = RotateAroundYInDegrees(v.positionOS.xyz, _Rotation);
                VertexPositionInputs positions = GetVertexPositionInputs(rotated);
                o.positionCS = positions.positionCS;
                o.texcoord = v.positionOS.xyz;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 day = SAMPLE_TEXTURECUBE(_Tex, sampler_Tex, i.texcoord);
                half4 night = SAMPLE_TEXTURECUBE(_Tex2, sampler_Tex2, i.texcoord);
                half4 result = lerp(day, night, _Blend);
                result.rgb *= _Exposure;
                return result;
            }
            ENDHLSL
        }
    }
}