// Charco de una fuga de agua (Emergencias) en el suelo de un local interior, sin sondas de reflexión: la sonda del
// campus refleja el cielo, que dentro de un edificio delata el truco. En su lugar:
// - el suelo se ve a través del agua (textura opaca de la cámara, desplazada por las ondas), más oscuro y frío;
// - refleja lo que hay alrededor (reflejo en espacio de pantalla: marcha por la profundidad de la cámara) y, donde
//   el rayo se sale de la pantalla, un «techo» uniforme (_Techo: la luz del local, que pone Emergencias según esté
//   encendido), con Fresnel; las luminarias del local (luces adicionales de Forward+) dan brillos nítidos;
// - ondas: rizado fino que se mueve y anillos que salen del punto donde gotea (el centro del charco);
// - el borde (uv1.x: 0 en el centro, 1 en el contorno) es una franja de suelo mojado, más oscuro y sin ondas, que se
//   funde con el suelo.
Shader "Gemelo/Agua"
{
    Properties
    {
        _Techo ("Color del techo reflejado", Color) = (0.55, 0.53, 0.5, 1)
        _Tinte ("Tinte del agua", Color) = (0.78, 0.86, 0.92, 1)
        _Oscurecer ("Oscurecimiento del suelo mojado", Range(0, 1)) = 0.38
        _F0 ("Reflectancia de frente", Range(0, 0.5)) = 0.24
        _Brillo ("Brillo de las luminarias", Float) = 4
        _Goteo ("Anillos del goteo", Range(0, 1)) = 1
    }
    SubShader
    {
        // (antes que los demás transparentes: la capa de color de la vista cenital va por encima)
        Tags { "Queue" = "Transparent-50" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Agua"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            Offset -1, -1

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Techo, _Tinte;
                half _Oscurecer, _F0, _Brillo, _Goteo;
            CBUFFER_END

            struct Atributos
            {
                float3 positionOS : POSITION;
                float2 radial : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 pantalla : TEXCOORD1;
                float2 extra : TEXCOORD2;      // radial (0 centro – 1 borde), niebla
                float3 centro : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Atributos v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                o.positionWS = TransformObjectToWorld(v.positionOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.pantalla = ComputeScreenPos(o.positionCS);
                o.extra = float2(v.radial.x, ComputeFogFactor(o.positionCS.z));
                o.centro = TransformObjectToWorld(float3(0, 0, 0));
                return o;
            }

            // Altura del agua (m) en un punto del plano: rizado de varias direcciones y anillos del goteo
            float Altura(float2 p, float2 c, float t)
            {
                float h = 0.0;
                h += 0.0012 * sin(dot(p, float2(7.3, 2.1)) + t * 1.9);
                h += 0.0010 * sin(dot(p, float2(-3.7, 6.4)) - t * 2.3);
                h += 0.0007 * sin(dot(p, float2(11.0, -9.0)) + t * 3.1);
                h += 0.0005 * sin(dot(p, float2(-14.0, -12.5)) - t * 3.7);
                // Anillos: tres gotas desfasadas que caen en torno al centro
                float d = length(p - c);
                for (int k = 0; k < 3; k++)
                {
                    float fase = frac(t * 0.45 + k / 3.0);
                    float r = fase * 1.6;
                    float onda = exp(-pow((d - r) * 9.0, 2.0)) * (1.0 - fase) * (1.0 - fase);
                    h += _Goteo * 0.004 * onda * sin((d - r) * 40.0);
                }
                return h;
            }

            // Reflejo en espacio de pantalla: marcha el rayo reflejado (pasos crecientes, ~35 m) contra la profundidad de
            // la cámara. Devuelve el color reflejado y su peso (0 si se sale de la pantalla o no choca con nada).
            // (dentro del bucle, lecturas con el nivel de detalle 0 explícito: SampleSceneDepth/SampleSceneColor usan
            // derivadas, que no valen en un bucle de iteraciones variables, y el compilador avisaba en cada build)
            half4 Reflejo(float3 p0, float3 r)
            {
                float paso = 0.04, s = 0.02;
                half4 res = 0;
                [loop] for (int k = 0; k < 32; k++)
                {
                    s += paso;
                    paso *= 1.17;
                    float3 p = p0 + r * s;
                    float4 cs = TransformWorldToHClip(p);
                    if (cs.w <= 0.01) break;
                    float4 sp = ComputeScreenPos(cs);
                    float2 uv = sp.xy / sp.w;
                    if (any(uv < 0.0) || any(uv > 1.0)) break;
                    float2 uvT = UnityStereoTransformScreenSpaceTex(uv);
                    float prof = SAMPLE_TEXTURE2D_X_LOD(_CameraDepthTexture, sampler_CameraDepthTexture, uvT, 0).r;
                    float dif = cs.w - LinearEyeDepth(prof, _ZBufferParams);
                    if (dif > 0.0 && dif < 0.25 + paso * 2.0)
                    {
                        // Atenúa hacia los bordes de la pantalla y a lo lejos
                        float2 b = min(uv, 1.0 - uv);
                        float peso = saturate(min(b.x, b.y) * 12.0) * saturate(1.5 - s / 25.0);
                        res = half4(SAMPLE_TEXTURE2D_X_LOD(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, uvT, 0).rgb, peso);
                        break;
                    }
                }
                return res;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float t = _Time.y;
                float2 p = i.positionWS.xz, c = i.centro.xz;
                float radial = i.extra.x;
                // 0 en la franja de suelo mojado del borde, 1 en el agua
                float agua = 1.0 - smoothstep(0.72, 0.86, radial);
                // Normal por diferencias finitas de la altura (solo en el agua)
                const float e = 0.01;
                float h0 = Altura(p, c, t);
                float hx = Altura(p + float2(e, 0), c, t), hz = Altura(p + float2(0, e), c, t);
                float3 n = normalize(float3(-(hx - h0) / e * agua, 1.0, -(hz - h0) / e * agua));

                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                float2 uv = i.pantalla.xy / i.pantalla.w;

                // Suelo a través del agua: desplazado por las ondas, más oscuro y algo frío
                float2 desp = n.xz * 0.035 * agua;
                half3 suelo = SampleSceneColor(uv + desp);
                half3 base = suelo * lerp(1.0, _Oscurecer, saturate(agua + 0.65)) * lerp(half3(1, 1, 1), _Tinte.rgb, agua);

                // Reflejo (lo que hay alrededor o, si no, el techo) con Fresnel (Schlick)
                float nv = saturate(dot(n, v));
                float F = (_F0 + (1.0 - _F0) * pow(1.0 - nv, 5.0)) * lerp(0.3, 1.0, agua);
                half3 refl = _Techo.rgb;
                if (agua > 0.01)
                {
                    float3 r = reflect(-v, n);
                    half4 ss = Reflejo(i.positionWS + float3(0, 0.005, 0), r);
                    refl = lerp(refl, ss.rgb, ss.a);
                }
                half3 col = lerp(base, refl, F);

                // Brillos de las luminarias: especular muy cerrado de cada luz adicional
                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.normalizedScreenSpaceUV = uv;
                half3 spec = 0;
                float potencia = lerp(40.0, 300.0, agua);
                uint nLuces = GetAdditionalLightsCount();
                #if USE_CLUSTER_LIGHT_LOOP
                [loop] for (uint li = 0; li < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); li++)
                {
                    Light l = GetAdditionalLight(li, i.positionWS, half4(1, 1, 1, 1));
                    float3 hh = normalize(l.direction + v);
                    spec += l.color * l.distanceAttenuation * pow(saturate(dot(n, hh)), potencia);
                }
                #endif
                LIGHT_LOOP_BEGIN(nLuces)
                    Light l = GetAdditionalLight(lightIndex, i.positionWS, half4(1, 1, 1, 1));
                    float3 hh = normalize(l.direction + v);
                    spec += l.color * l.distanceAttenuation * pow(saturate(dot(n, hh)), potencia);
                LIGHT_LOOP_END
                // (normalizado a ojo: más fino cuanto más liso, sin pasarse con las luces cercanas)
                col += spec * _Brillo * lerp(0.15, 1.0, agua) * (potencia / 300.0 * 2.0 + 0.2);

                col = MixFog(col, i.extra.y);
                // Se funde con el suelo en el contorno
                float alfa = 1.0 - smoothstep(0.9, 1.0, radial);
                return half4(col, alfa);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
