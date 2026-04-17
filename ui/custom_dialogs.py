from PyQt6.QtWidgets import (
    QDialog,
    QVBoxLayout,
    QHBoxLayout,
    QLabel,
    QLineEdit,
    QPushButton,
    QFileDialog,
    QDialogButtonBox,
    QCheckBox,
    QSpinBox,
    QFormLayout,
    QComboBox,
    QMessageBox,
    QWidget,
    QProgressDialog,
    QApplication,
)

from PyQt6.QtCore import QSettings, QTimer, Qt, QRect, QSize, QPropertyAnimation, QEasingCurve, pyqtProperty
from PyQt6.QtGui import QPainter, QPen, QColor
from PyQt6.QtWidgets import QStyleOptionButton, QStyle
import winreg # For Windows Registry
import sys, os
from datetime import datetime

from core.compression import find_7zip_executable
from config.app_config import DEFAULT_UI_THEME, normalize_ui_theme
from styles.manager import StyleManager
from ui.settings_framed_tabs import SettingsFramedTabs
from ui.seven_zip_install_worker import SevenZipInstallWorker
from utils.seven_zip_install import consent_summary_text
from utils.i18n import available_ui_language_codes

class CustomCheckBox(QCheckBox):
    """Custom checkbox with visible checkmark and animation"""
    def __init__(self, text="", parent=None):
        super().__init__(text, parent)
        self._checkmark_opacity = 0.0
        self._animation = QPropertyAnimation(self, b"checkmarkOpacity")
        self._animation.setDuration(150)
        self._animation.setEasingCurve(QEasingCurve.Type.OutCubic)
        self._animation.valueChanged.connect(self.update)
        
        # Connect to state changes
        self.toggled.connect(self._on_toggled)
        
        # Initialize opacity if already checked
        if self.isChecked():
            self._checkmark_opacity = 1.0
    
    def _on_toggled(self, checked):
        """Animate checkmark appearance/disappearance"""
        if checked:
            self._animation.setStartValue(0.0)
            self._animation.setEndValue(1.0)
        else:
            self._animation.setStartValue(1.0)
            self._animation.setEndValue(0.0)
        self._animation.start()
    
    def getCheckmarkOpacity(self):
        return self._checkmark_opacity
    
    def setCheckmarkOpacity(self, value):
        self._checkmark_opacity = value
        self.update()
    
    checkmarkOpacity = pyqtProperty(float, getCheckmarkOpacity, setCheckmarkOpacity)
    
    def paintEvent(self, event):
        """Custom paint to ensure checkmark is visible with smooth animation"""
        super().paintEvent(event)
        
        # Draw custom checkmark if checked (with opacity animation)
        if self.isChecked() or self._checkmark_opacity > 0:
            painter = QPainter(self)
            painter.setRenderHint(QPainter.RenderHint.Antialiasing)
            
            # Get indicator rect using style option
            opt = QStyleOptionButton()
            self.initStyleOption(opt)
            raw_rect = self.style().subElementRect(
                QStyle.SubElement.SE_CheckBoxIndicator, 
                opt, 
                self
            )
            # Add a bit of inner padding so the checkmark doesn't touch borders
            padding = 2
            indicator_rect = raw_rect.adjusted(padding, padding, -padding, -padding)
            
            # Apply opacity for smooth animation
            painter.setOpacity(self._checkmark_opacity if self._checkmark_opacity > 0 else 1.0)
            
            # Draw sleek checkmark (light on dark indicators; dark on light theme)
            if StyleManager.instance().indicator_checkmark_uses_dark_ink():
                pen = QPen(QColor(28, 28, 32), 1.4)
            else:
                pen = QPen(QColor(230, 230, 230), 1.4)
            pen.setCapStyle(Qt.PenCapStyle.RoundCap)
            pen.setJoinStyle(Qt.PenJoinStyle.RoundJoin)
            painter.setPen(pen)
            
            # Draw sleek checkmark (smoother, thinner)
            # Start from left-middle, go down-right, then up-right
            x1 = indicator_rect.x() + indicator_rect.width() * 0.2
            y1 = indicator_rect.y() + indicator_rect.height() * 0.55
            x2 = indicator_rect.x() + indicator_rect.width() * 0.45
            y2 = indicator_rect.y() + indicator_rect.height() * 0.75
            x3 = indicator_rect.x() + indicator_rect.width() * 0.8
            y3 = indicator_rect.y() + indicator_rect.height() * 0.25
            
            painter.drawLine(int(x1), int(y1), int(x2), int(y2))
            painter.drawLine(int(x2), int(y2), int(x3), int(y3))
            
            painter.end()

class AddCustomGameDialog(QDialog):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setWindowTitle("Add Custom Game")
        self.setMinimumWidth(400)
        from config.app_config import settings_app_name

        _st = QSettings("MyCompany", settings_app_name())
        StyleManager.instance().set_theme(
            normalize_ui_theme(_st.value("ui_theme", DEFAULT_UI_THEME, type=str))
        )
        StyleManager.instance().refresh()
        self.setStyleSheet(StyleManager.instance().small_dialog_qss())

        self.layout = QVBoxLayout(self)

        # Game Name Input
        self.layout.addWidget(QLabel("Game Name:"))
        self.game_name_input = QLineEdit()
        self.layout.addWidget(self.game_name_input)

        # Save Path Input
        self.layout.addWidget(QLabel("Save Game Folder Location:"))
        path_layout = QHBoxLayout()
        self.save_path_input = QLineEdit()
        path_layout.addWidget(self.save_path_input)
        
        browse_button = QPushButton("Browse...")
        browse_button.clicked.connect(self.browse_for_folder)
        path_layout.addWidget(browse_button)
        self.layout.addLayout(path_layout)

        # Add spacing between the Browse row and the bottom OK/Cancel row
        self.layout.addSpacing(20)

        # OK and Cancel buttons
        button_box = QDialogButtonBox(QDialogButtonBox.StandardButton.Ok | QDialogButtonBox.StandardButton.Cancel)
        button_box.accepted.connect(self.accept)
        button_box.rejected.connect(self.reject)
        self.layout.addWidget(button_box)

    def browse_for_folder(self):
        folder_path = QFileDialog.getExistingDirectory(self, "Select Save Game Folder")
        if folder_path:
            self.save_path_input.setText(folder_path)

    def get_data(self):
        return {
            "name": self.game_name_input.text().strip(),
            "save_path": self.save_path_input.text().strip()
        }


