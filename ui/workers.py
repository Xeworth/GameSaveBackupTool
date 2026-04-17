"""
Background workers: game detection, PCGW fetch, backup, compress, and optional watchdog handler.

Kept separate from ``main_window`` to keep the UI module focused on layout and signals.
"""

from __future__ import annotations

import difflib
import os
import re
import shutil
import threading
import time
import zipfile
from datetime import datetime
import subprocess
import tempfile
from typing import Callable, List, Optional, Tuple

from PyQt6.QtCore import QThread, pyqtSignal

from core.compression import CompressionOptions
from core.game_detector import GameDetector
from core.save_location_fetcher import (
    SaveLocationFetcher,
    build_title_variants_for_fallback,
    normalize_for_match,
    normalize_wiki_page_title,
    path_to_directory_only,
)
from core.registry_save_resolver import (
    extract_registry_hints,
    format_registry_save_display,
    looks_like_registry_hive_line,
    merged_steam_registry_save_keys,
    resolve_registry_hint_to_save_folder,
    try_registry_key_as_in_key_save_location,
    wiki_cell_looks_like_file_path,
)
from core.save_manager import SaveManager

try:
    from watchdog.events import FileSystemEventHandler
    from watchdog.observers import Observer

    WATCHDOG_AVAILABLE = True
except ImportError:
    WATCHDOG_AVAILABLE = False
    Observer = None  # type: ignore
    FileSystemEventHandler = object  # type: ignore


class GameDetectorWorker(QThread):
    finished = pyqtSignal(list)
    error = pyqtSignal(str)

    def run(self):
        try:
            detector = GameDetector()
            games = detector.detect_all_games()
            self.finished.emit(games)
        except Exception as e:
            self.error.emit(f"Error during game detection: {e}")


