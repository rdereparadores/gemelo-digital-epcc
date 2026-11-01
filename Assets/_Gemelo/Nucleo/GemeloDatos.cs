// Estructuras de la API v1 del gemelo (docs/CONTRATO_API.md), legibles con JsonUtility.
using System;
using System.Collections.Generic;

namespace Gemelo
{
    [Serializable]
    public class Metrica
    {
        public string property;
        public float value;
        public string unit;
        public string source;       // "real" | "simulated" | "mixed"
        public int sensor_count;
    }

    [Serializable]
    public class Alerta
    {
        public string code;         // co2_high, window_open_heating, fire_alarm...
        public string severity;     // info | warning | critical
        public string message;
        public string space_id;     // solo en las alertas de edificio
    }

    [Serializable]
    public class EstadoEstancia
    {
        public string space_id;
        public string unity_room;   // clave de interiores.json: "1903_P1_24"
        public string time;
        public List<Metrica> metrics;
        public int windows_open;
        public int windows_total;
        public int radiators_active;
        public int radiators_total;
        public string comfort;      // ok | cold | hot | stuffy | humid | dry | unknown
        public List<Alerta> alerts;

        public bool Metrica(string propiedad, out Metrica m)
        {
            m = metrics?.Find(x => x.property == propiedad);
            return m != null;
        }

        public float Valor(string propiedad, float porDefecto = float.NaN) =>
            Metrica(propiedad, out var m) ? m.value : porDefecto;
    }

    [Serializable]
    public class EstadoEdificio
    {
        public string building_id;
        public string unity_id;     // "1903"
        public string name;
        public string time;
        public List<Metrica> metrics;
        public List<EstadoEstancia> spaces;
        public List<Alerta> alerts;
    }

    [Serializable]
    public class EstadoSimulacion
    {
        public bool running;
        public float time_scale;
        public string clock;
        public string sim_time;
        public string model_time;   // úsala para el sol: difiere de sim_time en escenarios anclados
        public string scenario;
        public string weather_source;
        public string weather_in_use;
    }

    [Serializable]
    public class Tiempo
    {
        public List<Metrica> metrics;
        public float sun_elevation;       // grados
        public float cloud_cover;         // 0-1
        public float solar_irradiance;    // W/m2
        public float precipitation_rate;  // mm/h
    }

    [Serializable]
    public class EstadoCampus
    {
        public string time;
        public EstadoSimulacion simulation;
        public Tiempo weather;
        public List<EstadoEdificio> buildings;
    }

    [Serializable]
    public class Valor
    {
        public string property;
        public float value;
        public string unit;
    }

    [Serializable]
    public class Observacion
    {
        public string sensor_id;
        public string time;
        public string source;
        public List<Valor> values;
    }

    [Serializable]
    public class Escenario
    {
        public string id;
        public string name;
        public string description;
        public bool active;
    }

    [Serializable]
    public class ListaEscenarios { public List<Escenario> items; }

    [Serializable]
    public class EventoSimulacion
    {
        public string id;
        public string type;          // open_windows, close_windows, class_start, class_end, heating_off, heating_on,
                                     // fire_alarm, power_outage, water_leak, sensor_failure
        public string target;        // sensor, estancia (space_id), edificio (INF…) o "campus"
        public string start;
        public float duration_s;     // 0 = hasta que se borre
        public bool active;
    }

    [Serializable]
    public class ListaEventos { public List<EventoSimulacion> items; }

    [Serializable]
    public class ListaObservaciones { public List<Observacion> items; }
}
