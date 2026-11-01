// Superposiciones del módulo de control (capa de color de las estancias, auras y contornos de selección):
// sin luz, semitransparente, con color de vértice multiplicado por _Color. Los vértices con uv.x > 0 se
// desplazan uv.x·_Ancho metros a lo largo de su normal, para que el anillo de los edificios tenga un ancho
// en pantalla parecido a cualquier distancia sin rehacer la malla. No pinta donde el stencil trae el bit 3 (las
// personas, que lo escriben los sombreadores de Personas): así la capa no tiñe a la gente de pie.
Shader "Gemelo/Control/Superposicion"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _Ancho ("Ancho de los anillos (m)", Float) = 1
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
        [Enum(UnityEngine.Rendering.CompareFunction)] _StencilComp ("Stencil (personas)", Float) = 6
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Superposicion"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest [_ZTest]
            Cull Off
            // No tiñe a las personas: Personas marca con el bit 3 del stencil los píxeles visibles de los NPCs
            Stencil { Ref 8 ReadMask 8 Comp [_StencilComp] }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Ancho;
            CBUFFER_END

            struct Atributos
            {
                float4 posicion : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 posicion : SV_POSITION;
                half4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Atributos i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 p = i.posicion.xyz + i.normal * (i.uv.x * _Ancho);
                o.posicion = TransformObjectToHClip(p);
                o.color = i.color * _Color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                return i.color;
            }
            ENDHLSL
        }
    }
}
