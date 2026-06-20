"""Check whether GSBT retention backup folders exist under the configured backup root."""

from __future__ import annotations

import os
import re
from typing import Iterable, List, Optional

from core.game_name_validation import sanitize_for_windows_path_segment


def retention_run_contains_at_least_one_file(retention_run_directory: str) -> bool:
    if not retention_run_directory or not os.path.isdir(retention_run_directory):
        return False
    try:
        for _root, _dirs, files in os.walk(retention_run_directory):
            if files:
                return True
    except OSError:
        return False
    return False


def _enumerate_nonempty_retention_run_dirs(base_dir: str, safe: str) -> Iterable[str]:
    prefix = f"{safe} - Backup"
    if not os.path.isdir(base_dir):
        return
    try:
        for name in os.listdir(base_dir):
            full = os.path.join(base_dir, name)
            if not os.path.isdir(full):
                continue
            if not name.startswith(prefix):
                continue
            if retention_run_contains_at_least_one_file(full):
                yield full
    except OSError:
        return


def _enumerate_legacy_flat_reg_exports(base_dir: str, safe: str) -> Iterable[str]:
    prefix = f"{safe} - Backup"
    if not os.path.isdir(base_dir):
        return
    try:
        for name in os.listdir(base_dir):
            if not name.lower().endswith(".reg"):
                continue
            if name.startswith(prefix):
                yield os.path.join(base_dir, name)
    except OSError:
        return


def has_retention_artifact(backup_root: str, game_name: str, subfolder_per_game: bool) -> bool:
    if not backup_root or not os.path.isdir(backup_root):
        return False
    safe = sanitize_for_windows_path_segment(game_name)
    base_dir = os.path.join(backup_root, safe) if subfolder_per_game else backup_root
    if not os.path.isdir(base_dir):
        return False
    for _ in _enumerate_nonempty_retention_run_dirs(base_dir, safe):
        return True
    for _ in _enumerate_legacy_flat_reg_exports(base_dir, safe):
        return True
    return False


def try_get_latest_retention_run_directory(
    backup_root: str, game_name: str, subfolder_per_game: bool
) -> Optional[str]:
    if not backup_root or not os.path.isdir(backup_root):
        return None
    safe = sanitize_for_windows_path_segment(game_name)
    base_dir = os.path.join(backup_root, safe) if subfolder_per_game else backup_root
    if not os.path.isdir(base_dir):
        return None
    best: Optional[str] = None
    best_mtime = 0.0
    for d in _enumerate_nonempty_retention_run_dirs(base_dir, safe):
        try:
            m = os.path.getmtime(d)
        except OSError:
            continue
        if m > best_mtime:
            best_mtime = m
            best = d
    return best


def try_get_open_game_backups_folder_path(
    backup_root: str, game_name: str, subfolder_per_game: bool
) -> tuple[bool, Optional[str], Optional[str]]:
    """Return (ok, path, hint_message). Creates game subfolder when subfolder_per_game."""
    if not backup_root:
        return False, None, "No backup destination configured."
    safe = sanitize_for_windows_path_segment(game_name)
    if subfolder_per_game:
        path = os.path.join(backup_root, safe)
        try:
            os.makedirs(path, exist_ok=True)
        except OSError as exc:
            return False, None, str(exc)
        hint = None if os.path.isdir(path) else "Created backup folder for this game."
        return True, path, hint
    if not os.path.isdir(backup_root):
        try:
            os.makedirs(backup_root, exist_ok=True)
        except OSError as exc:
            return False, None, str(exc)
    return True, backup_root, None


def get_compress_candidates(
    game_names: List[str],
    backup_root: str,
    subfolder_per_game: bool,
    selected_names: Optional[List[str]] = None,
) -> List[str]:
    pool = selected_names if selected_names else game_names
    return [
        n
        for n in pool
        if n and has_retention_artifact(backup_root, n, subfolder_per_game)
    ]


def sanitized_game_names_for_filter(game_names: Iterable[str]) -> List[str]:
    return [sanitize_for_windows_path_segment(n) for n in game_names if n]
