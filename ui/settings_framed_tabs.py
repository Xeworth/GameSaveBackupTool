"""
Settings: QTabBar above a stacked content panel.

The pane background and radius come from QSS on ``#settingsFramedPanel``.
The frame outline is a single ``addRoundedRect`` stroke (identical corners), then
the top border segment under the selected tab is covered with the pane
background (same ``#3e3e42`` / ``#d0d0d8`` stroke as ``QTableWidget``).
"""

from __future__ import annotations

from PyQt6.QtCore import QPoint, QPointF, QRectF, Qt, pyqtSignal
from PyQt6.QtGui import QColor, QKeySequence, QPainter, QPainterPath, QPen, QShortcut
from PyQt6.QtWidgets import QHBoxLayout, QSizePolicy, QStackedWidget, QTabBar, QVBoxLayout, QWidget

from styles.manager import StyleManager

TAB_STRIP_LEFT_INSET_PX = 9
PANEL_CONTENT_PADDING_PX = 9


class _FramedStackPanel(QWidget):
    """Hosts the stacked pages; outline is painted (rounded rect + top gap via mask)."""

    def __init__(
        self,
        panel_object_name: str = "settingsFramedPanel",
        *,
        content_padding: int = PANEL_CONTENT_PADDING_PX,
    ) -> None:
        super().__init__()
        self._tab_bar: QTabBar | None = None
        self._stack = QStackedWidget()
        self.setObjectName(panel_object_name)
        self.setAttribute(Qt.WidgetAttribute.WA_StyledBackground, True)

        pad = max(0, int(content_padding))
        lay = QVBoxLayout(self)
        lay.setContentsMargins(pad, pad, pad, pad)
        lay.setSpacing(0)
        lay.addWidget(self._stack)

    def set_tab_bar(self, tab_bar: QTabBar) -> None:
        self._tab_bar = tab_bar

    def stack(self) -> QStackedWidget:
        return self._stack

    def resizeEvent(self, event) -> None:
        super().resizeEvent(event)
        self.update()

    def _border_color(self) -> QColor:
        sm = StyleManager.instance()
        if sm.is_light_theme():
            return QColor("#d0d0d8")
        return QColor("#3e3e42")

    def _panel_bg_color(self) -> QColor:
        """Must match the framed panel background in QSS (settings or sandbox, same hex)."""
        sm = StyleManager.instance()
        if sm.is_light_theme():
            return QColor("#f4f4f7")
        return QColor("#252526")

    def _erase_top_gap_under_tab(
        self,
        p: QPainter,
        cl: float,
        cr: float,
    ) -> None:
        """Paint over the stroked top segment between tab edges (same bg as pane)."""
        if cr <= cl:
            return
        p.setRenderHint(QPainter.RenderHint.Antialiasing, False)
        p.setPen(Qt.PenStyle.NoPen)
        p.setBrush(self._panel_bg_color())
        # Cover 1px stroke + AA fringe without eating into rounded corners (gap is inset on the flat top).
        p.drawRect(QRectF(cl, 0.0, cr - cl, 3.0))
        p.setBrush(Qt.BrushStyle.NoBrush)

    def _paint_sharp_outline(
        self,
        p: QPainter,
        pen: QPen,
        x: float,
        y: float,
        wi: float,
        hi: float,
        cl: float,
        cr: float,
    ) -> None:
        p.setPen(pen)
        if cr <= cl:
            p.drawLine(QPointF(x, y), QPointF(x + wi, y))
        else:
            p.drawLine(QPointF(x, y), QPointF(cl, y))
            p.drawLine(QPointF(cr, y), QPointF(x + wi, y))
        p.drawLine(QPointF(x + wi, y), QPointF(x + wi, y + hi))
        p.drawLine(QPointF(x + wi, y + hi), QPointF(x, y + hi))
        p.drawLine(QPointF(x, y + hi), QPointF(x, y))

    def paintEvent(self, event) -> None:
        super().paintEvent(event)
        w, h = self.width(), self.height()
        if w < 3 or h < 3:
            return

        tb = self._tab_bar
        if tb is None:
            return

        idx = tb.currentIndex()
        if idx < 0:
            return

        tr_tab = tb.tabRect(idx)
        gl = tb.mapToGlobal(QPoint(tr_tab.left(), tr_tab.bottom()))
        gr = tb.mapToGlobal(QPoint(tr_tab.right(), tr_tab.bottom()))
        p_left = self.mapFromGlobal(gl)
        p_right = self.mapFromGlobal(gr)
        cut_l = min(p_left.x(), p_right.x())
        cut_r = max(p_left.x(), p_right.x())
        gap_pad = 2
        cut_l = max(0, min(w - 1, cut_l - gap_pad))
        cut_r = max(0, min(w - 1, cut_r + gap_pad))
        if cut_r < cut_l:
            cut_l, cut_r = cut_r, cut_l

        # Half-pixel inset so a 1px cosmetic pen is not clipped on top/left (uniform edge weight).
        inset = 0.5
        x = inset
        y = inset
        wi = float(w) - 1.0
        hi = float(h) - 1.0
        if wi < 1.0 or hi < 1.0:
            return

        pen = QPen(self._border_color())
        pen.setWidthF(1.0)
        pen.setCosmetic(True)
        pen.setJoinStyle(Qt.PenJoinStyle.RoundJoin)
        pen.setCapStyle(Qt.PenCapStyle.FlatCap)

        p = QPainter(self)
        p.setRenderHint(QPainter.RenderHint.Antialiasing, True)

        max_r = min(6.0, wi * 0.5 - 1.0, hi * 0.5 - 1.0)
        if max_r < 1.0:
            cl = max(x, min(float(cut_l), x + wi))
            cr = min(x + wi, max(float(cut_r), x))
            pen.setJoinStyle(Qt.PenJoinStyle.MiterJoin)
            self._paint_sharp_outline(p, pen, x, y, wi, hi, cl, cr)
            self._erase_top_gap_under_tab(p, cl, cr)
            return

        pen.setJoinStyle(Qt.PenJoinStyle.RoundJoin)
        p.setPen(pen)
        path_full = QPainterPath()
        path_full.addRoundedRect(QRectF(x, y, wi, hi), max_r, max_r)
        p.strokePath(path_full, pen)
        x_tl = x + max_r
        x_tr = x + wi - max_r
        cl_flat = max(x_tl, min(float(cut_l), x_tr))
        cr_flat = min(x_tr, max(float(cut_r), x_tl))
        self._erase_top_gap_under_tab(p, cl_flat, cr_flat)


