Shader "FrcSim/Cad"
{
    // CAD 機器人專用:不吃場景燈光(場景頂光很強、側面環境光很弱,CAD 車會「上白側黑」),
    // 改用固定的半球假光:朝上 100%、側面 ~62%、朝下 ~42%,零件各自保留顏色。
    Properties
    {
        _Color ("Color", Color) = (0.6,0.6,0.65,1)
        _Side ("Side brightness", Range(0.2,1)) = 0.62
        _Down ("Down brightness", Range(0.1,1)) = 0.42
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow
        #pragma multi_compile_instancing
        #pragma target 3.0
        fixed4 _Color; half _Side, _Down;
        struct Input { float3 worldNormal; };
        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            float3 n = normalize(IN.worldNormal);
            float up = saturate(n.y), dn = saturate(-n.y);
            float k = lerp(_Side, 1.0, up) * lerp(1.0, _Down / _Side, dn);
            o.Albedo = 0;
            o.Metallic = 0;
            o.Smoothness = 0;
            o.Emission = _Color.rgb * k;
        }
        ENDCG
    }
    FallBack "Standard"
}
