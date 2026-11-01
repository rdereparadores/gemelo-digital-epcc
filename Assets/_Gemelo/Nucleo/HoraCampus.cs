// Reloj del gemelo. El servicio manda la hora del modelo (model_time, UTC) en cada estado; entre sondeos
// avanza aquí al ritmo de la simulación (time_scale) para que el sol, los NPCs y la UI vayan suaves.
// La hora local del campus es la de Madrid (CET/CEST), calculada sin depender de la base de zonas del SO.
using System;
using System.Globalization;
using UnityEngine;

namespace Gemelo
{
    public static class HoraCampus
    {
        // Cáceres (Escuela Politécnica)
        public const float Latitud = 39.4785f, Longitud = -6.3420f;

        static DateTime baseUtc = DateTime.UtcNow;
        static float baseReal;
        static float escala = 1f;
        static bool enMarcha = true;
        static bool sincronizada;

        /// <summary>Hora del modelo en UTC, interpolada.</summary>
        public static DateTime Utc =>
            sincronizada ? baseUtc.AddSeconds((enMarcha ? escala : 0f) * (Time.realtimeSinceStartup - baseReal)) : DateTime.UtcNow;

        /// <summary>Hora local del campus (Europe/Madrid).</summary>
        public static DateTime Local => ALocal(Utc);

        /// <summary>Segundos de modelo por segundo real (1 = tiempo real).</summary>
        public static float Escala => enMarcha ? escala : 0f;
        public static string Escenario { get; private set; } = "normal_day";
        public static bool EnDirecto { get; private set; } = true;

        /// <summary>Lo llama GemeloApp con cada estado nuevo.</summary>
        public static void Sincronizar(EstadoSimulacion sim)
        {
            if (sim == null) return;
            if (!DateTime.TryParse(sim.model_time ?? sim.sim_time, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t)) return;
            // Sin saltos hacia atrás de unos segundos por el retardo del sondeo (sí si el reloj se cambia de verdad)
            var ahora = Utc;
            double dif = (t - ahora).TotalSeconds;
            if (!sincronizada || Math.Abs(dif) > Math.Max(5.0, 3.0 * sim.time_scale) || dif > 0)
                baseUtc = t;
            else
                baseUtc = ahora;
            baseReal = Time.realtimeSinceStartup;
            escala = sim.time_scale <= 0 ? 1f : sim.time_scale;
            enMarcha = sim.running;
            Escenario = sim.scenario;
            EnDirecto = sim.clock == "live";
            sincronizada = true;
        }

        // ─── Zona horaria de Madrid: CET (+1) y CEST (+2) del último domingo de marzo al último de octubre, a la 01:00 UTC
        public static DateTime ALocal(DateTime utc)
        {
            utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            return DateTime.SpecifyKind(utc.AddHours(Verano(utc) ? 2 : 1), DateTimeKind.Unspecified);
        }

        public static DateTime AUtc(DateTime local)
        {
            var aprox = DateTime.SpecifyKind(local.AddHours(-1), DateTimeKind.Utc);
            return Verano(aprox) ? aprox.AddHours(-1) : aprox;
        }

        static bool Verano(DateTime utc)
        {
            var ini = UltimoDomingo(utc.Year, 3).AddHours(1);
            var fin = UltimoDomingo(utc.Year, 10).AddHours(1);
            return utc >= ini && utc < fin;
        }

        static DateTime UltimoDomingo(int año, int mes)
        {
            var d = new DateTime(año, mes, DateTime.DaysInMonth(año, mes), 0, 0, 0, DateTimeKind.Utc);
            return d.AddDays(-(int)d.DayOfWeek);
        }

        /// <summary>Elevación y acimut (grados, acimut desde el norte en sentido horario) del sol sobre el campus.</summary>
        public static void Sol(DateTime utc, out float elevacion, out float acimut)
        {
            // Algoritmo de la NOAA simplificado (error < 0,5°)
            double dia = utc.DayOfYear - 1 + (utc.Hour - 12 + utc.Minute / 60.0 + utc.Second / 3600.0) / 24.0;
            double g = 2 * Math.PI / 365.0 * dia;
            double eqt = 229.18 * (0.000075 + 0.001868 * Math.Cos(g) - 0.032077 * Math.Sin(g) - 0.014615 * Math.Cos(2 * g) - 0.040849 * Math.Sin(2 * g));
            double decl = 0.006918 - 0.399912 * Math.Cos(g) + 0.070257 * Math.Sin(g) - 0.006758 * Math.Cos(2 * g)
                          + 0.000907 * Math.Sin(2 * g) - 0.002697 * Math.Cos(3 * g) + 0.00148 * Math.Sin(3 * g);
            double minutos = utc.Hour * 60 + utc.Minute + utc.Second / 60.0 + eqt + 4 * Longitud;
            double ha = (minutos / 4.0 - 180.0) * Math.PI / 180.0;
            double lat = Latitud * Math.PI / 180.0;
            double cosZ = Math.Sin(lat) * Math.Sin(decl) + Math.Cos(lat) * Math.Cos(decl) * Math.Cos(ha);
            double z = Math.Acos(Math.Clamp(cosZ, -1, 1));
            elevacion = (float)(90.0 - z * 180.0 / Math.PI);
            double az = Math.Atan2(Math.Sin(ha), Math.Cos(ha) * Math.Sin(lat) - Math.Tan(decl) * Math.Cos(lat));
            acimut = (float)((az * 180.0 / Math.PI + 180.0) % 360.0);
        }
    }
}
