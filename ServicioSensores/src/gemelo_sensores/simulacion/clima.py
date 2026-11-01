"""Clima sintético de Cáceres y posición del sol.

Las temperaturas parten de las normales mensuales de Cáceres (AEMET, retocadas para que los
inviernos vayan de unos 3 a 13 °C y los veranos de unos 20 a 38 °C) con una anomalía diaria que
persiste varios días. La nubosidad, la lluvia, el viento y la presión son coherentes entre sí: los
días cubiertos tienen menos oscilación térmica, más humedad y menos sol.
"""

from __future__ import annotations

import math
from dataclasses import dataclass, field
from datetime import date

from .azar import clave, ruido, suave, u01
from .calendario import ORDINAL_EPOCA, desfase_madrid

LATITUD = 39.47
LONGITUD = -6.34
PRESION_SITIO = 96300.0  # Pa, unos 440 m de altitud

#                ene   feb   mar   abr   may   jun   jul   ago   sep   oct   nov   dic
_TMAX = (12.6, 14.6, 18.3, 20.0, 24.2, 30.2, 34.4, 34.0, 29.0, 22.6, 16.4, 13.0)
_TMIN = (3.6, 4.8, 7.0, 8.7, 12.0, 16.8, 19.6, 19.5, 16.8, 12.6, 7.6, 4.8)
_NUB = (0.50, 0.45, 0.40, 0.45, 0.35, 0.15, 0.06, 0.08, 0.20, 0.40, 0.50, 0.52)
_P_LLUVIA = (0.28, 0.26, 0.22, 0.30, 0.24, 0.09, 0.03, 0.04, 0.12, 0.28, 0.30, 0.32)

# Fachadas: N, E, S, O (azimut desde el norte, en sentido horario)
AZIMUT_FACHADAS = (0.0, 90.0, 180.0, 270.0)
_INCLINACION_FV = math.radians(30.0)


def presion_saturacion(t: float) -> float:
    """Presión de vapor de saturación (Pa), fórmula de Magnus."""
    return 610.94 * math.exp(17.625 * t / (t + 243.04))


def razon_humedad(t: float, rh: float) -> float:
    """kg de vapor por kg de aire seco."""
    pv = max(0.0, min(100.0, rh)) / 100.0 * presion_saturacion(t)
    return 0.622 * pv / (PRESION_SITIO - pv)


def humedad_relativa(t: float, w: float) -> float:
    pv = w * PRESION_SITIO / (0.622 + w)
    return max(3.0, min(100.0, 100.0 * pv / presion_saturacion(t)))


def _normal(tabla: tuple[float, ...], dia_anio: float) -> float:
    """Interpola una normal mensual (valores a mitad de mes) para un día del año."""
    x = (dia_anio - 15.2) / 30.44
    i = math.floor(x)
    f = x - i
    return tabla[i % 12] * (1.0 - f) + tabla[(i + 1) % 12] * f


def posicion_sol(t: float) -> tuple[float, float, float]:
    """(altura, azimut, ecuación del tiempo en minutos), ángulos en radianes. NOAA simplificado."""
    dias = t / 86400.0
    ordinal = ORDINAL_EPOCA + math.floor(dias)
    hora = (dias - math.floor(dias)) * 24.0
    doy = ordinal - date(date.fromordinal(ordinal).year, 1, 1).toordinal() + 1
    g = 2.0 * math.pi / 365.0 * (doy - 1 + (hora - 12.0) / 24.0)
    eot = 229.18 * (0.000075 + 0.001868 * math.cos(g) - 0.032077 * math.sin(g)
                    - 0.014615 * math.cos(2 * g) - 0.040849 * math.sin(2 * g))
    decl = (0.006918 - 0.399912 * math.cos(g) + 0.070257 * math.sin(g) - 0.006758 * math.cos(2 * g)
            + 0.000907 * math.sin(2 * g) - 0.002697 * math.cos(3 * g) + 0.00148 * math.sin(3 * g))
    tst = hora * 60.0 + eot + 4.0 * LONGITUD
    ha = math.radians(tst / 4.0 - 180.0)
    lat = math.radians(LATITUD)
    cos_z = math.sin(lat) * math.sin(decl) + math.cos(lat) * math.cos(decl) * math.cos(ha)
    alt = math.asin(max(-1.0, min(1.0, cos_z)))
    az = math.atan2(math.sin(ha), math.cos(ha) * math.sin(lat) - math.tan(decl) * math.cos(lat)) + math.pi
    return alt, az, eot


@dataclass
class AjusteClima:
    """Retoques del escenario sobre el clima sintético."""

    anomalia: float = 0.0  # °C sumados a las máximas y mínimas
    nubosidad_max: float | None = None


@dataclass
class EstadoExterior:
    te: float = 15.0
    rh: float = 60.0
    w: float = 0.007
    viento: float = 2.0
    direccion: float = 240.0
    presion: float = 1016.0
    lluvia: float = 0.0  # mm/h
    nubosidad: float = 0.3
    ghi: float = 0.0  # W/m² horizontal
    alt_sol: float = -0.5  # rad
    fachadas: list[float] = field(default_factory=lambda: [0.0] * 4)  # W/m² totales por fachada
    fachadas_dir: list[float] = field(default_factory=lambda: [0.0] * 4)  # solo directa
    poa_fv: float = 0.0  # W/m² sobre los paneles (30° al sur)
    co2: float = 400.0
    pm25: float = 8.0
    lux: float = 0.0