class FirstBackupDestinationDialog(QDialog):
    """
    Shown when user clicks Backup and no default backup folder is set.
    Message, Browse (same as Settings), OK, and "Don't show this message again".
    Saving the chosen folder to settings (default_backup_path) connects it with Settings.
    """
    def __init__(self, parent=None, initial_path=""):
        super().__init__(parent)
        self.setWindowTitle("Select Backup Destination")
        self.setMinimumWidth(400)
        from config.app_config import settings_app_name
        self.settings = QSettings("MyCompany", settings_app_name())
        self.selected_folder = ""
        self.remember_default = True
        StyleManager.instance().set_theme(
            normalize_ui_theme(self.settings.value("ui_theme", DEFAULT_UI_THEME, type=str))
        )
        StyleManager.instance().refresh()
        self.setStyleSheet(StyleManager.instance().small_dialog_qss())

        layout = QVBoxLayout(self)

        msg = QLabel(
            "No backup folder is set yet. Please select a folder where you want your game save backups to be stored.\n\n"
            "You can change this later in Settings."
        )
        msg.setWordWrap(True)
        layout.addWidget(msg)

        path_layout = QHBoxLayout()
        self.path_input = QLineEdit()
        self.path_input.setReadOnly(True)
        self.path_input.setPlaceholderText("No folder selected")
        if initial_path and os.path.exists(initial_path):
            self.path_input.setText(initial_path)
        path_layout.addWidget(self.path_input)
        browse_btn = QPushButton("Browse...")
        browse_btn.clicked.connect(self._browse)
        path_layout.addWidget(browse_btn)
        layout.addLayout(path_layout)

        layout.addSpacing(20)

        # Checkbox and buttons in one row: [checkbox] [padding] [OK] [Cancel]
        bottom_row = QHBoxLayout()
        self.dont_show_checkbox = CustomCheckBox("Don't show this message again (use this folder as default)")
        self.dont_show_checkbox.setChecked(True)
        bottom_row.addWidget(self.dont_show_checkbox)
        bottom_row.addStretch(1)
        button_box = QDialogButtonBox(QDialogButtonBox.StandardButton.Ok | QDialogButtonBox.StandardButton.Cancel)
        button_box.accepted.connect(self._on_ok)
        button_box.rejected.connect(self.reject)
        bottom_row.addWidget(button_box)
        layout.addLayout(bottom_row)

    def _browse(self):
        current = self.path_input.text() or self.settings.value("default_backup_path", "", type=str) or os.path.expanduser("~")
        folder = QFileDialog.getExistingDirectory(self, "Select Backup Destination Folder", current)
        if folder:
            self.path_input.setText(folder)

    def _on_ok(self):
        folder = self.path_input.text().strip()
        if not folder:
            QMessageBox.warning(self, "No Folder", "Please select a folder using the Browse button.")
            return
        if not os.path.exists(folder):
            QMessageBox.warning(self, "Invalid Folder", "The selected path does not exist. Please choose a valid folder.")
            return
        self.selected_folder = folder
        self.remember_default = self.dont_show_checkbox.isChecked()
        if self.remember_default:
            self.settings.setValue("default_backup_path", folder)
            self.settings.setValue("last_backup_path", folder)
        self.accept()

    def get_folder(self):
        return self.selected_folder

    def get_remember_default(self):
        return self.remember_default


