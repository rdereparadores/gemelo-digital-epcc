// Explorador: edificios → estancias → sensores, con el estado de la estancia, histórico y directo (SSE).
import { BASE, edificios, espaciosDe, lista, obtener, ruta, todos } from '../api.js';
import { horaSimulada } from '../estado.js';
import {
  ALERTAS, CONFORT, FUENTES, FUENTES_METRICA, etiquetaEspacio, etiquetaPlanta, etiquetaPropiedad, etiquetaTipo, etiquetaUnidad,
} from '../etiquetas.js';
import { crearGrafica } from '../grafica.js';
import {
  cadaCierto, cargando, chipFuente, chipModo, entero, esBooleana, fecha, h, iconoGravedad, idUnico, mostrarError, numero, relativo,
  resumenValores, segmentado, vaciar, valor,
} from '../ui.js';

const RANGOS = [
  { valor: '1h', etiqueta: '1 h', ms: 3600e3 },
  { valor: '6h', etiqueta: '6 h', ms: 6 * 3600e3 },
  { valor: '24h', etiqueta: '24 h', ms: 86400e3 },
  { valor: '7d', etiqueta: '7 días', ms: 7 * 86400e3 },
];

export function montar(contenedor, rutaInicial) {
  let vivo = true;
  let listaEdificios = [];
  let edificioSel = null; // id
  let espacios = []; // del edificio seleccionado
  let espacioSel = null; // objeto Space
  let sensoresEspacio = [];
  let sensorSel = null; // objeto Sensor
  let propiedadSel = null;
  let rango = '6h';
  let fuenteSse = null;
  let grafica = null;
  let peticionGrafica = 0;
  let filtroEstancias = '';

  const botonesEdificio = h('div', { class: 'lista-edificios', role: 'group', 'aria-label': 'Edificios' });
  const buscador = h('input', { type: 'search', id: idUnico('ex'), placeholder: 'Filtrar estancias…', autocomplete: 'off' });
  const listaEstancias = h('div', { class: 'lista', role: 'navigation', 'aria-label': 'Estancias' });
  const panel = h('div', { class: 'panel-explorador' });

  vaciar(contenedor, h('div', { class: 'vista' },
    h('div', { class: 'vista-cabecera' }, h('div', {}, h('h1', {}, 'Explorador'),
      h('p', {}, 'Recorre edificios y estancias, consulta su estado agregado y el histórico de cada sensor con lecturas en directo.'))),
    h('div', { class: 'explorador' },
      h('aside', { class: 'arbol', 'aria-label': 'Edificios y estancias' },
        botonesEdificio,
        h('label', { class: 'oculto', for: buscador.id }, 'Filtrar estancias'), buscador,
        listaEstancias),
      panel)));

  buscador.addEventListener('input', () => { filtroEstancias = buscador.value.trim().toLowerCase(); pintarEstancias(); });

  /* ---------- Navegación ---------- */
  const irA = (...partes) => { location.hash = `#/explorador/${partes.filter(Boolean).map(encodeURIComponent).join('/')}`; };

  async function aplicarRuta(r) {
    const [a, b] = r.partes;
    try {
      if (!a) {
        // Sin ruta: abre el primer edificio con su estado, para no dejar el panel vacío.
        if (!edificioSel && listaEdificios[0]) { await elegirEdificio(listaEdificios[0].id); await pintarEdificio(); }
        return;
      }
      if (listaEdificios.some((e) => e.id === a)) {
        await elegirEdificio(a);
        if (espacioSel) { espacioSel = null; cerrarSse(); }
        pintarEstancias();
        await pintarEdificio();
        return;
      }
      // Es una estancia
      let espacio = espacios.find((s) => s.id === a);
      if (!espacio) espacio = await obtener(ruta.espacio(a));
      await elegirEdificio(espacio.building_id);
      if (espacioSel?.id !== espacio.id) await elegirEspacio(espacio);
      if (b) { if (sensorSel?.id !== b) await elegirSensor(b); }
      else if (sensorSel) { sensorSel = null; pintarSensores(); pintarDetalle(); }
    } catch (e) { mostrarError(e, 'Explorador'); }
  }

  async function elegirEdificio(id) {
    if (edificioSel === id) return;
    edificioSel = id;
    for (const b of botonesEdificio.children) b.setAttribute('aria-pressed', String(b.dataset.id === id));
    vaciar(listaEstancias, cargando());
    espacios = await espaciosDe(id);
    pintarEstancias();
  }

  function pintarEstancias() {
    const f = filtroEstancias;
    const visibles = espacios.filter((s) => !f || `${s.id} ${s.code} ${s.name || ''} ${etiquetaEspacio(s.type)}`.toLowerCase().includes(f));
    if (!visibles.length) { vaciar(listaEstancias, h('div', { class: 'vacio' }, 'Sin estancias.')); return; }
    const porPlanta = new Map();
    const orden = listaEdificios.find((e) => e.id === edificioSel)?.floors || [];
    for (const p of orden) porPlanta.set(p, []);
    for (const s of visibles) { if (!porPlanta.has(s.floor)) porPlanta.set(s.floor, []); porPlanta.get(s.floor).push(s); }
    vaciar(listaEstancias, [...porPlanta].filter(([, ss]) => ss.length).map(([planta, ss]) => h('div', { role: 'group', 'aria-label': planta ? etiquetaPlanta(planta) : 'Sin planta' },
      h('div', { class: 'grupo-planta' }, planta ? `${etiquetaPlanta(planta)} · ${planta}` : 'Sin planta'),
      ss.map((s) => h('button', {
        type: 'button', class: 'item-estancia', 'aria-current': espacioSel?.id === s.id ? 'true' : 'false',
        title: `${s.id} · ${etiquetaEspacio(s.type)}`, onclick: () => irA(s.id),
      }, h('span', { class: 'c' }, s.code || s.id), h('span', { class: 'n' }, s.name || etiquetaEspacio(s.type)),
      h('span', { class: 'cnt', 'aria-label': `${s.sensor_count} sensores` }, s.sensor_count ?? ''))))));
    listaEstancias.querySelector('[aria-current="true"]')?.scrollIntoView({ block: 'nearest' });
  }

  /* ---------- Vista de edificio ---------- */
  async function pintarEdificio() {
    const e = listaEdificios.find((x) => x.id === edificioSel);
    vaciar(panel, h('section', { class: 'tarjeta' },
      h('header', {}, h('h2', {}, `${e.name} · ${e.id}`), h('span', { class: 'sub' }, `Unity ${e.unity_id ?? '—'}`)),
      cargando('Cargando el estado del edificio…')));
    try {
      const est = await obtener(`${ruta.edificio(e.id)}/state`);
      if (!vivo || espacioSel || edificioSel !== e.id) return;
      const resumen = lista(est.spaces);
      vaciar(panel,
        h('section', { class: 'tarjeta' },
          h('header', {}, h('h2', {}, `${e.name} · ${e.id}`), h('span', { class: 'sub' }, `Estado a ${fecha(est.time)}`)),
          h('div', { class: 'contadores mb' },
            contador(entero(e.space_count), 'estancias'), contador(entero(e.sensor_count), 'sensores'),
            contador(e.unity_id ?? '—', 'id en Unity'), contador((e.floors || []).join(' · '), 'plantas')),
          metricas(est.metrics), alertas(est.alerts)),
        resumen.length ? h('section', { class: 'tarjeta plana' },
          h('header', {}, h('h2', {}, 'Estancias')),
          h('div', { class: 'tabla-envoltorio' }, h('table', {},
            h('thead', {}, h('tr', {}, h('th', {}, 'Estancia'), h('th', {}, 'Confort'), h('th', {}, 'Métricas'), h('th', {}, 'Alertas'))),
            h('tbody', {}, resumen.map((s) => h('tr', { class: 'clicable', onclick: () => irA(s.space_id) },
              h('td', { class: 'mono' }, h('a', { href: `#/explorador/${encodeURIComponent(s.space_id)}` }, s.space_id)),
              h('td', {}, chipConfort(s.comfort)),
              h('td', { class: 'lectura' }, h('span', { class: 'valores' }, resumenValores(lista(s.metrics)))),
              h('td', {}, lista(s.alerts).length ? h('span', { class: 'chip estado-aviso' }, `${lista(s.alerts).length}`) : '—'))))))) : null);
    } catch (err) {
      mostrarError(err, 'Estado del edificio');
    }
  }

  /* ---------- Vista de estancia ---------- */
  const cajaEstado = h('div', {});
  const cajaSensores = h('div', { class: 'tabla-envoltorio' });
  const cajaDetalle = h('section', { class: 'tarjeta', 'aria-labelledby': 'tit-detalle' });
  const indicadorSse = h('span', { class: 'sse', role: 'status' }, h('span', { class: 'punto', 'aria-hidden': 'true' }), h('span', { class: 't' }, 'Directo: —'));
  const ultimas = new Map(); // sensor_id → Observation

  async function elegirEspacio(espacio) {
    espacioSel = espacio;
    sensorSel = null;
    sensoresEspacio = [];
    ultimas.clear();
    pintarEstancias();
    vaciar(panel,
      h('section', { class: 'tarjeta' },
        h('header', {},
          h('div', {}, h('h2', { class: 'titulo' },
            espacio.name || espacio.code || espacio.id),
          h('div', { class: 'sub' }, h('span', { class: 'mono' }, espacio.id), ` · ${etiquetaEspacio(espacio.type)} · ${etiquetaPlanta(espacio.floor)}`,
            espacio.unity_room ? [' · Unity ', h('span', { class: 'mono' }, espacio.unity_room)] : ' · sin sala en Unity')),
          h('div', { class: 'acciones' }, indicadorSse,
            h('a', { class: 'boton mini', href: `#/fuentes?building=${encodeURIComponent(espacio.building_id)}&space=${encodeURIComponent(espacio.id)}` }, 'Fuentes de esta estancia'))),
        cajaEstado),
      h('section', { class: 'tarjeta plana' }, h('header', {}, h('h2', {}, 'Sensores de la estancia')), cajaSensores),
      cajaDetalle);
    vaciar(cajaEstado, cargando());
    vaciar(cajaSensores, cargando());
    vaciar(cajaDetalle, h('p', { class: 'sub' }, 'Elige un sensor de la lista para ver su histórico.'));
    abrirSse(espacio.id);
    await Promise.all([refrescarEstado(), cargarSensores()]);
  }

  async function refrescarEstado() {
    if (!espacioSel) return;
    const id = espacioSel.id;
    try {
      const st = await obtener(`${ruta.espacio(id)}/state`);
      if (!vivo || espacioSel?.id !== id) return;
      vaciar(cajaEstado,
        h('div', { class: 'contadores mb' },
          h('div', { class: 'contador' }, h('span', { class: 'e' }, 'Confort'), chipConfort(st.comfort)),
          contador(`${entero(st.windows_open)} / ${entero(st.windows_total)}`, 'ventanas abiertas'),
          contador(`${entero(st.radiators_active)} / ${entero(st.radiators_total)}`, 'radiadores activos'),
          contador(entero(espacioSel.sensor_count), 'sensores'),
          h('span', { class: 'sub derecha', title: fecha(st.time) }, `Estado: ${relativo(st.time, horaSimulada()?.getTime())}`)),
        metricas(st.metrics), alertas(st.alerts));
    } catch (e) {
      vaciar(cajaEstado, h('p', { class: 'sub' }, `Estado no disponible: ${e.problema?.title || e.message}`));
    }
  }

  async function cargarSensores() {
    const id = espacioSel.id;
    try {
      const ss = await todos('/api/v1/sensors', { space: id });
      // El sensor trae su última observación (`latest`); si no, se pide aparte.
      const ult = ss.every((s) => 'latest' in s) ? ss.map((s) => s.latest).filter(Boolean)
        : lista(await obtener('/api/v1/observations/latest', { space: id }).catch(() => null));
      if (!vivo || espacioSel?.id !== id) return;
      sensoresEspacio = ss;
      for (const o of ult) if (!ultimas.has(o.sensor_id) || ultimas.get(o.sensor_id).time < o.time) ultimas.set(o.sensor_id, o);
      pintarSensores();
    } catch (e) {
      vaciar(cajaSensores, h('div', { class: 'vacio' }, 'No se pudieron cargar los sensores.'));
      mostrarError(e, 'Sensores de la estancia');
    }
  }

  const celdasVivas = new Map(); // sensor_id → td
  function pintarSensores() {
    celdasVivas.clear();
    if (!sensoresEspacio.length) { vaciar(cajaSensores, h('div', { class: 'vacio' }, 'Esta estancia no tiene sensores.')); return; }
    vaciar(cajaSensores, h('table', {},
      h('thead', {}, h('tr', {}, h('th', {}, 'Sensor'), h('th', {}, 'Tipo'), h('th', {}, 'Modo'), h('th', {}, 'Fuente'), h('th', {}, 'Valores en directo'))),
      h('tbody', {}, sensoresEspacio.map((s) => {
        const celda = h('td', { class: 'lectura' });
        celdasVivas.set(s.id, celda);
        pintarValores(s.id);
        const href = `#/explorador/${encodeURIComponent(espacioSel.id)}/${encodeURIComponent(s.id)}`;
        return h('tr', { class: `clicable${sensorSel?.id === s.id ? ' seleccionado' : ''}`, onclick: (e) => { if (e.target.tagName !== 'A') location.hash = href; } },
          h('td', { class: 'celda-sensor' }, h('a', { class: 'nombre', href, 'aria-current': sensorSel?.id === s.id ? 'true' : undefined }, s.name), h('span', { class: 'id' }, s.id)),
          h('td', {}, etiquetaTipo(s.kind)),
          h('td', {}, chipModo(s.mode)),
          h('td', {}, chipFuente(s.effective_source)),
          celda);
      }))));
  }

  function pintarValores(id, destello = false) {
    const celda = celdasVivas.get(id);
    if (!celda) return;
    const o = ultimas.get(id);
    if (!o) { vaciar(celda, h('span', { class: 'sub' }, 'sin lecturas')); return; }
    const txt = resumenValores(o.values);
    vaciar(celda, h('span', { class: 'valores', title: txt }, txt),
      h('span', { title: fecha(o.time) }, new Date(o.time).toLocaleTimeString('es-ES'), ' · ',
        h('span', { class: o.source === 'real' ? 'etiqueta-real' : 'etiqueta-sim' }, FUENTES[o.source] || o.source)));
    if (destello) { const tr = celda.parentElement; tr.classList.remove('destello'); void tr.offsetWidth; tr.classList.add('destello'); }
  }

  /* ---------- Sensor y gráfica ---------- */
  async function elegirSensor(id) {
    let s = sensoresEspacio.find((x) => x.id === id);
    if (!s) s = await obtener(ruta.sensor(id));
    sensorSel = s;
    const props = s.properties || [];
    if (!props.some((p) => p.name === propiedadSel)) propiedadSel = props[0]?.name || null;
    pintarSensores();
    pintarDetalle();
  }

  function pintarDetalle() {
    grafica?.destruir();
    grafica = null;
    if (!sensorSel) { vaciar(cajaDetalle, h('p', { class: 'sub' }, 'Elige un sensor de la lista para ver su histórico.')); return; }
    const s = sensorSel;
    const cajaGrafica = h('div', {});
    const props = s.properties || [];
    vaciar(cajaDetalle,
      h('header', {},
        h('div', {}, h('h2', { id: 'tit-detalle', class: 'titulo pequeno' }, s.name),
          h('div', { class: 'sub mono' }, s.id)),
        h('div', { class: 'acciones' }, chipModo(s.mode), chipFuente(s.effective_source),
          h('a', { class: 'boton mini', href: `#/fuentes?building=${encodeURIComponent(s.building_id)}&space=${encodeURIComponent(s.space_id)}&q=${encodeURIComponent(s.id)}` }, 'Cambiar fuente'))),
      h('div', { class: 'lado mb' },
        props.length > 1 ? segmentado({ etiqueta: 'Propiedad', compacto: true, valor: propiedadSel,
          opciones: props.map((p) => ({ valor: p.name, etiqueta: etiquetaPropiedad(p.name), clase: 'v-prop' })),
          alCambiar: (v) => { propiedadSel = v; cargarHistorico(); } }) : h('strong', {}, etiquetaPropiedad(propiedadSel)),
        h('span', { class: 'derecha' }),
        segmentado({ etiqueta: 'Intervalo', compacto: true, valor: rango, opciones: RANGOS,
          alCambiar: (v) => { rango = v; cargarHistorico(); } })),
      cajaGrafica,
      h('dl', { class: 'dl ancha' },
        h('dt', {}, 'Tipo'), h('dd', {}, etiquetaTipo(s.kind)),
        h('dt', {}, 'En SmartPoliTech'), h('dd', {}, s.real_available ? 'Sí' : 'No (solo simulado)'),
        h('dt', {}, 'Último dato real'), h('dd', { title: fecha(s.last_real_time) }, s.last_real_time ? `${fecha(s.last_real_time)} (${relativo(s.last_real_time)})` : '—'),
        h('dt', {}, 'Propiedades'), h('dd', { class: 'completa' },
          props.map((p) => `${etiquetaPropiedad(p.name)} (${etiquetaUnidad(p.unit) || p.type})`).join(' · '))));
    grafica = crearGrafica(cajaGrafica);
    cargarHistorico();
  }

  async function cargarHistorico() {
    if (!sensorSel || !propiedadSel || !grafica) return;
    const n = ++peticionGrafica;
    const s = sensorSel;
    const prop = (s.properties || []).find((p) => p.name === propiedadSel) || { name: propiedadSel };
    const ms = RANGOS.find((r) => r.valor === rango).ms;
    try {
      // El final del intervalo lo marca el servicio (`to` sin parámetros): hora real para datos de
      // SmartPoliTech y hora simulada para el búfer del simulador. Si no lo da, se usa el reloj simulado.
      let hasta = (horaSimulada() || new Date()).getTime();
      const sonda = await obtener(`${ruta.sensor(s.id)}/observations`, { limit: 1 }).catch(() => null);
      if (sonda?.to) hasta = new Date(sonda.to).getTime();
      if (n !== peticionGrafica) return;
      const desde = hasta - ms;
      const obs = await todos(`${ruta.sensor(s.id)}/observations`,
        { from: new Date(desde).toISOString(), to: new Date(hasta + 60e3).toISOString() }, { max: 6000 });
      if (n !== peticionGrafica || !grafica) return;
      const puntos = [];
      for (const o of obs) {
        const v = (o.values || []).find((x) => x.property === propiedadSel);
        const t = new Date(o.time).getTime();
        if (v && v.value !== null && t >= desde) puntos.push({ t, v: Number(v.value), fuente: o.source || 'simulated' });
      }
      const ult = puntos.reduce((m, p) => Math.max(m, p.t), 0);
      grafica.dibujar({
        puntos, desde, hasta: Math.max(hasta, ult), ventana: ms, vivo: true,
        unidad: prop.unit, booleana: prop.type === 'boolean' || esBooleana(prop.name, prop.unit),
        nombre: `${etiquetaPropiedad(prop.name)} de ${s.name}`,
      });
    } catch (e) {
      if (n === peticionGrafica) mostrarError(e, 'Histórico');
    }
  }

  /* ---------- Directo (SSE) ---------- */
  function abrirSse(espacioId) {
    cerrarSse();
    if (typeof EventSource === 'undefined') return;
    const url = `${BASE}/observations/stream?space=${encodeURIComponent(espacioId)}`;
    const es = new EventSource(url);
    fuenteSse = es;
    estadoSse('conectando', '');
    es.addEventListener('open', () => estadoSse('conectado', 'ok'));
    es.addEventListener('error', () => estadoSse(es.readyState === EventSource.CLOSED ? 'cerrado' : 'reconectando…', 'mal'));
    es.addEventListener('observation', (ev) => {
      let o;
      try { o = JSON.parse(ev.data); } catch { return; }
      if (!o?.sensor_id) return;
      ultimas.set(o.sensor_id, o);
      pintarValores(o.sensor_id, true);
      estadoSse(`conectado · ${new Date().toLocaleTimeString('es-ES')}`, 'ok');
      if (sensorSel?.id === o.sensor_id && grafica) {
        const v = (o.values || []).find((x) => x.property === propiedadSel);
        if (v && v.value !== null) grafica.anadir({ t: new Date(o.time).getTime(), v: Number(v.value), fuente: o.source || 'simulated' });
      }
    });
  }
  function estadoSse(texto, clase) {
    indicadorSse.className = `sse ${clase}`;
    indicadorSse.querySelector('.t').textContent = `Directo: ${texto}`;
  }
  function cerrarSse() { fuenteSse?.close(); fuenteSse = null; }

  /* ---------- Arranque ---------- */
  (async () => {
    try {
      listaEdificios = await edificios();
      if (!vivo) return;
      vaciar(botonesEdificio, listaEdificios.map((e) => h('button', {
        type: 'button', 'aria-pressed': 'false', 'data-id': e.id, title: e.name, onclick: () => irA(e.id),
      }, e.id)));
      await aplicarRuta(rutaInicial);
    } catch (e) { mostrarError(e, 'Explorador'); }
  })();

  const parar = cadaCierto(() => { if (espacioSel) refrescarEstado(); }, 10000);

  return {
    actualizar: (r) => aplicarRuta(r),
    desmontar() { vivo = false; parar(); cerrarSse(); grafica?.destruir(); },
  };
}

