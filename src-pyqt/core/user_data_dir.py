"""Resolve per-user GSBT data directories (%AppData%/Roaming/GSBT on Windows)."""

from __future__ import annotations

import os
import shutil
import sys

_GSBT_APP_NAME = "GSBT"
_PYQT_SUBDIR = "pyqt"


def _platform_user_data_base() -> str:
    if os.name == "nt":
        return os.environ.get("APPDATA") or os.path.expanduser("~")
    if sys.platform == "darwin":  # pragma: no cover
        return os.path.join(os.path.expanduser("~"), "Library", "Application Support")
    return os.environ.get("XDG_DATA_HOME") or os.path.join(os.path.expanduser("~"), ".local", "share")


def get_app_user_data_dir(app_name: str = _GSBT_APP_NAME) -> str:
    """Return (and create) ``%AppData%/GSBT``, migrating legacy folder names if present."""
    base = _platform_user_data_base()
    target = os.path.join(base, app_name)
    os.makedirs(target, exist_ok=True)

    for legacy in ("GSBT_Lite", "GSBT_Light"):
        _migrate_legacy_dir(os.path.join(base, legacy), target)

    return target


def get_pyqt_user_data_dir() -> str:
    """Return (and create) ``%AppData%/Roaming/GSBT/pyqt`` for PyQt user-generated files."""
    gsbt_root = get_app_user_data_dir()
    target = os.path.join(gsbt_root, _PYQT_SUBDIR)
    os.makedirs(target, exist_ok=True)

    # One-time: manifest/cache may have lived directly under GSBT/ before pyqt subfolder existed.
    for name in (
        "ludusavi-save-manifest.json",
        "ludusavi-save-manifest.meta.json",
        "game_save_data.json",
    ):
        _migrate_file_if_missing(os.path.join(gsbt_root, name), os.path.join(target, name))

    return target


def user_data_file(name: str) -> str:
    """Absolute path to a file inside the PyQt user-data folder."""
    return os.path.join(get_pyqt_user_data_dir(), name)


def _migrate_legacy_dir(legacy_dir: str, new_dir: str) -> None:
    if not os.path.isdir(legacy_dir):
        return
    for entry in os.listdir(legacy_dir):
        src = os.path.join(legacy_dir, entry)
        dst = os.path.join(new_dir, entry)
        if os.path.exists(dst):
            continue
        try:
            if os.path.isdir(src):
                shutil.copytree(src, dst)
            else:
                shutil.copy2(src, dst)
        except OSError:
            pass


def _migrate_file_if_missing(src: str, dst: str) -> None:
    if not os.path.isfile(src) or os.path.exists(dst):
        return
    try:
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        shutil.copy2(src, dst)
    except OSError:
        pass
