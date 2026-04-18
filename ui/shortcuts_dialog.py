"""In-app reference for keyboard shortcuts and common behaviors."""
from __future__ import annotations

from PyQt6.QtCore import Qt
from PyQt6.QtGui import QKeySequence, QShortcut
from PyQt6.QtWidgets import (
    QDialog,
    QFrame,
    QHBoxLayout,
    QPushButton,
    QTextBrowser,
    QVBoxLayout,
)

from styles.manager import StyleManager


def shortcuts_html() -> str:
    return """
<style type="text/css">
h3 { border: none; border-bottom: none; margin-top: 0.4em; margin-bottom: 0.2em; }
ul { margin-top: 0; margin-bottom: 0.4em; padding-left: 1.2em; }
</style>
<h3>Keyboard</h3>
<ul>
<li><b>Ctrl+B</b> — Start backup (same as Backup button): backs up selected rows, or all games with save data if nothing is selected.</li>
<li><b>Ctrl+A</b> — Select all games in the table (when the table has focus, or forwarded from the main window).</li>
<li><b>Ctrl+Z</b> — Undo the last row delete (table).</li>
<li><b>Delete</b> — Remove selected rows from the list (does not delete game files on disk).</li>
<li><b>Ctrl+Tab</b> / <b>Ctrl+Shift+Tab</b> — Next / previous tab in Settings.</li>
<li><b>F1</b> — Open this shortcuts and tips window.</li>
</ul>
<h3>Tray icon</h3>
<ul>
<li><b>Show</b> — Restore the main window.</li>
<li><b>Backup</b> — Same as a backup from the window (respects selection / defaults).</li>
<li><b>Compress</b> — Zip the backup folder; progress may appear in the tray menu while running.</li>
<li><b>Quit</b> — Exit the app (cleanup runs once on shutdown).</li>
</ul>
<h3>Tips</h3>
<ul>
<li>Closing the window may <b>minimize to tray</b> if that option is enabled in Settings.</li>
<li>Use <b>Tools → Export game list</b> to back up your discovered paths before reinstalling Windows or moving PCs.</li>
</ul>
"""


class ShortcutsDialog(QDialog):
    def __init__(self, parent=None) -> None:
        super().__init__(parent)
        self.setWindowTitle("Shortcuts & tips")
        sm = StyleManager.instance()
        pane_qss = sm.backup_estimate_browser_supplement_qss().replace(
            "backupEstimateBrowser", "shortcutsHelpBrowser"
        )
        self.setStyleSheet(sm.settings_dialog_qss() + "\n" + pane_qss)

        margin = 9
        lay = QVBoxLayout(self)
        lay.setContentsMargins(margin, margin, margin, margin)
        lay.setSpacing(margin)

        browser = QTextBrowser()
        browser.setObjectName("shortcutsHelpBrowser")
        browser.setFrameShape(QFrame.Shape.NoFrame)
        browser.setReadOnly(True)
        browser.setOpenExternalLinks(True)
        browser.setHorizontalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAlwaysOff)
        browser.setHtml(shortcuts_html())
        lay.addWidget(browser, 1)

        row = QHBoxLayout()
        row.setSpacing(margin)
        row.addStretch(1)
        close_btn = QPushButton("Close")
        close_btn.setAutoDefault(False)
        close_btn.setDefault(False)
        close_btn.clicked.connect(self.accept)
        row.addWidget(close_btn)
        row.addStretch(1)
        lay.addLayout(row)

        QShortcut(QKeySequence(Qt.Key.Key_Return), self, self.accept)
        QShortcut(QKeySequence(Qt.Key.Key_Enter), self, self.accept)
        QShortcut(QKeySequence(Qt.Key.Key_Escape), self, self.reject)

        # Client area; total window ~30px shorter than prior 740×420 target
        self.setFixedSize(740, 390)
