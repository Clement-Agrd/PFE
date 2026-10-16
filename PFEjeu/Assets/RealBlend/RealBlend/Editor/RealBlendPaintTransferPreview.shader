Shader "Hidden/RealBlend/PaintTransferPreview"
{
    Properties
    {
        _Channel ("Channel (-1 RGB, 0 R, 1 G, 2 B, 3 A)", Float) = -1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            Cull Off
            ZWrite On
            ZTest LEqual
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float _Channel;
            struct Attributes { float4 vertex : POSITION; float4 color : COLOR; };
            struct Varyings { float4 position : SV_POSITION; float4 color : COLOR; };
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.color = input.color;
                return output;
            }
            float4 frag(Varyings input) : SV_Target
            {
                float3 value = saturate(input.color.rgb);
                if (_Channel >= 0.0)
                {
                    float channel = _Channel < 0.5 ? input.color.r :
                                    _Channel < 1.5 ? input.color.g :
                                    _Channel < 2.5 ? input.color.b : input.color.a;
                    value = saturate(channel).xxx;
                }
                return float4(value, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
