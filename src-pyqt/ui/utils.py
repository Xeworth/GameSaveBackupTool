"""Utility helpers for UI components (colors, etc.)."""

from utils.system_info import get_windows_accent_hex, get_windows_accent_rgb


def get_windows_accent_color():
    """Hex ``#RRGGBB`` for the current Windows accent (see ``utils.system_info``)."""
    return get_windows_accent_hex()


__all__ = ["get_windows_accent_color", "get_windows_accent_hex", "get_windows_accent_rgb"]
