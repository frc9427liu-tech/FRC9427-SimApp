Shader "FrcSim/Glass"
{
    Properties
    {
        _Color ("Tint", Color) = (0.75,0.88,1,1)
        _Alpha ("Base alpha", Range(0,1)) = 0.10
        _Fresnel ("Fresnel alpha", Range(0,1)) = 0.45
        _Smooth ("Smoothness", Range(0,1)) = 0.92
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Cull Off ZWrite Off
        CGPROGRAM
        #pragma surface surf Standard alpha:premul
        #pragma target 3.0
        fixed4 _Color; float _Alpha, _Fresnel, _Smooth;
        struct Input { float3 worldPos; float3 worldNormal; };
        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            float3 v = normalize(_WorldSpaceCameraPos - IN.worldPos);
            float f = pow(1.0 - saturate(abs(dot(v, normalize(IN.worldNormal)))), 3.0);
            o.Albedo = _Color.rgb; o.Metallic = 0; o.Smoothness = _Smooth;
            o.Alpha = saturate(_Alpha + f * _Fresnel);
        }
        ENDCG
    }
}