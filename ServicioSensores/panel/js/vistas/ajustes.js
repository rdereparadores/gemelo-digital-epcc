// Ajustes del servicio (GET/PATCH /settings) y acciones del enlace con SmartPoliTech.
import { obtener, parchear, publicar } from '../api.js';
import { bus, refrescarEnlace, ultimoEnlace } from '../estado.js';
import { aviso, cargando, entero, fecha, h, icono, idUnico, mostrarError, numero, relativo, vaciar } from '../ui.js';

const CAMPOS = [
  { grupo: 'Enlace con SmartPoliTech', camino: ['upstream', 'enabled'], tipo: 'bool', etiqueta: 'Sondear SmartPoliTech', ayuda: 'Si se desactiva, los sensores en modo auto pasan a simulado y los de modo real quedan sin datos.' },
  { grupo: 'Enlace con SmartPoliTech', camino: ['upstream', 'base_url'], tipo: 'url', etiqueta: 'URL base', ancho: true },
  { grupo: 'Enlace con SmartPoliTech', camino: ['upstream', 'datasource_id'], tipo: 'int', etiqueta: 'Id de la fuente de datos', min: 0 },
  { grupo: 'Enlace con SmartPoliTech', camino: ['upstream', 'database'], tipo: 'text', etiqueta: 'Base de datos' },
  { grupo: 'Enlace con SmartPoliTech', camino: ['upstream', 'poll_interval_s'], tipo: 'num', etiqueta: 'Intervalo de sondeo (s)', min: 5 },
  { grupo: 'Enlace con SmartPoliTech', camino: ['upstream', 'timeout_s'], tipo: 'num', etiqueta: 'Tiempo de espera (s)', min: 1 },
  { grupo: 'Enlace con SmartPoliTech', camino: ['upstream', 'stale_after_s'], tipo: 'num', etiqueta: 'Dato viejo a partir de (s)', min: 60, ayuda: 'En modo auto, un dato real más antiguo que esto se sustituye por el simulado.' },
  { grupo: 'Enlace con SmartPoliTech', camino: ['upstream', 'lookback_s'], tipo: 'num', etiqueta: 'Buscar el último dato real hasta (s)', min: 3600, max: 7776000, ayuda: 'Hasta dónde se mira hacia atrás para encontrar el último dato real: entre 3600 s (1 h) y 7 776 000 s (90 días). 604 800 s = 7 días.' },
  { grupo: 'Simulación', camino: ['simulation', 'tick_s'], tipo: 'num', etiqueta: 'Paso del motor (s de reloj real)', min: 0.1, paso: 'any' },
  { grupo: 'Simulación', camino: ['simulation', 'seed'], tipo: 'int', etiqueta: 'Semilla' },
  { grupo: 'Simulación', camino: ['simulation', 'history_interval_s'], tipo: 'num', etiqueta: 'Intervalo del histórico simulado (s)', min: 1, paso: 'any' },
  { grupo: 'Directo (SSE)', camino: ['stream', 'heartbeat_s'], tipo: 'num', etiqueta: 'Latido (s)', min: 1 },
  { grupo: 'CORS', camino: ['cors_origins'], tipo: 'lista', etiqueta: 'Orígenes permitidos (uno por línea)', ancho: true, ayuda: '«*» permite cualquier origen (útil para Unity WebGL en desarrollo).' },
];

const leer = (obj, camino) => camino.reduce((o, k) => (o == null ? undefined : o[k]), obj);

