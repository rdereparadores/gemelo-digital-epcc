// Arranque del panel: tema, cabecera con el estado global y enrutado por hash.
import { bus, horaModelo, horaSimulada, modeloDistinto, refrescarEnlace, refrescarSimulacion, ultimaSimulacion } from './estado.js';
import { cadaCierto, fecha, mostrarError, relativo, numero } from './ui.js';

const VISTAS = {
  resumen: () => import('./vistas/resumen.js'),
  fuentes: () => import('./vistas/fuentes.js'),
  explorador: () => import('./vistas/explorador.js'),
  simulacion: () => import('./vistas/simulacion.js'),
  ajustes: () => import('./vistas/ajustes.js'),
};
const TITULOS = { resumen: 'Resumen', fuentes: 'Fuentes de datos', explorador: 'Explorador', simulacion: 'Simulación', ajustes: 'Ajustes' };

/* ---------- Tema ---------- */
const ORDEN_TEMA = ['sistema', 'dark', 'light'];
const TEXTO_TEMA = { sistema: 'Sistema', dark: 'Oscuro', light: 'Claro' };
function leerTema() { try { return localStorage.getItem('panel.tema') || 'sistema'; } catch { return 'sistema'; } }
function aplicarTema(t) {
  if (t === 'sistema') delete document.documentElement.dataset.theme;
  else document.documentElement.dataset.theme = t;
  try { localStorage.setItem('panel.tema', t); } catch { /* sin almacenamiento */ }
  const boton = document.getElementById('boton-tema');
  boton.querySelector('.tema-texto').textContent = TEXTO_TEMA[t];
  boton.setAttribute('aria-label', `Tema: ${TEXTO_TEMA[t]}. Pulsar para cambiar`);
}
document.getElementById('boton-tema').addEventListener('click', () => {
  const actual = leerTema();
  aplicarTema(ORDEN_TEMA[(ORDEN_TEMA.indexOf(actual) + 1) % ORDEN_TEMA.length]);
});
aplicarTema(leerTema());

/* ---------- Cabecera ---------- */
const pastillaEnlace = document.getElementById('estado-enlace');
const pastillaReloj = document.getElementById('estado-reloj');

bus.addEventListener('enlace', ({ detail: u }) => {
  pastillaEnlace.classList.remove('ok', 'mal', 'aviso');
  let texto;
  if (u.activado === false) { pastillaEnlace.classList.add('aviso'); texto = 'SmartPoliTech: desactivado'; }
  else if (u.alcanzable === true) {
    pastillaEnlace.classList.add('ok');
    texto = `SmartPoliTech: alcanzable${u.latencia != null ? ` · ${numero(u.latencia, 0)} ms` : ''}${u.frescos != null ? ` · ${u.frescos} frescos${u.conDatos != null ? ` de ${u.conDatos} con dato` : ''}` : ''}`;
  } else if (u.alcanzable === false) { pastillaEnlace.classList.add('mal'); texto = 'SmartPoliTech: sin conexión'; }
  else texto = 'SmartPoliTech: desconocido';
  pastillaEnlace.querySelector('.txt').textContent = texto;
  pastillaEnlace.title = `Último sondeo: ${u.ultimoSondeo ? `${fecha(u.ultimoSondeo)} (${relativo(u.ultimoSondeo)})` : '—'}`;
});

function pintarReloj() {
  const s = ultimaSimulacion();
  const t = horaSimulada();
  if (!s || !t) return;
  const fmt = new Intl.DateTimeFormat('es-ES', { weekday: 'short', day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit', second: '2-digit' });
  const estado = s.running ? (Number(s.time_scale) !== 1 ? `×${numero(Number(s.time_scale), 0)}` : 'en marcha') : 'en pausa';
  const modelo = modeloDistinto() ? ` · modelo: ${fmt.format(horaModelo())}` : '';
  pastillaReloj.querySelector('.txt').textContent = `Simulación: ${fmt.format(t)} · ${estado}${s.clock && s.clock !== 'live' ? ' · reloj fijado' : ''}${modelo}`;
  pastillaReloj.classList.toggle('aviso', !s.running);
}
bus.addEventListener('simulacion', pintarReloj);
setInterval(pintarReloj, 1000);

async function sondearCabecera() {
  await Promise.allSettled([
    refrescarEnlace().catch((e) => { pastillaEnlace.querySelector('.txt').textContent = 'SmartPoliTech: ?'; throw e; }),
    refrescarSimulacion(),
  ]).then((r) => {
    const fallo = r.find((x) => x.status === 'rejected');
    if (fallo && !sondearCabecera.avisado) { sondearCabecera.avisado = true; mostrarError(fallo.reason, 'Estado del servicio'); }
    if (!fallo) sondearCabecera.avisado = false;
  });
}
sondearCabecera();
cadaCierto(sondearCabecera, 15000);

/* ---------- Enrutado ---------- */
const contenedor = document.getElementById('contenido');
let actual = null; // {seccion, vista}
let generacion = 0;

export function leerRuta() {
  const hash = location.hash.replace(/^#\/?/, '');
  const [camino, consulta = ''] = hash.split('?');
  const partes = camino.split('/').filter(Boolean).map(decodeURIComponent);
  const seccion = VISTAS[partes[0]] ? partes[0] : 'resumen';
  return { seccion, partes: partes.slice(1), consulta: new URLSearchParams(consulta) };
}

async function enrutar() {
  const ruta = leerRuta();
  for (const a of document.querySelectorAll('.navegacion a[data-seccion]')) {
    if (a.dataset.seccion === ruta.seccion) a.setAttribute('aria-current', 'page'); else a.removeAttribute('aria-current');
  }
  document.title = `${TITULOS[ruta.seccion]} · Gemelo EPCC`;
  if (actual?.seccion === ruta.seccion && actual.vista?.actualizar) { actual.vista.actualizar(ruta); return; }
  const g = ++generacion;
  actual?.vista?.desmontar?.();
  actual = null;
  contenedor.replaceChildren();
  try {
    const modulo = await VISTAS[ruta.seccion]();
    if (g !== generacion) return;
    const vista = modulo.montar(contenedor, ruta);
    actual = { seccion: ruta.seccion, vista };
  } catch (e) {
    mostrarError(e, 'No se pudo abrir la sección');
  }
}

window.addEventListener('hashchange', () => {
  const antes = actual?.seccion;
  enrutar().then(() => { if (antes !== actual?.seccion) contenedor.focus({ preventScroll: true }); });
});
if (!location.hash) history.replaceState(null, '', '#/resumen');
enrutar();