class SaveLocationFetcherWorker(QThread):
    game_save_fetched = pyqtSignal(dict)
    all_fetching_finished = pyqtSignal()
    error = pyqtSignal(str)
    save_fetch_metrics = pyqtSignal(object)
    save_fetch_trace = pyqtSignal(str)

    def __init__(self, games_to_fetch, steam_ids, parent=None):
        super().__init__(parent)
        self.games_to_fetch = games_to_fetch
        self.steam_ids = steam_ids if steam_ids else {}
        self.save_manager = SaveManager()
        self.completed_count = 0
        self.total_count = len(games_to_fetch)
        self.is_cancelled = False
        self._sessions_to_close = []
        self._counter_lock = None

    def run(self):
        import concurrent.futures
        import threading

        import requests

        self._counter_lock = threading.Lock()
        self._sessions_to_close = []
        sess_lock = threading.Lock()

        def _init_thread_session() -> None:
            s = requests.Session()
            s.headers.update({"User-Agent": SaveLocationFetcher.USER_AGENT})
            threading.current_thread()._gsbt_http_session = s
            with sess_lock:
                self._sessions_to_close.append(s)

        max_workers = min(6, max(1, len(self.games_to_fetch)))

        with concurrent.futures.ThreadPoolExecutor(max_workers=max_workers, initializer=_init_thread_session) as executor:
            future_to_game = {
                executor.submit(self._process_single_game, game): game for game in self.games_to_fetch
            }
            for future in concurrent.futures.as_completed(future_to_game):
                if self.is_cancelled:
                    break
                try:
                    game_data = future.result()
                    if game_data:
                        self.game_save_fetched.emit(game_data)
                    with self._counter_lock:
                        self.completed_count += 1
                except Exception as e:
                    print(f"Error processing game: {e}")

        for s in self._sessions_to_close:
            try:
                s.close()
            except Exception:
                pass
        self._sessions_to_close.clear()
        self.all_fetching_finished.emit()

    def _process_single_game(self, game):
        if self.is_cancelled:
            return None

        import requests

        t0 = time.perf_counter()
        game_name = game.get("name")
        app_id = game.get("app_id")
        install_path = game.get("install_path")
        game_short = (game_name or "?")[:42]

        def tr(msg: str) -> None:
            ts = datetime.now().strftime("%H:%M:%S.%f")[:-3]
            off = time.perf_counter() - t0
            self.save_fetch_trace.emit(f"{ts} +{off:7.3f}s | {game_short} | {msg}")

        session = getattr(threading.current_thread(), "_gsbt_http_session", None)
        if session is None:
            session = requests.Session()
            session.headers.update({"User-Agent": SaveLocationFetcher.USER_AGENT})
        fetcher = OptimizedSaveLocationFetcher(session, lock=None, trace=tr)
        tr(f"BEGIN fetch app_id={app_id!r}")
        potential_paths = fetcher.fetch_save_location(game_name, app_id)
        final_raw_path, source_type = None, "Not Found"
        resolved_via_registry = False
        registry_only = False
        reg_hive: Optional[str] = None
        reg_sub: Optional[str] = None

        hint_list: List[str] = []
        if potential_paths and isinstance(potential_paths, dict):
            for platform_key in ["steam", "windows"]:
                path = potential_paths.get(platform_key)
                if not path:
                    continue
                if "<user-id>" in path:
                    path_try1 = path.replace("<user-id>", self.steam_ids.get("steamid64", ""))
                    resolved_try1 = self.save_manager.resolve_path(path_try1, install_path)
                    if resolved_try1 and os.path.exists(resolved_try1):
                        final_raw_path = path_try1
                        break
                    path_try2 = path.replace("<user-id>", self.steam_ids.get("steamid3", ""))
                    resolved_try2 = self.save_manager.resolve_path(path_try2, install_path)
                    if resolved_try2 and os.path.exists(resolved_try2):
                        final_raw_path = path_try2
                        break
                else:
                    resolved_path = self.save_manager.resolve_path(path, install_path)
                    if resolved_path and os.path.exists(resolved_path):
                        final_raw_path = path
                        break

            rh = potential_paths.get("registry_hints")
            if isinstance(rh, list):
                hint_list.extend(str(x).strip() for x in rh if str(x).strip())
            for platform_key in ("windows", "steam"):
                p = potential_paths.get(platform_key)
                if isinstance(p, str) and p.strip():
                    hint_list.extend(extract_registry_hints(p))

        aid = str(app_id).strip() if app_id else ""
        steam_reg_pair = merged_steam_registry_save_keys().get(aid)
        if steam_reg_pair:
            hn, sk = steam_reg_pair
            hint_full = f"{hn}\\{sk}"
            if hint_full not in hint_list:
                hint_list.insert(0, hint_full)

        hint_list = list(dict.fromkeys([x.strip() for x in hint_list if x.strip()]))

        if not final_raw_path and hint_list:
            for h in hint_list:
                folder = resolve_registry_hint_to_save_folder(h)
                if folder:
                    resolved_reg = self.save_manager.resolve_path(folder, install_path)
                    if resolved_reg and os.path.isdir(resolved_reg):
                        final_raw_path = folder
                        resolved_via_registry = True
                        source_type = "PCGW (registry)"
                        tr(f"resolved save folder via registry hint → {folder!r}")
                        break
                loc = try_registry_key_as_in_key_save_location(h)
                if loc:
                    registry_only = True
                    reg_hive, reg_sub = loc
                    source_type = "Registry (in-key save data)"
                    tr(f"save data lives in registry key → {format_registry_save_display(reg_hive, reg_sub)!r}")
                    break

        if final_raw_path and not resolved_via_registry:
            source_type = "PCGW"
        elif (
            potential_paths
            and isinstance(potential_paths, dict)
            and not final_raw_path
            and not registry_only
        ):
            print(f"    [DEBUG] Save path not found for '{game_name}'. Paths from wiki were tried but none exist on disk:")
            for platform_key in ["steam", "windows"]:
                path = potential_paths.get(platform_key)
                if not path:
                    continue
                resolved = self.save_manager.resolve_path(path, install_path)
                exists = os.path.exists(resolved) if resolved else False
                print(f"    [DEBUG]   {platform_key}: raw={path!r} -> resolved={resolved!r} exists={exists}")

        save_data = {"steam_app_id": app_id}
        if final_raw_path:
            save_data["save_path"] = final_raw_path
            if resolved_via_registry:
                save_data["save_resolved_via_registry"] = True
        elif registry_only and reg_hive and reg_sub:
            save_data["save_path"] = ""
            save_data["save_registry_hive"] = reg_hive
            save_data["save_registry_subkey"] = reg_sub
            save_data["save_in_registry_only"] = True
        else:
            save_data["save_path"] = ""

        self.save_manager.add_or_update_save_location(game_name, save_data)

        wall = time.perf_counter() - t0
        if final_raw_path:
            wiki_outcome = "SAVE_ON_DISK"
        elif registry_only:
            wiki_outcome = "REGISTRY_IN_KEY"
        elif potential_paths:
            wiki_outcome = "WIKI_PATHS_NO_DISK"
        else:
            wiki_outcome = "NO_WIKI_DATA"
        tr(f"END {wiki_outcome} | HTTP={fetcher.http_call_count} | {wall:.3f}s")
        found_any = bool(final_raw_path or registry_only)
        self.save_fetch_metrics.emit(
            {
                "game": game_name,
                "wall_sec": round(wall, 3),
                "found": found_any,
                "source": source_type,
                "http_calls": getattr(fetcher, "http_call_count", 0),
                "wiki_outcome": wiki_outcome,
            }
        )

        spr = self.save_manager.resolve_path(final_raw_path, install_path) if final_raw_path else None
        sld = spr or (
            format_registry_save_display(reg_hive, reg_sub) if registry_only and reg_hive and reg_sub else None
        )
        return {
            "name": game_name,
            "app_id": app_id,
            "install_path": install_path,
            "platform": game.get("platform", "Unknown"),
            "save_path_raw": final_raw_path,
            "save_path_resolved": spr,
            "save_location_display": sld,
            "save_in_registry_only": registry_only,
            "save_registry_hive": reg_hive,
            "save_registry_subkey": reg_sub,
            "source": source_type,
        }

    def cancel(self):
        self.is_cancelled = True


