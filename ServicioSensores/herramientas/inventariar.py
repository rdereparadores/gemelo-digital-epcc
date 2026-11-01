"""Hace un inventario de lo que publica SmartPoliTech y lo guarda en datos/inventario_smartpolitech.json.

SmartPoliTech expone un Grafana (http://smartpolitech.unex.es) con acceso anónimo de lectura; sus
paneles consultan un InfluxDB 1.x (fuente "smart-sensors", base "sensors") a través del proxy de
fuentes de datos de Grafana. Este script usa esa misma vía, solo con consultas de lectura y de una
en una, para listar las mediciones, sus campos y el último punto de cada una.

Uso:  uv run python herramientas/inventariar.py [--base http://smartpolitech.unex.es]
"""

from __future__ import annotations

import argparse
import json
import time
from concurrent.futures import ThreadPoolExecutor
from datetime import UTC, datetime
from pathlib import Path

import httpx

RAIZ = Path(__file__).resolve().parents[1]
SALIDA = RAIZ / "datos" / "inventario_smartpolitech.json"
# Campos con direcciones de red o claves de los dispositivos: no hacen falta y no se versionan.
PRIVADOS = {"ip", "ips", "mac", "apikey", "gatewayID", "idusuario", "nombredisp"}


def consultar(cliente: httpx.Client, base: str, ds: int, db: str, q: str, timeout: float = 60) -> list[dict]:
    r = cliente.get(
        f"{base}/api/datasources/proxy/{ds}/query",
        params={"db": db, "q": q, "epoch": "s"},
        timeout=timeout,
    )
    r.raise_for_status()
    return r.json()["results"]


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--base", default="http://smartpolitech.unex.es")
    ap.add_argument("--datasource", type=int, default=1)
    ap.add_argument("--db", default="sensors")
    ap.add_argument("--hilos", type=int, default=4)
    args = ap.parse_args()

    with httpx.Client(headers={"User-Agent": "gemelo-epcc-inventario/0.1"}) as c:
        q = lambda s, **kw: consultar(c, args.base, args.datasource, args.db, s, **kw)  # noqa: E731
        mediciones = [v[0] for v in q("SHOW MEASUREMENTS")[0]["series"][0]["values"]]
        campos = {s["name"]: {v[0]: v[1] for v in s["values"]} for s in q("SHOW FIELD KEYS")[0]["series"]}
        etiquetas = {s["name"]: [v[0] for v in s["values"]] for s in q("SHOW TAG KEYS")[0].get("series", [])}
        print(f"{len(mediciones)} mediciones")

        def ultimo(m: str) -> tuple[str, dict | str | None]:
            t = time.time()
            try:
                res = q(f'SELECT * FROM "{m}" ORDER BY time DESC LIMIT 1', timeout=30)[0]
            except Exception as e:  # noqa: BLE001 - una medición rota no debe parar el inventario
                print(f"  {m}: {type(e).__name__} ({time.time() - t:.0f} s)", flush=True)
                return m, f"error: {type(e).__name__}"
            serie = res.get("series")
            if not serie:
                return m, None
            fila = dict(zip(serie[0]["columns"], serie[0]["values"][0]))
            return m, {k: v for k, v in fila.items() if k not in PRIVADOS}

        t0 = time.time()
        with ThreadPoolExecutor(args.hilos) as ex:
            ultimos = dict(ex.map(ultimo, mediciones))
        print(f"últimos puntos en {time.time() - t0:.0f} s")

    inventario = {
        "_meta": {
            "fuente": args.base,
            "datasource_id": args.datasource,
            "database": args.db,
            "generado": datetime.now(UTC).isoformat(timespec="seconds"),
        },
        "measurements": {
            m: {"fields": campos.get(m, {}), "tags": etiquetas.get(m, []), "last": ultimos.get(m)}
            for m in mediciones
        },
    }
    SALIDA.parent.mkdir(parents=True, exist_ok=True)
    SALIDA.write_text(json.dumps(inventario, ensure_ascii=False, indent=1), encoding="utf-8")
    print(f"escrito {SALIDA.relative_to(RAIZ)}")


if __name__ == "__main__":
    main()
