// Fuentes de datos: política de fuentes (defecto, edificio, estancia, tipo) y modo de cada sensor.
import { edificios, espaciosDe, lista, obtener, parchear, ruta, todos, total } from '../api.js';
import { horaSimulada } from '../estado.js';
import { FUENTES, MODOS, TIPOS_SENSOR, etiquetaTipo } from '../etiquetas.js';
import {
  OPCIONES_MODO, OPCIONES_REGLA, aviso, cargando, chipFuente, chipModo, debounce, enParalelo, entero, fecha, h,
  idUnico, mostrarError, relativo, resumenValores, segmentado, vaciar,
} from '../ui.js';

const FILTROS = ['building', 'space', 'kind', 'source', 'mode', 'q', 'real_available'];

export function montar(contenedor, rutaInicial) {
  let politica = null;
  let vivo = true;
  const filtros = leerFiltros(rutaInicial.consulta);
  let offset = Number(rutaInicial.consulta.get('offset')) || 0;
  let limite = Number(rutaInicial.consulta.get('limit')) || 50;
  let peticion = 0;
  let totalFiltrado = 0;

  /* ---------- Estructura ---------- */
  const cajaDefecto = h('div', {}, cargando());
  const cajaEdificios = h('div', {}, cargando());
  const cajaTipos = h('div', {}, cargando());
  const cajaEstancias = h('div', {}, cargando());
  const cajaTabla = h('div', { class: 'tabla-envoltorio' }, cargando());
  const textoTotal = h('span', { class: 'sub', 'aria-live': 'polite' });
  const cajaPaginacion = h('div', { class: 'paginacion' });
  const cajaMasivo = h('div', { class: 'masivo', role: 'group', 'aria-label': 'Acciones sobre todos los sensores filtrados' });

  const selEdificio = h('select', { id: idUnico('f') });
  const selEstancia = h('select', { id: idUnico('f') });
  const selTipo = h('select', { id: idUnico('f') }, h('option', { value: '' }, 'Todos'),
    Object.entries(TIPOS_SENSOR).map(([k, v]) => h('option', { value: k }, v)));
  const selFuente = h('select', { id: idUnico('f') }, h('option', { value: '' }, 'Todas'),
    Object.entries(FUENTES).map(([k, v]) => h('option', { value: k }, v)));
  const selModo = h('select', { id: idUnico('f') }, h('option', { value: '' }, 'Todos'),
    Object.entries(MODOS).map(([k, v]) => h('option', { value: k }, v)));
  const entradaQ = h('input', { type: 'search', id: idUnico('f'), placeholder: 'Id o nombre…', autocomplete: 'off' });
  const chkReal = h('input', { type: 'checkbox', id: idUnico('f') });
  const selLimite = h('select', { id: idUnico('f'), 'aria-label': 'Sensores por página' },
    [25, 50, 100, 200].map((n) => h('option', { value: n }, `${n} por página`)));

  const campo = (etq, ctrl, clase = '') => h('div', { class: `campo ${clase}` }, h('label', { for: ctrl.id }, etq), ctrl);

  vaciar(contenedor, h('div', { class: 'vista' },
    h('div', { class: 'vista-cabecera' }, h('div', {},
      h('h1', {}, 'Fuentes de datos'),
      h('p', {}, 'Decide de dónde sale el dato de cada sensor. Gana la regla más específica: sensor → tipo → estancia → edificio → por defecto. ',
        h('strong', {}, 'Auto'), ' sirve el dato real si es reciente y, si no, el simulado; ', h('strong', {}, 'Real'),
        ' nunca inventa: sin dato reciente no hay lectura.'))),
    h('div', { class: 'rejilla c2 arriba' },
      h('div', { class: 'rejilla' },
        h('section', { class: 'tarjeta' }, h('header', {}, h('h2', {}, 'Modo por defecto')), cajaDefecto),
        h('section', { class: 'tarjeta' }, h('header', {}, h('h2', {}, 'Reglas por edificio')), cajaEdificios),
        h('section', { class: 'tarjeta' }, h('header', {}, h('h2', {}, 'Reglas por estancia')), cajaEstancias)),
      h('section', { class: 'tarjeta' }, h('header', {}, h('h2', {}, 'Reglas por tipo de sensor')), cajaTipos)),
    h('section', { class: 'tarjeta plana', 'aria-labelledby': 'tit-sensores' },
      h('header', {}, h('h2', { id: 'tit-sensores' }, 'Sensores'), textoTotal),
      h('form', { class: 'filtros', style: { padding: '10px 14px' }, role: 'search', onsubmit: (e) => e.preventDefault() },
        campo('Edificio', selEdificio), campo('Estancia', selEstancia, 'ancho'), campo('Tipo', selTipo, 'ancho'),
        campo('Fuente efectiva', selFuente), campo('Modo resuelto', selModo), campo('Buscar', entradaQ, 'ancho'),
        h('label', { class: 'campo-check', style: { height: '30px' } }, chkReal, 'Solo con datos reales disponibles'),
        h('button', { type: 'button', class: 'boton fantasma', onclick: limpiarFiltros }, 'Limpiar filtros')),
      cajaMasivo, cajaTabla, cajaPaginacion),
  ));

  /* ---------- Política ---------- */
  async function cargarPolitica() {
    politica = await obtener('/api/v1/source-policy');
    for (const k of ['buildings', 'spaces', 'kinds', 'sensors']) politica[k] ||= {};
  }

  async function aplicarParche(parche, descripcion) {
    try {
      const r = await parchear('/api/v1/source-policy', parche);
      if (r && typeof r === 'object' && 'default' in r) {
        politica = r;
        for (const k of ['buildings', 'spaces', 'kinds', 'sensors']) politica[k] ||= {};
      } else await cargarPolitica();
      aviso('exito', 'Política actualizada', descripcion);
      pintarReglas();
      cargarSensores();
      return true;
    } catch (e) {
      mostrarError(e, 'No se pudo cambiar la política');
      pintarReglas();
      return false;
    }
  }

  let listaEdificios = [];
  // Los controles se crean una vez y luego solo se sincronizan, para no perder el foco del teclado.
  const controlesReglas = [];
  let clavesEstancias = null;
  function pintarReglas() {
    if (!vivo || !politica) return;
    if (controlesReglas.length) {
      for (const { fs, valor } of controlesReglas) fs.fijar(valor());
      const claves = JSON.stringify(politica.spaces);
      if (claves !== clavesEstancias) pintarEstancias();
      return;
    }
    const control = (cfg, valor) => { const fs = segmentado({ ...cfg, valor: valor() }); controlesReglas.push({ fs, valor }); return fs; };
    vaciar(cajaDefecto,
      control({ etiqueta: 'Modo por defecto', opciones: OPCIONES_MODO,
        alCambiar: (v) => aplicarParche({ default: v }, `Modo por defecto: ${MODOS[v]}`) }, () => politica.default),
      h('p', { class: 'sub', style: { marginTop: '10px' } }, 'Se aplica a todo sensor sin una regla más específica.'));

    vaciar(cajaEdificios, h('div', { class: 'reglas' }, listaEdificios.map((e) => fila(
      h('span', { class: 'regla-nombre' }, e.name, h('span', { class: 'mono' }, e.id)),
      control({ etiqueta: `Regla del edificio ${e.name}`, opciones: OPCIONES_REGLA, compacto: true,
        alCambiar: (v) => aplicarParche({ buildings: { [e.id]: v } }, `${e.id}: ${v ? MODOS[v] : 'hereda'}`) }, () => politica.buildings[e.id] ?? null),
    ))));

    vaciar(cajaTipos, h('div', { class: 'reglas' }, Object.entries(TIPOS_SENSOR).map(([k, v]) => fila(
      h('span', { class: 'regla-nombre', title: k }, v, h('span', { class: 'mono' }, k)),
      control({ etiqueta: `Regla del tipo ${v}`, opciones: OPCIONES_REGLA, compacto: true,
        alCambiar: (m) => aplicarParche({ kinds: { [k]: m } }, `${v}: ${m ? MODOS[m] : 'hereda'}`) }, () => politica.kinds[k] ?? null),
    ))));

    pintarEstancias();
  }

  const fila = (a, b) => h('div', { class: 'fila' }, a, h('div', {}, b));

  function pintarEstancias() {
    clavesEstancias = JSON.stringify(politica.spaces);
    const reglas = Object.entries(politica.spaces);
    const selE = h('select', { id: idUnico('re') }, h('option', { value: '' }, 'Edificio…'), listaEdificios.map((e) => h('option', { value: e.id }, `${e.id} · ${e.name}`)));
    const selS = h('select', { id: idUnico('re'), disabled: true }, h('option', { value: '' }, 'Estancia…'));
    let modoNuevo = 'simulated';
    selE.addEventListener('change', async () => {
      vaciar(selS, h('option', { value: '' }, 'Estancia…'));
      selS.disabled = !selE.value;
      if (!selE.value) return;
      try { (await espaciosDe(selE.value)).forEach((s) => selS.append(h('option', { value: s.id }, `${s.code || s.id} · ${s.name || ''}`))); }
      catch (e) { mostrarError(e); }
    });
    vaciar(cajaEstancias,
      reglas.length
        ? h('div', { class: 'reglas', style: { marginBottom: '12px' } }, reglas.map(([id, m]) => fila(
          h('span', { class: 'regla-nombre' }, h('a', { href: `#/explorador/${encodeURIComponent(id)}`, class: 'mono' }, id)),
          segmentado({ etiqueta: `Regla de la estancia ${id}`, opciones: OPCIONES_REGLA, valor: m, compacto: true,
            alCambiar: (v) => aplicarParche({ spaces: { [id]: v } }, `${id}: ${v ? MODOS[v] : 'hereda'}`) }))))
        : h('p', { class: 'sub', style: { marginBottom: '10px' } }, 'No hay reglas por estancia.'),
      h('div', { class: 'filtros' },
        h('div', { class: 'campo' }, h('label', { for: selE.id }, 'Edificio'), selE),
        h('div', { class: 'campo ancho' }, h('label', { for: selS.id }, 'Estancia'), selS),
        h('div', { class: 'campo', style: { width: 'auto' } }, h('span', { class: 'etiqueta' }, 'Modo'),
          segmentado({ etiqueta: 'Modo de la nueva regla', opciones: OPCIONES_MODO, valor: modoNuevo, compacto: true, alCambiar: (v) => { modoNuevo = v; } })),
        h('button', { type: 'button', class: 'boton', onclick: () => {
          if (!selS.value) { aviso('info', 'Elige una estancia', 'Selecciona edificio y estancia para crear la regla.'); return; }
          aplicarParche({ spaces: { [selS.value]: modoNuevo } }, `${selS.value}: ${MODOS[modoNuevo]}`);
        } }, 'Añadir regla')));
  }

  /* ---------- Filtros ---------- */
  function rellenarFiltros() {
    vaciar(selEdificio, h('option', { value: '' }, 'Todos'), listaEdificios.map((e) => h('option', { value: e.id }, `${e.id} · ${e.name}`)));
    selEdificio.value = filtros.building || '';
    selTipo.value = filtros.kind || '';
    selFuente.value = filtros.source || '';
    selModo.value = filtros.mode || '';
    entradaQ.value = filtros.q || '';
    chkReal.checked = filtros.real_available === 'true';
    selLimite.value = String(limite);
    return rellenarEstancias();
  }

  async function rellenarEstancias() {
    vaciar(selEstancia, h('option', { value: '' }, filtros.building ? 'Todas' : 'Elige un edificio'));
    selEstancia.disabled = !filtros.building;
    if (!filtros.building) return;
    try {
      const espacios = await espaciosDe(filtros.building);
      espacios.forEach((s) => selEstancia.append(h('option', { value: s.id }, `${s.code || s.id}${s.name ? ` · ${s.name}` : ''}`)));
      selEstancia.value = filtros.space || '';
    } catch (e) { mostrarError(e); }
  }

  const alCambiarFiltro = () => {
    filtros.building = selEdificio.value;
    filtros.kind = selTipo.value;
    filtros.source = selFuente.value;
    filtros.mode = selModo.value;
    filtros.q = entradaQ.value.trim();
    filtros.real_available = chkReal.checked ? 'true' : '';
    offset = 0;
    guardarEnHash();
    cargarSensores();
  };
  selEdificio.addEventListener('change', () => { filtros.space = ''; rellenarEstancias(); alCambiarFiltro(); });
  selEstancia.addEventListener('change', () => { filtros.space = selEstancia.value; alCambiarFiltro(); });
  for (const s of [selTipo, selFuente, selModo, chkReal]) s.addEventListener('change', alCambiarFiltro);
  entradaQ.addEventListener('input', debounce(alCambiarFiltro, 350));
  selLimite.addEventListener('change', () => { limite = Number(selLimite.value); offset = 0; guardarEnHash(); cargarSensores(); });

  function limpiarFiltros() {
    for (const k of FILTROS) filtros[k] = '';
    offset = 0;
    rellenarFiltros();
    guardarEnHash();
    cargarSensores();
  }

  function guardarEnHash() {
    const q = new URLSearchParams();
    for (const k of FILTROS) if (filtros[k]) q.set(k, filtros[k]);
    if (offset) q.set('offset', offset);
    if (limite !== 50) q.set('limit', limite);
    const s = q.toString();
    history.replaceState(null, '', `#/fuentes${s ? `?${s}` : ''}`);
  }

  const consultaApi = () => Object.fromEntries(FILTROS.filter((k) => filtros[k]).map((k) => [k, filtros[k]]));

  /* ---------- Tabla de sensores ---------- */
  async function cargarSensores() {
    const n = ++peticion;
    cajaTabla.setAttribute('aria-busy', 'true');
    try {
      const d = await obtener('/api/v1/sensors', { ...consultaApi(), limit: limite, offset });
      if (n !== peticion || !vivo) return;
      const sensores = lista(d);
      totalFiltrado = total(d);
      pintarTabla(sensores);
      pintarPaginacion();
      pintarMasivo();
      cargarLecturas(sensores, n);
    } catch (e) {
      if (n !== peticion) return;
      vaciar(cajaTabla, h('div', { class: 'vacio' }, 'No se pudo cargar la lista de sensores.'));
      mostrarError(e, 'Sensores');
    } finally { if (n === peticion) cajaTabla.removeAttribute('aria-busy'); }
  }

  const filasPorId = new Map();

  function pintarTabla(sensores) {
    filasPorId.clear();
    textoTotal.textContent = `${entero(totalFiltrado)} sensores`;
    if (!sensores.length) { vaciar(cajaTabla, h('div', { class: 'vacio' }, 'Ningún sensor cumple los filtros.')); return; }
    vaciar(cajaTabla, h('table', { class: 'tabla-sensores' },
      h('caption', { class: 'oculto' }, 'Sensores filtrados con su modo y fuente'),
      h('thead', {}, h('tr', {},
        h('th', { scope: 'col' }, 'Sensor'), h('th', { scope: 'col' }, 'Tipo · estancia'),
        h('th', { scope: 'col' }, 'Excepción del sensor'), h('th', { scope: 'col' }, 'Modo pedido'),
        h('th', { scope: 'col' }, 'Fuente efectiva'), h('th', { scope: 'col' }, 'Última lectura'), h('th', { scope: 'col' }, 'Último dato real'))),
      h('tbody', {}, sensores.map(filaSensor))));
  }

  function filaSensor(s) {
    const celdaModo = h('td', {});
    const celdaFuente = h('td', {});
    const celdaLectura = h('td', { class: 'lectura' }, h('span', { class: 'sub' }, '…'));
    const pintarEstado = (x) => {
      vaciar(celdaModo, chipModo(x.mode), h('div', { class: 'origen' }, origenModo(x, politica)));
      vaciar(celdaFuente, chipFuente(x.effective_source));
    };
    const control = segmentado({
      etiqueta: `Modo del sensor ${s.name}`, compacto: true, valor: excepcion(s, politica),
      opciones: OPCIONES_REGLA.map((o) => (o.valor === 'real' && !s.real_available
        ? { ...o, deshabilitado: true, titulo: 'No existe en SmartPoliTech: solo puede ser simulado' } : o)),
      alCambiar: async (v, fs) => {
        fs.ocupado(true);
        try {
          const r = await parchear(ruta.sensor(s.id), { mode: v });
          if (v) politica.sensors[s.id] = v; else delete politica.sensors[s.id];
          const nuevo = r && typeof r === 'object' && r.id ? r : await obtener(ruta.sensor(s.id));
          Object.assign(s, nuevo);
          if ('mode_override' in s) s.mode_override = v;
          pintarEstado(s);
          if ('latest' in nuevo) pintarLectura(celdaLectura, nuevo.latest);
          else {
            obtener(`${ruta.sensor(s.id)}/observations/latest`, null, { aceptar: [404] })
              .then((o) => pintarLectura(celdaLectura, o?.sensor_id ? o : null)).catch(() => {});
          }
          aviso('exito', 'Sensor actualizado', `${s.id}: ${v ? MODOS[v] : 'hereda la política'} → fuente ${FUENTES[s.effective_source] || s.effective_source}`);
        } catch (e) {
          mostrarError(e, 'No se pudo cambiar el modo');
          fs.fijar(excepcion(s, politica));
        } finally { fs.ocupado(false); }
      },
    });
    pintarEstado(s);
    const tr = h('tr', {},
      h('td', { class: 'celda-sensor' },
        h('a', { class: 'nombre', href: `#/explorador/${encodeURIComponent(s.space_id)}/${encodeURIComponent(s.id)}`, title: s.name }, s.name),
        h('span', { class: 'id', title: s.real_available ? s.id : `${s.id} (solo simulado)` }, s.id)),
      h('td', { class: 'celda-tipo' }, etiquetaTipo(s.kind),
        h('a', { class: 'id', href: `#/explorador/${encodeURIComponent(s.space_id)}` }, s.space_id)),
      h('td', {}, control), celdaModo, celdaFuente, celdaLectura,
      h('td', { class: 'nowrap', title: fecha(s.last_real_time) }, s.last_real_time ? relativo(s.last_real_time) : h('span', { class: 'sub' }, s.real_available ? 'nunca' : 'no aplica')));
    filasPorId.set(s.id, celdaLectura);
    return tr;
  }

  /** Últimas lecturas: el sensor ya trae `latest`; si no, una petición agregada y, en último caso, por sensor. */
  async function cargarLecturas(sensores, n) {
    const mapa = new Map();
    if (sensores.every((s) => 'latest' in s)) {
      for (const s of sensores) if (s.latest) mapa.set(s.id, s.latest);
    } else {
      try {
        const c = consultaApi();
        const d = await obtener('/api/v1/observations/latest', { building: c.building, space: c.space, kind: c.kind });
        for (const o of lista(d)) mapa.set(o.sensor_id, o);
      } catch { /* se intenta por sensor */ }
      if (n !== peticion) return;
      const faltan = sensores.filter((s) => !mapa.has(s.id));
      if (faltan.length && faltan.length <= 60) {
        await enParalelo(faltan, 6, async (s) => {
          try {
            const o = await obtener(`${ruta.sensor(s.id)}/observations/latest`, null, { aceptar: [404] });
            if (o && o.sensor_id) mapa.set(s.id, o);
          } catch { /* sin lectura */ }
        });
      }
      if (n !== peticion) return;
    }
    for (const s of sensores) {
      const celda = filasPorId.get(s.id);
      if (celda) pintarLectura(celda, mapa.get(s.id));
    }
  }

  function pintarLectura(celda, o) {
    if (!o) { vaciar(celda, h('span', { class: 'sub' }, 'sin lecturas')); return; }
    const txt = resumenValores(o.values);
    vaciar(celda, h('span', { class: 'valores', title: txt }, txt),
      h('span', { title: fecha(o.time) }, relativo(o.time, o.source === 'real' ? Date.now() : horaSimulada()?.getTime()), ' · ', h('span', { class: o.source === 'real' ? 'etiqueta-real' : 'etiqueta-sim' }, FUENTES[o.source] || o.source)));
  }

  function pintarPaginacion() {
    const pagina = Math.floor(offset / limite) + 1;
    const paginas = Math.max(1, Math.ceil(totalFiltrado / limite));
    const ir = (o) => { offset = Math.max(0, o); guardarEnHash(); cargarSensores(); cajaTabla.scrollIntoView({ block: 'nearest' }); };
    vaciar(cajaPaginacion,
      h('span', { class: 'sub' }, totalFiltrado ? `${entero(offset + 1)}–${entero(Math.min(offset + limite, totalFiltrado))} de ${entero(totalFiltrado)}` : '0 resultados'),
      h('div', { class: 'acciones' },
        selLimite,
        h('button', { type: 'button', class: 'boton mini', disabled: offset === 0, onclick: () => ir(0), 'aria-label': 'Primera página' }, '«'),
        h('button', { type: 'button', class: 'boton mini', disabled: offset === 0, onclick: () => ir(offset - limite) }, '‹ Anterior'),
        h('span', { class: 'sub' }, `Página ${pagina} de ${paginas}`),
        h('button', { type: 'button', class: 'boton mini', disabled: offset + limite >= totalFiltrado, onclick: () => ir(offset + limite) }, 'Siguiente ›'),
        h('button', { type: 'button', class: 'boton mini', disabled: offset + limite >= totalFiltrado, onclick: () => ir((paginas - 1) * limite), 'aria-label': 'Última página' }, '»')));
  }

  /* ---------- Acciones masivas ---------- */
  function pintarMasivo() {
    const hayFiltro = FILTROS.some((k) => filtros[k]);
    const n = entero(totalFiltrado);
    const boton = (txt, clase, fn) => h('button', { type: 'button', class: `boton mini ${clase}`, disabled: !totalFiltrado, onclick: fn }, txt);
    vaciar(cajaMasivo,
      h('span', { class: 'sub' }, hayFiltro ? `Aplicar a los ${n} sensores filtrados:` : `Aplicar a los ${n} sensores (sin filtro):`),
      boton('Real', 'real', () => masivo('real')),
      boton('Auto', '', () => masivo('auto')),
      boton('Simulado', 'simulated', () => masivo('simulated')),
      boton('Quitar excepciones', '', () => masivo(null)));
  }

  async function masivo(modo) {
    const etiqueta = modo ? `«${MODOS[modo]}»` : 'heredar (quitar la excepción)';
    if (!confirm(`¿Poner ${etiqueta} en los ${totalFiltrado} sensores filtrados?\nSe guardará como excepción por sensor en la política de fuentes.`)) return;
    try {
      const sensores = await todos('/api/v1/sensors', consultaApi());
      const cambios = {};
      let omitidos = 0;
      for (const s of sensores) {
        if (modo === 'real' && !s.real_available) { omitidos++; continue; }
        if (modo === null) { if (politica.sensors[s.id]) cambios[s.id] = null; continue; }
        if (politica.sensors[s.id] !== modo) cambios[s.id] = modo;
      }
      const cuantos = Object.keys(cambios).length;
      if (!cuantos) { aviso('info', 'Nada que cambiar', omitidos ? `${omitidos} sensores solo simulados no pueden pasar a real.` : 'Los sensores ya estaban así.'); return; }
      await aplicarParche({ sensors: cambios },
        `${cuantos} sensores → ${modo ? MODOS[modo] : 'heredan'}${omitidos ? ` (${omitidos} solo simulados omitidos)` : ''}`);
    } catch (e) { mostrarError(e, 'Acción masiva'); }
  }

  /* ---------- Arranque ---------- */
  (async () => {
    try {
      [listaEdificios] = await Promise.all([edificios(), cargarPolitica()]);
      if (!vivo) return;
      pintarReglas();
      await rellenarFiltros();
      cargarSensores();
    } catch (e) {
      mostrarError(e, 'Fuentes de datos');
      for (const c of [cajaDefecto, cajaEdificios, cajaTipos, cajaEstancias, cajaTabla]) vaciar(c, h('div', { class: 'vacio' }, 'No disponible.'));
    }
  })();

  return {
    desmontar() { vivo = false; peticion++; },
    actualizar(r) {
      // Cambio de hash dentro de la sección (p. ej. un enlace desde el resumen).
      const nuevos = leerFiltros(r.consulta);
      if (FILTROS.every((k) => (nuevos[k] || '') === (filtros[k] || ''))) return;
      Object.assign(filtros, nuevos);
      offset = Number(r.consulta.get('offset')) || 0;
      rellenarFiltros();
      cargarSensores();
    },
  };
}

function leerFiltros(consulta) {
  const f = {};
  for (const k of FILTROS) f[k] = consulta.get(k) || '';
  return f;
}

/** Modo propio del sensor (excepción) o null: `mode_override` si el servicio lo da; si no, la política. */
function excepcion(s, p) {
  if ('mode_override' in s) return s.mode_override ?? null;
  return p?.sensors?.[s.id] ?? null;
}

function origenModo(s, p) {
  if (!p) return '';
  if (!s.real_available) return 'solo simulado';
  if (excepcion(s, p)) return 'excepción del sensor';
  if (p.kinds?.[s.kind]) return 'regla de tipo';
  if (p.spaces?.[s.space_id]) return 'regla de estancia';
  if (p.buildings?.[s.building_id]) return 'regla de edificio';
  return 'por defecto';
}
