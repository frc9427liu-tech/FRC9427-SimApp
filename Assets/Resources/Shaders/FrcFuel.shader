Shader "FrcSim/Fuel"
{
    Properties
    {
        _Color ("Color", Color) = (0.98,0.80,0.04,1)
        _Glossiness ("Smoothness", Range(0,1)) = 0.38
        _Metallic ("Metallic", Range(0,1)) = 0
        _RimColor ("Rim color", Color) = (1,0.92,0.45,1)
        _RimPower ("Rim power", Float) = 2.5
        _RimAmt ("Rim amount", Range(0,1)) = 0.22
        _Emit ("Emissive floor", Range(0,0.3)) = 0.05
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow
        #pragma multi_compile_instancing
        #pragma target 3.0
        fixed4 _Color, _RimColor; half _Glossiness, _Metallic, _RimPower, _RimAmt, _Emit;
        struct Input { float3 worldPos; float3 worldNormal; };
        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            float3 v = normalize(_WorldSpaceCameraPos - IN.worldPos);
            float f = pow(1.0 - saturate(dot(v, normalize(IN.worldNormal))), _RimPower);
            o.Albedo = _Color.rgb;
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
            o.Emission = _RimColor.rgb * f * _RimAmt + _Color.rgb * _Emit;
        }
        ENDCG
    }
    FallBack "Standard"
}