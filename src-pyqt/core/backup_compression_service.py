"""Backup folder compression with optional per-game filtering (mirrors C# BackupCompressionService)."""

from __future__ import annotations

import os
import re
from typing import Iterable, List, Optional, Set, Tuple


def _is_root_gsbt_backup_archive(relative_entry: str) -> bool:
    rel = relative_entry.replace("\\", "/").lstrip("/")
    base = os.path.basename(rel)
    if not base.lower().startswith("backups_"):
        return False
    lower = base.lower()
    return lower.endswith(".zip") or lower.endswith(".7z")


def _top_level_folder_from_entry(entry_name: str) -> str:
    normalized = entry_name.replace("\\", "/").strip("/")
    if not normalized:
        return ""
    slash = normalized.find("/")
    return normalized[:slash] if slash > 0 else normalized


def _entry_matches_game_filter(
    relative_entry: str,
    subfolder_per_game: bool,
    sanitized_game_names: Set[str],
) -> bool:
    top = _top_level_folder_from_entry(relative_entry)
    if not top:
        return False
    if subfolder_per_game:
        return top.lower() in {n.lower() for n in sanitized_game_names}
    prefix_matches = any(
        top.lower().startswith(f"{safe.lower()} - backup") for safe in sanitized_game_names
    )
    return prefix_matches


def collect_relative_entries(
    root: str,
    *,
    subfolder_per_game: bool = True,
    sanitized_game_names: Optional[Iterable[str]] = None,
) -> Tuple[List[Tuple[str, str]], int, int]:
    """
    Return (list of (full_path, arcname_with_forward_slashes), total_bytes, file_count).
    """
    if not root or not os.path.isdir(root):
        return [], 0, 0

    root = os.path.normpath(root)
    name_filter: Optional[Set[str]] = None
    if sanitized_game_names is not None:
        names = [n for n in sanitized_game_names if n]
        if names:
            name_filter = {n for n in names}

    out: List[Tuple[str, str]] = []
    total_bytes = 0
    for dirpath, dirnames, filenames in os.walk(root):
        dirnames[:] = [d for d in dirnames if d != "__pycache__"]
        for fn in filenames:
            if fn.endswith((".zip", ".7z")) and dirpath == root:
                continue
            path = os.path.join(dirpath, fn)
            rel = os.path.relpath(path, root)
            arc = rel.replace(os.sep, "/")
            if _is_root_gsbt_backup_archive(arc):
                continue
            if name_filter is not None and not _entry_matches_game_filter(
                arc, subfolder_per_game, name_filter
            ):
                continue
            try:
                total_bytes += os.path.getsize(path)
            except OSError:
                pass
            out.append((path, arc))
    out.sort(key=lambda x: x[1].lower())
    return out, total_bytes, len(out)
