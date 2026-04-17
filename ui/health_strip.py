"""Compact status row: backup folder, 7-Zip, free disk space."""
from __future__ import annotations

import os
import shutil
import tempfile
from typing import TYPE_CHECKING

from PyQt6.QtCore import Qt
from PyQt6.QtWidgets import QHBoxLayout, QLabel, QWidget

from core.compression import PRESET_SEVEN_ZIP, options_from_qsettings

if TYPE_CHECKING:
    from ui.main_window import MainWindow


class HealthStrip(QWidget):
    """One-line health indicators; call ``refresh()`` after settings or periodically."""

    def __init__(self, main_window: "MainWindow") -> None:
        super().__init__(main_window)
        self.setObjectName("healthStrip")
        self._mw = main_window
        lay = QHBoxLayout(self)
        lay.setContentsMargins(0, 4, 0, 2)
        lay.setSpacing(12)
        self._lbl_backup = QLabel()
        self._lbl_7z = QLabel()
        self._lbl_disk = QLabel()
        for w in (self._lbl_backup, self._lbl_7z, self._lbl_disk):
            w.setTextInteractionFlags(Qt.TextInteractionFlag.TextSelectableByMouse)
            w.setStyleSheet("font-size: 11px;")
            lay.addWidget(w)
        lay.addStretch(1)

    def refresh(self) -> None:
        settings = self._mw.settings
        backup = (settings.value("default_backup_path", "", type=str) or "").strip()
        if not backup:
            backup = (settings.value("last_backup_path", "", type=str) or "").strip()

        # --- Backup folder ---
        if not backup:
            self._lbl_backup.setText("Backup folder: not set")
            self._lbl_backup.setStyleSheet("font-size: 11px; color: #c9a227;")
        elif not os.path.isdir(backup):
            self._lbl_backup.setText("Backup folder: path missing")
            self._lbl_backup.setStyleSheet("font-size: 11px; color: #e57373;")
        else:
            ok_write = False
            try:
                fd, tmp = tempfile.mkstemp(prefix="gsbt_write_test_", dir=backup)
                os.close(fd)
                os.remove(tmp)
                ok_write = True
            except OSError:
                pass
            if ok_write:
                self._lbl_backup.setText("Backup folder: OK (writable)")
                self._lbl_backup.setStyleSheet("font-size: 11px; color: #8fdf9a;")
            else:
                self._lbl_backup.setText("Backup folder: not writable")
                self._lbl_backup.setStyleSheet("font-size: 11px; color: #e57373;")
            free_root = backup

        # --- 7-Zip (only when preset uses it) ---
        preset = settings.value("compression_preset", "", type=str)
        if preset == PRESET_SEVEN_ZIP:
            opts = options_from_qsettings(settings)
            if opts.seven_zip_exe:
                self._lbl_7z.setText("7-Zip: found")
                self._lbl_7z.setStyleSheet("font-size: 11px; color: #8fdf9a;")
            else:
                self._lbl_7z.setText("7-Zip: not found (set path in Settings)")
                self._lbl_7z.setStyleSheet("font-size: 11px; color: #e57373;")
        else:
            self._lbl_7z.setText("7-Zip: not required (built-in ZIP)")
            self._lbl_7z.setStyleSheet("font-size: 11px; color: #b0b0b0;")

        # --- Free space on volume containing backup folder (or user profile) ---
        try:
            if backup and os.path.isdir(backup):
                root = os.path.abspath(backup)
            else:
                root = os.path.abspath(os.path.expanduser("~"))
            usage = shutil.disk_usage(root)
            free_gib = usage.free / (1024**3)
            self._lbl_disk.setText(f"Free space (that drive): {free_gib:.1f} GiB")
            self._lbl_disk.setStyleSheet("font-size: 11px; color: #cccccc;")
        except OSError:
            self._lbl_disk.setText("Free space: unknown")
            self._lbl_disk.setStyleSheet("font-size: 11px; color: #b0b0b0;")
