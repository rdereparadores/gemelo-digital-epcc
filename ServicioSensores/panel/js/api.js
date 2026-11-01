// Cliente mínimo de la API pública /api/v1 (ver docs/CONTRATO_API.md).

export const BASE = '/api/v1';

/** Error de la API con el problem+json (RFC 9457) ya normalizado. */
export class ErrorApi extends Error {
  constructor(problema) {
    super(problema.title || 'Error de la API');
    this.name = 'ErrorApi';
    this.problema = problema;
    this.status = problema.status ?? 0;
  }
}

function construirUrl(ruta, consulta) {
  const url = new URL(ruta.startsWith('/') ? ruta : `${BASE}/${ruta}`, location.origin);
  if (consulta) {
    for (const [k, v] of Object.entries(consulta)) {
      if (v === undefined || v === null || v === '') continue;
      url.searchParams.set(k, String(v));
    }
  }
  return url;
}

/** Convierte cualquier cuerpo de error en {type,title,status,detail,instance}. */
function normalizarProblema(cuerpo, res, url) {
  const p = (cuerpo && typeof cuerpo === 'object' && !Array.isArray(cuerpo)) ? { ...cuerpo } : {};
  if (Array.isArray(p.detail)) {
    // Errores de validación al estilo FastAPI
    p.detail = p.detail.map((e) => `${(e.loc || []).slice(1).join('.') || 'cuerpo'}: ${e.msg}`).join(' · ');
  } else if (p.detail && typeof p.detail === 'object') {
    p.detail = JSON.stringify(p.detail);
  }
  if (!p.title) p.title = res.status === 422 ? 'Petición no válida' : (res.statusText || `Error ${res.status}`);
  if (!p.detail && typeof cuerpo === 'string' && cuerpo.trim()) p.detail = cuerpo.trim().slice(0, 300);
  p.status = p.status ?? res.status;
  p.instance = p.instance ?? url.pathname;
  return p;
}

/**
 * Hace una petición y devuelve el JSON (o null si 204).
 * opciones: {method, body, consulta, signal, aceptar: [códigos de error que no lanzan], completa: devuelve {datos, res}}
 */
export async function pedir(ruta, opciones = {}) {
  const { method = 'GET', body, consulta, signal, aceptar = [], completa = false } = opciones;
  const url = construirUrl(ruta, consulta);
  const init = { method, signal, headers: { Accept: 'application/json, application/problem+json' } };
  if (body !== undefined) {
    init.body = JSON.stringify(body);
    init.headers['Content-Type'] = method === 'PATCH' ? 'application/merge-patch+json' : 'application/json';
  }
  let res;
  try {
    res = await fetch(url, init);
  } catch (e) {
    if (e.name === 'AbortError') throw e;
    throw new ErrorApi({ title: 'Sin conexión con el servicio', detail: `No se pudo contactar con ${url.pathname}.`, status: 0, instance: url.pathname });
  }
  let datos = null;
  if (res.status !== 204 && res.status !== 304) {
    const tipo = res.headers.get('content-type') || '';
    const texto = await res.text();
    if (texto && tipo.includes('json')) {
      try { datos = JSON.parse(texto); } catch { datos = texto; }
    } else {
      datos = texto || null;
    }
  }
  if (!res.ok && !aceptar.includes(res.status)) {
    throw new ErrorApi(normalizarProblema(datos, res, url));
  }
  return completa ? { datos, res } : datos;
}

export const obtener = (ruta, consulta, extra = {}) => pedir(ruta, { ...extra, consulta });
export const parchear = (ruta, body, extra = {}) => pedir(ruta, { ...extra, method: 'PATCH', body });
export const publicar = (ruta, body, extra = {}) => pedir(ruta, { ...extra, method: 'POST', body, completa: true });
export const borrar = (ruta, extra = {}) => pedir(ruta, { ...extra, method: 'DELETE' });

/** Acepta una colección paginada {items,total} o una lista sin envolver. */
export function lista(datos) {
  if (Array.isArray(datos)) return datos;
  if (datos && Array.isArray(datos.items)) return datos.items;
  return [];
}

export function total(datos) {
  if (Array.isArray(datos)) return datos.length;
  return datos?.total ?? lista(datos).length;
}

/** Recorre todas las páginas de una colección (límite 1000 por página). */
export async function todos(ruta, consulta = {}, { signal, max = 20000 } = {}) {
  const limite = 1000;
  let offset = 0;
  const salida = [];
  for (;;) {
    const d = await obtener(ruta, { ...consulta, limit: limite, offset }, { signal });
    const items = lista(d);
    salida.push(...items);
    const t = Array.isArray(d) ? items.length : (d?.total ?? items.length);
    offset += items.length;
    if (!items.length || offset >= t || salida.length >= max || Array.isArray(d)) break;
  }
  return salida;
}

export const ruta = {
  sensor: (id) => `/api/v1/sensors/${encodeURIComponent(id)}`,
  espacio: (id) => `/api/v1/spaces/${encodeURIComponent(id)}`,
  edificio: (id) => `/api/v1/buildings/${encodeURIComponent(id)}`,
  evento: (id) => `/api/v1/simulation/events/${encodeURIComponent(id)}`,
};

/** Datos del catálogo que cambian poco: se piden una vez por sesión. */
const cache = new Map();
export function cacheado(clave, fn) {
  if (!cache.has(clave)) {
    const p = fn().catch((e) => { cache.delete(clave); throw e; });
    cache.set(clave, p);
  }
  return cache.get(clave);
}

export const edificios = () => cacheado('edificios', () => todos('/api/v1/buildings'));
export const espaciosDe = (edificio) => cacheado(`espacios:${edificio || '*'}`, () => todos('/api/v1/spaces', { building: edificio }));
export const escenarios = () => cacheado('escenarios', async () => lista(await obtener('/api/v1/simulation/scenarios')));
