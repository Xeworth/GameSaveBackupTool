"""
Append-only session log on disk for sandbox / profiling (no manual copy-paste).

Default file (next to this module): ``gsbt_last_session.log``

Safe to delete anytime. Not used when the app runs without the sandbox monitor.
"""

from __future__ import annotations

import threading
from datetime import datetime
from pathlib import Path

_LOCK = threading.Lock()
LOG_PATH = Path(__file__).resolve().parent / "gsbt_last_session.log"


def log_file_path() -> Path:
    return LOG_PATH


def append_session_line(line: str) -> None:
    """Append one UTF-8 line (newline added if missing). Thread-safe."""
    text = (line or "").rstrip("\r\n") + "\n"
    with _LOCK:
        with open(LOG_PATH, "a", encoding="utf-8", errors="replace") as f:
            f.write(text)


def reset_session_log(title: str = "GSBT sandbox session") -> None:
    """Truncate the log and write a short header (start of a new test run)."""
    ts = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    with _LOCK:
        with open(LOG_PATH, "w", encoding="utf-8", errors="replace") as f:
            f.write(f"=== {title} | {ts} ===\n")
            f.write("Save-fetch trace, per-game rows, and batch markers are mirrored here when enabled.\n\n")
