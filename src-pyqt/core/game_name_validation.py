"""Validate and sanitize game titles used as Windows path segments."""

from __future__ import annotations

import os
from typing import Iterable, List, Optional, Tuple

INVALID_CHARS_MSG = r'\ / : * ? " < > |'


def contains_invalid_filename_chars(name: str) -> bool:
    if not name:
        return False
    invalid = set('<>:"/\\|?*')
    return any(c in invalid or ord(c) < 32 for c in name)


def is_valid_game_name_for_storage(name: str) -> Tuple[bool, Optional[str]]:
    s = (name or "").strip()
    if not s:
        return False, "Enter a game name."
    if contains_invalid_filename_chars(s):
        return False, f"A game name cannot include these characters: {INVALID_CHARS_MSG}"
    if s != s.rstrip(" .") or s.endswith("."):
        return False, "Remove trailing spaces or dots from the name."
    return True, None


def sanitize_for_windows_path_segment(game_name: str) -> str:
    if not game_name or not str(game_name).strip():
        return "Game"
    invalid = set('<>:"/\\|?*')
    cleaned = "".join(c for c in game_name if c not in invalid and ord(c) >= 32)
    cleaned = cleaned.rstrip(" .")
    return cleaned if cleaned.strip() else "Game"


def get_sanitized_folder_collision_messages(game_names: Iterable[str]) -> List[str]:
    names = list(dict.fromkeys(n for n in game_names if n and str(n).strip()))
    messages: List[str] = []
    reported_safe: set[str] = set()
    for name in names:
        safe = sanitize_for_windows_path_segment(name).lower()
        if safe in reported_safe:
            continue
        reported_safe.add(safe)
        for other in names:
            if other.lower() == name.lower():
                continue
            if sanitize_for_windows_path_segment(other).lower() == safe:
                messages.append(
                    f'"{name}" and "{other}" share the same backup folder name ("{safe}"). '
                    "Rename one game to avoid retention deleting the wrong backups."
                )
                break
    return messages
