"""
Sandbox Live log: which categories are written to the monitor log (QSettings).
"""

from __future__ import annotations

from PyQt6.QtCore import QSettings
from PyQt6.QtWidgets import (
    QDialog,
    QDialogButtonBox,
    QFormLayout,
    QLabel,
    QPushButton,
    QVBoxLayout,
    QWidget,
)

from config.sandbox_log_prefs import DEFAULTS, log_setting_key, read_log_setting
from config.app_config import settings_app_name
from ui.custom_dialogs import CustomCheckBox


class SandboxLogSettingsDialog(QDialog):
    """Checkboxes for Live log output categories."""

    def __init__(self, parent: QWidget | None = None) -> None:
        super().__init__(parent)
        self.setWindowTitle("Sandbox — Live log output")
        self.setModal(True)
        self._settings = QSettings("MyCompany", settings_app_name())

        root = QVBoxLayout(self)
        intro = QLabel(
            "Choose which lines are appended to the Live log tab. "
            "Hardware snapshots for compression lines are optional below; the status strip always shows live metrics when psutil is installed."
        )
        intro.setWordWrap(True)
        root.addWidget(intro)

        form = QFormLayout()
        self._cb_sandbox = CustomCheckBox("Main window / sandbox notices")
        self._cb_scan = CustomCheckBox("Game scan & wiki fetch milestones")
        self._cb_cstart = CustomCheckBox("Compression: job started (folder, format)")
        self._cb_ctick = CustomCheckBox("Compression: live progress (engine, MiB/s, file counts)")
        self._cb_ctick.setToolTip("Turn off to hide noisy per-tick lines such as 7-Zip throughput updates.")
        self._cb_cnotes = CustomCheckBox("Compression progress: show long explanatory notes (7-Zip caveats)")
        self._cb_cnotes.setToolTip(
            "When off, progress lines omit the trailing note (e.g. LZMA2 / disk-growth explanations) so logs stay number-focused."
        )
        self._cb_chw = CustomCheckBox("Append hardware snapshot on compression log lines")
        self._cb_chw.setToolTip(
            "Adds a short CPU/RAM/app RSS snippet to each compression-related Live log line (not a separate periodic spam)."
        )
        self._cb_csummary = CustomCheckBox("Compression: SUMMARY line when the job finishes")
        self._cb_cexit = CustomCheckBox("Compression: worker exit / cancel result")
        self._cb_info = CustomCheckBox("Monitor tips (ready message, psutil tip, log cleared)")
        self._cb_warn = CustomCheckBox("Warnings (e.g. disk mirror write failures)")
        self._cb_marker = CustomCheckBox("Session markers ([GSBT_MARK])")

        form.addRow(self._cb_sandbox)
        form.addRow(self._cb_scan)
        form.addRow(self._cb_cstart)
        form.addRow(self._cb_ctick)
        form.addRow(self._cb_cnotes)
        form.addRow(self._cb_chw)
        form.addRow(self._cb_csummary)
        form.addRow(self._cb_cexit)
        form.addRow(self._cb_info)
        form.addRow(self._cb_warn)
        form.addRow(self._cb_marker)
        root.addLayout(form)

        defaults_btn = QPushButton("Restore defaults")
        defaults_btn.clicked.connect(self._load_defaults)
        root.addWidget(defaults_btn)

        buttons = QDialogButtonBox(
            QDialogButtonBox.StandardButton.Ok | QDialogButtonBox.StandardButton.Cancel
        )
        buttons.accepted.connect(self._save_and_accept)
        buttons.rejected.connect(self.reject)
        root.addWidget(buttons)

        self._load_from_settings()

    def _widgets_map(self) -> list[tuple[str, CustomCheckBox]]:
        return [
            ("show_sandbox", self._cb_sandbox),
            ("show_scan", self._cb_scan),
            ("show_compress_start", self._cb_cstart),
            ("show_compress_tick", self._cb_ctick),
            ("show_compress_tick_notes", self._cb_cnotes),
            ("show_compress_hw_inline", self._cb_chw),
            ("show_compress_summary", self._cb_csummary),
            ("show_compress_exit", self._cb_cexit),
            ("show_info", self._cb_info),
            ("show_warn", self._cb_warn),
            ("show_marker", self._cb_marker),
        ]

    def _load_from_settings(self) -> None:
        for key, cb in self._widgets_map():
            cb.setChecked(read_log_setting(self._settings, key))

    def _load_defaults(self) -> None:
        for key, cb in self._widgets_map():
            cb.setChecked(DEFAULTS.get(key, True))

    def _save_and_accept(self) -> None:
        for key, cb in self._widgets_map():
            self._settings.setValue(log_setting_key(key), cb.isChecked())
        self._settings.sync()
        self.accept()
