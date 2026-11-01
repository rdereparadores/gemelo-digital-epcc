"""Azar determinista y barato para el motor de simulación.

Todo lo aleatorio del modelo sale de funciones hash de enteros (splitmix64), no de un generador con
estado: así una decisión (si hay clase en un aula a una hora, si alguien abre una ventana en un
minuto…) depende solo de la semilla y de lo que se decide, y no del orden de las llamadas ni de
cuántas veces se haya consultado.
"""

from __future__ import annotations

import hashlib
import math

_M64 = (1 << 64) - 1
_INV53 = 1.0 / (1 << 53)


def mezclar(x: int) -> int:
    """Finalizador de splitmix64: entero de 64 bits bien mezclado."""
    x = (x + 0x9E3779B97F4A7C15) & _M64
    x = ((x ^ (x >> 30)) * 0xBF58476D1CE4E5B9) & _M64
    x = ((x ^ (x >> 27)) * 0x94D049BB133111EB) & _M64
    return x ^ (x >> 31)


def clave(texto: str, semilla: int) -> int:
    """Clave estable (independiente de PYTHONHASHSEED) de un texto con la semilla."""
    d = hashlib.blake2b(f"{semilla}\x1f{texto}".encode(), digest_size=8).digest()
    return int.from_bytes(d, "little")


def u01(c: int, a: int = 0, b: int = 0) -> float:
    """Uniforme en [0, 1) determinada por la clave y dos enteros."""
    h = mezclar(c ^ (a & _M64))
    h = mezclar(h ^ (b & _M64))
    return (h >> 11) * _INV53


def suave(f: float) -> float:
    return f * f * (3.0 - 2.0 * f)


def ruido(c: int, t: float, periodo: float) -> float:
    """Ruido de valor suave en [-1, 1]: nudos aleatorios cada `periodo` e interpolación suave."""
    x = t / periodo
    k = math.floor(x)
    a = u01(c, k)
    b = u01(c, k + 1)
    return 2.0 * (a + (b - a) * suave(x - k)) - 1.0


class RuidoSensor:
    """Ruido de sensor con correlación temporal y caché de nudos (barato si se lee a menudo).

    Dos octavas de ruido de valor: una lenta (deriva) y otra rápida (fluctuación), en [-1, 1].
    """

    __slots__ = ("a1", "a2", "b1", "b2", "c", "k1", "k2", "periodo")

    def __init__(self, c: int, periodo: float) -> None:
        self.c = c
        self.periodo = periodo
        self.k1 = self.k2 = None
        self.a1 = self.b1 = self.a2 = self.b2 = 0.0

    def valor(self, t: float) -> float:
        x = t / self.periodo
        k = math.floor(x)
        if k != self.k1:
            if self.k1 is not None and k == self.k1 + 1:
                self.a1 = self.b1
            else:
                self.a1 = u01(self.c, k)
            self.b1 = u01(self.c, k + 1)
            self.k1 = k
        v1 = self.a1 + (self.b1 - self.a1) * suave(x - k)
        x2 = x * 5.0
        k2 = math.floor(x2)
        if k2 != self.k2:
            if self.k2 is not None and k2 == self.k2 + 1:
                self.a2 = self.b2
            else:
                self.a2 = u01(self.c ^ 0x5555, k2)
            self.b2 = u01(self.c ^ 0x5555, k2 + 1)
            self.k2 = k2
        v2 = self.a2 + (self.b2 - self.a2) * suave(x2 - k2)
        return 2.0 * (0.7 * v1 + 0.3 * v2) - 1.0
