"""Canonical catalog keys for detected games (C# CatalogGameKeys parity)."""

from __future__ import annotations

import re


def clean_display_name(name: str) -> str:
    s = re.sub(r"[®™©]", "", name or "").strip()
    return re.sub(r"\s+", " ", s)


def catalog_key_from_detected_game(game: dict) -> str:
    return clean_display_name(str(game.get("name") or ""))