class SettingsDialog(QDialog):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setWindowTitle("Settings")

        from config.app_config import settings_app_name
        self.settings = QSettings("MyCompany", settings_app_name())
        self._7z_worker = None
        self._7z_progress_dlg = None
        self._tooltip_app_style_applied = False
        self._app_qss_snapshot_before_settings = ""
        self.finished.connect(self._restore_settings_tooltip_app_style)

        # Fixed dialog dimensions
        self.setMinimumWidth(470)
        self.setMaximumWidth(470)
        self.setMinimumHeight(510)
        self.setMaximumHeight(510)

        _sm = StyleManager.instance()
        _sm.set_theme(normalize_ui_theme(self.settings.value("ui_theme", DEFAULT_UI_THEME, type=str)))
        _sm.refresh()
        self.setStyleSheet(_sm.settings_dialog_qss())

        self.main_layout = QVBoxLayout(self)
        spacing_px = 9
        self.main_layout.setContentsMargins(spacing_px, spacing_px, spacing_px, spacing_px)
        self.main_layout.setSpacing(spacing_px)

        self._settings_tabs = SettingsFramedTabs()

        # --- Backup settings ---
        backup_tab = QWidget()
        backup_tab.setObjectName("settingsTabPage")
        backup_outer = QVBoxLayout(backup_tab)
        backup_outer.setContentsMargins(0, 0, 0, 0)
        backup_form = QFormLayout()
        backup_form.setSpacing(spacing_px)
        backup_form.setContentsMargins(0, 0, 0, 0)
        backup_form_bottom = QFormLayout()
        backup_form_bottom.setSpacing(spacing_px)
        backup_form_bottom.setContentsMargins(0, 0, 0, 0)

        self.auto_backup_checkbox = CustomCheckBox("Enable Automatic Backups")
        self.auto_backup_checkbox.setChecked(self.settings.value("auto_backup_enabled", False, type=bool))
        backup_form.addRow(self.auto_backup_checkbox)

        retention_label = QLabel("Number of recent backups to keep:")
        retention_label.setStyleSheet(
            f"padding-left: 20px; color: {_sm.settings_secondary_label_color()};"
        )
        self.backup_retention_spinbox = QSpinBox()
        self.backup_retention_spinbox.setMinimum(1)
        self.backup_retention_spinbox.setMaximum(100)
        self.backup_retention_spinbox.setValue(self.settings.value("backup_retention_count", 3, type=int))
        self.backup_retention_spinbox.setMaximumWidth(120)
        backup_form.addRow(retention_label, self.backup_retention_spinbox)

        frequency_label = QLabel("Auto-backup frequency (minutes):")
        frequency_label.setStyleSheet(
            f"padding-left: 20px; color: {_sm.settings_secondary_label_color()};"
        )
        self.backup_frequency_spinbox = QSpinBox()
        self.backup_frequency_spinbox.setMinimum(1)
        self.backup_frequency_spinbox.setMaximum(1440)
        self.backup_frequency_spinbox.setValue(self.settings.value("backup_frequency_minutes", 5, type=int))
        self.backup_frequency_spinbox.setToolTip("Minimum time between automatic backups for the same game (prevents excessive backups)")
        self.backup_frequency_spinbox.setMaximumWidth(120)
        backup_form.addRow(frequency_label, self.backup_frequency_spinbox)

        def update_auto_backup_state(enabled):
            self.backup_retention_spinbox.setEnabled(enabled)
            self.backup_frequency_spinbox.setEnabled(enabled)
            sm2 = StyleManager.instance()
            c = (
                sm2.settings_secondary_label_color()
                if enabled
                else sm2.settings_disabled_muted_color()
            )
            retention_label.setStyleSheet(f"padding-left: 20px; color: {c};")
            frequency_label.setStyleSheet(f"padding-left: 20px; color: {c};")
        self.auto_backup_checkbox.toggled.connect(update_auto_backup_state)
        update_auto_backup_state(self.auto_backup_checkbox.isChecked())

        self.confirm_before_backup_checkbox = CustomCheckBox("Ask for confirmation when clicking Backup")
        self.confirm_before_backup_checkbox.setChecked(self.settings.value("confirm_before_backup", False, type=bool))
        self.confirm_before_backup_checkbox.setToolTip("Show a confirmation dialog before starting a backup.")
        backup_form.addRow(self.confirm_before_backup_checkbox)

        self.show_backup_estimate_checkbox = CustomCheckBox("Show backup size estimate before starting backup")
        self.show_backup_estimate_checkbox.setChecked(self.settings.value("show_backup_estimate", True, type=bool))
        self.show_backup_estimate_checkbox.setToolTip(
            "Counts files and sizes under each save folder (and notes registry-only games) before copying."
        )
        backup_form.addRow(self.show_backup_estimate_checkbox)

        self.ask_compress_on_exit_checkbox = CustomCheckBox("Ask to compress backups when closing")
        self.ask_compress_on_exit_checkbox.setChecked(self.settings.value("ask_compress_on_exit", True, type=bool))
        self.ask_compress_on_exit_checkbox.setToolTip("When exiting (not minimizing to tray), suggest compressing the backup folder to save space.")
        backup_form.addRow(self.ask_compress_on_exit_checkbox)

        self.backup_subfolder_per_game_checkbox = CustomCheckBox("Store backups in a subfolder per game")
        self.backup_subfolder_per_game_checkbox.setChecked(self.settings.value("backup_subfolder_per_game", True, type=bool))
        self.backup_subfolder_per_game_checkbox.setToolTip("e.g. BackupFolder/GameName/GameName - Backup 2025-01-29 instead of BackupFolder/GameName - Backup 2025-01-29")
        backup_form.addRow(self.backup_subfolder_per_game_checkbox)

        self.skip_not_found_checkbox = CustomCheckBox("Skip games with no save files in subsequent scans")
        self.skip_not_found_checkbox.setChecked(self.settings.value("skip_not_found_games", True, type=bool))
        backup_form.addRow(self.skip_not_found_checkbox)

        backup_outer.addLayout(backup_form)
        backup_outer.addStretch(1)

        backup_folder_label = QLabel("Default backup folder:")
        self.backup_folder_layout = QHBoxLayout()
        self.backup_folder_layout.setContentsMargins(0, 0, 0, 0)
        self.backup_folder_layout.setSpacing(spacing_px)
        self.backup_folder_input = QLineEdit()
        self.backup_folder_input.setText(self.settings.value("default_backup_path", "", type=str))
        self.backup_folder_input.setReadOnly(True)
        self.backup_folder_input.setStyleSheet(_sm.settings_path_input_style())
        browse_backup_button = QPushButton("Browse...")
        browse_backup_button.clicked.connect(self.browse_backup_folder)
        self.backup_folder_layout.addWidget(self.backup_folder_input)
        self.backup_folder_layout.addWidget(browse_backup_button)
        backup_form_bottom.addRow(backup_folder_label, self.backup_folder_layout)

        date_format_label = QLabel("Backup date format:")
        self.date_format_combo = QComboBox()
        self.date_format_combo.addItem("ISO (YYYY-MM-DD HH:MM)", "iso")
        self.date_format_combo.addItem("US (MM/DD/YYYY HH:MM AM/PM)", "us")
        self.date_format_combo.addItem("European (DD/MM/YYYY HH:MM)", "european")
        self.date_format_combo.addItem("Asian (YYYY/MM/DD HH:MM)", "asian")
        self.date_format_combo.setMaximumWidth(235)
        saved_format = self.settings.value("date_format", "iso", type=str)
        index = self.date_format_combo.findData(saved_format)
        if index >= 0:
            self.date_format_combo.setCurrentIndex(index)
        backup_form_bottom.addRow(date_format_label, self.date_format_combo)

        preview_row = QHBoxLayout()
        preview_row.setContentsMargins(0, 0, 0, 0)
        preview_row.setSpacing(spacing_px)
        preview_prefix = QLabel("Preview ")
        preview_prefix.setStyleSheet(f"color: {_sm.settings_secondary_label_color()};")
        self.date_preview_label = QLabel()
        self.date_preview_label.setStyleSheet(
            f"color: {_sm.settings_muted_hint_color()}; font-style: italic;"
        )
        preview_row.addWidget(preview_prefix)
        preview_row.addWidget(self.date_preview_label)
        preview_row.addStretch(1)
        preview_widget = QWidget()
        preview_widget.setObjectName("settingsDatePreview")
        preview_widget.setLayout(preview_row)
        backup_form_bottom.addRow("", preview_widget)
        self.update_date_preview()
        self.date_format_combo.currentIndexChanged.connect(self.update_date_preview)

        backup_outer.addLayout(backup_form_bottom)
        self._settings_tabs.addTab(backup_tab, "Backup settings")

        # --- Archive compression (Compress button) ---
        compress_tab = QWidget()
        compress_tab.setObjectName("settingsTabPage")
        compress_outer = QVBoxLayout(compress_tab)
        compress_outer.setContentsMargins(0, 0, 0, 0)
        compress_form = QFormLayout()
        compress_form.setSpacing(spacing_px)
        compress_form.setContentsMargins(0, 0, 0, 0)

        self.compression_preset_combo = QComboBox()
        self.compression_preset_combo.setMinimumWidth(300)
        for label, key in (
            ("Store (no compression, fastest)", "store"),
            ("ZIP - fast deflate (~1 CPU core)", "deflate_fast"),
            ("ZIP - balanced deflate (default, ~1 core)", "deflate_balanced"),
            ("ZIP - max deflate (~1 core, smaller)", "deflate_max"),
            ("7-Zip engine (.7z or .zip via 7z.exe)", "seven_zip"),
        ):
            self.compression_preset_combo.addItem(label, key)
        saved_preset = self.settings.value("compression_preset", "deflate_balanced", type=str)
        pidx = self.compression_preset_combo.findData(saved_preset)
        if pidx >= 0:
            self.compression_preset_combo.setCurrentIndex(pidx)
        _engine_tt = (
            "How the Compress button packs your default backup folder.\n\n"
            "Store: no compression, largest output.\n\n"
            "ZIP fast / balanced / max: Python's built-in ZIP (Deflate), single-threaded; "
            "no extra software.\n\n"
            "7-Zip engine: runs 7z.exe for .7z (LZMA2, strong multithreading) or .zip "
            "(better MT with many files). Requires 7-Zip installed, or use Get 7-Zip on Windows."
        )
        self.compression_preset_combo.setToolTip(_engine_tt)

        self._lbl_compression_engine = QLabel("Compression engine:")
        self._lbl_compression_engine.setToolTip(_engine_tt)
        compress_form.addRow(self._lbl_compression_engine, self.compression_preset_combo)

        self.get_7zip_button = QPushButton("Get 7-Zip…")
        self.get_7zip_button.setToolTip(
            "Download and silently install a pinned official 7-Zip build (confirmation explains details)."
        )
        self.get_7zip_button.clicked.connect(self._on_get_7zip_clicked)

        self._compress_engine_slot = QWidget()
        self._compress_engine_slot.setObjectName("settingsCompressPromoSection")
        self._compress_engine_slot.setAttribute(Qt.WidgetAttribute.WA_StyledBackground, True)
        self._compress_engine_slot.setMinimumHeight(44)
        _engine_slot_layout = QVBoxLayout(self._compress_engine_slot)
        _engine_slot_layout.setContentsMargins(0, spacing_px // 2, 0, spacing_px // 2)
        _engine_slot_layout.setSpacing(0)
        _engine_slot_layout.addStretch(1)
        _get_7z_btn_row = QHBoxLayout()
        _get_7z_btn_row.setContentsMargins(0, 0, 0, 0)
        _get_7z_btn_row.setSpacing(0)
        _get_7z_btn_row.addStretch(1)
        _get_7z_btn_row.addWidget(self.get_7zip_button)
        _get_7z_btn_row.addStretch(1)
        _engine_slot_layout.addLayout(_get_7z_btn_row)
        _engine_slot_layout.addStretch(1)
        compress_form.addRow(self._compress_engine_slot)

        self.compression_7z_hint = QLabel()
        self.compression_7z_hint.setObjectName("compression7zHint")
        self.compression_7z_hint.setWordWrap(True)
        self.compression_7z_hint.setAlignment(
            Qt.AlignmentFlag.AlignHCenter | Qt.AlignmentFlag.AlignTop
        )
        self.compression_7z_hint.setStyleSheet(
            f"color: {_sm.settings_muted_hint_color()}; font-size: 10px;"
            " background: transparent; border: none; padding: 0px;"
        )

        self._compress_lbl_7z_format = QLabel("7-Zip output format:")
        _fmt_tt = (
            "Used only when Compression engine is 7-Zip.\n\n"
            ".7z (LZMA2): best compression and CPU use for large backups; "
            "opens in 7-Zip and most archivers.\n\n"
            ".zip (Deflate): maximum compatibility; multithreading helps most when "
            "there are many separate files rather than one huge file."
        )
        self._compress_lbl_7z_format.setToolTip(_fmt_tt)
        self.compression_7z_format_combo = QComboBox()
        self.compression_7z_format_combo.setMinimumWidth(300)
        for label, key in (
            (".7z archive (LZMA2, multithreaded, recommended)", "7z"),
            (".zip archive (Deflate, portable)", "zip"),
        ):
            self.compression_7z_format_combo.addItem(label, key)
        self.compression_7z_format_combo.setToolTip(_fmt_tt)
        zfmt = self.settings.value("compression_7z_format", "7z", type=str)
        zix = self.compression_7z_format_combo.findData(zfmt)
        if zix >= 0:
            self.compression_7z_format_combo.setCurrentIndex(zix)
        compress_form.addRow(self._compress_lbl_7z_format, self.compression_7z_format_combo)

        self._compress_lbl_7z_mx = QLabel("7-Zip level (-mx):")
        self.compression_7z_mx_spin = QSpinBox()
        self.compression_7z_mx_spin.setRange(0, 9)
        self.compression_7z_mx_spin.setValue(self.settings.value("compression_7z_level", 5, type=int))
        self.compression_7z_mx_spin.setToolTip(
            "0 = copy/store … 9 = smallest/slowest. For .7z LZMA2, 5–7 is a good balance; 9 is very heavy."
        )
        compress_form.addRow(self._compress_lbl_7z_mx, self.compression_7z_mx_spin)

        self._compress_lbl_7z_threads = QLabel("7-Zip threads (-mmt):")
        self.compression_7z_threads_spin = QSpinBox()
        self.compression_7z_threads_spin.setRange(0, 128)
        self.compression_7z_threads_spin.setSpecialValueText("Auto")
        self.compression_7z_threads_spin.setValue(self.settings.value("compression_7z_threads", 0, type=int))
        self.compression_7z_threads_spin.setToolTip("0 = Auto (7-Zip uses all logical cores). Set e.g. 16 to cap load.")
        compress_form.addRow(self._compress_lbl_7z_threads, self.compression_7z_threads_spin)

        self.compression_7z_path_input = QLineEdit()
        self.compression_7z_path_input.setText(self.settings.value("compression_7z_path", "", type=str))
        self.compression_7z_path_input.setReadOnly(True)
        self.compression_7z_path_input.setPlaceholderText("Leave empty to auto-detect 7z.exe")
        self.compression_7z_path_input.setStyleSheet(_sm.settings_path_input_style())
        self._browse_7z_btn = QPushButton("Browse…")
        self._browse_7z_btn.clicked.connect(self._browse_7zip_exe)
        path_7z_row = QHBoxLayout()
        path_7z_row.setContentsMargins(0, 0, 0, 0)
        path_7z_row.setSpacing(spacing_px)
        path_7z_row.addWidget(self.compression_7z_path_input)
        path_7z_row.addWidget(self._browse_7z_btn)
        path_7z_w = QWidget()
        path_7z_w.setObjectName("settingsSevenZipPathRow")
        path_7z_w.setLayout(path_7z_row)
        self._lbl_7zip_exe = QLabel("7-Zip executable:")
        self._lbl_7zip_exe.setObjectName("settingsSevenZipExeLabel")
        compress_form.addRow(self._lbl_7zip_exe, path_7z_w)

        self.compression_preset_combo.currentIndexChanged.connect(self._sync_compression_sub_ui)
        self.compression_7z_format_combo.currentIndexChanged.connect(self._sync_compression_sub_ui)
        self.compression_7z_path_input.textChanged.connect(self._sync_compression_sub_ui)
        self._sync_compression_sub_ui()

        compress_outer.addLayout(compress_form)
        compress_outer.addStretch(1)
        compress_outer.addWidget(self.compression_7z_hint)
        self._settings_tabs.addTab(compress_tab, "Compress backups")

        # --- Themes ---
        themes_tab = QWidget()
        themes_tab.setObjectName("settingsTabPage")
        themes_outer = QVBoxLayout(themes_tab)
        themes_outer.setContentsMargins(0, 0, 0, 0)
        themes_form = QFormLayout()
        themes_form.setSpacing(spacing_px)
        themes_form.setContentsMargins(0, 0, 0, 0)

        self.theme_combo = QComboBox()
        self.theme_combo.setMinimumWidth(300)
        self.theme_combo.addItem("Match system (Default)", "system")
        self.theme_combo.addItem("Dark", "default")
        self.theme_combo.addItem("Light", "light")
        saved_theme = normalize_ui_theme(self.settings.value("ui_theme", DEFAULT_UI_THEME, type=str))
        tidx = self.theme_combo.findData(saved_theme)
        self.theme_combo.setCurrentIndex(tidx if tidx >= 0 else 0)
        themes_form.addRow(QLabel("Application theme:"), self.theme_combo)

        self.lang_combo = QComboBox()
        self.lang_combo.setMinimumWidth(300)
        for lbl, code in available_ui_language_codes():
            self.lang_combo.addItem(lbl, code)
        lang_cur = (self.settings.value("ui_language", "en", type=str) or "en").strip().lower()
        lix = self.lang_combo.findData(lang_cur)
        self.lang_combo.setCurrentIndex(lix if lix >= 0 else 0)
        themes_form.addRow(QLabel("Language:"), self.lang_combo)

        lang_hint = QLabel(
            "Translations use Qt .qm files in a translations folder next to the app. "
            "Only English is bundled for now; see utils/i18n.py to add locales."
        )
        lang_hint.setWordWrap(True)
        lang_hint.setStyleSheet(f"color: {_sm.settings_muted_hint_color()}; font-size: 10px;")
        themes_form.addRow("", lang_hint)

        theme_hint = QLabel(
            "Match system (Default) follows Settings → Personalization → Colors (Windows light or dark app mode). "
            "Dark and Light are fixed palettes."
        )
        theme_hint.setWordWrap(True)
        theme_hint.setStyleSheet(f"color: {_sm.settings_muted_hint_color()}; font-size: 10px;")
        themes_form.addRow("", theme_hint)

        themes_outer.addLayout(themes_form)
        themes_outer.addStretch(1)
        self._settings_tabs.addTab(themes_tab, "Themes")

        # --- System settings ---
        system_tab = QWidget()
        system_tab.setObjectName("settingsTabPage")
        system_outer = QVBoxLayout(system_tab)
        system_outer.setContentsMargins(0, 0, 0, 0)
        system_form = QFormLayout()
        system_form.setSpacing(spacing_px)
        system_form.setContentsMargins(0, 0, 0, 0)

        self.notifications_checkbox = CustomCheckBox("Enable notifications")
        self.notifications_checkbox.setChecked(self.settings.value("notifications_enabled", True, type=bool))
        system_form.addRow(self.notifications_checkbox)

        self.notification_sound_checkbox = CustomCheckBox("Play notification sounds")
        self.notification_sound_checkbox.setStyleSheet("padding-left: 20px;")
        self.notification_sound_checkbox.setChecked(self.settings.value("notification_sound_enabled", True, type=bool))
        self.notification_sound_checkbox.setToolTip("Control notification sounds (Windows Toast notifications are always used when available)")
        system_form.addRow(self.notification_sound_checkbox)

        def update_notification_state(enabled):
            self.notification_sound_checkbox.setEnabled(enabled)
            sm3 = StyleManager.instance()
            c3 = (
                sm3.settings_primary_on_dialog_color()
                if enabled
                else sm3.settings_disabled_muted_color()
            )
            self.notification_sound_checkbox.setStyleSheet(f"padding-left: 20px; color: {c3};")
        self.notifications_checkbox.toggled.connect(update_notification_state)
        update_notification_state(self.notifications_checkbox.isChecked())

        self.minimize_to_tray_checkbox = CustomCheckBox("Minimize to system tray instead of closing")
        self.minimize_to_tray_checkbox.setChecked(self.settings.value("minimize_to_tray", True, type=bool))
        system_form.addRow(self.minimize_to_tray_checkbox)

        startup_label = QLabel("Run on Windows start-up:")
        self.startup_mode_combo = QComboBox()
        self.startup_mode_combo.addItem("Don't run on startup", "disabled")
        self.startup_mode_combo.addItem("Normal", "normal")
        self.startup_mode_combo.addItem("Minimized", "minimized")
        self.startup_mode_combo.addItem("Hidden", "hidden")
        self.startup_mode_combo.setMaximumWidth(200)
        saved_mode = self.settings.value("run_on_startup_mode", None, type=str)
        if not saved_mode and self.settings.value("run_on_startup_enabled", False, type=bool):
            saved_mode = "minimized"  # backward compat: old "enabled" = minimized
        if saved_mode:
            idx = self.startup_mode_combo.findData(saved_mode)
            if idx >= 0:
                self.startup_mode_combo.setCurrentIndex(idx)
        system_form.addRow(startup_label, self.startup_mode_combo)

        system_outer.addLayout(system_form)
        system_outer.addStretch(1)
        self._settings_tabs.addTab(system_tab, "System settings")

        self.main_layout.addWidget(self._settings_tabs, 1)

        # Bottom row with buttons and version info (9px gap is handled by main_layout spacing)
        bottom_layout = QHBoxLayout()
        bottom_layout.setContentsMargins(0, 0, 0, 0)
        bottom_layout.setSpacing(spacing_px)
        version_layout = QHBoxLayout()
        version_layout.setContentsMargins(0, 0, 0, 0)
        version_layout.setSpacing(spacing_px)
        version_label = QLabel("v1.5.2.190825")
        version_label.setStyleSheet(
            f"font-size: 9px; color: {_sm.settings_version_muted_color()};"
        )
        version_layout.addWidget(version_label)
        copyright_label = QLabel("© Xeworth")
        copyright_label.setStyleSheet(
            f"font-size: 9px; color: {_sm.settings_version_muted_color()};"
        )
        version_layout.addWidget(copyright_label)
        bottom_layout.addLayout(version_layout)
        bottom_layout.addStretch(1)
        button_box = QDialogButtonBox(QDialogButtonBox.StandardButton.Ok | QDialogButtonBox.StandardButton.Cancel)
        button_box.accepted.connect(self.accept)
        button_box.rejected.connect(self.reject)
        bottom_layout.addWidget(button_box)
        self.main_layout.addLayout(bottom_layout)

    def update_date_preview(self):
        """Update the date format preview with current time"""
        format_key = self.date_format_combo.currentData()
        now = datetime.now()
        
        if format_key == "iso":
            preview_text = now.strftime("%Y-%m-%d | %H:%M")
        elif format_key == "us":
            preview_text = now.strftime("%m/%d/%Y | %I:%M %p")
        elif format_key == "european":
            preview_text = now.strftime("%d/%m/%Y | %H:%M")
        elif format_key == "asian":
            preview_text = now.strftime("%Y/%m/%d | %H:%M")
        else:
            preview_text = now.strftime("%Y-%m-%d | %H:%M")
        
        self.date_preview_label.setText(preview_text)
    
    def browse_backup_folder(self):
        """Browse for default backup folder"""
        current_path = self.backup_folder_input.text()
        folder_path = QFileDialog.getExistingDirectory(self, "Select Default Backup Folder", current_path)
        if folder_path:
            self.backup_folder_input.setText(folder_path)

    def _browse_7zip_exe(self):
        path, _ = QFileDialog.getOpenFileName(
            self,
            "Select 7-Zip executable",
            self.compression_7z_path_input.text() or "C:\\Program Files\\7-Zip",
            "Executable (7z.exe);;All files (*.*)",
        )
        if path:
            self.compression_7z_path_input.setText(path)

    def _compression_has_usable_7zip(self) -> bool:
        custom = self.compression_7z_path_input.text().strip().strip('"')
        if custom and os.path.isfile(custom):
            return True
        return find_7zip_executable() is not None

    def showEvent(self, event):
        super().showEvent(event)
        if self._tooltip_app_style_applied:
            return
        app = QApplication.instance()
        if app is None:
            return
        self._app_qss_snapshot_before_settings = app.styleSheet()
        frag = StyleManager.instance().settings_tooltip_supplement_qss()
        app.setStyleSheet(self._app_qss_snapshot_before_settings + "\n" + frag)
        self._tooltip_app_style_applied = True

    def _restore_settings_tooltip_app_style(self, *_args):
        if not getattr(self, "_tooltip_app_style_applied", False):
            return
        app = QApplication.instance()
        if app is not None:
            app.setStyleSheet(self._app_qss_snapshot_before_settings)
        self._tooltip_app_style_applied = False

    def _sync_compression_sub_ui(self):
        is7 = self.compression_preset_combo.currentData() == "seven_zip"
        self._compress_lbl_7z_format.setEnabled(is7)
        self.compression_7z_format_combo.setEnabled(is7)
        self._compress_lbl_7z_mx.setEnabled(is7)
        self._compress_lbl_7z_threads.setEnabled(is7)
        self.compression_7z_mx_spin.setEnabled(is7)
        self.compression_7z_threads_spin.setEnabled(is7)
        self.compression_7z_path_input.setEnabled(is7)
        self._browse_7z_btn.setEnabled(is7)
        self._lbl_7zip_exe.setEnabled(is7)

        busy = self._7z_worker is not None and self._7z_worker.isRunning()

        if not is7:
            self.get_7zip_button.setVisible(False)
            self.get_7zip_button.setEnabled(False)
            self.compression_7z_hint.setText(
                "Store and ZIP options use Python's built-in ZIP (single CPU core). "
                "Choose 7-Zip engine to use 7z.exe for .7z or .zip, then set format, level, and threads below."
            )
            return

        has_7z = self._compression_has_usable_7zip()
        show_get_btn = sys.platform == "win32" and not has_7z
        self.get_7zip_button.setVisible(show_get_btn)
        self.get_7zip_button.setEnabled(show_get_btn and not busy)

        custom = self.compression_7z_path_input.text().strip().strip('"')
        if custom and os.path.isfile(custom):
            det = custom
        else:
            det = find_7zip_executable() or "not found"
        zf = self.compression_7z_format_combo.currentData()
        if zf == "7z":
            extra = "With .7z + LZMA2, all CPU cores are used efficiently. "
        else:
            extra = "With .zip + Deflate, multithreading helps most when there are many separate files. "
        if det == "not found":
            tail = (
                "No 7-Zip executable found. Use Get 7-Zip (Windows) in the slot above, or Browse to point at 7z.exe."
                if sys.platform == "win32"
                else "No 7-Zip executable found. Install 7-Zip for your platform or use Browse to point at 7z.exe."
            )
        else:
            tail = f"Effective 7-Zip binary: {det}"
        self.compression_7z_hint.setText((extra + tail).strip())

    def _on_get_7zip_clicked(self):
        if sys.platform != "win32":
            QMessageBox.information(
                self,
                "Get 7-Zip",
                "Automatic download and install is only supported on Windows.",
            )
            return
        if self._7z_worker is not None and self._7z_worker.isRunning():
            return

        consent = QMessageBox(self)
        consent.setIcon(QMessageBox.Icon.Information)
        consent.setWindowTitle("Get 7-Zip")
        consent.setText("Download and install 7-Zip automatically?")
        consent.setInformativeText(consent_summary_text())
        consent.setStandardButtons(QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.No)
        consent.setDefaultButton(QMessageBox.StandardButton.No)
        if consent.exec() != QMessageBox.StandardButton.Yes:
            return

        self._7z_progress_dlg = QProgressDialog(self)
        self._7z_progress_dlg.setWindowTitle("Getting 7-Zip")
        self._7z_progress_dlg.setLabelText("Starting…")
        self._7z_progress_dlg.setCancelButtonText("Cancel")
        self._7z_progress_dlg.setRange(0, 100)
        self._7z_progress_dlg.setValue(0)
        self._7z_progress_dlg.setMinimumDuration(0)
        self._7z_progress_dlg.setWindowModality(Qt.WindowModality.WindowModal)
        self._7z_progress_dlg.canceled.connect(self._on_7z_install_cancel)

        self._sync_compression_sub_ui()
        self._7z_worker = SevenZipInstallWorker(self)
        self._7z_worker.progress.connect(self._on_7z_install_progress)
        self._7z_worker.succeeded.connect(self._on_7z_install_succeeded)
        self._7z_worker.failed.connect(self._on_7z_install_failed)
        self._7z_worker.finished.connect(self._on_7z_install_thread_finished)
        self._7z_worker.start()
        self._7z_progress_dlg.show()

    def _on_7z_install_cancel(self):
        if self._7z_worker is not None:
            self._7z_worker.request_cancel()

    def _on_7z_install_progress(self, pct: int, msg: str):
        if self._7z_progress_dlg is not None:
            self._7z_progress_dlg.setLabelText(msg)
            self._7z_progress_dlg.setValue(pct)

    def _on_7z_install_succeeded(self, exe: str):
        if self._7z_progress_dlg is not None:
            self._7z_progress_dlg.setValue(100)
            self._7z_progress_dlg.close()
            self._7z_progress_dlg = None
        QMessageBox.information(
            self,
            "7-Zip",
            f"7-Zip is installed and ready to use.\n\n{exe}\n\n"
            "The install folder was added to your user PATH for new Command Prompt or PowerShell windows. "
            "This app also finds 7-Zip in Program Files without PATH.\n\n"
            'Tip: choose the "7-Zip engine" profile and .7z archive type for best multithreaded compression.',
        )
        self.compression_7z_path_input.clear()
        self._sync_compression_sub_ui()

    def _on_7z_install_failed(self, msg: str):
        if self._7z_progress_dlg is not None:
            self._7z_progress_dlg.close()
            self._7z_progress_dlg = None
        QMessageBox.warning(self, "7-Zip", msg)
        self._sync_compression_sub_ui()

    def _on_7z_install_thread_finished(self):
        if self._7z_progress_dlg is not None:
            self._7z_progress_dlg.close()
            self._7z_progress_dlg = None
        self._7z_worker = None
        self._sync_compression_sub_ui()

    def accept(self):
        new_startup_mode = self.startup_mode_combo.currentData()
        old_startup_mode = self.settings.value("run_on_startup_mode", None, type=str) or (
            "minimized" if self.settings.value("run_on_startup_enabled", False, type=bool) else "disabled"
        )

        self.settings.setValue("auto_backup_enabled", self.auto_backup_checkbox.isChecked())
        self.settings.setValue("backup_retention_count", self.backup_retention_spinbox.value())
        self.settings.setValue("backup_frequency_minutes", self.backup_frequency_spinbox.value())
        self.settings.setValue("notifications_enabled", self.notifications_checkbox.isChecked())
        self.settings.setValue("notification_sound_enabled", self.notification_sound_checkbox.isChecked())
        self.settings.setValue("run_on_startup_mode", new_startup_mode)
        self.settings.setValue("run_on_startup_enabled", new_startup_mode != "disabled")
        self.settings.setValue("minimize_to_tray", self.minimize_to_tray_checkbox.isChecked())
        self.settings.setValue("skip_not_found_games", self.skip_not_found_checkbox.isChecked())
        self.settings.setValue("confirm_before_backup", self.confirm_before_backup_checkbox.isChecked())
        self.settings.setValue("show_backup_estimate", self.show_backup_estimate_checkbox.isChecked())
        self.settings.setValue("ask_compress_on_exit", self.ask_compress_on_exit_checkbox.isChecked())
        self.settings.setValue("backup_subfolder_per_game", self.backup_subfolder_per_game_checkbox.isChecked())
        self.settings.setValue("date_format", self.date_format_combo.currentData())
        self.settings.setValue("default_backup_path", self.backup_folder_input.text())
        self.settings.setValue("compression_preset", self.compression_preset_combo.currentData())
        self.settings.setValue("compression_7z_format", self.compression_7z_format_combo.currentData())
        self.settings.setValue("compression_7z_level", self.compression_7z_mx_spin.value())
        self.settings.setValue("compression_7z_threads", self.compression_7z_threads_spin.value())
        self.settings.setValue("compression_7z_path", self.compression_7z_path_input.text().strip())
        self.settings.setValue("ui_theme", self.theme_combo.currentData())
        self.settings.setValue("ui_language", self.lang_combo.currentData())

        if old_startup_mode != new_startup_mode:
            self.set_startup(new_startup_mode)

        super().accept()
    
    def closeEvent(self, event):
        """Stop the preview timer when dialog closes"""
        if self._7z_worker is not None and self._7z_worker.isRunning():
            QMessageBox.information(
                self,
                "7-Zip",
                "Wait for the 7-Zip install to finish, or press Cancel on the progress window.",
            )
            event.ignore()
            return
        if hasattr(self, "preview_timer"):
            self.preview_timer.stop()
        super().closeEvent(event)

    def set_startup(self, mode):
        """Set or remove Windows startup entry. mode: 'disabled' | 'normal' | 'minimized' | 'hidden'."""
        app_name = "GameSaveBackupTool"
        main_script_path = os.path.abspath("main.py")
        python_executable = sys.executable.replace("python.exe", "pythonw.exe")
        base_path = f'"{python_executable}" "{main_script_path}"'
        if mode == "minimized":
            app_path = f"{base_path} --minimized"
        elif mode == "hidden":
            app_path = f"{base_path} --hidden"
        else:
            app_path = base_path

        try:
            key = winreg.OpenKey(winreg.HKEY_CURRENT_USER, r"Software\Microsoft\Windows\CurrentVersion\Run", 0, winreg.KEY_ALL_ACCESS)
            if mode != "disabled":
                print(f"Adding to startup: {app_path}")
                winreg.SetValueEx(key, app_name, 0, winreg.REG_SZ, app_path)
            else:
                print("Removing from startup.")
                try:
                    winreg.DeleteValue(key, app_name)
                except FileNotFoundError:
                    pass
            winreg.CloseKey(key)
        except Exception as e:
            print(f"Error setting startup registry key: {e}")

    