"""Shim — logic lives in core.backup_folder_size_estimator."""

from __future__ import annotations

from typing import Any, Dict, List

from core.backup_folder_size_estimator import (
    BackupSizeEstimateSummary,
    compute_backup_estimate,
    compute_directory_metrics,
    format_byte_size,
)

__all__ = [
    "BackupSizeEstimateSummary",
    "compute_backup_estimate",
    "compute_directory_metrics",
    "estimate_backup_batch",
    "format_byte_size",
]


def estimate_backup_batch(games: List[Dict[str, Any]], destination: str = "") -> Dict[str, Any]:
    """Legacy dict shape for callers that expect the old estimate format."""
    summary = compute_backup_estimate(games, destination or "")
    lines: List[str] = []
    per_game: List[Dict[str, Any]] = []
    for e in summary.entries:
        if e.is_registry_only:
            lines.append(f"{e.game_name}: Windows registry export (small .reg file)")
            per_game.append({"name": e.game_name, "kind": "registry"})
        elif not e.save_folder_path:
            lines.append(f"{e.game_name}: no folder on disk (skipped)")
            per_game.append({"name": e.game_name, "kind": "missing"})
        else:
            lines.append(f"{e.game_name}: {e.file_count:,} files, {format_byte_size(e.bytes_count)}")
            per_game.append(
                {
                    "name": e.game_name,
                    "kind": "disk",
                    "files": e.file_count,
                    "bytes": e.bytes_count,
                    "size_fmt": format_byte_size(e.bytes_count),
                    "severity": e.severity.value,
                    "save_folder_path": e.save_folder_path,
                }
            )
    return {
        "total_files": summary.total_files,
        "total_bytes": summary.total_bytes,
        "registry_games": summary.registry_only_count,
        "disk_games": summary.save_folders_on_disk,
        "lines": lines,
        "per_game": per_game,
        "summary": summary,
    }
