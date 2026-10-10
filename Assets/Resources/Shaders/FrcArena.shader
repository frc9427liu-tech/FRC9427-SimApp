Shader "FrcSim/Arena"
{
    Properties
    {
        _Color ("Color", Color) = (0.2,0.21,0.25,1)
        _Bottom ("Bottom multiplier", Range(0,1.5)) = 0.55
        _Top ("Top multiplier", Range(0,1.5)) = 1.0
        _Height ("Gradient height (m)", Float) = 9
        _PanelW ("Panel width (m)", Float) = 2.4
        _SeamAmt ("Seam darkness", Range(0,0.5)) = 0.12
        _Smooth ("Smoothness", Range(0,1)) = 0.25
        [HDR] _Accent ("Accent ribbon color", Color) = (0,0,0,1)
        _AccentY ("Ribbon height (m)", Float) = 6.5
        _AccentH ("Ribbon half height (m)", Float) = 0.07
        _AccentI ("Ribbon intensity", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0
        fixed4 _Color; float _Bottom,_Top,_Height,_PanelW,_SeamAmt,_Smooth,_AccentY,_AccentH,_AccentI; fixed4 _Accent;
        struct Input { float3 worldPos; float3 worldNormal; };
        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            float3 wp = IN.worldPos; float3 n = abs(normalize(IN.worldNormal));
            float vertical = step(n.y, 0.5);                       // 1 on walls, 0 on floors/tops
            float k = lerp(_Bottom, _Top, saturate(wp.y / _Height));
            float along = n.x > n.z ? wp.z : wp.x;
            float seam = abs(frac(along / _PanelW + 0.5) - 0.5);   // distance to nearest seam, in panels
            float sl = 1.0 - smoothstep(0.0, 0.03 / _PanelW, seam);
            k *= 1.0 - _SeamAmt * sl * vertical;
            o.Albedo = _Color.rgb * k;
            o.Metallic = 0; o.Smoothness = _Smooth;
            float a = (1.0 - smoothstep(_AccentH * 0.6, _AccentH, abs(wp.y - _AccentY))) * vertical;
            o.Emission = _Accent.rgb * a * _AccentI;
        }
        ENDCG
    }
    FallBack "Standard"
}