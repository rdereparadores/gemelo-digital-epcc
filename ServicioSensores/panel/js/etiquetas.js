// Textos en español para los códigos del contrato.

export const FUENTES = { real: 'Real', simulated: 'Simulado', none: 'Sin datos' };
/** Fuente de una métrica agregada: puede mezclar sensores reales y simulados. */
export const FUENTES_METRICA = { ...FUENTES, mixed: 'Mixto (real y simulado)' };
export const MODOS = { real: 'Real', auto: 'Auto', simulated: 'Simulado' };

export const TIPOS_SENSOR = {
  radiator_valve: 'Válvula de radiador',
  window_contact: 'Contacto de ventana',
  blind: 'Persiana',
  climate: 'Clima (T/H)',
  climate_co2: 'Clima con CO₂',
  pc_usage: 'Uso de PC',
  power_meter: 'Analizador de red',
  water_meter: 'Contador de agua',
  heating_plant: 'Planta de calefacción',
  solar_inverter: 'Inversor fotovoltaico',
  occupancy_camera: 'Cámara de aforo',
  wifi_occupancy: 'Ocupación wifi',
  sound_meter: 'Sonómetro',
  weather_station: 'Estación meteorológica',
  signage_screen: 'Pantalla de cartelería',
  light_sensor: 'Sensor de luz',
  air_quality: 'Calidad del aire',
  fire_alarm: 'Alarma de incendios',
};

export const PROPIEDADES = {
  air_temperature: 'Temperatura',
  relative_humidity: 'Humedad relativa',
  co2_concentration: 'CO₂',
  window_open: 'Ventana abierta',
  door_open: 'Puerta abierta',
  valve_position: 'Apertura de válvula',
  target_temperature: 'Consigna',
  battery_voltage: 'Batería',
  active_power: 'Potencia activa',
  active_energy: 'Energía activa',
  water_volume: 'Volumen de agua',
  water_flow: 'Caudal de agua',
  occupancy: 'Ocupación',
  devices_on: 'Equipos encendidos',
  power_on: 'Encendido',
  blind_position: 'Persiana',
  sound_level: 'Nivel sonoro',
  illuminance: 'Iluminancia',
  pm25: 'PM2,5',
  water_temperature: 'Temperatura del agua',
  wind_speed: 'Viento',
  wind_direction: 'Dirección del viento',
  atmospheric_pressure: 'Presión',
  precipitation: 'Precipitación',
  solar_power: 'Potencia fotovoltaica',
  lights_on: 'Luces encendidas',
  alarm_active: 'Alarma activa',
  wifi_clients: 'Clientes wifi',
};

/** Unidades UCUM → texto para mostrar. */
export const UNIDADES = {
  Cel: '°C', '%': '%', ppm: 'ppm', W: 'W', 'kW.h': 'kWh', L: 'L', 'L/min': 'L/min', V: 'V',
  dB: 'dB', lx: 'lx', 'ug/m3': 'µg/m³', '{people}': 'pers.', '{devices}': 'equipos', '1': '',
  'm/s': 'm/s', deg: '°', hPa: 'hPa', mm: 'mm',
};

export const TIPOS_ESPACIO = {
  classroom: 'Aula', computer_lab: 'Aula de informática', lab: 'Laboratorio', office: 'Despacho',
  corridor: 'Pasillo', hall: 'Vestíbulo', technical_room: 'Sala técnica', restroom: 'Aseo',
  cafeteria: 'Cafetería', meeting_room: 'Sala de reuniones', auditorium: 'Salón de grados', outdoor: 'Exterior',
  building: 'Todo el edificio', other: 'Otro',
};

export const CONFORT = {
  ok: ['Confortable', 'estado-ok'], cold: ['Frío', 'estado-aviso'], hot: ['Calor', 'estado-aviso'],
  stuffy: ['Aire cargado', 'estado-aviso'], humid: ['Húmedo', 'estado-aviso'], dry: ['Seco', 'estado-aviso'],
  unknown: ['Desconocido', 'neutro'],
};

export const ALERTAS = {
  co2_high: 'CO₂ alto', co2_critical: 'CO₂ crítico', window_open_heating: 'Ventana abierta con calefacción',
  temperature_low: 'Temperatura baja', temperature_high: 'Temperatura alta', fire_alarm: 'Alarma de incendio',
  sensor_battery_low: 'Batería baja', sensor_stale: 'Sensor sin datos recientes',
};

export const EVENTOS = {
  open_windows: 'Abrir ventanas',
  close_windows: 'Cerrar ventanas',
  class_start: 'Empieza una clase',
  class_end: 'Termina la clase',
  heating_off: 'Apagar calefacción',
  heating_on: 'Encender calefacción',
  fire_alarm: 'Alarma de incendio',
  power_outage: 'Corte de luz',
  water_leak: 'Fuga de agua',
  sensor_failure: 'Fallo de sensor',
};

export const PLANTAS = { PS2: 'Segundo sótano', PS1: 'Sótano', P00: 'Planta baja', P01: 'Primera planta', P02: 'Segunda planta', EXT: 'Exterior' };

export const etiquetaTipo = (k) => TIPOS_SENSOR[k] || k;
export const etiquetaPropiedad = (p) => PROPIEDADES[p] || p;
export const etiquetaUnidad = (u) => (u in UNIDADES ? UNIDADES[u] : (u || ''));
export const etiquetaPlanta = (p) => PLANTAS[p] || p;
export const etiquetaEspacio = (t) => TIPOS_ESPACIO[t] || t;
