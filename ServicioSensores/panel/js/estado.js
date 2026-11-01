// Estado compartido entre la cabecera y las vistas: simulación y enlace con SmartPoliTech.
import { obtener } from './api.js';

export const bus = new EventTarget();

let simulacion = null; // último SimulationState
let recibidoEn = 0; // Date.now() cuando llegó
let enlace = null; // último estado de /upstream

export const ultimaSimulacion = () => simulacion;
export const ultimoEnlace = () => enlace;

export function fijarSimulacion(s) {
  simulacion = s;
  recibidoEn = Date.now();
  bus.dispatchEvent(new CustomEvent('simulacion', { detail: s }));
}

export function fijarEnlace(u) {
  enlace = u;
  bus.dispatchEvent(new CustomEvent('enlace', { detail: u }));
}

function extrapolar(iso) {
  if (!iso) return null;
  const base = new Date(iso).getTime();
  if (!simulacion.running) return new Date(base);
  const escala = Number(simulacion.time_scale) || 1;
  return new Date(base + (Date.now() - recibidoEn) * escala);
}

/** Hora simulada (sim_time) extrapolada en el navegador a partir del último estado (Date o null). */
export const horaSimulada = () => extrapolar(simulacion?.sim_time);

/**
 * Hora del modelo (model_time): difiere de sim_time en los escenarios anclados a una fecha
 * (winter_morning, summer_afternoon). Si el servicio no la da, es la simulada.
 */
export const horaModelo = () => extrapolar(simulacion?.model_time || simulacion?.sim_time);

/** true si la hora del modelo se separa de la simulada (más de un minuto). */
export function modeloDistinto() {
  if (!simulacion?.model_time || !simulacion?.sim_time) return false;
  return Math.abs(new Date(simulacion.model_time) - new Date(simulacion.sim_time)) > 60e3;
}

export async function refrescarSimulacion() {
  fijarSimulacion(await obtener('/api/v1/simulation'));
  return simulacion;
}

/**
 * El contrato fija qué contiene /upstream pero no los nombres exactos de los campos;
 * se aceptan las variantes razonables y se deja el JSON crudo a la vista.
 */
export function normalizarEnlace(u) {
  if (!u || typeof u !== 'object') return { alcanzable: null };
  const primero = (...claves) => { for (const k of claves) if (u[k] !== undefined && u[k] !== null) return u[k]; return null; };
  let alcanzable = primero('reachable', 'alcanzable', 'ok', 'online', 'up');
  if (alcanzable === null && typeof u.status === 'string') alcanzable = ['ok', 'up', 'reachable', 'online'].includes(u.status);
  const errores = primero('errors', 'recent_errors', 'last_errors') || [];
  const cuarentena = primero('quarantined_measurements') || [];
  return {
    alcanzable,
    activado: primero('enabled'),
    latencia: primero('latency_ms', 'last_latency_ms', 'latency'),
    ultimoSondeo: primero('last_poll', 'last_poll_time', 'last_poll_at', 'last_polled', 'polled_at'),
    proximoSondeo: primero('next_poll', 'next_poll_at', 'next_poll_time'),
    ultimoExito: primero('last_success', 'last_success_at', 'last_ok'),
    duracion: primero('poll_duration_ms'),
    intervalo: primero('poll_interval_s'),
    umbral: primero('stale_after_s'),
    frescos: primero('fresh_sensors', 'sensors_fresh', 'fresh_sensor_count', 'fresh_count'),
    conDatos: primero('sensors_with_data'),
    sondeados: primero('polled_sensors'),
    totalReales: primero('real_sensors', 'total_real_sensors', 'sensors_total', 'known_sensors', 'tracked_sensors'),
    url: primero('base_url', 'url'),
    errores: Array.isArray(errores) ? errores : [errores],
    cuarentena: Array.isArray(cuarentena) ? cuarentena : [cuarentena],
    crudo: u,
  };
}

export async function refrescarEnlace() {
  const u = normalizarEnlace(await obtener('/api/v1/upstream'));
  fijarEnlace(u);
  return u;
}
