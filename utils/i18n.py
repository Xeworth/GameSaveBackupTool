"""
Optional Qt translations (lrelease .qm files).

Ship translations as ``translations/gsbt_<locale>.qm`` (e.g. ``gsbt_de.qm``).
Pipeline: extract strings with ``pylupdate6`` / Qt Linguist → translate →
``lrelease`` to build .qm → place next to the app or under ``translations/``.

``QSettings`` key ``ui_language``: ``en`` (default) loads no translator; any
other code attempts ``gsbt_<code>.qm`` from the app resource directory.
"""
from __future__ import annotations

import os
import sys
from PyQt6.QtCore import QSettings, QTranslator
from PyQt6.QtWidgets import QApplication


def _base_dir() -> str:
    try:
        return sys._MEIPASS  # type: ignore[attr-defined]
    except Exception:
        return os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))


def available_ui_language_codes() -> list[tuple[str, str]]:
    """(label, code) for settings UI — extend when .qm files exist."""
    return [
        ("English", "en"),
    ]


def install_ui_translators(app: QApplication, settings: QSettings) -> list[QTranslator]:
    """
    Install translators for ``ui_language``. Returns installed translators
    (keep references on the caller so they are not garbage-collected).
    """
    installed: list[QTranslator] = []
    lang = (settings.value("ui_language", "en", type=str) or "en").strip().lower()
    if lang in ("", "en", "c"):
        return installed

    base = _base_dir()
    paths = [
        os.path.join(base, "translations"),
        os.path.join(os.path.dirname(base), "translations"),
    ]

    app_trans = QTranslator(app)
    for d in paths:
        qm = os.path.join(d, f"gsbt_{lang}.qm")
        if os.path.isfile(qm) and app_trans.load(qm):
            app.installTranslator(app_trans)
            installed.append(app_trans)
            break
    return installed
