"""Pre-backup folder size walk with severity tiers (mirrors C# BackupFolderSizeEstimator)."""

from __future__ import annotations

import os
from dataclasses import dataclass, field
from enum import Enum
from typing import Any, Dict, List, Optional, Set


class BackupSizeSeverity(str, Enum):
    NORMAL = "normal"
    LARGE = "large"
    SUSPICIOUS = "suspicious"


LARGE_SAVE_THRESHOLD_BYTES = 4 * 1024 * 1024 * 1024
SUSPICIOUS_SAVE_THRESHOLD_BYTES = 8 * 1024 * 1024 * 1024


def classify_size(bytes_count: int) -> BackupSizeSeverity:
    if bytes_count >= SUSPICIOUS_SAVE_THRESHOLD_BYTES:
        return BackupSizeSeverity.SUSPICIOUS
    if bytes_count >= LARGE_SAVE_THRESHOLD_BYTES:
        return BackupSizeSeverity.LARGE
    return BackupSizeSeverity.NORMAL


def compute_directory_metrics(root: str) -> tuple[int, int]:
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
        pass
    return n_bytes, n_files


def format_byte_size(n: int) -> str:
    if n < 1024:
        return f"{n} B"
    for unit, div in (("KiB", 1024), ("MiB", 1024**2), ("GiB", 1024**3), ("TiB", 1024**4)):
        v = n / div
        if v < 1024.0 or unit == "TiB":
            return f"{v:.1f} {unit}"
    return f"{n} B"


@dataclass
class BackupSizeEstimateEntry:
    game_name: str
    bytes_count: int
    file_count: int
    is_registry_only: bool
    severity: BackupSizeSeverity
    save_folder_path: Optional[str] = None


@dataclass
class BackupSizeEstimateSummary:
    total_bytes: int
    total_files: int
    games_in_backup: int
    save_folders_on_disk: int
    registry_only_count: int
    backup_destination_display: str
    entries: List[BackupSizeEstimateEntry] = field(default_factory=list)

    @property
    def has_severity_warnings(self) -> bool:
        return any(
            not e.is_registry_only
            and e.severity in (BackupSizeSeverity.LARGE, BackupSizeSeverity.SUSPICIOUS)
            for e in self.entries
        )


def compute_backup_estimate(
    games: List[Dict[str, Any]],
    destination: str,
    trusted_large_save_paths: Optional[Set[str]] = None,
) -> BackupSizeEstimateSummary:
    trusted = {n.lower() for n in (trusted_large_save_paths or set())}
    entries: List[BackupSizeEstimateEntry] = []
    total_bytes = 0
    total_files = 0
    disk_count = 0
    reg_count = 0

    for g in games:
        name = str(g.get("name") or "?")
        if g.get("save_in_registry_only"):
            reg_count += 1
            entries.append(
                BackupSizeEstimateEntry(
                    game_name=name,
                    bytes_count=0,
                    file_count=0,
                    is_registry_only=True,
                    severity=BackupSizeSeverity.NORMAL,
                    save_folder_path=None,
                )
            )
            continue

        path = g.get("save_path_resolved")
        if not path or not os.path.isdir(str(path)):
            entries.append(
                BackupSizeEstimateEntry(
                    game_name=name,
                    bytes_count=0,
                    file_count=0,
                    is_registry_only=False,
                    severity=BackupSizeSeverity.NORMAL,
                    save_folder_path=None,
                )
            )
            continue

        nb, nf = compute_directory_metrics(str(path))
        disk_count += 1
        total_bytes += nb
        total_files += nf
        severity = (
            BackupSizeSeverity.NORMAL
            if name.lower() in trusted
            else classify_size(nb)
        )
        entries.append(
            BackupSizeEstimateEntry(
                game_name=name,
                bytes_count=nb,
                file_count=nf,
                is_registry_only=False,
                severity=severity,
                save_folder_path=str(path),
            )
        )

    entries.sort(key=lambda e: e.bytes_count, reverse=True)
    return BackupSizeEstimateSummary(
        total_bytes=total_bytes,
        total_files=total_files,
        games_in_backup=len(games),
        save_folders_on_disk=disk_count,
        registry_only_count=reg_count,
        backup_destination_display=destination,
        entries=entries,
    )
