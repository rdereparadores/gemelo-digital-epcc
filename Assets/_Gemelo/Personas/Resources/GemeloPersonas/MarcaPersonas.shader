// Marca en el stencil los píxeles visibles de los avatares animados (bit 3, valor 8: «aquí se ve una persona»),
// para que las superposiciones de la vista cenital (capa de color de las estancias) no tiñan a la gente. Se añade
// como material extra a los SkinnedMeshRenderer de los avatares solo mientras hace falta (DibujoMultitud): vuelve
// a dibujar la malla sin color ni profundidad, después de los opacos, y solo pasa donde el avatar quedó delante.
// La multitud instanciada (MultitudInstanciada.shader) escribe el mismo bit en su pasada de color.
Shader "Gemelo/Personas/MarcaPersonas"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry+10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "MarcaPersonas"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ColorMask 0
            ZWrite Off
            ZTest LEqual
            Offset -1, -1
            Cull Back
            Stencil { Ref 8 WriteMask 8 Comp Always Pass Replace }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Atributos { float4 posicion : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 posicion : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Atributos i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.posicion = TransformObjectToHClip(i.posicion.xyz);
                return o;
            }

            half4 frag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }
    }
    Fallback Off
}
