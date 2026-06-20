"""
Download and silently install a pinned 7-Zip build for Windows.

Uses an official https://www.7-zip.org/a/ URL (fixed version) so links stay stable;
the in-app notice explains this may not match the newest release on the website.
"""

from __future__ import annotations

import ctypes
import os
import platform
import subprocess
import time
from ctypes import wintypes
from typing import Callable, Optional, Tuple

import requests

# Pinned release (still hosted on 7-zip.org; newer builds often move to GitHub).
PINNED_7ZIP_DISPLAY_VERSION = "23.01"
PINNED_7ZIP_BUILD = "7z2301"

DEFAULT_INSTALL_DIR = os.path.join(os.environ.get("ProgramFiles", r"C:\Program Files"), "7-Zip")
DEFAULT_7Z_EXE = os.path.join(DEFAULT_INSTALL_DIR, "7z.exe")

CancelCheck = Callable[[], bool]


def pinned_installer_url_and_name() -> Tuple[str, str]:
    """Return (https URL, local filename) for this machine's architecture."""
    m = platform.machine().lower()
    if m in ("amd64", "x86_64"):
        return f"https://www.7-zip.org/a/{PINNED_7ZIP_BUILD}-x64.exe", f"{PINNED_7ZIP_BUILD}-x64.exe"
    if m in ("arm64", "aarch64"):
        return f"https://www.7-zip.org/a/{PINNED_7ZIP_BUILD}-arm64.exe", f"{PINNED_7ZIP_BUILD}-arm64.exe"
    return f"https://www.7-zip.org/a/{PINNED_7ZIP_BUILD}.exe", f"{PINNED_7ZIP_BUILD}.exe"


def consent_summary_text() -> str:
    return (
        f"This will download 7-Zip {PINNED_7ZIP_DISPLAY_VERSION} ({PINNED_7ZIP_BUILD}) from the official "
        "7-zip.org site — a fixed version the app can rely on, which may be older than the newest release.\n\n"
        "The installer will run silently into the default folder (usually "
        f'"{DEFAULT_INSTALL_DIR}"). Windows may show one User Account Control (UAC) prompt '
        "for administrator approval.\n\n"
        "After installation, the 7-Zip folder can be added to your user PATH so "
        "`7z` works in new terminal windows. This app detects 7-Zip in Program Files even without PATH.\n\n"
        "Requires an internet connection. Continue?"
    )


def download_installer(
    dest_path: str,
    progress_cb: Optional[Callable[[int, int], None]],
    is_cancelled: CancelCheck,
    chunk_size: int = 256 * 1024,
    timeout: Tuple[float, float] = (30.0, 300.0),
) -> None:
    url, _ = pinned_installer_url_and_name()
    with requests.get(url, stream=True, timeout=timeout) as r:
        r.raise_for_status()
        total = int(r.headers.get("Content-Length") or 0)
        done = 0
        with open(dest_path, "wb") as f:
            for chunk in r.iter_content(chunk_size=chunk_size):
                if is_cancelled():
                    raise InterruptedError("cancelled")
                if not chunk:
                    continue
                f.write(chunk)
                done += len(chunk)
                if progress_cb and total > 0:
                    progress_cb(done, total)
        if progress_cb and total > 0:
            progress_cb(total, total)


# CreateProcess fails with this when the EXE's manifest requires admin (no UAC yet).
ERROR_ELEVATION_REQUIRED = 740


def _run_installer_normal(installer_path: str, install_dir: str) -> int:
    """Run installer as a normal child. May raise OSError or return non-zero if install failed."""
    # Some sessions are already elevated — avoids an extra UAC when not needed.
    try:
        r = subprocess.run(
            [installer_path, "/S", f"/D={install_dir}"],
            capture_output=True,
            timeout=600,
            creationflags=subprocess.CREATE_NO_WINDOW if hasattr(subprocess, "CREATE_NO_WINDOW") else 0,
        )
        return int(r.returncode)
    except OSError as e:
        # Windows refuses to start the process until UAC — subprocess never spawns the child.
        if getattr(e, "winerror", None) == ERROR_ELEVATION_REQUIRED:
            return ERROR_ELEVATION_REQUIRED
        raise