function contador(v, e) { return h('div', { class: 'contador' }, h('span', { class: 'v' }, v), h('span', { class: 'e' }, e)); }

function chipConfort(c) {
  const [txt, clase] = CONFORT[c] || [c || '—', 'neutro'];
  return h('span', { class: `chip ${clase}` }, txt);
}

function metricas(lst) {
  const ms = lista(lst);
  if (!ms.length) return h('p', { class: 'sub' }, 'Sin métricas.');
  return h('div', { class: 'metricas' }, ms.map((m) => {
    const bool = esBooleana(m.property, m.unit);
    const u = etiquetaUnidad(m.unit);
    return h('div', { class: `metrica ${m.source || 'none'}` },
      h('div', { class: 'm-nombre', title: m.property }, etiquetaPropiedad(m.property)),
      h('div', { class: 'm-valor' }, m.value === null || m.value === undefined ? '—' : bool ? valor(m.value, m.unit, m.property) : numero(m.value), !bool && u ? h('small', {}, u) : null),
      h('div', { class: 'm-pie' }, `${FUENTES_METRICA[m.source] || 'Sin datos'}${m.sensor_count != null ? ` · ${m.sensor_count} sensor${m.sensor_count === 1 ? '' : 'es'}` : ''}`));
  }));
}

function alertas(lst) {
  const as = lista(lst);
  if (!as.length) return h('p', { class: 'sub mt' }, 'Sin alertas.');
  return h('ul', { class: 'alertas mt', 'aria-label': 'Alertas' }, as.map((a) => h('li', { class: `alerta ${a.severity === 'critical' ? 'critical' : a.severity === 'info' ? 'info' : ''}` },
    h('span', { class: 'ico' }, iconoGravedad(a.severity)),
    h('span', {}, h('strong', {}, ALERTAS[a.code] || a.code), a.message ? ` — ${a.message}` : '', ' ', h('code', {}, a.code),
      a.space_id ? [' · ', h('a', { href: `#/explorador/${encodeURIComponent(a.space_id)}`, class: 'mono' }, a.space_id)] : null,
      a.sensor_id ? h('span', { class: 'sub' }, ` · ${a.sensor_id}`) : null))));
}

