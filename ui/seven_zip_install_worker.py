"""Background download + silent install of pinned 7-Zip (Windows)."""

from __future__ import annotations

import os
import tempfile
from typing import Optional

import requests
from PyQt6.QtCore import QThread, pyqtSignal

from utils.seven_zip_install import (
    DEFAULT_INSTALL_DIR,
    append_user_path_entry,
    download_installer,
    install_silent,
    wait_for_7z_exe,
)


class SevenZipInstallWorker(QThread):
    progress = pyqtSignal(int, str)
    succeeded = pyqtSignal(str)
    failed = pyqtSignal(str)

    def __init__(self, parent=None):
        super().__init__(parent)
        self._cancel = False
        self._tmp_path: Optional[str] = None

    def request_cancel(self) -> None:
        self._cancel = True

    def run(self) -> None:
        path: Optional[str] = None
        try:
            fd, path = tempfile.mkstemp(suffix=".exe", prefix="gsbt_7z_")
            os.close(fd)
            self._tmp_path = path

            def prog(done: int, total: int) -> None:
                pct = min(99, int(100 * done / total)) if total else 0
                mib = total / (1024 * 1024) if total else 0.0
                self.progress.emit(pct, f"Downloading 7-Zip… ({mib:.1f} MiB total)" if total else "Downloading 7-Zip…")

            download_installer(path, prog, lambda: self._cancel)
            if self._cancel:
                self.failed.emit("Download cancelled.")
                return

            self.progress.emit(99, "Installing silently (you may see one UAC prompt)…")
            code, _note = install_silent(path, DEFAULT_INSTALL_DIR)
            if code != 0:
                self.failed.emit(
                    f"Installer reported exit code {code}. If you declined the permission prompt, try again and approve it."
                )
                return

            exe = wait_for_7z_exe()
            if not exe:
                self.failed.emit(
                    "Install finished but 7z.exe was not found yet. Try closing Settings and using Compress again, "
                    "or install manually from https://www.7-zip.org/"
                )
                return

            try:
                append_user_path_entry(DEFAULT_INSTALL_DIR)
            except OSError:
                pass

            self.progress.emit(100, "Done.")
            self.succeeded.emit(exe)
        except InterruptedError:
            self.failed.emit("Download cancelled.")
        except requests.RequestException as e:
            self.failed.emit(f"Download failed: {e}")
        except OSError as e:
            self.failed.emit(f"Install failed: {e}")
        except Exception as e:
            self.failed.emit(str(e))
        finally:
            if path and os.path.isfile(path):
                try:
                    os.unlink(path)
                except OSError:
                    pass
