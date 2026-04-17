"""
Resolve PC Gaming Wiki / in-game save locations that point at the Windows registry.

Some games store the save *folder* in a REG_SZ (or only document a registry key); we read
the key and return a normal directory path when it exists on disk.
"""

from __future__ import annotations

import json
import os
import re
import winreg
from pathlib import Path
from typing import Dict, List, Optional, Tuple

from core.save_location_fetcher import path_to_directory_only

# Built-in fallbacks when PCGW has no data (extend via config/steam_registry_save_keys.json).
STEAM_APP_REGISTRY_SAVE_KEYS: Dict[str, Tuple[str, str]] = {
    "1843760": ("HKEY_CURRENT_USER", r"Software\Die of Death Games\Rogue Tower"),
}

_STEAM_REGISTRY_MERGED_CACHE: Optional[Dict[str, Tuple[str, str]]] = None


def merged_steam_registry_save_keys() -> Dict[str, Tuple[str, str]]:
    """Defaults plus optional JSON map: app id string -> (hive_name, subkey)."""
    global _STEAM_REGISTRY_MERGED_CACHE
    if _STEAM_REGISTRY_MERGED_CACHE is not None:
        return _STEAM_REGISTRY_MERGED_CACHE
    out: Dict[str, Tuple[str, str]] = dict(STEAM_APP_REGISTRY_SAVE_KEYS)
    cfg = Path(__file__).resolve().parent.parent / "config" / "steam_registry_save_keys.json"
    if cfg.is_file():
        try:
            raw = json.loads(cfg.read_text(encoding="utf-8"))
            if isinstance(raw, dict):
                for k, v in raw.items():
                    aid = str(k).strip()
                    if not aid or not isinstance(v, dict):
                        continue
                    hive = str(v.get("hive", "")).strip()
                    sub = str(v.get("subkey", "")).strip()
                    if hive and sub:
                        out[aid] = (hive, sub.replace("/", "\\"))
        except (json.JSONDecodeError, OSError):
            pass
    _STEAM_REGISTRY_MERGED_CACHE = out
    return out

_HIVE_PREFIXES: tuple[tuple[str, int], ...] = (
    ("HKEY_CURRENT_USER\\", winreg.HKEY_CURRENT_USER),
    ("HKCU\\", winreg.HKEY_CURRENT_USER),
    ("HKEY_LOCAL_MACHINE\\", winreg.HKEY_LOCAL_MACHINE),
    ("HKLM\\", winreg.HKEY_LOCAL_MACHINE),
    ("HKEY_USERS\\", winreg.HKEY_USERS),
    ("HKU\\", winreg.HKEY_USERS),
    ("HKEY_CLASSES_ROOT\\", winreg.HKEY_CLASSES_ROOT),
    ("HKCR\\", winreg.HKEY_CLASSES_ROOT),
)

# Order: try explicit value name(s), then default.
_VALUE_NAMES_TO_TRY: tuple[str, ...] = (
    "SavePath",
    "Savegame",
    "SaveDirectory",
    "Save Dir",
    "SaveDir",
    "SaveGame",
    "PlayerSaveDir",
    "InstallPath",
    "Path",
    "SaveLocation",
    "DataPath",
    "",
)

_REGISTRY_LINE_RE = re.compile(
    r"(?is)\b("
    r"HKEY_(?:CURRENT_USER|LOCAL_MACHINE|USERS|CLASSES_ROOT|PERFORMANCE_DATA)\s*[\\/][^;\n\r|]+"
    r"|(?:HKCU|HKLM|HKCR|HKU)\s*[\\/][^;\n\r|]+"
    r")"
)


def wiki_cell_looks_like_file_path(text: str) -> bool:
    """True if the wiki cell text looks like a filesystem path (not registry-only)."""
    if not text or not isinstance(text, str):
        return False
    t = text.strip()
    if not t:
        return False
    if "%" in t or t.startswith("~/") or "/Users/" in t or "/Library/" in t:
        return True
    if re.search(r"[A-Za-z]:\\", t) or re.search(r"[A-Za-z]:/", t):
        return True
    return False


def looks_like_registry_hive_line(text: str) -> bool:
    if not text or not isinstance(text, str):
        return False
    s = normalize_registry_pasted_path(text)
    if not s:
        return False
    up = s.upper()
    if up.startswith("HKEY_") or up.startswith("HKCU") or up.startswith("HKLM") or up.startswith("HKCR") or up.startswith("HKU"):
        return True
    return bool(_REGISTRY_LINE_RE.search(s))


def normalize_registry_pasted_path(text: str) -> str:
    """Strip ``Computer\\`` prefix from Registry Editor copy-paste."""
    return re.sub(r"(?i)^Computer\\+", "", (text or "").strip())


