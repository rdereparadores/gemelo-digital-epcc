// Simulación: reloj, escala, escenario y clima; inyección y fin de eventos.
import { borrar, edificios, escenarios, espaciosDe, lista, obtener, parchear, publicar, ruta } from '../api.js';
import { bus, fijarSimulacion, horaModelo, horaSimulada, modeloDistinto, refrescarSimulacion } from '../estado.js';
import { EVENTOS, etiquetaTipo } from '../etiquetas.js';
import {
  aviso, cadaCierto, cargando, debounce, fecha, h, icono, idUnico, mostrarError, numero, relativo, segmentado, textoClima, vaciar,
} from '../ui.js';

const PRESETS = [1, 10, 60, 600, 3600];
const MAX_ESCALA = 3600;

export function montar(contenedor) {
  let vivo = true;
  let estado = null; // SimulationState del servidor
  let borrador = null; // cambios del formulario sin aplicar
  let listaEscenarios = [];
  let listaEdificios = [];

  const cajaReloj = h('div', {}, cargando());
  const cajaForm = h('div', {}, cargando());
  const cajaEventoForm = h('div', {}, cargando());
  const cajaEventos = h('div', { class: 'tabla-envoltorio' }, cargando());

  vaciar(contenedor, h('div', { class: 'vista' },
    h('div', { class: 'vista-cabecera' }, h('div', {}, h('h1', {}, 'Simulación'),
      h('p', {}, 'Controla el reloj del motor de simulación, el escenario y el clima, e inyecta eventos (ventanas, clases, averías…) para ver la respuesta del gemelo.'))),
    h('div', { class: 'rejilla simulacion' },
      h('section', { class: 'tarjeta' }, h('header', {}, h('h2', {}, 'Reloj')), cajaReloj),
      h('section', { class: 'tarjeta' }, h('header', {}, h('h2', {}, 'Parámetros')), cajaForm)),
    h('section', { class: 'tarjeta' }, h('header', {}, h('h2', {}, 'Inyectar evento')), cajaEventoForm),
    h('section', { class: 'tarjeta plana' }, h('header', {}, h('h2', {}, 'Eventos')), cajaEventos)));

  /* ---------- Reloj ---------- */
  function pintarReloj() {
    if (!vivo || !estado) return;
    const distinto = modeloDistinto();
    const t = distinto ? horaModelo() : horaSimulada();
    vaciar(cajaReloj,
      h('div', { class: 'reloj-grande' }, t ? t.toLocaleTimeString('es-ES') : '—'),
      h('div', { class: 'sub' }, t ? t.toLocaleDateString('es-ES', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' }) : '',
        distinto ? ' · hora del modelo' : ''),
      h('dl', { class: 'dl' },
        distinto ? [h('dt', {}, 'Reloj simulado'), h('dd', {}, fecha(horaSimulada()))] : null,
        h('dt', {}, 'Estado'), h('dd', {}, h('span', { class: `chip ${estado.running ? 'estado-ok' : 'estado-aviso'}` }, estado.running ? 'En marcha' : 'En pausa')),
        h('dt', {}, 'Escala'), h('dd', {}, `×${numero(Number(estado.time_scale), 0)}`),
        h('dt', {}, 'Reloj'), h('dd', {}, estado.clock === 'live' ? 'Hora real' : fecha(estado.clock)),
        h('dt', {}, 'Escenario'), h('dd', {}, listaEscenarios.find((e) => e.id === estado.scenario)?.name || estado.scenario || '—'),
        h('dt', {}, 'Clima'), h('dd', { class: 'envuelve' }, textoClima(estado))),
      h('div', { class: 'acciones mt' },
        h('button', { type: 'button', class: `boton ${estado.running ? '' : 'primario'}`, onclick: () => enviar({ running: !estado.running }) },
          estado.running ? [icono('pausa'), 'Pausar'] : [icono('play'), 'Reanudar'])));
  }

  /* ---------- Formulario de parámetros ---------- */
  function pintarForm() {
    if (!vivo || !estado) return;
    borrador = { time_scale: Number(estado.time_scale), clock: estado.clock, scenario: estado.scenario, weather_source: estado.weather_source };
    const idEscala = idUnico('s');
    const idNum = idUnico('s');
    const idFecha = idUnico('s');
    const idEsc = idUnico('s');
    const deslizador = h('input', { type: 'range', id: idEscala, min: 0, max: 1000, step: 1, value: aDeslizador(borrador.time_scale), style: { flex: 1 },
      'aria-valuetext': `×${borrador.time_scale}` });
    const numeroEscala = h('input', { type: 'number', id: idNum, min: 1, max: MAX_ESCALA, step: 'any', value: borrador.time_scale, style: { width: '96px' } });
    const fijarEscala = (v, desde) => {
      v = Math.min(MAX_ESCALA, Math.max(1, Number(v) || 1));
      borrador.time_scale = v;
      if (desde !== 'deslizador') deslizador.value = aDeslizador(v);
      if (desde !== 'numero') numeroEscala.value = v;
      deslizador.setAttribute('aria-valuetext', `×${numero(v, 0)}`);
    };
    deslizador.addEventListener('input', () => fijarEscala(Math.round(desdeDeslizador(Number(deslizador.value))), 'deslizador'));
    numeroEscala.addEventListener('change', () => fijarEscala(numeroEscala.value, 'numero'));

    const entradaFecha = h('input', { type: 'datetime-local', id: idFecha, step: 60,
      value: estado.clock && estado.clock !== 'live' ? aLocal(estado.clock) : aLocal(new Date().toISOString()),
      disabled: borrador.clock === 'live' });
    entradaFecha.addEventListener('change', () => { if (entradaFecha.value) borrador.clock = isoUtc(entradaFecha.value); });
    const modoReloj = segmentado({ etiqueta: 'Reloj', valor: borrador.clock === 'live' ? 'live' : 'fijo',
      opciones: [{ valor: 'live', etiqueta: 'Hora real', clase: 'v-x' }, { valor: 'fijo', etiqueta: 'Desde una fecha', clase: 'v-x' }],
      alCambiar: (v) => {
        entradaFecha.disabled = v === 'live';
        borrador.clock = v === 'live' ? 'live' : (entradaFecha.value ? isoUtc(entradaFecha.value) : 'live');
        if (v !== 'live') entradaFecha.focus();
      } });

    const descripcion = h('p', { class: 'desc-escenario', 'aria-live': 'polite' });
    const selEscenario = h('select', { id: idEsc }, listaEscenarios.map((e) => h('option', { value: e.id }, `${e.name || e.id}${e.id === estado.scenario ? ' (activo)' : ''}`)));
    if (!listaEscenarios.some((e) => e.id === estado.scenario)) selEscenario.append(h('option', { value: estado.scenario }, estado.scenario));
    selEscenario.value = estado.scenario;
    const describir = () => { descripcion.textContent = listaEscenarios.find((e) => e.id === selEscenario.value)?.description || ''; };
    selEscenario.addEventListener('change', () => { borrador.scenario = selEscenario.value; describir(); });
    describir();

    vaciar(cajaForm, h('form', { class: 'formulario dos', onsubmit: (e) => { e.preventDefault(); aplicar(); } },
      h('div', { class: 'campo', style: { gridColumn: '1 / -1' } },
        h('label', { for: idEscala }, 'Escala de tiempo (1 = tiempo real, hasta 3600 = una hora por segundo)'),
        h('div', { class: 'lado' }, deslizador, h('label', { class: 'oculto', for: idNum }, 'Escala exacta'), numeroEscala,
          h('div', { class: 'acciones', role: 'group', 'aria-label': 'Escalas habituales' },
            PRESETS.map((p) => h('button', { type: 'button', class: 'boton mini', onclick: () => fijarEscala(p) }, `×${p}`))))),
      h('div', { class: 'campo' }, h('span', { class: 'etiqueta' }, 'Reloj'),
        h('div', { class: 'lado' }, modoReloj, h('label', { class: 'oculto', for: idFecha }, 'Fecha y hora de arranque (hora local)'), entradaFecha),
        h('span', { class: 'ayuda' }, 'La fecha se introduce en hora local y se envía en UTC.')),
      h('div', { class: 'campo' }, h('span', { class: 'etiqueta' }, 'Clima exterior'),
        segmentado({ etiqueta: 'Clima exterior', valor: borrador.weather_source,
          opciones: [{ valor: 'real', etiqueta: 'Real si hay dato', clase: 'v-real' }, { valor: 'simulated', etiqueta: 'Sintético de Cáceres', clase: 'v-simulated' }],
          alCambiar: (v) => { borrador.weather_source = v; } })),
      h('div', { class: 'campo', style: { gridColumn: '1 / -1' } }, h('label', { for: idEsc }, 'Escenario'), selEscenario, descripcion),
      h('div', { class: 'acciones fin', style: { gridColumn: '1 / -1' } },
        h('button', { type: 'button', class: 'boton fantasma', onclick: pintarForm }, 'Descartar'),
        h('button', { type: 'submit', class: 'boton primario' }, 'Aplicar cambios'))));
  }

  async function aplicar() {
    const parche = {};
    if (borrador.time_scale !== Number(estado.time_scale)) parche.time_scale = borrador.time_scale;
    if (borrador.clock !== estado.clock) parche.clock = borrador.clock;
    if (borrador.scenario !== estado.scenario) parche.scenario = borrador.scenario;
    if (borrador.weather_source !== estado.weather_source) parche.weather_source = borrador.weather_source;
    if (!Object.keys(parche).length) { aviso('info', 'Sin cambios', 'El estado de la simulación ya es ese.'); return; }
    await enviar(parche);
  }

  async function enviar(parche) {
    try {
      const r = await parchear('/api/v1/simulation', parche);
      if (r && typeof r === 'object' && 'running' in r) fijarSimulacion(r); else await refrescarSimulacion();
      aviso('exito', 'Simulación actualizada', Object.entries(parche).map(([k, v]) => `${k} = ${v}`).join(', '));
    } catch (e) { mostrarError(e, 'No se pudo cambiar la simulación'); }
  }

  const alSimular = ({ detail }) => {
    const cambioForm = !estado || ['time_scale', 'clock', 'scenario', 'weather_source'].some((k) => detail[k] !== estado[k]);
    estado = detail;
    pintarReloj();
    if (cambioForm) pintarForm();
  };
  bus.addEventListener('simulacion', alSimular);

  /* ---------- Formulario de eventos ---------- */
  function pintarEventoForm() {
    const ids = { tipo: idUnico('ev'), ed: idUnico('ev'), es: idUnico('ev'), se: idUnico('ev'), dur: idUnico('ev'), per: idUnico('ev'), dl: idUnico('dl') };
    let ambito = 'space';
    const selTipo = h('select', { id: ids.tipo }, Object.entries(EVENTOS).map(([k, v]) => h('option', { value: k }, v)));
    const selEdificio = h('select', { id: ids.ed }, listaEdificios.map((e) => h('option', { value: e.id }, `${e.id} · ${e.name}`)));
    const selEstancia = h('select', { id: ids.es });
    const entradaSensor = h('input', { type: 'text', id: ids.se, list: ids.dl, placeholder: 'Escribe para buscar un sensor…', autocomplete: 'off', spellcheck: 'false' });
    const datalist = h('datalist', { id: ids.dl });
    const entradaDur = h('input', { type: 'number', id: ids.dur, min: 0, step: 1, value: 15, style: { width: '110px' } });
    const entradaPersonas = h('input', { type: 'number', id: ids.per, min: 1, max: 500, step: 1, value: 30, style: { width: '110px' } });
    const campoPersonas = h('div', { class: 'campo' }, h('label', { for: ids.per }, 'Personas'), entradaPersonas);
    const campoEdificio = h('div', { class: 'campo' }, h('label', { for: ids.ed }, 'Edificio'), selEdificio);
    const campoEstancia = h('div', { class: 'campo', style: { minWidth: '260px' } }, h('label', { for: ids.es }, 'Estancia'), selEstancia);
    const campoSensor = h('div', { class: 'campo', style: { minWidth: '360px' } }, h('label', { for: ids.se }, 'Sensor (id)'), entradaSensor, datalist);
    const resumen = h('p', { class: 'sub', 'aria-live': 'polite' });

    const ambitos = segmentado({ etiqueta: 'Ámbito del evento', valor: ambito, compacto: false,
      opciones: [{ valor: 'campus', etiqueta: 'Campus', clase: 'v-x' }, { valor: 'building', etiqueta: 'Edificio', clase: 'v-x' },
        { valor: 'space', etiqueta: 'Estancia', clase: 'v-x' }, { valor: 'sensor', etiqueta: 'Sensor', clase: 'v-x' }],
      alCambiar: (v) => { ambito = v; actualizarVisibles(); } });

    async function rellenarEstancias() {
      vaciar(selEstancia, h('option', { value: '' }, 'Cargando…'));
      try {
        const es = await espaciosDe(selEdificio.value);
        vaciar(selEstancia, es.map((s) => h('option', { value: s.id }, `${s.code || s.id} · ${s.name || s.type}`)));
      } catch (e) { mostrarError(e); }
      actualizarResumen();
    }
    const buscarSensores = debounce(async () => {
      const q = entradaSensor.value.trim();
      if (q.length < 2) return;
      try {
        const d = await obtener('/api/v1/sensors', { q, building: selEdificio.value || undefined, limit: 25 });
        vaciar(datalist, lista(d).map((s) => h('option', { value: s.id }, `${s.name} · ${etiquetaTipo(s.kind)} · ${s.space_id}`)));
      } catch { /* sin sugerencias */ }
    }, 250);
    entradaSensor.addEventListener('input', () => { buscarSensores(); actualizarResumen(); });
    selEdificio.addEventListener('change', () => { rellenarEstancias(); actualizarResumen(); });
    selEstancia.addEventListener('change', actualizarResumen);
    selTipo.addEventListener('change', () => {
      if (selTipo.value === 'sensor_failure' && ambito !== 'sensor') { ambito = 'sensor'; ambitos.fijar('sensor'); }
      actualizarVisibles();
    });
    entradaDur.addEventListener('input', actualizarResumen);

    function objetivo() {
      if (ambito === 'campus') return 'campus';
      if (ambito === 'building') return selEdificio.value;
      if (ambito === 'space') return selEstancia.value;
      return entradaSensor.value.trim();
    }
    function actualizarVisibles() {
      campoEdificio.hidden = ambito === 'campus';
      campoEstancia.hidden = ambito !== 'space';
      campoSensor.hidden = ambito !== 'sensor';
      campoPersonas.hidden = selTipo.value !== 'class_start';
      actualizarResumen();
    }
    function actualizarResumen() {
      const d = Number(entradaDur.value);
      resumen.textContent = `${EVENTOS[selTipo.value]} en ${objetivo() || '…'} ${d > 0 ? `durante ${d} min (hora simulada)` : selTipo.value === 'class_start' ? 'durante 50 min (lo que dura una clase)' : selTipo.value === 'class_end' ? 'hasta la hora de la siguiente clase' : 'sin límite de duración'}.`;
    }

    vaciar(cajaEventoForm, h('form', { class: 'formulario', onsubmit: async (e) => {
      e.preventDefault();
      const target = objetivo();
      if (!target) { aviso('info', 'Falta el objetivo', 'Elige un edificio, una estancia o un sensor.'); return; }
      const cuerpo = { type: selTipo.value, target, duration_s: Number(entradaDur.value) > 0 ? Math.round(Number(entradaDur.value) * 60) : null, params: {} };
      if (cuerpo.type === 'class_start') cuerpo.params.people = Math.max(1, Math.round(Number(entradaPersonas.value) || 1));
      const boton = e.submitter;
      if (boton) boton.disabled = true;
      try {
        const { datos, res } = await publicar('/api/v1/simulation/events', cuerpo);
        aviso('exito', 'Evento creado', `${EVENTOS[cuerpo.type]} → ${target}${datos?.id ? ` (id ${datos.id})` : ''}${res.headers.get('Location') ? ` · ${res.headers.get('Location')}` : ''}`);
        cargarEventos();
      } catch (err) { mostrarError(err, 'No se pudo crear el evento'); } finally { if (boton) boton.disabled = false; }
    } },
    h('div', { class: 'filtros' },
      h('div', { class: 'campo', style: { width: '220px' } }, h('label', { for: ids.tipo }, 'Tipo de evento'), selTipo),
      h('div', { class: 'campo', style: { width: 'auto' } }, h('span', { class: 'etiqueta' }, 'Ámbito'), ambitos),
      campoEdificio, campoEstancia, campoSensor,
      h('div', { class: 'campo', style: { width: 'auto' } }, h('label', { for: ids.dur }, 'Duración (min, 0 = la de por defecto)'), entradaDur),
      campoPersonas),
    h('div', { class: 'lado' }, resumen, h('button', { type: 'submit', class: 'boton primario derecha' }, 'Inyectar evento'))));
    if (listaEdificios.length) rellenarEstancias();
    actualizarVisibles();
  }

  /* ---------- Lista de eventos ---------- */
  async function cargarEventos() {
    try {
      const d = await obtener('/api/v1/simulation/events');
      if (!vivo) return;
      let evs;
      if (Array.isArray(d) || Array.isArray(d?.items)) evs = lista(d);
      else evs = [...(d?.active || []), ...(d?.recent || d?.history || [])];
      pintarEventos(evs);
    } catch (e) {
      vaciar(cajaEventos, h('div', { class: 'vacio' }, 'No se pudo cargar la lista de eventos.'));
      if (!cargarEventos.avisado) { cargarEventos.avisado = true; mostrarError(e, 'Eventos'); }
    }
  }

  let firmaEventos = '';
  function pintarEventos(evs) {
    // Solo se repinta si algo cambió, para no perder el foco al sondear.
    const firma = JSON.stringify(evs);
    if (firma === firmaEventos && cajaEventos.querySelector('table, .vacio')) return;
    firmaEventos = firma;
    if (!evs.length) { vaciar(cajaEventos, h('div', { class: 'vacio' }, 'No hay eventos activos ni recientes.')); return; }
    const activos = evs.filter((e) => e.active !== false);
    const terminados = evs.filter((e) => e.active === false);
    const ahora = horaSimulada()?.getTime() ?? Date.now();
    const filaEv = (ev) => {
      // (una duración fuera del rango de Date lanzaba RangeError y dejaba la tabla sin cargar)
      const tFin = ev.start && ev.duration_s ? new Date(ev.start).getTime() + ev.duration_s * 1000 : NaN;
      const fin = ev.end || ev.ends_at || (Number.isFinite(tFin) && Math.abs(tFin) <= 8.64e15 ? new Date(tFin).toISOString() : null);
      return h('tr', {},
        h('td', {}, h('span', { class: `chip ${ev.active === false ? 'neutro' : 'estado-ok'}` }, ev.active === false ? 'Terminado' : 'Activo')),
        h('td', {}, h('strong', {}, EVENTOS[ev.type] || ev.type), h('div', { class: 'sub mono' }, ev.id)),
        h('td', { class: 'mono' }, ev.target === 'campus' ? 'campus' : h('a', { href: `#/explorador/${encodeURIComponent(ev.target)}` }, ev.target)),
        h('td', { title: fecha(ev.start) }, ev.start ? fecha(ev.start) : '—'),
        h('td', {}, ev.duration_s ? `${numero(ev.duration_s / 60, 1)} min` : 'sin límite'),
        h('td', { title: fecha(fin) }, ev.active !== false && fin ? relativo(fin, ahora) : '—'),
        h('td', { class: 'mono' }, ev.params && Object.keys(ev.params).length ? JSON.stringify(ev.params) : '—'),
        h('td', { class: 'num' }, ev.active !== false
          ? h('button', { type: 'button', class: 'boton mini peligro', 'aria-label': `Terminar el evento ${EVENTOS[ev.type] || ev.type} en ${ev.target}`, onclick: (e) => terminar(ev, e.currentTarget) }, 'Terminar')
          : null));
    };
    vaciar(cajaEventos, h('table', {},
      h('thead', {}, h('tr', {}, h('th', {}, 'Estado'), h('th', {}, 'Evento'), h('th', {}, 'Objetivo'), h('th', {}, 'Inicio (simulado)'),
        h('th', {}, 'Duración'), h('th', {}, 'Termina'), h('th', {}, 'Parámetros'), h('th', { class: 'num' }, h('span', { class: 'oculto' }, 'Acciones')))),
      h('tbody', {}, [...activos, ...terminados].map(filaEv))));
  }

  async function terminar(ev, boton) {
    boton.disabled = true;
    try {
      await borrar(ruta.evento(ev.id));
      aviso('exito', 'Evento terminado', `${EVENTOS[ev.type] || ev.type} en ${ev.target}`);
      cargarEventos();
    } catch (e) { boton.disabled = false; mostrarError(e, 'No se pudo terminar el evento'); }
  }

  /* ---------- Arranque ---------- */
  (async () => {
    try {
      const [s, esc, eds] = await Promise.all([refrescarSimulacion(), escenarios().catch(() => []), edificios()]);
      if (!vivo) return;
      listaEscenarios = esc;
      listaEdificios = eds;
      estado = s;
      pintarReloj();
      pintarForm();
      pintarEventoForm();
    } catch (e) { mostrarError(e, 'Simulación'); vaciar(cajaForm, h('div', { class: 'vacio' }, 'No disponible.')); }
  })();
  cargarEventos();
  const pararEventos = cadaCierto(cargarEventos, 5000);
  const pararReloj = setInterval(pintarRelojLigero, 1000);
  function pintarRelojLigero() {
    const nodo = cajaReloj.querySelector('.reloj-grande');
    const t = modeloDistinto() ? horaModelo() : horaSimulada();
    if (nodo && t) nodo.textContent = t.toLocaleTimeString('es-ES');
  }

  return {
    desmontar() { vivo = false; pararEventos(); clearInterval(pararReloj); bus.removeEventListener('simulacion', alSimular); },
  };
}

// Escala logarítmica del deslizador: 0..1000 → 1..3600
const aDeslizador = (v) => Math.round((Math.log(v) / Math.log(MAX_ESCALA)) * 1000);
const desdeDeslizador = (p) => Math.exp((p / 1000) * Math.log(MAX_ESCALA));

/** ISO UTC → valor de <input type="datetime-local"> en hora local. */
function aLocal(iso) {
  const d = new Date(iso);
  const p = (n) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}T${p(d.getHours())}:${p(d.getMinutes())}`;
}


/** Valor de <input type="datetime-local"> (hora local) → ISO 8601 en UTC sin milisegundos. */
const isoUtc = (local) => new Date(local).toISOString().replace(/\.\d{3}Z$/, 'Z');
