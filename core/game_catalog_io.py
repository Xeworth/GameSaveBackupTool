"""Export / import discovered game save catalog (JSON or CSV)."""
from __future__ import annotations

import csv
import json
import os
from datetime import datetime, timezone
from typing import Any, Dict, Tuple


CATALOG_JSON_KEY = "gsbt_game_catalog"
CATALOG_VERSION = 1


def export_catalog_json(game_save_locations: Dict[str, Any], path: str) -> None:
    payload = {
        CATALOG_JSON_KEY: CATALOG_VERSION,
        "exported_at": datetime.now(timezone.utc).isoformat(),
        "games": game_save_locations,
    }
    parent = os.path.dirname(os.path.abspath(path))
    if parent:
        os.makedirs(parent, exist_ok=True)
    with open(path, "w", encoding="utf-8") as f:
        json.dump(payload, f, indent=2, ensure_ascii=False)


def export_catalog_csv(game_save_locations: Dict[str, Any], path: str) -> None:
    parent = os.path.dirname(os.path.abspath(path))
    if parent:
        os.makedirs(parent, exist_ok=True)
    fieldnames = [
        "name",
        "steam_app_id",
        "save_path",
        "save_registry_hive",
        "save_registry_subkey",
        "save_in_registry_only",
        "last_backup",
        "notes",
    ]
    with open(path, "w", encoding="utf-8", newline="") as f:
        w = csv.DictWriter(f, fieldnames=fieldnames, extrasaction="ignore")
        w.writeheader()
        for name, row in game_save_locations.items():
            d = {fn: "" if row.get(fn) is None else str(row.get(fn, "")) for fn in fieldnames if fn != "name"}
            d["name"] = name
            w.writerow(d)


def _row_bool(raw: str) -> bool:
    return raw.strip().lower() in ("1", "true", "yes", "y")


def import_catalog_csv(path: str) -> Dict[str, Any]:
    out: Dict[str, Any] = {}
    with open(path, "r", encoding="utf-8-sig", newline="") as f:
        r = csv.DictReader(f)
        if not r.fieldnames or "name" not in r.fieldnames:
            raise ValueError("CSV must include a 'name' column.")
        for row in r:
            name = (row.get("name") or "").strip()
            if not name:
                continue
            entry: Dict[str, Any] = {}
            if "save_path" in row and row["save_path"]:
                entry["save_path"] = row["save_path"].strip()
            if "steam_app_id" in row and str(row.get("steam_app_id", "")).strip():
                v = str(row["steam_app_id"]).strip()
                entry["steam_app_id"] = v if not v.isdigit() else int(v)
            for k in ("save_registry_hive", "save_registry_subkey", "notes"):
                if k in row and str(row.get(k, "")).strip():
                    entry[k] = str(row[k]).strip()
            if "save_in_registry_only" in row and str(row.get("save_in_registry_only", "")).strip():
                entry["save_in_registry_only"] = _row_bool(str(row["save_in_registry_only"]))
            if "last_backup" in row and str(row.get("last_backup", "")).strip():
                entry["last_backup"] = str(row["last_backup"]).strip()
            out[name] = entry
    return out


def import_catalog_json(path: str) -> Tuple[Dict[str, Any], str]:
    """
    Load games dict from JSON.

    Returns ``(games_dict, format_note)`` where format_note describes envelope.
    """
    with open(path, "r", encoding="utf-8") as f:
        data = json.load(f)
    if isinstance(data, dict) and CATALOG_JSON_KEY in data:
        inner = data.get("games")
        if not isinstance(inner, dict):
            raise ValueError("Invalid catalog JSON: missing 'games' object.")
        return inner, "GSBT catalog envelope"
    if isinstance(data, dict) and all(isinstance(v, dict) for v in data.values()):
        return data, "flat game map (same as game_save_data.json)"
    raise ValueError("Unrecognized JSON structure for game catalog import.")
