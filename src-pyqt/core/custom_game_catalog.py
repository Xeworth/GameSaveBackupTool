"""Catalog helpers for custom games and manual row assignment (TUI + PyQt shared)."""

from __future__ import annotations

import os
import re
from typing import Any, Dict, Optional, Tuple

from core.game_name_validation import is_valid_game_name_for_storage, sanitize_for_windows_path_segment
from core.registry_save_resolver import (
    extract_registry_hints,
    format_registry_save_display,
    looks_like_registry_hive_line,
    normalize_registry_pasted_path,
    resolve_registry_hint_to_save_folder,
    try_registry_key_as_in_key_save_location,
)
from core.save_manager import SaveManager
from core.scan_service import is_user_added_catalog_entry


def _clean_display_name(name: str) -> str:
    s = re.sub(r"[®™©]", "", name or "").strip()
    return re.sub(r"\s+", " ", s)


def looks_like_filesystem_path(text: str) -> bool:
    t = (text or "").strip()
    if not t:
        return False
    if re.search(r"[A-Za-z]:\\", t) or t.startswith("\\\\"):
        return True
    if t.startswith("/") or t.startswith("~"):
        return True
    if "%" in t and "\\" in t:
        return True
    return os.path.isabs(t)


def game_info_from_catalog(name: str, catalog_row: dict, save_manager: SaveManager) -> dict:
    raw = catalog_row.get("save_path") or ""
    resolved = save_manager.resolve_path(raw) if raw else None
    reg_only = bool(catalog_row.get("save_in_registry_only"))
    hive = catalog_row.get("save_registry_hive")
    sub = catalog_row.get("save_registry_subkey")
    if reg_only and hive and sub:
        display = format_registry_save_display(str(hive), str(sub))
    else:
        display = resolved
    return {
        "name": name,
        "app_id": catalog_row.get("steam_app_id"),
        "install_path": catalog_row.get("install_path") or "Not Scanned",
        "platform": catalog_row.get("platform") or "Custom",
        "save_path_raw": raw or None,
        "save_path_resolved": resolved if not reg_only else None,
        "save_location_display": display,
        "save_in_registry_only": reg_only,
        "save_registry_hive": hive,
        "save_registry_subkey": sub,
        "source": "Manual" if catalog_row.get("is_custom_game") else catalog_row.get("source", "Cached"),
        "last_backup": catalog_row.get("last_backup"),
        "scan_outcome": catalog_row.get("scan_outcome"),
        "is_custom_game": bool(catalog_row.get("is_custom_game")),
        "is_user_added": is_user_added_catalog_entry(catalog_row),
    }


def build_folder_custom_game_payload(folder_raw: str, save_manager: SaveManager) -> Tuple[bool, str, Optional[dict]]:
    folder_input = (folder_raw or "").strip()
    if not folder_input:
        return False, "Choose a save folder.", None
    try:
        resolved = os.path.normpath(save_manager.resolve_path(folder_input) or folder_input)
    except (OSError, ValueError):
        return False, "That folder path is not valid.", None
    if not os.path.isdir(resolved):
        return False, "That folder does not exist or is not reachable.", None
    return True, "", {
        "save_path": folder_input,
        "scan_outcome": "SAVE_ON_DISK",
        "is_custom_game": True,
    }


def build_registry_custom_game_payload(registry_raw: str) -> Tuple[bool, str, Optional[dict]]:
    text = normalize_registry_pasted_path(registry_raw or "")
    if not text.strip():
        return False, "Enter a registry path or paste from Regedit.", None
    if looks_like_filesystem_path(text) and text.lower().endswith(".reg"):
        return False, "Use “Browse for .reg” or paste a registry key path from Regedit.", None
    if looks_like_filesystem_path(text):
        return False, "That looks like a file or folder path, not a registry key.", None

    hints = extract_registry_hints(text)
    if not hints and looks_like_registry_hive_line(text):
        hints = [normalize_registry_pasted_path(text)]

    if not hints:
        return False, "Could not parse a registry path. Example: HKCU\\Software\\…", None

    for hint in hints:
        folder = resolve_registry_hint_to_save_folder(hint)
        if folder:
            return True, "", {
                "save_path": folder,
                "scan_outcome": "SAVE_ON_DISK",
                "is_custom_game": True,
                "save_resolved_via_registry": True,
            }
        loc = try_registry_key_as_in_key_save_location(hint)
        if loc:
            hive, sub = loc
            return True, "", {
                "save_path": "",
                "scan_outcome": "REGISTRY_IN_KEY",
                "is_custom_game": True,
                "save_in_registry_only": True,
                "save_registry_hive": hive,
                "save_registry_subkey": sub,
            }

    return False, "Registry key was not found or does not contain save data.", None


