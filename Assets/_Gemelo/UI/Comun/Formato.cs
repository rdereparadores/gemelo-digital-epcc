// Textos de la interfaz en español: números con coma decimal, fechas, nombres de propiedades, usos,
// eventos y escenarios.
using System;
using System.Globalization;
using UnityEngine;

namespace Gemelo
{
    public static class Formato
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly string[] Dias = { "domingo", "lunes", "martes", "miércoles", "jueves", "viernes", "sábado" };
        static readonly string[] DiasCortos = { "dom", "lun", "mar", "mié", "jue", "vie", "sáb" };
        static readonly string[] Meses = { "enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre" };
        static readonly string[] MesesCortos = { "ene", "feb", "mar", "abr", "may", "jun", "jul", "ago", "sep", "oct", "nov", "dic" };

        /// <summary>Número con coma decimal y punto de millares a partir de 10 000 (norma RAE).</summary>
        public static string Num(float v, int dec = 0)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return "—";
            string s = v.ToString("F" + dec, Inv);
            int punto = s.IndexOf('.');
            string ent = punto >= 0 ? s.Substring(0, punto) : s;
            string frac = punto >= 0 ? s.Substring(punto + 1) : null;
            bool neg = ent.StartsWith("-");
            if (neg) ent = ent.Substring(1);
            if (ent.Length > 4)
            {
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < ent.Length; i++)
                {
                    if (i > 0 && (ent.Length - i) % 3 == 0) sb.Append('.');
                    sb.Append(ent[i]);
                }
                ent = sb.ToString();
            }
            return (neg ? "−" : "") + ent + (frac != null ? "," + frac : "");
        }

        public static string Potencia(float w) =>
            float.IsNaN(w) ? "—" : Mathf.Abs(w) >= 1e6f ? Num(w / 1e6f, 2) + " MW" : Mathf.Abs(w) >= 1000 ? Num(w / 1000f, 1) + " kW" : Num(w) + " W";

        public static string Hora(DateTime t) => t.ToString("HH:mm", Inv);
        public static string HoraSeg(DateTime t) => t.ToString("HH:mm:ss", Inv);
        public static string Fecha(DateTime t) => $"{Dias[(int)t.DayOfWeek]}, {t.Day} de {Meses[t.Month - 1]} de {t.Year}";
        public static string FechaCorta(DateTime t) => $"{DiasCortos[(int)t.DayOfWeek]} {t.Day} {MesesCortos[t.Month - 1]} {t.Year}";
        public static string Dia(DateTime t) => Mayus(Dias[(int)t.DayOfWeek]);
        public static string Mayus(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1);

        public static string Escala(float e)
        {
            if (e <= 0) return "En pausa";
            if (Mathf.Approximately(e, 1)) return "1×";
            return Num(e, e < 10 && Mathf.Abs(e - Mathf.Round(e)) > 0.01f ? 1 : 0) + "×";
        }

        public static string Duracion(float s)
        {
            if (s <= 0) return "sin límite";
            if (s < 3600) return Num(Mathf.Round(s / 60f)) + " min";
            float h = s / 3600f;
            return Num(h, Mathf.Abs(h - Mathf.Round(h)) < 0.05f ? 0 : 1) + " h";
        }

        public static bool LeerFecha(string iso, out DateTime utc) =>
            DateTime.TryParse(iso, Inv, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out utc);

        // ─── Propiedades observadas
        public static string Propiedad(string p) => p switch
        {
            "air_temperature" => "Temperatura",
            "relative_humidity" => "Humedad",
            "co2_concentration" => "CO₂",
            "occupancy" => "Ocupación",
            "active_power" => "Potencia",
            "sound_level" => "Ruido",
            "illuminance" => "Iluminación",
            "pm25" => "PM2,5",
            "valve_position" => "Válvulas",
            "window_open" => "Ventanas",
            "door_open" => "Puertas",
            "water_flow" => "Caudal de agua",
            "water_volume" => "Agua",
            "wifi_clients" => "Clientes wifi",
            "solar_power" => "Fotovoltaica",
            "devices_on" => "Equipos encendidos",
            "lights_on" => "Luces",
            "alarm_active" => "Alarma",
            "blind_position" => "Persianas",
            _ => p,
        };

        public static string Unidad(string p) => p switch
        {
            "air_temperature" => "°C",
            "relative_humidity" => "%",
            "co2_concentration" => "ppm",
            "occupancy" => "pers.",
            "active_power" => "W",
            "sound_level" => "dB",
            "illuminance" => "lx",
            "pm25" => "µg/m³",
            "valve_position" => "%",
            "blind_position" => "%",
            "water_flow" => "l/min",
            "water_volume" => "l",
            _ => "",
        };

        public static int Decimales(string p) => p switch
        {
            "air_temperature" => 1,
            "relative_humidity" => 0,
            "pm25" => 1,
            _ => 0,
        };

        public static string Valor(string p, float v)
        {
            if (float.IsNaN(v)) return "—";
            if (p == "active_power" || p == "solar_power") return Potencia(v);
            string u = Unidad(p);
            if (p == "occupancy") return Num(v, v < 10 && Mathf.Abs(v - Mathf.Round(v)) > 0.05f ? 1 : 0);
            return Num(v, Decimales(p)) + (u.Length > 0 ? (u == "%" || u == "°C" ? " " + u : " " + u) : "");
        }

        public static string ValorCapa(Capa c, float v)
        {
            if (float.IsNaN(v)) return "Sin datos";
            return c switch
            {
                Capa.Ocupacion => Num(v * 100f) + " % de aforo",
                Capa.Confort => "",
                Capa.Consumo => Potencia(v),
                _ => Num(v, c == Capa.Temperatura ? 1 : 0) + " " + Escalas.Unidad(c),
            };
        }

        // ─── Usos de los locales (Local.uso)
        public static string Uso(string u) => u switch
        {
            "aula" => "Aula", "aula_informatica" => "Aula de informática", "aula_dibujo" => "Aula de dibujo",
            "aula_flexible" => "Aula flexible", "seminario" => "Seminario", "minisala" => "Sala de trabajo",
            "salon_grados" => "Salón de grados", "salon_actos" => "Salón de actos",
            "despacho" => "Despacho", "despacho_direccion" => "Dirección", "oficina" => "Oficina", "secretaria" => "Secretaría",
            "conserjeria" => "Conserjería", "sala_juntas" => "Sala de juntas", "archivo" => "Archivo",
            "pasillo" => "Pasillo", "distribuidor" => "Distribuidor", "vestibulo" => "Vestíbulo", "hall" => "Hall",
            "galeria" => "Galería", "terraza" => "Terraza", "aseo" => "Aseo", "aseo_adaptado" => "Aseo adaptado",
            "almacen" => "Almacén", "instalaciones" => "Instalaciones", "sala_servidores" => "Sala de servidores",
            "reservado" => "Reservado", "cuarto_limpieza" => "Cuarto de limpieza",
            "sala_estudiantes" => "Sala de estudiantes", "sala_estudio" => "Sala de estudio", "cafeteria" => "Cafetería",
            "comedor" => "Comedor", "cocina" => "Cocina", "sala_descanso" => "Sala de descanso",
            null => "Local",
            _ => u.StartsWith("lab_") ? "Laboratorio de " + u.Substring(4).Replace('_', ' ') : Mayus(u.Replace('_', ' ')),
        };

        public enum GrupoUso { Docencia, Laboratorio, Despachos, Circulacion, Servicios, Estudiantes }

        public static GrupoUso Grupo(string u)
        {
            if (u == null) return GrupoUso.Servicios;
            if (u.StartsWith("lab_")) return GrupoUso.Laboratorio;
            switch (u)
            {
                case "aula": case "aula_informatica": case "aula_dibujo": case "aula_flexible": case "seminario":
                case "minisala": case "salon_grados": case "salon_actos":
                    return GrupoUso.Docencia;
                case "despacho": case "despacho_direccion": case "oficina": case "secretaria": case "conserjeria":
                case "sala_juntas": case "archivo":
                    return GrupoUso.Despachos;
                case "pasillo": case "distribuidor": case "vestibulo": case "hall": case "galeria": case "terraza":
                    return GrupoUso.Circulacion;
                case "sala_estudiantes": case "sala_estudio": case "cafeteria": case "comedor": case "cocina": case "sala_descanso":
                    return GrupoUso.Estudiantes;
                default:
                    return GrupoUso.Servicios;
            }
        }

        public static Color ColorGrupo(GrupoUso g) => g switch
        {
            GrupoUso.Docencia => new Color32(0xF4, 0xB2, 0x23, 255),
            GrupoUso.Laboratorio => new Color32(0x8E, 0x7C, 0xE8, 255),
            GrupoUso.Despachos => new Color32(0x1F, 0xA0, 0x84, 255),
            GrupoUso.Circulacion => new Color32(0x5C, 0x60, 0x66, 255),
            GrupoUso.Estudiantes => new Color32(0xE8, 0x83, 0x4A, 255),
            _ => new Color32(0x3A, 0x3D, 0x42, 255),
        };

        public static string NombreGrupo(GrupoUso g) => g switch
        {
            GrupoUso.Docencia => "Docencia", GrupoUso.Laboratorio => "Laboratorios", GrupoUso.Despachos => "Despachos",
            GrupoUso.Circulacion => "Pasillos", GrupoUso.Estudiantes => "Estudiantes", _ => "Servicios",
        };

        /// <summary>Nombre legible de un local: «Aula 24», «C2.01 · Aula de informática»…</summary>
        public static string NombreLocal(InfoLocal l)
        {
            if (l == null) return "";
            var lo = l.local;
            string uso = Uso(lo.uso);
            if (!string.IsNullOrEmpty(lo.codigo)) return lo.codigo.Length <= 8 ? $"{uso} {lo.codigo}" : lo.codigo;
            if (!string.IsNullOrEmpty(lo.rotulo) && lo.rotulo.Length <= 28) return Mayus(lo.rotulo.ToLowerInvariant());
            return $"{uso} {lo.numero}";
        }

        public static string NombreEdificio(InfoEdificio e) => e == null ? "" : !string.IsNullOrEmpty(e.nombre) ? e.nombre : "Edificio " + e.codigo;

        public static string Planta(string p) => p switch
        {
            "P-1" => "Sótano", "P0" => "Planta baja", "P1" => "Planta primera", "P2" => "Planta segunda", "P3" => "Planta tercera",
            _ => p,
        };

        public static string PlantaCorta(string p) => p switch { "P-1" => "S", "P0" => "PB", _ => p?.Replace("P", "P") };

        // ─── Confort
        public static string Confort(string c) => Escalas.ConfortTexto(c);

        // ─── Eventos de simulación
        public static string Evento(string t) => t switch
        {
            "open_windows" => "Abrir ventanas",
            "close_windows" => "Cerrar ventanas",
            "class_start" => "Empezar clase",
            "class_end" => "Terminar clase",
            "heating_off" => "Apagar calefacción",
            "heating_on" => "Encender calefacción",
            "fire_alarm" => "Alarma de incendio",
            "power_outage" => "Corte eléctrico",
            "water_leak" => "Fuga de agua",
            "sensor_failure" => "Fallo de sensor",
            _ => t,
        };

        public static string IconoEvento(string t) => t switch
        {
            "open_windows" => "ventana",
            "close_windows" => "ventana-cerrada",
            "class_start" => "clase",
            "class_end" => "salir",
            "heating_off" => "radiador-off",
            "heating_on" => "radiador",
            "fire_alarm" => "fuego",
            "power_outage" => "rayo",
            "water_leak" => "gota",
            _ => "evento",
        };

        public static string Escenario(string id, string nombre = null) => id switch
        {
            "normal_day" => "Día normal",
            "winter_morning" => "Mañana de invierno",
            "summer_afternoon" => "Tarde de verano",
            "exam_period" => "Época de exámenes",
            "weekend" => "Fin de semana",
            "holiday_closure" => "Cierre por vacaciones",
            "heating_failure" => "Avería de calefacción",
            "fire_drill" => "Simulacro de incendio",
            _ => nombre ?? id,
        };

        public static string Severidad(string s) => s switch { "critical" => "Crítica", "warning" => "Aviso", _ => "Info" };

        public static string Fuente(string s) => s switch { "real" => "Real", "simulated" => "Simulado", "mixed" => "Mixto", _ => "Sin datos" };
    }
}
