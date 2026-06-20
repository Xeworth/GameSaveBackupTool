"""Skip save lookup for installs already catalogued with no usable save path (WinUI CatalogAwareDetectionFilter parity)."""

from __future__ import annotations

from typing import Any, Dict, List

from core.catalog_game_keys import catalog_key_from_detected_game


def filter_games_for_rescan(
    detected_games: List[dict],
    catalog: Dict[str, dict],
    *,
    skip_when_previously_not_found: bool = True,
) -> List[dict]:
    if not skip_when_previously_not_found:
        return list(detected_games)

    out: List[dict] = []
    for game in detected_games:
        key = catalog_key_from_detected_game(game)
        row = _catalog_row_insensitive(catalog, key)
        if row is None:
            out.append(game)
            continue

        raw = str(row.get("save_path") or "").strip()
        has_path = bool(raw)
        reg_only = bool(row.get("save_in_registry_only"))
        if has_path or reg_only:
            out.append(game)
    return out


def _catalog_row_insensitive(catalog: Dict[str, dict], key: str) -> dict | None:
    if not key:
        return None
    if key in catalog:
        return catalog[key]
    lower = key.lower()
    for name, row in catalog.items():
        if str(name).lower() == lower:
            return row
    return None
