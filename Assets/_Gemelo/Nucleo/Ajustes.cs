// Ajustes del usuario (menú inicial y de pausa). Se guardan en JSON en persistentDataPath.
// Cada módulo lee Ajustes.Actual y se suscribe a Ajustes.Cambiados para aplicar los cambios en caliente.
using System;
using System.IO;
using UnityEngine;

namespace Gemelo
{
    [Serializable]
    public class Ajustes
    {
        // Servicio
        public string urlServicio = "http://127.0.0.1:8800";

        // Gráficos
        public bool pantallaCompleta = true;
        public int fpsMaximo = 0;              // 0 = sin límite (vSync)
        public bool mostrarFps;
        public float escalaUI = 1f;
        // (los valores de fábrica son los del preajuste Alto: ver Graficos.Poner)
        public int antialias = 3;              // Graficos.NombresAntialias: no, FXAA, SMAA, MSAA 2×, MSAA 4×
        public int sombras = 3;                // Graficos.NombresSombras: no, bajas, medias, altas, ultra
        public int oclusion = 1;               // oclusión ambiental: no, media resolución, completa
        public float escalaRender = 1f;        // resolución 3D respecto a la de la pantalla (por debajo de 1, FSR)
        public float detalle = 2f;             // QualitySettings.lodBias: distancia a la que se simplifican las mallas
        public float distanciaHierba = 90f;    // m

        // Personas
        public int maxPersonas = 2000;         // tope de NPCs simulados (1:1 hasta este número)
        public int maxAnimadas = 40;           // NPCs con animación completa cerca de la cámara
        public float distanciaPersonas = 250f; // más allá no se dibujan

        // Controles
        public float sensibilidadRaton = 1f;
        public bool invertirY;
        public float campoVision = 60f;
        public float velocidadCamaraCenital = 1f;
        public int avatarJugador;              // cuerpo en tercera persona: 0 = Brian, 1 = Megan

        // Audio
        public float volumenGeneral = 0.8f;
        public float volumenAmbiente = 0.7f;
        public float volumenEfectos = 0.8f;

        // Gemelo
        public bool indicadoresFlotantes = true;
        public bool sincronizarSol = true;     // el sol y el cielo siguen la hora del modelo

        static Ajustes actual;
        public static Ajustes Actual => actual ??= Cargar();
        public static event Action<Ajustes> Cambiados;

        static string Ruta => Path.Combine(Application.persistentDataPath, "ajustes_gemelo.json");

        static Ajustes Cargar()
        {
            try
            {
                if (File.Exists(Ruta)) return JsonUtility.FromJson<Ajustes>(File.ReadAllText(Ruta)) ?? new Ajustes();
            }
            catch (Exception e) { Debug.LogWarning($"[Gemelo] Ajustes ilegibles, uso los de fábrica: {e.Message}"); }
            return new Ajustes();
        }

        /// <summary>Guarda y avisa a los módulos. Llamar tras cambiar campos de Actual (guardar = false: solo para
        /// esta sesión, como en las pruebas).</summary>
        public static void Aplicar(bool guardar = true)
        {
            if (guardar)
                try { File.WriteAllText(Ruta, JsonUtility.ToJson(Actual, true)); }
                catch (Exception e) { Debug.LogWarning($"[Gemelo] No se pudieron guardar los ajustes: {e.Message}"); }
            Cambiados?.Invoke(Actual);
        }

        public static void Restablecer()
        {
            actual = new Ajustes();
            Aplicar();
        }
    }
}
