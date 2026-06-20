"""
Live log category toggles for the sandbox monitor (QSettings keys only — no UI).

Kept separate from ``ui/sandbox_log_settings_dialog.py`` so ``MainWindow`` can read
preferences without importing the sandbox dialog module (helps PyInstaller retail excludes).
"""

from __future__ import annotations

from PyQt6.QtCore import QSettings

from config.app_config import settings_app_name

SETTINGS_GROUP = "SandboxMonitor"


def log_setting_key(name: str) -> str:
    return f"{SETTINGS_GROUP}/log/{name}"


DEFAULTS: dict[str, bool] = {
    "show_sandbox": True,
    "show_scan": True,
    "show_compress_start": True,
    "show_compress_tick": True,
    "show_compress_summary": True,
    "show_compress_exit": True,
    "show_info": True,
    "show_warn": True,
    "show_marker": True,
    "show_compress_hw_inline": True,
    "show_compress_tick_notes": False,
}


def read_log_setting(settings: QSettings, key: str) -> bool:
    qk = log_setting_key(key)
    return settings.value(qk, DEFAULTS.get(key, True), type=bool)