class ClimaCaceres:
    def __init__(self, semilla: int) -> None:
        self.c_anom = clave("clima.anomalia", semilla)
        self.c_anom2 = clave("clima.anomalia2", semilla)
        self.c_nub = clave("clima.nubes", semilla)
        self.c_nub_h = clave("clima.nubes_h", semilla)
        self.c_lluvia = clave("clima.lluvia", semilla)
        self.c_lluvia_h = clave("clima.lluvia_h", semilla)
        self.c_viento = clave("clima.viento", semilla)
        self.c_viento2 = clave("clima.viento2", semilla)
        self.c_dir = clave("clima.direccion", semilla)
        self.c_dir2 = clave("clima.direccion2", semilla)
        self.c_pres = clave("clima.presion", semilla)
        self.c_co2 = clave("clima.co2", semilla)
        self.c_pm = clave("clima.pm", semilla)
        self._dias: dict[tuple, tuple] = {}

    # ------------------------------------------------------------------ días
    def _dia(self, ordinal: int, aj: AjusteClima) -> tuple[float, float, float, bool, float, float, float, float]:
        """(tmin, tmax, nubosidad, lluvia, punto de rocío, pm25, hora solar del mínimo, del máximo)."""
        k = (ordinal, aj.anomalia, aj.nubosidad_max)
        d = self._dias.get(k)
        if d is not None:
            return d
        if len(self._dias) > 4000:
            self._dias.clear()
        fecha = date.fromordinal(ordinal)
        doy = ordinal - date(fecha.year, 1, 1).toordinal() + 1
        tmax_n = _normal(_TMAX, doy)
        tmin_n = _normal(_TMIN, doy)
        nub_n = _normal(_NUB, doy)
        p_ll = _normal(_P_LLUVIA, doy)
        anom = 2.6 * ruido(self.c_anom, ordinal, 4.0) + 1.6 * (u01(self.c_anom2, ordinal) - 0.5)
        u = u01(self.c_nub, ordinal)
        lluvia = u01(self.c_lluvia, ordinal) < p_ll
        nub = 0.8 + 0.2 * u if lluvia else min(1.0, nub_n * (0.15 + 1.7 * u))
        if aj.nubosidad_max is not None:
            nub = min(nub, aj.nubosidad_max)
            if aj.nubosidad_max < 0.5:
                lluvia = False
        tmax = tmax_n + anom - 5.0 * (nub - nub_n) + aj.anomalia
        tmin = tmin_n + 0.7 * anom + 2.0 * (nub - nub_n) + 0.8 * aj.anomalia
        tmin = min(tmin, tmax - 2.0)
        dep = 0.3 if lluvia else 0.6 + 0.28 * max(0.0, tmax_n - 12.0)
        upm = u01(self.c_pm, ordinal)
        pm = 4.0 + 10.0 * upm
        if u01(self.c_pm, ordinal, 1) < 0.05 and not lluvia:
            pm = 25.0 + 30.0 * upm  # episodio de polvo sahariano
        if lluvia:
            pm *= 0.5
        decl = math.radians(23.44) * math.sin(2.0 * math.pi * (284 + doy) / 365.0)
        medio_dia = math.degrees(math.acos(max(-1.0, min(1.0, -math.tan(math.radians(LATITUD)) * math.tan(decl))))) / 15.0
        d = (tmin, tmax, nub, lluvia, tmin - dep, pm, 12.0 - medio_dia + 0.4, 14.6)
        self._dias[k] = d
        return d

    def _entre_mediodias(self, ordinal: int, hs: float, aj: AjusteClima, i: int) -> float:
        """Interpola un valor diario (índice `i` de `_dia`) entre los mediodías solares."""
        if hs < 12.0:
            a, b, f = self._dia(ordinal - 1, aj)[i], self._dia(ordinal, aj)[i], (hs + 12.0) / 24.0
        else:
            a, b, f = self._dia(ordinal, aj)[i], self._dia(ordinal + 1, aj)[i], (hs - 12.0) / 24.0
        return a + (float(b) - float(a)) * suave(f)

    def _temperatura(self, ordinal: int, hs: float, aj: AjusteClima) -> float:
        d = self._dia(ordinal, aj)
        hmin, hmax = d[6], d[7]
        if hs < hmin:
            p = self._dia(ordinal - 1, aj)
            x = (hs + 24.0 - p[7]) / (hmin + 24.0 - p[7])
            return p[1] + (d[0] - p[1]) * (1.0 - math.cos(math.pi * x)) / 2.0
        if hs <= hmax:
            x = (hs - hmin) / (hmax - hmin)
            return d[0] + (d[1] - d[0]) * (1.0 - math.cos(math.pi * x)) / 2.0
        n = self._dia(ordinal + 1, aj)
        x = (hs - hmax) / (n[6] + 24.0 - hmax)
        return d[1] + (n[0] - d[1]) * (1.0 - math.cos(math.pi * x)) / 2.0

    def intensidad_lluvia(self, t: float, aj: AjusteClima) -> float:
        """mm/h, constante dentro de cada hora."""
        hora = math.floor(t / 3600.0)
        ordinal = ORDINAL_EPOCA + math.floor((t + desfase_madrid(t)) / 86400.0)
        if not self._dia(ordinal, aj)[3]:
            return 0.0
        if u01(self.c_lluvia_h, hora) >= 0.45:
            return 0.0
        return min(10.0, -math.log(1.0 - u01(self.c_lluvia_h, hora, 1)) * 1.1 + 0.1)

    def precipitacion_dia(self, t: float, desde: float, aj: AjusteClima) -> float:
        """mm acumulados entre `desde` (medianoche local) y `t`."""
        total = 0.0
        h = desde
        while h < t:
            fin = min(t, (math.floor(h / 3600.0) + 1) * 3600.0)
            total += self.intensidad_lluvia(h, aj) * (fin - h) / 3600.0
            h = fin
        return total

    # ------------------------------------------------------------------ estado
    def calcular(self, t: float, aj: AjusteClima) -> EstadoExterior:
        alt, az, eot = posicion_sol(t)
        ts = t + (LONGITUD / 15.0) * 3600.0 + eot * 60.0
        dias = math.floor(ts / 86400.0)
        ordinal = ORDINAL_EPOCA + dias
        hs = (ts - dias * 86400.0) / 3600.0

        e = EstadoExterior()
        e.te = self._temperatura(ordinal, hs, aj)
        td = self._entre_mediodias(ordinal, hs, aj, 4)
        e.rh = max(8.0, min(100.0, 100.0 * presion_saturacion(td) / presion_saturacion(e.te)))
        e.w = razon_humedad(e.te, e.rh)
        nub = self._entre_mediodias(ordinal, hs, aj, 2) + 0.15 * ruido(self.c_nub_h, t, 3 * 3600.0)
        if aj.nubosidad_max is not None:
            nub = min(nub, aj.nubosidad_max)
        e.nubosidad = max(0.0, min(1.0, nub))
        e.lluvia = self.intensidad_lluvia(t, aj)
        if e.lluvia > 0:
            e.rh = max(e.rh, 88.0)
            e.w = razon_humedad(e.te, e.rh)
        lluvia_dia = self._entre_mediodias(ordinal, hs, aj, 3)

        tarde = max(0.0, math.sin(math.pi * (hs - 9.0) / 12.0)) if 9.0 < hs < 21.0 else 0.0
        e.viento = max(0.2, 2.6 + 1.6 * ruido(self.c_viento, t, 6 * 3600.0) + 0.7 * ruido(self.c_viento2, t, 2400.0)
                       + 1.0 * tarde + 2.0 * lluvia_dia)
        e.direccion = (240.0 + 100.0 * ruido(self.c_dir, t, 8 * 3600.0) + 25.0 * ruido(self.c_dir2, t, 1800.0)) % 360.0
        e.presion = 1016.0 + 7.0 * ruido(self.c_pres, t, 3 * 86400.0) - 8.0 * lluvia_dia

        e.alt_sol = alt
        if alt > 0.0:
            sh = math.sin(alt)
            ghi_claro = 1098.0 * sh * math.exp(-0.057 / sh)
            ghi = ghi_claro * (1.0 - 0.75 * e.nubosidad ** 3.4)
            if e.lluvia > 0:
                ghi *= 0.6
            kd = min(1.0, 0.13 + 0.85 * e.nubosidad ** 1.5)
            dhi = kd * ghi
            dni = (ghi - dhi) / max(sh, 0.05)
            ca = math.cos(alt)
            for i, azf in enumerate(AZIMUT_FACHADAS):
                directa = dni * max(0.0, ca * math.cos(az - math.radians(azf)))
                e.fachadas_dir[i] = directa
                e.fachadas[i] = directa + 0.5 * dhi + 0.1 * ghi
            cb = math.cos(_INCLINACION_FV)
            cos_i = sh * cb + ca * math.sin(_INCLINACION_FV) * math.cos(az - math.pi)
            e.poa_fv = dni * max(0.0, cos_i) + dhi * (1.0 + cb) / 2.0 + 0.2 * ghi * (1.0 - cb) / 2.0
            e.ghi = ghi
            e.lux = ghi * 115.0
        # CO₂ y partículas exteriores, con algo más de tráfico a primera hora y al anochecer
        hl = ((t + desfase_madrid(t)) % 86400.0) / 3600.0
        trafico = math.exp(-((hl - 8.5) / 1.2) ** 2) + 0.7 * math.exp(-((hl - 20.0) / 1.5) ** 2)
        e.co2 = 400.0 + 6.0 * ruido(self.c_co2, t, 7200.0) + 12.0 * trafico
        e.pm25 = self._entre_mediodias(ordinal, hs, aj, 5) * (1.0 + 0.3 * trafico)
        return e