def extract_registry_hints(text: Optional[str]) -> List[str]:
    """Return distinct registry path substrings found in a wiki cell or paragraph."""
    if not text or not isinstance(text, str):
        return []
    text = normalize_registry_pasted_path(text)
    out: List[str] = []
    seen: set[str] = set()
    for m in _REGISTRY_LINE_RE.finditer(text):
        h = m.group(1).strip().strip('"').replace("/", "\\")
        if h and h not in seen:
            seen.add(h)
            out.append(h)
    return out


def _split_hive_and_remainder(hint: str) -> tuple[Optional[int], str]:
    hint = hint.strip().strip('"').replace("/", "\\")
    for prefix, hive in _HIVE_PREFIXES:
        if hint.upper().startswith(prefix.upper()):
            rest = hint[len(prefix) :].lstrip("\\")
            return hive, rest
    return None, ""


def _value_to_folder(val: object, typ: int) -> Optional[str]:
    if typ == winreg.REG_MULTI_SZ:
        if not isinstance(val, list):
            return None
        parts = [str(x).strip() for x in val if str(x).strip()]
        if not parts:
            return None
        return _value_to_folder(parts[0], winreg.REG_SZ)
    if typ not in (winreg.REG_SZ, winreg.REG_EXPAND_SZ):
        return None
    s = os.path.expandvars(str(val).strip().strip('"'))
    if not s:
        return None
    if os.path.isfile(s):
        s = os.path.dirname(s)
    s = os.path.normpath(s)
    if os.path.isdir(s):
        return path_to_directory_only(s + "\\") if not s.endswith("\\") else path_to_directory_only(s)
    return None


def _try_read_values_from_open_key(key) -> Optional[str]:
    for vn in _VALUE_NAMES_TO_TRY:
        try:
            val, typ = winreg.QueryValueEx(key, vn)
        except OSError:
            continue
        folder = _value_to_folder(val, typ)
        if folder:
            return folder
    return None


def format_registry_save_display(hive_name: str, subkey: str) -> str:
    return f"{hive_name}\\{subkey}"


def try_registry_key_as_in_key_save_location(hint: str) -> Optional[Tuple[str, str]]:
    """
    If ``hint`` opens as a registry *key* whose values hold save state (no folder path),
    return ``(hive_name, subkey)`` for display and ``reg export`` backup.
    """
    hint = normalize_registry_pasted_path(hint)
    hive, remainder = _split_hive_and_remainder(hint)
    if hive is None or not remainder:
        return None
    hive_name = _hive_id_to_name(hive)
    try:
        k = winreg.OpenKey(hive, remainder, 0, winreg.KEY_READ)
    except OSError:
        return None
    try:
        n_values, n_subkeys, _ = winreg.QueryInfoKey(k)
        if n_values < 1 and n_subkeys < 1:
            return None
        return (hive_name, remainder)
    finally:
        try:
            k.Close()
        except Exception:
            pass


def _hive_id_to_name(hk: int) -> str:
    mapping = {
        winreg.HKEY_CURRENT_USER: "HKEY_CURRENT_USER",
        winreg.HKEY_LOCAL_MACHINE: "HKEY_LOCAL_MACHINE",
        winreg.HKEY_USERS: "HKEY_USERS",
        winreg.HKEY_CLASSES_ROOT: "HKEY_CLASSES_ROOT",
    }
    return mapping.get(hk, "HKEY_UNKNOWN")


def resolve_registry_hint_to_save_folder(hint: str) -> Optional[str]:
    """
    Given a line like ``HKEY_CURRENT_USER\\Software\\Vendor\\Game`` or a path that ends
    with a value name under an existing key, return a save *directory* on disk if found.
    """
    hint = normalize_registry_pasted_path(hint)
    hive, remainder = _split_hive_and_remainder(hint)
    if hive is None or not remainder:
        return None
    parts = [p for p in remainder.split("\\") if p]
    if not parts:
        return None

    for depth in range(len(parts), 0, -1):
        key_path = "\\".join(parts[:depth])
        value_segments = parts[depth:]
        try:
            k = winreg.OpenKey(hive, key_path, 0, winreg.KEY_READ)
        except OSError:
            continue
        try:
            if value_segments:
                vn = value_segments[0]
                try:
                    val, typ = winreg.QueryValueEx(k, vn)
                except OSError:
                    pass
                else:
                    folder = _value_to_folder(val, typ)
                    if folder:
                        return folder
            folder = _try_read_values_from_open_key(k)
            if folder:
                return folder
        finally:
            try:
                k.Close()
            except Exception:
                pass
    return None
