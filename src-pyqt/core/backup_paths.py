"""Default backup folder helpers (WinUI MainPage.BackupPrompt parity; shared by PyQt and future TUI)."""

from __future__ import annotations

import os

_SUGGESTED_FOLDER_NAME = "GSBT_Backups"


def suggested_default_backup_path() -> str:
    documents = os.path.join(os.path.expanduser("~"), "Documents")
    return os.path.normpath(os.path.join(documents, _SUGGESTED_FOLDER_NAME))


def ensure_backup_directory(path: str) -> tuple[bool, str]:
    """Normalize path and create the directory if missing. Returns (ok, error_message)."""
    raw = str(path or "").strip()
    if not raw:
        return False, "Choose a folder or enter a valid path."

    try:
        resolved = os.path.normpath(os.path.abspath(os.path.expanduser(raw)))
    except (OSError, ValueError):
        return False, "That path is not valid."

    if os.path.isdir(resolved):
        return True, ""

    try:
        os.makedirs(resolved, exist_ok=True)
    except OSError as exc:
        return False, f"Could not create folder: {exc}"

    if not os.path.isdir(resolved):
        return False, "That folder does not exist or is not reachable."
    return True, ""