export function montar(contenedor) {
  let vivo = true;
  let ajustes = null;
  const cajaForm = h('div', {}, cargando());
  const cajaEnlace = h('div', {}, cargando());

  vaciar(contenedor, h('div', { class: 'vista' },
    h('div', { class: 'vista-cabecera' }, h('div', {}, h('h1', {}, 'Ajustes'),
      h('p', {}, 'Configuración del servicio. Los cambios se envían como JSON Merge Patch y solo incluyen los campos modificados.'))),
    h('div', { class: 'rejilla ajustes' },
      h('section', { class: 'tarjeta' }, h('header', {}, h('h2', {}, 'Configuración')), cajaForm),
      h('div', { class: 'rejilla' },
        h('section', { class: 'tarjeta' }, h('header', {}, h('h2', {}, 'Enlace con SmartPoliTech')), cajaEnlace),
        h('section', { class: 'tarjeta' }, h('header', {}, h('h2', {}, 'API')),
          h('ul', { class: 'lista-enlaces' },
            h('li', {}, h('a', { href: '/api/v1/docs', target: '_blank', rel: 'noopener' }, 'Documentación interactiva (OpenAPI) ', icono('externo', 'mini'))),
            h('li', {}, h('a', { href: '/api/v1/openapi.json', target: '_blank', rel: 'noopener' }, 'openapi.json ', icono('externo', 'mini'))),
            h('li', {}, h('a', { href: '/api/v1', target: '_blank', rel: 'noopener' }, 'Raíz de la API (/api/v1) ', icono('externo', 'mini'))),
            h('li', {}, h('a', { href: '/api/v1/state', target: '_blank', rel: 'noopener' }, 'Estado del campus (/api/v1/state) ', icono('externo', 'mini')))))))));

  /* ---------- Formulario ---------- */
  function pintarForm() {
    const controles = [];
    const grupos = new Map();
    for (const c of CAMPOS) {
      const actual = leer(ajustes, c.camino);
      if (actual === undefined) continue; // el servidor no tiene ese ajuste
      const id = idUnico('aj');
      let ctrl;
      if (c.tipo === 'bool') ctrl = h('input', { type: 'checkbox', id, checked: Boolean(actual) });
      else if (c.tipo === 'lista') ctrl = h('textarea', { id, rows: 3 }, (actual || []).join('\n'));
      else ctrl = h('input', {
        id, type: c.tipo === 'url' ? 'url' : c.tipo === 'text' ? 'text' : 'number', value: actual ?? '',
        min: c.min, max: c.max, step: c.paso || (c.tipo === 'int' ? 1 : 'any'), required: c.tipo !== 'text',
      });
      controles.push({ c, ctrl });
      const campo = c.tipo === 'bool'
        ? h('div', { class: 'campo completo' },
          h('label', { class: 'conmutador', for: id }, ctrl, h('span', {}, c.etiqueta)), c.ayuda ? h('span', { class: 'ayuda' }, c.ayuda) : null)
        : h('div', { class: c.ancho ? 'campo completo' : 'campo' },
          h('label', { for: id }, c.etiqueta), ctrl, c.ayuda ? h('span', { class: 'ayuda' }, c.ayuda) : null);
      if (!grupos.has(c.grupo)) grupos.set(c.grupo, []);
      grupos.get(c.grupo).push(campo);
    }

    const valorDe = ({ c, ctrl }) => {
      if (c.tipo === 'bool') return ctrl.checked;
      if (c.tipo === 'lista') return ctrl.value.split(/[\n,]/).map((x) => x.trim()).filter(Boolean);
      if (c.tipo === 'int') return Math.round(Number(ctrl.value));
      if (c.tipo === 'num') return Number(ctrl.value);
      return ctrl.value.trim();
    };

    const enviar = async (e) => {
      e.preventDefault();
      const parche = {};
      for (const x of controles) {
        const nuevo = valorDe(x);
        if (JSON.stringify(nuevo) === JSON.stringify(leer(ajustes, x.c.camino))) continue;
        let nodo = parche;
        x.c.camino.slice(0, -1).forEach((k) => { nodo = (nodo[k] ||= {}); });
        nodo[x.c.camino.at(-1)] = nuevo;
      }
      if (!Object.keys(parche).length) { aviso('info', 'Sin cambios', 'No has modificado ningún ajuste.'); return; }
      const boton = e.submitter;
      if (boton) boton.disabled = true;
      try {
        const r = await parchear('/api/v1/settings', parche);
        ajustes = r && typeof r === 'object' && Object.keys(r).length ? r : await obtener('/api/v1/settings');
        aviso('exito', 'Ajustes guardados', resumirParche(parche));
        pintarForm();
        refrescarEnlace().catch(() => {});
      } catch (err) { mostrarError(err, 'No se pudieron guardar los ajustes'); } finally { if (boton) boton.disabled = false; }
    };

    vaciar(cajaForm, h('form', { class: 'formulario', onsubmit: enviar },
      [...grupos].map(([g, campos]) => h('fieldset', {}, h('legend', {}, g), h('div', { class: 'formulario tres' }, campos))),
      h('details', {}, h('summary', {}, 'Ajustes completos (JSON)'), h('pre', { class: 'json' }, JSON.stringify(ajustes, null, 2))),
      h('div', { class: 'acciones fin' },
        h('button', { type: 'button', class: 'boton fantasma', onclick: pintarForm }, 'Descartar cambios'),
        h('button', { type: 'submit', class: 'boton primario' }, 'Guardar'))));
  }

  /* ---------- Enlace ---------- */
  // El botón se crea una vez para que no pierda el foco cuando llega un estado nuevo.
  const botonSondeo = h('button', { type: 'button', class: 'boton primario', onclick: (e) => sondear(e) }, icono('actualizar'), 'Sondear ahora');
  const cuerpoEnlace = h('div', {}, cargando());
  vaciar(cajaEnlace, cuerpoEnlace, h('div', { class: 'acciones mt' }, botonSondeo));
  function pintarEnlace(u = ultimoEnlace()) {
    if (!vivo || !u) return;
    const estado = u.activado === false ? ['Desactivado', 'estado-aviso'] : u.alcanzable === true ? ['Alcanzable', 'estado-ok']
      : u.alcanzable === false ? ['Inalcanzable', 'estado-mal'] : ['Desconocido', 'neutro'];
    vaciar(cuerpoEnlace,
      h('span', { class: `chip ${estado[1]}` }, estado[0]),
      h('dl', { class: 'dl' },
        h('dt', {}, 'Latencia'), h('dd', {}, u.latencia != null ? `${numero(u.latencia, 0)} ms` : '—'),
        h('dt', {}, 'Último sondeo'), h('dd', { title: fecha(u.ultimoSondeo) }, relativo(u.ultimoSondeo)),
        h('dt', {}, 'Sensores con algún dato'), h('dd', {}, u.conDatos != null ? `${entero(u.conDatos)}${u.totalReales != null ? ` de ${entero(u.totalReales)}` : ''}` : '—'),
        h('dt', {}, 'Sensores con dato fresco'), h('dd', {}, u.frescos != null ? entero(u.frescos) : '—'),
        h('dt', {}, 'Errores recientes'), h('dd', {}, entero(u.errores.length))));
  }

  async function sondear(e) {
    const boton = e.currentTarget;
    boton.disabled = true;
    try {
      const { res } = await publicar('/api/v1/upstream/refresh');
      aviso('info', 'Sondeo solicitado', `El servicio respondió ${res.status}. El estado se actualizará en unos segundos.`);
      for (const ms of [1500, 4000]) setTimeout(() => refrescarEnlace().catch(() => {}), ms);
    } catch (err) { mostrarError(err, 'No se pudo sondear'); } finally { setTimeout(() => { boton.disabled = false; }, 1500); }
  }

  const alEnlace = ({ detail }) => pintarEnlace(detail);
  bus.addEventListener('enlace', alEnlace);
  pintarEnlace();
  refrescarEnlace().catch((e) => mostrarError(e, 'Enlace con SmartPoliTech'));

  obtener('/api/v1/settings').then((a) => { if (!vivo) return; ajustes = a; pintarForm(); })
    .catch((e) => { mostrarError(e, 'Ajustes'); vaciar(cajaForm, h('div', { class: 'vacio' }, 'No se pudieron cargar los ajustes.')); });

  return { desmontar() { vivo = false; bus.removeEventListener('enlace', alEnlace); } };
}

function resumirParche(p, prefijo = '') {
  return Object.entries(p).map(([k, v]) => (v && typeof v === 'object' && !Array.isArray(v)
    ? resumirParche(v, `${prefijo}${k}.`) : `${prefijo}${k} = ${Array.isArray(v) ? v.join(', ') : v}`)).join(' · ');
}
