Shader "FrcSim/Crowd"
{
    Properties { _Color ("Tint", Color) = (1,1,1,1) }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        CGPROGRAM
        #pragma surface surf Lambert vertex:vert
        #pragma multi_compile_instancing
        #pragma target 3.0
        fixed4 _Color; float _FrcExcite;
        UNITY_INSTANCING_BUFFER_START(Props)
            UNITY_DEFINE_INSTANCED_PROP(fixed4, _Shirt)
            UNITY_DEFINE_INSTANCED_PROP(fixed4, _Skin)
            UNITY_DEFINE_INSTANCED_PROP(float, _Phase)
        UNITY_INSTANCING_BUFFER_END(Props)
        struct Input { float4 vcol : COLOR; };
        void vert (inout appdata_full v, out Input o)
        {
            UNITY_SETUP_INSTANCE_ID(v);
            UNITY_INITIALIZE_OUTPUT(Input, o);
            float ph = UNITY_ACCESS_INSTANCED_PROP(Props, _Phase);
            float e = saturate(_FrcExcite);
            float t = _Time.y * (3.5 + ph * 2.5) + ph * 6.2831;
            float bob = sin(t) * 0.5 + 0.5;
            v.vertex.y += v.color.r * e * (0.22 + 0.22 * bob);                 // arms up when the crowd is excited
            v.vertex.y += v.color.g * (0.015 + 0.04 * e) * sin(t * 0.8);        // head bob
            v.vertex.y += (v.color.b > 0.75 ? 1.0 : 0.0) * e * 0.05 * bob;     // torso lift
        }
        void surf (Input IN, inout SurfaceOutput o)
        {
            fixed4 shirt = UNITY_ACCESS_INSTANCED_PROP(Props, _Shirt);
            fixed4 skin = UNITY_ACCESS_INSTANCED_PROP(Props, _Skin);
            float b = IN.vcol.b;
            fixed3 c = b > 0.75 ? shirt.rgb : (b > 0.25 ? fixed3(0.10, 0.11, 0.16) : skin.rgb);
            o.Albedo = c * _Color.rgb;
        }
        ENDCG
    }
    FallBack "Diffuse"
}