"""First-run QSettings defaults (PyQt + future TUI)."""

from __future__ import annotations

from PyQt6.QtCore import QSettings

# When a key is absent from disk, these values apply.
BOOL_DEFAULTS: dict[str, bool] = {
    # Backup tab
    "auto_backup_enabled": False,
    "show_backup_estimate": True,
    "ask_compress_on_exit": False,
    "backup_subfolder_per_game": True,
    "skip_not_found_games": True,
    # Scan / catalog
    "show_duplicate_save_titles": False,
    # System tab
    "notifications_enabled": False,
    "notification_sound_enabled": False,
    "minimize_to_tray": False,
    "main_window_lock_resolution": False,
}

STRING_DEFAULTS: dict[str, str] = {
    "main_window_client_preset": "800x600",
}


def settings_bool(settings: QSettings, key: str, *, default: bool | None = None) -> bool:
    """Read a bool setting using ``BOOL_DEFAULTS`` when ``default`` is not passed."""
    fallback = BOOL_DEFAULTS[key] if default is None and key in BOOL_DEFAULTS else (default if default is not None else False)
    return settings.value(key, fallback, type=bool)


def settings_str(settings: QSettings, key: str, *, default: str | None = None) -> str:
    fallback = STRING_DEFAULTS[key] if default is None and key in STRING_DEFAULTS else (default if default is not None else "")
    return str(settings.value(key, fallback, type=str) or fallback)