class OptimizedSaveLocationFetcher:
    BASE_URL = "https://www.pcgamingwiki.com/w/api.php"
    USER_AGENT = "GameSaveBackupTool/1.1"

    def __init__(
        self,
        session,
        lock: Optional[threading.Lock] = None,
        trace: Optional[Callable[[str], None]] = None,
    ):
        self.session = session
        self.lock = lock
        self._trace = trace
        # Per-fetcher pacing (one fetcher per game task on this thread's Session).
        self._last_local_request = 0.0
        self.http_call_count = 0

    def _t(self, msg: str) -> None:
        if self._trace:
            try:
                self._trace(msg)
            except Exception:
                pass

    def _rate_limit(self):
        min_delay = 0.1
        now = time.time()
        elapsed = now - self._last_local_request
        if self._last_local_request > 0.0 and elapsed < min_delay:
            time.sleep(min_delay - elapsed)
        self._last_local_request = time.time()

    def _session_get(self, *args, **kwargs):
        """Use one Session per thread — no lock. If lock is set, wrap get (shared session, legacy)."""
        self._rate_limit()
        if self.lock is not None:
            with self.lock:
                self.http_call_count += 1
                return self.session.get(*args, **kwargs)
        self.http_call_count += 1
        return self.session.get(*args, **kwargs)

    def _get_page_name_from_app_id(self, app_id):
        self._t("HTTP wiki search appid:…")
        params = {"action": "query", "list": "search", "srsearch": f"appid:{app_id}", "format": "json"}
        try:
            response = self._session_get(self.BASE_URL, params=params, timeout=10)
            response.raise_for_status()
            data = response.json()
            hits = data.get("query", {}).get("search") or []
            if not hits:
                self._t("appid search no hits (wiki returned empty list)")
                return None
            title = hits[0].get("title")
            if title:
                print(f"--> Found page name for App ID {app_id}: '{title}'")
                self._t(f"appid search OK → {title!r}")
                return title
            self._t("appid search empty (no title in results)")
            return None
        except Exception as e:
            print(f"!! Could not find page by App ID {app_id}: {e}")
            self._t(f"appid search FAIL ({type(e).__name__})")
            return None

    def _get_page_name_from_search(self, search_title):
        if not search_title or not str(search_title).strip():
            return None
        self._t("HTTP wiki title search (variants)")
        search_title = str(search_title).strip()
        norm_query = normalize_for_match(search_title)
        if not norm_query:
            return None
        variants = [search_title]
        if " - " in search_title:
            variants.append(search_title.replace(" - ", ": ", 1))
        if ": " in search_title and search_title not in variants:
            variants.append(search_title.replace(": ", " - ", 1))
        seen_titles = set()
        all_hits = []
        try:
            for variant in variants[:2]:
                params = {
                    "action": "query",
                    "list": "search",
                    "srsearch": variant,
                    "format": "json",
                    "srlimit": 15,
                }
                response = self._session_get(self.BASE_URL, params=params, timeout=10)
                response.raise_for_status()
                data = response.json()
                for h in data.get("query", {}).get("search", []):
                    t = h.get("title")
                    if t and t not in seen_titles:
                        seen_titles.add(t)
                        all_hits.append(t)
        except (Exception, KeyError) as e:
            print(f"!! Wiki title search failed for '{search_title}': {e}")
            return None
        if not all_hits and (" " in search_title or ":" in search_title or "-" in search_title):
            short_query = re.sub(r"\s*(?:[-:])\s*", " ", search_title).strip()
            short_query = re.sub(r"\s+the\s+", " ", short_query, flags=re.IGNORECASE).strip()
            if short_query and short_query != search_title:
                try:
                    params = {
                        "action": "query",
                        "list": "search",
                        "srsearch": short_query,
                        "format": "json",
                        "srlimit": 15,
                    }
                    response = self._session_get(self.BASE_URL, params=params, timeout=10)
                    response.raise_for_status()
                    for h in response.json().get("query", {}).get("search", []):
                        t = h.get("title")
                        if t and t not in seen_titles:
                            seen_titles.add(t)
                            all_hits.append(t)
                except (Exception, KeyError):
                    pass
        if not all_hits:
            return None
        best_title = None
        best_ratio = 0.0
        for candidate in all_hits:
            norm_candidate = normalize_for_match(candidate)
            if not norm_candidate:
                continue
            ratio = difflib.SequenceMatcher(None, norm_query, norm_candidate).ratio()
            if norm_candidate == norm_query:
                ratio = max(ratio, 1.0)
            elif norm_candidate.startswith(norm_query) or norm_query.startswith(norm_candidate):
                ratio = max(ratio, 0.85)
            if ratio > best_ratio:
                best_ratio = ratio
                best_title = candidate
        if best_title and best_ratio >= 0.5:
            print(f"--> Wiki title search matched '{search_title}' -> page '{best_title}' (score={best_ratio:.2f})")
            self._t(f"title search MATCH {best_title!r} score={best_ratio:.2f} hits={len(all_hits)}")
            return best_title
        self._t("title search NO_MATCH (low score or empty)")
        return None

    def _normalize_section_line(self, line):
        if not line:
            return ""
        normalized = "".join(c if c.isalnum() or c.isspace() else " " for c in line.lower())
        return " ".join(normalized.split())

    def _fallback_parse_html_section(self, page_title):
        print(f"--> Attempting HTML parse fallback for '{page_title}'...")
        self._t("HTML fallback: parse sections + table")
        try:
            import html
            import traceback

            params_sections = {
                "action": "parse",
                "page": page_title,
                "prop": "sections",
                "format": "json",
                "redirects": "true",
            }
            response_sections = self._session_get(self.BASE_URL, params=params_sections, timeout=10)
            response_sections.raise_for_status()
            sections = response_sections.json().get("parse", {}).get("sections", [])
            target_normalized = "save game data location"
            save_section_index = None
            for s in sections:
                line = s.get("line", "")
                if self._normalize_section_line(line) == target_normalized:
                    save_section_index = s.get("index")
                    break
                if "save" in line.lower() and "location" in line.lower():
                    save_section_index = s.get("index")
                    break
            if not save_section_index:
                section_titles = [s.get("line", "(no title)") for s in sections]
                print(f"--> No 'Save game data location' section found for '{page_title}'")
                print(f"    [DEBUG] Available section titles on this page: {section_titles}")
                self._t("HTML fallback: no save section on page")
                return None

            print(f"--> Found 'Save game data location' as section {save_section_index}. Fetching its HTML...")
            self._t(f"HTML fallback: section idx={save_section_index}")
            params_html = {
                "action": "parse",
                "page": page_title,
                "prop": "text",
                "section": save_section_index,
                "format": "json",
                "redirects": "true",
            }
            response_html = self._session_get(self.BASE_URL, params=params_html, timeout=10)
            response_html.raise_for_status()
            section_html = response_html.json().get("parse", {}).get("text", {}).get("*", "")
            if not section_html:
                print(f"--> No HTML content found in section for '{page_title}'")
                print(f"    [DEBUG] Section index {save_section_index} returned empty HTML.")
                return None

            def _pick_first_file_path_line(cell_plain: str) -> Optional[str]:
                for line in [x.strip() for x in cell_plain.replace("\r", "").split("\n") if x.strip()]:
                    if looks_like_registry_hive_line(line):
                        continue
                    if wiki_cell_looks_like_file_path(line):
                        return path_to_directory_only(line)
                return None

            found_paths: dict = {}
            registry_hints: List[str] = []

            for platform in ["Steam", "Windows", "Registry"]:
                match = re.search(
                    rf"{platform}\s*<\/th>\s*<td.*?>(.*?)<\/td>",
                    section_html,
                    re.DOTALL | re.IGNORECASE,
                )
                if not match:
                    continue
                path_with_tags = match.group(1)
                plain_br = re.sub(r"<br\s*/?>", "\n", path_with_tags, flags=re.IGNORECASE)
                cell_plain = html.unescape(re.sub(r"<.*?>", "", plain_br)).strip()
                if platform.lower() == "registry":
                    registry_hints.extend(extract_registry_hints(cell_plain))
                    continue
                registry_hints.extend(extract_registry_hints(cell_plain))
                file_line = _pick_first_file_path_line(cell_plain)
                if file_line:
                    found_paths[platform.lower()] = file_line

            registry_hints = list(dict.fromkeys([h for h in registry_hints if h.strip()]))

            out: dict = {}
            if found_paths.get("steam"):
                out["steam"] = found_paths["steam"]
            if found_paths.get("windows"):
                out["windows"] = found_paths["windows"]
            if registry_hints:
                out["registry_hints"] = registry_hints

            if out:
                print(f"--> Fallback success! Found potential paths / registry hints: {out}")
                self._t(f"HTML fallback OK keys={list(out.keys())}")
            else:
                print(f"--> No paths found via HTML fallback for '{page_title}' (table has no Steam/Windows/Registry cells with paths).")
                print(f"    [DEBUG] Section HTML length: {len(section_html)} chars.")
                self._t("HTML fallback EMPTY (no usable cells)")
            return out if out else None
        except Exception as e:
            print(f"!! Unexpected error during HTML fallback for '{page_title}': {e}")
            import traceback

            print(f"    [DEBUG] Traceback: {traceback.format_exc()}")
            return None

    def _fetch_for_page_title(self, page_title):
        ask_query = f"[[{page_title}]]|?Has save game location|?Save game data location (Windows)=-|?Save game data location (Steam)=-"
        params = {"action": "ask", "query": ask_query, "format": "json"}
        try:
            self._t(f"HTTP ASK semantic page={page_title!r}")
            response = self._session_get(self.BASE_URL, params=params, timeout=10)
            response.raise_for_status()
            query_results = response.json().get("query", {}).get("results", {})
            if not query_results:
                print(
                    f"    [DEBUG] Semantic query returned no results for page '{page_title}' (page may not exist or has no properties)."
                )
                self._t("semantic: empty results (no row / no props)")
            if query_results:
                printouts = next(iter(query_results.values())).get("printouts", {})
                win_paths = printouts.get("Save game data location (Windows)", [])
                steam_paths = printouts.get("Save game data location (Steam)", [])
                if win_paths or steam_paths:
                    win_raw = str(win_paths[0]).strip() if win_paths else ""
                    steam_raw = str(steam_paths[0]).strip() if steam_paths else ""

                    def _pick_first_file_path_line_sem(cell: str) -> Optional[str]:
                        if not cell:
                            return None
                        for line in [x.strip() for x in cell.replace("\r", "").split("\n") if x.strip()]:
                            if looks_like_registry_hive_line(line):
                                continue
                            if wiki_cell_looks_like_file_path(line):
                                return path_to_directory_only(line)
                        return None

                    win_out = _pick_first_file_path_line_sem(win_raw)
                    steam_out = _pick_first_file_path_line_sem(steam_raw)
                    hints = extract_registry_hints(win_raw) + extract_registry_hints(steam_raw)
                    hints = list(dict.fromkeys([h for h in hints if h.strip()]))
                    out_sem: dict = {"windows": win_out, "steam": steam_out}
                    if hints:
                        out_sem["registry_hints"] = hints
                    if win_out or steam_out or hints:
                        print(f"--> Found semantic data for '{page_title}': {out_sem}")
                        self._t("semantic: paths and/or registry hints from wiki (good)")
                        return out_sem
            if query_results:
                printouts_debug = next(iter(query_results.values()), {}).get("printouts", {})
                print(f"    [DEBUG] Semantic printouts (no save paths): {list(printouts_debug.keys())}")
                self._t(f"semantic: row but NO save path props keys={list(printouts_debug.keys())[:6]}")
            print(f"--> Semantic data not found for '{page_title}'. Trying HTML fallback method...")
            return self._fallback_parse_html_section(page_title)
        except Exception as e:
            print(f"!! An unexpected error occurred while processing '{page_title}': {e}")
            self._t(f"semantic: ERROR {type(e).__name__}")
            return None

    def fetch_save_location(self, game_name, steam_app_id):
        page_title = self._get_page_name_from_app_id(steam_app_id) if steam_app_id else None
        if page_title:
            print(f"--> Fetching semantic data from wiki page: '{page_title}'...")
            self._t("branch: Steam appid → wiki title")
            result = self._fetch_for_page_title(page_title)
            if result:
                self._t("result: appid wiki page returned path strings")
                return result
            print(
                "--> No save paths from Steam app ID wiki title; trying game name, title search, and variants..."
            )
            self._t("branch: appid wiki miss → name / search / title variants")
        clean_name = normalize_wiki_page_title(game_name)
        if clean_name != game_name:
            print(f"--> Using clean page title: '{game_name}' -> '{clean_name}'")
            self._t(f"normalized wiki title {clean_name!r}")
        print(f"--> Fetching semantic data from wiki page: '{clean_name}'...")
        self._t("branch: game name → primary wiki title")
        result = self._fetch_for_page_title(clean_name)
        if result:
            self._t("result: wiki returned path strings (check disk later)")
            return result
        page_title = self._get_page_name_from_search(clean_name)
        if page_title and page_title != clean_name:
            print(f"--> Fetching semantic data from wiki page: '{page_title}'...")
            self._t(f"branch: title search → {page_title!r}")
            result = self._fetch_for_page_title(page_title)
            if result:
                self._t("result: alternate title had path strings")
                return result
        for variant in build_title_variants_for_fallback(clean_name):
            if variant == clean_name:
                continue
            print(f"--> Trying wiki title variant: '{variant}'...")
            self._t(f"branch: title variant {variant!r}")
            result = self._fetch_for_page_title(variant)
            if result:
                self._t("result: variant had path strings")
                return result
        self._t("result: exhausted wiki branches → None")
        return None


