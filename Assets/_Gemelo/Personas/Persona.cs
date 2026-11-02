// Una persona simulada y su aspecto (variante de cuerpo, estatura, ropa y paso), que sale de una semilla.
using UnityEngine;

namespace Gemelo
{
    public enum EstadoPersona : byte { Inactiva, Quieta, Preparando, Andando, Reunida }
    public enum Gesto : byte { Nada, Habla, Movil }

    public struct Apariencia
    {
        public int variante;          // 0 hombre, 1 hombre con mochila, 2 mujer, 3 mujer con mochila (malla lejana)
        public int personaje;         // índice en RecursosPersonas.avatares (personaje de Mixamo); −1 = por variante
        public float escala;          // el maniquí mide 1,81 m a escala 1
        public float paso;            // velocidad al andar (m/s)
        public Color32 piel, top, mangas, bajo, zapatos, pelo, mochila;

        static readonly Color32[] Pieles = { new(244, 205, 180, 255), new(232, 185, 150, 255), new(214, 160, 120, 255), new(180, 125, 90, 255), new(130, 88, 60, 255), new(95, 62, 45, 255) };
        static readonly float[] PesoPiel = { 3, 5, 4, 2, 1, 0.6f };
        static readonly Color32[] Pelos = { new(25, 20, 18, 255), new(55, 38, 25, 255), new(90, 62, 38, 255), new(150, 110, 65, 255), new(205, 175, 120, 255), new(120, 120, 118, 255), new(120, 40, 30, 255) };
        static readonly float[] PesoPelo = { 4, 5, 3, 1.5f, 1, 0.8f, 0.4f };
        static readonly Color32[] Tops =
        {
            new(32, 40, 70, 255), new(25, 25, 28, 255), new(110, 112, 118, 255), new(225, 225, 220, 255), new(120, 30, 40, 255),
            new(70, 85, 50, 255), new(190, 170, 135, 255), new(70, 110, 160, 255), new(200, 150, 40, 255), new(170, 50, 45, 255),
            new(60, 120, 110, 255), new(150, 95, 140, 255), new(235, 120, 90, 255), new(160, 190, 215, 255), new(85, 55, 40, 255),
        };
        static readonly Color32[] Bajos = { new(48, 70, 110, 255), new(62, 88, 130, 255), new(28, 28, 32, 255), new(85, 85, 90, 255), new(175, 155, 120, 255), new(55, 65, 45, 255), new(95, 60, 45, 255) };
        static readonly float[] PesoBajo = { 5, 3, 4, 2, 1.5f, 1, 0.6f };
        static readonly Color32[] Zapatos = { new(235, 235, 232, 255), new(25, 25, 25, 255), new(90, 90, 95, 255), new(100, 65, 40, 255), new(40, 60, 110, 255) };
        static readonly Color32[] Mochilas = { new(25, 25, 30, 255), new(30, 45, 80, 255), new(90, 95, 100, 255), new(150, 35, 35, 255), new(50, 80, 55, 255), new(200, 140, 40, 255) };

        static T Elegir<T>(System.Random r, T[] v, float[] pesos = null)
        {
            if (pesos == null) return v[r.Next(v.Length)];
            float tot = 0;
            foreach (var p in pesos) tot += p;
            float x = (float)r.NextDouble() * tot;
            for (int i = 0; i < v.Length; i++) { x -= pesos[i]; if (x <= 0) return v[i]; }
            return v[v.Length - 1];
        }

        static Color32 Variar(System.Random r, Color32 c, float k)
        {
            float f = 1f + ((float)r.NextDouble() * 2f - 1f) * k;
            return new Color32((byte)Mathf.Clamp(c.r * f, 0, 255), (byte)Mathf.Clamp(c.g * f, 0, 255), (byte)Mathf.Clamp(c.b * f, 0, 255), 255);
        }

        static float Normal(System.Random r)
        {
            double u1 = 1.0 - r.NextDouble(), u2 = r.NextDouble();
            return (float)(System.Math.Sqrt(-2.0 * System.Math.Log(u1)) * System.Math.Sin(2.0 * System.Math.PI * u2));
        }

