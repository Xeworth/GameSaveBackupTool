"""Readable backup size / confirm dialog (replaces plain QMessageBox for estimates)."""
from __future__ import annotations

from typing import Any, Dict

from PyQt6.QtCore import Qt
from PyQt6.QtWidgets import (
    QDialog,
    QDialogButtonBox,
    QLabel,
    QTextBrowser,
    QVBoxLayout,
)

from styles.manager import StyleManager
from utils.backup_estimate import estimate_summary_html


class BackupEstimatePromptDialog(QDialog):
    def __init__(
        self,
        parent,
        est: Dict[str, Any],
        game_count: int,
        destination_folder: str,
        *,
        want_confirm: bool,
    ) -> None:
        super().__init__(parent)
        self.setWindowTitle("Confirm backup" if want_confirm else "Backup estimate")
        self.setModal(True)
        self.setMinimumSize(460, 420)
        self.resize(520, 480)
        sm = StyleManager.instance()
        self.setStyleSheet(sm.settings_dialog_qss())

        lay = QVBoxLayout(self)
        head = QLabel()
        head.setWordWrap(True)
        if want_confirm:
            head.setText(
                f"You are about to back up <b>{game_count}</b> game(s). "
                "Review the estimate below, then choose an action."
            )
        else:
            head.setText("Here is what will be copied. Choose whether to start the backup.")
        lay.addWidget(head)

        browser = QTextBrowser()
        browser.setReadOnly(True)
        browser.setOpenExternalLinks(False)
        browser.setHtml(
            estimate_summary_html(
                est,
                game_count,
                destination_folder,
                light_theme=sm.is_light_theme(),
            )
        )
        lay.addWidget(browser, 1)

        foot = QLabel()
        foot.setWordWrap(True)
        if want_confirm:
            foot.setText("<b>Start the backup now?</b>")
        else:
            foot.setText("<b>Start backup now?</b>")
        lay.addWidget(foot)

        buttons = QDialogButtonBox(
            QDialogButtonBox.StandardButton.Yes | QDialogButtonBox.StandardButton.No
        )
        buttons.button(QDialogButtonBox.StandardButton.Yes).setText("Start backup")
        buttons.button(QDialogButtonBox.StandardButton.No).setText("Cancel")
        buttons.accepted.connect(self.accept)
        buttons.rejected.connect(self.reject)
        lay.addWidget(buttons)