class BackupEstimateWorker(QThread):
    """Compute folder sizes for a dry-run estimate (runs off the UI thread)."""

    finished_ok = pyqtSignal(dict)
    failed = pyqtSignal(str)

    def __init__(self, games_to_backup, parent=None):
        super().__init__(parent)
        self.games = games_to_backup

    def run(self):
        from utils.backup_estimate import estimate_backup_batch

        try:
            self.finished_ok.emit(estimate_backup_batch(self.games))
        except Exception as e:
            self.failed.emit(str(e))


class BackupWorker(QThread):
    progress = pyqtSignal(int, str)
    finished = pyqtSignal(str)
    error = pyqtSignal(str)
    game_backed_up = pyqtSignal(str, str)

    def __init__(self, games_to_backup, destination_folder, subfolder_per_game=False, parent=None):
        super().__init__(parent)
        self.games = games_to_backup
        self.destination = destination_folder
        self.subfolder_per_game = subfolder_per_game

    def run(self):
        total_games = len(self.games)
        creationflags = getattr(subprocess, "CREATE_NO_WINDOW", 0)
        for i, game_info in enumerate(self.games):
            game_name = game_info.get("name")
            source_path = game_info.get("save_path_resolved")
            progress_percent = int(((i + 1) / total_games) * 100)
            self.progress.emit(progress_percent, f"Backing up: {game_name}...")
            timestamp = datetime.now().strftime("%Y-%m-%d_at_%H-%M-%S")
            sanitized_game_name = "".join(c for c in game_name if c.isalnum() or c in (" ", ".", "_")).rstrip()
            if self.subfolder_per_game:
                game_base = os.path.join(self.destination, sanitized_game_name)
                os.makedirs(game_base, exist_ok=True)
                backup_path = os.path.join(game_base, f"{sanitized_game_name} - Backup {timestamp}")
            else:
                backup_path = os.path.join(self.destination, f"{sanitized_game_name} - Backup {timestamp}")
            try:
                if game_info.get("save_in_registry_only"):
                    hk = game_info.get("save_registry_hive")
                    sk = game_info.get("save_registry_subkey")
                    if not hk or not sk:
                        self.error.emit(f"Missing registry key for {game_name}")
                        continue
                    key_full = f"{hk}\\{sk}"
                    os.makedirs(backup_path, exist_ok=True)
                    safe_slug = "".join(c if c.isalnum() or c in ("-", "_") else "_" for c in sanitized_game_name).strip("_")
                    reg_out = os.path.join(backup_path, f"{safe_slug or 'save'}_registry_export.reg")
                    r = subprocess.run(
                        ["reg", "export", key_full, reg_out, "/y"],
                        capture_output=True,
                        text=True,
                        creationflags=creationflags,
                    )
                    if r.returncode != 0:
                        err = (r.stderr or r.stdout or "reg export failed").strip()
                        self.error.emit(f"Registry export failed for {game_name}: {err}")
                        continue
                else:
                    if not source_path or not os.path.exists(source_path):
                        self.error.emit(f"No save folder to back up for {game_name}")
                        continue
                    shutil.copytree(source_path, backup_path, dirs_exist_ok=True)
                self.game_backed_up.emit(game_name, datetime.now().isoformat())
            except Exception as e:
                self.error.emit(f"Failed to back up {game_name}: {e}")
                continue
        self.finished.emit(f"Successfully backed up {total_games} game(s).")


