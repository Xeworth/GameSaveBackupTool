import sys
import os
import argparse

# Parse --sandbox before importing UI so config.app_config sees it (fresh settings for testing).
parser = argparse.ArgumentParser(description="Game Save Backup Tool")
parser.add_argument(
    "-s",
    "--sandbox",
    action="store_true",
    help="Fresh QSettings scope + opens Sandbox Monitor (CPU/RAM, compression & scan timings).",
)
parser.add_argument(
    "--minimized",
    action="store_true",
    help="Start minimized to taskbar (used when run from Windows startup).",
)
parser.add_argument(
    "--hidden",
    action="store_true",
    help="Start hidden in system tray (used when run from Windows startup).",
)
args = parser.parse_args()
if args.sandbox:
    os.environ["GSBT_SANDBOX"] = "1"

from PyQt6.QtCore import QSettings
from PyQt6.QtWidgets import QApplication
from PyQt6.QtGui import QFont, QIcon

from config.app_config import SANDBOX, settings_app_name
from utils.i18n import install_ui_translators
from ui.main_window import MainWindow

# --- NEW: Helper function to find files when packaged ---
def resource_path(relative_path):
    """ Get absolute path to resource, works for dev and for PyInstaller """
    try:
        # PyInstaller creates a temp folder and stores path in _MEIPASS
        base_path = sys._MEIPASS
    except Exception:
        base_path = os.path.abspath(".")
    return os.path.join(base_path, relative_path)


def main():
    app = QApplication(sys.argv)
    base_font = QFont()
    base_font.setFamilies(
        ["Segoe UI", "Segoe UI Variable", "Segoe UI Variable Text", "Tahoma"]
    )
    base_font.setPointSize(9)
    app.setFont(base_font)
    # Optional Qt translations (.qm); keep references on the app object
    _qs = QSettings("MyCompany", settings_app_name())
    app._gsbt_translators = install_ui_translators(app, _qs)  # noqa: SLF001

    icon_path = resource_path("gsbt.ico")
    app_icon = QIcon(icon_path)
    app.setWindowIcon(app_icon)

    sandbox_monitor = None
    # Sandbox UI is only imported when GSBT_SANDBOX is set (-s / --sandbox). For PyInstaller retail
    # builds you can omit ui/sandbox_monitor.py and ui/sandbox_log_settings_dialog.py from the bundle;
    # MainWindow imports ``config/sandbox_log_prefs.py`` (small QSettings helpers) for compression
    # tick-note toggles; if that module is omitted from a bundle, a built-in fallback is used.
    if SANDBOX:
        from ui.sandbox_monitor import SandboxMonitorWindow

        sandbox_monitor = SandboxMonitorWindow()
        sandbox_monitor.setWindowIcon(app_icon)

    window = MainWindow(sandbox_monitor=sandbox_monitor)
    window.setWindowIcon(app_icon)
    if args.hidden:
        window.hide()
    elif args.minimized:
        window.showMinimized()
    else:
        window.show()
        window.raise_()
        window.activateWindow()
    if sandbox_monitor is not None:
        sandbox_monitor.set_main_window(window)
        sandbox_monitor.apply_app_style()
        # Show monitor after the main window so taskbar ordering is main (left) then monitor (right).
        sandbox_monitor.show()
    sys.exit(app.exec())

if __name__ == "__main__":
    main()