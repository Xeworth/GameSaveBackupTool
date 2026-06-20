"""Parallel Ludusavi manifest save lookup (mirrors C# ScanService)."""

from __future__ import annotations

import concurrent.futures
import os
import threading
import time
from datetime import datetime
from typing import Callable, Dict, List, Optional

from core.game_scan_post_processor import deduplicate_by_shared_save_root
from core.ludusavi_manifest_provider import LudusaviManifestProvider, LudusaviMatchKind
from core.registry_save_resolver import (
    extract_registry_hints,
    format_registry_save_display,
    merged_steam_registry_save_keys,
    resolve_registry_hint_to_save_folder,
    try_registry_key_as_in_key_save_location,
)
from core.save_manager import SaveManager


def scan_row_id(game: dict) -> str:
    return f"{game.get('name', '')}\x1e{game.get('app_id') or ''}\x1e{(game.get('install_path') or '').lower()}"


def is_user_added_catalog_entry(existing_row: dict) -> bool:
    if existing_row.get("is_custom_game"):
        return True
    if "steam_app_id" in existing_row:
        return False
    outcome = str(existing_row.get("scan_outcome") or "")
    path = str(existing_row.get("save_path") or "")
    return outcome.upper() == "SAVE_ON_DISK" and bool(path.strip())


def build_catalog_payload(result: dict) -> dict:
    payload = {
        "steam_app_id": result.get("app_id"),
        "scan_outcome": result.get("scan_outcome"),
    }
    platform = result.get("platform")
    if platform:
        payload["platform"] = platform

    source = str(result.get("source") or "")
    resolved_via_registry = source.lower() == "ludusavi (registry)"
    raw_path = result.get("save_path_raw")

    if raw_path:
        payload["save_path"] = raw_path
        if resolved_via_registry:
            payload["save_resolved_via_registry"] = True
    elif result.get("save_in_registry_only") and result.get("save_registry_hive") and result.get("save_registry_subkey"):
        payload["save_path"] = ""
        payload["save_registry_hive"] = result["save_registry_hive"]
        payload["save_registry_subkey"] = result["save_registry_subkey"]
        payload["save_in_registry_only"] = True
    else:
        payload["save_path"] = ""
    return payload


