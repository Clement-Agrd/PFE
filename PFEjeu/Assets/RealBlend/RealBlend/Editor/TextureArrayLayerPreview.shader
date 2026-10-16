Shader "Hidden/RealBlend/Texture Array Layer Preview"
{
    Properties
    {
        _SourceArray ("Source Array", 2DArray) = "" {}
        _Slice ("Slice", Float) = 0
    }

    CGINCLUDE
    #include "UnityCG.cginc"

    UNITY_DECLARE_TEX2DARRAY(_SourceArray);
    float _Slice;

    fixed4 frag(v2f_img input) : SV_Target
    {
        return UNITY_SAMPLE_TEX2DARRAY(_SourceArray, float3(input.uv, _Slice));
    }
    ENDCG

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma require 2darray
            #pragma vertex vert_img
            #pragma fragment frag
            ENDCG
        }
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="HDRenderPipeline" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma require 2darray
            #pragma vertex vert_img
            #pragma fragment frag
            ENDCG
        }
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma require 2darray
            #pragma vertex vert_img
            #pragma fragment frag
            ENDCG
        }
    }

    Fallback Off
}
