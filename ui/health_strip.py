"""Backup folder, 7-Zip, and free-space indicators (used from Settings dialog)."""
from __future__ import annotations

import os
import shutil
import tempfile

from PyQt6.QtCore import Qt, QSettings
from PyQt6.QtWidgets import QLabel, QVBoxLayout, QWidget

from core.compression import PRESET_SEVEN_ZIP, options_from_qsettings


class HealthStrip(QWidget):
    """Vertical status lines; call ``refresh()`` after opening."""

    def __init__(
        self, parent: QWidget | None, settings: QSettings, *, compact: bool = False
    ) -> None:
        super().__init__(parent)
        self.setObjectName("healthStrip")
        self._settings = settings
        self._compact = compact
        self._font_px = 11 if compact else 12
        vm = 2 if compact else 6
        sp = 4 if compact else 6
        lay = QVBoxLayout(self)
        lay.setContentsMargins(0, vm, 0, max(2, vm - 2))
        lay.setSpacing(sp)
        self._lbl_backup = QLabel()
        self._lbl_7z = QLabel()
        self._lbl_disk = QLabel()
        for w in (self._lbl_backup, self._lbl_7z, self._lbl_disk):
            w.setTextInteractionFlags(Qt.TextInteractionFlag.TextSelectableByMouse)
            w.setWordWrap(True)
            w.setStyleSheet(f"font-size: {self._font_px}px;")
            lay.addWidget(w)
        if not compact:
            lay.addStretch(1)

    def refresh(self) -> None:
        settings = self._settings
        backup = (settings.value("default_backup_path", "", type=str) or "").strip()
        if not backup:
            backup = (settings.value("last_backup_path", "", type=str) or "").strip()

        if not backup:
            self._lbl_backup.setText("Backup folder: not set")
            self._lbl_backup.setStyleSheet(f"font-size: {self._font_px}px; color: #c9a227;")
        elif not os.path.isdir(backup):
            self._lbl_backup.setText("Backup folder: path missing on disk")
            self._lbl_backup.setStyleSheet(f"font-size: {self._font_px}px; color: #e57373;")
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
                self._lbl_backup.setText(f"Backup folder: OK (writable)\n{backup}")
                self._lbl_backup.setStyleSheet(f"font-size: {self._font_px}px; color: #8fdf9a;")
            else:
                self._lbl_backup.setText(f"Backup folder: not writable\n{backup}")
                self._lbl_backup.setStyleSheet(f"font-size: {self._font_px}px; color: #e57373;")

        preset = settings.value("compression_preset", "", type=str)
        if preset == PRESET_SEVEN_ZIP:
            opts = options_from_qsettings(settings)
            if opts.seven_zip_exe:
                self._lbl_7z.setText(f"7-Zip: found\n{opts.seven_zip_exe}")
                self._lbl_7z.setStyleSheet(f"font-size: {self._font_px}px; color: #8fdf9a;")
            else:
                self._lbl_7z.setText(
                    "7-Zip: not found — install 7-Zip or set a path under Settings → Compress backups."
                )
                self._lbl_7z.setStyleSheet(f"font-size: {self._font_px}px; color: #e57373;")
        else:
            self._lbl_7z.setText("7-Zip: not required (compression preset uses built-in ZIP).")
            self._lbl_7z.setStyleSheet(f"font-size: {self._font_px}px; color: #b0b0b0;")

        try:
            if backup and os.path.isdir(backup):
                root = os.path.abspath(backup)
            else:
                root = os.path.abspath(os.path.expanduser("~"))
            usage = shutil.disk_usage(root)
            free_gib = usage.free / (1024**3)
            self._lbl_disk.setText(f"Free space on that drive: {free_gib:.1f} GiB")
            self._lbl_disk.setStyleSheet(f"font-size: {self._font_px}px; color: #cccccc;")
        except OSError:
            self._lbl_disk.setText("Free space: unknown")
            self._lbl_disk.setStyleSheet(f"font-size: {self._font_px}px; color: #b0b0b0;")