class SaveFileEventHandler(FileSystemEventHandler if WATCHDOG_AVAILABLE else object):
    """Invokes ``on_auto_backup(game_name, save_path)`` when a file under ``save_path`` changes."""

    def __init__(self, game_name: str, save_path: str, on_auto_backup: Callable[[str, str], None]):
        if WATCHDOG_AVAILABLE:
            super().__init__()
        self.game_name = game_name
        self.save_path = save_path
        self._on_auto_backup = on_auto_backup

    def on_modified(self, event):
        if not WATCHDOG_AVAILABLE:
            return
        if event.is_directory:
            return
        self._on_auto_backup(self.game_name, self.save_path)


class AutoBackupWorker(QThread):
    backup_completed = pyqtSignal(str, bool, str)

    def __init__(self, game_name, save_path, backup_dest, retention_count, subfolder_per_game=False):
        super().__init__()
        self.game_name = game_name
        self.save_path = save_path
        self.backup_dest = backup_dest
        self.retention_count = retention_count
        self.subfolder_per_game = subfolder_per_game

    def run(self):
        try:
            sanitized_name = "".join(c for c in self.game_name if c.isalnum() or c in (" ", ".", "_")).rstrip()
            if self.subfolder_per_game:
                base_dir = os.path.join(self.backup_dest, sanitized_name)
                os.makedirs(base_dir, exist_ok=True)
            else:
                base_dir = self.backup_dest
            try:
                game_backups = sorted(
                    [
                        os.path.join(base_dir, d)
                        for d in os.listdir(base_dir)
                        if d.startswith(f"{sanitized_name} - Backup")
                    ],
                    key=os.path.getmtime,
                )
                while len(game_backups) >= self.retention_count:
                    oldest_backup = game_backups.pop(0)
                    shutil.rmtree(oldest_backup)
                    print(f"Deleted old backup: {oldest_backup}")
            except Exception as e:
                print(f"Error cleaning up old backups: {e}")
            timestamp = datetime.now().strftime("%Y-%m-%d_at_%H-%M-%S")
            backup_path = os.path.join(base_dir, f"{sanitized_name} - Backup {timestamp}")
            shutil.copytree(self.save_path, backup_path, dirs_exist_ok=True)
            self.backup_completed.emit(self.game_name, True, "Backup completed successfully")
        except Exception as e:
            self.backup_completed.emit(self.game_name, False, str(e))


