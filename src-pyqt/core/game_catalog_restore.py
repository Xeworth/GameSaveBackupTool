"""
Catalog restore and scan-finalize helpers (WinUI MainViewModel.Catalog parity).

Shared by PyQt and future TUI — no UI dependencies.
"""

from __future__ import annotations

from typing import Any, Dict, Iterable, List, Set, Tuple

from core.catalog_game_keys import catalog_key_from_detected_game
from core.custom_game_catalog import game_info_from_catalog
from core.game_scan_post_processor import deduplicate_by_shared_save_root
from core.scan_service import is_user_added_catalog_entry
from core.save_manager import SaveManager

SAVED_GAME_LIST_ESTABLISHED_KEY = "saved_game_list_established"


def has_saved_game_list_established(settings) -> bool:
    return bool(settings.value(SAVED_GAME_LIST_ESTABLISHED_KEY, False, type=bool))


def mark_saved_game_list_established(settings) -> None:
    settings.setValue(SAVED_GAME_LIST_ESTABLISHED_KEY, True)


def startup_restore_plan(save_manager: SaveManager, settings) -> Tuple[bool, bool]:
    """
    Return (should_restore, should_mark_established_for_migration).
    """
    catalog = save_manager.game_save_locations
    if not catalog:
        return False, False
    if has_saved_game_list_established(settings):
        return True, False
    return True, True


def find_catalog_entry_insensitive(
    catalog: Dict[str, dict],
    key: str,
) -> Tuple[str, dict] | None:
    if not key:
        return None
    if key in catalog:
        return key, catalog[key]
    lower = key.lower()
    for name, row in catalog.items():
        if str(name).lower() == lower:
            return name, row
    return None


def build_scan_result_from_catalog(
    game_name: str,
    catalog_row: dict,
    save_manager: SaveManager,
) -> dict | None:
    if not str(game_name or "").strip():
        return None

    reg_only = bool(catalog_row.get("save_in_registry_only"))
    raw_path = str(catalog_row.get("save_path") or "").strip() or None
    app_id = catalog_row.get("steam_app_id")
    platform = catalog_row.get("platform") or (
        "Steam" if app_id else "Unknown"
    )

    resolved = None
    if not reg_only and raw_path:
        resolved = save_manager.resolve_path(raw_path)

    return {
        "row_id": game_name,
        "name": game_name,
        "app_id": app_id,
        "install_path": catalog_row.get("install_path"),
        "platform": platform,
        "save_path_raw": raw_path,
        "save_path_resolved": resolved,
        "save_in_registry_only": reg_only,
        "save_registry_hive": catalog_row.get("save_registry_hive"),
        "save_registry_subkey": catalog_row.get("save_registry_subkey"),
        "source": "Cached",
        "scan_outcome": catalog_row.get("scan_outcome") or "NO_MANIFEST_PATHS",
    }


def is_scan_derived_game_info(game_info: dict, save_manager: SaveManager) -> bool:
    if game_info.get("is_user_added") or game_info.get("is_custom_game"):
        return False
    if str(game_info.get("source") or "") == "Manual":
        return False
    name = str(game_info.get("name") or "")
    if not name:
        return True
    row = save_manager.get_save_location(name)
    if row and is_user_added_catalog_entry(row):
        return False
    return True


def merge_user_added_from_catalog(
    save_manager: SaveManager,
    existing_names: Set[str],
) -> List[dict]:
    out: List[dict] = []
    for name, row in save_manager.game_save_locations.items():
        if not is_user_added_catalog_entry(row):
            continue
        if _name_in_set(name, existing_names):
            continue
        out.append(game_info_from_catalog(name, row, save_manager))
    return out


def merge_skipped_detected_rows(
    detected: Iterable[dict],
    scanned_for_save_lookup: Iterable[dict],
    save_manager: SaveManager,
    existing_names: Set[str],
) -> List[dict]:
    scanned_keys = {
        catalog_key_from_detected_game(g).lower()
        for g in scanned_for_save_lookup
    }
    out: List[dict] = []
    catalog = save_manager.game_save_locations

    for game in detected:
        key = catalog_key_from_detected_game(game)
        if key.lower() in scanned_keys:
            continue
        found = find_catalog_entry_insensitive(catalog, key)
        if found is None:
            continue
        catalog_key, row = found
        if _name_in_set(catalog_key, existing_names):
            continue
        out.append(game_info_from_catalog(catalog_key, row, save_manager))
    return out


def finalize_scan_game_infos(
    detected: Iterable[dict],
    scanned_for_save_lookup: Iterable[dict],
    save_manager: SaveManager,
    existing_names: Set[str],
) -> List[dict]:
    names = set(existing_names)
    merged: List[dict] = []
    for info in merge_skipped_detected_rows(
        detected, scanned_for_save_lookup, save_manager, names
    ):
        merged.append(info)
        names.add(info["name"])
    for info in merge_user_added_from_catalog(save_manager, names):
        merged.append(info)
    return merged


def restore_game_infos_from_catalog(
    save_manager: SaveManager,
    *,
    dedupe_shared: bool,
) -> Tuple[List[dict], str]:
    """
    Load custom + scan-derived rows from disk catalog (with optional dedup).
    Returns (game_info list, status message).
    """
    catalog = save_manager.game_save_locations
    if not catalog:
        return [], "Ready. Click 'Scan for games'."

    infos: List[dict] = []
    existing: Set[str] = set()

    for name, row in catalog.items():
        if not is_user_added_catalog_entry(row):
            continue
        if _name_in_set(name, existing):
            continue
        info = game_info_from_catalog(name, row, save_manager)
        infos.append(info)
        existing.add(info["name"])

    scan_results: List[dict] = []
    for name, row in catalog.items():
        if is_user_added_catalog_entry(row):
            continue
        built = build_scan_result_from_catalog(name, row, save_manager)
        if built:
            scan_results.append(built)

    to_restore = scan_results
    if dedupe_shared and len(scan_results) > 1:
        kept, dropped = deduplicate_by_shared_save_root(scan_results)
        if dropped:
            save_manager.delete_games(dropped)
        to_restore = kept

    found_count = 0
    not_found_count = 0
    for result in to_restore:
        name = str(result.get("name") or "")
        found = find_catalog_entry_insensitive(catalog, name)
        if found is None:
            continue
        catalog_key, row = found
        if _name_in_set(catalog_key, existing):
            continue
        info = game_info_from_catalog(catalog_key, row, save_manager)
        infos.append(info)
        existing.add(info["name"])
        if info.get("save_path_resolved") or info.get("save_in_registry_only"):
            found_count += 1
        else:
            not_found_count += 1

    if not infos:
        return [], "Ready. Click 'Scan for games'."

    status = f"Loaded {len(infos)} game(s). Click 'Scan' to refresh installs and save paths."
    return infos, status


def scan_result_to_game_info(result: dict) -> dict:
    """Normalize a ScanService result dict for the game table."""
    info = dict(result)
    info.pop("_metrics", None)
    info.setdefault("is_user_added", False)
    return info


def _name_in_set(name: str, names: Set[str]) -> bool:
    lower = str(name).lower()
    return any(str(n).lower() == lower for n in names)
