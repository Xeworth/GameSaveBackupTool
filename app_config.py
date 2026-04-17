"""
App configuration. Used for sandbox mode so settings are isolated when testing.
"""
import os

# When GSBT_SANDBOX=1, the app uses a separate QSettings scope (fresh settings every run).
SANDBOX = os.environ.get("GSBT_SANDBOX") == "1"


def settings_app_name():
    """Application name used for QSettings. Use sandbox name when testing."""
    return "GameSaveBackupTool_Sandbox" if SANDBOX else "GameSaveBackupTool"


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
