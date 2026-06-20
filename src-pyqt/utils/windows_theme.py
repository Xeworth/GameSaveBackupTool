"""Windows light/dark preference for apps (Settings → Personalization → Colors)."""

from __future__ import annotations

import sys
import winreg


def windows_apps_use_light_theme() -> bool:
    """
    Return True when Windows is configured for light-colored apps.

    Reads ``HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize``
    ``AppsUseLightTheme``: 1 = light, 0 = dark. Missing key (older Windows) defaults to light.
    """
    if sys.platform != "win32":
        return True
    try:
        key = winreg.OpenKey(
            winreg.HKEY_CURRENT_USER,
            r"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            0,
            winreg.KEY_READ,
        )
        try:
            val, _ = winreg.QueryValueEx(key, "AppsUseLightTheme")
            return int(val) != 0
        finally:
            winreg.CloseKey(key)
    except OSError:
        return True
