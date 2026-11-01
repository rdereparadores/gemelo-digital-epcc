// Brillo aditivo instanciado (Graphics.RenderMeshInstanced): ventanas iluminadas por dentro, señales de
// salida de emergencia, destellos de los estroboscópicos y cabezas de las farolas. Cada instancia trae su color
// (HDR) y su forma: 0 = rectángulo plano orientado por su matriz, 1 = destello redondo de cara a la cámara.
Shader "Gemelo/Brillo"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0.8, 0.5, 1)
        _Forma ("Forma", Vector) = (0, 0, 0, 0)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+10" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Blend One One
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Forma)
            UNITY_INSTANCING_BUFFER_END(Props)

            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float niebla : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                float4 forma = UNITY_ACCESS_INSTANCED_PROP(Props, _Forma);
                float3 p;
                if (forma.x > 0.5)
                {
                    // De cara a la cámara: centro de la matriz y tamaño de su escala X
                    float3 centro = TransformObjectToWorld(float3(0, 0, 0));
                    float tam = length(TransformObjectToWorldDir(float3(1, 0, 0), false));
                    float3 der = UNITY_MATRIX_V[0].xyz, arr = UNITY_MATRIX_V[1].xyz;
                    p = centro + (der * v.positionOS.x + arr * v.positionOS.y) * tam;
                }
                else p = TransformObjectToWorld(v.positionOS);
                o.positionCS = TransformWorldToHClip(p);
                o.uv = v.uv;
                o.niebla = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float4 c = UNITY_ACCESS_INSTANCED_PROP(Props, _Color);
                float4 forma = UNITY_ACCESS_INSTANCED_PROP(Props, _Forma);
                float2 q = abs(i.uv * 2.0 - 1.0);
                float a;
                if (forma.x > 0.5)
                {
                    float r = length(i.uv * 2.0 - 1.0);
                    a = exp(-r * r * 9.0) + 0.25 * exp(-r * r * 2.5);
                    a *= saturate(1.0 - r);
                }
                else
                {
                    // Rectángulo con el borde suave; forma.y: degradado vertical (más luz arriba, como un techo encendido)
                    float bx = smoothstep(1.0, 1.0 - max(forma.z, 0.02), q.x);
                    float by = smoothstep(1.0, 1.0 - max(forma.z, 0.02), q.y);
                    a = bx * by * lerp(1.0, 0.55 + 0.45 * i.uv.y, forma.y);
                }
                float3 col = c.rgb * a * c.a;
                // La niebla apaga el brillo a lo lejos (es aditivo: se funde con negro)
                col = MixFogColor(col, float3(0, 0, 0), i.niebla);
                return half4(col, 0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
