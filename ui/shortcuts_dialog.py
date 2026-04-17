"""In-app reference for keyboard shortcuts and common behaviors."""
from __future__ import annotations

from PyQt6.QtWidgets import QDialog, QHBoxLayout, QPushButton, QVBoxLayout, QTextBrowser

from styles.manager import StyleManager


def shortcuts_html() -> str:
    return """
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
        self.setMinimumSize(420, 380)
        sm = StyleManager.instance()
        self.setStyleSheet(sm.settings_dialog_qss())
        lay = QVBoxLayout(self)
        browser = QTextBrowser()
        browser.setReadOnly(True)
        browser.setOpenExternalLinks(True)
        browser.setHtml(shortcuts_html())
        lay.addWidget(browser)
        row = QHBoxLayout()
        row.addStretch(1)
        close_btn = QPushButton("Close")
        close_btn.setDefault(True)
        close_btn.clicked.connect(self.accept)
        row.addWidget(close_btn)
        row.addStretch(1)
        lay.addLayout(row)
