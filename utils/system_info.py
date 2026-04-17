"""
Windows system information for theming (accent color from DWM).

DWM ``AccentColor`` is stored as a DWORD matching the Win32 COLORREF layout from ``RGB()``:
red in the least significant byte, then green, then blue (bytes 0–2). Older code often treated
it like ``0xAABBGGRR`` and shifted incorrectly; we use the low 24 bits as R, G, B.
"""

from __future__ import annotations

import winreg
from typing import Tuple

# Fallback when registry read fails (Windows blue)
_DEFAULT_ACCENT: Tuple[int, int, int] = (0, 120, 212)


def get_windows_accent_rgb() -> Tuple[int, int, int]:
    """Return ``(r, g, b)`` for the current user accent color (0–255 each)."""
    try:
        key = winreg.OpenKey(
            winreg.HKEY_CURRENT_USER,
            r"Software\Microsoft\Windows\DWM",
            0,
            winreg.KEY_READ,
        )
        try:
            dword, _ = winreg.QueryValueEx(key, "AccentColor")
        finally:
            winreg.CloseKey(key)
    except OSError as e:
        print(f"Could not read Windows accent color: {e}")
        return _DEFAULT_ACCENT

    # Low 24 bits: R, G, B (COLORREF / RGB macro order)
    r = dword & 0xFF
    g = (dword >> 8) & 0xFF
    b = (dword >> 16) & 0xFF
    return (r, g, b)


def get_windows_accent_hex() -> str:
    """``#RRGGBB`` for QSS and CSS."""
    r, g, b = get_windows_accent_rgb()
    return f"#{r:02X}{g:02X}{b:02X}"