def add_custom_game(
    save_manager: SaveManager,
    raw_name: str,
    *,
    folder_path: Optional[str] = None,
    registry_path: Optional[str] = None,
) -> Tuple[bool, str, Optional[dict]]:
    ok, err = is_valid_game_name_for_storage(raw_name)
    if not ok:
        return False, err or "Invalid name.", None
    display = _clean_display_name(raw_name)
    ok2, err2 = is_valid_game_name_for_storage(display)
    if not ok2:
        return False, err2 or "Invalid name.", None

    if folder_path is not None:
        f_ok, f_err, payload = build_folder_custom_game_payload(folder_path, save_manager)
        if not f_ok:
            return False, f_err, None
    elif registry_path is not None:
        r_ok, r_err, payload = build_registry_custom_game_payload(registry_path)
        if not r_ok:
            return False, r_err, None
    else:
        return False, "Provide a save folder or registry path.", None

    save_manager.add_or_update_save_location(display, payload)
    info = game_info_from_catalog(display, payload, save_manager)
    info["platform"] = "Custom"
    info["source"] = "Manual"
    info["install_path"] = "N/A"
    return True, f"Added custom game: {display}", info


def row_has_save_location(game_info: dict) -> bool:
    if game_info.get("save_in_registry_only"):
        return bool(game_info.get("save_registry_hive") and game_info.get("save_registry_subkey"))
    resolved = game_info.get("save_path_resolved")
    return bool(resolved and os.path.isdir(str(resolved)))


def assign_save_folder_for_row(
    save_manager: SaveManager,
    game_name: str,
    folder_raw: str,
    existing_info: dict,
) -> Tuple[bool, str, Optional[dict]]:
    if row_has_save_location(existing_info):
        return False, "This entry already has a save location.", None
    f_ok, f_err, payload = build_folder_custom_game_payload(folder_raw, save_manager)
    if not f_ok:
        return False, f_err, None

    existing = save_manager.get_save_location(game_name) or {}
    merged = dict(existing)
    merged.update(payload)
    if existing.get("last_backup"):
        merged["last_backup"] = existing["last_backup"]
    if existing.get("steam_app_id") is not None:
        merged["steam_app_id"] = existing["steam_app_id"]
    save_manager.add_or_update_save_location(game_name, merged)

    info = dict(existing_info)
    info.update(game_info_from_catalog(game_name, merged, save_manager))
    return True, f"Save folder set for “{game_name}”.", info


def assign_registry_save_for_row(
    save_manager: SaveManager,
    game_name: str,
    registry_raw: str,
    existing_info: dict,
) -> Tuple[bool, str, Optional[dict]]:
    if row_has_save_location(existing_info):
        return False, "This entry already has a save location.", None
    r_ok, r_err, payload = build_registry_custom_game_payload(registry_raw)
    if not r_ok:
        return False, r_err, None

    existing = save_manager.get_save_location(game_name) or {}
    merged = dict(existing)
    merged.update(payload)
    merged.pop("save_resolved_via_registry", None)
    if payload.get("save_resolved_via_registry"):
        merged["save_resolved_via_registry"] = True
    if existing.get("last_backup"):
        merged["last_backup"] = existing["last_backup"]
    if existing.get("steam_app_id") is not None:
        merged["steam_app_id"] = existing["steam_app_id"]
    save_manager.add_or_update_save_location(game_name, merged)

    info = dict(existing_info)
    info.update(game_info_from_catalog(game_name, merged, save_manager))
    return True, "Save location updated from registry.", info
