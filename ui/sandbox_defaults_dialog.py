"""Sandbox monitor: configure which persisted defaults are ignored for testing."""

from __future__ import annotations

from PyQt6.QtCore import Qt
from PyQt6.QtGui import QKeySequence, QShortcut
from PyQt6.QtWidgets import (
    QCheckBox,
    QComboBox,
    QDialog,
    QDialogButtonBox,
    QFormLayout,
    QLabel,
    QVBoxLayout,
    QWidget,
)

from config.sandbox_defaults import (
    ignore_cached_game_list,
    ignore_saved_backup_paths,
    seven_zip_ui_override,
    set_ignore_cached_game_list,
    set_ignore_saved_backup_paths,
    set_seven_zip_ui_override,
)


class SandboxDefaultsDialog(QDialog):
    """Same checkbox presentation as ``SandboxLogSettingsDialog`` (no Settings QSS sheet)."""

    def __init__(self, parent: QWidget | None = None) -> None:
        super().__init__(parent)
        self.setWindowTitle("Sandbox defaults")
        self.setModal(True)
        self.setMinimumWidth(460)

        root = QVBoxLayout(self)
        intro = QLabel(
            "The main app persists settings and the cached game list. Here you can make the sandbox "
            "behave as if those were not saved—useful for first-run or empty-state testing. "
            "7-Zip overrides affect only the Settings dialog (Get 7-Zip visibility and hints), not "
            "whether a real 7z.exe exists on disk."
        )
        intro.setWordWrap(True)
        root.addWidget(intro)

        form = QFormLayout()
        self._cb_backup = QCheckBox("Ignore saved backup folder paths")
        self._cb_backup.setChecked(ignore_saved_backup_paths())
        self._cb_backup.setToolTip(
            "Treat default and last backup folder as unset in the main window and in Settings "
            "(paths stay in QSettings; this only hides them for sandbox testing)."
        )

        self._cb_games = QCheckBox("Ignore cached game list on load / refresh")
        self._cb_games.setChecked(ignore_cached_game_list())
        self._cb_games.setToolTip(
            "Do not fill the table from config/game_save_data.json until you run Scan or turn this off."
        )

        form.addRow(self._cb_backup)
        form.addRow(self._cb_games)

        self._combo_7z = QComboBox()
        self._combo_7z.addItem("Auto (detect real 7-Zip on this PC)", "auto")
        self._combo_7z.addItem("Simulate 7-Zip installed", "present")
        self._combo_7z.addItem("Simulate 7-Zip not installed", "absent")
        cur = seven_zip_ui_override()
        for i in range(self._combo_7z.count()):
            if self._combo_7z.itemData(i) == cur:
                self._combo_7z.setCurrentIndex(i)
                break
        self._combo_7z.setToolTip(
            "When the 7-Zip preset is selected: controls visibility of “Get 7-Zip…” and the hint line. "
            "Use “not installed” to preview that UI even if 7-Zip is on your machine."
        )
        form.addRow("7-Zip in Settings UI:", self._combo_7z)

        root.addLayout(form)

        buttons = QDialogButtonBox(
            QDialogButtonBox.StandardButton.Ok | QDialogButtonBox.StandardButton.Cancel
        )
        buttons.accepted.connect(self.accept)
        buttons.rejected.connect(self.reject)
        ok_btn = buttons.button(QDialogButtonBox.StandardButton.Ok)
        if ok_btn is not None:
            ok_btn.setDefault(True)
            ok_btn.setAutoDefault(True)
        root.addWidget(buttons)

        esc = QShortcut(QKeySequence(Qt.Key.Key_Escape), self)
        esc.activated.connect(self.reject)

    def apply_to_settings(self) -> None:
        set_ignore_saved_backup_paths(self._cb_backup.isChecked())
        set_ignore_cached_game_list(self._cb_games.isChecked())
        set_seven_zip_ui_override(self._combo_7z.currentData())