def _run_installer_elevated(installer_path: str, install_dir: str) -> int:
    """ShellExecuteEx with runas; waits for child. Shows UAC if required."""
    install_dir = install_dir.rstrip("\\")
    params = f'/S /D="{install_dir}"'

    class SHELLEXECUTEINFO(ctypes.Structure):
        _fields_ = [
            ("cbSize", wintypes.DWORD),
            ("fMask", wintypes.ULONG),
            ("hwnd", wintypes.HWND),
            ("lpVerb", wintypes.LPCWSTR),
            ("lpFile", wintypes.LPCWSTR),
            ("lpParameters", wintypes.LPCWSTR),
            ("lpDirectory", wintypes.LPCWSTR),
            ("nShow", ctypes.c_int),
            ("hInstApp", wintypes.HINSTANCE),
            ("lpIDList", ctypes.c_void_p),
            ("lpClass", wintypes.LPCWSTR),
            ("hKeyClass", wintypes.HANDLE),
            ("dwHotKey", wintypes.DWORD),
            ("hIcon", wintypes.HANDLE),
            ("hProcess", wintypes.HANDLE),
        ]

    SEE_MASK_NOCLOSEPROCESS = 0x00000040
    INFINITE = 0xFFFFFFFF
    SW_HIDE = 0

    shell32 = ctypes.windll.shell32
    kernel32 = ctypes.windll.kernel32

    sei = SHELLEXECUTEINFO()
    sei.cbSize = ctypes.sizeof(SHELLEXECUTEINFO)
    sei.fMask = SEE_MASK_NOCLOSEPROCESS
    sei.hwnd = None
    sei.lpVerb = "runas"
    sei.lpFile = installer_path
    sei.lpParameters = params
    sei.lpDirectory = None
    sei.nShow = SW_HIDE
    sei.hInstApp = None
    sei.lpIDList = None
    sei.lpClass = None
    sei.hKeyClass = None
    sei.dwHotKey = 0
    sei.hIcon = None
    sei.hProcess = None

    if not shell32.ShellExecuteExW(ctypes.byref(sei)):
        raise ctypes.WinError()

    if not sei.hProcess:
        return -1

    try:
        kernel32.WaitForSingleObject(sei.hProcess, INFINITE)
        code = wintypes.DWORD()
        if not kernel32.GetExitCodeProcess(sei.hProcess, ctypes.byref(code)):
            return -1
        return int(code.value)
    finally:
        kernel32.CloseHandle(sei.hProcess)


def install_silent(installer_path: str, install_dir: str = DEFAULT_INSTALL_DIR) -> Tuple[int, str]:
    """
    Run the official installer silently. Returns (exit_code, note).
    Tries a normal child process first, then ShellExecuteEx ``runas`` (UAC) if the process
    cannot be created (WinError 740) or the installer exits non-zero.
    """
    install_dir = os.path.normpath(install_dir)

    code = _run_installer_normal(installer_path, install_dir)
    if code == 0:
        return 0, "standard"
    # 740 = could not launch without elevation; other non-zero = install failed, try elevated once
    code = _run_installer_elevated(installer_path, install_dir)
    return code, "elevated"


def wait_for_7z_exe(max_wait_sec: float = 90.0, poll: float = 0.35) -> Optional[str]:
    """After install, wait until 7z.exe appears (installer may still be finishing)."""
    deadline = time.monotonic() + max_wait_sec
    while time.monotonic() < deadline:
        if os.path.isfile(DEFAULT_7Z_EXE):
            return DEFAULT_7Z_EXE
        time.sleep(poll)
    return None


def append_user_path_entry(directory: str) -> None:
    """Idempotently append ``directory`` to the current user's PATH (new terminals)."""
    try:
        import winreg
    except ImportError:
        return

    directory = os.path.normcase(os.path.normpath(directory))
    key = winreg.OpenKey(winreg.HKEY_CURRENT_USER, r"Environment", 0, winreg.KEY_READ | winreg.KEY_WRITE)
    try:
        try:
            raw, typ = winreg.QueryValueEx(key, "Path")
        except FileNotFoundError:
            raw, typ = "", winreg.REG_EXPAND_SZ
        if not isinstance(raw, str):
            raw = str(raw)
        parts = [p.strip() for p in raw.split(os.pathsep) if p.strip()]
        norm_parts = [os.path.normcase(os.path.normpath(p)) for p in parts]
        if directory not in norm_parts:
            new_val = (raw.rstrip(os.pathsep) + os.pathsep + directory) if raw.strip() else directory
            winreg.SetValueEx(key, "Path", 0, typ if typ in (winreg.REG_SZ, winreg.REG_EXPAND_SZ) else winreg.REG_EXPAND_SZ, new_val)
            _broadcast_setting_change()
    finally:
        winreg.CloseKey(key)


def _broadcast_setting_change() -> None:
    try:
        HWND_BROADCAST = 0xFFFF
        WM_SETTINGCHANGE = 0x001A
        SMTO_ABORTIFHUNG = 0x0002
        ctypes.windll.user32.SendMessageTimeoutW(
            HWND_BROADCAST,
            WM_SETTINGCHANGE,
            0,
            "Environment",
            SMTO_ABORTIFHUNG,
            2000,
            None,
        )
    except OSError:
        pass
