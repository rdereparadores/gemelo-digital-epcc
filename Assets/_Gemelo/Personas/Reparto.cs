// Reparto de los personajes de Mixamo entre la gente del campus según el local al que va cada persona: el personal
// (profesorado: Josh, Joe, Martha, Suzie, Roth) sale sobre todo en despachos, secretaría, conserjería y salas de
// juntas (85 %) y en los laboratorios de investigación (40 %); en las aulas y laboratorios docentes, alguno (3-8 %: el
// que da la clase); el resto es alumnado. (A 01-11-2026 el servicio no publica ocupación de los despachos, así que
// casi todo el personal que se ve está en salas de juntas, laboratorios y aulas.) Mantenimiento (Pete) y vigilancia (Alex)
// salen poco y con tope: como mucho MaxPorPersonaje de cada personaje a la vez en todo el campus.
// El sexo del personaje decide la malla lejana (hombre o mujer, sin mochila: los de Mixamo no la llevan) y sus
// colores dominantes pasan a la Apariencia, para que el maniquí lejano lleve los mismos que el personaje cercano.
// Sin perfiles (maniquíes), no hace nada.
using System.Collections.Generic;
using UnityEngine;

namespace Gemelo
{
    public static class RepartoPersonajes
    {
        public const int MaxPorPersonaje = 2;     // mantenimiento y vigilancia

        static readonly HashSet<string> Personal = new()
        {
            "despacho", "despacho_direccion", "secretaria", "conserjeria", "oficina", "sala_juntas", "archivo",
        };
        static readonly HashSet<string> Entradas = new() { "conserjeria", "vestibulo", "hall" };
        static readonly HashSet<string> Servicio = new() { "instalaciones", "almacen", "cuarto_limpieza", "sala_servidores", "cocina", "pasillo", "distribuidor" };
        static readonly List<int> candidatos = new();

        static bool Docencia(string uso) => uso.StartsWith("aula") || uso.StartsWith("lab") || uso == "seminario" || uso == "salon_actos" || uso == "salon_grados";

        static PapelPersonaje Papel(string uso, double x)
        {
            if (Entradas.Contains(uso) && x < 0.12) return PapelPersonaje.Vigilancia;
            if (Servicio.Contains(uso) && x < 0.05) return PapelPersonaje.Mantenimiento;
            if (Personal.Contains(uso)) return x < 0.85 ? PapelPersonaje.Profesorado : PapelPersonaje.Alumnado;
            if (uso == "lab_investigacion") return x < 0.4 ? PapelPersonaje.Profesorado : PapelPersonaje.Alumnado;
            if (uso.StartsWith("lab")) return x < 0.08 ? PapelPersonaje.Profesorado : PapelPersonaje.Alumnado;
            if (Docencia(uso)) return x < 0.03 ? PapelPersonaje.Profesorado : PapelPersonaje.Alumnado;
            return x < 0.06 ? PapelPersonaje.Profesorado : PapelPersonaje.Alumnado;
        }

        /// <summary>Elige el personaje de quien va a un local de este uso y le pone sexo, estatura y colores.
        /// <paramref name="cuenta"/>[i] = cuántas personas activas llevan ya el personaje i.</summary>
        public static void Elegir(ref Apariencia ap, string uso, int semilla, int[] cuenta)
        {
            var rec = RecursosPersonas.Cargar();
            if (rec == null || rec.perfiles == null || rec.perfiles.Length == 0 || rec.avatares == null || rec.perfiles.Length != rec.avatares.Length) return;
            var r = new System.Random(semilla * 31 + 7);
            bool mujer = ap.variante >= 2;
            var papel = Papel(uso ?? "", r.NextDouble());
            int n = rec.perfiles.Length;

            candidatos.Clear();
            for (int pasada = 0; pasada < 3 && candidatos.Count == 0; pasada++)
            {
                for (int i = 0; i < n; i++)
                {
                    var p = rec.perfiles[i];
                    if (p == null || rec.avatares[i] == null) continue;
                    bool unico = p.papel == PapelPersonaje.Mantenimiento || p.papel == PapelPersonaje.Vigilancia;
                    if (unico && cuenta != null && i < cuenta.Length && cuenta[i] >= MaxPorPersonaje) continue;
                    if (pasada == 0 && (p.papel != papel || !unico && p.mujer != mujer)) continue;
                    if (pasada == 1 && (p.papel != PapelPersonaje.Alumnado || p.mujer != mujer)) continue;
                    if (pasada == 2 && unico) continue;
                    candidatos.Add(i);
                }
            }
            if (candidatos.Count == 0) return;
            // De dos al azar, el menos repetido (más variedad en un aula llena)
            int a = candidatos[r.Next(candidatos.Count)], b = candidatos[r.Next(candidatos.Count)];
            int elegido = cuenta != null && b < cuenta.Length && a < cuenta.Length && cuenta[b] < cuenta[a] ? b : a;
            Aplicar(ref ap, elegido, rec.perfiles[elegido], r);
        }

        public static void Aplicar(ref Apariencia ap, int indice, PerfilPersonaje p, System.Random r)
        {
            ap.personaje = indice;
            ap.variante = p.mujer ? 2 : 0;
            float k = 1f + Mathf.Clamp(Normal(r) * 0.02f, -0.05f, 0.05f);
            ap.escala = p.altura / 1.81f * k;
            ap.piel = p.piel;
            ap.top = p.top;
            ap.mangas = p.mangas;
            ap.bajo = p.bajo;
            ap.zapatos = p.zapatos;
            ap.pelo = p.pelo;
            ap.mochila = p.top;
            if (p.mujer) ap.paso = Mathf.Min(ap.paso, 1.5f);
        }

        static float Normal(System.Random r)
        {
            double u1 = 1.0 - r.NextDouble(), u2 = r.NextDouble();
            return (float)(System.Math.Sqrt(-2.0 * System.Math.Log(u1)) * System.Math.Sin(2.0 * System.Math.PI * u2));
        }
    }
}
