"""Pre-backup size estimate dialog with severity warnings and path confirmation."""

from __future__ import annotations

from typing import Callable, Dict, List, Optional, Set

from PyQt6.QtCore import Qt, QUrl
from PyQt6.QtGui import QDesktopServices
from PyQt6.QtWidgets import (
    QDialog,
    QDialogButtonBox,
    QFrame,
    QHBoxLayout,
    QLabel,
    QPushButton,
    QScrollArea,
    QToolButton,
    QVBoxLayout,
    QWidget,
)

from core.backup_folder_size_estimator import (
    BackupSizeEstimateSummary,
    BackupSizeSeverity,
    format_byte_size,
)
from styles.manager import StyleManager


class BackupEstimatePromptDialog(QDialog):
    def __init__(
        self,
        parent,
        est: Dict,
        game_count: int,
        destination_folder: str,
        *,
        want_confirm: bool,
        warning_only: bool = False,
        trusted_names: Optional[Set[str]] = None,
        on_trust_path: Optional[Callable[[str], None]] = None,
        on_remove_game: Optional[Callable[[str], None]] = None,
    ) -> None:
        super().__init__(parent)
        self.setObjectName("BackupEstimatePromptDialog")
        self.setWindowTitle("Confirm backup")
        self.setModal(True)
        self._trusted = {n.lower() for n in (trusted_names or set())}
        self._on_trust = on_trust_path
        self._on_remove = on_remove_game
        self._removed: List[str] = []
        self._summary: Optional[BackupSizeEstimateSummary] = est.get("summary")
        self._game_count = game_count

        margin = 9
        self.setMinimumWidth(520)
        self.resize(520, 520)

        sm = StyleManager.instance()
        self._sm = sm
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
        if warning_only:
            head.setText(
                "<b>Some save folders look unusually large.</b> "
                "Review them before starting the backup."
            )
        elif want_confirm:
            head.setText(
                f"You are about to back up <b>{game_count}</b> game(s). "
                "Review the estimate below, then choose an action."
            )
        else:
            head.setText("Review what will be copied, then start the backup or cancel.")
        lay.addWidget(head)

        dest = QLabel(f"<b>Destination</b><br/><span style='color:#888;'>{destination_folder}</span>")
        dest.setWordWrap(True)
        lay.addWidget(dest)

        if self._summary:
            stats = (
                f"Games: {self._summary.games_in_backup} · "
                f"Folders: {self._summary.save_folders_on_disk} · "
                f"Files: {self._summary.total_files:,} · "
                f"Size: {format_byte_size(self._summary.total_bytes)}"
            )
            lay.addWidget(QLabel(stats))

        scroll = QScrollArea()
        scroll.setWidgetResizable(True)
        scroll.setFrameShape(QFrame.Shape.NoFrame)
        inner = QWidget()
        self._blocks_layout = QVBoxLayout(inner)
        self._blocks_layout.setContentsMargins(0, 0, 0, 0)
        self._blocks_layout.setSpacing(8)
        scroll.setWidget(inner)
        lay.addWidget(scroll, 1)

        self._block_widgets: Dict[str, QWidget] = {}
        if self._summary:
            entries = self._summary.entries
            if warning_only:
                entries = [
                    e
                    for e in entries
                    if not e.is_registry_only
                    and e.severity in (BackupSizeSeverity.LARGE, BackupSizeSeverity.SUSPICIOUS)
                ]
            for entry in entries:
                self._add_game_block(entry)

        footer = QHBoxLayout()
        prompt = QLabel("<b>Start the backup now?</b>")
        footer.addWidget(prompt, 1)
        buttons = QDialogButtonBox(
            QDialogButtonBox.StandardButton.Yes | QDialogButtonBox.StandardButton.No
        )
        yes_btn = buttons.button(QDialogButtonBox.StandardButton.Yes)
        yes_btn.setObjectName("backupEstimateStartButton")
        yes_btn.setText("Start backup")
        yes_btn.setDefault(True)
        buttons.button(QDialogButtonBox.StandardButton.No).setText("Cancel")
        buttons.accepted.connect(self.accept)
        buttons.rejected.connect(self.reject)
        footer.addWidget(buttons)
        lay.addLayout(footer)

    @property
    def removed_game_names(self) -> List[str]:
        return list(self._removed)

    def _severity_color(self, severity: BackupSizeSeverity) -> str:
        if self._sm.is_light_theme():
            return {
                BackupSizeSeverity.NORMAL: "#2e7d32",
                BackupSizeSeverity.LARGE: "#f9a825",
                BackupSizeSeverity.SUSPICIOUS: "#c62828",
            }[severity]
        return {
            BackupSizeSeverity.NORMAL: "#8fdf9a",
            BackupSizeSeverity.LARGE: "#ffcc80",
            BackupSizeSeverity.SUSPICIOUS: "#ff8a80",
        }[severity]

    def _hint_for(self, severity: BackupSizeSeverity) -> str:
        if severity == BackupSizeSeverity.LARGE:
            return "Unusually large — double-check this is not an install or wrong game folder."
        if severity == BackupSizeSeverity.SUSPICIOUS:
            return "Very large for typical saves — confirm this path before backing up."
        return ""

    def _add_game_block(self, entry) -> None:
        block = QFrame()
        block.setFrameShape(QFrame.Shape.StyledPanel)
        bl = QVBoxLayout(block)
        bl.setContentsMargins(8, 8, 8, 8)

        header = QHBoxLayout()
        title = QLabel(f"<b>{entry.game_name}</b>")
        title.setWordWrap(True)
        header.addWidget(title, 1)

        if entry.save_folder_path:
            open_btn = QToolButton()
            open_btn.setText("📁")
            open_btn.setToolTip("Open save folder in File Explorer (Space/Enter when focused)")
            path = entry.save_folder_path
            open_btn.clicked.connect(
                lambda _=False, p=path: QDesktopServices.openUrl(QUrl.fromLocalFile(p))
            )
            header.addWidget(open_btn)

        bl.addLayout(header)

        if entry.is_registry_only:
            bl.addWidget(QLabel("<span style='color:#888;'>Registry export (small .reg file)</span>"))
        elif not entry.save_folder_path:
            bl.addWidget(QLabel("<span style='color:#c62828;'>No folder on disk</span>"))
        else:
            color = self._severity_color(entry.severity)
            size_line = QLabel(
                f"Files: {entry.file_count:,} · "
                f"<span style='color:{color}; font-weight:600;'>{format_byte_size(entry.bytes_count)}</span>"
            )
            bl.addWidget(size_line)
            hint = self._hint_for(entry.severity)
            hint_lbl = QLabel(hint)
            hint_lbl.setWordWrap(True)
            hint_lbl.setObjectName("estimateHint")
            if hint:
                hint_lbl.setStyleSheet(f"color: {color}; font-size: 11px;")
            bl.addWidget(hint_lbl)

            if entry.severity in (BackupSizeSeverity.LARGE, BackupSizeSeverity.SUSPICIOUS):
                if entry.game_name.lower() not in self._trusted:
                    btn_row = QHBoxLayout()
                    yes_btn = QPushButton("✓ Yes, this path is correct")
                    no_btn = QPushButton("✗ No, this path isn't correct")
                    yes_btn.clicked.connect(lambda _=False, e=entry, b=block: self._trust(e, b))
                    no_btn.clicked.connect(lambda _=False, e=entry, b=block: self._reject_path(e, b))
                    btn_row.addWidget(yes_btn)
                    btn_row.addWidget(no_btn)
                    bl.addLayout(btn_row)

        self._blocks_layout.addWidget(block)
        self._block_widgets[entry.game_name] = block

    def _trust(self, entry, block: QWidget) -> None:
        if self._on_trust:
            self._on_trust(entry.game_name)
        self._trusted.add(entry.game_name.lower())
        block.setStyleSheet("QFrame { border: 1px solid #2e7d32; }")

    def _reject_path(self, entry, block: QWidget) -> None:
        from PyQt6.QtWidgets import QMessageBox

        if (
            QMessageBox.question(
                self,
                "Remove from list",
                f"Remove “{entry.game_name}” from your game list?\n\nYou can undo with Ctrl+Z after closing this dialog.",
            )
            != QMessageBox.StandardButton.Yes
        ):
            return
        if self._on_remove:
            self._on_remove(entry.game_name)
        self._removed.append(entry.game_name)
        block.hide()
        self._game_count = max(0, self._game_count - 1)
