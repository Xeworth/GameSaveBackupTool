"""
Sandbox-only overrides (--sandbox): pretend saved QSettings / disk cache are empty for UI testing.

Stored under the same QSettings scope as the rest of sandbox (GameSaveBackupTool_Sandbox).
"""

from __future__ import annotations

from PyQt6.QtCore import QSettings

from config.app_config import SANDBOX, settings_app_name

K_IGNORE_BACKUP_PATHS = "sandbox_default_ignore_saved_backup_paths"
K_IGNORE_GAME_CACHE = "sandbox_default_ignore_cached_game_list"
K_7ZIP_UI = "sandbox_default_7zip_ui_override"  # auto | present | absent
K_QUIT_REMEMBER = "sandbox_quit_remember_exit_choice"
K_QUIT_CLOSE_MONITOR = "sandbox_quit_close_monitor_with_main"


def _qs() -> QSettings:
    return QSettings("MyCompany", settings_app_name())


def ignore_saved_backup_paths() -> bool:
    if not SANDBOX:
        return False
    return _qs().value(K_IGNORE_BACKUP_PATHS, False, type=bool)


def ignore_cached_game_list() -> bool:
    if not SANDBOX:
        return False
    return _qs().value(K_IGNORE_GAME_CACHE, False, type=bool)


def seven_zip_ui_override() -> str:
    """How Settings → compression treats 7-Zip detection: auto | present | absent."""
    if not SANDBOX:
        return "auto"
    v = _qs().value(K_7ZIP_UI, "auto", type=str)
    if v in ("auto", "present", "absent"):
        return v
    return "auto"


def effective_default_backup_path_for_settings(settings: QSettings) -> str:
    """Default backup folder line in Settings; empty when sandbox ignores saved paths."""
    if ignore_saved_backup_paths():
        return ""
    return settings.value("default_backup_path", "", type=str)


def set_ignore_saved_backup_paths(value: bool) -> None:
    if not SANDBOX:
        return
    _qs().setValue(K_IGNORE_BACKUP_PATHS, bool(value))


def set_ignore_cached_game_list(value: bool) -> None:
    if not SANDBOX:
        return
    _qs().setValue(K_IGNORE_GAME_CACHE, bool(value))


def set_seven_zip_ui_override(mode: str) -> None:
    if not SANDBOX:
        return
    if mode in ("auto", "present", "absent"):
        _qs().setValue(K_7ZIP_UI, mode)


def quit_exit_remember() -> bool:
    """User chose "Remember my choice" when closing the main app with the monitor still open."""
    if not SANDBOX:
        return False
    return _qs().value(K_QUIT_REMEMBER, False, type=bool)


def quit_exit_close_monitor_with_main() -> bool:
    """When ``quit_exit_remember()`` is True: if True, always close monitor when quitting main."""
    if not SANDBOX:
        return True
    return _qs().value(K_QUIT_CLOSE_MONITOR, True, type=bool)


def set_quit_exit_preference(remember: bool, close_monitor_with_main: bool) -> None:
    if not SANDBOX:
        return
    _qs().setValue(K_QUIT_REMEMBER, bool(remember))
    _qs().setValue(K_QUIT_CLOSE_MONITOR, bool(close_monitor_with_main))


def compression_dialog_7zip_installed_override(have_valid_custom_exe: bool) -> bool | None:
    """
    When not None, Settings compression UI should treat 7-Zip as installed (True) or not (False).
    None means use real find_7zip_executable() (after checking custom path).
    """
    if not SANDBOX or have_valid_custom_exe:
        return None
    mode = seven_zip_ui_override()
    if mode == "auto":
        return None
    return mode == "present"