class ScanService:
    def __init__(
        self,
        manifest_provider: LudusaviManifestProvider,
        save_manager: Optional[SaveManager] = None,
    ):
        self.manifest_provider = manifest_provider
        self.save_manager = save_manager or SaveManager()

    def process_single_game(
        self,
        game: dict,
        steam_ids: dict,
        trace: Optional[Callable[[str], None]] = None,
    ) -> dict:
        t0 = time.perf_counter()
        game_name = game.get("name")
        app_id = game.get("app_id")
        install_path = game.get("install_path")
        platform = game.get("platform", "Unknown")
        game_short = (game_name or "?")[:42]

        def tr(msg: str) -> None:
            if trace:
                off = time.perf_counter() - t0
                ts = datetime.now().strftime("%H:%M:%S.%f")[:-3]
                trace(f"{ts} +{off:7.3f}s | {game_short} | {msg}")

        tr(f"BEGIN manifest lookup app_id={app_id!r}")

        strict_steam_indexing = str(platform).lower() == "steam" and bool(str(app_id or "").strip())
        manifest_lookup = self.manifest_provider.find_save_paths_with_meta(
            str(game_name or ""),
            str(app_id) if app_id else None,
            strict_steam_indexing,
        )
        candidates = manifest_lookup.paths
        suppress_manifest_hints_from_name_only_match = (
            strict_steam_indexing
            and manifest_lookup.match_kind == LudusaviMatchKind.NAME_INDEX
            and bool(str(app_id or "").strip())
        )

        final_raw_path: Optional[str] = None
        source_type = "Not Found"
        resolved_via_registry = False
        registry_only = False
        reg_hive: Optional[str] = None
        reg_sub: Optional[str] = None
        hint_list: List[str] = []

        for candidate in candidates:
            if "<user-id>" in candidate.lower():
                for key in ("steamid64", "steamid3"):
                    test_path = candidate.replace("<user-id>", steam_ids.get(key, ""))
                    resolved = self.save_manager.resolve_path(test_path, install_path)
                    if resolved and os.path.isdir(resolved):
                        final_raw_path = test_path
                        break
            else:
                resolved = self.save_manager.resolve_path(candidate, install_path)
                if resolved and os.path.isdir(resolved):
                    final_raw_path = candidate
                    break

            if not suppress_manifest_hints_from_name_only_match:
                hint_list.extend(extract_registry_hints(candidate))

            if final_raw_path:
                break

        aid = str(app_id).strip() if app_id else ""
        steam_reg_pair = merged_steam_registry_save_keys().get(aid)
        if steam_reg_pair:
            hn, sk = steam_reg_pair
            hint_full = f"{hn}\\{sk}"
            if hint_full not in hint_list:
                hint_list.insert(0, hint_full)

        hint_list = list(dict.fromkeys(x.strip() for x in hint_list if x.strip()))

        if not final_raw_path and hint_list:
            for h in hint_list:
                folder = resolve_registry_hint_to_save_folder(h)
                if folder:
                    resolved_reg = self.save_manager.resolve_path(folder, install_path)
                    if resolved_reg and os.path.isdir(resolved_reg):
                        final_raw_path = folder
                        resolved_via_registry = True
                        source_type = "Ludusavi (registry)"
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
            source_type = "Ludusavi"

        spr = self.save_manager.resolve_path(final_raw_path, install_path) if final_raw_path else None
        sld = spr or (
            format_registry_save_display(reg_hive, reg_sub) if registry_only and reg_hive and reg_sub else None
        )

        if final_raw_path:
            scan_outcome = "SAVE_ON_DISK"
        elif registry_only:
            scan_outcome = "REGISTRY_IN_KEY"
        elif candidates:
            scan_outcome = "MANIFEST_PATHS_NO_DISK"
        else:
            scan_outcome = "NO_MANIFEST_PATHS"

        wall = time.perf_counter() - t0
        tr(f"END {scan_outcome} | {wall:.3f}s")

        return {
            "row_id": scan_row_id(game),
            "name": game_name,
            "app_id": app_id,
            "install_path": install_path,
            "platform": platform,
            "save_path_raw": final_raw_path,
            "save_path_resolved": spr,
            "save_location_display": sld,
            "save_in_registry_only": registry_only,
            "save_registry_hive": reg_hive,
            "save_registry_subkey": reg_sub,
            "source": source_type,
            "scan_outcome": scan_outcome,
            "_metrics": {
                "game": game_name,
                "wall_sec": round(wall, 3),
                "found": bool(final_raw_path or registry_only),
                "source": source_type,
                "http_calls": 0,
                "scan_outcome": scan_outcome,
                "wiki_outcome": scan_outcome,
            },
        }

    def persist_catalog_for_scan_result(self, result: dict) -> None:
        game_name = str(result.get("name") or "")
        existing = self.save_manager.get_save_location(game_name)
        if existing and is_user_added_catalog_entry(existing):
            return
        payload = build_catalog_payload(result)
        if existing and existing.get("last_backup"):
            payload["last_backup"] = existing["last_backup"]
        self.save_manager.add_or_update_save_location(game_name, payload)

    def run_save_fetch_parallel(
        self,
        games: List[dict],
        steam_ids: dict,
        *,
        deduplicate_shared_save_folders: bool = True,
        max_workers: int = 6,
        on_progress: Optional[Callable[[int], None]] = None,
        on_trace: Optional[Callable[[str], None]] = None,
        on_metrics: Optional[Callable[[dict], None]] = None,
        on_dropped_from_dedup: Optional[Callable[[List[str]], None]] = None,
        is_cancelled: Optional[Callable[[], bool]] = None,
    ) -> List[dict]:
        deduped: Dict[str, dict] = {}
        for game in games:
            rid = scan_row_id(game)
            if rid not in deduped:
                deduped[rid] = game
        game_list = list(deduped.values())
        workers = min(max_workers, max(1, len(game_list)))
        results: List[dict] = []
        counter_lock = threading.Lock()
        completed = 0

        def _trace(msg: str) -> None:
            if on_trace:
                on_trace(msg)

        with concurrent.futures.ThreadPoolExecutor(max_workers=workers) as executor:
            futures = {
                executor.submit(self.process_single_game, g, steam_ids, _trace): g for g in game_list
            }
            for future in concurrent.futures.as_completed(futures):
                if is_cancelled and is_cancelled():
                    break
                try:
                    result = future.result()
                    if result:
                        metrics = result.pop("_metrics", None)
                        if metrics and on_metrics:
                            on_metrics(metrics)
                        results.append(result)
                except Exception as exc:
                    print(f"Error processing game: {exc}")
                with counter_lock:
                    completed += 1
                    if on_progress:
                        on_progress(completed)

        if is_cancelled and is_cancelled():
            return []

        if deduplicate_shared_save_folders:
            kept, dropped_names = deduplicate_by_shared_save_root(results)
            if dropped_names:
                self.save_manager.delete_games(dropped_names)
                if on_dropped_from_dedup:
                    on_dropped_from_dedup(dropped_names)
        else:
            kept = sorted(results, key=lambda row: str(row.get("name") or "").lower())

        for result in kept:
            self.persist_catalog_for_scan_result(result)
        return kept
