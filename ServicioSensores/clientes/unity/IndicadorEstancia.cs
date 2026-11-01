// Ejemplo: tiñe un renderer (suelo, volumen de la sala...) según la temperatura o el CO₂ de su estancia.
using UnityEngine;

namespace Gemelo
{
    public class IndicadorEstancia : MonoBehaviour
    {
        public enum Capa { Temperatura, CO2, Ocupacion, Confort }

        [Tooltip("Clave de la estancia en interiores.json, p. ej. 1903_P1_24")]
        public string unityRoom;
        public Capa capa = Capa.Temperatura;
        public Renderer objetivo;
        public Gradient degradado = new()
        {
            colorKeys = new[]
            {
                new GradientColorKey(new Color(0.20f, 0.45f, 0.95f), 0f),
                new GradientColorKey(new Color(0.25f, 0.80f, 0.45f), 0.5f),
                new GradientColorKey(new Color(0.95f, 0.30f, 0.20f), 1f),
            },
        };

        MaterialPropertyBlock bloque;
        static readonly int ColorBase = Shader.PropertyToID("_BaseColor");

        void OnEnable()
        {
            bloque ??= new MaterialPropertyBlock();
            if (objetivo == null) objetivo = GetComponent<Renderer>();
            if (ClienteGemelo.Instancia != null) ClienteGemelo.Instancia.EstadoActualizado += Pintar;
        }

        void OnDisable()
        {
            if (ClienteGemelo.Instancia != null) ClienteGemelo.Instancia.EstadoActualizado -= Pintar;
        }

        void Pintar(EstadoCampus _)
        {
            if (objetivo == null || !ClienteGemelo.Instancia.Estancia(unityRoom, out var e)) return;
            float t = capa switch
            {
                Capa.Temperatura => Mathf.InverseLerp(16f, 28f, e.Valor("air_temperature", 21f)),
                Capa.CO2 => Mathf.InverseLerp(400f, 1500f, e.Valor("co2_concentration", 400f)),
                Capa.Ocupacion => Mathf.InverseLerp(0f, 60f, e.Valor("occupancy", 0f)),
                _ => e.comfort switch { "ok" => 0.5f, "cold" => 0f, "unknown" => 0.5f, _ => 1f },
            };
            objetivo.GetPropertyBlock(bloque);
            bloque.SetColor(ColorBase, degradado.Evaluate(t));
            objetivo.SetPropertyBlock(bloque);
        }
    }
}