class SettingsFramedTabs(QWidget):
    """Drop-in replacement for the settings QTabWidget (same addTab / stack API)."""

    currentChanged = pyqtSignal(int)

    def __init__(
        self,
        parent: QWidget | None = None,
        *,
        main_tabs_object_name: str = "settingsMainTabs",
        framed_panel_object_name: str = "settingsFramedPanel",
        framed_content_padding: int | None = None,
    ) -> None:
        super().__init__(parent)
        self.setObjectName(main_tabs_object_name)

        pad = framed_content_padding if framed_content_padding is not None else PANEL_CONTENT_PADDING_PX
        self._panel = _FramedStackPanel(framed_panel_object_name, content_padding=pad)
        self._bar = QTabBar()
        self._panel.set_tab_bar(self._bar)

        self._bar.setExpanding(False)
        self._bar.setDocumentMode(False)
        self._bar.setUsesScrollButtons(False)
        self._bar.setDrawBase(False)
        self._bar.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Fixed)
        self._bar.currentChanged.connect(self._on_bar_index_changed)

        tab_row = QWidget()
        tab_row_lay = QHBoxLayout(tab_row)
        tab_row_lay.setContentsMargins(TAB_STRIP_LEFT_INSET_PX, 0, 0, 0)
        tab_row_lay.setSpacing(0)
        tab_row_lay.addWidget(self._bar, 1)

        outer = QVBoxLayout(self)
        outer.setContentsMargins(0, 0, 0, 0)
        outer.setSpacing(0)
        outer.addWidget(tab_row)
        outer.addWidget(self._panel, 1)

        sc_next = QShortcut(QKeySequence("Ctrl+Tab"), self)
        sc_next.setContext(Qt.ShortcutContext.WidgetWithChildrenShortcut)
        sc_next.activated.connect(self._activate_next_tab)
        sc_prev = QShortcut(QKeySequence("Ctrl+Shift+Tab"), self)
        sc_prev.setContext(Qt.ShortcutContext.WidgetWithChildrenShortcut)
        sc_prev.activated.connect(self._activate_prev_tab)

    def _activate_next_tab(self) -> None:
        n = self._bar.count()
        if n <= 1:
            return
        self._bar.setCurrentIndex((self._bar.currentIndex() + 1) % n)

    def _activate_prev_tab(self) -> None:
        n = self._bar.count()
        if n <= 1:
            return
        self._bar.setCurrentIndex((self._bar.currentIndex() - 1 + n) % n)

    def _on_bar_index_changed(self, index: int) -> None:
        sw = self._panel.stack()
        if 0 <= index < sw.count():
            sw.setCurrentIndex(index)
        self._panel.update()
        self.currentChanged.emit(index)

    def tabBar(self) -> QTabBar:
        return self._bar

    def stack(self) -> QStackedWidget:
        return self._panel.stack()

    def addTab(self, widget: QWidget, label: str) -> int:
        self._panel.stack().addWidget(widget)
        return self._bar.addTab(label)

    def currentIndex(self) -> int:
        return self._bar.currentIndex()

    def setCurrentIndex(self, index: int) -> None:
        self._bar.setCurrentIndex(index)

    def count(self) -> int:
        return self._bar.count()

    def widget(self, index: int) -> QWidget | None:
        return self._panel.stack().widget(index)
