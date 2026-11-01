// Gráfica de serie temporal en SVG, sin librerías. Un eje Y, tramos coloreados por fuente.
import { FUENTES, etiquetaUnidad } from './etiquetas.js';
import { h, numero } from './ui.js';

const NS = 'http://www.w3.org/2000/svg';
const s = (etq, at = {}, ...hijos) => {
  const n = document.createElementNS(NS, etq);
  for (const [k, v] of Object.entries(at)) if (v !== undefined && v !== null) n.setAttribute(k, v);
  for (const c of hijos) if (c) n.append(c);
  return n;
};

const M = { arriba: 22, derecha: 16, abajo: 26, izquierda: 52 };
const ALTO = 250;

/**
 * crearGrafica(contenedor) → {dibujar({puntos, desde, hasta, unidad, booleana, nombre}), anadir(punto)}
 * puntos: [{t: ms, v: número, fuente: 'real'|'simulated'}] en cualquier orden.
 */
export function crearGrafica(contenedor) {
  const raiz = h('div', { class: 'grafica' });
  const tooltip = h('div', { class: 'tooltip', hidden: true });
  contenedor.replaceChildren(raiz);
  let datos = null;
  let indiceCursor = -1;

  const ro = new ResizeObserver(() => { if (datos) pintar(); });
  ro.observe(contenedor);

  function dibujar(d) {
    datos = { ...d, puntos: [...d.puntos].sort((a, b) => a.t - b.t) };
    indiceCursor = -1;
    pintar();
  }

  function anadir(p) {
    if (!datos) return;
    datos.puntos.push(p);
    if (datos.vivo) { datos.hasta = Math.max(datos.hasta, p.t); datos.desde = datos.hasta - datos.ventana; }
    datos.puntos = datos.puntos.filter((x) => x.t >= datos.desde);
    pintar();
  }

  function pintar() {
    const ancho = Math.max(320, contenedor.clientWidth || 600);
    const { puntos, booleana, unidad } = datos;
    const desde = datos.desde ?? (puntos[0]?.t ?? Date.now() - 3600e3);
    const hasta = datos.hasta ?? (puntos.at(-1)?.t ?? Date.now());
    if (!puntos.length) {
      raiz.replaceChildren(h('div', { class: 'grafica-vacia' }, 'No hay observaciones en este intervalo.'));
      return;
    }
    const iw = ancho - M.izquierda - M.derecha;
    const ih = ALTO - M.arriba - M.abajo;
    let [ymin, ymax] = booleana ? [0, 1] : extremos(puntos.map((p) => p.v));
    const ticksY = booleana ? [0, 1] : ticksBonitos(ymin, ymax, 5);
    if (!booleana) { ymin = Math.min(ymin, ticksY[0]); ymax = Math.max(ymax, ticksY.at(-1)); }
    const x = (t) => M.izquierda + ((t - desde) / Math.max(1, hasta - desde)) * iw;
    const y = (v) => M.arriba + ih - ((v - ymin) / Math.max(1e-9, ymax - ymin)) * ih;
    const u = etiquetaUnidad(unidad);
    const fmtY = (v) => (booleana ? (v ? 'Sí' : 'No') : numero(v));

    const svg = s('svg', { viewBox: `0 0 ${ancho} ${ALTO}`, height: ALTO, role: 'img', 'aria-label': `${datos.nombre}: ${puntos.length} observaciones` });
    const rej = s('g', { class: 'rejilla-g' });
    const eje = s('g', { class: 'eje' });
    for (const v of ticksY) {
      rej.append(s('line', { x1: M.izquierda, x2: ancho - M.derecha, y1: y(v), y2: y(v) }));
      eje.append(s('text', { x: M.izquierda - 8, y: y(v) + 4, 'text-anchor': 'end' }, document.createTextNode(fmtY(v))));
    }
    if (u && !booleana) eje.append(s('text', { x: M.izquierda - 8, y: 10, 'text-anchor': 'end' }, document.createTextNode(u)));
    for (const t of ticksTiempo(desde, hasta, Math.max(2, Math.floor(iw / 110)))) {
      eje.append(s('line', { x1: x(t.t), x2: x(t.t), y1: M.arriba + ih, y2: M.arriba + ih + 4 }));
      eje.append(s('text', { x: x(t.t), y: ALTO - 6, 'text-anchor': 'middle' }, document.createTextNode(t.txt)));
    }
    eje.append(s('line', { class: 'base', x1: M.izquierda, x2: ancho - M.derecha, y1: M.arriba + ih, y2: M.arriba + ih }));
    svg.append(rej, eje);

    // Tramos por fuente; se corta la línea en huecos grandes.
    const huecoMax = umbralHueco(puntos);
    const series = s('g');
    let tramo = [];
    const cerrar = () => {
      if (tramo.length > 1) series.append(s('path', { class: `serie ${tramo[0].fuente}`, d: camino(tramo, x, y, booleana) }));
      else if (tramo.length === 1) series.append(s('circle', { class: `serie ${tramo[0].fuente}`, cx: x(tramo[0].t), cy: y(tramo[0].v), r: 1.5 }));
      tramo = [];
    };
    puntos.forEach((p, i) => {
      const prev = puntos[i - 1];
      if (prev && (p.t - prev.t > huecoMax)) cerrar();
      else if (prev && prev.fuente !== p.fuente) { tramo.push({ ...p, fuente: prev.fuente }); cerrar(); }
      tramo.push(p);
    });
    cerrar();
    svg.append(series);
    const ult = puntos.at(-1);
    svg.append(s('circle', { class: 'ultimo', cx: x(ult.t), cy: y(ult.v), r: 4, fill: `var(--${ult.fuente === 'real' ? 'real' : 'sim'})` }));

    // Capa de cursor
    const cursor = s('g', { class: 'cursor', visibility: 'hidden' },
      s('line', { y1: M.arriba, y2: M.arriba + ih }), s('circle', { r: 5 }));
    const zona = s('rect', { class: 'zona', x: M.izquierda, y: M.arriba, width: iw, height: ih, tabindex: 0,
      'aria-label': 'Recorrer la gráfica con las flechas izquierda y derecha' });
    svg.append(cursor, zona);

    const mostrar = (i) => {
      if (i < 0 || i >= puntos.length) { ocultar(); return; }
      indiceCursor = i;
      const p = puntos[i];
      const cx = x(p.t);
      const cy = y(p.v);
      cursor.setAttribute('visibility', 'visible');
      cursor.firstChild.setAttribute('x1', cx);
      cursor.firstChild.setAttribute('x2', cx);
      const c = cursor.lastChild;
      c.setAttribute('cx', cx); c.setAttribute('cy', cy);
      c.setAttribute('fill', `var(--${p.fuente === 'real' ? 'real' : 'sim'})`);
      tooltip.hidden = false;
      tooltip.replaceChildren(
        h('div', { class: 'tv' }, booleana ? (p.v >= 0.5 ? 'Sí' : 'No') : `${numero(p.v)}${u ? ` ${u}` : ''}`),
        h('div', { class: 'tt' }, new Date(p.t).toLocaleString('es-ES', { dateStyle: 'short', timeStyle: 'medium' })),
        h('div', {}, h('i', { class: `muestra ${p.fuente}` }), FUENTES[p.fuente] || p.fuente));
      const rx = (cx / ancho) * raiz.clientWidth;
      const ry = (cy / ALTO) * raiz.clientHeight;
      tooltip.style.left = `${Math.min(Math.max(rx, 70), raiz.clientWidth - 70)}px`;
      tooltip.style.top = `${Math.max(ry, 50)}px`;
    };
    const ocultar = () => { cursor.setAttribute('visibility', 'hidden'); tooltip.hidden = true; };
    const cercano = (px) => {
      const t = desde + ((px - M.izquierda) / iw) * (hasta - desde);
      let lo = 0; let hi = puntos.length - 1;
      while (hi - lo > 1) { const m = (lo + hi) >> 1; if (puntos[m].t < t) lo = m; else hi = m; }
      return Math.abs(puntos[lo].t - t) <= Math.abs(puntos[hi].t - t) ? lo : hi;
    };
    zona.addEventListener('pointermove', (e) => {
      const r = svg.getBoundingClientRect();
      mostrar(cercano(((e.clientX - r.left) / r.width) * ancho));
    });
    zona.addEventListener('pointerleave', ocultar);
    zona.addEventListener('blur', ocultar);
    zona.addEventListener('focus', () => mostrar(indiceCursor >= 0 ? indiceCursor : puntos.length - 1));
    zona.addEventListener('keydown', (e) => {
      const paso = e.shiftKey ? 10 : 1;
      if (e.key === 'ArrowLeft') { e.preventDefault(); mostrar(Math.max(0, (indiceCursor < 0 ? puntos.length : indiceCursor) - paso)); }
      else if (e.key === 'ArrowRight') { e.preventDefault(); mostrar(Math.min(puntos.length - 1, indiceCursor + paso)); }
      else if (e.key === 'Home') { e.preventDefault(); mostrar(0); }
      else if (e.key === 'End') { e.preventDefault(); mostrar(puntos.length - 1); }
      else if (e.key === 'Escape') ocultar();
    });

    const fuentes = [...new Set(puntos.map((p) => p.fuente))];
    const leyenda = fuentes.length > 1 || fuentes[0] !== 'simulated'
      ? h('div', { class: 'leyenda' }, fuentes.map((f) => h('span', {}, h('i', { class: `muestra ${f}` }), FUENTES[f] || f)))
      : h('div', { class: 'leyenda' }, h('span', {}, h('i', { class: 'muestra simulated' }), 'Simulado'));
    raiz.replaceChildren(svg, tooltip, leyenda);
  }

  return { dibujar, anadir, destruir: () => ro.disconnect() };
}

