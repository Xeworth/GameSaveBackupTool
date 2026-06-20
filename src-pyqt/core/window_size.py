"""Main window size presets and helpers (WinUI WindowSizeHelper parity; PyQt + future TUI)."""

from __future__ import annotations

PRESET_800 = "800x600"
PRESET_1024 = "1024x768"
PRESET_CUSTOM = "custom"

FALLBACK_MIN_W_RETAIL = 735
FALLBACK_MIN_W_SANDBOX = 805

NOMINAL_W_800 = 800
NOMINAL_H_600 = 600
NOMINAL_W_1024 = 1024
NOMINAL_H_768 = 768


def normalize_preset(preset: str | None) -> str:
    p = str(preset or "").strip().lower()
    if p in (PRESET_800, PRESET_1024, PRESET_CUSTOM):
        return p
    return PRESET_800


def _clamp_size(w: int, h: int, min_w: int, min_h: int) -> tuple[int, int]:
    return max(min_w, int(w)), max(min_h, int(h))


def resolve_client_size(
    preset: str | None,
    custom_w: int,
    custom_h: int,
    *,
    min_w: int,
    min_h: int,
) -> tuple[int, int]:
    tag = normalize_preset(preset)
    if tag == PRESET_1024:
        return _clamp_size(NOMINAL_W_1024, NOMINAL_H_768, min_w, min_h)
    if tag == PRESET_CUSTOM and custom_w >= min_w and custom_h >= min_h:
        return int(custom_w), int(custom_h)
    return _clamp_size(NOMINAL_W_800, NOMINAL_H_600, min_w, min_h)


def classify_client_size(width: int, height: int, *, epsilon: int = 4) -> str:
    if abs(width - NOMINAL_W_800) <= epsilon and abs(height - NOMINAL_H_600) <= epsilon:
        return PRESET_800
    if abs(width - NOMINAL_W_1024) <= epsilon and abs(height - NOMINAL_H_768) <= epsilon:
        return PRESET_1024
    return PRESET_CUSTOM