def _collect_files_for_zip(backup_root: str) -> Tuple[List[Tuple[str, str]], int, int]:
    """Return (list of (full_path, arcname_with_forward_slashes), total_bytes, file_count)."""
    out: List[Tuple[str, str]] = []
    total_bytes = 0
    for root, dirs, files in os.walk(backup_root):
        dirs[:] = [d for d in dirs if d != "__pycache__"]
        for f in files:
            if f.endswith((".zip", ".7z")):
                continue
            path = os.path.join(root, f)
            rel = os.path.relpath(path, backup_root)
            arc = rel.replace(os.sep, "/")
            try:
                total_bytes += os.path.getsize(path)
            except OSError:
                pass
            out.append((path, arc))
    return out, total_bytes, len(out)


def _rough_7z_percent(zip_size: int, total_uncompressed: int) -> int:
    """Map growing archive size to 0–92% while 7-Zip runs (heuristic)."""
    if total_uncompressed <= 0:
        return 0
    lo = max(total_uncompressed * 0.02, 1024 * 1024)
    hi = max(total_uncompressed * 0.45, lo * 2)
    if zip_size <= 0:
        return 0
    if zip_size >= hi:
        return 92
    if zip_size <= lo:
        return max(1, min(20, int(20 * zip_size / lo)))
    return int(20 + 72 * (zip_size - lo) / (hi - lo))


def _human_bytes(num: int) -> str:
    n = float(max(0, num))
    if n >= 1024**3:
        return f"{n / (1024**3):.2f} GiB"
    if n >= 1024**2:
        return f"{n / (1024**2):.2f} MiB"
    if n >= 1024:
        return f"{n / 1024:.2f} KiB"
    return f"{int(n)} B"


def complete_compression_ui_fields(
    *,
    archive_basename: str,
    opts: CompressionOptions,
    seven_archive_format: Optional[str],
    zip_bytes: int,
    raw_bytes: int,
    wall_sec: float,
    files_total: int,
) -> dict:
    """UI/history fields attached to compression_metrics when phase == complete."""
    threads_token = "auto" if opts.seven_mmt <= 0 else str(opts.seven_mmt)
    if opts.engine == "zipfile":
        if opts.zip_compression == zipfile.ZIP_STORED:
            arch_type = "ZIP store (no compression)"
            level_disp = "0 (store)"
            thr_disp = "1 thread (built-in)"
        else:
            arch_type = "ZIP Deflate (built-in)"
            level_disp = f"level {opts.deflate_level}"
            thr_disp = "1 thread (built-in)"
        fmt_key = "zip"
    else:
        fmt = (seven_archive_format or "7z").lower()
        if fmt not in ("zip", "7z"):
            fmt = "7z"
        fmt_key = fmt
        if fmt == "7z":
            arch_type = ".7z — LZMA2 (7-Zip)"
        else:
            arch_type = ".zip — Deflate (7-Zip)"
        level_disp = f"-mx={opts.seven_mx}"
        thr_disp = f"-mmt={threads_token}"
    return {
        "archive_basename": archive_basename,
        "archive_format_key": fmt_key,
        "archive_type_display": arch_type,
        "level_display": level_disp,
        "threads_display": thr_disp,
        "archive_size_bytes": zip_bytes,
        "archive_size_human": _human_bytes(zip_bytes),
        "raw_size_human": _human_bytes(raw_bytes),
        "files_total_ui": files_total,
        "engine_kind": opts.engine,
    }


