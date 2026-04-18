"""Walk on-disk save folders to estimate file count and size before backup."""
from __future__ import annotations

import html
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
    per_game: List[Dict[str, Any]] = []

    for g in games:
        name = g.get("name") or "?"
        if g.get("save_in_registry_only"):
            registry_games += 1
            lines.append(f"{name}: Windows registry export (small .reg file)")
            per_game.append({"name": name, "kind": "registry"})
            continue
        p = g.get("save_path_resolved")
        if not p or not os.path.isdir(p):
            lines.append(f"{name}: no folder on disk (skipped)")
            per_game.append({"name": name, "kind": "missing"})
            continue
        nf, nb = _walk_dir_stats(p)
        disk_games += 1
        total_files += nf
        total_bytes += nb
        lines.append(f"{name}: {nf:,} files, {format_byte_size(nb)}")
        per_game.append(
            {
                "name": name,
                "kind": "disk",
                "files": nf,
                "bytes": nb,
                "size_fmt": format_byte_size(nb),
            }
        )

    return {
        "total_files": total_files,
        "total_bytes": total_bytes,
        "registry_games": registry_games,
        "disk_games": disk_games,
        "lines": lines,
        "per_game": per_game,
    }


def estimate_summary_html(est: Dict[str, Any], game_count: int, dest: str, *, light_theme: bool) -> str:
    """Rich summary for QTextBrowser (bullets + accent colors for counts and sizes)."""
    dest_esc = html.escape(dest, quote=True)
    acc = "#1565c0" if light_theme else "#90caf9"
    acc2 = "#2e7d32" if light_theme else "#8fdf9a"
    muted = "#616161" if light_theme else "#b0b0b0"
    warn = "#c62828" if light_theme else "#ff8a80"

    def num_span(n: int) -> str:
        return f'<span style="color:{acc}; font-weight:600;">{n:,}</span>'

    def size_span(s: str) -> str:
        return f'<span style="color:{acc2}; font-weight:600;">{html.escape(s)}</span>'

    parts: List[str] = [
        f'<p style="margin:0 0 10px 0;"><b>Destination</b><br/>'
        f'<span style="color:{muted};">{dest_esc}</span></p>',
        "<p style=\"margin:0 0 8px 0;\"><b>Summary</b></p>",
        "<ul style=\"margin:0 0 12px 18px; padding:0;\">",
        f"<li>Games in this backup: {num_span(game_count)}</li>",
        f"<li>Save folders on disk: {num_span(est['disk_games'])}</li>",
        f"<li>Approx. files to copy: {num_span(est['total_files'])}</li>",
        f"<li>Approx. total size: {size_span(format_byte_size(est['total_bytes']))}</li>",
    ]
    if est["registry_games"]:
        parts.append(
            f"<li>Registry-only saves: {num_span(est['registry_games'])} "
            f"<span style=\"color:{muted};\">(small .reg each)</span></li>"
        )
    parts.append("</ul>")
    parts.append(f'<p style="margin:0 0 6px 0; color:{muted};"><b>Per game</b></p><ul style="margin:0 0 0 18px;">')

    for row in est.get("per_game") or []:
        nm = html.escape(str(row.get("name") or "?"), quote=True)
        kind = row.get("kind")
        if kind == "registry":
            parts.append(
                f'<li style="margin:4px 0;"><b>{nm}</b> — '
                f'<span style="color:{muted};">registry export (tiny)</span></li>'
            )
        elif kind == "missing":
            parts.append(
                f'<li style="margin:4px 0;"><b>{nm}</b> — '
                f'<span style="color:{warn};">no folder on disk</span></li>'
            )
        else:
            nf = int(row.get("files") or 0)
            sz = str(row.get("size_fmt") or "")
            parts.append(
                f'<li style="margin:4px 0;"><b>{nm}</b><br/>'
                f'<span style="color:{muted};">Files:</span> {num_span(nf)}'
                f' &nbsp; <span style="color:{muted};">Size:</span> {size_span(sz)}</li>'
            )
    parts.append("</ul>")
    return "\n".join(parts)
