"""
Sandbox admin monitor: live system metrics + append-only event log.

Shown when the app runs with ``--sandbox`` / ``-s`` (``GSBT_SANDBOX``).
"""

from __future__ import annotations

import html
import re
from collections import deque
from datetime import datetime
from typing import Any, Deque, Dict, List, Optional

from PyQt6.QtCore import QByteArray, Qt, QSettings, QTimer
from PyQt6.QtGui import QFont, QKeySequence, QShortcut, QTextCursor
from PyQt6.QtWidgets import (
    QApplication,
    QCheckBox,
    QDialog,
    QHBoxLayout,
    QInputDialog,
    QLabel,
    QLineEdit,
    QMainWindow,
    QMessageBox,
    QPlainTextEdit,
    QPushButton,
    QSpinBox,
    QTextEdit,
    QVBoxLayout,
    QWidget,
)

from config.app_config import settings_app_name
from config.sandbox_log_prefs import read_log_setting
from styles.manager import StyleManager
from ui.sandbox_log_settings_dialog import SandboxLogSettingsDialog
from ui.settings_framed_tabs import SettingsFramedTabs
from utils.session_debug_log import append_session_line, log_file_path, reset_session_log
from utils.system_metrics import format_inline_hw_snapshot, format_snapshot_line, snapshot

MARK_PREFIX = "[GSBT_MARK]"

_LOG_CATEGORY_TO_SETTING: Dict[str, str] = {
    "sandbox": "show_sandbox",
    "scan": "show_scan",
    "compress_start": "show_compress_start",
    "compress_tick": "show_compress_tick",
    "compress_summary": "show_compress_summary",
    "compress_exit": "show_compress_exit",
    "info": "show_info",
    "warn": "show_warn",
    "marker": "show_marker",
}

_COMPRESS_HW_SUFFIX_CATEGORIES = frozenset(
    {"compress_start", "compress_tick", "compress_summary", "compress_exit"}
)


