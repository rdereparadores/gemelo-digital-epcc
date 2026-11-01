// Resumen: enlace con SmartPoliTech, salud, fuentes efectivas por edificio y reloj de simulación.
import { edificios, escenarios, obtener, pedir, total } from '../api.js';
import { bus, horaModelo, horaSimulada, modeloDistinto, refrescarEnlace, ultimaSimulacion, ultimoEnlace } from '../estado.js';
import { MODOS } from '../etiquetas.js';
import {
  barraFuentes, cadaCierto, cargando, chipModo, enParalelo, entero, fecha, h, icono, iconoGravedad, mostrarError, numero, relativo, textoClima, vaciar,
} from '../ui.js';

const FUENTES = ['real', 'simulated', 'none'];
const COMPROBACIONES = { simulation: 'Motor de simulación', upstream: 'Enlace con SmartPoliTech' };

export function montar(contenedor) {
  const cajaEnlace = h('div', {}, cargando());
  const cajaSalud = h('div', {}, cargando());
  const cajaReloj = h('div', {}, cargando());
  const cajaFuentes = h('div', {}, cargando());
  const cajaPolitica = h('div', {}, cargando());
  const cajaEdificios = h('div', { class: 'tabla-envoltorio' }, cargando());
  const marcaHora = h('span', { class: 'sub' });

  vaciar(contenedor, h('div', { class: 'vista' },
    h('div', { class: 'vista-cabecera' },
      h('div', {}, h('h1', {}, 'Resumen'),
        h('p', {}, 'Estado del servicio puente entre SmartPoliTech y el gemelo digital del campus.')),
      h('div', { class: 'acciones' }, marcaHora,
        h('button', { type: 'button', class: 'boton', onclick: () => refrescar(true) }, icono('actualizar'), 'Actualizar'))),
    h('div', { class: 'rejilla c4' },
      tarjeta('Enlace con SmartPoliTech', cajaEnlace, h('a', { href: '#/ajustes', class: 'sub' }, 'Ajustes')),
      tarjeta('Salud del servicio', cajaSalud),
      tarjeta('Reloj de simulación', cajaReloj, h('a', { href: '#/simulacion', class: 'sub' }, 'Controlar')),
      tarjeta('Política de fuentes', cajaPolitica, h('a', { href: '#/fuentes', class: 'sub' }, 'Editar'))),
    tarjeta('Sensores por fuente efectiva', cajaFuentes),
    h('section', { class: 'tarjeta plana', 'aria-labelledby': 'tit-edif' },
      h('header', {}, h('h2', { id: 'tit-edif' }, 'Por edificio')),
      cajaEdificios),
  ));

  let vivo = true;
  let enCurso = false;

  async function refrescar(manual = false) {
    if (enCurso) return;
    enCurso = true;
    try {
      await Promise.all([
        refrescarEnlace().then(pintarEnlace).catch((e) => { vaciar(cajaEnlace, errorCaja(e)); if (manual) mostrarError(e); }),
        pedir('/api/v1/health', { aceptar: [503] }).then(pintarSalud).catch((e) => { vaciar(cajaSalud, errorCaja(e)); if (manual) mostrarError(e); }),
        obtener('/api/v1/source-policy').then(pintarPolitica).catch((e) => vaciar(cajaPolitica, errorCaja(e))),
        contarFuentes().catch((e) => { vaciar(cajaFuentes, errorCaja(e)); vaciar(cajaEdificios, errorCaja(e)); mostrarError(e, 'Recuento de sensores'); }),
      ]);
      marcaHora.textContent = `Actualizado a las ${new Date().toLocaleTimeString('es-ES')}`;
    } finally { enCurso = false; }
  }

  function pintarEnlace(u = ultimoEnlace()) {
    if (!vivo || !u) return;
    const estado = u.activado === false ? ['Desactivado', 'estado-aviso']
      : u.alcanzable === true ? ['Alcanzable', 'estado-ok']
        : u.alcanzable === false ? ['Inalcanzable', 'estado-mal'] : ['Desconocido', 'neutro'];
    vaciar(cajaEnlace,
      h('div', { class: 'lado' }, h('span', { class: `chip ${estado[1]}` }, estado[0]),
        u.latencia != null ? h('span', { class: 'cifra' }, numero(u.latencia, 0), h('small', {}, 'ms')) : null),
      h('dl', { class: 'dl' },
        h('dt', {}, 'Último sondeo'), h('dd', { title: fecha(u.ultimoSondeo) }, relativo(u.ultimoSondeo)),
        u.duracion != null ? [h('dt', {}, 'Duración del sondeo'), h('dd', {}, `${numero(u.duracion / 1000, 1)} s`)] : null,
        u.ultimoExito ? [h('dt', {}, 'Último éxito'), h('dd', { title: fecha(u.ultimoExito) }, relativo(u.ultimoExito))] : null,
        u.proximoSondeo ? [h('dt', {}, 'Próximo'), h('dd', { title: fecha(u.proximoSondeo) }, relativo(u.proximoSondeo))] : null,
        u.totalReales != null ? [h('dt', {}, 'Sensores reales'), h('dd', {}, entero(u.totalReales))] : null,
        u.sondeados != null ? [h('dt', {}, 'Sondeados'), h('dd', {}, entero(u.sondeados))] : null,
        u.conDatos != null ? [h('dt', {}, 'Con algún dato'), h('dd', {}, entero(u.conDatos))] : null,
        h('dt', { title: u.umbral ? `Dato de menos de ${numero(u.umbral / 3600, 1)} h` : undefined }, `Con dato fresco${u.umbral ? ` (< ${numero(u.umbral / 3600, 1)} h)` : ''}`),
        h('dd', {}, u.frescos != null ? h('strong', { class: u.frescos ? 'etiqueta-real' : '' }, entero(u.frescos)) : '—'),
        u.url ? [h('dt', {}, 'URL'), h('dd', { class: 'mono recorta', title: u.url }, u.url.replace(/^https?:\/\//, ''))] : null),
      u.cuarentena.length ? h('details', { class: 'mt' },
        h('summary', {}, `Mediciones en cuarentena (${u.cuarentena.length})`),
        h('ul', { class: 'alertas mt' }, u.cuarentena.slice(0, 20).map((q) => h('li', { class: 'alerta' },
          h('span', { class: 'ico' }, iconoGravedad('warning')),
          h('span', { class: 'mono' }, typeof q === 'string' ? q : (q.measurement || q.name || q.id || JSON.stringify(q)),
            typeof q === 'object' && (q.reason || q.message) ? h('span', { class: 'sub' }, ` · ${q.reason || q.message}`) : null))))) : null,
      u.errores.length ? h('details', { class: 'mt' },
        h('summary', {}, `Errores recientes (${u.errores.length})`),
        h('ul', { class: 'alertas mt' }, u.errores.slice(0, 6).map((e) => h('li', { class: 'alerta critical' },
          h('span', { class: 'ico' }, iconoGravedad('critical')),
          h('span', {}, typeof e === 'string' ? e : (e.message || e.detail || JSON.stringify(e)),
            e.time ? h('span', { class: 'sub' }, ` · ${relativo(e.time)}`) : null))))) : null,
      h('details', { class: 'mt' }, h('summary', {}, 'Respuesta completa'), h('pre', { class: 'json' }, JSON.stringify(u.crudo, null, 2))));
  }

  function pintarSalud(s) {
    if (!vivo) return;
    const ok = s?.status === 'ok';
    const checks = s?.checks && typeof s.checks === 'object' ? Object.entries(s.checks) : [];
    vaciar(cajaSalud,
      h('div', { class: 'lado' }, h('span', { class: `chip ${ok ? 'estado-ok' : 'estado-aviso'}` }, ok ? 'Correcto' : s?.status === 'degraded' ? 'Degradado' : (s?.status || 'Desconocido'))),
      checks.length ? h('dl', { class: 'dl' }, checks.map(([k, v]) => {
        const est = typeof v === 'object' && v !== null ? (v.status ?? v.ok ?? JSON.stringify(v)) : v;
        const bien = est === true || est === 'ok' || est === 'up';
        const detalle = typeof v === 'object' && v !== null ? (v.detail || v.message || '') : '';
        return [h('dt', {}, COMPROBACIONES[k] || k), h('dd', { title: detalle || undefined }, h('span', { class: `chip ${bien ? 'estado-ok' : 'estado-aviso'}` }, bien ? 'Correcto' : String(est)))];
      })) : h('p', { class: 'sub mt' }, 'Sin comprobaciones detalladas.'));
  }

  function pintarReloj() {
    if (!vivo) return;
    const s = ultimaSimulacion();
    if (!s) return;
    const distinto = modeloDistinto();
    const t = distinto ? horaModelo() : horaSimulada();
    const escenario = nombreEscenario(s.scenario);
    vaciar(cajaReloj,
      h('div', { class: 'reloj-grande', 'aria-live': 'off' }, t ? t.toLocaleTimeString('es-ES') : '—'),
      h('div', { class: 'sub' }, t ? t.toLocaleDateString('es-ES', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' }) : '',
        distinto ? ' · hora del modelo' : ''),
      h('dl', { class: 'dl' },
        distinto ? [h('dt', {}, 'Reloj simulado'), h('dd', {}, fecha(horaSimulada()))] : null,
        h('dt', {}, 'Estado'), h('dd', {}, h('span', { class: `chip ${s.running ? 'estado-ok' : 'estado-aviso'}` }, s.running ? 'En marcha' : 'En pausa')),
        h('dt', {}, 'Escala'), h('dd', {}, `×${numero(Number(s.time_scale), 0)}`),
        h('dt', {}, 'Reloj'), h('dd', {}, s.clock === 'live' ? 'Hora real' : `Desde ${fecha(s.clock)}`),
        h('dt', {}, 'Escenario'), h('dd', {}, escenario),
        h('dt', {}, 'Clima'), h('dd', { class: 'envuelve' }, textoClima(s))));
  }

  let nombresEscenarios = {};
  escenarios().then((l) => { nombresEscenarios = Object.fromEntries(l.map((e) => [e.id, e.name])); pintarReloj(); }).catch(() => {});
  const nombreEscenario = (id) => nombresEscenarios[id] || id || '—';

  function pintarPolitica(p) {
    if (!vivo) return;
    const n = (o) => Object.keys(o || {}).length;
    vaciar(cajaPolitica,
      h('div', { class: 'lado' }, h('span', { class: 'sub' }, 'Por defecto'), chipModo(p.default)),
      h('dl', { class: 'dl' },
        h('dt', {}, 'Reglas por edificio'), h('dd', {}, n(p.buildings)),
        h('dt', {}, 'Reglas por estancia'), h('dd', {}, n(p.spaces)),
        h('dt', {}, 'Reglas por tipo'), h('dd', {}, n(p.kinds)),
        h('dt', {}, 'Excepciones de sensor'), h('dd', {}, n(p.sensors))),
      h('p', { class: 'sub mt' }, `Modos: ${Object.values(MODOS).join(' · ')}. Gana la regla más específica.`));
  }

  async function contarFuentes() {
    const lista = await edificios();
    const cuenta = (consulta) => obtener('/api/v1/sensors', { ...consulta, limit: 1 }).then(total);
    const tareas = [{ edificio: null }, ...lista.map((e) => ({ edificio: e.id }))]
      .flatMap((t) => FUENTES.map((f) => ({ ...t, fuente: f })));
    const valores = await enParalelo(tareas, 6, (t) => cuenta({ building: t.edificio, source: t.fuente }));
    const por = {};
    tareas.forEach((t, i) => { (por[t.edificio ?? '*'] ||= {})[t.fuente] = valores[i]; });
    if (!vivo) return;
    const g = por['*'];
    const tot = g.real + g.simulated + g.none;
    vaciar(cajaFuentes,
      h('div', { class: 'cifras' },
        bloqueCifra('Total', tot),
        bloqueCifra('Real', g.real, 'etiqueta-real', tot),
        bloqueCifra('Simulado', g.simulated, 'etiqueta-sim', tot),
        bloqueCifra('Sin datos', g.none, 'sub', tot)),
      barraFuentes(g, true),
      h('div', { class: 'leyenda' },
        h('span', {}, h('i', { class: 'muestra real' }), 'Real: dato de SmartPoliTech'),
        h('span', {}, h('i', { class: 'muestra simulated' }), 'Simulado: motor propio'),
        h('span', {}, h('i', { class: 'muestra none' }), 'Sin datos: modo real sin dato reciente')));

    const filas = lista.map((e) => {
      const c = por[e.id];
      const t = c.real + c.simulated + c.none;
      const enlace = (f, v) => h('a', { href: `#/fuentes?building=${encodeURIComponent(e.id)}&source=${f}` }, entero(v));
      return h('tr', {},
        h('td', {}, h('a', { href: `#/explorador/${encodeURIComponent(e.id)}` }, h('strong', {}, e.id)), ' ', h('span', { class: 'sub' }, e.name)),
        h('td', { class: 'mono' }, e.unity_id ?? '—'),
        h('td', { class: 'num' }, entero(e.space_count)),
        h('td', { class: 'num' }, entero(t)),
        h('td', { class: 'num etiqueta-real' }, enlace('real', c.real)),
        h('td', { class: 'num etiqueta-sim' }, enlace('simulated', c.simulated)),
        h('td', { class: 'num' }, enlace('none', c.none)),
        h('td', { class: 'reparto' }, barraFuentes(c)));
    });
    vaciar(cajaEdificios, h('table', {},
      h('thead', {}, h('tr', {}, h('th', {}, 'Edificio'), h('th', {}, 'Unity'), h('th', { class: 'num' }, 'Estancias'),
        h('th', { class: 'num' }, 'Sensores'), h('th', { class: 'num' }, 'Real'), h('th', { class: 'num' }, 'Simulado'),
        h('th', { class: 'num' }, 'Sin datos'), h('th', {}, 'Reparto'))),
      h('tbody', {}, filas)));
  }

  const alSimular = () => pintarReloj();
  bus.addEventListener('simulacion', alSimular);
  const pararReloj = setInterval(pintarReloj, 1000);
  pintarReloj();
  if (ultimoEnlace()) pintarEnlace(ultimoEnlace());
  refrescar();
  const parar = cadaCierto(refrescar, 30000);

  return {
    desmontar() { vivo = false; parar(); clearInterval(pararReloj); bus.removeEventListener('simulacion', alSimular); },
  };
}

function tarjeta(titulo, cuerpo, extra) {
  return h('section', { class: 'tarjeta' }, h('header', {}, h('h2', {}, titulo), extra || null), cuerpo);
}

function bloqueCifra(etiqueta, v, clase = '', tot) {
  return h('div', {}, h('div', { class: `sub ${clase}` }, etiqueta),
    h('div', { class: 'cifra' }, entero(v), tot && etiqueta !== 'Total' ? h('small', {}, `${numero((100 * v) / tot, 0)} %`) : null));
}

function errorCaja(e) {
  const p = e?.problema;
  return h('div', { class: 'alerta critical' }, h('span', { class: 'ico' }, iconoGravedad('critical')),
    h('span', {}, p ? `${p.title}${p.detail ? ` — ${p.detail}` : ''}` : (e?.message || 'Error')));
}
