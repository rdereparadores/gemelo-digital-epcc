// Utilidades de interfaz: creación de nodos, avisos, formatos y controles comunes.
import { ErrorApi } from './api.js';
import { FUENTES, MODOS, etiquetaPropiedad, etiquetaUnidad } from './etiquetas.js';

/** h('div', {class: 'x', onclick: fn}, hijo1, 'texto', [hijos]) */
export function h(etiqueta, atributos = {}, ...hijos) {
  const nodo = document.createElement(etiqueta);
  for (const [k, v] of Object.entries(atributos || {})) {
    if (v === undefined || v === null || v === false) continue;
    if (k === 'class') nodo.className = v;
    else if (k === 'dataset') Object.assign(nodo.dataset, v);
    else if (k === 'style' && typeof v === 'object') Object.assign(nodo.style, v);
    else if (k.startsWith('on') && typeof v === 'function') nodo.addEventListener(k.slice(2), v);
    else if (k === 'value') nodo.value = v;
    else if (k === 'checked' || k === 'selected' || k === 'disabled' || k === 'hidden') nodo[k] = Boolean(v);
    else nodo.setAttribute(k, v === true ? '' : String(v));
  }
  anadir(nodo, hijos);
  return nodo;
}

function anadir(nodo, hijos) {
  for (const hijo of hijos) {
    if (hijo === null || hijo === undefined || hijo === false) continue;
    if (Array.isArray(hijo)) anadir(nodo, hijo);
    else nodo.append(hijo instanceof Node ? hijo : document.createTextNode(String(hijo)));
  }
}

/** Icono del sprite de index.html: icono('aviso'), icono('ok', 'mini'). */
export function icono(nombre, clase = '') {
  const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  svg.setAttribute('class', `icono${clase ? ` ${clase}` : ''}`);
  svg.setAttribute('aria-hidden', 'true');
  const uso = document.createElementNS('http://www.w3.org/2000/svg', 'use');
  uso.setAttribute('href', `#i-${nombre}`);
  svg.append(uso);
  return svg;
}

/** Icono de una alerta o aviso según su gravedad. */
export const iconoGravedad = (g) => icono({ critical: 'error', error: 'error', info: 'info', exito: 'ok' }[g] || 'aviso');

export const vaciar = (nodo, ...hijos) => { nodo.replaceChildren(); anadir(nodo, hijos); return nodo; };

let contadorId = 0;
export const idUnico = (prefijo = 'c') => `${prefijo}-${++contadorId}`;

/* ---------- Avisos ---------- */
export function aviso(tipo, titulo, detalle, ms = tipo === 'error' ? 9000 : 4500) {
  const zona = document.getElementById('avisos');
  const nodo = h('div', { class: `aviso ${tipo}`, role: tipo === 'error' ? 'alert' : 'status' },
    h('span', { class: 'ico' }, iconoGravedad(tipo === 'info' ? 'info' : tipo)),
    h('span', { class: 't' }, titulo),
    h('button', { type: 'button', class: 'cerrar', 'aria-label': 'Cerrar aviso', onclick: () => nodo.remove() }, '×'),
    detalle ? h('span', { class: 'd' }, detalle) : null,
  );
  zona.append(nodo);
  while (zona.children.length > 4) zona.firstElementChild.remove();
  if (ms) setTimeout(() => nodo.remove(), ms);
  return nodo;
}

/** Muestra un error de la API (title + detail del problem+json). */
export function mostrarError(e, contexto) {
  if (e?.name === 'AbortError') return;
  if (e instanceof ErrorApi) {
    const p = e.problema;
    const pie = [p.status ? `HTTP ${p.status}` : null, p.instance].filter(Boolean).join(' · ');
    aviso('error', contexto ? `${contexto}: ${p.title}` : p.title, [p.detail, pie && `(${pie})`].filter(Boolean).join(' '));
  } else {
    console.error(e);
    aviso('error', contexto || 'Error inesperado', e?.message || String(e));
  }
}

/* ---------- Formatos ---------- */
const fmtFechaHora = new Intl.DateTimeFormat('es-ES', { dateStyle: 'short', timeStyle: 'medium' });
const fmtHora = new Intl.DateTimeFormat('es-ES', { hour: '2-digit', minute: '2-digit', second: '2-digit' });
const fmtFechaCorta = new Intl.DateTimeFormat('es-ES', { day: '2-digit', month: 'short', year: 'numeric' });
// Corto para segundos, minutos y horas («hace 3 min»); largo para días o más, donde el corto es ambiguo («hace 2 m»).
const fmtRelCorto = new Intl.RelativeTimeFormat('es', { numeric: 'auto', style: 'short' });
const fmtRel = new Intl.RelativeTimeFormat('es', { numeric: 'auto' });

export const fecha = (iso) => (iso ? fmtFechaHora.format(new Date(iso)) : '—');
export const hora = (iso) => (iso ? fmtHora.format(new Date(iso)) : '—');
export const fechaCorta = (iso) => (iso ? fmtFechaCorta.format(new Date(iso)) : '—');

export function relativo(iso, ahora = Date.now()) {
  if (!iso) return '—';
  const s = (new Date(iso).getTime() - ahora) / 1000;
  const a = Math.abs(s);
  if (a < 10) return 'ahora';
  if (a < 45) return fmtRelCorto.format(Math.round(s), 'second');
  if (a < 2700) return fmtRelCorto.format(Math.round(s / 60), 'minute');
  if (a < 64800) return fmtRelCorto.format(Math.round(s / 3600), 'hour');
  if (a < 86400 * 45) return fmtRel.format(Math.round(s / 86400), 'day');
  if (a < 86400 * 365) return fmtRel.format(Math.round(s / (86400 * 30.4)), 'month');
  return fmtRel.format(Math.round(s / (86400 * 365)), 'year');
}