class CompressBackupWorker(QThread):
    progress = pyqtSignal(str)
    progress_percent = pyqtSignal(int)
    zip_created = pyqtSignal(str)
    finished = pyqtSignal(bool, str)
    compression_metrics = pyqtSignal(object)

    def __init__(self, backup_folder_path: str, options: Optional[CompressionOptions] = None, parent=None):
        super().__init__(parent)
        self.backup_folder_path = backup_folder_path
        self.options = options or CompressionOptions.default_zip_balanced()
        self._cancelled = False
        self._seven_proc: Optional[subprocess.Popen] = None

    def cancel(self):
        self._cancelled = True
        if self._seven_proc and self._seven_proc.poll() is None:
            try:
                self._seven_proc.terminate()
            except Exception:
                pass

    def _emit_progress_metrics(
        self,
        count: int,
        total: int,
        bytes_uncompressed: int,
        t0: float,
        last_emit: float,
    ) -> float:
        now = time.perf_counter()
        if now - last_emit < 0.4 and count != total:
            return last_emit
        elapsed = now - t0
        mib_s = (bytes_uncompressed / (1024 * 1024)) / elapsed if elapsed > 0 else 0.0
        self.compression_metrics.emit(
            {
                "phase": "compressing",
                "files_done": count,
                "total_files": total,
                "bytes_uncompressed": bytes_uncompressed,
                "elapsed_sec": round(elapsed, 3),
                "throughput_mib_s": round(mib_s, 2),
                "engine": self.options.summary_label,
            }
        )
        return now

    def _run_zipfile(self, zip_path: str, zip_name: str, file_entries: List[Tuple[str, str]], total: int) -> None:
        opts = self.options
        comp = opts.zip_compression
        kw = {"compression": comp}
        if comp == zipfile.ZIP_DEFLATED:
            kw["compresslevel"] = max(1, min(9, opts.deflate_level or 6))

        count = 0
        last_pct = -1
        bytes_uncompressed = 0
        t0 = time.perf_counter()
        last_metric_emit = t0

        with zipfile.ZipFile(zip_path, "w", **kw) as zf:
            self.zip_created.emit(zip_path)
            for path, arcname in file_entries:
                if self._cancelled:
                    break
                try:
                    sz = os.path.getsize(path)
                except OSError:
                    sz = 0
                zf.write(path, arcname)
                bytes_uncompressed += sz
                count += 1
                pct = min(100, int(100 * count / total)) if total else 0
                if pct != last_pct:
                    last_pct = pct
                    self.progress_percent.emit(pct)
                if count % 10 == 0 or count == total:
                    self.progress.emit(f"Compressing... ({count}/{total} files)")
                last_metric_emit = self._emit_progress_metrics(
                    count, total, bytes_uncompressed, t0, last_metric_emit
                )

        if self._cancelled:
            self.finished.emit(False, "Cancelled")
            return

        wall = time.perf_counter() - t0
        zip_size = os.path.getsize(zip_path) if os.path.isfile(zip_path) else 0
        ratio = (zip_size / bytes_uncompressed) if bytes_uncompressed else 0.0
        avg_mib = (bytes_uncompressed / (1024 * 1024)) / wall if wall > 0 else 0.0
        ui_extra = complete_compression_ui_fields(
            archive_basename=zip_name,
            opts=opts,
            seven_archive_format=None,
            zip_bytes=zip_size,
            raw_bytes=bytes_uncompressed,
            wall_sec=wall,
            files_total=total,
        )
        self.compression_metrics.emit(
            {
                "phase": "complete",
                "files_total": total,
                "bytes_uncompressed": bytes_uncompressed,
                "zip_bytes": zip_size,
                "wall_sec": round(wall, 3),
                "avg_throughput_mib_s": round(avg_mib, 2),
                "compression_ratio_pct": round(ratio * 100.0, 2),
                "engine": opts.summary_label,
                **ui_extra,
            }
        )
        self.progress_percent.emit(100)
        self.finished.emit(True, f"Created {zip_name}")

    def _run_7zip(self, archive_path: str, archive_name: str, file_entries: List[Tuple[str, str]], total_bytes: int) -> None:
        opts = self.options
        exe = opts.seven_zip_exe
        if not exe:
            self.finished.emit(False, "7-Zip executable not found.")
            return

        arch_fmt = getattr(opts, "seven_archive_format", "zip") or "zip"
        if arch_fmt not in ("zip", "7z"):
            arch_fmt = "7z"

        list_fd, list_path = tempfile.mkstemp(suffix=".txt", prefix="gsbt_7z_", text=True)
        try:
            with os.fdopen(list_fd, "w", encoding="utf-8", newline="\n") as lf:
                for _path, arc in file_entries:
                    lf.write(arc.replace("/", os.sep) + "\n")
        except Exception as e:
            try:
                os.unlink(list_path)
            except OSError:
                pass
            self.finished.emit(False, f"Failed to write 7-Zip file list: {e}")
            return

        out_abs = os.path.abspath(archive_path)
        list_abs = os.path.abspath(list_path)
        if arch_fmt == "7z":
            cmd = [exe, "a", "-t7z", "-m0=lzma2", f"-mx={opts.seven_mx}"]
            metric_note = (
                "archive bytes on disk (LZMA2 uses all cores; size lags wall time at high -mx — not MiB/s of source data)"
            )
        else:
            cmd = [exe, "a", "-tzip", f"-mx={opts.seven_mx}"]
            metric_note = (
                "archive bytes on disk (ZIP Deflate: slow at -mx 9, little MT with few/large files — not source MiB/s)"
            )
        if opts.seven_mmt <= 0:
            cmd.append("-mmt=on")
        else:
            cmd.append(f"-mmt={opts.seven_mmt}")
        cmd.extend(["-bso0", "-y", out_abs, f"@{list_abs}"])

        self.zip_created.emit(archive_path)
        t0 = time.perf_counter()
        last_metric_emit = t0
        last_ui_pct = -1

        try:
            self._seven_proc = subprocess.Popen(
                cmd,
                cwd=self.backup_folder_path,
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
                stdin=subprocess.DEVNULL,
            )
        except Exception as e:
            try:
                os.unlink(list_path)
            except OSError:
                pass
            self.finished.emit(False, f"Could not start 7-Zip: {e}")
            return

        try:
            while self._seven_proc.poll() is None:
                if self._cancelled:
                    self._seven_proc.terminate()
                    try:
                        self._seven_proc.wait(timeout=3)
                    except subprocess.TimeoutExpired:
                        self._seven_proc.kill()
                    self.finished.emit(False, "Cancelled")
                    return

                zsz = os.path.getsize(out_abs) if os.path.isfile(out_abs) else 0
                pct = _rough_7z_percent(zsz, total_bytes)
                if pct != last_ui_pct:
                    last_ui_pct = pct
                    self.progress_percent.emit(pct)
                now = time.perf_counter()
                if now - last_metric_emit >= 0.45:
                    elapsed = now - t0
                    mib_s = (zsz / (1024 * 1024)) / elapsed if elapsed > 0 else 0.0
                    self.compression_metrics.emit(
                        {
                            "phase": "compressing",
                            "files_done": -1,
                            "total_files": len(file_entries),
                            "bytes_uncompressed": zsz,
                            "elapsed_sec": round(elapsed, 3),
                            "throughput_mib_s": round(mib_s, 2),
                            "engine": opts.summary_label,
                            "note": metric_note,
                            "archive_format": arch_fmt,
                        }
                    )
                    last_metric_emit = now
                    self.progress.emit(f"7-Zip… {pct}% (~{zsz // (1024 * 1024)} MiB on disk)")
                time.sleep(0.25)

            rc = self._seven_proc.returncode
            if rc != 0:
                self.finished.emit(False, f"7-Zip failed with exit code {rc}.")
                return

            if self._cancelled:
                self.finished.emit(False, "Cancelled")
                return

            wall = time.perf_counter() - t0
            zip_size = os.path.getsize(out_abs) if os.path.isfile(out_abs) else 0
            ratio = (zip_size / total_bytes) if total_bytes else 0.0
            avg_mib = (total_bytes / (1024 * 1024)) / wall if wall > 0 else 0.0
            ui_extra = complete_compression_ui_fields(
                archive_basename=archive_name,
                opts=opts,
                seven_archive_format=arch_fmt,
                zip_bytes=zip_size,
                raw_bytes=total_bytes,
                wall_sec=wall,
                files_total=len(file_entries),
            )
            self.compression_metrics.emit(
                {
                    "phase": "complete",
                    "files_total": len(file_entries),
                    "bytes_uncompressed": total_bytes,
                    "zip_bytes": zip_size,
                    "wall_sec": round(wall, 3),
                    "avg_throughput_mib_s": round(avg_mib, 2),
                    "compression_ratio_pct": round(ratio * 100.0, 2),
                    "engine": opts.summary_label,
                    "archive_format": arch_fmt,
                    **ui_extra,
                }
            )
            self.progress_percent.emit(100)
            self.finished.emit(True, f"Created {archive_name} (7-Zip)")
        finally:
            self._seven_proc = None
            try:
                os.unlink(list_path)
            except OSError:
                pass

    def run(self):
        try:
            if not self.backup_folder_path or not os.path.isdir(self.backup_folder_path):
                self.finished.emit(False, "Backup folder not set or not found.")
                return

            file_entries, total_bytes, total = _collect_files_for_zip(self.backup_folder_path)
            if total == 0:
                self.progress.emit("No files to compress.")
                self.progress_percent.emit(100)
                self.finished.emit(True, "No files to compress.")
                return

            timestamp = datetime.now().strftime("%Y-%m-%d_%H-%M-%S")
            if self.options.engine == "7z":
                ext = self.options.seven_archive_format if self.options.seven_archive_format in ("zip", "7z") else "7z"
            else:
                ext = "zip"
            zip_name = f"Backups_{timestamp}.{ext}"
            zip_path = os.path.join(self.backup_folder_path, zip_name)
            self.progress.emit(f"Compressing ({self.options.summary_label}) → {zip_name}...")
            self.progress_percent.emit(0)

            if self.options.engine == "7z":
                self._run_7zip(zip_path, zip_name, file_entries, total_bytes)
            else:
                self._run_zipfile(zip_path, zip_name, file_entries, total)
        except Exception as e:
            self.finished.emit(False, str(e))
