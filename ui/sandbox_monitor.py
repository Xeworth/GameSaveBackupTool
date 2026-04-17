"""
Sandbox admin monitor: live system metrics + append-only event log.

Shown when the app runs with ``--sandbox`` / ``-s`` (``GSBT_SANDBOX``).
"""

from __future__ import annotations

import html
from datetime import datetime
from typing import Any, Dict, Optional

from PyQt6.QtCore import Qt, QTimer
from PyQt6.QtGui import QFont, QTextCursor
from PyQt6.QtWidgets import (
    QCheckBox,
    QHBoxLayout,
    QLabel,
    QMainWindow,
    QPlainTextEdit,
    QPushButton,
    QTabWidget,
    QTextEdit,
    QVBoxLayout,
    QWidget,
)

from styles.manager import StyleManager
from utils.session_debug_log import append_session_line, log_file_path, reset_session_log
from utils.system_metrics import format_snapshot_line, snapshot


class SandboxMonitorWindow(QMainWindow):
    """Second window for benchmarks: CPU/RAM strip + live log + compression history tab."""

    MAX_LOG_BLOCKS = 2500

    def __init__(self, parent=None):
        super().__init__(parent)
        self.setWindowTitle("GSBT Sandbox Monitor")
        self.resize(780, 520)
        self.setMinimumSize(480, 320)
        self._compression_test_n = 0

        central = QWidget()
        self.setCentralWidget(central)
        layout = QVBoxLayout(central)
        layout.setContentsMargins(8, 8, 8, 8)
        layout.setSpacing(6)

        self._status = QLabel()
        self._status.setWordWrap(True)
        self._status.setTextInteractionFlags(Qt.TextInteractionFlag.TextSelectableByMouse)
        layout.addWidget(self._status)

        self._tabs = QTabWidget()
        layout.addWidget(self._tabs, 1)

        # --- Tab: Live log ---
        live = QWidget()
        live_layout = QVBoxLayout(live)
        live_layout.setContentsMargins(0, 0, 0, 0)
        row = QHBoxLayout()
        self._btn_clear = QPushButton("Clear log")
        self._btn_clear.clicked.connect(self.clear_log)
        self._btn_copy = QPushButton("Copy log to clipboard")
        self._btn_copy.clicked.connect(self._copy_log)
        row.addWidget(self._btn_clear)
        row.addWidget(self._btn_copy)
        row.addStretch(1)
        self._hint_live = QLabel("Shortcuts: Ctrl+A select all, Ctrl+C copy")
        row.addWidget(self._hint_live)
        live_layout.addLayout(row)

        disk_row = QHBoxLayout()
        self._mirror_disk_cb = QCheckBox("Mirror save-fetch tabs to disk log")
        self._mirror_disk_cb.setChecked(True)
        self._btn_reset_disk = QPushButton("New disk log…")
        self._btn_reset_disk.setToolTip(f"Truncate and start a fresh file:\n{log_file_path()}")
        self._btn_reset_disk.clicked.connect(self._on_reset_disk_log)
        disk_row.addWidget(self._mirror_disk_cb)
        disk_row.addWidget(self._btn_reset_disk)
        disk_row.addStretch(1)
        self._disk_path_label = QLabel(str(log_file_path()))
        self._disk_path_label.setWordWrap(True)
        self._disk_path_label.setTextInteractionFlags(Qt.TextInteractionFlag.TextSelectableByMouse)
        disk_row.addWidget(self._disk_path_label, 1)
        live_layout.addLayout(disk_row)

        self._log = QPlainTextEdit()
        self._log.setReadOnly(True)
        self._log.setFont(QFont("Consolas", 10))
        if not self._log.font().family():
            self._log.setFont(QFont("Courier New", 10))
        live_layout.addWidget(self._log, 1)
        self._tabs.addTab(live, "Live log")

        # --- Tab: Save fetch — one line per game (PCGW / path resolve) ---
        sf_games = QWidget()
        sf_games_layout = QVBoxLayout(sf_games)
        sf_games_layout.setContentsMargins(0, 0, 0, 0)
        sfg_row = QHBoxLayout()
        self._btn_clear_sf_games = QPushButton("Clear")
        self._btn_clear_sf_games.clicked.connect(self.clear_save_fetch_games)
        self._btn_copy_sf_games = QPushButton("Copy")
        self._btn_copy_sf_games.clicked.connect(self._copy_save_fetch_games)
        sfg_row.addWidget(self._btn_clear_sf_games)
        sfg_row.addWidget(self._btn_copy_sf_games)
        sfg_row.addStretch(1)
        self._hint_sfg = QLabel("Ctrl+A / Ctrl+C")
        sfg_row.addWidget(self._hint_sfg)
        sf_games_layout.addLayout(sfg_row)
        self._save_fetch_per = QPlainTextEdit()
        self._save_fetch_per.setReadOnly(True)
        self._save_fetch_per.setFont(QFont("Consolas", 10))
        if not self._save_fetch_per.font().family():
            self._save_fetch_per.setFont(QFont("Courier New", 10))
        sf_games_layout.addWidget(self._save_fetch_per, 1)
        self._tabs.addTab(sf_games, "Save fetch (games)")

        # --- Tab: Save fetch — batch summaries ---
        sf_batch = QWidget()
        sf_batch_layout = QVBoxLayout(sf_batch)
        sf_batch_layout.setContentsMargins(0, 0, 0, 0)
        sfb_row = QHBoxLayout()
        self._btn_clear_sf_batch = QPushButton("Clear")
        self._btn_clear_sf_batch.clicked.connect(self.clear_save_fetch_batches)
        self._btn_copy_sf_batch = QPushButton("Copy")
        self._btn_copy_sf_batch.clicked.connect(self._copy_save_fetch_batches)
        sfb_row.addWidget(self._btn_clear_sf_batch)
        sfb_row.addWidget(self._btn_copy_sf_batch)
        sfb_row.addStretch(1)
        self._hint_sfb = QLabel("One block per wiki phase")
        sfb_row.addWidget(self._hint_sfb)
        sf_batch_layout.addLayout(sfb_row)
        self._save_fetch_batches = QPlainTextEdit()
        self._save_fetch_batches.setReadOnly(True)
        self._save_fetch_batches.setFont(QFont("Consolas", 10))
        if not self._save_fetch_batches.font().family():
            self._save_fetch_batches.setFont(QFont("Courier New", 10))
        sf_batch_layout.addWidget(self._save_fetch_batches, 1)
        self._tabs.addTab(sf_batch, "Save fetch (batches)")

        # --- Tab: Save fetch — fine-grained trace (timestamps + outcome hints) ---
        sf_trace = QWidget()
        sf_trace_layout = QVBoxLayout(sf_trace)
        sf_trace_layout.setContentsMargins(0, 0, 0, 0)
        sft_row = QHBoxLayout()
        self._btn_clear_sf_trace = QPushButton("Clear")
        self._btn_clear_sf_trace.clicked.connect(self.clear_save_fetch_trace)
        self._btn_copy_sf_trace = QPushButton("Copy")
        self._btn_copy_sf_trace.clicked.connect(self._copy_save_fetch_trace)
        sft_row.addWidget(self._btn_clear_sf_trace)
        sft_row.addWidget(self._btn_copy_sf_trace)
        sft_row.addStretch(1)
        self._hint_sft = QLabel("Per HTTP branch; SAVE_ON_DISK = found folder")
        sft_row.addWidget(self._hint_sft)
        sf_trace_layout.addLayout(sft_row)
        self._save_fetch_trace = QPlainTextEdit()
        self._save_fetch_trace.setReadOnly(True)
        self._save_fetch_trace.setFont(QFont("Consolas", 9))
        if not self._save_fetch_trace.font().family():
            self._save_fetch_trace.setFont(QFont("Courier New", 9))
        sf_trace_layout.addWidget(self._save_fetch_trace, 1)
        self._tabs.addTab(sf_trace, "Save fetch (trace)")
        self._save_fetch_trace.appendPlainText(
            "Legend (per-game END line): SAVE_ON_DISK | WIKI_PATHS_NO_DISK (wiki had strings, not on disk) | "
            "NO_WIKI_DATA\n"
            "Inline: semantic PATH STRINGS = good cells | semantic empty / NO save path props = dead end | "
            "HTML fallback = slower second pass\n"
        )

        # --- Tab: Compression history ---
        hist = QWidget()
        hist_layout = QVBoxLayout(hist)
        hist_layout.setContentsMargins(0, 0, 0, 0)
        hrow = QHBoxLayout()
        self._btn_clear_hist = QPushButton("Clear history")
        self._btn_clear_hist.clicked.connect(self.clear_compression_history)
        self._btn_copy_hist = QPushButton("Copy history")
        self._btn_copy_hist.clicked.connect(self._copy_history)
        hrow.addWidget(self._btn_clear_hist)
        hrow.addWidget(self._btn_copy_hist)
        hrow.addStretch(1)
        self._hint_hist = QLabel("Ctrl+A / Ctrl+C in history")
        hrow.addWidget(self._hint_hist)
        hist_layout.addLayout(hrow)

        self._history = QTextEdit()
        self._history.setReadOnly(True)
        self._history.setAcceptRichText(True)
        self._history.setFont(QFont("Consolas", 10))
        if not self._history.font().family():
            self._history.setFont(QFont("Courier New", 10))
        hist_layout.addWidget(self._history, 1)
        self._tabs.addTab(hist, "Compression history")

        self._timer = QTimer(self)
        self._timer.timeout.connect(self._on_tick)
        self._timer.start(500)
        self._on_tick()

        self.log_line("Sandbox monitor ready. Fresh QSettings scope; use this log for compression & scan timings.")
        self.log_line("Tip: install psutil for live CPU/RAM (pip install psutil).")
        self._save_fetch_batches.appendPlainText(
            "Save fetch (batches): each scan’s PCGW phase logs a header when fetch starts and a footer when all games finish.\n"
        )
        self._append_history_plain(self._compression_history_intro_html())

        self.apply_app_style()

    def apply_app_style(self) -> None:
        """Sync monitor chrome with ``StyleManager`` (call after ``MainWindow`` loads ``ui_theme``)."""
        sm = StyleManager.instance()
        self.setStyleSheet(sm.sandbox_monitor_window_qss())
        self._apply_panel_chrome()

    def _apply_panel_chrome(self) -> None:
        sm = StyleManager.instance()
        mh = sm.settings_muted_hint_color()
        hint_css = f"color: {mh}; font-size: 10px;"
        for w in (
            getattr(self, "_hint_live", None),
            getattr(self, "_hint_sfg", None),
            getattr(self, "_hint_sfb", None),
            getattr(self, "_hint_sft", None),
            getattr(self, "_hint_hist", None),
        ):
            if w is not None:
                w.setStyleSheet(hint_css)

        if sm.sandbox_panel_is_bright():
            self._status.setStyleSheet(
                "QLabel { background-color: #ececf0; color: #141418; padding: 8px; "
                "border: 1px solid #c8c8d4; border-radius: 4px; font-size: 11px; }"
            )
            self._mirror_disk_cb.setStyleSheet("color: #2a2a32; font-size: 11px;")
            self._disk_path_label.setStyleSheet("color: #6a6a78; font-size: 9px;")
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
            self._disk_path_label.setStyleSheet("color: #666666; font-size: 9px;")
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
        s = snapshot()
        self._status.setText(format_snapshot_line(s))

    def _mirror_fetch_to_disk(self, line: str) -> None:
        if not self._mirror_disk_cb.isChecked():
            return
        try:
            append_session_line(line)
        except OSError:
            pass

    def _on_reset_disk_log(self) -> None:
        reset_session_log()
        self.log_line(f"Disk session log reset → {log_file_path()}", "info")

    def log_line(self, message: str, category: str = "info") -> None:
        ts = datetime.now().strftime("%H:%M:%S.%f")[:-3]
        line = f"[{ts}] [{category}] {message}"
        self._log.appendPlainText(line)
        self._trim_log()
        sb = self._log.verticalScrollBar()
        sb.setValue(sb.maximum())

    def _trim_log(self) -> None:
        doc = self._log.document()
        while doc.blockCount() > self.MAX_LOG_BLOCKS:
            blk = doc.findBlockByNumber(0)
            cur = QTextCursor(blk)
            cur.select(QTextCursor.SelectionType.BlockUnderCursor)
            cur.removeSelectedText()
            cur.deleteChar()

    def clear_log(self) -> None:
        self._log.clear()
        self.log_line("Log cleared.", "info")

    def _copy_log(self) -> None:
        from PyQt6.QtWidgets import QApplication

        QApplication.clipboard().setText(self._log.toPlainText())

    def _append_history_plain(self, html_fragment: str) -> None:
        cur = self._history.textCursor()
        cur.movePosition(QTextCursor.MoveOperation.End)
        self._history.setTextCursor(cur)
        self._history.insertHtml(html_fragment)
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
        self._compression_test_n += 1
        self._append_history_plain(self._fmt_compression_block(d, self._compression_test_n))

    def clear_compression_history(self) -> None:
        self._history.clear()
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
        from PyQt6.QtWidgets import QApplication

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
        self._save_fetch_per.appendPlainText(line)
        self._mirror_fetch_to_disk(f"[games] {line}")
        sb = self._save_fetch_per.verticalScrollBar()
        sb.setValue(sb.maximum())

    def clear_save_fetch_games(self) -> None:
        self._save_fetch_per.clear()

    def _copy_save_fetch_games(self) -> None:
        from PyQt6.QtWidgets import QApplication

        QApplication.clipboard().setText(self._save_fetch_per.toPlainText())

    def append_save_fetch_batch_header(self, game_count: int) -> None:
        ts = datetime.now().strftime("%H:%M:%S.%f")[:-3]
        bline = f"\n[{ts}] === Batch start | {game_count} game(s) ===\n"
        self._save_fetch_batches.appendPlainText(bline)
        self._mirror_fetch_to_disk(f"[batch]{bline.rstrip()}")
        sb = self._save_fetch_batches.verticalScrollBar()
        sb.setValue(sb.maximum())
        tline = f"\n{'='*72}\n[{ts}] BATCH {game_count} game(s) — detailed trace\n"
        self._save_fetch_trace.appendPlainText(tline)
        self._mirror_fetch_to_disk(f"[trace]{tline.rstrip()}")
        sb2 = self._save_fetch_trace.verticalScrollBar()
        sb2.setValue(sb2.maximum())

    def append_save_fetch_batch_footer(self, games_done: int, wall_sec: float) -> None:
        ts = datetime.now().strftime("%H:%M:%S.%f")[:-3]
        avg = wall_sec / max(1, games_done)
        fline = (
            f"[{ts}] === Batch end | {games_done} game(s) finished | {wall_sec:.2f}s wall | {avg:.3f}s/game avg ===\n"
        )
        self._save_fetch_batches.appendPlainText(fline)
        self._mirror_fetch_to_disk(f"[batch]{fline.rstrip()}")
        sb = self._save_fetch_batches.verticalScrollBar()
        sb.setValue(sb.maximum())

    def append_save_fetch_trace(self, line: str) -> None:
        self._save_fetch_trace.appendPlainText(line)
        self._mirror_fetch_to_disk(f"[trace] {line}")
        sb = self._save_fetch_trace.verticalScrollBar()
        sb.setValue(sb.maximum())

    def clear_save_fetch_trace(self) -> None:
        self._save_fetch_trace.clear()

    def _copy_save_fetch_trace(self) -> None:
        from PyQt6.QtWidgets import QApplication

        QApplication.clipboard().setText(self._save_fetch_trace.toPlainText())

    def clear_save_fetch_batches(self) -> None:
        self._save_fetch_batches.clear()

    def _copy_save_fetch_batches(self) -> None:
        from PyQt6.QtWidgets import QApplication

        QApplication.clipboard().setText(self._save_fetch_batches.toPlainText())

    def closeEvent(self, event):
        """Closing the monitor does not quit the main window."""
        event.accept()
