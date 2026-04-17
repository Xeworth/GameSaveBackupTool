import sys
import os
import argparse

# Parse --sandbox before importing UI so app_config sees it (fresh settings for testing).
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

from PyQt6.QtWidgets import QApplication
from PyQt6.QtGui import QIcon

from app_config import SANDBOX
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
    icon_path = resource_path("gsbt.ico")
    app_icon = QIcon(icon_path)
    app.setWindowIcon(app_icon)

    sandbox_monitor = None
    if SANDBOX:
        from ui.sandbox_monitor import SandboxMonitorWindow

        sandbox_monitor = SandboxMonitorWindow()
        sandbox_monitor.setWindowIcon(app_icon)

    window = MainWindow(sandbox_monitor=sandbox_monitor)
    if sandbox_monitor is not None:
        sandbox_monitor.apply_app_style()
        sandbox_monitor.show()
        sandbox_monitor.raise_()
        sandbox_monitor.activateWindow()
    window.setWindowIcon(app_icon)
    if args.hidden:
        window.hide()
    elif args.minimized:
        window.showMinimized()
    else:
        window.show()
    sys.exit(app.exec())

if __name__ == "__main__":
    main()