function camino(tramo, x, y, escalon) {
  let d = '';
  tramo.forEach((p, i) => {
    const px = x(p.t).toFixed(1);
    const py = y(p.v).toFixed(1);
    if (i === 0) d += `M${px},${py}`;
    else if (escalon) d += `H${px}V${py}`;
    else d += `L${px},${py}`;
  });
  return d;
}

function extremos(vs) {
  let lo = Math.min(...vs);
  let hi = Math.max(...vs);
  if (lo === hi) { const m = Math.abs(lo) * 0.05 || 1; lo -= m; hi += m; }
  return [lo, hi];
}

function ticksBonitos(lo, hi, n) {
  const paso0 = (hi - lo) / n;
  const mag = 10 ** Math.floor(Math.log10(paso0));
  const paso = [1, 2, 2.5, 5, 10].map((m) => m * mag).find((p) => p >= paso0) || 10 * mag;
  const ini = Math.floor(lo / paso) * paso;
  const fin = Math.ceil(hi / paso) * paso;
  const t = [];
  for (let v = ini; v <= fin + paso / 2; v += paso) t.push(Number(v.toFixed(10)));
  return t;
}

const PASOS = [60e3, 300e3, 600e3, 900e3, 1800e3, 3600e3, 2 * 3600e3, 3 * 3600e3, 6 * 3600e3, 12 * 3600e3, 86400e3, 2 * 86400e3, 7 * 86400e3];
function ticksTiempo(desde, hasta, n) {
  const rango = hasta - desde;
  const paso = PASOS.find((p) => rango / p <= n) || PASOS.at(-1);
  const diario = paso >= 86400e3;
  // Alinea a la hora local
  const d0 = new Date(desde);
  if (diario) d0.setHours(0, 0, 0, 0); else d0.setMinutes(0, 0, 0);
  // Los pasos de días se suman en el calendario local para no descuadrarse con el cambio de hora.
  const dias = diario ? Math.round(paso / 86400e3) : 0;
  const siguiente = (f) => { const g = new Date(f); if (dias) g.setDate(g.getDate() + dias); else g.setTime(g.getTime() + paso); return g; };
  let f = d0;
  while (f.getTime() < desde) f = siguiente(f);
  const salida = [];
  const multiDia = rango > 20 * 3600e3;
  for (; f.getTime() <= hasta; f = siguiente(f)) {
    const t = f.getTime();
    const txt = diario ? f.toLocaleDateString('es-ES', { day: 'numeric', month: 'short' })
      : multiDia && f.getHours() === 0 && f.getMinutes() === 0 ? f.toLocaleDateString('es-ES', { day: 'numeric', month: 'short' })
        : f.toLocaleTimeString('es-ES', { hour: '2-digit', minute: '2-digit' });
    salida.push({ t, txt });
  }
  return salida;
}

function umbralHueco(puntos) {
  if (puntos.length < 3) return Infinity;
  const difs = [];
  for (let i = 1; i < puntos.length; i++) difs.push(puntos[i].t - puntos[i - 1].t);
  difs.sort((a, b) => a - b);
  const mediana = difs[difs.length >> 1];
  return Math.max(mediana * 6, 120e3);
}
