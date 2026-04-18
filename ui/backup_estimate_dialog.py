"""Readable backup size / confirm dialog (replaces plain QMessageBox for estimates)."""
from __future__ import annotations

from typing import Any, Dict

from PyQt6.QtCore import Qt
from PyQt6.QtWidgets import (
    QDialog,
    QDialogButtonBox,
    QHBoxLayout,
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
        self.setObjectName("BackupEstimatePromptDialog")
        self.setWindowTitle("Confirm backup" if want_confirm else "Backup estimate")
        self.setModal(True)

        margin = 9
        self.setFixedWidth(470)
        self.setMinimumHeight(360)
        self.resize(470, 480)

        sm = StyleManager.instance()
        self.setStyleSheet(
            sm.settings_dialog_qss()
            + "\n"
            + sm.backup_estimate_browser_supplement_qss()
            + "\n"
            + sm.backup_estimate_start_backup_button_qss()
        )

        lay = QVBoxLayout(self)
        lay.setContentsMargins(margin, margin, margin, margin)
        lay.setSpacing(margin)

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
        browser.setObjectName("backupEstimateBrowser")
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

        footer = QHBoxLayout()
        footer.setContentsMargins(0, 0, 0, 0)
        footer.setSpacing(margin)

        prompt = QLabel()
        prompt.setWordWrap(True)
        if want_confirm:
            prompt.setText("<b>Start the backup now?</b>")
        else:
            prompt.setText("<b>Start backup now?</b>")
        prompt.setAlignment(
            Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter
        )
        footer.addWidget(prompt, 1)

        buttons = QDialogButtonBox(
            QDialogButtonBox.StandardButton.Yes | QDialogButtonBox.StandardButton.No
        )
        yes_btn = buttons.button(QDialogButtonBox.StandardButton.Yes)
        yes_btn.setObjectName("backupEstimateStartButton")
        yes_btn.setText("Start backup")
        yes_btn.setDefault(True)
        yes_btn.setAutoDefault(True)
        buttons.button(QDialogButtonBox.StandardButton.No).setText("Cancel")
        buttons.accepted.connect(self.accept)
        buttons.rejected.connect(self.reject)
        footer.addWidget(buttons)

        lay.addLayout(footer)
