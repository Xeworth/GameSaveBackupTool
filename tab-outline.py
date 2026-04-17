import sys
from PyQt6.QtWidgets import QApplication, QTabWidget, QWidget, QVBoxLayout, QLabel
from PyQt6.QtCore import Qt

class ModernSettingsTabs(QTabWidget):
    def __init__(self):
        super().__init__()

        # 1. Connect the signal to our custom logic
        self.currentChanged.connect(self.handle_tab_change)
        
        # 2. Add some dummy tabs for demonstration
        self.addTab(self._create_dummy_tab("Backup Content"), "Backup")
        self.addTab(self._create_dummy_tab("Compression Content"), "Compression")
        self.addTab(self._create_dummy_tab("Appearance Content"), "Appearance")
        self.addTab(self._create_dummy_tab("System Content"), "System")

        # 3. Initialize the style for the first tab
        self.handle_tab_change(0)

    def _create_dummy_tab(self, text):
        widget = QWidget()
        layout = QVBoxLayout(widget)
        label = QLabel(text)
        label.setStyleSheet("color: #bbb; font-size: 14px;")
        layout.addWidget(label, alignment=Qt.AlignmentFlag.AlignCenter)
        return widget

    def handle_tab_change(self, index):
        """
        This is the magic part. We set a custom property 'isFirstTab' 
        which our CSS can target specifically.
        """
        is_first = (index == 0)
        self.setProperty("isFirstTab", is_first)
        
        # This forces the widget to re-read the stylesheet rules
        self.style().unpolish(self)
        self.style().polish(self)

# --- THE STYLESHEET ---
# This is where the visual 'fusion' happens.
STYLESHEET = """
/* The 'Window' background (Color 1) */
QMainWindow, QDialog {
    background-color: #121212;
}

/* The 'Widget' container (Color 2) */
QTabWidget::pane {
    border: 1px solid #444444;
    background-color: #1e1e1e;
    border-radius: 12px;
    top: -1px; /* Overlap the tab bar line */
}

/* The Logic: Square the top-left ONLY if first tab is active */
QTabWidget[isFirstTab="true"]::pane {
    border-top-left-radius: 0px;
}

/* Tab Bar configuration */
QTabBar::tab {
    background-color: transparent;
    color: #888888;
    padding: 10px 20px;
    margin-right: 4px;
    border: 1px solid transparent;
    border-top-left-radius: 8px;
    border-top-right-radius: 8px;
}

/* Selected Tab styling */
QTabBar::tab:selected {
    color: #ffffff;
    background-color: #1e1e1e; /* Must match Color 2 */
    border: 1px solid #444444;
    border-bottom: none; /* Dissolves the line between tab and pane */
    margin-bottom: -1px; /* Ensures overlap */
}

/* Make sure the very first tab flush edge is sharp when selected */
QTabBar::tab:first:selected {
    border-top-left-radius: 8px; /* Optional: Keep tab rounded, or 0 if you want it square */
}
"""

if __name__ == "__main__":
    app = QApplication(sys.argv)
    
    # Apply the stylesheet to the whole app
    app.setStyleSheet(STYLESHEET)
    
    window = QWidget()
    window.setWindowTitle("Settings Logic Demo")
    window.resize(600, 400)
    
    layout = QVBoxLayout(window)
    tabs = ModernSettingsTabs()
    layout.addWidget(tabs)
    
    window.show()
    sys.exit(app.exec())