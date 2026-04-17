"""Walk on-disk save folders to estimate file count and size before backup."""
from __future__ import annotations

import os
from typing import Any, Dict, List


def _walk_dir_stats(root: str) -> tuple[int, int]:
    """Return (file_count, total_bytes) for files under ``root`` (best-effort)."""
    n_files = 0
    n_bytes = 0
    if not root or not os.path.isdir(root):
        return 0, 0
    try:
        for dirpath, _dirnames, filenames in os.walk(root, followlinks=False):
            for fn in filenames:
                fp = os.path.join(dirpath, fn)
                try:
                    n_bytes += os.path.getsize(fp)
                    n_files += 1
                except OSError:
                    continue
    except OSError:
        return n_files, n_bytes
    return n_files, n_bytes


def format_byte_size(n: int) -> str:
    if n < 1024:
        return f"{n} B"
    for unit, div in (("KiB", 1024), ("MiB", 1024**2), ("GiB", 1024**3), ("TiB", 1024**4)):
        v = n / div
        if v < 1024.0 or unit == "TiB":
            return f"{v:.1f} {unit}"
    return f"{n} B"


def estimate_backup_batch(games: List[Dict[str, Any]]) -> Dict[str, Any]:
    """
    Summarize what a backup would copy for the given table ``game_info`` dicts.

    Registry-only games count as one small export each (size not walked).
    """
    total_files = 0
    total_bytes = 0
    registry_games = 0
    disk_games = 0
    lines: List[str] = []

    for g in games:
        name = g.get("name") or "?"
        if g.get("save_in_registry_only"):
            registry_games += 1
            lines.append(f"{name}: Windows registry export (small .reg file)")
            continue
        p = g.get("save_path_resolved")
        if not p or not os.path.isdir(p):
            lines.append(f"{name}: no folder on disk (skipped)")
            continue
        nf, nb = _walk_dir_stats(p)
        disk_games += 1
        total_files += nf
        total_bytes += nb
        lines.append(f"{name}: {nf:,} files, {format_byte_size(nb)}")

    return {
        "total_files": total_files,
        "total_bytes": total_bytes,
        "registry_games": registry_games,
        "disk_games": disk_games,
        "lines": lines,
    }


def estimate_summary_text(est: Dict[str, Any], game_count: int) -> str:
    parts = [
        f"Games in this backup: {game_count}",
        f"Folders to copy from disk: {est['disk_games']}",
        f"Approx. files to copy: {est['total_files']:,}",
        f"Approx. total size: {format_byte_size(est['total_bytes'])}",
    ]
    if est["registry_games"]:
        parts.append(f"Registry-only saves: {est['registry_games']} (each exports one .reg; negligible size)")
    parts.append("")
    parts.append("Per game:")
    parts.extend(est["lines"])
    return "\n".join(parts)
