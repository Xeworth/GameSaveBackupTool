"""
Compression presets for backup archives: built-in zipfile (single-thread) vs 7-Zip (.7z LZMA2 or .zip Deflate).

Settings keys:
  compression_preset: store | deflate_fast | deflate_balanced | deflate_max | seven_zip
  compression_7z_format: zip | 7z  (7-Zip preset only: .7z uses LZMA2 + real multithreading)
  compression_7z_level: 0-9 (-mx)
  compression_7z_threads: 0 = auto (-mmt=on), else -mmt=N
  compression_7z_path: optional full path to 7z.exe
"""

from __future__ import annotations

import os
import shutil
import zipfile
from dataclasses import dataclass
from typing import Any, Optional

PRESET_STORE = "store"
PRESET_DEFLATE_FAST = "deflate_fast"
PRESET_DEFLATE_BALANCED = "deflate_balanced"
PRESET_DEFLATE_MAX = "deflate_max"
PRESET_SEVEN_ZIP = "seven_zip"


@dataclass(frozen=True)
class CompressionOptions:
    """Resolved options passed to ``CompressBackupWorker``."""

    engine: str  # "zipfile" | "7z"
    zip_compression: int  # ZIP_STORED or ZIP_DEFLATED
    deflate_level: int  # 1–9 when using DEFLATED; ignored for STORED
    seven_zip_exe: Optional[str]
    seven_archive_format: str  # "zip" | "7z" — only used when engine == "7z"
    seven_mx: int
    seven_mmt: int  # 0 → -mmt=on (7-Zip picks thread count)
    summary_label: str  # for logs / sandbox

    @staticmethod
    def default_zip_balanced() -> "CompressionOptions":
        return CompressionOptions(
            engine="zipfile",
            zip_compression=zipfile.ZIP_DEFLATED,
            deflate_level=6,
            seven_zip_exe=None,
            seven_archive_format="zip",
            seven_mx=9,
            seven_mmt=0,
            summary_label="Built-in ZIP deflate level 6 (single-thread)",
        )


def find_7zip_executable() -> Optional[str]:
    """Return path to 7z.exe if found on PATH or standard install dirs."""
    for name in ("7z.exe", "7z"):
        p = shutil.which(name)
        if p and os.path.isfile(p):
            return p
    for env_key in ("ProgramFiles", "ProgramFiles(x86)"):
        base = os.environ.get(env_key)
        if not base:
            continue
        cand = os.path.join(base, "7-Zip", "7z.exe")
        if os.path.isfile(cand):
            return cand
    # Common explicit path
    cand = r"C:\Program Files\7-Zip\7z.exe"
    if os.path.isfile(cand):
        return cand
    return None


def resolve_7zip_exe(settings: Any) -> Optional[str]:
    """Custom path from settings, else auto-detect."""
    custom = settings.value("compression_7z_path", "", type=str)
    custom = custom.strip().strip('"') if custom else ""
    if custom and os.path.isfile(custom):
        return custom
    return find_7zip_executable()


def options_from_qsettings(settings: Any) -> CompressionOptions:
    preset = settings.value("compression_preset", PRESET_DEFLATE_BALANCED, type=str)
    mx = max(0, min(9, settings.value("compression_7z_level", 5, type=int)))
    mmt = max(0, min(256, settings.value("compression_7z_threads", 0, type=int)))
    seven = resolve_7zip_exe(settings) if preset == PRESET_SEVEN_ZIP else None
    zfmt = settings.value("compression_7z_format", "7z", type=str)
    if zfmt not in ("zip", "7z"):
        zfmt = "7z"

    if preset == PRESET_STORE:
        return CompressionOptions(
            engine="zipfile",
            zip_compression=zipfile.ZIP_STORED,
            deflate_level=0,
            seven_zip_exe=None,
            seven_archive_format="zip",
            seven_mx=mx,
            seven_mmt=mmt,
            summary_label="ZIP store (no compression, minimal CPU)",
        )
    if preset == PRESET_DEFLATE_FAST:
        return CompressionOptions(
            engine="zipfile",
            zip_compression=zipfile.ZIP_DEFLATED,
            deflate_level=1,
            seven_zip_exe=None,
            seven_archive_format="zip",
            seven_mx=mx,
            seven_mmt=mmt,
            summary_label="Built-in ZIP deflate level 1 (single core, fast)",
        )
    if preset == PRESET_DEFLATE_MAX:
        return CompressionOptions(
            engine="zipfile",
            zip_compression=zipfile.ZIP_DEFLATED,
            deflate_level=9,
            seven_zip_exe=None,
            seven_archive_format="zip",
            seven_mx=mx,
            seven_mmt=mmt,
            summary_label="Built-in ZIP deflate level 9 (single core, heavy)",
        )
    if preset == PRESET_SEVEN_ZIP:
        mmt_desc = "auto threads" if mmt <= 0 else f"{mmt} threads"
        if zfmt == "7z":
            summary = f"7-Zip .7z LZMA2 -mx={mx} -mmt={mmt_desc}"
        else:
            summary = (
                f"7-Zip .zip Deflate -mx={mx} -mmt={mmt_desc} "
                "(MT mainly when there are many files)"
            )
        return CompressionOptions(
            engine="7z",
            zip_compression=zipfile.ZIP_DEFLATED,
            deflate_level=6,
            seven_zip_exe=seven,
            seven_archive_format=zfmt,
            seven_mx=mx,
            seven_mmt=mmt,
            summary_label=summary,
        )
    # default balanced
    return CompressionOptions(
        engine="zipfile",
        zip_compression=zipfile.ZIP_DEFLATED,
        deflate_level=6,
        seven_zip_exe=None,
        seven_archive_format="zip",
        seven_mx=mx,
        seven_mmt=mmt,
        summary_label="Built-in ZIP deflate level 6 (single-thread)",
    )
