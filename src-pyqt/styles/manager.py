"""
Central accent-aware styles. Reads Windows accent, builds QSS strings for main window and dialogs.
"""

from __future__ import annotations

from typing import ClassVar, Optional, Tuple

from PyQt6.QtGui import QColor

from config.app_config import normalize_ui_theme
from utils.system_info import get_windows_accent_rgb
from utils.windows_theme import windows_apps_use_light_theme


def _clamp_byte(x: float) -> int:
    return max(0, min(255, int(round(x))))


def _rgb_hex(r: int, g: int, b: int) -> str:
    return f"#{r:02x}{g:02x}{b:02x}"


def _mix_rgb(
    r1: int, g1: int, b1: int, r2: int, g2: int, b2: int, t: float
) -> Tuple[int, int, int]:
    t = max(0.0, min(1.0, t))
    return (
        _clamp_byte(r1 * (1 - t) + r2 * t),
        _clamp_byte(g1 * (1 - t) + g2 * t),
        _clamp_byte(b1 * (1 - t) + b2 * t),
    )


class StyleManager:
    """Singleton-style accessor via ``instance()``; call ``refresh()`` before building QSS."""

    _instance: ClassVar[Optional["StyleManager"]] = None

    def __init__(self) -> None:
        self._r: int = 148
        self._g: int = 0
        self._b: int = 211
        self._theme: str = "default"
        self.refresh()

    @classmethod
    def instance(cls) -> "StyleManager":
        if cls._instance is None:
            cls._instance = cls()
        return cls._instance

    def refresh(self) -> None:
        self._r, self._g, self._b = get_windows_accent_rgb()

    def set_theme(self, theme: str | None) -> None:
        self._theme = normalize_ui_theme(theme)

    def theme(self) -> str:
        return self._theme

    def is_system_theme(self) -> bool:
        return self._theme == "system"

    def is_light_theme(self) -> bool:
        """True for fixed ``light``, or ``system`` when Windows app mode is light."""
        if self._theme == "light":
            return True
        if self._theme == "system":
            return windows_apps_use_light_theme()
        return False

    def uses_vivid_success_error_buttons(self) -> bool:
        """Green/red action buttons use saturated fills + dark text (not white on muted)."""
        return self.is_light_theme()

    def ui_font_family_css(self) -> str:
        """QSS font stack for UI chrome (Windows-first)."""
        return (
            '"Segoe UI", "Segoe UI Variable", "Segoe UI Variable Text", '
            "Tahoma, sans-serif"
        )

    def ui_body_text_dark(self) -> str:
        """Primary text on dark chrome (buttons, labels, menus) — one consistent shade."""
        return "#ebebed"

    def ui_body_text_light(self) -> str:
        """Primary text on light chrome."""
        return "#141418"

    def indicator_checkmark_uses_dark_ink(self) -> bool:
        """Custom painted checkmark should be dark (light-ish checkbox surface)."""
        return self.is_light_theme()

    def sandbox_panel_is_bright(self) -> bool:
        """Sandbox text areas / hints use the light palette."""
        return self.is_light_theme()

    def progress_bar_track_color(self) -> QColor:
        """Groove behind the accent fill (``PurpleProgressBar`` paints this)."""
        if self.is_light_theme():
            return QColor(236, 236, 240)
        return QColor(26, 26, 26)

    @property
    def rgb(self) -> Tuple[int, int, int]:
        return (self._r, self._g, self._b)

    def accent_qcolor(self) -> QColor:
        return QColor(self._r, self._g, self._b)

    def accent_hex(self) -> str:
        return _rgb_hex(self._r, self._g, self._b)

    def rgba(self, alpha: int) -> str:
        """QSS ``rgba(r,g,b,a)`` with integer alpha 0–255."""
        return f"rgba({self._r},{self._g},{self._b},{alpha})"

    def rgba_f(self, alpha: float) -> str:
        """QSS ``rgba(r,g,b,a)`` with float alpha 0–1."""
        return f"rgba({self._r},{self._g},{self._b},{alpha})"

    def accent_soft_border_hex(self) -> str:
        """Light outline (replaces fixed orchid) — mix accent toward white."""
        r, g, b = _mix_rgb(self._r, self._g, self._b, 255, 255, 255, 0.55)
        return _rgb_hex(r, g, b)

    def accent_checkbox_checked_top(self) -> str:
        r, g, b = _mix_rgb(self._r, self._g, self._b, 40, 40, 50, 0.5)
        return _rgb_hex(r, g, b)

    def accent_checkbox_checked_bottom(self) -> str:
        r, g, b = _mix_rgb(self._r, self._g, self._b, 20, 20, 35, 0.55)
        return _rgb_hex(r, g, b)

    def accent_checkbox_checked_hover_top(self) -> str:
        r, g, b = _mix_rgb(self._r, self._g, self._b, 60, 60, 75, 0.52)
        return _rgb_hex(r, g, b)

    def accent_checkbox_checked_hover_bottom(self) -> str:
        r, g, b = _mix_rgb(self._r, self._g, self._b, 40, 40, 55, 0.52)
        return _rgb_hex(r, g, b)

    def accent_checkbox_checked_pressed_top(self) -> str:
        r, g, b = _mix_rgb(self._r, self._g, self._b, 30, 30, 45, 0.55)
        return _rgb_hex(r, g, b)

    def accent_checkbox_checked_pressed_bottom(self) -> str:
        r, g, b = _mix_rgb(self._r, self._g, self._b, 15, 15, 30, 0.55)
        return _rgb_hex(r, g, b)

    def accent_checkbox_checked_top_light(self) -> str:
        r, g, b = _mix_rgb(self._r, self._g, self._b, 255, 255, 255, 0.22)
        return _rgb_hex(r, g, b)

    def accent_checkbox_checked_bottom_light(self) -> str:
        r, g, b = _mix_rgb(self._r, self._g, self._b, 190, 190, 200, 0.38)
        return _rgb_hex(r, g, b)

    def accent_checkbox_checked_hover_top_light(self) -> str:
        r, g, b = _mix_rgb(self._r, self._g, self._b, 255, 255, 255, 0.28)
        return _rgb_hex(r, g, b)

    def accent_checkbox_checked_hover_bottom_light(self) -> str:
        r, g, b = _mix_rgb(self._r, self._g, self._b, 175, 175, 188, 0.4)
        return _rgb_hex(r, g, b)

    def accent_checkbox_checked_pressed_top_light(self) -> str:
        r, g, b = _mix_rgb(self._r, self._g, self._b, 160, 160, 175, 0.45)
        return _rgb_hex(r, g, b)

    def accent_checkbox_checked_pressed_bottom_light(self) -> str:
        r, g, b = _mix_rgb(self._r, self._g, self._b, 130, 130, 148, 0.48)
        return _rgb_hex(r, g, b)

    def settings_path_input_style(self) -> str:
        """Inline path fields that bypass dialog QSS (7-Zip path, backup folder)."""
        if self.is_light_theme():
            return (
                "background-color: #ffffff; color: #1a1a1a; padding: 2px 8px; "
                "border: 1px solid #c4c4cc; border-radius: 4px;"
            )
        return (
            "background-color: #252526; color: #cccccc; padding: 0px 8px; "
            "border: 1px solid #404040; border-radius: 4px; "
            "min-height: 22px; max-height: 22px; height: 22px; font-size: 11px;"
        )

    def settings_muted_hint_color(self) -> str:
        return "#6a6a78" if self.is_light_theme() else "#888888"

    def settings_secondary_label_color(self) -> str:
        return "#4a4a58" if self.is_light_theme() else "#cccccc"

    def settings_version_muted_color(self) -> str:
        return "#7a7a8a" if self.is_light_theme() else "#888888"

    def settings_disabled_muted_color(self) -> str:
        return "#9898a4" if self.is_light_theme() else "#666666"

    def settings_primary_on_dialog_color(self) -> str:
        """Primary label text on the settings dialog background (e.g. nested rows)."""
        return self.ui_body_text_light() if self.is_light_theme() else self.ui_body_text_dark()

    def scroll_handle_accent_dark(self) -> str:
        r, g, b = _mix_rgb(self._r, self._g, self._b, 26, 26, 26, 0.35)
        return _rgb_hex(r, g, b)

    def scroll_handle_accent_mid(self) -> str:
        r, g, b = _mix_rgb(self._r, self._g, self._b, 80, 80, 90, 0.45)
        return _rgb_hex(r, g, b)

    def main_window_qss(self) -> str:
        if self.is_light_theme():
            return self._main_window_qss_light()
        return self._main_window_qss_dark()

    def _main_window_qss_dark(self) -> str:
        a = self
        body = a.ui_body_text_dark()
        ff = a.ui_font_family_css()
        return f"""
            QMainWindow {{
                background-color: #202020;
                color: {body};
                font-family: {ff};
                font-size: 11px;
            }}
            QWidget {{
                background-color: #202020;
                color: {body};
                font-family: {ff};
                font-size: 11px;
            }}
            QTableWidget {{
                background-color: #252526;
                alternate-background-color: #252526;
                gridline-color: #3e3e42;
                color: #cccccc;
                border: 1px solid #3e3e42;
                border-radius: 4px;
                selection-background-color: {a.rgba_f(0.2)};
                selection-color: #ffffff;
                padding: 0px;
                margin: 0px;
            }}
            QTableWidget::item {{
                padding: 4px 8px;
                border: none;
                background-color: transparent;
            }}
            QTableWidget::item:selected {{
                background-color: transparent;
                color: #ffffff;
                border: none;
                outline: none;
            }}
            QTableWidget::item:selected:active {{
                background-color: transparent;
                border: none;
                outline: none;
            }}
            QTableWidget::item:hover:selected {{
                background-color: transparent;
            }}
            QHeaderView::section {{
                background-color: #2d2d30;
                color: {body};
                padding: 4px 8px;
                border: none;
                border-right: 1px solid #3e3e42;
                border-bottom: 2px solid #3e3e42;
                font-weight: 600;
                font-size: 11px;
            }}
            QHeaderView::section:first {{
                border-left: none;
            }}
            QHeaderView::section:last {{
                border-right: none;
            }}
            QHeaderView::section:hover {{
                background-color: #37373d;
            }}
            QHeaderView::section:pressed {{
                background-color: #404040;
            }}
            QPushButton {{
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #454548, stop:1 #2a2a2d);
                border: 1px solid #3e3e42;
                border-radius: 4px;
                color: {body};
                min-height: 19px;
                max-height: 19px;
                height: 19px;
                padding: 1px 12px;
                font-size: 11px;
            }}
            QPushButton:hover {{
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #505053, stop:1 #3a3a3d);
                border: 2px solid {a.rgba(200)};
                border-radius: 4px;
            }}
            QPushButton:pressed {{
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #2d2d30, stop:1 #202020);
            }}
            QPushButton:disabled {{
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #202020, stop:1 #202020);
                color: #6a6a6a;
                border-color: #2d2d30;
            }}
            QLabel {{
                color: {body};
            }}
            QScrollBar:vertical {{
                background-color: #1a1a1a;
                width: 14px;
                margin: 14px 0 14px 0;
                border: none;
                border-radius: 7px;
            }}
            QScrollBar::add-page:vertical,
            QScrollBar::sub-page:vertical {{
                background: none;
            }}
            QScrollBar::handle:vertical {{
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #2d2d30, stop:0.3 #3a3a3d, stop:0.5 #454548, stop:0.7 #3a3a3d, stop:1 #2d2d30);
                min-height: 25px;
                margin: 2px;
                border-radius: 7px;
                border: 1px solid #3e3e42;
                border-top: 1px solid #404040;
                border-bottom: 1px solid #1a1a1a;
                background-image: url("ui/scroll_groove.svg");
                background-repeat: no-repeat;
                background-position: center;
            }}
            QScrollBar::handle:vertical:hover,
            QScrollBar::handle:vertical:pressed {{
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #4a4a4d, stop:0.5 #505053, stop:1 #4a4a4d);
                border: 1px solid {a.rgba(220)};
                background-image: url("ui/scroll_groove.svg");
                background-repeat: no-repeat;
                background-position: center;
            }}
            QScrollBar::sub-line:vertical,
            QScrollBar::add-line:vertical {{
                border: 1px solid #3e3e42;
                background: qlineargradient(
                    x1:0, y1:0, x2:0, y2:1,
                    stop:0 #343437,
                    stop:1 #252528
                );
                height: 14px;
                subcontrol-origin: margin;
            }}
            QScrollBar::sub-line:vertical {{
                subcontrol-position: top;
                border-radius: 0px;
            }}
            QScrollBar::add-line:vertical {{
                subcontrol-position: bottom;
                border-radius: 0px;
            }}
            QScrollBar::sub-line:vertical:hover,
            QScrollBar::add-line:vertical:hover {{
                background: qlineargradient(
                    x1:0, y1:0, x2:0, y2:1,
                    stop:0 #3f3f43,
                    stop:1 #2c2c30
                );
                border: 1px solid {a.rgba(210)};
            }}
            QScrollBar::sub-line:vertical:pressed,
            QScrollBar::add-line:vertical:pressed {{
                background: qlineargradient(
                    x1:0, y1:0, x2:0, y2:1,
                    stop:0 #2a2a2e,
                    stop:1 #19191d
                );
                border: 1px solid {a.rgba(255)};
            }}
            QScrollBar::up-arrow:vertical {{
                width: 10px;
                height: 8px;
                image: url("ui/scroll_up.svg");
            }}
            QScrollBar::down-arrow:vertical {{
                width: 10px;
                height: 8px;
                image: url("ui/scroll_down.svg");
            }}
            QScrollBar:horizontal {{
                background: #202020;
                height: 14px;
                border: none;
                margin: 0px;
                border-radius: 7px;
            }}
            QScrollBar::handle:horizontal {{
                background: qlineargradient(x1:0, y1:0, x2:1, y2:0,
                    stop:0 #2d2d30, stop:0.3 #3a3a3d, stop:0.5 #454548, stop:0.7 #3a3a3d, stop:1 #2d2d30);
                min-width: 30px;
                border-radius: 7px;
                margin: 2px 16px 2px 16px;
                border: 1px solid #3e3e42;
                border-top: 1px solid #404040;
                border-bottom: 1px solid #1a1a1a;
                background-image: url("ui/scroll_groove_horizontal.svg");
                background-repeat: no-repeat;
                background-position: center;
            }}
            QScrollBar::handle:horizontal:hover {{
                background: qlineargradient(x1:0, y1:0, x2:1, y2:0,
                    stop:0 #4a4a4d, stop:0.5 #505053, stop:1 #4a4a4d);
                border: 1px solid {a.rgba(200)};
                background-image: url("ui/scroll_groove_horizontal.svg");
                background-repeat: no-repeat;
                background-position: center;
            }}
            QScrollBar::handle:horizontal:pressed {{
                background: qlineargradient(x1:0, y1:0, x2:1, y2:0,
                    stop:0 #2d2d30, stop:0.5 #353538, stop:1 #2d2d30);
                border: 1px solid {a.rgba(255)};
                background-image: url("ui/scroll_groove_horizontal.svg");
                background-repeat: no-repeat;
                background-position: center;
            }}
            QScrollBar::add-line:horizontal,
            QScrollBar::sub-line:horizontal {{
                height: 14px;
                width: 14px;
                subcontrol-origin: margin;
                border: 1px solid #3e3e42;
                background: qlineargradient(
                    x1:0, y1:0, x2:0, y2:1,
                    stop:0 #343437,
                    stop:1 #252528
                );
            }}
            QScrollBar::sub-line:horizontal {{
                subcontrol-position: left;
                border-radius: 0px;
            }}
            QScrollBar::add-line:horizontal {{
                subcontrol-position: right;
                border-radius: 0px;
            }}
            QScrollBar::add-line:horizontal:hover,
            QScrollBar::sub-line:horizontal:hover {{
                background: qlineargradient(
                    x1:0, y1:0, x2:0, y2:1,
                    stop:0 #3f3f43,
                    stop:1 #2c2c30
                );
                border: 1px solid {a.rgba(210)};
            }}
            QScrollBar::add-line:horizontal:pressed,
            QScrollBar::sub-line:horizontal:pressed {{
                background: qlineargradient(
                    x1:0, y1:0, x2:0, y2:1,
                    stop:0 #2a2a2e,
                    stop:1 #19191d
                );
                border: 1px solid {a.rgba(255)};
            }}
            QScrollBar::left-arrow:horizontal {{
                width: 10px;
                height: 8px;
                image: url("ui/scroll_left.svg");
            }}
            QScrollBar::right-arrow:horizontal {{
                width: 10px;
                height: 8px;
                image: url("ui/scroll_right.svg");
            }}
            QScrollBar::add-page:horizontal, QScrollBar::sub-page:horizontal {{
                background: none;
            }}
            QCheckBox {{
                spacing: 6px;
                color: {body};
            }}
            QCheckBox::indicator {{
                width: 12px;
                height: 12px;
                border: 2px solid #404040;
                border-radius: 3px;
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #2a2a2d, stop:1 #202020);
            }}
            QCheckBox::indicator:hover {{
                border: 2px solid {a.rgba(200)};
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #3a3a3d, stop:1 #2d2d30);
            }}
            QCheckBox::indicator:pressed {{
                border: 2px solid {a.rgba(255)};
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #202020, stop:1 #1a1a1a);
            }}
            QCheckBox::indicator:checked {{
                background-color: transparent;
                border: 2px solid {a.rgba(220)};
            }}
            QCheckBox::indicator:checked:hover {{
                background-color: transparent;
                border: 2px solid {a.rgba(255)};
            }}
            QCheckBox::indicator:checked:pressed {{
                background-color: transparent;
                border: 2px solid {a.rgba(255)};
            }}
            QCheckBox::indicator:disabled {{
                background: #202020;
                border: 2px solid #2d2d30;
                color: #666666;
            }}
        """

    def _main_window_qss_light(self) -> str:
        a = self
        body = a.ui_body_text_light()
        ff = a.ui_font_family_css()
        return f"""
            QMainWindow {{
                background-color: #f4f4f7;
                color: {body};
                font-family: {ff};
                font-size: 11px;
            }}
            QWidget {{
                background-color: #f4f4f7;
                color: {body};
                font-family: {ff};
                font-size: 11px;
            }}
            QTableWidget {{
                background-color: #ffffff;
                alternate-background-color: #f6f6f9;
                gridline-color: #d8d8e0;
                color: #1a1a1e;
                border: 1px solid #d0d0d8;
                border-radius: 4px;
                selection-background-color: {a.rgba_f(0.18)};
                selection-color: #0a0a0c;
                padding: 0px;
                margin: 0px;
            }}
            QTableWidget::item {{
                padding: 4px 8px;
                border: none;
                background-color: transparent;
            }}
            QTableWidget::item:selected {{
                background-color: transparent;
                color: #0a0a0c;
                border: none;
                outline: none;
            }}
            QTableWidget::item:selected:active {{
                background-color: transparent;
                border: none;
                outline: none;
            }}
            QTableWidget::item:hover:selected {{
                background-color: transparent;
            }}
            QHeaderView::section {{
                background-color: #ececf2;
                color: {body};
                padding: 4px 8px;
                border: none;
                border-right: 1px solid #d8d8e0;
                border-bottom: 2px solid #c8c8d4;
                font-weight: 600;
                font-size: 11px;
            }}
            QHeaderView::section:first {{
                border-left: none;
            }}
            QHeaderView::section:last {{
                border-right: none;
            }}
            QHeaderView::section:hover {{
                background-color: #e2e2ea;
            }}
            QHeaderView::section:pressed {{
                background-color: #d8d8e2;
            }}
            QPushButton {{
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #fafafc, stop:1 #e8e8ee);
                border: 1px solid #c4c4ce;
                border-radius: 4px;
                color: {body};
                min-height: 19px;
                max-height: 19px;
                height: 19px;
                padding: 1px 12px;
                font-size: 11px;
            }}
            QPushButton:hover {{
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #ffffff, stop:1 #efeff4);
                border: 2px solid {a.rgba(200)};
                border-radius: 4px;
            }}
            QPushButton:pressed {{
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #dedee6, stop:1 #d0d0da);
            }}
            QPushButton:disabled {{
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #ececf0, stop:1 #ececf0);
                color: #9898a4;
                border-color: #d4d4dc;
            }}
            QLabel {{
                color: {body};
            }}
            QScrollBar:vertical {{
                background-color: #e6e6ec;
                width: 14px;
                margin: 14px 0 14px 0;
                border: none;
                border-radius: 7px;
            }}
            QScrollBar::add-page:vertical,
            QScrollBar::sub-page:vertical {{
                background: none;
            }}
            QScrollBar::handle:vertical {{
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #dedee4, stop:0.3 #e8e8ee, stop:0.5 #f0f0f4, stop:0.7 #e4e4ea, stop:1 #d6d6de);
                min-height: 25px;
                margin: 2px;
                border-radius: 7px;
                border: 1px solid #c8c8d2;
                border-top: 1px solid #f8f8fa;
                border-bottom: 1px solid #bcbcc8;
                background-image: url("ui/scroll_groove.svg");
                background-repeat: no-repeat;
                background-position: center;
            }}
            QScrollBar::handle:vertical:hover,
            QScrollBar::handle:vertical:pressed {{
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #eaeaf0, stop:0.5 #f4f4f8, stop:1 #e0e0e8);
                border: 1px solid {a.rgba(220)};
                background-image: url("ui/scroll_groove.svg");
                background-repeat: no-repeat;
                background-position: center;
            }}
            QScrollBar::sub-line:vertical,
            QScrollBar::add-line:vertical {{
                border: 1px solid #c8c8d2;
                background: qlineargradient(
                    x1:0, y1:0, x2:0, y2:1,
                    stop:0 #f0f0f4,
                    stop:1 #e2e2ea
                );
                height: 14px;
                subcontrol-origin: margin;
            }}
            QScrollBar::sub-line:vertical {{
                subcontrol-position: top;
                border-radius: 0px;
            }}
            QScrollBar::add-line:vertical {{
                subcontrol-position: bottom;
                border-radius: 0px;
            }}
            QScrollBar::sub-line:vertical:hover,
            QScrollBar::add-line:vertical:hover {{
                background: qlineargradient(
                    x1:0, y1:0, x2:0, y2:1,
                    stop:0 #f6f6fa,
                    stop:1 #eaeaf0
                );
                border: 1px solid {a.rgba(210)};
            }}
            QScrollBar::sub-line:vertical:pressed,
            QScrollBar::add-line:vertical:pressed {{
                background: qlineargradient(
                    x1:0, y1:0, x2:0, y2:1,
                    stop:0 #d8d8e2,
                    stop:1 #ceced8
                );
                border: 1px solid {a.rgba(255)};
            }}
            QScrollBar::up-arrow:vertical {{
                width: 10px;
                height: 8px;
                image: url("ui/scroll_up.svg");
            }}
            QScrollBar::down-arrow:vertical {{
                width: 10px;
                height: 8px;
                image: url("ui/scroll_down.svg");
            }}
            QScrollBar:horizontal {{
                background: #ececf0;
                height: 14px;
                border: none;
                margin: 0px;
                border-radius: 7px;
            }}
            QScrollBar::handle:horizontal {{
                background: qlineargradient(x1:0, y1:0, x2:1, y2:0,
                    stop:0 #dedee4, stop:0.3 #e8e8ee, stop:0.5 #f0f0f4, stop:0.7 #e4e4ea, stop:1 #d6d6de);
                min-width: 30px;
                border-radius: 7px;
                margin: 2px 16px 2px 16px;
                border: 1px solid #c8c8d2;
                border-top: 1px solid #f8f8fa;
                border-bottom: 1px solid #bcbcc8;
                background-image: url("ui/scroll_groove_horizontal.svg");
                background-repeat: no-repeat;
                background-position: center;
            }}
            QScrollBar::handle:horizontal:hover {{
                background: qlineargradient(x1:0, y1:0, x2:1, y2:0,
                    stop:0 #eaeaf0, stop:0.5 #f4f4f8, stop:1 #e0e0e8);
                border: 1px solid {a.rgba(200)};
                background-image: url("ui/scroll_groove_horizontal.svg");
                background-repeat: no-repeat;
                background-position: center;
            }}
            QScrollBar::handle:horizontal:pressed {{
                background: qlineargradient(x1:0, y1:0, x2:1, y2:0,
                    stop:0 #d8d8e2, stop:0.5 #e0e0e8, stop:1 #d8d8e2);
                border: 1px solid {a.rgba(255)};
                background-image: url("ui/scroll_groove_horizontal.svg");
                background-repeat: no-repeat;
                background-position: center;
            }}
            QScrollBar::add-line:horizontal,
            QScrollBar::sub-line:horizontal {{
                height: 14px;
                width: 14px;
                subcontrol-origin: margin;
                border: 1px solid #c8c8d2;
                background: qlineargradient(
                    x1:0, y1:0, x2:0, y2:1,
                    stop:0 #f0f0f4,
                    stop:1 #e2e2ea
                );
            }}
            QScrollBar::sub-line:horizontal {{
                subcontrol-position: left;
                border-radius: 0px;
            }}
            QScrollBar::add-line:horizontal {{
                subcontrol-position: right;
                border-radius: 0px;
            }}
            QScrollBar::add-line:horizontal:hover,
            QScrollBar::sub-line:horizontal:hover {{
                background: qlineargradient(
                    x1:0, y1:0, x2:0, y2:1,
                    stop:0 #f6f6fa,
                    stop:1 #eaeaf0
                );
                border: 1px solid {a.rgba(210)};
            }}
            QScrollBar::add-line:horizontal:pressed,
            QScrollBar::sub-line:horizontal:pressed {{
                background: qlineargradient(
                    x1:0, y1:0, x2:0, y2:1,
                    stop:0 #d8d8e2,
                    stop:1 #ceced8
                );
                border: 1px solid {a.rgba(255)};
            }}
            QScrollBar::left-arrow:horizontal {{
                width: 10px;
                height: 8px;
                image: url("ui/scroll_left.svg");
            }}
            QScrollBar::right-arrow:horizontal {{
                width: 10px;
                height: 8px;
                image: url("ui/scroll_right.svg");
            }}
            QScrollBar::add-page:horizontal, QScrollBar::sub-page:horizontal {{
                background: none;
            }}
            QCheckBox {{
                spacing: 6px;
                color: {body};
                background-color: transparent;
            }}
            QCheckBox::indicator {{
                width: 12px;
                height: 12px;
                border: 2px solid #a8a8b4;
                border-radius: 3px;
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #ffffff, stop:1 #ececf0);
            }}
            QCheckBox::indicator:hover {{
                border: 2px solid {a.rgba(200)};
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #fafafc, stop:1 #f0f0f4);
            }}
            QCheckBox::indicator:pressed {{
                border: 2px solid {a.rgba(255)};
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #e4e4ea, stop:1 #d8d8e2);
            }}
            QCheckBox::indicator:checked {{
                background-color: transparent;
                border: 2px solid {a.rgba(220)};
            }}
            QCheckBox::indicator:checked:hover {{
                background-color: transparent;
                border: 2px solid {a.rgba(255)};
            }}
            QCheckBox::indicator:checked:pressed {{
                background-color: transparent;
                border: 2px solid {a.rgba(255)};
            }}
            QCheckBox::indicator:disabled {{
                background: #ececf0;
                border: 2px solid #d8d8e0;
                color: #9898a4;
            }}
        """

    def menu_qss(self) -> str:
        if self.is_light_theme():
            return self._menu_qss_light()
        return self._menu_qss_dark()

    def _menu_qss_dark(self) -> str:
        a = self
        body = a.ui_body_text_dark()
        return f"""
            QMenu {{
                background-color: #1a1a1a;
                border: 1px solid #404040;
                border-radius: 4px;
                color: {body};
                padding: 2px;
                font-size: 11px;
            }}
            QMenu::item {{
                padding: 3px 10px;
                border-radius: 3px;
            }}
            QMenu::item:selected {{
                background-color: {a.rgba(120)};
                color: {body};
            }}
            QMenu::item:pressed {{
                background-color: {a.rgba(200)};
                color: {body};
            }}
            QMenu::separator {{
                height: 1px;
                background-color: #404040;
                margin: 2px 8px;
            }}
        """

    def _menu_qss_light(self) -> str:
        a = self
        body = a.ui_body_text_light()
        return f"""
            QMenu {{
                background-color: #ffffff;
                border: 1px solid #c8c8d4;
                border-radius: 4px;
                color: {body};
                padding: 2px;
                font-size: 11px;
            }}
            QMenu::item {{
                padding: 3px 10px;
                border-radius: 3px;
            }}
            QMenu::item:selected {{
                background-color: {a.rgba_f(0.16)};
                color: #0a0a0c;
            }}
            QMenu::item:pressed {{
                background-color: {a.rgba_f(0.26)};
                color: #0a0a0c;
            }}
            QMenu::separator {{
                height: 1px;
                background-color: #d8d8e2;
                margin: 2px 8px;
            }}
        """

    def settings_dialog_qss(self) -> str:
        if self.is_light_theme():
            return self._settings_dialog_qss_light()
        return self._settings_dialog_qss_dark()

    def backup_estimate_browser_supplement_qss(self) -> str:
        """
        QTextBrowser pane (#252526) + grooved vertical scrollbar for the backup estimate dialog.
        Appended after ``settings_dialog_qss()`` so scrollbars match the main window look.
        """
        a = self
        if self.is_light_theme():
            return f"""
            QTextBrowser#backupEstimateBrowser {{
                background-color: #f4f4f7;
                color: #1a1a1e;
                border: 1px solid #d0d0d8;
                border-radius: 4px;
                padding: 9px;
            }}
            QTextBrowser#backupEstimateBrowser QScrollBar:vertical {{
                background-color: #e6e6ec;
                width: 14px;
                margin: 14px 0 14px 0;
                border: none;
                border-radius: 7px;
            }}
            QTextBrowser#backupEstimateBrowser QScrollBar::add-page:vertical,
            QTextBrowser#backupEstimateBrowser QScrollBar::sub-page:vertical {{
                background: none;
            }}
            QTextBrowser#backupEstimateBrowser QScrollBar::handle:vertical {{
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #dedee4, stop:0.3 #e8e8ee, stop:0.5 #f0f0f4, stop:0.7 #e4e4ea, stop:1 #d6d6de);
                min-height: 25px;
                margin: 2px;
                border-radius: 7px;
                border: 1px solid #c8c8d2;
                border-top: 1px solid #f4f4f8;
                border-bottom: 1px solid #bcbcc8;
                background-image: url("ui/scroll_groove.svg");
                background-repeat: no-repeat;
                background-position: center;
            }}
            QTextBrowser#backupEstimateBrowser QScrollBar::handle:vertical:hover,
            QTextBrowser#backupEstimateBrowser QScrollBar::handle:vertical:pressed {{
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #eaeaf0, stop:0.5 #f4f4f8, stop:1 #e0e0e8);
                border: 1px solid {a.rgba(200)};
                background-image: url("ui/scroll_groove.svg");
                background-repeat: no-repeat;
                background-position: center;
            }}
            QTextBrowser#backupEstimateBrowser QScrollBar::sub-line:vertical,
            QTextBrowser#backupEstimateBrowser QScrollBar::add-line:vertical {{
                border: 1px solid #c8c8d2;
                background: qlineargradient(
                    x1:0, y1:0, x2:0, y2:1,
                    stop:0 #f0f0f4,
                    stop:1 #e2e2ea
                );
                height: 14px;
                subcontrol-origin: margin;
            }}
            QTextBrowser#backupEstimateBrowser QScrollBar::sub-line:vertical {{
                subcontrol-position: top;
            }}
            QTextBrowser#backupEstimateBrowser QScrollBar::add-line:vertical {{
                subcontrol-position: bottom;
            }}
            QTextBrowser#backupEstimateBrowser QScrollBar::sub-line:vertical:hover,
            QTextBrowser#backupEstimateBrowser QScrollBar::add-line:vertical:hover {{
                background: qlineargradient(
                    x1:0, y1:0, x2:0, y2:1,
                    stop:0 #f6f6fa,
                    stop:1 #eaeaf0
                );
                border: 2px solid {a.rgba(210)};
            }}
            QTextBrowser#backupEstimateBrowser QScrollBar::sub-line:vertical:pressed,
            QTextBrowser#backupEstimateBrowser QScrollBar::add-line:vertical:pressed {{
                background: qlineargradient(
                    x1:0, y1:0, x2:0, y2:1,
                    stop:0 #d8d8e2,
                    stop:1 #ceced8
                );
                border: 2px solid {a.rgba(255)};
            }}
            QTextBrowser#backupEstimateBrowser QScrollBar::up-arrow:vertical {{
                width: 10px;
                height: 8px;
                image: url("ui/scroll_up.svg");
            }}
            QTextBrowser#backupEstimateBrowser QScrollBar::down-arrow:vertical {{
                width: 10px;
                height: 8px;
                image: url("ui/scroll_down.svg");
            }}
            """
        return f"""
            QTextBrowser#backupEstimateBrowser {{
                background-color: #252526;
                color: #cccccc;
                border: 1px solid #3e3e42;
                border-radius: 4px;
                padding: 9px;
            }}
            QTextBrowser#backupEstimateBrowser QScrollBar:vertical {{
                background-color: #1a1a1a;
                width: 14px;
                margin: 14px 0 14px 0;
                border: none;
                border-radius: 7px;
            }}
            QTextBrowser#backupEstimateBrowser QScrollBar::add-page:vertical,
            QTextBrowser#backupEstimateBrowser QScrollBar::sub-page:vertical {{
                background: none;
            }}
            QTextBrowser#backupEstimateBrowser QScrollBar::handle:vertical {{
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #2d2d30, stop:0.3 #3a3a3d, stop:0.5 #454548, stop:0.7 #3a3a3d, stop:1 #2d2d30);
                min-height: 25px;
                margin: 2px;
                border-radius: 7px;
                border: 1px solid #3e3e42;
                border-top: 1px solid #404040;
                border-bottom: 1px solid #1a1a1a;
                background-image: url("ui/scroll_groove.svg");
                background-repeat: no-repeat;
                background-position: center;
            }}
            QTextBrowser#backupEstimateBrowser QScrollBar::handle:vertical:hover,
            QTextBrowser#backupEstimateBrowser QScrollBar::handle:vertical:pressed {{
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #4a4a4d, stop:0.5 #505053, stop:1 #4a4a4d);
                border: 1px solid {a.rgba(220)};
                background-image: url("ui/scroll_groove.svg");
                background-repeat: no-repeat;
                background-position: center;
            }}
            QTextBrowser#backupEstimateBrowser QScrollBar::sub-line:vertical,
            QTextBrowser#backupEstimateBrowser QScrollBar::add-line:vertical {{
                border: 1px solid #3e3e42;
                background: qlineargradient(
                    x1:0, y1:0, x2:0, y2:1,
                    stop:0 #343437,
                    stop:1 #252528
                );
                height: 14px;
                subcontrol-origin: margin;
            }}
            QTextBrowser#backupEstimateBrowser QScrollBar::sub-line:vertical {{
                subcontrol-position: top;
                border-radius: 0px;
            }}
            QTextBrowser#backupEstimateBrowser QScrollBar::add-line:vertical {{
                subcontrol-position: bottom;
                border-radius: 0px;
            }}
            QTextBrowser#backupEstimateBrowser QScrollBar::sub-line:vertical:hover,
            QTextBrowser#backupEstimateBrowser QScrollBar::add-line:vertical:hover {{
                background: qlineargradient(
                    x1:0, y1:0, x2:0, y2:1,
                    stop:0 #3f3f43,
                    stop:1 #2c2c30
                );
                border: 1px solid {a.rgba(210)};
            }}
            QTextBrowser#backupEstimateBrowser QScrollBar::sub-line:vertical:pressed,
            QTextBrowser#backupEstimateBrowser QScrollBar::add-line:vertical:pressed {{
                background: qlineargradient(
                    x1:0, y1:0, x2:0, y2:1,
                    stop:0 #2a2a2e,
                    stop:1 #19191d
                );
                border: 1px solid {a.rgba(255)};
            }}
            QTextBrowser#backupEstimateBrowser QScrollBar::up-arrow:vertical {{
                width: 10px;
                height: 8px;
                image: url("ui/scroll_up.svg");
            }}
            QTextBrowser#backupEstimateBrowser QScrollBar::down-arrow:vertical {{
                width: 10px;
                height: 8px;
                image: url("ui/scroll_down.svg");
            }}
            """

    def backup_estimate_start_backup_button_qss(self) -> str:
        """Green primary action on the backup estimate dialog (matches main Backup button)."""
        if self.is_light_theme():
            return """
            QPushButton#backupEstimateStartButton {
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #c4f7c0, stop:1 #5cc068);
                border: 1px solid #4aad58;
                color: #0a1810;
                min-height: 22px;
                max-height: 22px;
                height: 22px;
                padding: 0px 12px;
                font-size: 11px;
                border-radius: 4px;
                font-weight: 600;
            }
            QPushButton#backupEstimateStartButton:hover {
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #d2fad0, stop:1 #6cc875);
                border: 2px solid rgba(56, 142, 60, 200);
                border-radius: 4px;
            }
            QPushButton#backupEstimateStartButton:pressed {
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #4aad55, stop:1 #2e8b40);
            }
            QPushButton#backupEstimateStartButton:disabled {
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #ececf0, stop:1 #ececf0);
                color: #9898a4;
                border-color: #d4d4dc;
            }
            """
        return """
            QPushButton#backupEstimateStartButton {
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #4a6a4a, stop:1 #2d4a2d);
                border: 1px solid #4a6b4a;
                color: #ffffff;
                min-height: 22px;
                max-height: 22px;
                height: 22px;
                padding: 0px 12px;
                font-size: 11px;
                border-radius: 4px;
            }
            QPushButton#backupEstimateStartButton:hover {
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #5a7a5a, stop:1 #3a5a3a);
                border: 2px solid rgba(76, 175, 80, 200);
                border-radius: 4px;
            }
            QPushButton#backupEstimateStartButton:pressed {
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #2d4a2d, stop:1 #1f3d1f);
            }
            QPushButton#backupEstimateStartButton:disabled {
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #1e1e1e, stop:1 #1e1e1e);
                color: #6a6a6a;
                border-color: #2d2d30;
            }
            """

    def settings_tooltip_supplement_qss(self) -> str:
        """Merged onto QApplication.styleSheet while Settings is open (tooltips are not dialog children)."""
        if self.is_light_theme():
            return """
                QToolTip {
                    color: #1a1a1e;
                    background-color: #f4f4f7;
                    border: none;
                    padding: 4px 6px;
                    font-size: 11px;
                }
            """
        return """
            QToolTip {
                color: #cccccc;
                background-color: #252526;
                border: none;
                padding: 4px 6px;
                font-size: 11px;
            }
        """

    def _settings_dialog_qss_dark(self) -> str:
        """Full Settings dialog including scrollbars (accent scroll handle hovers)."""
        a = self
        sd = a.scroll_handle_accent_dark()
        sm = a.scroll_handle_accent_mid()
        sb = a.accent_soft_border_hex()
        body = a.ui_body_text_dark()
        ff = a.ui_font_family_css()
        return f"""
            QDialog {{
                background-color: #202020;
                color: {body};
                font-family: {ff};
                font-size: 11px;
            }}
            QWidget#settingsMainTabs {{
                background-color: #202020;
            }}
            QLabel {{
                color: {body};
                background-color: #202020;
            }}
            QWidget#settingsFramedPanel {{
                background-color: #252526;
                border: none;
                border-radius: 6px;
            }}
            #settingsFramedPanel QLabel {{
                background-color: #252526;
            }}
            QWidget#settingsDatePreview,
            QWidget#settingsDatePreview QLabel {{
                background-color: transparent;
            }}
            #settingsFramedPanel QLabel#settingsSevenZipExeLabel {{
                background-color: transparent;
            }}
            #settingsFramedPanel QLabel#compression7zHint {{
                background-color: transparent;
                border: none;
            }}
            QWidget#settingsSevenZipPathRow {{
                background-color: transparent;
            }}
            QPushButton {{
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #454548, stop:1 #2a2a2d);
                border: 1px solid #3e3e42;
                border-radius: 4px;
                color: {body};
                min-height: 22px;
                max-height: 22px;
                height: 22px;
                padding: 0px 12px;
                font-size: 11px;
            }}
            QPushButton:hover {{
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #505053, stop:1 #3a3a3d);
                border: 2px solid {a.rgba(200)};
                border-radius: 4px;
            }}
            QPushButton:pressed {{
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #2d2d30, stop:1 #202020);
            }}
            QPushButton:disabled {{
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #2a2a2e, stop:1 #252528);
                color: #6a6a6a;
                border: 1px solid #353538;
            }}
            QSpinBox:disabled {{
                background-color: #252526;
                color: #666666;
                border: 1px solid #2a2a2a;
            }}
            #settingsFramedPanel QComboBox {{
                background-color: #252526;
                border: 1px solid #404040;
                border-radius: 4px;
                color: {body};
                padding: 0px 8px;
                min-height: 22px;
                max-height: 22px;
                height: 22px;
                font-size: 11px;
            }}
            #settingsFramedPanel QComboBox:hover {{
                border-color: #505050;
            }}
            #settingsFramedPanel QComboBox::drop-down {{
                border: none;
                width: 20px;
            }}
            #settingsFramedPanel QComboBox::down-arrow {{
                width: 10px;
                height: 8px;
                padding-right: 4px;
                image: url("ui/scroll_down.svg");
            }}
            #settingsFramedPanel QComboBox:disabled {{
                background-color: #252526;
                color: #666666;
                border: 1px solid #2a2a2a;
            }}
            QComboBox QAbstractItemView {{
                background-color: #202020;
                border: 1px solid #404040;
                selection-background-color: #3a3a3a;
                color: {body};
            }}
            #settingsFramedPanel QLineEdit {{
                background-color: #252526;
                border: 1px solid #404040;
                border-radius: 4px;
                color: {body};
                padding: 0px 8px;
                min-height: 22px;
                max-height: 22px;
                height: 22px;
                font-size: 11px;
            }}
            #settingsFramedPanel QLineEdit:disabled {{
                background-color: #252526;
                color: #666666;
                border: 1px solid #2a2a2a;
            }}
            QCheckBox {{
                spacing: 6px;
                color: {body};
                background-color: #202020;
            }}
            #settingsFramedPanel QCheckBox {{
                background-color: #252526;
            }}
            QCheckBox::indicator {{
                width: 12px;
                height: 12px;
                border: 2px solid #404040;
                border-radius: 3px;
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #2a2a2d, stop:1 #1e1e1e);
            }}
            QCheckBox::indicator:hover {{
                border: 2px solid {a.rgba(200)};
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #3a3a3d, stop:1 #2d2d30);
            }}
            QCheckBox::indicator:pressed {{
                border: 2px solid {a.rgba(255)};
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #1e1e1e, stop:1 #1a1a1a);
            }}
            QCheckBox::indicator:checked {{
                background-color: transparent;
                border: 2px solid {a.rgba(220)};
            }}
            QCheckBox::indicator:checked:hover {{
                background-color: transparent;
                border: 2px solid {a.rgba(255)};
            }}
            QCheckBox::indicator:checked:pressed {{
                background-color: transparent;
                border: 2px solid {a.rgba(255)};
            }}
            QCheckBox::indicator:disabled {{
                background: #1e1e1e;
                border: 2px solid #2d2d30;
                color: #666666;
            }}
            QWidget#settingsTabPage {{
                background-color: #252526;
            }}
            QWidget#settingsSectionSeparatorWrap {{
                background-color: transparent;
            }}
            #settingsFramedPanel QFrame#settingsSectionSeparator {{
                background-color: #3e3e42;
                border: none;
                min-height: 1px;
                max-height: 1px;
            }}
            QScrollBar:vertical {{
                background-color: #1a1a1a;
                width: 14px;
                border: none;
                margin: 14px 0 14px 0;
                border-radius: 7px;
            }}
            QScrollBar::add-page:vertical,
            QScrollBar::sub-page:vertical {{
                background: none;
            }}
            QScrollBar::handle:vertical {{
                background: qlineargradient(
                    x1:0, y1:0, x2:1, y2:0,
                    stop:0 #262626,
                    stop:0.5 #3b3b3f,
                    stop:1 #262626
                );
                min-height: 25px;
                margin: 2px;
                border-radius: 7px;
                border: 1px solid #3e3e42;
            }}
            QScrollBar::handle:vertical:hover,
            QScrollBar::handle:vertical:pressed {{
                background: qlineargradient(
                    x1:0, y1:0, x2:1, y2:0,
                    stop:0 {sd},
                    stop:0.5 {sm},
                    stop:1 {sd}
                );
                border: 1px solid {sb};
            }}
            QScrollBar::sub-line:vertical,
            QScrollBar::add-line:vertical {{
                border: 1px solid #3e3e42;
                background: qlineargradient(
                    x1:0, y1:0, x2:0, y2:1,
                    stop:0 #343437,
                    stop:1 #252528
                );
                height: 14px;
                subcontrol-origin: margin;
            }}
            QScrollBar::sub-line:vertical {{
                subcontrol-position: top;
                border-radius: 0px;
            }}
            QScrollBar::add-line:vertical {{
                subcontrol-position: bottom;
                border-radius: 0px;
            }}
            QScrollBar::sub-line:vertical:hover,
            QScrollBar::add-line:vertical:hover {{
                background: qlineargradient(
                    x1:0, y1:0, x2:0, y2:1,
                    stop:0 #3f3f43,
                    stop:1 #2c2c30
                );
                border: 2px solid {a.rgba(210)};
            }}
            QScrollBar::sub-line:vertical:pressed,
            QScrollBar::add-line:vertical:pressed {{
                background: qlineargradient(
                    x1:0, y1:0, x2:0, y2:1,
                    stop:0 #2a2a2e,
                    stop:1 #19191d
                );
                border: 2px solid {a.rgba(255)};
            }}
            QScrollBar:horizontal {{
                background: #202020;
                height: 14px;
                border: none;
                margin: 0px;
                border-radius: 7px;
            }}
            QScrollBar::handle:horizontal {{
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #2d2d30, stop:0.3 #3a3a3d, stop:0.5 #454548, stop:0.7 #3a3a3d, stop:1 #2d2d30);
                min-width: 30px;
                border-radius: 7px;
                margin: 2px;
                border: 2px solid {a.rgba(100)};
                border-top: 1px solid #404040;
                border-bottom: 1px solid #1a1a1a;
            }}
            QScrollBar::handle:horizontal:hover {{
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #4a4a4d, stop:0.5 #505053, stop:1 #4a4a4d);
                border: 2px solid {a.rgba(200)};
            }}
            QScrollBar::handle:horizontal:pressed {{
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #2d2d30, stop:0.5 #353538, stop:1 #2d2d30);
                border: 2px solid {a.rgba(255)};
            }}
            QScrollBar::add-line:horizontal {{
                width: 14px;
                subcontrol-origin: margin;
                subcontrol-position: right;
                background: #202020;
                border-left: 1px solid #3e3e42;
                border-radius: 0px 7px 7px 0px;
            }}
            QScrollBar::add-line:horizontal:hover {{
                background: #2d2d30;
            }}
            QScrollBar::add-line:horizontal:pressed {{
                background: #1a1a1a;
            }}
            QScrollBar::sub-line:horizontal {{
                width: 14px;
                subcontrol-origin: margin;
                subcontrol-position: left;
                background: #202020;
                border-right: 1px solid #3e3e42;
                border-radius: 7px 0px 0px 7px;
            }}
            QScrollBar::sub-line:horizontal:hover {{
                background: #2d2d30;
            }}
            QScrollBar::sub-line:horizontal:pressed {{
                background: #1a1a1a;
            }}
            QScrollBar::left-arrow:horizontal {{
                image: none;
                border: none;
                width: 0px;
                height: 0px;
                border-top: 4px solid transparent;
                border-bottom: 4px solid transparent;
                border-right: 5px solid #cccccc;
                margin: 3px;
            }}
            QScrollBar::left-arrow:horizontal:hover {{
                border-right-color: {a.rgba(255)};
            }}
            QScrollBar::right-arrow:horizontal {{
                image: none;
                border: none;
                width: 0px;
                height: 0px;
                border-top: 4px solid transparent;
                border-bottom: 4px solid transparent;
                border-left: 5px solid #cccccc;
                margin: 3px;
            }}
            QScrollBar::right-arrow:horizontal:hover {{
                border-left-color: {a.rgba(255)};
            }}
            QScrollBar::add-page:horizontal, QScrollBar::sub-page:horizontal {{
                background: none;
            }}
            #settingsMainTabs QTabBar {{
                background-color: transparent;
                border: none;
                padding: 0px;
                margin: 0px;
            }}
            #settingsMainTabs QTabBar::tab {{
                background-color: transparent;
                color: #8c8c8c;
                border: 1px solid transparent;
                border-top-left-radius: 6px;
                border-top-right-radius: 6px;
                padding: 3px 6px 5px 6px;
                margin-right: 2px;
                margin-top: 0px;
                margin-bottom: 0px;
                font-size: 11px;
                min-height: 0px;
            }}
            #settingsMainTabs QTabBar::tab:last {{
                margin-right: 0px;
            }}
            #settingsMainTabs QTabBar::tab:selected {{
                background-color: #252526;
                color: {body};
                font-weight: 600;
                font-size: 11px;
                border: 1px solid #3e3e42;
                border-bottom: none;
                border-top-left-radius: 6px;
                border-top-right-radius: 6px;
                padding: 4px 8px 6px 8px;
                margin-top: 0px;
                margin-bottom: -1px;
            }}
            #settingsMainTabs QTabBar::tab:hover:!selected {{
                background-color: transparent;
                color: #c8c8c8;
            }}
            QGroupBox {{
                background-color: #252526;
                border: 1px solid #404040;
                border-radius: 4px;
                margin-top: 14px;
                padding: 16px 14px 14px 14px;
                padding-top: 22px;
                font-size: 11px;
            }}
            QGroupBox::title {{
                subcontrol-origin: margin;
                subcontrol-position: top left;
                left: 8px;
                padding: 0 6px;
                color: {body};
                background-color: #252526;
            }}
            QFormLayout {{
                background-color: transparent;
            }}
        """

    def _settings_dialog_qss_light(self) -> str:
        a = self
        sd = a.scroll_handle_accent_dark()
        sm = a.scroll_handle_accent_mid()
        sb = a.accent_soft_border_hex()
        body = a.ui_body_text_light()
        ff = a.ui_font_family_css()
        return f"""
            QDialog {{
                background-color: #f4f4f7;
                color: {body};
                font-family: {ff};
                font-size: 11px;
            }}
            QWidget#settingsMainTabs {{
                background-color: #f4f4f7;
            }}
            QLabel {{
                color: {body};
                background-color: transparent;
            }}
            QWidget#settingsDatePreview,
            QWidget#settingsDatePreview QLabel {{
                background-color: transparent;
            }}
            QWidget#settingsFramedPanel {{
                background-color: #ffffff;
                border: none;
                border-radius: 6px;
            }}
            QWidget#settingsTabPage {{
                background-color: #ffffff;
            }}
            #settingsFramedPanel QLabel {{
                background-color: #ffffff;
            }}
            #settingsFramedPanel QLabel#settingsSevenZipExeLabel {{
                background-color: transparent;
            }}
            #settingsFramedPanel QLabel#compression7zHint {{
                background-color: transparent;
                border: none;
            }}
            QWidget#settingsSevenZipPathRow {{
                background-color: transparent;
            }}
            QWidget#settingsSectionSeparatorWrap {{
                background-color: transparent;
            }}
            #settingsFramedPanel QFrame#settingsSectionSeparator {{
                background-color: #d0d0d8;
                border: none;
                min-height: 1px;
                max-height: 1px;
            }}
            QPushButton {{
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #fafafc, stop:1 #e8e8ee);
                border: 1px solid #c4c4ce;
                border-radius: 4px;
                color: {body};
                min-height: 19px;
                max-height: 19px;
                height: 19px;
                padding: 1px 12px;
                font-size: 11px;
            }}
            QPushButton:hover {{
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #ffffff, stop:1 #efeff4);
                border: 2px solid {a.rgba(200)};
                border-radius: 4px;
            }}
            QPushButton:pressed {{
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #dedee6, stop:1 #d4d4dc);
            }}
            QPushButton:disabled {{
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #ececf0, stop:1 #ececf0);
                color: #9898a4;
                border-color: #d4d4dc;
            }}
            QSpinBox:disabled {{
                background-color: #ececf0;
                color: #9898a4;
                border: 1px solid #d8d8e0;
            }}
            #settingsFramedPanel QComboBox {{
                background-color: #ffffff;
                border: 1px solid #c4c4cc;
                border-radius: 4px;
                color: {body};
                padding: 0px 8px;
                min-height: 22px;
                max-height: 22px;
                height: 22px;
                font-size: 11px;
            }}
            #settingsFramedPanel QComboBox:hover {{
                border-color: {a.rgba(180)};
            }}
            #settingsFramedPanel QComboBox::drop-down {{
                border: none;
                width: 20px;
            }}
            #settingsFramedPanel QComboBox::down-arrow {{
                width: 10px;
                height: 8px;
                padding-right: 4px;
                image: url("ui/scroll_down.svg");
            }}
            #settingsFramedPanel QComboBox:disabled {{
                background-color: #ececf0;
                color: #9898a4;
                border: 1px solid #d8d8e0;
            }}
            QComboBox QAbstractItemView {{
                background-color: #ffffff;
                border: 1px solid #c4c4cc;
                selection-background-color: {a.rgba_f(0.14)};
                color: #1a1a1e;
            }}
            #settingsFramedPanel QLineEdit {{
                background-color: #ffffff;
                border: 1px solid #c4c4cc;
                border-radius: 4px;
                color: #1a1a1e;
                padding: 2px 8px;
            }}
            #settingsFramedPanel QLineEdit:disabled {{
                background-color: #ececf0;
                color: #9898a4;
                border: 1px solid #d8d8e0;
            }}
            #settingsMainTabs QTabBar {{
                background-color: transparent;
                border: none;
                padding: 0px;
                margin: 0px;
            }}
            #settingsMainTabs QTabBar::tab {{
                background-color: transparent;
                color: #6a6a78;
                border: 1px solid transparent;
                border-top-left-radius: 6px;
                border-top-right-radius: 6px;
                padding: 3px 6px 5px 6px;
                margin-right: 2px;
                margin-top: 0px;
                margin-bottom: 0px;
                font-size: 11px;
                min-height: 0px;
            }}
            #settingsMainTabs QTabBar::tab:last {{
                margin-right: 0px;
            }}
            #settingsMainTabs QTabBar::tab:selected {{
                background-color: #ffffff;
                color: #0a0a0c;
                font-weight: 600;
                font-size: 11px;
                border: 1px solid #d0d0d8;
                border-bottom: none;
                border-top-left-radius: 6px;
                border-top-right-radius: 6px;
                padding: 4px 8px 6px 8px;
                margin-top: 0px;
                margin-bottom: -1px;
            }}
            #settingsMainTabs QTabBar::tab:hover:!selected {{
                background-color: transparent;
                color: #2a2a32;
            }}
            #settingsFramedPanel QCheckBox {{
                spacing: 6px;
                color: #1a1a1e;
                background-color: transparent;
            }}
            QCheckBox::indicator {{
                width: 12px;
                height: 12px;
                border: 2px solid #a8a8b4;
                border-radius: 3px;
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #ffffff, stop:1 #ececf0);
            }}
            QCheckBox::indicator:hover {{
                border: 2px solid {a.rgba(200)};
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #fafafc, stop:1 #f0f0f4);
            }}
            QCheckBox::indicator:pressed {{
                border: 2px solid {a.rgba(255)};
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #e4e4ea, stop:1 #d8d8e2);
            }}
            QCheckBox::indicator:checked {{
                background-color: transparent;
                border: 2px solid {a.rgba(220)};
            }}
            QCheckBox::indicator:checked:hover {{
                background-color: transparent;
                border: 2px solid {a.rgba(255)};
            }}
            QCheckBox::indicator:checked:pressed {{
                background-color: transparent;
                border: 2px solid {a.rgba(255)};
            }}
            QCheckBox::indicator:disabled {{
                background: #ececf0;
                border: 2px solid #d8d8e0;
                color: #9898a4;
            }}
            QScrollBar:vertical {{
                background-color: #e6e6ec;
                width: 14px;
                border: none;
                margin: 14px 0 14px 0;
                border-radius: 7px;
            }}
            QScrollBar::add-page:vertical,
            QScrollBar::sub-page:vertical {{
                background: none;
            }}
            QScrollBar::handle:vertical {{
                background: qlineargradient(
                    x1:0, y1:0, x2:1, y2:0,
                    stop:0 #dedee4,
                    stop:0.5 #ececf0,
                    stop:1 #dedee4
                );
                min-height: 25px;
                margin: 2px;
                border-radius: 7px;
                border: 1px solid #c8c8d2;
            }}
            QScrollBar::handle:vertical:hover,
            QScrollBar::handle:vertical:pressed {{
                background: qlineargradient(
                    x1:0, y1:0, x2:1, y2:0,
                    stop:0 {sd},
                    stop:0.5 {sm},
                    stop:1 {sd}
                );
                border: 1px solid {sb};
            }}
            QScrollBar::sub-line:vertical,
            QScrollBar::add-line:vertical {{
                border: 1px solid #c8c8d2;
                background: qlineargradient(
                    x1:0, y1:0, x2:0, y2:1,
                    stop:0 #f0f0f4,
                    stop:1 #e2e2ea
                );
                height: 14px;
                subcontrol-origin: margin;
            }}
            QScrollBar::sub-line:vertical {{
                subcontrol-position: top;
                border-radius: 0px;
            }}
            QScrollBar::add-line:vertical {{
                subcontrol-position: bottom;
                border-radius: 0px;
            }}
            QScrollBar::sub-line:vertical:hover,
            QScrollBar::add-line:vertical:hover {{
                background: qlineargradient(
                    x1:0, y1:0, x2:0, y2:1,
                    stop:0 #f6f6fa,
                    stop:1 #eaeaf0
                );
                border: 2px solid {a.rgba(210)};
            }}
            QScrollBar::sub-line:vertical:pressed,
            QScrollBar::add-line:vertical:pressed {{
                background: qlineargradient(
                    x1:0, y1:0, x2:0, y2:1,
                    stop:0 #d8d8e2,
                    stop:1 #ceced8
                );
                border: 2px solid {a.rgba(255)};
            }}
            QScrollBar:horizontal {{
                background: #ececf0;
                height: 14px;
                border: none;
                margin: 0px;
                border-radius: 7px;
            }}
            QScrollBar::handle:horizontal {{
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #dedee4, stop:0.3 #e8e8ee, stop:0.5 #f0f0f4, stop:0.7 #e4e4ea, stop:1 #d6d6de);
                min-width: 30px;
                border-radius: 7px;
                margin: 2px;
                border: 2px solid {a.rgba(90)};
                border-top: 1px solid #f4f4f8;
                border-bottom: 1px solid #bcbcc8;
            }}
            QScrollBar::handle:horizontal:hover {{
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #eaeaf0, stop:0.5 #f4f4f8, stop:1 #e0e0e8);
                border: 2px solid {a.rgba(200)};
            }}
            QScrollBar::handle:horizontal:pressed {{
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #d8d8e2, stop:0.5 #e0e0e8, stop:1 #d8d8e2);
                border: 2px solid {a.rgba(255)};
            }}
            QScrollBar::add-line:horizontal {{
                width: 14px;
                subcontrol-origin: margin;
                subcontrol-position: right;
                background: #ececf0;
                border-left: 1px solid #c8c8d2;
                border-radius: 0px 7px 7px 0px;
            }}
            QScrollBar::add-line:horizontal:hover {{
                background: #e2e2ea;
            }}
            QScrollBar::add-line:horizontal:pressed {{
                background: #d8d8e2;
            }}
            QScrollBar::sub-line:horizontal {{
                width: 14px;
                subcontrol-origin: margin;
                subcontrol-position: left;
                background: #ececf0;
                border-right: 1px solid #c8c8d2;
                border-radius: 7px 0px 0px 7px;
            }}
            QScrollBar::sub-line:horizontal:hover {{
                background: #e2e2ea;
            }}
            QScrollBar::sub-line:horizontal:pressed {{
                background: #d8d8e2;
            }}
            QScrollBar::left-arrow:horizontal {{
                image: none;
                border: none;
                width: 0px;
                height: 0px;
                border-top: 4px solid transparent;
                border-bottom: 4px solid transparent;
                border-right: 5px solid #404050;
                margin: 3px;
            }}
            QScrollBar::left-arrow:horizontal:hover {{
                border-right-color: {a.rgba(255)};
            }}
            QScrollBar::right-arrow:horizontal {{
                image: none;
                border: none;
                width: 0px;
                height: 0px;
                border-top: 4px solid transparent;
                border-bottom: 4px solid transparent;
                border-left: 5px solid #404050;
                margin: 3px;
            }}
            QScrollBar::right-arrow:horizontal:hover {{
                border-left-color: {a.rgba(255)};
            }}
            QScrollBar::add-page:horizontal, QScrollBar::sub-page:horizontal {{
                background: none;
            }}
            QGroupBox {{
                background-color: #f0f0f4;
                border: 1px solid #d0d0d8;
                border-radius: 4px;
                margin-top: 14px;
                padding: 16px 14px 14px 14px;
                padding-top: 22px;
                font-size: 11px;
            }}
            QGroupBox::title {{
                subcontrol-origin: margin;
                subcontrol-position: top left;
                left: 8px;
                padding: 0 6px;
                color: #1a1a1e;
                background-color: #f0f0f4;
            }}
            QFormLayout {{
                background-color: transparent;
            }}
        """

    def small_dialog_qss(self) -> str:
        if self.is_light_theme():
            return self._small_dialog_qss_light()
        return self._small_dialog_qss_dark()

    def _small_dialog_qss_dark(self) -> str:
        """Add Custom Game / First Backup Destination — compact dark dialogs."""
        a = self
        body = a.ui_body_text_dark()
        ff = a.ui_font_family_css()
        return f"""
            QDialog {{
                background-color: #202020;
                color: {body};
                font-family: {ff};
                font-size: 11px;
            }}
            QLabel {{
                color: {body};
            }}
            QPushButton {{
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #454548, stop:1 #2a2a2d);
                border: 1px solid #3e3e42;
                border-radius: 4px;
                color: {body};
                min-height: 19px;
                max-height: 19px;
                height: 19px;
                padding: 1px 12px;
                font-size: 11px;
            }}
            QPushButton:hover {{
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #505053, stop:1 #3a3a3d);
                border: 2px solid {a.rgba(200)};
                border-radius: 4px;
            }}
            QPushButton:pressed {{
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #2d2d30, stop:1 #202020);
            }}
            QLineEdit {{
                background-color: #202020;
                border: 1px solid #404040;
                border-radius: 4px;
                color: {body};
                padding: 2px 8px;
            }}
            QCheckBox {{
                color: {body};
                spacing: 6px;
            }}
            QCheckBox::indicator {{
                width: 12px;
                height: 12px;
            }}
        """

    def _small_dialog_qss_light(self) -> str:
        a = self
        body = a.ui_body_text_light()
        ff = a.ui_font_family_css()
        return f"""
            QDialog {{
                background-color: #f4f4f7;
                color: {body};
                font-family: {ff};
                font-size: 11px;
            }}
            QLabel {{
                color: {body};
            }}
            QPushButton {{
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #fafafc, stop:1 #e8e8ee);
                border: 1px solid #c4c4ce;
                border-radius: 4px;
                color: {body};
                min-height: 19px;
                max-height: 19px;
                height: 19px;
                padding: 1px 12px;
                font-size: 11px;
            }}
            QPushButton:hover {{
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #ffffff, stop:1 #efeff4);
                border: 2px solid {a.rgba(200)};
                border-radius: 4px;
            }}
            QPushButton:pressed {{
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #dedee6, stop:1 #d4d4dc);
            }}
            QLineEdit {{
                background-color: #ffffff;
                border: 1px solid #c4c4cc;
                border-radius: 4px;
                color: {body};
                padding: 2px 8px;
            }}
            QCheckBox {{
                color: {body};
                spacing: 6px;
            }}
            QCheckBox::indicator {{
                width: 12px;
                height: 12px;
            }}
        """

    def sandbox_monitor_window_qss(self) -> str:
        """Chrome for the optional ``--sandbox`` monitor (tabs, metrics strip, editors)."""
        if self.is_light_theme():
            return self._sandbox_monitor_window_qss_light()
        return self._sandbox_monitor_window_qss_dark()

    def _sandbox_monitor_window_qss_dark(self) -> str:
        sm = self
        body = sm.ui_body_text_dark()
        return f"""
            QMainWindow {{ background-color: #1e1e1e; color: #e0e0e0; }}
            QWidget {{ background-color: #1e1e1e; color: #e0e0e0; }}
            QWidget#sandboxMainTabs {{
                background-color: #1e1e1e;
            }}
            QWidget#sandboxFramedPanel {{
                background-color: #252526;
                border: none;
                border-radius: 6px;
            }}
            #sandboxMainTabs QTabBar {{
                background-color: transparent;
                border: none;
                padding: 0px;
                margin: 0px;
            }}
            #sandboxMainTabs QTabBar::tab {{
                background-color: transparent;
                color: #8c8c8c;
                border: 1px solid transparent;
                border-top-left-radius: 6px;
                border-top-right-radius: 6px;
                padding: 3px 6px 5px 6px;
                margin-right: 2px;
                margin-top: 0px;
                margin-bottom: 0px;
                font-size: 11px;
                min-height: 0px;
            }}
            #sandboxMainTabs QTabBar::tab:last {{
                margin-right: 0px;
            }}
            #sandboxMainTabs QTabBar::tab:selected {{
                background-color: #252526;
                color: {body};
                font-weight: 600;
                font-size: 11px;
                border: 1px solid #3e3e42;
                border-bottom: none;
                border-top-left-radius: 6px;
                border-top-right-radius: 6px;
                padding: 4px 8px 6px 8px;
                margin-top: 0px;
                margin-bottom: -1px;
            }}
            #sandboxMainTabs QTabBar::tab:hover:!selected {{
                background-color: transparent;
                color: #c8c8c8;
            }}
            QPushButton {{
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1, stop:0 #454548, stop:1 #2a2a2d);
                border: 1px solid #3e3e42; border-radius: 4px; color: #cccccc;
                min-height: 19px; max-height: 19px; height: 19px; padding: 1px 12px; font-size: 11px;
            }}
            QPushButton:hover {{
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1, stop:0 #505053, stop:1 #3a3a3d);
                border: 2px solid #888888;
            }}
            QPushButton:pressed {{
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1, stop:0 #3d3d40, stop:1 #2a2a2d);
                border: 2px solid #a0a0a0;
            }}
            QCheckBox {{
                spacing: 6px;
                color: {body};
                background-color: transparent;
                font-size: 11px;
            }}
            QCheckBox::indicator {{
                width: 12px;
                height: 12px;
                border: 2px solid #404040;
                border-radius: 3px;
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #2a2a2d, stop:1 #202020);
            }}
            QCheckBox::indicator:hover {{
                border: 2px solid #a8a8b0;
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #3a3a3d, stop:1 #2d2d30);
            }}
            QCheckBox::indicator:pressed {{
                border: 2px solid #d0d0d8;
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #202020, stop:1 #1a1a1a);
            }}
            QCheckBox::indicator:checked {{
                background-color: transparent;
                border: 2px solid #c8c8d0;
            }}
            QCheckBox::indicator:checked:hover {{
                background-color: transparent;
                border: 2px solid #d8d8e0;
            }}
            QCheckBox::indicator:checked:pressed {{
                background-color: transparent;
                border: 2px solid #e8e8ee;
            }}
            QCheckBox::indicator:disabled {{
                background: #202020;
                border: 2px solid #2d2d30;
            }}
            QWidget#sandboxDiagToolbar {{
                background-color: transparent;
            }}
            QWidget#sandboxDiagToolbar QLabel {{
                background-color: transparent;
                color: #b0b0b0;
                font-size: 11px;
            }}
            QLineEdit {{
                background-color: #252526;
                border: 1px solid #404040;
                border-radius: 4px;
                color: {body};
                padding: 3px 8px;
                font-size: 11px;
                min-height: 20px;
            }}
        """

    def _sandbox_monitor_window_qss_light(self) -> str:
        a = self
        body = a.ui_body_text_light()
        return f"""
            QMainWindow {{ background-color: #f4f4f7; color: #1a1a1e; }}
            QWidget {{ background-color: #f4f4f7; color: #1a1a1e; }}
            QWidget#sandboxMainTabs {{
                background-color: #f4f4f7;
            }}
            QWidget#sandboxFramedPanel {{
                background-color: #f4f4f7;
                border: none;
                border-radius: 6px;
            }}
            #sandboxMainTabs QTabBar {{
                background-color: transparent;
                border: none;
                padding: 0px;
                margin: 0px;
            }}
            #sandboxMainTabs QTabBar::tab {{
                background-color: transparent;
                color: #6a6a78;
                border: 1px solid transparent;
                border-top-left-radius: 6px;
                border-top-right-radius: 6px;
                padding: 3px 6px 5px 6px;
                margin-right: 2px;
                margin-top: 0px;
                margin-bottom: 0px;
                font-size: 11px;
                min-height: 0px;
            }}
            #sandboxMainTabs QTabBar::tab:last {{
                margin-right: 0px;
            }}
            #sandboxMainTabs QTabBar::tab:selected {{
                background-color: #f4f4f7;
                color: #0a0a0c;
                font-weight: 600;
                font-size: 11px;
                border: 1px solid #d0d0d8;
                border-bottom: none;
                border-top-left-radius: 6px;
                border-top-right-radius: 6px;
                padding: 4px 8px 6px 8px;
                margin-top: 0px;
                margin-bottom: -1px;
            }}
            #sandboxMainTabs QTabBar::tab:hover:!selected {{
                background-color: transparent;
                color: #2a2a32;
            }}
            QPushButton {{
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1, stop:0 #fafafc, stop:1 #e8e8ee);
                border: 1px solid #c4c4ce; border-radius: 4px; color: #141418;
                min-height: 19px; max-height: 19px; height: 19px; padding: 1px 12px; font-size: 11px;
            }}
            QPushButton:hover {{
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1, stop:0 #ffffff, stop:1 #efeff4);
                border: 2px solid #9898a4;
            }}
            QPushButton:pressed {{
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1, stop:0 #dedee6, stop:1 #d4d4dc);
                border: 2px solid #787888;
            }}
            QCheckBox {{
                spacing: 6px;
                color: #2a2a32;
                background-color: transparent;
                font-size: 11px;
            }}
            QCheckBox::indicator {{
                width: 12px;
                height: 12px;
                border: 2px solid #a8a8b4;
                border-radius: 3px;
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #ffffff, stop:1 #ececf0);
            }}
            QCheckBox::indicator:hover {{
                border: 2px solid #9898a4;
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #fafafc, stop:1 #f0f0f4);
            }}
            QCheckBox::indicator:pressed {{
                border: 2px solid #787888;
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 #e4e4ea, stop:1 #d8d8e2);
            }}
            QCheckBox::indicator:checked {{
                background-color: transparent;
                border: 2px solid #9898a4;
            }}
            QCheckBox::indicator:checked:hover {{
                background-color: transparent;
                border: 2px solid #787888;
            }}
            QCheckBox::indicator:checked:pressed {{
                background-color: transparent;
                border: 2px solid #686878;
            }}
            QCheckBox::indicator:disabled {{
                background: #ececf0;
                border: 2px solid #d8d8e0;
            }}
            QWidget#sandboxDiagToolbar {{
                background-color: transparent;
            }}
            QWidget#sandboxDiagToolbar QLabel {{
                background-color: transparent;
                color: #5a5a68;
                font-size: 11px;
            }}
            QLineEdit {{
                background-color: #ffffff;
                border: 1px solid #c4c4cc;
                border-radius: 4px;
                color: #1a1a1e;
                padding: 3px 8px;
                font-size: 11px;
                min-height: 20px;
            }}
        """