export function numero(v, decimales) {
  if (v === null || v === undefined || Number.isNaN(v)) return '—';
  const d = decimales ?? (Math.abs(v) >= 1000 ? 0 : Math.abs(v) >= 100 ? 1 : Math.abs(v) >= 10 ? 1 : 2);
  return new Intl.NumberFormat('es-ES', { maximumFractionDigits: d, minimumFractionDigits: 0 }).format(v);
}

export const entero = (v) => (v === null || v === undefined ? '—' : new Intl.NumberFormat('es-ES').format(v));

const BOOLEANAS = new Set(['window_open', 'door_open', 'power_on', 'lights_on', 'alarm_active']);
export const esBooleana = (propiedad, unidad) => BOOLEANAS.has(propiedad) || unidad === '1';

/** Texto de un valor con su unidad ("21,4 °C", "Sí"). */
export function valor(v, unidad, propiedad) {
  if (v === null || v === undefined) return '—';
  if (esBooleana(propiedad, unidad)) return v >= 0.5 ? 'Sí' : 'No';
  const u = etiquetaUnidad(unidad);
  return u ? `${numero(v)} ${u}` : numero(v);
}

export const resumenValores = (valores = []) =>
  valores.map((x) => `${etiquetaPropiedad(x.property)} ${valor(x.value, x.unit, x.property)}`).join(' · ');

/* ---------- Chips ---------- */
export const chipFuente = (f) => h('span', { class: `chip fuente-${f in FUENTES ? f : 'none'}` }, FUENTES[f] || 'Sin datos');
export const chipModo = (m) => h('span', { class: `chip ${m === 'auto' ? 'modo-auto' : `fuente-${m}`}` }, MODOS[m] || m || '—');

/* ---------- Control segmentado (grupo de radios accesible) ---------- */
/**
 * opciones: [{valor, etiqueta, clase, deshabilitado, titulo}]
 * Devuelve el fieldset; .fijar(valor) cambia la selección sin disparar alCambiar.
 */
export function segmentado({ etiqueta, opciones, valor: inicial, alCambiar, compacto = false, nombre = idUnico('seg') }) {
  const fs = h('fieldset', { class: `segmentado${compacto ? ' compacto' : ''}` }, h('legend', {}, etiqueta));
  const entradas = opciones.map((o) => {
    const entrada = h('input', {
      type: 'radio', name: nombre, value: o.valor ?? '', checked: (o.valor ?? null) === (inicial ?? null),
      disabled: o.deshabilitado, 'aria-label': o.etiquetaAccesible || undefined,
    });
    entrada.addEventListener('change', () => { if (entrada.checked) alCambiar?.(o.valor ?? null, fs); });
    fs.append(h('label', { title: o.titulo }, entrada, h('span', { class: o.clase || `v-${o.valor ?? 'heredar'}` }, o.etiqueta)));
    return entrada;
  });
  fs.fijar = (v) => { for (const e of entradas) e.checked = (e.value || null) === (v ?? null); };
  fs.ocupado = (b) => { fs.setAttribute('aria-busy', b ? 'true' : 'false'); for (const e of entradas) if (!e.dataset.fijo) e.disabled = b || e.dataset.bloq === '1'; };
  opciones.forEach((o, i) => { if (o.deshabilitado) entradas[i].dataset.bloq = '1'; });
  return fs;
}

export const OPCIONES_MODO = [
  { valor: 'real', etiqueta: 'Real' },
  { valor: 'auto', etiqueta: 'Auto' },
  { valor: 'simulated', etiqueta: 'Simulado' },
];
export const OPCIONES_REGLA = [{ valor: null, etiqueta: 'Heredar', clase: 'v-heredar' }, ...OPCIONES_MODO];

/** Barra apilada real/simulado/sin datos. */
export function barraFuentes(c, gruesa = false) {
  const t = (c.real || 0) + (c.simulated || 0) + (c.none || 0);
  const b = h('div', { class: `barra${gruesa ? ' gruesa' : ''}`, role: 'img',
    'aria-label': `Real ${c.real || 0}, simulado ${c.simulated || 0}, sin datos ${c.none || 0}` });
  if (t) for (const f of ['real', 'simulated', 'none']) if (c[f]) b.append(h('span', { class: f, style: { width: `${(100 * c[f]) / t}%` } }));
  return b;
}

export function debounce(fn, ms = 300) {
  let t;
  return (...a) => { clearTimeout(t); t = setTimeout(() => fn(...a), ms); };
}

/** Ejecuta fn cada ms mientras la pestaña está visible; devuelve la función para pararlo. */
export function cadaCierto(fn, ms) {
  let parado = false;
  const id = setInterval(() => { if (!parado && !document.hidden) fn(); }, ms);
  return () => { parado = true; clearInterval(id); };
}

/** Limita la concurrencia de una lista de tareas asíncronas. */
export async function enParalelo(items, n, fn) {
  const resultados = new Array(items.length);
  let i = 0;
  await Promise.all(Array.from({ length: Math.min(n, items.length) }, async () => {
    while (i < items.length) { const k = i++; resultados[k] = await fn(items[k], k); }
  }));
  return resultados;
}

export const cargando = (texto = 'Cargando…') => h('div', { class: 'cargando' }, texto);

/** Clima pedido y, si el servicio lo dice, el que está en uso. */
export function textoClima(s) {
  const pedido = s.weather_source === 'real' ? 'Real si hay dato' : 'Sintético';
  if (!s.weather_in_use) return pedido;
  const uso = s.weather_in_use === 'real' ? 'real' : 'sintético';
  return `${pedido} · en uso: ${uso}`;
}
