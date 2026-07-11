// MMO Toon Shader - 卡通渲染着色器（内置管线）
// 特性：分段光照、菲涅尔轮廓光、阴影过渡、金属高光
Shader "MMO/Toon"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        _ShadowColor ("Shadow Color", Color) = (0.5,0.5,0.5,1)
        _RimColor ("Rim Color", Color) = (1,1,1,0.5)
        _RimPower ("Rim Power", Range(0.5, 8)) = 3.0
        _Metallic ("Metallic", Range(0,1)) = 0.0
        _Smoothness ("Smoothness", Range(0,1)) = 0.3
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        LOD 100

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "ForwardBase" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            #pragma multi_compile_fog

            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            float4 _BaseColor;
            float4 _ShadowColor;
            float4 _RimColor;
            float _RimPower;
            float _Metallic;
            float _Smoothness;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldNormal : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                UNITY_FOG_COORDS(2)
                LIGHTING_COORDS(3, 4)
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                UNITY_TRANSFER_FOG(o, o.pos);
                TRANSFER_SHADOW(o);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 normalWS = normalize(i.worldNormal);
                float3 viewDirWS = normalize(_WorldSpaceCameraPos - i.worldPos);

                // 主光源方向
                float3 lightDir = normalize(_WorldSpaceLightPos0.xyz);

                // NdotL
                float NdotL = dot(normalWS, lightDir);

                // 卡通分段光照 - 使用 smoothstep 实现硬边过渡
                float toonFactor = smoothstep(-0.1, 0.15, NdotL);
                float3 diffuse = lerp(_ShadowColor.rgb, _BaseColor.rgb, toonFactor) * _LightColor0.rgb;

                // 环境光
                float3 ambient = ShadeSH9(half4(normalWS, 1)) * _BaseColor.rgb * 0.6;

                // 金属高光
                float3 specular = 0;
                if (_Metallic > 0.01)
                {
                    float3 halfDir = normalize(lightDir + viewDirWS);
                    float spec = pow(max(dot(normalWS, halfDir), 0), 32) * _Smoothness;
                    specular = spec * _LightColor0.rgb * _Metallic;
                }

                // 菲涅尔轮廓光
                float fresnel = 1.0 - max(dot(normalWS, viewDirWS), 0);
                fresnel = pow(fresnel, _RimPower);
                float3 rim = _RimColor.rgb * fresnel * _RimColor.a;

                // 阴影衰减
                float shadowAttenuation = LIGHT_ATTENUATION(i);
                diffuse *= shadowAttenuation;

                // 合成
                float3 finalColor = diffuse + ambient + specular + rim;
                UNITY_APPLY_FOG(i.fogCoord, finalColor);

                return fixed4(finalColor, 1.0);
            }
            ENDCG
        }

        // 附加光源 Pass（点光源/聚光灯）
        Pass
        {
            Name "ForwardAdd"
            Tags { "LightMode" = "ForwardAdd" }
            Blend One One
            ZWrite Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdadd
            #pragma multi_compile_fog

            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            float4 _BaseColor;
            float4 _ShadowColor;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldNormal : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                UNITY_FOG_COORDS(2)
                LIGHTING_COORDS(3, 4)
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                UNITY_TRANSFER_FOG(o, o.pos);
                TRANSFER_SHADOW(o);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 normalWS = normalize(i.worldNormal);
                float3 lightDir = normalize(_WorldSpaceLightPos0.xyz - i.worldPos);
                float NdotL = dot(normalWS, lightDir);
                float toonFactor = smoothstep(-0.1, 0.15, NdotL);
                float3 diffuse = lerp(_ShadowColor.rgb, _BaseColor.rgb, toonFactor) * _LightColor0.rgb;
                diffuse *= LIGHT_ATTENUATION(i);
                UNITY_APPLY_FOG(i.fogCoord, diffuse);
                return fixed4(diffuse, 1.0);
            }
            ENDCG
        }

        // 阴影投射 Pass
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_shadowcaster

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                V2F_SHADOW_CASTER;
            };

            v2f vert(appdata v)
            {
                v2f o;
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(o);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                SHADOW_CASTER_FRAGMENT(i);
            }
            ENDCG
        }
    }

    Fallback "VertexLit"
}
