"""Modal dialog for backup-folder / 7-Zip / disk space (opened from Settings)."""
from __future__ import annotations

from PyQt6.QtCore import QSettings
from PyQt6.QtWidgets import QDialog, QHBoxLayout, QPushButton, QVBoxLayout

from styles.manager import StyleManager
from ui.health_strip import HealthStrip


class HealthInfoDialog(QDialog):
    def __init__(self, parent, settings: QSettings) -> None:
        super().__init__(parent)
        self.setWindowTitle("Backup folder & disk health")
        self.setModal(True)
        sm = StyleManager.instance()
        self.setStyleSheet(sm.settings_dialog_qss())
        lay = QVBoxLayout(self)
        lay.setContentsMargins(8, 6, 8, 6)
        lay.setSpacing(6)
        self._strip = HealthStrip(self, settings, compact=True)
        self._strip.refresh()
        lay.addWidget(self._strip)
        row = QHBoxLayout()
        row.addStretch(1)
        close_btn = QPushButton("Close")
        close_btn.setAutoDefault(False)
        close_btn.setDefault(False)
        close_btn.clicked.connect(self.accept)
        row.addWidget(close_btn)
        row.addStretch(1)
        lay.addLayout(row)

        # Fixed dialog; height 30px less than prior 185px client target
        self.setFixedSize(420, 155)
