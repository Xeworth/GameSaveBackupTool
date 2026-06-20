"""
App configuration. Used for sandbox mode so settings are isolated when testing.
"""
import os

from PyQt6.QtCore import QSettings

# When GSBT_SANDBOX=1, the app uses a separate QSettings scope (fresh settings every run).
SANDBOX = os.environ.get("GSBT_SANDBOX") == "1"

_SETTINGS_ORG = "settings"
_QSETTINGS_INITIALIZED = False


def settings_app_name():
    """Application name used for QSettings. Use sandbox name when testing."""
    return "GameSaveBackupTool_Sandbox" if SANDBOX else "GameSaveBackupTool"


def init_qsettings_storage() -> None:
    """Store QSettings as INI files under ``%AppData%/Roaming/GSBT/pyqt/settings``."""
    global _QSETTINGS_INITIALIZED
    if _QSETTINGS_INITIALIZED:
        return

    from core.user_data_dir import get_pyqt_user_data_dir

    data_dir = get_pyqt_user_data_dir()
    QSettings.setDefaultFormat(QSettings.Format.IniFormat)
    QSettings.setPath(QSettings.Format.IniFormat, QSettings.Scope.UserScope, data_dir)
    _QSETTINGS_INITIALIZED = True


def app_settings() -> QSettings:
    """Shared QSettings instance scope for the PyQt app."""
    init_qsettings_storage()
    return QSettings(
        QSettings.Format.IniFormat,
        QSettings.Scope.UserScope,
        _SETTINGS_ORG,
        settings_app_name(),
    )


# Persisted ``ui_theme`` values (QSettings): default | light | system.
DEFAULT_UI_THEME = "system"

_UI_THEME_ALIASES = {
    "dark": "default",
    "modern": "default",
    "modern_dark": "default",
    "auto": "system",
    "follow_system": "system",
}


def normalize_ui_theme(raw: str | None) -> str:
    """Return a known theme id; unknown values map to ``default``."""
    v = (raw or "default").strip().lower()
    v = _UI_THEME_ALIASES.get(v, v)
    if v in ("default", "light", "system"):
        return v
    return "default"
