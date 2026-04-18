from PyQt6.QtWidgets import (
    QMainWindow, QWidget, QVBoxLayout, QHBoxLayout, QLabel, QPushButton,
    QTableWidget, QTableWidgetItem, QAbstractItemView, QHeaderView,
    QMessageBox, QFileDialog, QProgressBar, QProgressDialog, QSystemTrayIcon, QMenu, QCheckBox,
    QApplication, QDialog, QGraphicsDropShadowEffect, QStyledItemDelegate, QGraphicsOpacityEffect,
    QStyle, QStyleOptionButton, QToolButton, QWidgetAction,
)
from PyQt6.QtCore import Qt, pyqtSignal, QSettings, QThreadPool, QTimer, QPropertyAnimation, QEasingCurve, pyqtProperty, QPoint, QRect, QSize
from PyQt6.QtGui import QIcon, QAction, QPalette, QColor, QPainter, QLinearGradient, QPen, QBrush, QPainterPath, QPainterPath, QKeySequence, QShortcut, QCursor, QGuiApplication
import sys
import os
import json
from datetime import datetime
import winreg
import time

from core.registry_save_resolver import format_registry_save_display

# --- UI helper widgets ------------------------------------------------------


class ToolsMenuButton(QPushButton):
    """Single 'Tools' button: small up-triangle to the right of the label, top-floating (menu opens above)."""

    def paintEvent(self, event) -> None:
        super().paintEvent(event)
        if not self.isEnabled():
            return
        opt = QStyleOptionButton()
        self.initStyleOption(opt)
        style = self.style()
        r = style.subElementRect(QStyle.SubElement.SE_PushButtonContents, opt, self)
        if r.width() < 4 or r.height() < 4:
            return
        fm = self.fontMetrics()
        text = self.text()
        tw = fm.horizontalAdvance(text)
        cell = QRect(
            int(r.left() + (r.width() - tw) // 2),
            int(r.top()),
            tw,
            int(r.height()),
        )
        br = fm.boundingRect(
            cell,
            int(Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter),
            text,
        )
        gap = 3
        tri_w = 5.0
        tri_h = 4.0
        left = float(br.right() + gap)
        tip_x = left + tri_w * 0.5
        tip_y = float(br.top() + 1)
        p = QPainter(self)
        p.setRenderHint(QPainter.RenderHint.Antialiasing, True)
        p.setPen(Qt.PenStyle.NoPen)
        cg = QPalette.ColorGroup.Disabled if not self.isEnabled() else QPalette.ColorGroup.Active
        p.setBrush(self.palette().brush(cg, QPalette.ColorRole.ButtonText))
        path = QPainterPath()
        path.moveTo(tip_x, tip_y)
        path.lineTo(left, tip_y + tri_h)
        path.lineTo(left + tri_w, tip_y + tri_h)
        path.closeSubpath()
        p.drawPath(path)


# Windows Toast notification support
try:
    from winotify import Notification, audio
    WINDOWS_TOAST_AVAILABLE = True
except ImportError:
    WINDOWS_TOAST_AVAILABLE = False
    audio = None

from core.compression import options_from_qsettings
from core.game_catalog_io import (
    export_catalog_csv,
    export_catalog_json,
    import_catalog_csv,
    import_catalog_json,
)
from core.game_detector import GameDetector
from core.save_manager import SaveManager
from styles.manager import StyleManager
from ui.custom_dialogs import AddCustomGameDialog, SettingsDialog, FirstBackupDestinationDialog
from ui.backup_estimate_dialog import BackupEstimatePromptDialog
from ui.health_dialog import HealthInfoDialog
from ui.about_dialog import AboutDialog
from ui.shortcuts_dialog import ShortcutsDialog
try:
    from config.sandbox_log_prefs import read_log_setting
except ImportError:

    def read_log_setting(_settings, key: str) -> bool:  # noqa: ARG001
        return False if key == "show_compress_tick_notes" else True
from ui.workers import (
    AutoBackupWorker,
    BackupEstimateWorker,
    BackupWorker,
    CompressBackupWorker,
    GameDetectorWorker,
    Observer,
    SaveFileEventHandler,
    SaveLocationFetcherWorker,
    WATCHDOG_AVAILABLE,
)

# --- Custom Progress Bar (accent gradient + glow) ---
PROGRESS_BAR_FADE_DELAY_MS = 2000  # Fade out after completion (scan/compress); single shared behaviour

class PurpleProgressBar(QProgressBar):
    """Progress bar for scan and compress; fill and glow follow the Windows accent color."""

    def __init__(self, parent=None, accent_color=None):
        super().__init__(parent)
        self._accent = QColor(accent_color) if accent_color is not None else QColor(148, 0, 211)
        self._track = QColor(26, 26, 26)
        self.setFixedHeight(3)
        self.setTextVisible(False)
        self.setMaximumHeight(3)
        self.setMinimumHeight(3)

        self.glow_effect = QGraphicsDropShadowEffect()
        self.glow_effect.setBlurRadius(10)
        self.glow_effect.setOffset(0, 0)
        self._apply_accent_glow()
        self.setGraphicsEffect(self.glow_effect)
        
        # Internal opacity for fade-out animation
        self._opacity = 1.0

        # Auto-hide timer (e.g. hide 10 seconds after completion)
        self._auto_hide_timer = QTimer(self)
        self._auto_hide_timer.setSingleShot(True)
        self._auto_hide_timer.timeout.connect(self._start_fade_out)
        
        # Animation for show/hide
        self.show_animation = QPropertyAnimation(self, b"maximumHeight")
        self.show_animation.setDuration(200)
        self.show_animation.setEasingCurve(QEasingCurve.Type.OutCubic)
        self.show_animation.setStartValue(0)
        self.show_animation.setEndValue(3)
        
        self.hide_animation = QPropertyAnimation(self, b"maximumHeight")
        self.hide_animation.setDuration(200)
        self.hide_animation.setEasingCurve(QEasingCurve.Type.InCubic)
        self.hide_animation.setStartValue(3)
        self.hide_animation.setEndValue(0)
        self.hide_animation.finished.connect(self._on_hide_animation_finished)

        # Fade-out animation (opacity)
        self.fade_animation = QPropertyAnimation(self, b"barOpacity")
        self.fade_animation.setDuration(300)
        self.fade_animation.setEasingCurve(QEasingCurve.Type.InOutCubic)
        self.fade_animation.finished.connect(self._on_fade_finished)

    def set_accent_color(self, color: QColor) -> None:
        self._accent = QColor(color)
        self._apply_accent_glow()
        self.update()

    def set_track_color(self, color: QColor) -> None:
        self._track = QColor(color)
        self.update()

    def _apply_accent_glow(self) -> None:
        c = QColor(self._accent)
        c.setAlpha(120)
        self.glow_effect.setColor(c)

    def _on_hide_animation_finished(self):
        """Called when hide animation completes"""
        self.setMaximumHeight(0)
        self.setVisible(False)

    def getBarOpacity(self):
        return self._opacity

    def setBarOpacity(self, value):
        self._opacity = float(value)
        self.update()

    barOpacity = pyqtProperty(float, getBarOpacity, setBarOpacity)

    def animated_show(self):
        """Animate showing the progress bar"""
        self._opacity = 1.0  # Reset opacity so bar is visible after a previous fade
        self.setVisible(True)
        self.setMaximumHeight(0)
        self.show_animation.start()
        # Update position in container if parent exists
        if self.parent() and hasattr(self.parent(), '_update_progress_bar_position'):
            # Update immediately and after animation
            self.parent()._update_progress_bar_position()
            QTimer.singleShot(50, self.parent()._update_progress_bar_position)
            QTimer.singleShot(250, self.parent()._update_progress_bar_position)  # After animation completes

    def animated_hide(self):
        """Animate hiding the progress bar"""
        self.hide_animation.start()
        # Update position in container if parent exists
        if self.parent() and hasattr(self.parent(), '_update_progress_bar_position'):
            QTimer.singleShot(50, self.parent()._update_progress_bar_position)

    def _start_fade_out(self):
        """Start fade-out animation before hiding the bar."""
        self.fade_animation.stop()
        self.fade_animation.setStartValue(1.0)
        self.fade_animation.setEndValue(0.0)
        self.fade_animation.start()

    def _on_fade_finished(self):
        """Finalize fade-out by hiding the bar without extra flicker."""
        # Keep the opacity at 0 (fully faded), then collapse and hide.
        self._opacity = 0.0
        self.setMaximumHeight(0)
        self.setVisible(False)
        # Let the container recompute geometry once after we hide.
        if self.parent() and hasattr(self.parent(), '_update_progress_bar_position'):
            QTimer.singleShot(50, self.parent()._update_progress_bar_position)
    
    def setValue(self, value):
        """Override to show/hide based on value"""
        super().setValue(value)
        # Show when we start making progress
        if value > 0 and not self.isVisible():
            self.animated_show()
        # When we reach or exceed maximum, schedule fade-out (shared behaviour for scan and compress)
        if self.maximum() > 0 and value >= self.maximum():
            self._auto_hide_timer.stop()
            self._auto_hide_timer.start(PROGRESS_BAR_FADE_DELAY_MS)
    
    def paintEvent(self, event):
        """Accent-colored horizontal gradient on the progress chunk (same geometry as before)."""
        if self.maximumHeight() == 0:
            return

        painter = QPainter(self)
        painter.setRenderHint(QPainter.RenderHint.Antialiasing)
        painter.setOpacity(self._opacity)

        bg_rect = self.rect()
        painter.fillRect(bg_rect, self._track)

        if self.maximum() > 0 and self.value() > 0:
            progress_width = int((self.value() / self.maximum()) * bg_rect.width())
            if progress_width > 0:
                progress_rect = bg_rect.adjusted(0, 0, progress_width - bg_rect.width(), 0)
                gradient = QLinearGradient(progress_rect.left(), 0, progress_rect.right(), 0)
                base = self._accent
                gradient.setColorAt(0.0, QColor(base).lighter(108))
                gradient.setColorAt(0.5, base)
                gradient.setColorAt(1.0, QColor(base).lighter(125))
                painter.fillRect(progress_rect, QBrush(gradient))

        painter.end()


# --- Small subclass for Scan button so its menu can pop above with an up-arrow look ---
class ScanToolButton(QToolButton):
    def showMenu(self):
        """Override to show the menu above the button instead of below."""
        menu = self.menu()
        if not menu:
            return
        # Position menu so its bottom-right aligns with the button's top-right
        global_top_right = self.mapToGlobal(self.rect().topRight())
        menu_size = menu.sizeHint()
        pos = QPoint(global_top_right.x() - menu_size.width(), global_top_right.y() - menu_size.height())
        menu.popup(pos)


# --- Custom Item Delegate for Fade-in Animation and selection (accent) ---
class FadeInItemDelegate(QStyledItemDelegate):
    """Fade-in row opacity; selection fill uses Windows accent (same inset layout as before)."""

    def __init__(self, accent_color: QColor, parent=None):
        super().__init__(parent)
        self._accent = QColor(accent_color)

    def set_accent_color(self, color: QColor) -> None:
        self._accent = QColor(color)

    def paint(self, painter, option, index):
        # Check if item has opacity data
        opacity = index.data(Qt.ItemDataRole.UserRole + 10)
        if opacity is not None and opacity < 1.0:
            painter.setOpacity(opacity)
        
        # Check if item is selected
        from PyQt6.QtWidgets import QStyle
        is_selected = option.state & QStyle.StateFlag.State_Selected
        
        # Get column index
        col = index.column()
        model = index.model()
        total_cols = model.columnCount() if model else 4
        
        if is_selected:
            bg_color = QColor(self._accent)
            bg_color.setAlpha(int(0.25 * 255))
            if option.state & QStyle.StateFlag.State_Active:
                bg_color = QColor(self._accent)
                bg_color.setAlpha(int(0.35 * 255))
            elif option.state & QStyle.StateFlag.State_MouseOver:
                bg_color = QColor(self._accent)
                bg_color.setAlpha(int(0.3 * 255))
            
            # Set up margins - same on all sides for consistent inset
            margin_y = 4  # Vertical margin
            margin_x_left = 5 if col == 0 else 0  # Left margin only for first column
            margin_x_right = 5 if col == total_cols - 1 else 0  # Right margin only for last column
            
            # Adjust rect with margins
            rounded_rect = option.rect.adjusted(margin_x_left, margin_y, -margin_x_right, -margin_y)
            
            # Draw simple rectangle with inset margins (no rounding)
            painter.setRenderHint(QPainter.RenderHint.Antialiasing)
            painter.setBrush(QBrush(bg_color))
            painter.setPen(Qt.PenStyle.NoPen)
            painter.fillRect(rounded_rect, bg_color)
        
        # Draw the item content (text, icon, etc.)
        super().paint(painter, option, index)
        painter.setOpacity(1.0)  # Reset opacity
    


# --- Custom Table Widget Container with Embedded Progress Bar ---
class TableWithProgressBar(QWidget):
    """Container widget that embeds a progress bar at the bottom of the table"""
    def __init__(self, parent=None):
        super().__init__(parent)
        self.layout = QVBoxLayout(self)
        self.layout.setContentsMargins(0, 0, 0, 0)
        self.layout.setSpacing(0)
        
        # Create table widget
        self.table = GameTableWidget()
        # Remove any padding/margins from table widget
        self.table.setContentsMargins(0, 0, 0, 0)
        
        # Create progress bar - will be positioned absolutely at bottom
        self.progress_bar = PurpleProgressBar()
        self.progress_bar.setMaximumHeight(0)  # Start with 0 height
        self.progress_bar.setValue(0)
        self.progress_bar.setMaximum(100)
        self.progress_bar.setParent(self)  # Make it a child of this widget
        self.progress_bar.hide()  # Start hidden
        self.progress_bar.raise_()  # Raise above table widget
        
        # Add table to layout (fills the container)
        self.layout.addWidget(self.table)
        
        # Initial position update
        QTimer.singleShot(100, self._update_progress_bar_position)
    
    def resizeEvent(self, event):
        """Update progress bar position when container is resized"""
        super().resizeEvent(event)
        self._update_progress_bar_position()
    
    def _update_progress_bar_position(self):
        """Update progress bar position at bottom edge"""
        if self.progress_bar:
            bar_height = 3 if self.progress_bar.maximumHeight() > 0 else 0
            # Position at bottom edge, accounting for table's border
            margin = 1  # Small margin to align with table's inner edge
            self.progress_bar.setGeometry(
                margin,  # Left edge with small margin
                self.height() - bar_height - margin,  # Bottom edge with margin
                self.width() - (margin * 2),  # Width minus margins
                bar_height  # Height
            )
            # Make sure progress bar is shown if it has height and raised above table
            if bar_height > 0:
                self.progress_bar.show()
                self.progress_bar.raise_()  # Ensure it's above the table
            elif self.progress_bar.maximumHeight() == 0:
                self.progress_bar.hide()
    
    def get_table(self):
        """Get the table widget"""
        return self.table
    
    def get_progress_bar(self):
        """Get the progress bar"""
        return self.progress_bar

    def set_accent_color(self, color: QColor) -> None:
        self.table.set_accent_color(color)
        self.progress_bar.set_accent_color(color)


# --- Custom Table Widget to handle key presses ---
class GameTableWidget(QTableWidget):
    delete_pressed = pyqtSignal()
    undo_pressed = pyqtSignal()

    def __init__(self, parent=None):
        super().__init__(parent)
        self._accent_color = QColor(148, 0, 211)
        self._fade_delegate = FadeInItemDelegate(self._accent_color, self)
        self.setItemDelegate(self._fade_delegate)
        self.fixed_columns_width = 480  # Platform (140) + Save Status (140) + Last Backup (200)
        self.last_backup_fixed_width = 200
        self._is_resizing = False  # Flag to prevent recursion

    def set_accent_color(self, color: QColor) -> None:
        self._accent_color = QColor(color)
        self._fade_delegate.set_accent_color(self._accent_color)

    def keyPressEvent(self, event):
        # Global-style shortcuts within the table:
        # - Delete: delete selected rows
        # - Ctrl+A: select all rows
        # - Ctrl+Z: undo last delete (handled by MainWindow)
        if event.matches(QKeySequence.StandardKey.SelectAll):
            # Ctrl+A - select all rows
            self.selectAll()
            return
        if event.matches(QKeySequence.StandardKey.Undo):
            # Ctrl+Z - request undo of last delete
            self.undo_pressed.emit()
            return
        if event.key() == Qt.Key.Key_Delete:
            # Delete key - delete selected rows
            self.delete_pressed.emit()
            return

        super().keyPressEvent(event)
    
    def resizeEvent(self, event):
        """Override resize to keep fixed columns at right edge and maintain Last Backup at 200px"""
        if self._is_resizing:
            super().resizeEvent(event)
            return

        self._is_resizing = True
        super().resizeEvent(event)
        
        # Calculate available width for Game Name column
        # Total viewport width minus fixed columns (Platform + Save Status + Last Backup)
        # Use viewport().width() directly so Last Backup column visually aligns with the scrollbar
        total_available = self.viewport().width()
        fixed_width_total = 140 + 140 + 200  # Platform + Save Status + Last Backup
        available_for_game_name = total_available - fixed_width_total
        
        # Ensure minimum width for Game Name
        min_game_name_width = 200
        new_game_name_width = max(min_game_name_width, available_for_game_name)
        
        # Set all column widths to maintain layout
        # We do this in a way that prevents recursion
        header = self.horizontalHeader()
        if header.sectionResizeMode(0) == QHeaderView.ResizeMode.Interactive:
            self.setColumnWidth(0, new_game_name_width)
        
        # Ensure fixed columns stay at their fixed widths
        self.setColumnWidth(1, 140)  # Platform
        self.setColumnWidth(2, 140)  # Save Status
        self.setColumnWidth(3, 200)  # Last Backup
        
        self._is_resizing = False


# --- Main Window Class (Final Version with QoL Features) ---
class MainWindow(QMainWindow):
    def __init__(self, sandbox_monitor=None):
        super().__init__()
        self._sandbox_monitor = sandbox_monitor
        self._styles = StyleManager.instance()

        self._default_window_title = "Game Save Backup Tool"
        self.setWindowTitle(self._default_window_title)

        from config.app_config import settings_app_name, DEFAULT_UI_THEME, normalize_ui_theme
        self.settings = QSettings("MyCompany", settings_app_name())
        self._load_settings()
        self._styles.set_theme(
            normalize_ui_theme(self.settings.value("ui_theme", DEFAULT_UI_THEME, type=str))
        )
        self._styles.refresh()

        self.central_widget = QWidget()
        self.setCentralWidget(self.central_widget)
        self.main_layout = QVBoxLayout(self.central_widget)
        
        # Create table container with embedded progress bar
        self.table_container = TableWithProgressBar()
        self.game_table_widget = self.table_container.get_table()
        self.progress_bar = self.table_container.get_progress_bar()
        self.setup_game_table()
        self.table_container.set_accent_color(self._styles.accent_qcolor())
        self.main_layout.addWidget(self.table_container)

        self.status_label = QLabel("Ready.")
        self.status_label.setStyleSheet("font-size: 11px;")
        self.main_layout.addWidget(self.status_label)

        self.compress_summary_label = QLabel("")
        self.compress_summary_label.setWordWrap(True)
        self.compress_summary_label.setTextInteractionFlags(Qt.TextInteractionFlag.TextSelectableByMouse)
        self.compress_summary_label.setStyleSheet(
            "color: #8fdf9a; font-size: 11px; font-weight: 600; padding: 4px 2px 8px 2px;"
        )
        self.compress_summary_label.hide()
        self.main_layout.addWidget(self.compress_summary_label)
        self._compress_complete_metrics = None

        bottom_layout = QHBoxLayout()
        self.main_layout.addLayout(bottom_layout)

        self.backup_selected_button = QPushButton("Backup")
        self.backup_selected_button.setEnabled(False)
        self.backup_selected_button.setToolTip("Back up selected games, or all games with save locations if none selected. Shortcut: Ctrl+B")
        self.backup_selected_button.clicked.connect(self.backup_selected_saves)
        bottom_layout.addWidget(self.backup_selected_button)
        QShortcut(QKeySequence("Ctrl+B"), self, self.backup_selected_saves)
        QShortcut(QKeySequence("F1"), self, self._show_shortcuts_dialog)

        self.compress_button = QPushButton("Compress")
        self.compress_button.setEnabled(False)
        self.compress_button.setToolTip("Zip the default backup folder to save space. You can also be prompted on exit.")
        self.compress_button.clicked.connect(self._on_compress_button_clicked)
        bottom_layout.addWidget(self.compress_button)
        self._compress_running = False
        self._compress_cancel_requested = False
        self._compress_progress = 0
        self._compress_zip_path = None

        # Composite Scan button: [ Scan for Games | ▲ ]
        # Main text button
        self.scan_button = QPushButton("Scan for Games")
        self.scan_button.setObjectName("scan_main_button")
        self.is_scanning = False
        self.scan_button.clicked.connect(self.toggle_scan)

        # Small arrow sub-button that shows the options menu
        arrow_icon_path = os.path.join(os.path.dirname(__file__), "scroll_up.svg")
        self.scan_arrow_button = QToolButton()
        self.scan_arrow_button.setObjectName("scan_arrow_button")
        self.scan_arrow_button.setFixedWidth(18)
        # Match height of the main buttons so the split button looks seamless
        self.scan_arrow_button.setFixedHeight(19)
        self.scan_arrow_button.setIcon(QIcon(arrow_icon_path))
        # Make the arrow visually small
        self.scan_arrow_button.setIconSize(QSize(8, 6))

        # Drop-down menu with extra scan options (e.g. Reset scan)
        self.scan_menu = QMenu(self)
        self.scan_menu.setStyleSheet(self._styles.menu_qss())
        reset_action = self.scan_menu.addAction("Clear table and rescan")
        reset_action.triggered.connect(self.reset_scan)
        # Show the menu above the arrow button when clicked
        self.scan_arrow_button.clicked.connect(self._show_scan_menu_above)

        # Wrap the two pieces so they behave like a single split button
        self.scan_button_container = QWidget()
        self.scan_button_container.setObjectName("scan_button_container")
        scan_layout = QHBoxLayout(self.scan_button_container)
        scan_layout.setContentsMargins(0, 0, 0, 0)
        scan_layout.setSpacing(0)
        scan_layout.addWidget(self.scan_button)
        scan_layout.addWidget(self.scan_arrow_button)

        self._apply_scan_button_style(is_cancel=False)
        bottom_layout.addWidget(self.scan_button_container)

        self.add_manual_button = QPushButton("Add Custom Game")
        self.add_manual_button.clicked.connect(self.add_custom_game)
        bottom_layout.addWidget(self.add_manual_button)

        bottom_layout.addStretch(1)

        # Load saved filter state first, default to "found" for new users
        self.current_filter_state = self.settings.value("filter_state", "found", type=str)

        self.filter_button = QPushButton(self._get_filter_button_text())
        self.filter_button.setToolTip("Cycle between showing all games, only found, or only not found.")
        self.filter_button.clicked.connect(self._cycle_filter_state)
        bottom_layout.addWidget(self.filter_button)

        self._tools_menu = QMenu(self)
        self._tools_menu.setStyleSheet(self._styles.menu_qss())
        self._tools_menu.addAction("Backup folder & disk health…", self._open_tools_health_dialog)
        self._tools_menu.addSeparator()
        self._tools_menu.addAction("Export game list (JSON)…", self._export_catalog_json)
        self._tools_menu.addAction("Export game list (CSV)…", self._export_catalog_csv)
        self._tools_menu.addSeparator()
        self._tools_menu.addAction("Import game list (JSON)…", self._import_catalog_json)
        self._tools_menu.addAction("Import game list (CSV)…", self._import_catalog_csv)
        self._tools_menu.addSeparator()
        self._tools_menu.addAction("Shortcuts and tips…", self._show_shortcuts_dialog)
        self._tools_menu.addSeparator()
        self._tools_menu.addAction("About Game Save Backup Tool…", self._show_about_dialog)

        tools_tip = (
            "Tools: backup folder & disk health, export/import, shortcuts (F1), about"
        )
        self.tools_button = ToolsMenuButton("Tools")
        self.tools_button.setObjectName("tools_menu_button")
        self.tools_button.setToolTip(tools_tip)
        self.tools_button.setCursor(Qt.CursorShape.PointingHandCursor)
        self.tools_button.clicked.connect(self._popup_tools_menu_above_tools_button)
        self._apply_tools_menu_button_style()
        bottom_layout.addWidget(self.tools_button)

        self.settings_button = QPushButton("Settings")
        self.settings_button.setEnabled(True)
        self.settings_button.clicked.connect(self.open_settings)
        bottom_layout.addWidget(self.settings_button)
        
        self.quit_button = QPushButton("Quit")
        self.quit_button.clicked.connect(self.graceful_quit)
        bottom_layout.addWidget(self.quit_button)

        # Core data structures used by scan and cache logic
        self.save_manager = SaveManager()
        self.games_data = {}
        # For Ctrl+Z undo of last deletion
        self._last_deleted_entries = []
        self._backup_estimate_worker = None

        # Perform the heavier post-construction setup once everything above exists
        self._set_scan_button_idle_text()
        if self._sandbox_monitor:
            self._sandbox_log(
                "sandbox",
                "Main window ready. Sandbox uses isolated QSettings; use Scan and Compress to capture timings in the monitor.",
            )

    def _sandbox_log(self, category: str, message: str) -> None:
        if self._sandbox_monitor:
            self._sandbox_monitor.log_line(message, category)

    def _apply_scan_button_style(self, is_cancel: bool = False) -> None:
        """
        Apply styling for the composite Scan button:
        [ Scan for Games | ▲ ]

        We style the outer container so the hover glow and outline cover the
        entire control, and keep the inner buttons visually seamless.
        """
        if not hasattr(self, "scan_button_container"):
            return

        sm = StyleManager.instance()
        # Choose colors based on normal vs cancel state and theme
        if is_cancel:
            bg_top = "#6a4a2a"
            bg_bottom = "#4a2d1a"
            border_color = "#d97b1a"
            hover_border = "rgba(255, 152, 0, 200)"
            width_rule = "min-width: 121px; max-width: 121px;"
            disabled_bg = "stop:0 #4a2d1a, stop:1 #3a1f12"
            ch_top, ch_bottom = "#7a5a3a", "#5a3a22"
            cd_border = "#2d2d30"
            scan_main_color = "#ffffff"
            scan_dis_color = "#6a6a6a"
            sp_top, sp_bottom = "#2d2d30", "#1e1e1e"
            arrow_dis_border = "#2d2d30"
        elif sm.is_light_theme():
            bg_top = "#f4f4f8"
            bg_bottom = "#e6e6ee"
            border_color = "#c4c4ce"
            hover_border = sm.rgba(200)
            width_rule = ""
            disabled_bg = "stop:0 #ececf0, stop:1 #e4e4ea"
            ch_top, ch_bottom = "#ffffff", "#efeff4"
            cd_border = "#d4d4dc"
            scan_main_color = sm.ui_body_text_light()
            scan_dis_color = "#9898a4"
            sp_top, sp_bottom = "#d8d8e2", "#ceced8"
            arrow_dis_border = "#d0d0d8"
        else:
            bg_top = "#3a3a3d"
            bg_bottom = "#2d2d30"
            border_color = "#3e3e42"
            hover_border = sm.rgba(200)
            width_rule = ""
            disabled_bg = "stop:0 #1e1e1e, stop:1 #1e1e1e"
            ch_top, ch_bottom = "#505053", "#3a3a3d"
            cd_border = "#2d2d30"
            scan_main_color = sm.ui_body_text_dark()
            scan_dis_color = "#6a6a6a"
            sp_top, sp_bottom = "#2d2d30", "#1e1e1e"
            arrow_dis_border = "#2d2d30"

        self.scan_button_container.setStyleSheet(
            f"""
            QWidget#scan_button_container {{
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 {bg_top}, stop:1 {bg_bottom});
                border: 1px solid {border_color};
                border-radius: 4px;
                {width_rule}
            }}
            QWidget#scan_button_container:hover:enabled {{
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 {ch_top}, stop:1 {ch_bottom});
                border: 2px solid {hover_border};
                border-radius: 4px;
            }}
            QWidget#scan_button_container:disabled {{
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    {disabled_bg});
                border: 1px solid {cd_border};
                border-radius: 4px;
            }}

            QPushButton#scan_main_button {{
                background: transparent;
                border: none;
                color: {scan_main_color};
                font-size: 11px;
                min-height: 19px;
                max-height: 19px;
                height: 19px;
                padding: 1px 12px;  /* same left padding as other buttons */
            }}
            QPushButton#scan_main_button:disabled {{
                background: transparent;
                border: none;
                color: {scan_dis_color};
            }}
            QPushButton#scan_main_button:hover {{
                background: transparent;
                border: none;
            }}
            QPushButton#scan_main_button:pressed {{
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 {sp_top}, stop:1 {sp_bottom});
                border: none;
            }}

            QToolButton#scan_arrow_button {{
                background: transparent;
                border: none;
                min-height: 19px;
                max-height: 19px;
                height: 19px;
                padding: 0 4px;  /* space around the tiny arrow */
                border-left: 1px solid {border_color};  /* visual '|' divider */
            }}
            QToolButton#scan_arrow_button:disabled {{
                background: transparent;
                border-left: 1px solid {arrow_dis_border};
                color: {scan_dis_color};
            }}
            QToolButton#scan_arrow_button::menu-indicator {{
                image: none;   /* ensure no default down-arrow is drawn */
                width: 0px;
            }}
            QToolButton#scan_arrow_button:pressed {{
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 {sp_top}, stop:1 {sp_bottom});
                border: none;
            }}
            """
        )

    def _apply_tools_menu_button_style(self) -> None:
        """Same gradients as main-window QPushButton (styles/manager.py) + triangle paint."""
        btn = getattr(self, "tools_button", None)
        if btn is None:
            return
        sm = StyleManager.instance()
        hover_border = sm.rgba(200)
        if sm.is_light_theme():
            main_color = sm.ui_body_text_light()
            btn.setStyleSheet(
                f"""
            QPushButton#tools_menu_button {{
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #fafafc, stop:1 #e8e8ee);
                border: 1px solid #c4c4ce;
                border-radius: 4px;
                color: {main_color};
                font-size: 11px;
                padding: 1px 14px 1px 6px;
                min-height: 19px;
                max-height: 19px;
                height: 19px;
                min-width: 0px;
            }}
            QPushButton#tools_menu_button:hover:enabled {{
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #ffffff, stop:1 #efeff4);
                border: 2px solid {hover_border};
                border-radius: 4px;
            }}
            QPushButton#tools_menu_button:pressed {{
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #dedee6, stop:1 #d0d0da);
            }}
            QPushButton#tools_menu_button:disabled {{
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #ececf0, stop:1 #ececf0);
                color: #9898a4;
                border-color: #d4d4dc;
            }}
            """
            )
        else:
            main_color = sm.ui_body_text_dark()
            btn.setStyleSheet(
                f"""
            QPushButton#tools_menu_button {{
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #454548, stop:1 #2a2a2d);
                border: 1px solid #3e3e42;
                border-radius: 4px;
                color: {main_color};
                font-size: 11px;
                padding: 1px 14px 1px 6px;
                min-height: 19px;
                max-height: 19px;
                height: 19px;
                min-width: 0px;
            }}
            QPushButton#tools_menu_button:hover:enabled {{
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #505053, stop:1 #3a3a3d);
                border: 2px solid {hover_border};
                border-radius: 4px;
            }}
            QPushButton#tools_menu_button:pressed {{
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #2d2d30, stop:1 #202020);
            }}
            QPushButton#tools_menu_button:disabled {{
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #202020, stop:1 #202020);
                color: #6a6a6a;
                border-color: #2d2d30;
            }}
            """
            )

    def _set_scan_button_label_only(self) -> None:
        """Set only the label text for the scan button (no caret)."""
        if hasattr(self, "scan_button"):
            # Text label only; the small up-arrow is provided by the menu-indicator icon.
            self.scan_button.setText("Scan for Games")

    def _set_scan_button_idle_text(self) -> None:
        """
        Perform the one-time post-construction setup that was previously done
        when initializing the window, including styling, system tray, watchers,
        and populating cached games.
        """
        # Ensure the label and styling are in their default state
        self._set_scan_button_label_only()
        self._apply_scan_button_style(is_cancel=False)

        # System tray setup
        self.setup_system_tray()

        # Apply minimal styling for button consistency and green/red colors
        self.apply_minimal_styling()

        # Initialize file watcher
        self.setup_file_watcher()

        # Connect application shutdown signal for cleanup
        QApplication.instance().aboutToQuit.connect(self._cleanup_background_processes)

        # Now that save_manager is initialized in __init__, it's safe to populate
        self.populate_games_from_cache()

        self._setup_system_theme_listener()

    def _setup_system_theme_listener(self) -> None:
        """When ``ui_theme`` is ``system``, follow Windows light/dark without restarting."""
        hints = QGuiApplication.styleHints()
        if hasattr(hints, "colorSchemeChanged"):
            hints.colorSchemeChanged.connect(self._on_system_color_scheme_changed)

    def _on_system_color_scheme_changed(self) -> None:
        if not self._styles.is_system_theme():
            return
        self._styles.refresh()
        self.apply_minimal_styling()
        self.table_container.set_accent_color(self._styles.accent_qcolor())
        self.scan_menu.setStyleSheet(self._styles.menu_qss())
        if hasattr(self, "tray_menu"):
            self.tray_menu.setStyleSheet(self._styles.menu_qss())
        if hasattr(self, "_tools_menu"):
            self._tools_menu.setStyleSheet(self._styles.menu_qss())
        self._apply_tray_cancel_button_style()
        self.update_scan_button_style(is_cancel=self.is_scanning)
        if getattr(self, "_compress_running", False):
            self._set_compress_button_cancel_style()
        if self._sandbox_monitor:
            self._sandbox_monitor.apply_app_style()

    def _show_scan_menu_above(self) -> None:
        """Show the scan options menu above the arrow button."""
        if not hasattr(self, "scan_menu") or not hasattr(self, "scan_arrow_button"):
            return

        menu_size = self.scan_menu.sizeHint()
        global_top_right = self.scan_arrow_button.mapToGlobal(self.scan_arrow_button.rect().topRight())
        pos = QPoint(global_top_right.x() - menu_size.width(), global_top_right.y() - menu_size.height())
        self.scan_menu.popup(pos)

    def _resolved_backup_dest_for_auto_backup(self) -> str:
        """Return a usable on-disk backup root for auto-backup, or empty string."""
        backup_dest = self.settings.value("default_backup_path", "", type=str)
        if backup_dest and os.path.exists(backup_dest):
            return backup_dest
        backup_dest = self.settings.value("last_backup_path", "", type=str)
        if backup_dest and os.path.exists(backup_dest):
            return backup_dest
        return ""

    def _watcher_poll_timer_wanted(self) -> bool:
        """Poll only while auto-backup might need (re)start, or an observer thread is running."""
        if not WATCHDOG_AVAILABLE:
            return False
        if self.file_observer and self.file_observer.is_alive():
            return True
        if not self.settings.value("auto_backup_enabled", False, type=bool):
            return False
        return bool(self._resolved_backup_dest_for_auto_backup())

    def _sync_watcher_poll_timer(self) -> None:
        """Start/stop the 30s poll timer so we never tick when auto-backup is fully idle."""
        if not WATCHDOG_AVAILABLE or not getattr(self, "watcher_timer", None):
            return
        want = self._watcher_poll_timer_wanted()
        active = self.watcher_timer.isActive()
        if want and not active:
            self.watcher_timer.start(30000)
            print("File watcher: poll timer on (30s; auto-backup may start or restart monitoring).")
        elif not want and active:
            self.watcher_timer.stop()
            print("File watcher: poll timer off.")

    def setup_file_watcher(self):
        """Prepare auto-backup file monitoring (watchdog observer is created only when needed)."""
        if not WATCHDOG_AVAILABLE:
            print("Watchdog library not installed - file monitoring disabled")
            self.file_observer = None
            self.watched_games = {}
            self.watcher_timer = None
            return

        self.file_observer = None
        self.watched_games = {}
        self.last_backup_times = {}
        self.watcher_timer = QTimer()
        self.watcher_timer.timeout.connect(self.check_watcher_status)
        self._sync_watcher_poll_timer()
        if not self._watcher_poll_timer_wanted():
            print("File watcher: idle (automatic backups off or no valid backup folder).")

    def start_file_monitoring(self):
        """Start monitoring save files for automatic backup"""
        if not WATCHDOG_AVAILABLE:
            return

        auto_backup_enabled = self.settings.value("auto_backup_enabled", False, type=bool)
        backup_dest = self._resolved_backup_dest_for_auto_backup()

        if not auto_backup_enabled or not backup_dest:
            self.stop_file_monitoring(quiet=False)
            self._sync_watcher_poll_timer()
            return

        self.stop_file_monitoring(quiet=True)

        observer = Observer()
        monitored_count = 0
        for game_name, data in self.save_manager.game_save_locations.items():
            save_path_raw = data.get("save_path")
            if not save_path_raw or save_path_raw == "":
                continue

            save_path = self.save_manager.resolve_path(save_path_raw)
            if save_path and os.path.exists(save_path):
                handler = SaveFileEventHandler(game_name, save_path, self.trigger_auto_backup)
                observer.schedule(handler, save_path, recursive=True)
                self.watched_games[game_name] = handler
                monitored_count += 1
                print(f"Monitoring {game_name} at {save_path}")

        if monitored_count == 0:
            print("File watcher: auto-backup enabled, but no on-disk save folders to watch yet.")
            self._sync_watcher_poll_timer()
            return

        self.file_observer = observer
        self.file_observer.start()
        print(f"File monitoring started ({monitored_count} game folder(s) watched).")
        self._sync_watcher_poll_timer()
        self._show_tray_notification(
            "Auto-Backup Active",
            f"Monitoring {monitored_count} games for automatic backup",
            QSystemTrayIcon.MessageIcon.Information,
            3000,
        )

    def stop_file_monitoring(self, *, quiet: bool = False) -> None:
        """Stop file monitoring. Use ``quiet=True`` when immediately restarting the observer."""
        was_alive = bool(self.file_observer and self.file_observer.is_alive())
        if was_alive:
            self.file_observer.stop()
            self.file_observer.join()
            if not quiet:
                print("File monitoring stopped (save folders unwatched).")

        self.watched_games.clear()
        self.file_observer = None
        self._sync_watcher_poll_timer()

    def check_watcher_status(self):
        """Periodically check if watcher should be restarted"""
        if not WATCHDOG_AVAILABLE:
            return

        auto_backup_enabled = self.settings.value("auto_backup_enabled", False, type=bool)
        backup_dest = self._resolved_backup_dest_for_auto_backup()
        if not auto_backup_enabled:
            backup_dest = ""

        def _cache_has_disk_or_reg_save(d):
            if d.get("save_path"):
                return True
            return bool(d.get("save_in_registry_only") and d.get("save_registry_subkey"))

        should_be_monitoring = (
            auto_backup_enabled
            and backup_dest
            and os.path.exists(backup_dest)
            and bool([g for g in self.save_manager.game_save_locations.values() if _cache_has_disk_or_reg_save(g)])
        )
        
        is_monitoring = self.file_observer and self.file_observer.is_alive()
        
        if should_be_monitoring and not is_monitoring:
            self.start_file_monitoring()
        elif not should_be_monitoring and is_monitoring:
            self.stop_file_monitoring()

    def _should_show_notification(self):
        """Check if notifications should be shown based on user settings"""
        return (self.settings.value("notifications_enabled", True, type=bool) and 
                hasattr(self, 'tray_icon') and self.tray_icon.isVisible())
    


    def _show_tray_notification(self, title, message, icon=QSystemTrayIcon.MessageIcon.Information, duration=2000):
        """Show tray notification if enabled, with optional sound control"""
        if not self._should_show_notification():
            return
            
        # Always try Windows Toast first when available
        sound_enabled = self.settings.value("notification_sound_enabled", True, type=bool)
        
        # Try Windows Toast first if available
        if WINDOWS_TOAST_AVAILABLE:
            try:
                # Create Windows Toast notification
                toast = Notification(
                    app_id="GameSaveBackupTool",
                    title=title,
                    msg=message,
                    duration="short"  # Short duration as you requested
                )
                
                # Control sound based on user setting
                if sound_enabled:
                    toast.set_audio(audio.Default, loop=False)
                else:
                    # No sound - use silent option
                    pass  # Don't set any audio
                
                # Show the toast
                toast.show()
                return  # Exit early since we used Windows Toast
                
            except Exception as e:
                print(f"Windows Toast failed, falling back to PyQt: {e}")
                # Fall back to PyQt if Windows Toast fails
        
        # Fallback to PyQt system tray notification
        # Show notification (sound will be controlled by Windows system settings if sound_enabled is False)
        self.tray_icon.showMessage(title, message, icon, duration)
        
        # Note: PyQt system tray notifications have limited sound control
        # The user will need to adjust Windows system settings for full sound control

    def trigger_auto_backup(self, game_name, save_path):
        """Trigger automatic backup for a specific game"""
        # Cooldown check - use user-defined frequency setting
        now = time.time()
        last_backup = self.last_backup_times.get(game_name, 0)
        frequency_minutes = self.settings.value("backup_frequency_minutes", 5, type=int)
        cooldown_seconds = frequency_minutes * 60
        if now - last_backup < cooldown_seconds:
            return
            
        self.last_backup_times[game_name] = now
        
        backup_dest = self.settings.value("default_backup_path", "")
        if not backup_dest or not os.path.exists(backup_dest):
            # Fallback to last_backup_path for backward compatibility
            backup_dest = self.settings.value("last_backup_path", "")
            if not backup_dest or not os.path.exists(backup_dest):
                return
            
        print(f"Auto-backup triggered for {game_name}")
        
        # Get retention count from settings
        retention_count = self.settings.value("backup_retention_count", 3, type=int)
        
        # Show tray notification using helper method
        self._show_tray_notification("Auto-Backup", f"Backing up {game_name}...", 
                                   QSystemTrayIcon.MessageIcon.Information, 2000)
        
        subfolder_per_game = self.settings.value("backup_subfolder_per_game", True, type=bool)
        self.auto_backup_worker = AutoBackupWorker(game_name, save_path, backup_dest, retention_count, subfolder_per_game=subfolder_per_game)
        self.auto_backup_worker.backup_completed.connect(self.on_auto_backup_completed)
        self.auto_backup_worker.start()

    def on_auto_backup_completed(self, game_name, success, message):
        """Handle completion of automatic backup"""
        if success:
            print(f"Auto-backup completed for {game_name}")
            self._show_tray_notification("Backup Complete", f"Successfully backed up {game_name}",
                                       QSystemTrayIcon.MessageIcon.Information, 2000)
            
            # Update the last backup timestamp in our data
            self.save_manager.update_last_backup(game_name, datetime.now().isoformat())
            
            # Update the table if the game is visible
            items = self.game_table_widget.findItems(game_name, Qt.MatchFlag.MatchExactly)
            if items:
                row = items[0].row()
                friendly_date = self.format_backup_date(datetime.now().isoformat())
                backup_item = QTableWidgetItem(friendly_date)
                backup_item.setTextAlignment(Qt.AlignmentFlag.AlignCenter)
                self.game_table_widget.setItem(row, 3, backup_item)
        else:
            print(f"Auto-backup failed for {game_name}: {message}")

    def apply_minimal_styling(self):
        """Apply theme QSS from StyleManager (Windows accent-aware; light or dark)."""
        self.setStyleSheet(self._styles.main_window_qss())
        self.progress_bar.set_track_color(self._styles.progress_bar_track_color())
        self._apply_compress_summary_label_theme()
        self.apply_button_colors()
        self._apply_tools_menu_button_style()

    def _open_tools_health_dialog(self) -> None:
        HealthInfoDialog(self, self.settings).exec()

    def _popup_tools_menu_above_tools_button(self) -> None:
        """Open the Tools menu above the button (same geometry idea as the Scan arrow)."""
        if not hasattr(self, "_tools_menu") or not hasattr(self, "tools_button"):
            return
        menu_size = self._tools_menu.sizeHint()
        global_top_right = self.tools_button.mapToGlobal(self.tools_button.rect().topRight())
        pos = QPoint(
            global_top_right.x() - menu_size.width(),
            global_top_right.y() - menu_size.height(),
        )
        self._tools_menu.popup(pos)

    def _apply_compress_summary_label_theme(self) -> None:
        if not hasattr(self, "compress_summary_label"):
            return
        if self._styles.uses_vivid_success_error_buttons():
            self.compress_summary_label.setStyleSheet(
                "color: #1b5e20; font-size: 11px; font-weight: 600; padding: 4px 2px 8px 2px;"
            )
        else:
            self.compress_summary_label.setStyleSheet(
                "color: #8fdf9a; font-size: 11px; font-weight: 600; padding: 4px 2px 8px 2px;"
            )

    def apply_button_colors(self):
        """Apply the green and red colors to specific buttons with gradients and hover effects."""
        lt = self._styles.uses_vivid_success_error_buttons()
        # Green backup button (only if it exists)
        if hasattr(self, "backup_selected_button"):
            if lt:
                self.backup_selected_button.setStyleSheet("""
                    QPushButton {
                        background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                            stop:0 #7be879, stop:1 #2e9d3e);
                        border: 1px solid #1f8a32;
                        color: #0a1a0c;
                        min-height: 19px;
                        max-height: 19px;
                        height: 19px;
                        padding: 1px 12px;
                        font-size: 11px;
                        border-radius: 4px;
                    }
                    QPushButton:hover {
                        background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                            stop:0 #8ef48a, stop:1 #3ab84a);
                        border: 2px solid rgba(46, 125, 50, 220);
                        border-radius: 4px;
                    }
                    QPushButton:pressed {
                        background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                            stop:0 #248a34, stop:1 #1a6e28);
                    }
                    QPushButton:disabled {
                        background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                            stop:0 #ececf0, stop:1 #ececf0);
                        color: #9898a4;
                        border-color: #d4d4dc;
                    }
                """)
            else:
                self.backup_selected_button.setStyleSheet("""
                    QPushButton {
                        background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                            stop:0 #4a6a4a, stop:1 #2d4a2d);
                        border: 1px solid #4a6b4a;
                        color: #ffffff;
                        min-height: 19px;
                        max-height: 19px;
                        height: 19px;
                        padding: 1px 12px;
                        font-size: 11px;
                        border-radius: 4px;
                    }
                    QPushButton:hover {
                        background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                            stop:0 #5a7a5a, stop:1 #3a5a3a);
                        border: 2px solid rgba(76, 175, 80, 200);
                        border-radius: 4px;
                    }
                    QPushButton:pressed {
                        background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                            stop:0 #2d4a2d, stop:1 #1f3d1f);
                    }
                    QPushButton:disabled {
                        background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                            stop:0 #1e1e1e, stop:1 #1e1e1e);
                        color: #6a6a6a;
                        border-color: #2d2d30;
                    }
                """)
        if hasattr(self, "compress_button"):
            self._set_compress_button_compress_style()

        if hasattr(self, "quit_button"):
            if lt:
                self.quit_button.setStyleSheet("""
                    QPushButton {
                        background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                            stop:0 #ff8a80, stop:1 #e53935);
                        border: 1px solid #c62828;
                        color: #1a0606;
                        min-height: 19px;
                        max-height: 19px;
                        height: 19px;
                        padding: 1px 12px;
                        font-size: 11px;
                        border-radius: 4px;
                    }
                    QPushButton:hover {
                        background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                            stop:0 #ffab91, stop:1 #ef5350);
                        border: 2px solid rgba(211, 47, 47, 220);
                        border-radius: 4px;
                    }
                    QPushButton:pressed {
                        background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                            stop:0 #c62828, stop:1 #9e1f24);
                    }
                    QPushButton:disabled {
                        background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                            stop:0 #ececf0, stop:1 #ececf0);
                        color: #9898a4;
                        border-color: #d4d4dc;
                    }
                """)
            else:
                self.quit_button.setStyleSheet("""
                    QPushButton {
                        background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                            stop:0 #6a4a4a, stop:1 #4a2d2d);
                        border: 1px solid #6b4a4a;
                        color: #ffffff;
                        min-height: 19px;
                        max-height: 19px;
                        height: 19px;
                        padding: 1px 12px;
                        font-size: 11px;
                        border-radius: 4px;
                    }
                    QPushButton:hover {
                        background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                            stop:0 #7a5a5a, stop:1 #5a3a3a);
                        border: 2px solid rgba(244, 67, 54, 200);
                        border-radius: 4px;
                    }
                    QPushButton:pressed {
                        background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                            stop:0 #4a2d2d, stop:1 #3d1f1f);
                    }
                    QPushButton:disabled {
                        background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                            stop:0 #1e1e1e, stop:1 #1e1e1e);
                        color: #6a6a6a;
                        border-color: #2d2d30;
                    }
                """)

    def _set_compress_button_cancel_style(self):
        """Red Cancel style for Compress button when compression is running."""
        if not hasattr(self, "compress_button"):
            return
        if self._styles.uses_vivid_success_error_buttons():
            self.compress_button.setStyleSheet("""
                QPushButton {
                    background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                        stop:0 #ff8a80, stop:1 #e53935);
                    border: 1px solid #c62828;
                    color: #1a0606;
                    min-height: 19px;
                    max-height: 19px;
                    height: 19px;
                    padding: 1px 12px;
                    font-size: 11px;
                    border-radius: 4px;
                }
                QPushButton:hover {
                    background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                        stop:0 #ffab91, stop:1 #ef5350);
                    border: 2px solid rgba(211, 47, 47, 220);
                    border-radius: 4px;
                }
                QPushButton:pressed {
                    background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                        stop:0 #c62828, stop:1 #9e1f24);
                }
                QPushButton:disabled {
                    background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                        stop:0 #ececf0, stop:1 #ececf0);
                    color: #9898a4;
                    border-color: #d4d4dc;
                }
            """)
        else:
            self.compress_button.setStyleSheet("""
                QPushButton {
                    background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                        stop:0 #6a4a4a, stop:1 #4a2d2d);
                    border: 1px solid #6b4a4a;
                    color: #ffffff;
                    min-height: 19px;
                    max-height: 19px;
                    height: 19px;
                    padding: 1px 12px;
                    font-size: 11px;
                    border-radius: 4px;
                }
                QPushButton:hover {
                    background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                        stop:0 #7a5a5a, stop:1 #5a3a3a);
                    border: 2px solid rgba(244, 67, 54, 200);
                    border-radius: 4px;
                }
                QPushButton:pressed {
                    background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                        stop:0 #4a2d2d, stop:1 #3d1f1f);
                }
                QPushButton:disabled {
                    background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                        stop:0 #1e1e1e, stop:1 #1e1e1e);
                    color: #6a6a6a;
                    border-color: #2d2d30;
                }
            """)

    def _set_compress_button_compress_style(self):
        """Green Compress style (same as Backup button)."""
        if not hasattr(self, "compress_button"):
            return
        if self._styles.uses_vivid_success_error_buttons():
            self.compress_button.setStyleSheet("""
                QPushButton {
                    background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                        stop:0 #7be879, stop:1 #2e9d3e);
                    border: 1px solid #1f8a32;
                    color: #0a1a0c;
                    min-height: 19px;
                    max-height: 19px;
                    height: 19px;
                    padding: 1px 12px;
                    font-size: 11px;
                    border-radius: 4px;
                }
                QPushButton:hover {
                    background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                        stop:0 #8ef48a, stop:1 #3ab84a);
                    border: 2px solid rgba(46, 125, 50, 220);
                    border-radius: 4px;
                }
                QPushButton:pressed {
                    background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                        stop:0 #248a34, stop:1 #1a6e28);
                }
                QPushButton:disabled {
                    background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                        stop:0 #ececf0, stop:1 #ececf0);
                    color: #9898a4;
                    border-color: #d4d4dc;
                }
            """)
        else:
            self.compress_button.setStyleSheet("""
                QPushButton {
                    background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                        stop:0 #4a6a4a, stop:1 #2d4a2d);
                    border: 1px solid #4a6b4a;
                    color: #ffffff;
                    min-height: 19px;
                    max-height: 19px;
                    height: 19px;
                    padding: 1px 12px;
                    font-size: 11px;
                    border-radius: 4px;
                }
                QPushButton:hover {
                    background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                        stop:0 #5a7a5a, stop:1 #3a5a3a);
                    border: 2px solid rgba(76, 175, 80, 200);
                    border-radius: 4px;
                }
                QPushButton:pressed {
                    background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                        stop:0 #2d4a2d, stop:1 #1f3d1f);
                }
                QPushButton:disabled {
                    background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                        stop:0 #1e1e1e, stop:1 #1e1e1e);
                    color: #6a6a6a;
                    border-color: #2d2d30;
                }
            """)

    def update_scan_button_style(self, is_cancel=False):
        """Update scan button style based on state"""
        if not hasattr(self, 'scan_button'):
            return
            
        if is_cancel:
            # Red cancel style while preserving split-button layout and small arrow
            self._apply_scan_button_style(is_cancel=True)
        else:
            # Reset to default themed styling (split-button layout + small arrow)
            self._apply_scan_button_style(is_cancel=False)

    def _apply_tray_cancel_button_style(self) -> None:
        btn = getattr(self, "_tray_compress_cancel_btn", None)
        if btn is None:
            return
        if self._styles.is_light_theme():
            btn.setStyleSheet(
                """
                QPushButton {
                    background-color: #ececf0;
                    color: #141418;
                    border: none;
                    padding: 3px 10px;
                    padding-left: 12px;
                    font-size: 11px;
                    border-radius: 3px;
                    text-align: left;
                }
                QPushButton:hover {
                    background-color: rgba(255, 152, 0, 140);
                }
                QPushButton:pressed {
                    background-color: rgba(255, 152, 0, 180);
                }
            """
            )
        else:
            btn.setStyleSheet(
                """
                QPushButton {
                    background-color: #1a1a1a;
                    color: #ffffff;
                    border: none;
                    padding: 3px 10px;
                    padding-left: 12px;
                    font-size: 11px;
                    border-radius: 3px;
                    text-align: left;
                }
                QPushButton:hover {
                    background-color: rgba(255, 152, 0, 140);
                }
                QPushButton:pressed {
                    background-color: rgba(255, 152, 0, 180);
                }
            """
            )

    def setup_system_tray(self):
        """Setup system tray icon with context menu"""
        if not QSystemTrayIcon.isSystemTrayAvailable():
            QMessageBox.critical(self, "System Tray", "System tray is not available on this system.")
            return

        # Create system tray icon
        self.tray_icon = QSystemTrayIcon(self)
        icon_path = os.path.join(os.path.dirname(os.path.dirname(__file__)), "gsbt.ico")
        if os.path.exists(icon_path):
            self.tray_icon.setIcon(QIcon(icon_path))
        else:
            # Fallback to window icon
            self.tray_icon.setIcon(self.windowIcon())
        
        tray_menu = QMenu()
        tray_menu.setStyleSheet(self._styles.menu_qss())
        
        # Order: Show, Backup, Compress, [Cancel when running], — div —, Quit
        show_action = QAction("Show", self)
        show_action.triggered.connect(self.show_window)
        tray_menu.addAction(show_action)

        backup_action = QAction("Backup", self)
        backup_action.triggered.connect(self.backup_selected_saves)
        tray_menu.addAction(backup_action)

        compress_action = QAction("Compress", self)
        compress_action.triggered.connect(lambda: self.compress_backups(then_exit=False, from_tray=True))
        tray_menu.addAction(compress_action)
        self.tray_compress_action = compress_action

        # Divider above Compress (%) when compression runs; inserted/removed with Cancel
        tray_compress_div = QAction(self)
        tray_compress_div.setSeparator(True)
        self.tray_compress_divider_action = tray_compress_div

        # Cancel (indented, black by default, orange on hover) — inserted above bottom divider when compression runs
        cancel_btn = QPushButton("    Cancel")
        cancel_btn.setCursor(Qt.CursorShape.PointingHandCursor)
        cancel_btn.setFlat(True)
        self._tray_compress_cancel_btn = cancel_btn
        self._apply_tray_cancel_button_style()
        cancel_btn.clicked.connect(self._on_tray_cancel_compress_clicked)
        tray_cancel_action = QWidgetAction(self)
        tray_cancel_action.setDefaultWidget(cancel_btn)
        self.tray_cancel_compress_action = tray_cancel_action

        # Separator (reference kept so Cancel can be inserted above it when compression runs)
        tray_sep = QAction(self)
        tray_sep.setSeparator(True)
        tray_menu.addAction(tray_sep)
        self.tray_separator_action = tray_sep

        quit_action = QAction("Quit", self)
        quit_action.triggered.connect(self.quit_application)
        tray_menu.addAction(quit_action)
        
        self.tray_menu = tray_menu
        # Don't use setContextMenu - we show the menu ourselves so it pops up above the icon
        self.tray_icon.activated.connect(self.tray_icon_activated)
        
        # Set tooltip
        self.tray_icon.setToolTip("Game Save Backup Tool")
        
        # Show the tray icon
        self.tray_icon.show()

    def tray_icon_activated(self, reason):
        """Handle tray icon activation"""
        if reason == QSystemTrayIcon.ActivationReason.DoubleClick:
            if self.isVisible():
                self.hide()
            else:
                self.show_window()
        elif reason == QSystemTrayIcon.ActivationReason.Context:
            self._show_tray_menu_above()

    def _show_tray_menu_above(self):
        """Show tray context menu above the tray icon (same as other tray apps)."""
        if hasattr(self, "tray_compress_action"):
            if getattr(self, "_compress_running", False):
                self.tray_compress_action.setEnabled(False)
                self.tray_compress_action.setText(f"Compress ({getattr(self, '_compress_progress', 0)}%)")
            else:
                self.tray_compress_action.setEnabled(True)
                self.tray_compress_action.setText("Compress")
        self.tray_menu.ensurePolished()
        self.tray_menu.adjustSize()
        pos = QCursor.pos()
        menu_height = self.tray_menu.sizeHint().height()
        menu_width = self.tray_menu.sizeHint().width()
        # Position menu so its bottom edge is just above the cursor (icon)
        x = pos.x()
        y = pos.y() - menu_height
        # Keep on screen: don't go above top or off left/right
        screen = QGuiApplication.screenAt(pos) or QGuiApplication.primaryScreen()
        if screen:
            geo = screen.availableGeometry()
            if y < geo.top():
                y = geo.top()
            if x + menu_width > geo.right():
                x = geo.right() - menu_width
            if x < geo.left():
                x = geo.left()
        self.tray_menu.popup(QPoint(x, y))

    def show_window(self):
        """Show and raise the main window"""
        self.show()
        self.raise_()
        self.activateWindow()

    def hide_window(self):
        """Hide the main window"""
        self.hide()

    def graceful_quit(self):
        """Gracefully quit the application, stopping any active scans first"""
        # If scanning is active, cancel it first
        if self.is_scanning:
            self.cancel_scan()
            # Wait a moment for cleanup, then quit
            QTimer.singleShot(500, self.quit_application)
        else:
            self.quit_application()
    
    def quit_application(self):
        """Quit the application completely"""
        self._save_settings()
        if hasattr(self, 'tray_icon'):
            self.tray_icon.hide()
        QApplication.instance().quit()



    def _find_steam_ids(self):
        print("--> Checking for active Steam user...")
        detector = GameDetector()
        steam_path = detector._get_steam_install_path()
        if not steam_path: return None
        
        userdata_path = os.path.join(steam_path, "userdata")
        if not os.path.exists(userdata_path): return None

        user_ids = [d for d in os.listdir(userdata_path) if d.isdigit() and d != '0']
        if not user_ids: return None
        
        # Find the most recently modified user folder (this is the SteamID3)
        steam_id3 = max(user_ids, key=lambda d: os.path.getmtime(os.path.join(userdata_path, d)))
        
        # --- NEW: Calculate the SteamID64 from the SteamID3 ---
        steam_id64_base = 76561197960265728
        steam_id64 = str(int(steam_id3) + steam_id64_base)
        
        print(f"--> Found SteamID3: {steam_id3}")
        print(f"--> Calculated SteamID64: {steam_id64}")
        
        return {'steamid3': steam_id3, 'steamid64': steam_id64}

    def _find_active_steam_user_id(self):
        print("--> Checking for active Steam user...")
        detector = GameDetector()
        steam_path = detector._get_steam_install_path()
        if not steam_path: return None
        
        userdata_path = os.path.join(steam_path, "userdata")
        if not os.path.exists(userdata_path): return None

        user_ids = [d for d in os.listdir(userdata_path) if d.isdigit() and d != '0']
        if not user_ids:
            print("--> No user ID folders found in Steam/userdata.")
            return None
        
        # Find the most recently modified user folder
        latest_id = max(user_ids, key=lambda d: os.path.getmtime(os.path.join(userdata_path, d)))
        print(f"--> Found most recent Steam User ID: {latest_id}")
        return latest_id

    def open_settings(self):
        dialog = SettingsDialog(self)
        if dialog.exec():
            from config.app_config import DEFAULT_UI_THEME, normalize_ui_theme

            self._styles.set_theme(
                normalize_ui_theme(self.settings.value("ui_theme", DEFAULT_UI_THEME, type=str))
            )
            self._styles.refresh()
            self.apply_minimal_styling()
            self.table_container.set_accent_color(self._styles.accent_qcolor())
            self.scan_menu.setStyleSheet(self._styles.menu_qss())
            if hasattr(self, "tray_menu"):
                self.tray_menu.setStyleSheet(self._styles.menu_qss())
            if hasattr(self, "_tools_menu"):
                self._tools_menu.setStyleSheet(self._styles.menu_qss())
            self._apply_tray_cancel_button_style()
            self.update_scan_button_style(is_cancel=self.is_scanning)
            if getattr(self, "_compress_running", False):
                self._set_compress_button_cancel_style()
            self.start_file_monitoring()
            self.refresh_backup_dates()
            self.status_label.setText("Settings saved.")
            if self._sandbox_monitor:
                self._sandbox_monitor.apply_app_style()

    def refresh_backup_dates(self):
        """Refresh all backup dates in the table with the current date format"""
        for row in range(self.game_table_widget.rowCount()):
            game_info = self.game_table_widget.item(row, 0).data(Qt.ItemDataRole.UserRole)
            if game_info:
                last_backup_raw = game_info.get("last_backup")
                formatted_date = self.format_backup_date(last_backup_raw)
                backup_item = QTableWidgetItem(formatted_date)
                backup_item.setTextAlignment(Qt.AlignmentFlag.AlignCenter)
                self.game_table_widget.setItem(row, 3, backup_item)
    
    def _load_settings(self):
        geometry = self.settings.value("geometry")
        if geometry: self.restoreGeometry(geometry)
        self.last_backup_path = self.settings.value("last_backup_path", "")
        
        # Load default backup path from settings (if not set, use last_backup_path)
        default_backup_path = self.settings.value("default_backup_path", "", type=str)
        if not default_backup_path and self.last_backup_path:
            # If default is not set but last_backup_path exists, use it as default
            self.settings.setValue("default_backup_path", self.last_backup_path)
        
        # Load column widths and ensure they're integers
        # Only save/load Game Name column width (others are fixed)
        saved_game_name_width = self.settings.value("game_name_column_width", 350, type=int)
        self.saved_game_name_width = saved_game_name_width if saved_game_name_width else 350

    def _save_settings(self):
        self.settings.setValue("geometry", self.saveGeometry())
        self.settings.setValue("last_backup_path", self.last_backup_path)
        # Save only Game Name column width (others are fixed)
        self.settings.setValue("game_name_column_width", self.game_table_widget.columnWidth(0))
        # Save current filter state
        self._save_filter_state()
        
    def closeEvent(self, event):
        """Handle window close event - minimize to tray if enabled; optionally suggest compress on exit."""
        minimize_to_tray = self.settings.value("minimize_to_tray", True, type=bool)

        if minimize_to_tray and hasattr(self, 'tray_icon') and self.tray_icon.isVisible():
            self.hide()
            self._show_tray_notification(
                "Game Save Backup Tool",
                "Application was minimized to tray",
                QSystemTrayIcon.MessageIcon.Information,
                2000
            )
            event.ignore()
            return
        # Actually exiting
        ask_compress = self.settings.value("ask_compress_on_exit", True, type=bool)
        backup_path = self.settings.value("default_backup_path", "", type=str) or self.settings.value("last_backup_path", "", type=str)
        if ask_compress and backup_path and os.path.isdir(backup_path):
            reply = QMessageBox.question(
                self,
                "Compress backups?",
                "Would you like to compress your backups before closing?\n\n"
                "This creates a single ZIP of your backup folder to save space. You can also compress anytime from the Compress button or tray menu.",
                QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.No | QMessageBox.StandardButton.Cancel,
                QMessageBox.StandardButton.No,
            )
            if reply == QMessageBox.StandardButton.Cancel:
                event.ignore()
                return
            if reply == QMessageBox.StandardButton.Yes:
                event.ignore()
                self.compress_backups(then_exit=True)
                return
        self._do_quit(event)

    def _do_quit(self, event=None):
        """Perform actual quit: save, hide tray; teardown runs once on ``aboutToQuit``."""
        self._save_settings()
        if hasattr(self, 'tray_icon'):
            self.tray_icon.hide()
        if event is not None:
            event.accept()
        else:
            QApplication.instance().quit()
    
    def _cleanup_background_processes(self):
        """Clean up threads, timers, and watchdog once when the app is quitting (``aboutToQuit``)."""
        if getattr(self, "_shutdown_cleanup_ran", False):
            return
        self._shutdown_cleanup_ran = True
        print("Cleaning up background processes...")

        # Stop file monitoring (may already be stopped; join with timeout on quit)
        if getattr(self, "file_observer", None):
            try:
                if self.file_observer.is_alive():
                    self.file_observer.stop()
                    self.file_observer.join(timeout=2)
                    print("File monitoring stopped (shutdown).")
            except Exception as e:
                print(f"Error stopping file observer: {e}")

        if getattr(self, "watcher_timer", None) and self.watcher_timer.isActive():
            self.watcher_timer.stop()
            print("File watcher: poll timer off (shutdown).")
        
        # Stop any running worker threads
        if hasattr(self, 'game_detector_thread') and self.game_detector_thread and self.game_detector_thread.isRunning():
            self.game_detector_thread.terminate()
            self.game_detector_thread.wait(2000)
            print("Game detector thread stopped")
        
        if hasattr(self, 'save_fetcher_thread') and self.save_fetcher_thread and self.save_fetcher_thread.isRunning():
            self.save_fetcher_thread.cancel()
            self.save_fetcher_thread.wait(2000)
            print("Save fetcher thread stopped")
        
        if hasattr(self, 'backup_worker') and self.backup_worker and self.backup_worker.isRunning():
            self.backup_worker.terminate()
            self.backup_worker.wait(2000)
            print("Backup worker stopped")
        
        if hasattr(self, 'auto_backup_worker') and self.auto_backup_worker and self.auto_backup_worker.isRunning():
            self.auto_backup_worker.terminate()
            self.auto_backup_worker.wait(2000)
            print("Auto backup worker stopped")
        
        # Cancel compression if running and delete partial zip (user closed/quit without clicking Cancel)
        if getattr(self, "_compress_running", False) and getattr(self, "compress_worker", None) and self.compress_worker.isRunning():
            self.compress_worker.cancel()
            self.compress_worker.wait(3000)
            print("Compress worker stopped")
        zip_path = getattr(self, "_compress_zip_path", None)
        if zip_path and os.path.isfile(zip_path):
            try:
                os.remove(zip_path)
                print(f"Removed partial zip on exit: {zip_path}")
            except OSError as e:
                print(f"Could not remove partial zip on exit: {zip_path} — {e}")
        
        print("Background process cleanup complete")

    def keyPressEvent(self, event):
        """
        Global keyboard shortcuts:
        - Ctrl+A: select all rows in the table
        - Delete: delete selected rows
        - Ctrl+Z: undo last delete
        - F1: shortcuts and tips (same as Tools menu)
        These work even if focus is not currently on the table widget.
        """
        if event.matches(QKeySequence.StandardKey.SelectAll) or \
           event.matches(QKeySequence.StandardKey.Undo) or \
           event.key() == Qt.Key.Key_Delete:
            # Forward to the table so its logic is reused
            self.game_table_widget.keyPressEvent(event)
            return

        super().keyPressEvent(event)

    def setup_game_table(self):
        # Fresh table setup with simple, working column behavior
        self.game_table_widget.clear()
        self.game_table_widget.setColumnCount(4)
        
        # Remove padding/margins so Last Backup column anchors to scrollbar
        self.game_table_widget.setContentsMargins(0, 0, 0, 0)
        # Remove padding/margins and hide focus outline while still allowing keyboard focus
        self.game_table_widget.setStyleSheet("""
            QTableWidget {
                padding: 0px;
                margin: 0px;
                outline: none;
            }
            QTableView:focus {
                outline: none;
            }
        """)
        
        # Set headers with proper alignment
        header_labels = ["Game Name", "Platform", "Save Status", "Last Backup"]
        self.game_table_widget.setHorizontalHeaderLabels(header_labels)
        
        # Scrollbar behavior – only show when needed
        self.game_table_widget.setVerticalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAsNeeded)
        self.game_table_widget.setHorizontalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAsNeeded)
        
        # Basic table settings for proper selection behavior
        self.game_table_widget.setSelectionBehavior(QAbstractItemView.SelectionBehavior.SelectRows)
        self.game_table_widget.setSelectionMode(QAbstractItemView.SelectionMode.ExtendedSelection)  # Multi-select with Ctrl and drag
        self.game_table_widget.setEditTriggers(QAbstractItemView.EditTrigger.NoEditTriggers)
        self.game_table_widget.verticalHeader().setVisible(False)
        self.game_table_widget.setSortingEnabled(True)
        # Add vertical spacing between rows
        self.game_table_widget.verticalHeader().setDefaultSectionSize(28)  # Row height with spacing
        # Allow keyboard focus (for shortcuts) but hide visual focus outline via stylesheet above
        self.game_table_widget.setFocusPolicy(Qt.FocusPolicy.StrongFocus)
        # Disable tab key moving focus between cells
        self.game_table_widget.setTabKeyNavigation(False)
        # Enable drag selection
        self.game_table_widget.setDragDropMode(QAbstractItemView.DragDropMode.NoDragDrop)
        self.game_table_widget.setDefaultDropAction(Qt.DropAction.IgnoreAction)
        
        # Simple column resize setup - Game Name is resizable, others are fixed
        header = self.game_table_widget.horizontalHeader()
        header.setStretchLastSection(False)  # Don't auto-stretch last section
        
        # Set fixed column widths for locked columns
        self.game_table_widget.setColumnWidth(1, 140)  # Platform - fixed at 140px
        self.game_table_widget.setColumnWidth(2, 140)  # Save Status - fixed at 140px
        self.game_table_widget.setColumnWidth(3, 200)  # Last Backup - fixed at 200px
        
        # Set column resize modes
        header.setSectionResizeMode(0, QHeaderView.ResizeMode.Interactive)  # Game Name - resizable
        header.setSectionResizeMode(1, QHeaderView.ResizeMode.Fixed)        # Platform - fixed
        header.setSectionResizeMode(2, QHeaderView.ResizeMode.Fixed)        # Save Status - fixed
        header.setSectionResizeMode(3, QHeaderView.ResizeMode.Fixed)        # Last Backup - fixed at 200px, stays at right edge
        
        # Set Game Name column width (restore from saved or use default 350px)
        game_name_width = 350  # Default width
        if hasattr(self, 'saved_game_name_width'):
            game_name_width = self.saved_game_name_width
        
        # Calculate initial Game Name width based on table width
        # This will be adjusted by resizeEvent, but set initial value
        self.game_table_widget.setColumnWidth(0, game_name_width)  # Game Name - default 350px
        
        # Connect to header section resize to save Game Name width when user manually resizes it
        header.sectionResized.connect(self._on_section_resized)
        
        # Set header alignments after creating headers
        self._set_header_alignments()
        
        # Connect signals
        self.game_table_widget.itemDoubleClicked.connect(self.on_game_double_clicked)
        self.game_table_widget.delete_pressed.connect(self.on_delete_key_pressed)
        self.game_table_widget.undo_pressed.connect(self.undo_last_delete)

        # Ensure the table starts with keyboard focus so global shortcuts work immediately
        self.game_table_widget.setFocus()
    
    def _on_section_resized(self, logicalIndex, oldSize, newSize):
        """Save Game Name width when user manually resizes it, and maintain fixed columns"""
        if logicalIndex == 0:
            # Game Name column - save the width when user manually resizes
            self.saved_game_name_width = newSize
        elif logicalIndex in [1, 2, 3]:
            # Fixed columns - reset to their fixed widths if somehow they were resized
            if logicalIndex == 1:
                self.game_table_widget.setColumnWidth(1, 140)  # Platform
            elif logicalIndex == 2:
                self.game_table_widget.setColumnWidth(2, 140)  # Save Status
            elif logicalIndex == 3:
                self.game_table_widget.setColumnWidth(3, 200)  # Last Backup

    def _set_header_alignments(self):
        """Set header text alignments - Game Name left, others centered"""
        # Get the existing header items and update their alignment
        for i in range(self.game_table_widget.columnCount()):
            item = self.game_table_widget.horizontalHeaderItem(i)
            if item:
                if i == 0:  # Game Name - left aligned
                    item.setTextAlignment(Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter)
                else:  # Others - center aligned
                    item.setTextAlignment(Qt.AlignmentFlag.AlignCenter)

    def format_backup_date(self, timestamp_str):
        """Format a backup timestamp according to user's preferred date format"""
        if not timestamp_str or "T" not in timestamp_str:
            return "Not yet backed up"
        
        try:
            dt_object = datetime.fromisoformat(timestamp_str)
            date_format = self.settings.value("date_format", "iso", type=str)
            
            if date_format == "us":
                return dt_object.strftime("%m/%d/%Y | %I:%M %p")
            elif date_format == "european":
                return dt_object.strftime("%d/%m/%Y | %H:%M")
            elif date_format == "asian":
                return dt_object.strftime("%Y/%m/%d | %H:%M")
            else:  # Default to ISO
                return dt_object.strftime("%Y-%m-%d | %H:%M")
        except ValueError:
            return timestamp_str

    def _game_has_known_save_location(self, game_info: dict) -> bool:
        if not game_info:
            return False
        if game_info.get("save_in_registry_only"):
            return bool(game_info.get("save_registry_hive") and game_info.get("save_registry_subkey"))
        return game_info.get("save_path_resolved") is not None

    def _game_save_location_display(self, game_info: dict) -> str:
        d = game_info.get("save_location_display")
        if d:
            return str(d)
        p = game_info.get("save_path_resolved")
        return str(p) if p else "N/A"

    def _can_backup_game_save(self, game_info: dict) -> bool:
        if game_info.get("save_in_registry_only"):
            return bool(game_info.get("save_registry_hive") and game_info.get("save_registry_subkey"))
        p = game_info.get("save_path_resolved")
        return bool(p) and os.path.exists(p)

    def _add_game_to_table_widget(self, game_info, animate=True):
        game_name = game_info["name"]
        items = self.game_table_widget.findItems(game_name, Qt.MatchFlag.MatchExactly)
        
        if items:
            # Game already exists in table - update it while preserving backup date
            row = items[0].row()
            old_info = self.game_table_widget.item(row, 0).data(Qt.ItemDataRole.UserRole)
            if isinstance(old_info, dict):
                merged = dict(old_info)
                merged.update(game_info)
                game_info = merged
            existing_backup_item = self.game_table_widget.item(row, 3)
            existing_backup_text = existing_backup_item.text() if existing_backup_item else "Not yet backed up"
            # Preserve the existing backup date unless we have a newer one from game_info
            preserve_backup_date = existing_backup_text != "Not yet backed up"
        else:
            # New game - add new row
            row = self.game_table_widget.rowCount()
            self.game_table_widget.insertRow(row)
            preserve_backup_date = False

        item_name = QTableWidgetItem(game_name)
        item_name.setTextAlignment(Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter)
        
        item_platform = QTableWidgetItem(game_info.get("platform", "N/A"))
        item_platform.setTextAlignment(Qt.AlignmentFlag.AlignCenter)
        
        status_text = "✓ Found" if self._game_has_known_save_location(game_info) else "✗ Not Found"
        item_status = QTableWidgetItem(status_text)
        item_status.setTextAlignment(Qt.AlignmentFlag.AlignCenter)
        
        # --- PRESERVE BACKUP DATES LOGIC ---
        if preserve_backup_date:
            # Keep the existing backup date from the table
            existing_backup_item = self.game_table_widget.item(row, 3)
            display_text = existing_backup_item.text() if existing_backup_item else "Not yet backed up"
        else:
            # Use backup date from game_info (new game or fresh scan)
            last_backup_raw = game_info.get("last_backup")
            display_text = self.format_backup_date(last_backup_raw)

        item_backup = QTableWidgetItem(display_text)
        item_backup.setTextAlignment(Qt.AlignmentFlag.AlignCenter)
        # --- END OF PRESERVATION LOGIC ---

        tooltip_text = (
            f"Install: {game_info.get('install_path', 'N/A')}\n"
            f"Save: {self._game_save_location_display(game_info)}"
            + (" (registry)" if game_info.get("save_in_registry_only") else "")
        )
        item_name.setToolTip(tooltip_text)
        item_name.setData(Qt.ItemDataRole.UserRole, game_info)
        
        self.game_table_widget.setItem(row, 0, item_name)
        self.game_table_widget.setItem(row, 1, item_platform)
        self.game_table_widget.setItem(row, 2, item_status)
        self.game_table_widget.setItem(row, 3, item_backup)
        self.games_data[game_name] = game_info
        
        # Add fade-in animation for new rows
        if animate and not items:
            self._animate_row_fade_in(row)
    
    def _animate_row_fade_in(self, row):
        """Animate a row fading in using opacity"""
        # Set initial opacity to 0 via item data (delegate will read this)
        for col in range(self.game_table_widget.columnCount()):
            item = self.game_table_widget.item(row, col)
            if item:
                item.setData(Qt.ItemDataRole.UserRole + 10, 0.0)  # Store opacity
        
        # Use QTimer to gradually increase opacity
        fade_steps = 20
        current_step = [0]
        
        def update_fade():
            if current_step[0] <= fade_steps:
                opacity = current_step[0] / fade_steps
                
                # Update opacity for all items in the row
                for col in range(self.game_table_widget.columnCount()):
                    item = self.game_table_widget.item(row, col)
                    if item:
                        item.setData(Qt.ItemDataRole.UserRole + 10, opacity)
                
                # Force repaint
                self.game_table_widget.viewport().update()
                
                if current_step[0] < fade_steps:
                    current_step[0] += 1
                    QTimer.singleShot(20, update_fade)
                else:
                    # Cleanup - remove opacity data
                    for col in range(self.game_table_widget.columnCount()):
                        item = self.game_table_widget.item(row, col)
                        if item:
                            item.setData(Qt.ItemDataRole.UserRole + 10, None)
        
        # Start animation after small delay
        QTimer.singleShot(50, update_fade)

    def _get_filter_button_text(self):
        """Get the display text for the current filter state"""
        filter_texts = {
            "all": "Filter: All",
            "found": "Filter: Found", 
            "not_found": "Filter: Not Found"
        }
        return filter_texts.get(self.current_filter_state, "Filter: Found")

    def _cycle_filter_state(self):
        if self.current_filter_state == "all":
            self.current_filter_state = "found"
        elif self.current_filter_state == "found":
            self.current_filter_state = "not_found"
        else:
            self.current_filter_state = "all"
        
        self.filter_button.setText(self._get_filter_button_text())
        self._save_filter_state()
        self._apply_filter()

    def _save_filter_state(self):
        """Save the current filter state to settings"""
        self.settings.setValue("filter_state", self.current_filter_state)

    def _apply_filter(self):
        for row in range(self.game_table_widget.rowCount()):
            game_info = self.game_table_widget.item(row, 0).data(Qt.ItemDataRole.UserRole)
            if not game_info: continue
            has_save_path = self._game_has_known_save_location(game_info)
            if self.current_filter_state == "all":
                self.game_table_widget.setRowHidden(row, False)
            elif self.current_filter_state == "found":
                self.game_table_widget.setRowHidden(row, not has_save_path)
            elif self.current_filter_state == "not_found":
                self.game_table_widget.setRowHidden(row, has_save_path)

    def populate_games_from_cache(self):
        cached_games = self.save_manager.game_save_locations
        if not cached_games: return
        self.status_label.setText(f"Loaded {len(cached_games)} games from cache. Click 'Scan' for fresh data.")
        found_count = 0
        not_found_count = 0
        
        for game_name, data in cached_games.items():
            save_path = data.get("save_path")
            reg_only = bool(data.get("save_in_registry_only"))
            rh, rs = data.get("save_registry_hive"), data.get("save_registry_subkey")

            # Determine platform from JSON data
            # If steam_app_id exists, it's a Steam game, otherwise it's Custom/Manual
            platform = "Steam" if data.get("steam_app_id") else "Custom"

            save_path_resolved = None
            save_location_display = None
            if save_path and str(save_path).strip():
                save_path_resolved = self.save_manager.resolve_path(save_path)
                save_location_display = save_path_resolved
                found_count += 1
            elif reg_only and rh and rs:
                save_location_display = format_registry_save_display(str(rh), str(rs))
                found_count += 1
            else:
                save_path = None
                not_found_count += 1

            self._add_game_to_table_widget({
                "name": game_name,
                "app_id": data.get("steam_app_id"),
                "install_path": "Not Scanned",
                "platform": platform,
                "save_path_raw": save_path,
                "save_path_resolved": save_path_resolved,
                "save_location_display": save_location_display,
                "save_in_registry_only": reg_only,
                "save_registry_hive": rh if reg_only else None,
                "save_registry_subkey": rs if reg_only else None,
                "source": "Cached",
                "last_backup": data.get("last_backup"),
            })
        
        # Update status to show breakdown
        status_parts = [f"Loaded {len(cached_games)} games from cache"]
        if found_count > 0:
            status_parts.append(f"{found_count} with save files")
        if not_found_count > 0:
            status_parts.append(f"{not_found_count} without save files")
        status_parts.append("Click 'Scan' for fresh data.")
        self.status_label.setText(" (" + ", ".join(status_parts[1:-1]) + "). " + status_parts[-1] if len(status_parts) > 2 else ". ".join(status_parts))
        
        self._update_backup_compress_buttons()
        self._apply_filter()

    def toggle_scan(self):
        if not self.is_scanning:
            self.start_game_scan()
        else:
            self.cancel_scan()

    def _update_backup_compress_buttons(self):
        """Enable Backup and Compress only if at least one game has a valid save path."""
        has_valid = False
        for row in range(self.game_table_widget.rowCount()):
            item = self.game_table_widget.item(row, 0)
            if not item:
                continue
            game_info = item.data(Qt.ItemDataRole.UserRole)
            if game_info and self._can_backup_game_save(game_info):
                has_valid = True
                break
        self.backup_selected_button.setEnabled(has_valid)
        self.compress_button.setEnabled(has_valid)

    def start_game_scan(self):
        self.is_scanning = True
        # Switch to a single continuous Cancel button (hide the arrow)
        self.scan_button.setText("Cancel Scan")
        self.scan_arrow_button.setVisible(False)
        self.update_scan_button_style(is_cancel=True)
        self.status_label.setText("Scanning for installed games...")
        self.add_manual_button.setEnabled(False)
        self.backup_selected_button.setEnabled(False)
        self.compress_button.setEnabled(False)
        # Don't clear the table - preserve existing data including backup dates
        # self.game_table_widget.setRowCount(0)  # Commented out to preserve backup dates
        # Show progress bar from the start (same behaviour as compress): indeterminate during game detection
        self.progress_bar.setRange(0, 0)
        self.progress_bar.setValue(0)
        if hasattr(self.progress_bar, 'animated_show'):
            self.progress_bar.animated_show()
        self._sandbox_phase_t0 = time.perf_counter()
        self._sandbox_log("scan", "Local game detection started (Steam manifests, GOG, registry, …)")
        self.steam_ids = self._find_steam_ids()
        self.active_user_id = self._find_active_steam_user_id()
        self.game_detector_thread = GameDetectorWorker()
        self.game_detector_thread.finished.connect(self.on_game_detection_finished)
        self.game_detector_thread.error.connect(self.display_error)
        self.game_detector_thread.start()

    def cancel_scan(self):
        """
        Handle user-initiated cancellation of an in-progress scan.

        If we can't stop instantly, we at least show a clear "Stopping..."
        disabled state in orange so the user knows their click was received.
        """
        if not self.is_scanning:
            return

        # Immediately reflect that cancellation was requested
        self.is_scanning = False
        self._sandbox_log("scan", "Scan cancel requested (stopping game detection or wiki fetch).")
        self.scan_button.setText("Stopping...")
        self.status_label.setText("Stopping scan...")
        # Disable the whole composite control so hover/press no longer apply
        self.scan_button_container.setEnabled(False)
        self.update_scan_button_style(is_cancel=True)
        # Do not hide the bar here; it stays visible until the task actually finishes, then fades (see finish handlers)

        # Cancel running threads gracefully, but don't block the UI thread;
        # let existing finished/error handlers restore the button state.
        if hasattr(self, 'game_detector_thread') and self.game_detector_thread.isRunning():
            self.game_detector_thread.terminate()

        if hasattr(self, 'save_fetcher_thread') and self.save_fetcher_thread.isRunning():
            self.save_fetcher_thread.cancel()  # Use graceful cancellation

    def reset_scan(self):
        """
        Clear the current table contents and run a fresh scan.
        This is exposed via the small drop-down button next to 'Scan for Games'.
        """
        if self.is_scanning:
            QMessageBox.information(
                self,
                "Reset Scan",
                "Please wait for the current scan to finish or cancel it before resetting.",
            )
            return

        # Clear table and in-memory game cache (does NOT wipe saved save-path data)
        self.game_table_widget.setRowCount(0)
        self.games_data.clear()
        self.progress_bar.setValue(0)
        self.status_label.setText("Table cleared. Starting a fresh scan...")

        self.start_game_scan()

    def on_game_detection_finished(self, detected_games):
        dt = time.perf_counter() - getattr(self, "_sandbox_phase_t0", time.perf_counter())
        if self._sandbox_monitor:
            if not self.is_scanning:
                self._sandbox_log(
                    "scan",
                    f"Game detector thread ended after cancel (~{dt:.2f}s since scan start).",
                )
            else:
                n = len(detected_games) if detected_games else 0
                self._sandbox_log("scan", f"Local detection finished in {dt:.2f}s | {n} game(s) found (before wiki filter).")
        # If user cancelled during game detection, restore UI and start progress bar fade (bar stays until now)
        if not self.is_scanning:
            self.scan_button_container.setEnabled(True)
            self.scan_arrow_button.setVisible(True)
            self._set_scan_button_label_only()
            self.update_scan_button_style(is_cancel=False)
            self.add_manual_button.setEnabled(True)
            self._update_backup_compress_buttons()
            if hasattr(self.progress_bar, '_auto_hide_timer'):
                self.progress_bar._auto_hide_timer.stop()
                self.progress_bar._auto_hide_timer.start(PROGRESS_BAR_FADE_DELAY_MS)
            return
        if not detected_games:
            self.status_label.setText("No installed games found.")
            self.is_scanning = False
            self.scan_button_container.setEnabled(True)
            self.scan_arrow_button.setVisible(True)
            self._set_scan_button_label_only()
            self.update_scan_button_style(is_cancel=False)
            self.add_manual_button.setEnabled(True)
            self._update_backup_compress_buttons()
            if hasattr(self.progress_bar, 'animated_hide'):
                self.progress_bar.animated_hide()
            return
        
        # Filter out games that should be skipped
        games_to_scan = self._filter_games_to_scan(detected_games)
        
        if not games_to_scan:
            self.status_label.setText("All games already scanned (no save files found previously).")
            self.is_scanning = False
            self.scan_button_container.setEnabled(True)
            self.scan_arrow_button.setVisible(True)
            self._set_scan_button_label_only()
            self.update_scan_button_style(is_cancel=False)
            self.add_manual_button.setEnabled(True)
            self._update_backup_compress_buttons()
            if hasattr(self.progress_bar, 'animated_hide'):
                self.progress_bar.animated_hide()
            self.populate_games_from_cache()
            return
            
        skipped_count = len(detected_games) - len(games_to_scan)
        status_msg = f"Found {len(detected_games)} games. Fetching save locations for {len(games_to_scan)} games"
        if skipped_count > 0:
            status_msg += f" (skipped {skipped_count} games with no save files)"
        status_msg += "..."
        
        self.status_label.setText(status_msg)
        self.progress_bar.setRange(0, len(games_to_scan))
        self.progress_bar.setValue(0)
        if hasattr(self.progress_bar, 'animated_show'):
            self.progress_bar.animated_show()
        self.processed_games_count = 0
        self.detected_games_count = len(games_to_scan)
        self._sandbox_wiki_t0 = time.perf_counter()
        self._sandbox_log(
            "scan",
            f"PCGW / save-path fetch started for {len(games_to_scan)} game(s) (network + local cache writes).",
        )
        self.save_fetcher_thread = SaveLocationFetcherWorker(games_to_scan, self.steam_ids)
        self.save_fetcher_thread.game_save_fetched.connect(self.on_save_location_fetched)
        self.save_fetcher_thread.all_fetching_finished.connect(self.on_all_save_locations_fetched)
        self.save_fetcher_thread.error.connect(self.display_error)
        self.save_fetcher_thread.save_fetch_metrics.connect(self._on_save_fetch_metrics)
        self.save_fetcher_thread.save_fetch_trace.connect(self._on_save_fetch_trace)
        if self._sandbox_monitor:
            self._sandbox_monitor.append_save_fetch_batch_header(len(games_to_scan))
        self.save_fetcher_thread.start()

    def _filter_games_to_scan(self, detected_games):
        """Filter out games that should be skipped based on settings"""
        skip_not_found = self.settings.value("skip_not_found_games", True, type=bool)
        
        if not skip_not_found:
            return detected_games  # Don't skip anything
        
        games_to_scan = []
        cached_games = self.save_manager.game_save_locations
        
        for game in detected_games:
            game_name = game.get("name")
            
            # Check if this game was previously scanned and no save files were found
            if game_name in cached_games:
                cached_data = cached_games[game_name]
                # If save_path exists and is not empty, we found saves before
                if cached_data.get("save_path") or cached_data.get("save_in_registry_only"):
                    games_to_scan.append(game)
                # If no save_path / registry save, we found nothing last time — skip
                else:
                    print(f"--> Skipping '{game_name}' (no save files found in previous scan)")
            else:
                # Never scanned before, include it
                games_to_scan.append(game)
        
        return games_to_scan

    def _on_save_fetch_metrics(self, d):
        if self._sandbox_monitor and isinstance(d, dict):
            self._sandbox_monitor.append_save_fetch_per_game(d)

    def _on_save_fetch_trace(self, line: str):
        if self._sandbox_monitor and isinstance(line, str):
            self._sandbox_monitor.append_save_fetch_trace(line)

    def on_save_location_fetched(self, game_data):
        self.processed_games_count += 1
        if self._sandbox_monitor and self.detected_games_count > 0:
            step = max(1, self.detected_games_count // 20)
            if self.processed_games_count % step == 0 or self.processed_games_count == self.detected_games_count:
                wdt = time.perf_counter() - getattr(self, "_sandbox_wiki_t0", time.perf_counter())
                g = game_data.get("name", "?")
                self._sandbox_log(
                    "scan",
                    f"… {self.processed_games_count}/{self.detected_games_count} ({wdt:.1f}s elapsed) last: {g!r}",
                )
        self.progress_bar.setValue(self.processed_games_count)
        self.status_label.setText(f"Fetching... ({self.processed_games_count}/{self.detected_games_count})")
        self._add_game_to_table_widget(game_data)
        # Do not enable Backup/Compress here; keep them disabled until scan fully finishes or is cancelled

    def on_all_save_locations_fetched(self):
        if self._sandbox_monitor and getattr(self, "_sandbox_wiki_t0", None):
            wdt = time.perf_counter() - self._sandbox_wiki_t0
            avg = wdt / max(1, self.processed_games_count)
            self._sandbox_log(
                "scan",
                f"Wiki phase done in {wdt:.2f}s | {self.processed_games_count} game(s) (~{avg:.2f}s each avg).",
            )
            self._sandbox_monitor.append_save_fetch_batch_footer(self.processed_games_count, wdt)
        self.is_scanning = False
        self.scan_button_container.setEnabled(True)
        self.scan_arrow_button.setVisible(True)
        self._set_scan_button_label_only()
        self.update_scan_button_style(is_cancel=False)
        self.add_manual_button.setEnabled(True)
        self._update_backup_compress_buttons()
        self.progress_bar.setValue(100)  # Show completion, will reset on next scan
        self.status_label.setText(f"Scan complete. Found data for {self.game_table_widget.rowCount()} games.")
        if self._should_show_notification():
            self._show_tray_notification("Scan complete", f"Found data for {self.game_table_widget.rowCount()} games.", QSystemTrayIcon.MessageIcon.Information, 2000)
        self._apply_filter()
        
        # Start file monitoring with updated game data
        self.start_file_monitoring()

    def on_game_double_clicked(self, item):
        row = item.row()
        game_info = self.game_table_widget.item(row, 0).data(Qt.ItemDataRole.UserRole)
        if game_info.get("save_in_registry_only"):
            disp = self._game_save_location_display(game_info)
            QGuiApplication.clipboard().setText(disp)
            QMessageBox.information(
                self,
                "Registry save",
                f"Save data for '{game_info.get('name')}' is stored in the Windows registry (not a folder).\n\n"
                f"{disp}\n\n"
                "The path was copied to the clipboard — paste it into Registry Editor’s address bar (Ctrl+V), "
                "or use Backup to export a .reg file.",
            )
            return
        resolved_save_path = game_info.get("save_path_resolved")
        if resolved_save_path and os.path.exists(resolved_save_path):
            try: os.startfile(resolved_save_path)
            except Exception as e: QMessageBox.critical(self, "Error", f"Could not open folder: {e}")
        else: QMessageBox.warning(self, "Path Not Found", f"The save path for '{game_info.get('name')}' could not be found.")

    def on_delete_key_pressed(self):
        selected_rows = sorted(list(set(item.row() for item in self.game_table_widget.selectedItems())), reverse=True)
        if not selected_rows:
            return

        reply = QMessageBox.question(
            self,
            "Confirm Delete",
            f"Are you sure you want to delete {len(selected_rows)} game(s) from the list?",
            QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.No,
            QMessageBox.StandardButton.No,
        )
        if reply == QMessageBox.StandardButton.Yes:
            # Capture full info for undo (Ctrl+Z)
            self._last_deleted_entries = []
            games_to_delete = []
            for row in selected_rows:
                item = self.game_table_widget.item(row, 0)
                if not item:
                    continue
                game_name = item.text()
                game_info = item.data(Qt.ItemDataRole.UserRole)
                save_data = self.save_manager.game_save_locations.get(game_name, {}).copy()
                self._last_deleted_entries.append(
                    {"name": game_name, "game_info": game_info, "save_data": save_data}
                )
                games_to_delete.append(game_name)

            # Remove rows from bottom up
            for row in selected_rows:
                self.game_table_widget.removeRow(row)

            # Remove from persistent save data
            if games_to_delete:
                self.save_manager.delete_games(games_to_delete)

            self.status_label.setText(
                f"Deleted {len(games_to_delete)} game(s). Press Ctrl+Z to undo."
            )

    def undo_last_delete(self):
        """Undo the last delete operation (Ctrl+Z)."""
        if not self._last_deleted_entries:
            return

        restored_count = 0
        for entry in self._last_deleted_entries:
            name = entry.get("name")
            game_info = entry.get("game_info") or {}
            save_data = entry.get("save_data") or {}

            # Restore save location info in SaveManager if we had it before
            if save_data:
                self.save_manager.add_or_update_save_location(name, save_data)
                # Pass last_backup back into game_info so table shows correct date
                if "last_backup" in save_data:
                    game_info = dict(game_info) if game_info else {"name": name}
                    game_info["last_backup"] = save_data["last_backup"]

            # Re-add to table if we have enough info
            if game_info:
                self._add_game_to_table_widget(game_info)
                restored_count += 1

        if restored_count:
            self._apply_filter()
            self.status_label.setText(f"Restored {restored_count} deleted game(s).")

        # Clear undo buffer after use
        self._last_deleted_entries = []

    def display_error(self, message):
        self.status_label.setText("An error occurred.")
        self.is_scanning = False
        if hasattr(self, "scan_button_container"):
            self.scan_button_container.setEnabled(True)
        if hasattr(self, "scan_arrow_button"):
            self.scan_arrow_button.setVisible(True)
        self._set_scan_button_label_only()
        self.update_scan_button_style(is_cancel=False)
        self.add_manual_button.setEnabled(True)
        self.progress_bar.setValue(0)  # Reset progress bar
        QMessageBox.critical(self, "Error", message)

    def add_custom_game(self):
        dialog = AddCustomGameDialog(self)
        if dialog.exec():
            data = dialog.get_data()
            if not data.get("name") or not data.get("save_path"):
                QMessageBox.warning(self, "Missing Information", "Both game name and save path are required.")
                return
            game_info = {"name": data.get("name"), "app_id": None, "install_path": "N/A", "platform": "Custom", "save_path_raw": data.get("save_path"), "save_path_resolved": data.get("save_path"), "source": "Manual"}
            self.save_manager.add_or_update_save_location(data.get("name"), {"save_path": data.get("save_path"), "notes": "Manually added"})
            self._add_game_to_table_widget(game_info)
            self._update_backup_compress_buttons()
            self.status_label.setText(f"Added custom game: {data.get('name')}")
            self._apply_filter()

    def backup_selected_saves(self):
        selected_rows = sorted(list(set(item.row() for item in self.game_table_widget.selectedItems())))
        if selected_rows:
            games_to_backup = []
            for row in selected_rows:
                game_info = self.game_table_widget.item(row, 0).data(Qt.ItemDataRole.UserRole)
                if game_info and self._can_backup_game_save(game_info):
                    games_to_backup.append(game_info)
                else:
                    name = game_info.get("name", "?") if game_info else "?"
                    QMessageBox.warning(
                        self,
                        "Invalid Path",
                        f"Cannot backup '{name}': no valid save folder or registry key to export.",
                    )
            if not games_to_backup:
                return
        else:
            # No selection: backup all games that have a valid save path
            games_to_backup = []
            for row in range(self.game_table_widget.rowCount()):
                item = self.game_table_widget.item(row, 0)
                if not item:
                    continue
                game_info = item.data(Qt.ItemDataRole.UserRole)
                if game_info and self._can_backup_game_save(game_info):
                    games_to_backup.append(game_info)
            if not games_to_backup:
                QMessageBox.information(
                    self,
                    "Nothing to Backup",
                    "No games with save locations found to backup. Run a scan first, or select specific games.",
                )
                return

        # Resolve destination folder: use settings, or show first-backup dialog
        default_backup_path = self.settings.value("default_backup_path", "", type=str)
        if default_backup_path and os.path.exists(default_backup_path):
            destination_folder = default_backup_path
        else:
            fallback = self.last_backup_path or self.settings.value("last_backup_path", "", type=str) or os.path.expanduser("~")
            dialog = FirstBackupDestinationDialog(self, initial_path=fallback)
            if not dialog.exec():
                return
            destination_folder = dialog.get_folder()
            if dialog.get_remember_default():
                self.last_backup_path = destination_folder

        subfolder_per_game = self.settings.value("backup_subfolder_per_game", True, type=bool)
        want_est = self.settings.value("show_backup_estimate", True, type=bool)
        want_confirm = self.settings.value("confirm_before_backup", False, type=bool)

        if not want_est and not want_confirm:
            self._start_backup_worker(games_to_backup, destination_folder, subfolder_per_game)
            return

        if not want_est and want_confirm:
            if not self._prompt_backup_start(games_to_backup, destination_folder, None):
                return
            self._start_backup_worker(games_to_backup, destination_folder, subfolder_per_game)
            return

        self._run_backup_estimate_async(games_to_backup, destination_folder, subfolder_per_game)

    def _start_backup_worker(self, games_to_backup, destination_folder, subfolder_per_game):
        self.scan_button.setEnabled(False)
        self.backup_selected_button.setEnabled(False)
        self.progress_bar.setValue(0)
        self.backup_worker = BackupWorker(
            games_to_backup, destination_folder, subfolder_per_game=subfolder_per_game
        )
        self.backup_worker.progress.connect(
            lambda val, msg: [self.progress_bar.setValue(val), self.status_label.setText(msg)]
        )
        self.backup_worker.game_backed_up.connect(self.on_game_backed_up)
        self.backup_worker.finished.connect(self.on_backup_finished)
        self.backup_worker.error.connect(self.display_error)
        self.backup_worker.start()

    def _prompt_backup_start(self, games_to_backup, destination_folder, est):
        """Ask for confirmation and/or show size estimate. ``est`` may be None."""
        n = len(games_to_backup)
        want_confirm = self.settings.value("confirm_before_backup", False, type=bool)
        if est is None and not want_confirm:
            return True
        if est is None and want_confirm:
            msg = f"Back up {n} game(s) to:\n{destination_folder}\n\nContinue?"
            return (
                QMessageBox.question(
                    self,
                    "Confirm backup",
                    msg,
                    QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.No,
                    QMessageBox.StandardButton.No,
                )
                == QMessageBox.StandardButton.Yes
            )
        dlg = BackupEstimatePromptDialog(
            self,
            est,
            n,
            destination_folder,
            want_confirm=want_confirm,
        )
        return dlg.exec() == QDialog.DialogCode.Accepted

    def _run_backup_estimate_async(self, games_to_backup, destination_folder, subfolder_per_game):
        prog = QProgressDialog(self)
        prog.setWindowTitle("Backup")
        prog.setLabelText("Calculating backup size…")
        prog.setRange(0, 0)
        prog.setCancelButton(None)
        prog.setWindowModality(Qt.WindowModality.WindowModal)
        prog.setMinimumDuration(0)
        prog.show()
        QApplication.processEvents()

        worker = BackupEstimateWorker(games_to_backup, self)
        self._backup_estimate_worker = worker

        def cleanup_worker():
            self._backup_estimate_worker = None
            worker.deleteLater()

        def on_ok(est):
            prog.close()
            cleanup_worker()
            if not self._prompt_backup_start(games_to_backup, destination_folder, est):
                return
            self._start_backup_worker(games_to_backup, destination_folder, subfolder_per_game)

        def on_err(msg):
            prog.close()
            cleanup_worker()
            q = QMessageBox.question(
                self,
                "Estimate failed",
                f"{msg}\n\nStart backup without a size estimate?",
                QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.No,
                QMessageBox.StandardButton.No,
            )
            if q != QMessageBox.StandardButton.Yes:
                return
            if not self._prompt_backup_start(games_to_backup, destination_folder, None):
                return
            self._start_backup_worker(games_to_backup, destination_folder, subfolder_per_game)

        worker.finished_ok.connect(on_ok)
        worker.failed.connect(on_err)
        worker.start()

    def _export_catalog_json(self):
        path, _ = QFileDialog.getSaveFileName(self, "Export game list", "", "JSON (*.json);;All files (*.*)")
        if not path:
            return
        if not path.lower().endswith(".json"):
            path += ".json"
        try:
            export_catalog_json(self.save_manager.game_save_locations, path)
            self.status_label.setText(f"Exported game list to {path}")
        except OSError as e:
            QMessageBox.warning(self, "Export failed", str(e))

    def _export_catalog_csv(self):
        path, _ = QFileDialog.getSaveFileName(self, "Export game list", "", "CSV (*.csv);;All files (*.*)")
        if not path:
            return
        if not path.lower().endswith(".csv"):
            path += ".csv"
        try:
            export_catalog_csv(self.save_manager.game_save_locations, path)
            self.status_label.setText(f"Exported game list to {path}")
        except OSError as e:
            QMessageBox.warning(self, "Export failed", str(e))

    def _import_catalog_json(self):
        path, _ = QFileDialog.getOpenFileName(self, "Import game list", "", "JSON (*.json);;All files (*.*)")
        if not path:
            return
        try:
            data, note = import_catalog_json(path)
        except (OSError, ValueError, json.JSONDecodeError) as e:
            QMessageBox.warning(self, "Import failed", str(e))
            return
        self._apply_catalog_import(data, note)

    def _import_catalog_csv(self):
        path, _ = QFileDialog.getOpenFileName(self, "Import game list", "", "CSV (*.csv);;All files (*.*)")
        if not path:
            return
        try:
            data = import_catalog_csv(path)
        except (OSError, ValueError) as e:
            QMessageBox.warning(self, "Import failed", str(e))
            return
        self._apply_catalog_import(data, "CSV")

    def _apply_catalog_import(self, data: dict, note: str):
        if not data:
            QMessageBox.information(self, "Import", "No game entries found in file.")
            return
        msg = QMessageBox(self)
        msg.setIcon(QMessageBox.Icon.Question)
        msg.setWindowTitle("Import game list")
        msg.setText(
            f"Loaded {len(data)} game(s) ({note}).\n\n"
            "Merge adds/updates entries and keeps other games. Replace clears the current list first."
        )
        merge_btn = msg.addButton("Merge with existing", QMessageBox.ButtonRole.YesRole)
        replace_btn = msg.addButton("Replace entire list", QMessageBox.ButtonRole.DestructiveRole)
        cancel_btn = msg.addButton("Cancel", QMessageBox.ButtonRole.RejectRole)
        msg.exec()
        clicked = msg.clickedButton()
        if clicked in (None, cancel_btn):
            return
        if clicked == replace_btn:
            self.save_manager.replace_all_games(data)
            self.status_label.setText(f"Imported game list: replaced with {len(data)} game(s).")
        elif clicked == merge_btn:
            n = self.save_manager.merge_imported_games(data)
            self.status_label.setText(f"Imported game list: merged {n} game(s).")
        else:
            return
        self.game_table_widget.setRowCount(0)
        self.populate_games_from_cache()
        self._update_backup_compress_buttons()
        self._apply_filter()

    def _show_shortcuts_dialog(self):
        dlg = ShortcutsDialog(self)
        dlg.exec()

    def _show_about_dialog(self) -> None:
        AboutDialog(self).exec()

    def on_game_backed_up(self, game_name, timestamp_str):
        """
        Updates the UI and saves the timestamp when a single game backup is complete.
        """
        # 1. Update the JSON file
        self.save_manager.update_last_backup(game_name, timestamp_str)

        # 2. Update the table in the UI
        items = self.game_table_widget.findItems(game_name, Qt.MatchFlag.MatchExactly)
        if items:
            row = items[0].row()
            # Format the timestamp for display using user's preferred format
            friendly_date = self.format_backup_date(timestamp_str)
            backup_item = QTableWidgetItem(friendly_date)
            backup_item.setTextAlignment(Qt.AlignmentFlag.AlignCenter)
            self.game_table_widget.setItem(row, 3, backup_item)

    def on_backup_finished(self, message):
        self.status_label.setText(message)
        self.scan_button.setEnabled(True)
        self.backup_selected_button.setEnabled(True)
        self.progress_bar.setValue(100)  # Show completion
        if self._should_show_notification():
            self._show_tray_notification("Backup complete", message, QSystemTrayIcon.MessageIcon.Information, 2000)
        QMessageBox.information(self, "Backup Complete", message)

    def _on_compress_button_clicked(self):
        """Compress button: start compression or cancel if already running."""
        if self._compress_running:
            self.cancel_compress()
        else:
            self.compress_backups(then_exit=False)

    def compress_backups(self, then_exit=False, from_tray=False):
        """Zip the default backup folder. If then_exit, quit the app when done. from_tray=True when started from tray menu."""
        if getattr(self, "_compress_running", False):
            return  # Guard: do not start a second compression (e.g. tray menu clicked again)
        backup_path = self.settings.value("default_backup_path", "", type=str)
        if not backup_path:
            backup_path = self.settings.value("last_backup_path", "", type=str)
        if not backup_path or not os.path.isdir(backup_path):
            QMessageBox.warning(
                self,
                "No Backup Folder",
                "No backup folder is set or the folder does not exist. Set a default backup folder in Settings.",
            )
            return

        comp_opts = options_from_qsettings(self.settings)
        if comp_opts.engine == "7z" and not comp_opts.seven_zip_exe:
            QMessageBox.warning(
                self,
                "7-Zip not found",
                "The compression preset uses the 7-Zip program, but 7z.exe was not found.\n\n"
                "Install 7-Zip from https://www.7-zip.org/ or set a custom path in Settings → "
                "Compress backups, or choose a built-in ZIP preset.",
            )
            return

        self._compress_then_exit = then_exit
        self._compress_zip_path = None
        self._compress_cancel_requested = False
        self._compress_complete_metrics = None
        self.compress_summary_label.hide()
        self._compress_running = True
        self.scan_button_container.setEnabled(False)
        self.backup_selected_button.setEnabled(False)
        self.add_manual_button.setEnabled(False)
        self.compress_button.setText("Cancel")
        self.compress_button.setEnabled(True)
        self._set_compress_button_cancel_style()
        self.progress_bar.setRange(0, 100)
        self.progress_bar.setValue(0)
        if hasattr(self.progress_bar, 'animated_show'):
            self.progress_bar.animated_show()
        self.compress_worker = CompressBackupWorker(backup_path, comp_opts, self)
        self.compress_worker.progress.connect(lambda msg: self.status_label.setText(msg))
        self.compress_worker.progress_percent.connect(self.progress_bar.setValue)
        self.compress_worker.progress_percent.connect(self._on_compress_progress)
        self.compress_worker.zip_created.connect(self._on_compress_zip_created)
        self.compress_worker.finished.connect(self._on_compress_finished)
        self.compress_worker.compression_metrics.connect(self._on_compression_metrics)
        if self._sandbox_monitor:
            self._sandbox_log(
                "compress_start",
                f"Compression started — folder: {backup_path} | mode: {comp_opts.summary_label}",
            )
        self.compress_worker.start()
        # Add divider above Compress (%) and Cancel above bottom divider; both removed when compression ends or user clicks Cancel
        if hasattr(self, "tray_menu") and hasattr(self, "tray_compress_action") and hasattr(self, "tray_compress_divider_action"):
            self.tray_menu.insertAction(self.tray_compress_action, self.tray_compress_divider_action)
        if hasattr(self, "tray_menu") and hasattr(self, "tray_cancel_compress_action") and hasattr(self, "tray_separator_action"):
            self.tray_menu.insertAction(self.tray_separator_action, self.tray_cancel_compress_action)
        # Show 0% in title bar and tray menu immediately (taskbar / minimized at a glance)
        self._on_compress_progress(0)
        # Notify when compression started from tray (user may have minimized or started from tray)
        if from_tray and self._should_show_notification():
            self._show_tray_notification("Compression started", "Zipping backup folder…", QSystemTrayIcon.MessageIcon.Information, 2000)

    def _on_compression_metrics(self, d):
        if isinstance(d, dict) and d.get("phase") == "complete":
            self._compress_complete_metrics = d
        if self._sandbox_monitor:
            self._on_sandbox_compression_metrics(d)

    @staticmethod
    def _format_compression_green_summary(d: dict) -> str:
        wall = float(d.get("wall_sec", 0) or 0)
        sz_b = int(d.get("archive_size_bytes", d.get("zip_bytes", 0)) or 0)
        ratio = d.get("compression_ratio_pct", 0)
        files = int(d.get("files_total", 0) or 0)
        lines = [
            f"File: {d.get('archive_basename', '—')}",
            f"Type: {d.get('archive_type_display', '—')}  |  Level: {d.get('level_display', '—')}  |  Threads: {d.get('threads_display', '—')}",
            f"Time: {wall:.2f}s  |  Archive size: {d.get('archive_size_human', '—')} ({sz_b:,} bytes)",
            f"Raw: {d.get('raw_size_human', '—')}  |  {ratio}% of raw  |  {files} files  |  {d.get('avg_throughput_mib_s', 0)} MiB/s mean (input)",
        ]
        return "\n".join(lines)

    def _on_sandbox_compression_metrics(self, d):
        if not self._sandbox_monitor or not isinstance(d, dict):
            return
        phase = d.get("phase")
        if phase == "compressing":
            b = d.get("bytes_uncompressed", 0)
            eng = d.get("engine", "")
            note = d.get("note", "")
            fd = d.get("files_done", 0)
            notes_on = read_log_setting(self.settings, "show_compress_tick_notes")
            if fd is not None and fd < 0:
                af = d.get("archive_format", "zip")
                out_lbl = "7z out" if af == "7z" else "zip out"
                tail = f" — {note or '7-Zip running'}" if notes_on else ""
                self._sandbox_monitor.log_line(
                    f"{eng} | {b / (1024**2):.1f} MiB ({out_lbl}) | {d.get('throughput_mib_s', 0)} MiB/s disk growth | "
                    f"{d.get('elapsed_sec', 0)}s{tail}",
                    "compress_tick",
                )
            else:
                self._sandbox_monitor.log_line(
                    f"{fd}/{d.get('total_files', 0)} files | {b / (1024**2):.1f} MiB read | "
                    f"{d.get('throughput_mib_s', 0)} MiB/s | {d.get('elapsed_sec', 0)}s | {eng}",
                    "compress_tick",
                )
        elif phase == "complete":
            b = d.get("bytes_uncompressed", 0)
            z = d.get("zip_bytes", 0)
            eng = d.get("engine", "")
            self._sandbox_monitor.log_line(
                f"SUMMARY: {d.get('files_total', 0)} files | raw {b / (1024**3):.2f} GiB → archive {z / (1024**2):.1f} MiB "
                f"({d.get('compression_ratio_pct', 0)}% of raw size) | {d.get('wall_sec', 0)}s wall | "
                f"{d.get('avg_throughput_mib_s', 0)} MiB/s mean (input bytes) | {eng}",
                "compress_summary",
            )

    def _on_compress_progress(self, pct):
        """Update window title and tray menu with compression progress (taskbar / minimized at a glance)."""
        self._compress_progress = pct
        self.setWindowTitle(f"{pct}% - {self._default_window_title}")
        if hasattr(self, "tray_compress_action"):
            self.tray_compress_action.setText(f"Compress ({pct}%)")

    def _on_compress_zip_created(self, zip_path):
        """Store path so we can delete partial zip if user cancels."""
        self._compress_zip_path = zip_path

    def _on_tray_cancel_compress_clicked(self):
        """Remove divider and Cancel from tray menu, close menu, and cancel compression (reset tray state)."""
        if hasattr(self, "tray_menu"):
            if hasattr(self, "tray_cancel_compress_action"):
                self.tray_menu.removeAction(self.tray_cancel_compress_action)
            if hasattr(self, "tray_compress_divider_action"):
                self.tray_menu.removeAction(self.tray_compress_divider_action)
            self.tray_menu.close()
        self.cancel_compress()

    def cancel_compress(self):
        """Cancel running compression; partial zip will be deleted in _on_compress_finished."""
        if not getattr(self, "compress_worker", None) or not self.compress_worker.isRunning():
            return
        self._sandbox_log("compress_exit", "Compression cancel requested.")
        self.compress_button.setText("Stopping...")
        self.compress_button.setEnabled(False)
        self.compress_worker.cancel()

    def _on_compress_finished(self, success, message):
        if self._sandbox_monitor:
            self._sandbox_log("compress_exit", f"Compression worker exit: success={success} | {message}")
        self._compress_running = False
        self._compress_cancel_requested = False
        self.compress_button.setText("Compress")
        self.compress_button.setEnabled(True)
        self._set_compress_button_compress_style()
        self.scan_button_container.setEnabled(True)
        self.add_manual_button.setEnabled(True)
        self._update_backup_compress_buttons()
        # Restore window title and tray menu (no %, re-enable Compress, remove tray divider and Cancel)
        self.setWindowTitle(self._default_window_title)
        if hasattr(self, "tray_compress_action"):
            self.tray_compress_action.setEnabled(True)
            self.tray_compress_action.setText("Compress")
        if hasattr(self, "tray_menu"):
            if hasattr(self, "tray_cancel_compress_action"):
                self.tray_menu.removeAction(self.tray_cancel_compress_action)
            if hasattr(self, "tray_compress_divider_action"):
                self.tray_menu.removeAction(self.tray_compress_divider_action)
        self.progress_bar.setRange(0, 100)
        if success:
            self.progress_bar.setValue(100)  # Fade out after 2s is handled by PurpleProgressBar.setValue(100)
            self.status_label.setText(message)
            if message == "No files to compress.":
                self.compress_summary_label.hide()
            else:
                d = self._compress_complete_metrics
                if d and self._sandbox_monitor:
                    self.compress_summary_label.setText(self._format_compression_green_summary(d))
                    self.compress_summary_label.show()
                    self._sandbox_monitor.append_compression_record(d)
                else:
                    self.compress_summary_label.hide()
            self._show_tray_notification("Compress Complete", message, QSystemTrayIcon.MessageIcon.Information, 2000)
            if getattr(self, "_compress_then_exit", False):
                self._compress_then_exit = False
                self.quit_application()
        else:
            self.compress_summary_label.hide()
            # Cancelled or failed: keep bar at current fill, then 2s delay + fade animation
            if hasattr(self.progress_bar, '_auto_hide_timer'):
                self.progress_bar._auto_hide_timer.stop()
                self.progress_bar._auto_hide_timer.start(PROGRESS_BAR_FADE_DELAY_MS)
            if message == "Cancelled" and getattr(self, "_compress_zip_path", None):
                zip_path = self._compress_zip_path
                self._compress_zip_path = None
                if os.path.isfile(zip_path):
                    try:
                        os.remove(zip_path)
                        self.status_label.setText("Compression cancelled; partial zip removed.")
                        if self._should_show_notification():
                            self._show_tray_notification("Compression cancelled", "Partial zip removed.", QSystemTrayIcon.MessageIcon.Information, 2000)
                    except OSError as e:
                        self.status_label.setText("Compression cancelled; could not remove partial zip.")
                        QMessageBox.warning(self, "Partial Zip", f"Compression was cancelled but the partial zip could not be deleted:\n{zip_path}\n\n{e}")
                else:
                    self.status_label.setText("Compression cancelled.")
                    if self._should_show_notification():
                        self._show_tray_notification("Compression cancelled", "Compression was cancelled.", QSystemTrayIcon.MessageIcon.Information, 2000)
            else:
                self.status_label.setText("Compression failed.")
                if message != "Cancelled":
                    if self._should_show_notification():
                        self._show_tray_notification("Compression failed", message, QSystemTrayIcon.MessageIcon.Warning, 2500)
                    QMessageBox.warning(self, "Compression Failed", message)
        self._compress_zip_path = None