        public static Apariencia Generar(int semilla, float probMochila = 0.45f)
        {
            var r = new System.Random(semilla * 7919 + 17);
            bool mujer = r.NextDouble() < 0.42;
            bool mochila = r.NextDouble() < probMochila;
            var a = new Apariencia
            {
                variante = (mujer ? 2 : 0) + (mochila ? 1 : 0),
                personaje = -1,
                escala = Mathf.Clamp((mujer ? 0.915f : 0.97f) + Normal(r) * 0.035f, 0.84f, 1.07f),
                paso = Mathf.Clamp(1.32f + Normal(r) * 0.13f, 1.0f, 1.65f),
                piel = Variar(r, Elegir(r, Pieles, PesoPiel), 0.05f),
                pelo = Variar(r, Elegir(r, Pelos, PesoPelo), 0.08f),
                top = Variar(r, Elegir(r, Tops), 0.12f),
                bajo = Variar(r, Elegir(r, Bajos, PesoBajo), 0.08f),
                zapatos = Elegir(r, Zapatos),
                mochila = Variar(r, Elegir(r, Mochilas), 0.1f),
            };
            // Noviembre: casi todos con manga larga
            a.mangas = r.NextDouble() < 0.82 ? a.top : a.piel;
            if (r.NextDouble() < 0.25) a.mangas = Variar(r, Elegir(r, Tops), 0.1f);   // chaqueta de otro color
            return a;
        }

        public Color32 Region(int i) => i switch
        {
            0 => piel, 1 => top, 2 => mangas, 3 => bajo, 4 => zapatos, 5 => pelo, _ => mochila,
        };
    }

    public class Persona
    {
        public int id;
        public int semilla;
        public Apariencia ap;

        public EstadoPersona estado;
        public Vector3 pos;
        public float rumbo;                 // rad (0 = +Z)
        public float vel;                   // velocidad actual (m/s)
        public float velObjetivo;
        public float fase;                  // ciclos del paso
        public bool sentada;
        public Gesto gesto;
        public float tDecision;             // próxima decisión estando quieta
        public float tLibre;                // sin estancia: no se va del campus antes (tras una alarma espera un poco)

        // Dónde está y adónde va
        public Estancia destino;            // estancia asignada (null si va o viene de fuera)
        public Estancia donde;              // estancia en la que está quieta (null andando o fuera)
        public int plaza = -1;              // plaza (asiento) ocupada en destino
        public Vector3 objetivo;            // punto final (asiento o sitio de pie)
        public float rumboFinal;
        public float alturaAsiento;
        public int grupo = -1;              // id del que organiza el corrillo
        public Vector3 centroGrupo;
        public Llegada llegada;             // de dónde viene o adónde va (fuera del campus)
        public bool saliendo;               // al acabar el viaje desaparece
        public EdificioPersonas evacuando;  // edificio del que huye (o en cuya reunión espera)
        public EdificioPersonas edificio;   // edificio en el que está ahora (para el corte por plantas y la luz)
        public float tEdificio;

        // Viaje: hasta tres tramos (al acceso de la estancia, entre nodos y hasta el sitio final)
        public readonly Ruta[] tramos = new Ruta[4];
        public readonly bool[] inversos = new bool[4];
        public int nTramos, tramo;
        public float s;                     // distancia recorrida en el tramo
        public float lateral, lateralObj;   // desplazamiento a un lado del camino (carril propio y esquivar)
        public Vector3 desvio;              // ese desplazamiento ya aplicado, suavizado (sin saltos en las esquinas)
        public float tArranque;             // no echa a andar antes (levantarse, reaccionar a la alarma)
        public bool pendienteRuta;
        public float multVel = 1f;

        // Dibujo
        public int avatar = -1;             // índice en la reserva de avatares cercanos
        public bool visible;
        public InfoLocal localAndando;      // local bajo techo en el que está andando (null fuera), cada poco
        public float tLocal;

        public bool Activa => estado != EstadoPersona.Inactiva;
        public bool Andando => estado == EstadoPersona.Andando;
    }
}