class SandboxMonitorWindow(QMainWindow):
    """Second window: metrics, buffered logs (filter / tail / markers), compression history."""

    MAX_LOG_BLOCKS = 2500
    MAX_GAMES_LINES = 8000
    MAX_BATCH_LINES = 4000
    MAX_TRACE_LINES = 12000

    _TRACE_INTRO_LINES = (
        "Legend (per-game END line): SAVE_ON_DISK | WIKI_PATHS_NO_DISK (wiki had strings, not on disk) | "
        "NO_WIKI_DATA",
        "Inline: semantic PATH STRINGS = good cells | semantic empty / NO save path props = dead end | "
        "HTML fallback = slower second pass",
    )

    def __init__(self, parent=None):
        super().__init__(parent)
        self.setWindowTitle("GSBT Sandbox Monitor")
        self.setMinimumSize(480, 320)
        self._settings = QSettings("MyCompany", settings_app_name())
        geom = self._settings.value("sandbox_monitor_geometry")
        if isinstance(geom, QByteArray) and not geom.isEmpty():
            self.restoreGeometry(geom)
        else:
            self.resize(820, 540)
        self._compression_test_n = 0

        self._live_buf: Deque[str] = deque(maxlen=self.MAX_LOG_BLOCKS)
        self._games_buf: Deque[str] = deque(maxlen=self.MAX_GAMES_LINES)
        self._batch_buf: Deque[str] = deque(maxlen=self.MAX_BATCH_LINES)
        self._trace_buf: Deque[str] = deque(maxlen=self.MAX_TRACE_LINES)
        self._buffers: List[Deque[str]] = [self._live_buf, self._games_buf, self._batch_buf, self._trace_buf]

        self._follow_tail = [True, True, True, True, True]
        self._disk_write_failures = 0
        self._disk_error_log_milestone = 0
        self._last_tab_idx: int = 0
        self._compression_intro_active = True
        self._main_window: Any = None

        self.menuBar().setVisible(False)

        central = QWidget()
        self.setCentralWidget(central)
        layout = QVBoxLayout(central)
        layout.setContentsMargins(8, 8, 8, 8)
        layout.setSpacing(6)

        self._status = QLabel()
        self._status.setWordWrap(True)
        self._status.setTextInteractionFlags(Qt.TextInteractionFlag.TextSelectableByMouse)
        layout.addWidget(self._status)

        # --- Diagnostics toolbar (filter, tail, copy helpers, disk errors) ---
        diag = QWidget()
        diag.setObjectName("sandboxDiagToolbar")
        diag_outer = QVBoxLayout(diag)
        diag_outer.setContentsMargins(0, 0, 0, 0)
        diag_outer.setSpacing(4)

        row1 = QHBoxLayout()
        row1.setSpacing(8)
        self._filter_edit = QLineEdit()
        self._filter_edit.setPlaceholderText("Filter lines in current tab…")
        self._filter_edit.setClearButtonEnabled(True)
        self._filter_edit.setToolTip(
            "Shows only lines that match. Applies to Live log and Save fetch tabs. "
            "Compression history uses plain-text match on extracted text. "
            "Shortcuts: Ctrl+F focus here, Esc clear focus."
        )
        self._filter_case = QCheckBox("Match case")
        self._filter_case.setToolTip("Case-sensitive substring or regex.")
        self._filter_regex = QCheckBox("Regex")
        self._filter_regex.setToolTip("Python ``re`` syntax. Invalid patterns show all lines until fixed.")
        self._btn_clear_filter = QPushButton("Clear filter")
        self._btn_clear_filter.clicked.connect(self._clear_filter)
        self._filter_regex_error = QLabel("Invalid regex")
        self._filter_regex_error.setStyleSheet("color: #f14c4c; font-size: 10px;")
        self._filter_regex_error.hide()
        self._disk_errors_label = QLabel()
        self._disk_errors_label.setToolTip("Failed writes when mirroring save-fetch lines to the session log file.")
        self._refresh_disk_errors_label()
        row1.addWidget(self._filter_edit, 1)
        row1.addWidget(self._filter_case)
        row1.addWidget(self._filter_regex)
        row1.addWidget(self._btn_clear_filter)
        row1.addWidget(self._filter_regex_error)
        row1.addWidget(self._disk_errors_label)
        diag_outer.addLayout(row1)

        row2 = QHBoxLayout()
        row2.setSpacing(8)
        self._follow_tail_cb = QCheckBox("Follow tail")
        self._follow_tail_cb.setToolTip(
            "When checked, the active tab scrolls to the newest content on each append. "
            "Turn off to read earlier lines without being pulled to the bottom."
        )
        self._follow_tail_cb.toggled.connect(self._on_follow_tail_toggled)
        self._btn_scroll_latest = QPushButton("Scroll to latest")
        self._btn_scroll_latest.setToolTip("Scroll the active tab to the bottom once.")
        self._btn_scroll_latest.clicked.connect(self._scroll_active_to_latest)
        self._spin_last_n = QSpinBox()
        self._spin_last_n.setRange(1, 2500)
        self._spin_last_n.setValue(50)
        self._spin_last_n.setMaximumWidth(72)
        self._spin_last_n.setToolTip("How many trailing lines to copy from the full buffer (not the filtered view).")
        self._btn_copy_last_n = QPushButton("Copy last N")
        self._btn_copy_last_n.setToolTip("Copy the last N lines from the current tab’s full buffer to the clipboard.")
        self._btn_copy_last_n.clicked.connect(self._copy_last_n_lines)
        self._btn_drop_marker = QPushButton("Drop marker…")
        self._btn_drop_marker.setToolTip(
            f"Insert {MARK_PREFIX} in the current tab so you can copy everything since that point later."
        )
        self._btn_drop_marker.clicked.connect(self._drop_marker)
        self._btn_copy_since_marker = QPushButton("Copy since marker")
        self._btn_copy_since_marker.setToolTip(f"Copy from the last {MARK_PREFIX} line to the end of the buffer.")
        self._btn_copy_since_marker.clicked.connect(self._copy_since_marker)
        self._btn_log_settings = QPushButton("Live log output…")
        self._btn_log_settings.setToolTip(
            "Choose which categories are written to the Live log (scan, compression ticks, metrics, …)."
        )
        self._btn_log_settings.clicked.connect(self._open_log_settings)
        self._btn_defaults = QPushButton("Defaults…")
        self._btn_defaults.setToolTip(
            "Ignore saved backup paths, cached game list, or simulate 7-Zip install state for UI testing."
        )
        self._btn_defaults.clicked.connect(self._open_sandbox_defaults_dialog)
        row2.addWidget(self._follow_tail_cb)
        row2.addWidget(self._btn_scroll_latest)
        row2.addWidget(QLabel("N:"))
        row2.addWidget(self._spin_last_n)
        row2.addWidget(self._btn_copy_last_n)
        row2.addSpacing(12)
        row2.addWidget(self._btn_drop_marker)
        row2.addWidget(self._btn_copy_since_marker)
        row2.addSpacing(12)
        row2.addWidget(self._btn_log_settings)
        row2.addWidget(self._btn_defaults)
        self._btn_show_main = QPushButton("Show main window")
        self._btn_show_main.setToolTip(
            "Bring the Game Save Backup Tool window back when you closed it but kept the monitor open."
        )
        self._btn_show_main.clicked.connect(self._on_show_main_window_clicked)
        row2.addWidget(self._btn_show_main)
        row2.addStretch(1)
        diag_outer.addLayout(row2)

        layout.addWidget(diag)

        self._filter_debounce = QTimer(self)
        self._filter_debounce.setSingleShot(True)
        self._filter_debounce.timeout.connect(self._apply_filter_refresh)
        self._filter_edit.textChanged.connect(lambda: self._filter_debounce.start(200))
        self._filter_case.toggled.connect(lambda: self._filter_debounce.start(50))
        self._filter_regex.toggled.connect(lambda: self._filter_debounce.start(50))

        sc_find = QShortcut(QKeySequence("Ctrl+F"), self)
        sc_find.setContext(Qt.ShortcutContext.WidgetWithChildrenShortcut)
        sc_find.activated.connect(self._focus_filter_edit)

        self._tabs = SettingsFramedTabs(
            central,
            main_tabs_object_name="sandboxMainTabs",
            framed_panel_object_name="sandboxFramedPanel",
            framed_content_padding=3,
        )
        layout.addWidget(self._tabs, 1)

        # --- Tab: Live log ---
        live = QWidget()
        live_layout = QVBoxLayout(live)
        live_layout.setContentsMargins(0, 0, 0, 0)
        self._log = QPlainTextEdit()
        self._log.setReadOnly(True)
        self._log.setFont(QFont("Consolas", 10))
        if not self._log.font().family():
            self._log.setFont(QFont("Courier New", 10))
        live_layout.addWidget(self._log, 1)
        self._tabs.addTab(live, "Live log")

        # --- Tab: Save fetch (games) ---
        sf_games = QWidget()
        sf_games_layout = QVBoxLayout(sf_games)
        sf_games_layout.setContentsMargins(0, 0, 0, 0)
        self._save_fetch_per = QPlainTextEdit()
        self._save_fetch_per.setReadOnly(True)
        self._save_fetch_per.setFont(QFont("Consolas", 10))
        if not self._save_fetch_per.font().family():
            self._save_fetch_per.setFont(QFont("Courier New", 10))
        sf_games_layout.addWidget(self._save_fetch_per, 1)
        self._tabs.addTab(sf_games, "Save fetch (games)")

        # --- Tab: Save fetch (batches) ---
        sf_batch = QWidget()
        sf_batch_layout = QVBoxLayout(sf_batch)
        sf_batch_layout.setContentsMargins(0, 0, 0, 0)
        self._save_fetch_batches = QPlainTextEdit()
        self._save_fetch_batches.setReadOnly(True)
        self._save_fetch_batches.setFont(QFont("Consolas", 10))
        if not self._save_fetch_batches.font().family():
            self._save_fetch_batches.setFont(QFont("Courier New", 10))
        sf_batch_layout.addWidget(self._save_fetch_batches, 1)
        self._tabs.addTab(sf_batch, "Save fetch (batches)")

        # --- Tab: Save fetch (trace) ---
        sf_trace = QWidget()
        sf_trace_layout = QVBoxLayout(sf_trace)
        sf_trace_layout.setContentsMargins(0, 0, 0, 0)
        self._save_fetch_trace = QPlainTextEdit()
        self._save_fetch_trace.setReadOnly(True)
        self._save_fetch_trace.setFont(QFont("Consolas", 9))
        if not self._save_fetch_trace.font().family():
            self._save_fetch_trace.setFont(QFont("Courier New", 9))
        sf_trace_layout.addWidget(self._save_fetch_trace, 1)
        self._tabs.addTab(sf_trace, "Save fetch (trace)")
        for intro in self._TRACE_INTRO_LINES:
            self._trace_buf.append(intro)

        # --- Tab: Compression history ---
        hist = QWidget()
        hist_layout = QVBoxLayout(hist)
        hist_layout.setContentsMargins(0, 0, 0, 0)
        self._history = QTextEdit()
        self._history.setReadOnly(True)
        self._history.setAcceptRichText(True)
        self._history.setFont(QFont("Consolas", 10))
        if not self._history.font().family():
            self._history.setFont(QFont("Courier New", 10))
        hist_layout.addWidget(self._history, 1)
        self._tabs.addTab(hist, "Compression history")

        self._plain_widgets = [self._log, self._save_fetch_per, self._save_fetch_batches, self._save_fetch_trace]

        # Bottom bar (per-tab clear/copy + live disk controls)
        bottom = QWidget()
        bottom_lay = QVBoxLayout(bottom)
        bottom_lay.setContentsMargins(8, 4, 8, 0)
        bottom_lay.setSpacing(3)

        btn_row = QHBoxLayout()
        btn_row.setSpacing(6)
        self._btn_clear = QPushButton()
        self._btn_clear.clicked.connect(self._on_bottom_clear_clicked)
        self._btn_copy = QPushButton()
        self._btn_copy.clicked.connect(self._on_bottom_copy_clicked)
        btn_row.addWidget(self._btn_clear)
        btn_row.addWidget(self._btn_copy)
        btn_row.addSpacing(20)
        self._btn_reset_disk = QPushButton("New disk log…")
        self._btn_reset_disk.setToolTip("Truncate the session log and start a new empty file.")
        self._btn_reset_disk.clicked.connect(self._on_reset_disk_log)
        btn_row.addWidget(self._btn_reset_disk)
        btn_row.addStretch(1)
        self._mirror_disk_cb = QCheckBox("Mirror save-fetch tabs to disk log")
        self._mirror_disk_cb.setChecked(True)
        btn_row.addWidget(self._mirror_disk_cb, 0, Qt.AlignmentFlag.AlignRight)
        bottom_lay.addLayout(btn_row)

        labels_row = QHBoxLayout()
        labels_row.setSpacing(0)
        self._bottom_hint = QLabel()
        self._bottom_hint.setWordWrap(True)
        self._bottom_hint.setTextInteractionFlags(Qt.TextInteractionFlag.TextSelectableByMouse)
        self._bottom_hint.setAlignment(Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignTop)
        labels_row.addWidget(self._bottom_hint, 1)
        bottom_lay.addLayout(labels_row)

        layout.addWidget(bottom)

        self._bottom_clear_handlers = [
            self.clear_log,
            self.clear_save_fetch_games,
            self.clear_save_fetch_batches,
            self.clear_save_fetch_trace,
            self.clear_compression_history,
        ]
        self._bottom_copy_handlers = [
            self._copy_log,
            self._copy_save_fetch_games,
            self._copy_save_fetch_batches,
            self._copy_save_fetch_trace,
            self._copy_history,
        ]
        self._tabs.currentChanged.connect(self._on_sandbox_tab_changed)
        self._sync_bottom_bar(0)
        self._update_mirror_disk_tooltip()
        self._sync_follow_tail_checkbox()
        self._update_filter_controls_enabled()
        self._update_filter_error_label()
        self._render_plain_tab(3, scroll=False)

        self._timer = QTimer(self)
        self._timer.setTimerType(Qt.TimerType.CoarseTimer)
        self._timer.timeout.connect(self._on_tick)
        self._timer.start(1000)
        self._on_tick()

        self.log_line("Sandbox monitor ready. Fresh QSettings scope; use this log for compression & scan timings.")
        self.log_line("Tip: install psutil for live CPU/RAM (pip install psutil).")
        self._push_batch_lines(
            "Save fetch (batches): each scan’s PCGW phase logs a header when fetch starts and a footer when all games finish.\n"
        )
        self._append_history_plain(self._compression_history_intro_html())

        self.apply_app_style()

    def set_main_window(self, window: Any) -> None:
        """Main app window; used to apply Defaults overrides without restarting."""
        self._main_window = window

    def _open_sandbox_defaults_dialog(self) -> None:
        from ui.sandbox_defaults_dialog import SandboxDefaultsDialog

        dlg = SandboxDefaultsDialog(self)
        if dlg.exec() != QDialog.DialogCode.Accepted:
            return
        dlg.apply_to_settings()
        mw = self._main_window
        if mw is not None:
            mw.apply_sandbox_defaults_refresh()

    def refresh_show_main_button(self) -> None:
        """Enable **Show main window** when the main window is hidden (sandbox)."""
        if not hasattr(self, "_btn_show_main"):
            return
        mw = self._main_window
        self._btn_show_main.setEnabled(mw is not None and not mw.isVisible())

    def _on_show_main_window_clicked(self) -> None:
        mw = self._main_window
        if mw is None:
            return
        if mw.isMinimized():
            mw.showNormal()
        mw.show()
        mw.raise_()
        mw.activateWindow()
        self.refresh_show_main_button()

    # --- Buffers & rendering -------------------------------------------------

    def _plain_widget(self, tab_idx: int) -> QPlainTextEdit:
        return self._plain_widgets[tab_idx]

    def _push_line(self, tab_idx: int, line: str) -> None:
        if not (0 <= tab_idx <= 3):
            return
        text = line.rstrip("\n")
        if not text:
            return
        buf = self._buffers[tab_idx]
        buf.append(text)
        if self._tabs.currentIndex() == tab_idx:
            self._render_plain_tab(tab_idx, scroll=None)

    def _push_batch_lines(self, blob: str) -> None:
        for part in blob.splitlines():
            self._push_line(2, part)

    def _filter_needle(self) -> str:
        return self._filter_edit.text().strip()

    def _filtered_lines(self, lines: List[str]) -> List[str]:
        needle = self._filter_needle()
        if not needle:
            return lines
        case = self._filter_case.isChecked()
        if self._filter_regex.isChecked():
            try:
                flags = 0 if case else re.IGNORECASE
                cre = re.compile(needle, flags)
                return [ln for ln in lines if cre.search(ln)]
            except re.error:
                return lines
        if case:
            return [ln for ln in lines if needle in ln]
        n = needle.lower()
        return [ln for ln in lines if n in ln.lower()]

    def _display_lines_for_tab(self, tab_idx: int) -> str:
        lines = list(self._buffers[tab_idx])
        if tab_idx >= 4 or not self._filter_needle():
            return "\n".join(lines)
        return "\n".join(self._filtered_lines(lines))

    def _render_plain_tab(self, tab_idx: int, *, scroll: Optional[bool] = None) -> None:
        if not (0 <= tab_idx <= 3):
            return
        w = self._plain_widget(tab_idx)
        w.setPlainText(self._display_lines_for_tab(tab_idx))
        if scroll is None:
            scroll = self._follow_tail[tab_idx] and (self._tabs.currentIndex() == tab_idx)
        if scroll:
            sb = w.verticalScrollBar()
            sb.setValue(sb.maximum())

    def _apply_filter_refresh(self) -> None:
        self._update_filter_error_label()
        idx = self._tabs.currentIndex()
        if 0 <= idx <= 3:
            self._render_plain_tab(idx, scroll=self._follow_tail[idx])
        elif idx == 4:
            if self._filter_needle():
                self._render_history_filter_view()
            else:
                hb = self._history_full_html_backup()
                if hb is not None:
                    self._history.setHtml(hb)
                    self._history_html_backup = self._history.toHtml()

    def _render_history_filter_view(self) -> None:
        """Plain-text filter of history; full HTML is restored when the filter is cleared or you leave the tab."""
        if not self._filter_needle():
            return
        if self._history_full_html_backup() is None:
            self._history_html_backup = self._history.toHtml()
        hb = self._history_full_html_backup()
        if hb is None:
            return
        self._history.setHtml(hb)
        plain_lines = self._history.toPlainText().splitlines()
        kept = self._filtered_lines(plain_lines)
        self._history.setPlainText("\n".join(kept))

    def _history_full_html_backup(self) -> Optional[str]:
        return getattr(self, "_history_html_backup", None)

    def _refresh_disk_errors_label(self) -> None:
        n = self._disk_write_failures
        self._disk_errors_label.setText("Disk mirror: 0 write errors" if n == 0 else f"Disk mirror: {n} write error(s)")

    def _clear_filter(self) -> None:
        self._filter_edit.clear()
        self._filter_regex_error.hide()
        idx = self._tabs.currentIndex()
        if 0 <= idx <= 3:
            self._render_plain_tab(idx, scroll=self._follow_tail[idx])
        elif idx == 4:
            html_bak = self._history_full_html_backup()
            if html_bak is not None:
                self._history.setHtml(html_bak)
                self._history_html_backup = self._history.toHtml()

    def _focus_filter_edit(self) -> None:
        if self._filter_edit.isEnabled():
            self._filter_edit.setFocus(Qt.FocusReason.ShortcutFocusReason)
            self._filter_edit.selectAll()

    def _open_log_settings(self) -> None:
        SandboxLogSettingsDialog(self).exec()

    @staticmethod
    def _normalize_log_category(category: str, message: str) -> str:
        if category != "compress":
            return category
        if message.startswith("SUMMARY:"):
            return "compress_summary"
        if "Compression started" in message:
            return "compress_start"
        if "Compression worker exit" in message or "Compression cancel requested" in message:
            return "compress_exit"
        return "compress_tick"

    def _log_line_allowed_with_message(self, category: str, message: str) -> bool:
        c = self._normalize_log_category(category, message)
        sk = _LOG_CATEGORY_TO_SETTING.get(c)
        if sk is None:
            return True
        return read_log_setting(self._settings, sk)

    def _remove_compression_welcome_paragraph(self) -> None:
        """Remove the initial 'Successful compressions append here…' tip before the first real run."""
        if self._filter_needle() and self._tabs.currentIndex() == 4:
            return
        doc = self._history.document()
        blk = doc.firstBlock()
        if not blk.isValid():
            return
        t = blk.text()
        if "Successful compressions append" not in t and not t.strip().startswith("Successful compressions"):
            return
        cur = QTextCursor(blk)
        cur.select(QTextCursor.SelectionType.BlockUnderCursor)
        cur.removeSelectedText()
        if blk != doc.lastBlock():
            cur.deleteChar()
        self._history_html_backup = self._history.toHtml()
        if self._tabs.currentIndex() == 4 and self._filter_needle():
            self._render_history_filter_view()

    def _update_filter_error_label(self) -> None:
        t = self._filter_needle()
        if self._filter_regex.isChecked() and t:
            try:
                re.compile(t, 0 if self._filter_case.isChecked() else re.IGNORECASE)
                self._filter_regex_error.hide()
            except re.error:
                self._filter_regex_error.show()
                return
        self._filter_regex_error.hide()

    def _update_filter_controls_enabled(self) -> None:
        idx = self._tabs.currentIndex()
        self._filter_edit.setEnabled(True)
        self._filter_case.setEnabled(True)
        self._filter_regex.setEnabled(True)
        self._btn_clear_filter.setEnabled(True)
        if idx == 4:
            self._filter_edit.setPlaceholderText("Filter compression history (plain text)…")
        else:
            self._filter_edit.setPlaceholderText("Filter lines in current tab…")

    def _sync_follow_tail_checkbox(self) -> None:
        idx = self._tabs.currentIndex()
        self._follow_tail_cb.blockSignals(True)
        self._follow_tail_cb.setChecked(self._follow_tail[idx])
        self._follow_tail_cb.blockSignals(False)

    def _on_follow_tail_toggled(self, checked: bool) -> None:
        self._follow_tail[self._tabs.currentIndex()] = checked

    def _scroll_active_to_latest(self) -> None:
        idx = self._tabs.currentIndex()
        if 0 <= idx <= 3:
            w = self._plain_widget(idx)
            sb = w.verticalScrollBar()
            sb.setValue(sb.maximum())
        elif idx == 4:
            sb = self._history.verticalScrollBar()
            sb.setValue(sb.maximum())

    def _full_plain_lines(self, tab_idx: int) -> List[str]:
        return list(self._buffers[tab_idx])

    def _copy_last_n_lines(self) -> None:
        idx = self._tabs.currentIndex()
        n = int(self._spin_last_n.value())
        if 0 <= idx <= 3:
            lines = self._full_plain_lines(idx)
            chunk = lines[-n:] if n < len(lines) else lines
            QApplication.clipboard().setText("\n".join(chunk))
        else:
            lines = self._history.toPlainText().splitlines()
            chunk = lines[-n:] if n < len(lines) else lines
            QApplication.clipboard().setText("\n".join(chunk))

    def _drop_marker(self) -> None:
        idx = self._tabs.currentIndex()
        text, ok = QInputDialog.getText(self, "Session marker", "Label (optional):", text="")
        if not ok:
            return
        label = (text or "").strip() or "marker"
        ts = datetime.now().strftime("%H:%M:%S.%f")[:-3]
        if idx == 0:
            self.log_line(f"{MARK_PREFIX} {label}", "marker")
        elif 1 <= idx <= 3:
            self._push_line(idx, f"[{ts}] {MARK_PREFIX} {label}")
        else:
            esc = html.escape(label)
            self._append_history_plain(
                f"<p style='color:#888888;font-size:11px;'>━━ {MARK_PREFIX} {esc} ({ts}) ━━</p>"
            )

    def _copy_since_marker(self) -> None:
        idx = self._tabs.currentIndex()
        if 0 <= idx <= 3:
            lines = self._full_plain_lines(idx)
            cut = -1
            for i in range(len(lines) - 1, -1, -1):
                if MARK_PREFIX in lines[i]:
                    cut = i
                    break
            if cut < 0:
                QMessageBox.information(self, "Copy since marker", f"No {MARK_PREFIX} line found in this tab yet.")
                return
            QApplication.clipboard().setText("\n".join(lines[cut:]))
        else:
            plain = self._history.toPlainText().splitlines()
            cut = -1
            for i in range(len(plain) - 1, -1, -1):
                if MARK_PREFIX in plain[i]:
                    cut = i
                    break
            if cut < 0:
                QMessageBox.information(self, "Copy since marker", f"No {MARK_PREFIX} line found in history yet.")
                return
            QApplication.clipboard().setText("\n".join(plain[cut:]))

    def _restore_history_html_if_filtered(self) -> None:
        """If the history tab was showing a plain-text filter view, restore full HTML."""
        if self._filter_needle() and self._history_full_html_backup() is not None:
            self._history.setHtml(self._history_html_backup)
            self._filter_edit.blockSignals(True)
            self._filter_edit.clear()
            self._filter_edit.blockSignals(False)
            self._history_html_backup = self._history.toHtml()

    def apply_app_style(self) -> None:
        """Sync monitor chrome with ``StyleManager`` (call after ``MainWindow`` loads ``ui_theme``)."""
        sm = StyleManager.instance()
        self.setStyleSheet(sm.sandbox_monitor_window_qss())
        self._apply_panel_chrome()
        self._update_mirror_disk_tooltip()

    def _apply_panel_chrome(self) -> None:
        sm = StyleManager.instance()
        mh = sm.settings_muted_hint_color()
        hint_css = f"color: {mh}; font-size: 10px;"
        self._bottom_hint.setStyleSheet(hint_css)
        err_color = "#c72e2e" if not sm.sandbox_panel_is_bright() else "#b00020"
        ok_color = mh
        self._refresh_disk_errors_label()
        self._disk_errors_label.setStyleSheet(
            f"color: {err_color if self._disk_write_failures else ok_color}; font-size: 10px;"
        )

        if sm.sandbox_panel_is_bright():
            self._status.setStyleSheet(
                "QLabel { background-color: #ececf0; color: #141418; padding: 8px; "
                "border: 1px solid #c8c8d4; border-radius: 4px; font-size: 11px; }"
            )
            self._mirror_disk_cb.setStyleSheet("color: #2a2a32; font-size: 11px;")
            pte = (
                "QPlainTextEdit { background-color: #ffffff; color: #1a1a1e; "
                "border: 1px solid #c8c8d4; border-radius: 4px; }"
            )
            self._log.setStyleSheet(pte)
            self._save_fetch_per.setStyleSheet(pte)
            self._save_fetch_batches.setStyleSheet(pte)
            self._save_fetch_trace.setStyleSheet(pte)
            self._history.setStyleSheet(
                "QTextEdit { background-color: #eef8ef; color: #14532a; "
                "border: 1px solid #b8d4bc; border-radius: 4px; }"
            )
        else:
            self._status.setStyleSheet(
                "QLabel { background-color: #2d2d30; color: #e0e0e0; padding: 8px; "
                "border: 1px solid #3e3e42; border-radius: 4px; font-size: 11px; }"
            )
            self._mirror_disk_cb.setStyleSheet("color: #cccccc; font-size: 11px;")
            self._log.setStyleSheet(
                "QPlainTextEdit { background-color: #1e1e1e; color: #d4d4d4; "
                "border: 1px solid #3e3e42; border-radius: 4px; }"
            )
            self._save_fetch_per.setStyleSheet(
                "QPlainTextEdit { background-color: #1e1e24; color: #d4d4d4; "
                "border: 1px solid #3e3e42; border-radius: 4px; }"
            )
            self._save_fetch_batches.setStyleSheet(
                "QPlainTextEdit { background-color: #1e201e; color: #d4d4d4; "
                "border: 1px solid #3e3e42; border-radius: 4px; }"
            )
            self._save_fetch_trace.setStyleSheet(
                "QPlainTextEdit { background-color: #1a1a1e; color: #c5c5d0; "
                "border: 1px solid #3e3e42; border-radius: 4px; }"
            )
            self._history.setStyleSheet(
                "QTextEdit { background-color: #1a1f1a; color: #c8f0c8; "
                "border: 1px solid #3e3e42; border-radius: 4px; }"
            )

    def _on_bottom_clear_clicked(self) -> None:
        i = self._tabs.currentIndex()
        if 0 <= i < len(self._bottom_clear_handlers):
            self._bottom_clear_handlers[i]()

    def _on_bottom_copy_clicked(self) -> None:
        i = self._tabs.currentIndex()
        if 0 <= i < len(self._bottom_copy_handlers):
            self._bottom_copy_handlers[i]()

    def _on_sandbox_tab_changed(self, index: int) -> None:
        prev = self._last_tab_idx
        self._last_tab_idx = index
        if prev == 4 and index != 4:
            self._restore_history_html_if_filtered()

        self._sync_bottom_bar(index)
        if 0 <= index <= 3:
            self._render_plain_tab(index, scroll=self._follow_tail[index])
        self._sync_follow_tail_checkbox()
        self._update_filter_controls_enabled()
        if index == 4 and self._filter_needle():
            self._render_history_filter_view()

    def _update_mirror_disk_tooltip(self) -> None:
        path = str(log_file_path())
        self._mirror_disk_cb.setToolTip(
            "When checked, save-fetch lines are mirrored to the session log on disk.\n\n"
            f"Log file:\n{path}"
        )

    def _sync_bottom_bar(self, index: int) -> None:
        live = index == 0
        self._mirror_disk_cb.setVisible(live)
        self._btn_reset_disk.setVisible(live)
        hints = (
            "Shortcuts: Ctrl+A/C, arrows, Delete — mirror tooltip — Ctrl+F filter — Live log output… for categories",
            "Ctrl+A / Ctrl+C / arrows / Delete (selection)",
            "One block per wiki phase — Ctrl+A / Ctrl+C / arrows",
            "Per HTTP branch; SAVE_ON_DISK = found folder — Ctrl+A / Ctrl+C / arrows",
            "Ctrl+A / Ctrl+C / arrows — filter uses plain text of history",
        )
        clears = ("Clear log", "Clear", "Clear", "Clear", "Clear history")
        copys = ("Copy log to clipboard", "Copy", "Copy", "Copy", "Copy history")
        if 0 <= index < len(hints):
            self._bottom_hint.setText(hints[index])
            self._btn_clear.setText(clears[index])
            self._btn_copy.setText(copys[index])

    def _compression_history_intro_html(self) -> str:
        if StyleManager.instance().sandbox_panel_is_bright():
            return (
                "<p style='color:#2e6b38; font-size:11px;'>Successful compressions append here as "
                '<b style="color:#0d5016;">Test 1</b>, <b style="color:#0d5016;">Test 2</b>, '
                "… with full settings and sizes.</p>"
            )
        return (
            "<p style='color:#6b8f6b; font-size:11px;'>Successful compressions append here as "
            '<b style="color:#8fdf9a;">Test 1</b>, <b style="color:#8fdf9a;">Test 2</b>, '
            "… with full settings and sizes.</p>"
        )

    def _on_tick(self) -> None:
        s = snapshot(lite=True)
        base = format_snapshot_line(s)
        if self._disk_write_failures:
            base += f"  |  Disk mirror write errors: {self._disk_write_failures}"
        self._status.setText(base)
        self.refresh_show_main_button()

    def _mirror_fetch_to_disk(self, line: str) -> None:
        if not self._mirror_disk_cb.isChecked():
            return
        try:
            append_session_line(line)
        except OSError:
            self._disk_write_failures += 1
            self._refresh_disk_errors_label()
            self._apply_panel_chrome()
            if self._disk_write_failures <= 3 or self._disk_write_failures - self._disk_error_log_milestone >= 10:
                self._disk_error_log_milestone = self._disk_write_failures
                self.log_line(f"Disk mirror write failed ({self._disk_write_failures} total)", "warn")

    def _on_reset_disk_log(self) -> None:
        reset_session_log()
        self._update_mirror_disk_tooltip()
        self.log_line(f"Disk session log reset → {log_file_path()}", "info")

    def log_line(self, message: str, category: str = "info") -> None:
        if not self._log_line_allowed_with_message(category, message):
            return
        nc = self._normalize_log_category(category, message)
        msg = message
        if read_log_setting(self._settings, "show_compress_hw_inline") and nc in _COMPRESS_HW_SUFFIX_CATEGORIES:
            suf = format_inline_hw_snapshot(snapshot(lite=True))
            if suf:
                msg = f"{message}  |  {suf}"
        ts = datetime.now().strftime("%H:%M:%S.%f")[:-3]
        line = f"[{ts}] [{category}] {msg}"
        self._push_line(0, line)

    def clear_log(self) -> None:
        self._live_buf.clear()
        self.log_line("Log cleared.", "info")

    def _copy_log(self) -> None:
        QApplication.clipboard().setText("\n".join(self._live_buf))

    def _append_history_plain(self, html_fragment: str) -> None:
        if self._tabs.currentIndex() == 4 and self._filter_needle():
            hb = self._history_full_html_backup()
            if hb is not None:
                self._history.setHtml(hb)
        cur = self._history.textCursor()
        cur.movePosition(QTextCursor.MoveOperation.End)
        self._history.setTextCursor(cur)
        self._history.insertHtml(html_fragment)
        self._history_html_backup = self._history.toHtml()
        if self._tabs.currentIndex() == 4 and self._filter_needle():
            self._render_history_filter_view()
        if self._follow_tail[4]:
            sb = self._history.verticalScrollBar()
            sb.setValue(sb.maximum())

    def _fmt_compression_block(self, d: Dict[str, Any], test_n: int) -> str:
        base = html.escape(str(d.get("archive_basename", "—")))
        atype = html.escape(str(d.get("archive_type_display", "—")))
        lvl = html.escape(str(d.get("level_display", "—")))
        thr = html.escape(str(d.get("threads_display", "—")))
        wall = float(d.get("wall_sec", 0) or 0)
        sz_h = html.escape(str(d.get("archive_size_human", "—")))
        sz_b = int(d.get("archive_size_bytes", 0) or 0)
        raw_h = html.escape(str(d.get("raw_size_human", "—")))
        ratio = d.get("compression_ratio_pct", 0)
        files = int(d.get("files_total", d.get("files_total_ui", 0)) or 0)
        thr_mib = d.get("avg_throughput_mib_s", 0)
        sm = StyleManager.instance()
        if sm.sandbox_panel_is_bright():
            c_body, c_head, c_sub, c_hr = "#1b5e20", "#0d5016", "#2e6b38", "#b8d4bc"
        else:
            c_body, c_head, c_sub, c_hr = "#a8e6a8", "#8fdf9a", "#7a9a7a", "#3a4a3a"
        return (
            f'<div style="color:{c_body}; font-family: Consolas, \'Courier New\', monospace; '
            f'font-size: 11px; line-height: 1.45; margin: 12px 0 8px 0;">'
            f'<div style="color:{c_head}; font-weight: bold; margin-bottom: 6px;">━━ Test {test_n} ━━</div>'
            f"<b>File:</b> {base}<br/>"
            f"<b>Type:</b> {atype}<br/>"
            f"<b>Level:</b> {lvl} &nbsp;|&nbsp; <b>Threads:</b> {thr}<br/>"
            f"<b>Wall time:</b> {wall:.3f}s<br/>"
            f'<b>Archive size:</b> {sz_h} <span style="color:{c_sub};">({sz_b:,} bytes)</span><br/>'
            f"<b>Raw input:</b> {raw_h} &nbsp;|&nbsp; <b>Ratio:</b> {ratio}% of raw &nbsp;|&nbsp; "
            f"<b>Files:</b> {files}<br/>"
            f"<b>Mean throughput (input):</b> {thr_mib} MiB/s"
            f"</div>"
            f'<hr style="border:0; border-top:1px solid {c_hr}; margin: 0 0 4px 0;" />'
        )

    def append_compression_record(self, d: Optional[Dict[str, Any]]) -> None:
        """Append one green summary block (successful compression complete payload)."""
        if not isinstance(d, dict) or d.get("phase") != "complete":
            return
        if self._compression_intro_active:
            self._remove_compression_welcome_paragraph()
            self._compression_intro_active = False
        self._compression_test_n += 1
        self._append_history_plain(self._fmt_compression_block(d, self._compression_test_n))

    def clear_compression_history(self) -> None:
        self._history.clear()
        self._history_html_backup = None
        self._compression_test_n = 0
        sm = StyleManager.instance()
        if sm.sandbox_panel_is_bright():
            msg = (
                "<p style='color:#2e6b38;'>History cleared. New runs will be numbered "
                "Test 1, Test 2, …</p>"
            )
        else:
            msg = "<p style='color:#6b8f6b;'>History cleared. New runs will be numbered Test 1, Test 2, …</p>"
        self._append_history_plain(msg)

    def _copy_history(self) -> None:
        QApplication.clipboard().setText(self._history.toPlainText())

    def append_save_fetch_per_game(self, d: Dict[str, Any]) -> None:
        """Append one line: wall time, HTTP call count, found?, source, game name."""
        if not isinstance(d, dict):
            return
        ts = datetime.now().strftime("%H:%M:%S.%f")[:-3]
        g = str(d.get("game", "?"))[:60]
        wall = float(d.get("wall_sec", 0) or 0)
        found = "FOUND" if d.get("found") else "—"
        src = str(d.get("source", "?"))[:12]
        http = int(d.get("http_calls", 0) or 0)
        wo = str(d.get("wiki_outcome", "?"))[:16]
        line = f"[{ts}] {wall:7.3f}s | HTTP {http:2d} | {wo:16} | {found:5} | {src:12} | {g}"
        self._push_line(1, line)
        self._mirror_fetch_to_disk(f"[games] {line}")

    def clear_save_fetch_games(self) -> None:
        self._games_buf.clear()
        self._save_fetch_per.clear()

    def _copy_save_fetch_games(self) -> None:
        QApplication.clipboard().setText("\n".join(self._games_buf))

    def append_save_fetch_batch_header(self, game_count: int) -> None:
        ts = datetime.now().strftime("%H:%M:%S.%f")[:-3]
        bline = f"\n[{ts}] === Batch start | {game_count} game(s) ===\n"
        for ln in bline.splitlines():
            if ln.strip():
                self._push_line(2, ln)
        self._mirror_fetch_to_disk(f"[batch]{bline.rstrip()}")
        tline = f"\n{'='*72}\n[{ts}] BATCH {game_count} game(s) — detailed trace\n"
        for ln in tline.splitlines():
            if ln.strip():
                self._push_line(3, ln)
        self._mirror_fetch_to_disk(f"[trace]{tline.rstrip()}")

    def append_save_fetch_batch_footer(self, games_done: int, wall_sec: float) -> None:
        ts = datetime.now().strftime("%H:%M:%S.%f")[:-3]
        avg = wall_sec / max(1, games_done)
        fline = (
            f"[{ts}] === Batch end | {games_done} game(s) finished | {wall_sec:.2f}s wall | {avg:.3f}s/game avg ===\n"
        )
        for ln in fline.splitlines():
            if ln.strip():
                self._push_line(2, ln)
        self._mirror_fetch_to_disk(f"[batch]{fline.rstrip()}")

    def append_save_fetch_trace(self, line: str) -> None:
        for ln in line.splitlines():
            if ln.strip():
                self._push_line(3, ln)
        self._mirror_fetch_to_disk(f"[trace] {line}")

    def clear_save_fetch_trace(self) -> None:
        self._trace_buf.clear()
        for intro in self._TRACE_INTRO_LINES:
            self._trace_buf.append(intro)
        self._save_fetch_trace.setPlainText(self._display_lines_for_tab(3))

    def _copy_save_fetch_trace(self) -> None:
        QApplication.clipboard().setText("\n".join(self._trace_buf))

    def clear_save_fetch_batches(self) -> None:
        self._batch_buf.clear()
        self._save_fetch_batches.clear()

    def _copy_save_fetch_batches(self) -> None:
        QApplication.clipboard().setText("\n".join(self._batch_buf))

    def closeEvent(self, event):
        """Closing the monitor does not quit the main window; remember position for next run."""
        self._settings.setValue("sandbox_monitor_geometry", self.saveGeometry())
        self._settings.sync()
        event.accept()
