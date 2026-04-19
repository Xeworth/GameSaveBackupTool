"""Standard-style About box (version, credits, source link)."""
from __future__ import annotations

from PyQt6.QtCore import Qt
from PyQt6.QtGui import QKeySequence, QShortcut
from PyQt6.QtWidgets import QDialog, QHBoxLayout, QLabel, QPushButton, QVBoxLayout

from config.app_metadata import (
    APP_COPYRIGHT,
    APP_NAME,
    APP_VERSION_DISPLAY,
    SOURCE_REPOSITORY_URL,
)
from styles.manager import StyleManager


class AboutDialog(QDialog):
    def __init__(self, parent=None) -> None:
        super().__init__(parent)
        self.setWindowTitle(f"About {APP_NAME}")
        self.setModal(True)
        self.setFixedSize(480, 220)
        sm = StyleManager.instance()
        self.setStyleSheet(sm.settings_dialog_qss())
        muted = sm.settings_version_muted_color()

        lay = QVBoxLayout(self)
        lay.setContentsMargins(9, 9, 9, 9)
        lay.setSpacing(10)

        title = QLabel(f"<b>{APP_NAME}</b>")
        title.setAlignment(Qt.AlignmentFlag.AlignCenter)
        lay.addWidget(title)

        ver = QLabel(APP_VERSION_DISPLAY)
        ver.setAlignment(Qt.AlignmentFlag.AlignCenter)
        ver.setStyleSheet(f"font-size: 11px; color: {sm.settings_primary_on_dialog_color()};")
        lay.addWidget(ver)

        cr = QLabel(APP_COPYRIGHT)
        cr.setAlignment(Qt.AlignmentFlag.AlignCenter)
        cr.setStyleSheet(f"font-size: 10px; color: {muted};")
        lay.addWidget(cr)

        link = QLabel(
            f'<a href="{SOURCE_REPOSITORY_URL}">Source code on GitHub</a>'
        )
        link.setAlignment(Qt.AlignmentFlag.AlignCenter)
        link.setOpenExternalLinks(True)
        link.setTextInteractionFlags(
            Qt.TextInteractionFlag.LinksAccessibleByMouse
            | Qt.TextInteractionFlag.LinksAccessibleByKeyboard
        )
        link.setStyleSheet(f"font-size: 10px; color: {muted};")
        lay.addWidget(link)

        blurb = QLabel()
        blurb.setTextFormat(Qt.TextFormat.RichText)
        blurb.setWordWrap(True)
        blurb.setAlignment(Qt.AlignmentFlag.AlignCenter)
        blurb.setText(
            f"<div style='padding: 4px 20px; font-size: 9px; color: {muted};'>"
            "Distributed as open source; portable builds may be published separately "
            "from the repository releases page."
            "</div>"
        )
        lay.addWidget(blurb)

        lay.addSpacing(9)

        row = QHBoxLayout()
        row.addStretch(1)
        ok = QPushButton("OK")
        ok.setDefault(False)
        ok.setAutoDefault(False)
        ok.clicked.connect(self.accept)
        row.addWidget(ok)
        row.addStretch(1)
        lay.addLayout(row)

        QShortcut(QKeySequence(Qt.Key.Key_Return), self, self.accept)
        QShortcut(QKeySequence(Qt.Key.Key_Enter), self, self.accept)
        QShortcut(QKeySequence(Qt.Key.Key_Escape), self, self.reject)
