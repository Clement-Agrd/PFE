Shader "Hidden/RealBlend/MaskPacker"
{
    CGINCLUDE
    #include "UnityCG.cginc"

    sampler2D _MetallicTex;
    sampler2D _AOTex;
    sampler2D _SurfaceTex;
    sampler2D _BlueTex;
    float4 _Defaults;
    float4 _HasTextures;
    float _InvertSurface;

    fixed4 frag(v2f_img input) : SV_Target
    {
        float metallic = lerp(_Defaults.r, tex2D(_MetallicTex, input.uv).r, _HasTextures.r);
        float ao = lerp(_Defaults.g, tex2D(_AOTex, input.uv).r, _HasTextures.g);
        float blue = lerp(_Defaults.b, tex2D(_BlueTex, input.uv).r, _HasTextures.b);
        float surface = tex2D(_SurfaceTex, input.uv).r;
        surface = lerp(surface, 1.0 - surface, _InvertSurface);
        float smoothness = lerp(_Defaults.a, surface, _HasTextures.a);
        return saturate(float4(metallic, ao, blue, smoothness));
    }
    ENDCG

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma editor_sync_compilation
            #pragma vertex vert_img
            #pragma fragment frag
            ENDCG
        }
    }

    SubShader
    {
        Tags { "RenderPipeline"="HDRenderPipeline" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma editor_sync_compilation
            #pragma vertex vert_img
            #pragma fragment frag
            ENDCG
        }
    }

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma editor_sync_compilation
            #pragma vertex vert_img
            #pragma fragment frag
            ENDCG
        }
    }

    Fallback Off
}
