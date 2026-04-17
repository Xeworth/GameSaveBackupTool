"""
Optional system metrics for the sandbox monitor. Uses ``psutil`` when installed.
"""

from __future__ import annotations

import os
from typing import Any, Dict

try:
    import psutil

    _PSUTIL = True
except ImportError:
    _PSUTIL = False


def psutil_available() -> bool:
    return _PSUTIL


def snapshot() -> Dict[str, Any]:
    """
    Return a dict safe to show in the UI. Keys are always present; values may be None if psutil is missing.
    """
    out: Dict[str, Any] = {
        "psutil": _PSUTIL,
        "cpu_percent": None,
        "cpu_per_core": None,
        "ram_percent": None,
        "ram_used_gb": None,
        "ram_total_gb": None,
        "process_rss_mb": None,
        "process_cpu_percent": None,
        "logical_cpus": os.cpu_count() or 0,
    }
    if not _PSUTIL:
        return out
    try:
        out["cpu_percent"] = psutil.cpu_percent(interval=None)
        out["cpu_per_core"] = psutil.cpu_percent(interval=None, percpu=True)
        vm = psutil.virtual_memory()
        out["ram_percent"] = vm.percent
        out["ram_used_gb"] = round(vm.used / (1024**3), 2)
        out["ram_total_gb"] = round(vm.total / (1024**3), 2)
        proc = psutil.Process()
        out["process_rss_mb"] = round(proc.memory_info().rss / (1024**2), 1)
        out["process_cpu_percent"] = proc.cpu_percent(interval=None)
    except Exception:
        pass
    return out


def format_snapshot_line(s: Dict[str, Any]) -> str:
    """One-line summary for the status header."""
    if not s.get("psutil"):
        return (
            "psutil not installed — install with: pip install psutil  "
            f"(logical CPUs: {s.get('logical_cpus', 0)})"
        )
    cpu = s.get("cpu_percent")
    ram = s.get("ram_percent")
    prss = s.get("process_rss_mb")
    ppc = s.get("process_cpu_percent")
    parts = []
    if cpu is not None:
        parts.append(f"CPU (system): {cpu:.1f}%")
    per = s.get("cpu_per_core")
    if per and isinstance(per, list):
        mn, mx = min(per), max(per)
        parts.append(f"cores min/max: {mn:.0f}% / {mx:.0f}%")
    if ram is not None:
        parts.append(
            f"RAM: {ram:.1f}% ({s.get('ram_used_gb')}/{s.get('ram_total_gb')} GiB)"
        )
    if prss is not None:
        parts.append(f"this app RSS: {prss} MiB")
    if ppc is not None:
        parts.append(f"this app CPU: {ppc:.1f}%")
    return "  |  ".join(parts) if parts else "metrics unavailable"
