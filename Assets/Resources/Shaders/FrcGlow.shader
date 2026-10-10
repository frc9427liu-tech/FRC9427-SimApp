Shader "FrcSim/Glow"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (1,1,1,1)
        _Intensity ("Intensity", Float) = 3
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            float4 _Color; float _Intensity;
            struct v2f { float4 pos : SV_POSITION; UNITY_FOG_COORDS(0) };
            v2f vert (appdata_base v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); UNITY_TRANSFER_FOG(o, o.pos); return o; }
            float4 frag (v2f i) : SV_Target { float4 c = float4(_Color.rgb * _Intensity, 1); UNITY_APPLY_FOG(i.fogCoord, c); return c; }
            ENDCG
        }
    }
}