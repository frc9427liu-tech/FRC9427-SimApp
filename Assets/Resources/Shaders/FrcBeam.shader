Shader "FrcSim/Beam"
{
    Properties { _Color ("Color", Color) = (1,1,1,1) _Intensity ("Intensity", Float) = 0.08 }
    SubShader
    {
        Tags { "Queue"="Transparent+5" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha One
        ZWrite Off Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 _Color; float _Intensity;
            struct appdata { float4 v : POSITION; float3 n : NORMAL; float4 c : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float3 n : TEXCOORD0; float3 vd : TEXCOORD1; float a : TEXCOORD2; };
            v2f vert (appdata a)
            {
                v2f o; o.pos = UnityObjectToClipPos(a.v);
                float3 wp = mul(unity_ObjectToWorld, a.v).xyz;
                o.n = UnityObjectToWorldNormal(a.n); o.vd = _WorldSpaceCameraPos - wp; o.a = a.c.a; return o;
            }
            float4 frag (v2f i) : SV_Target
            {
                float fres = pow(saturate(abs(dot(normalize(i.n), normalize(i.vd)))), 2.0);   // 邊緣淡出:光柱看起來是體積光而不是塑膠錐
                return float4(_Color.rgb, _Intensity * i.a * fres);
            }
            ENDCG
        }
    }
}
