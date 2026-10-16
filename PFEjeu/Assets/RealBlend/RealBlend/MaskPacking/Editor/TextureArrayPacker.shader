Shader "Hidden/RealBlend/Texture Array Packer"
{
    Properties
    {
        _Source ("Source", 2D) = "white" {}
        _Fallback ("Fallback", Color) = (1,1,1,1)
        _HasSource ("Has Source", Float) = 1
        _Mode ("Mode", Float) = 0
        _FlipNormalGreen ("Flip Normal Green", Float) = 0
    }

    CGINCLUDE
    #include "UnityCG.cginc"

    struct appdata
    {
        float4 vertex : POSITION;
        float2 uv : TEXCOORD0;
    };

    struct v2f
    {
        float4 vertex : SV_POSITION;
        float2 uv : TEXCOORD0;
    };

    sampler2D _Source;
    float4 _Fallback;
    float _HasSource;
    float _Mode;
    float _FlipNormalGreen;

    v2f vert(appdata input)
    {
        v2f output;
        output.vertex = UnityObjectToClipPos(input.vertex);
        output.uv = input.uv;
        return output;
    }

    float4 frag(v2f input) : SV_Target
    {
        if (_HasSource < 0.5)
            return _Fallback;

        float4 sampled = tex2D(_Source, input.uv);
        if (_Mode > 0.5 && _Mode < 1.5)
        {
            float3 normal = normalize(UnpackNormal(sampled));
            if (_FlipNormalGreen > 0.5)
                normal.y = -normal.y;
            return float4(normal * 0.5 + 0.5, 1.0);
        }

        return sampled;
    }
    ENDCG

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
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
            #pragma vertex vert
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
            #pragma vertex vert
            #pragma fragment frag
            ENDCG
        }
    }

    Fallback Off
}
