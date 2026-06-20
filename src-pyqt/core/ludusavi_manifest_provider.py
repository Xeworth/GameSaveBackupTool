"""
Offline Ludusavi save-path manifest: bundled seed, user-data cache, optional online refresh.
"""

from __future__ import annotations

import json
import os
import re
import tempfile
import threading
import time
from dataclasses import dataclass
from enum import Enum
from typing import Dict, List, Optional

import requests
import yaml

from core.user_data_dir import get_pyqt_user_data_dir

MANIFEST_URL = "https://raw.githubusercontent.com/mtkennerly/ludusavi-manifest/master/data/manifest.yaml"
MANIFEST_FILENAME = "ludusavi-save-manifest.json"
META_FILENAME = "ludusavi-save-manifest.meta.json"
MAX_MANIFEST_DOWNLOAD_BYTES = 64 * 1024 * 1024
MANIFEST_HTTP_TIMEOUT_SEC = 180

_NAME_CLEAN = re.compile(r"[^a-z0-9]+", re.IGNORECASE)


class LudusaviMatchKind(str, Enum):
    NONE = "none"
    STEAM_ID = "steam_id"
    NAME_INDEX = "name_index"


@dataclass(frozen=True)
class LudusaviSaveLookup:
    paths: List[str]
    match_kind: LudusaviMatchKind


def normalize_manifest_game_name(name: str) -> str:
    if not name or not str(name).strip():
        return ""
    return _NAME_CLEAN.sub(" ", str(name).strip().lower()).strip()


def default_bundled_manifest_path() -> str:
    return os.path.join(os.path.dirname(os.path.dirname(__file__)), "data", MANIFEST_FILENAME)


def _atomic_write_text(path: str, text: str) -> None:
    directory = os.path.dirname(path)
    os.makedirs(directory, exist_ok=True)
    fd, tmp = tempfile.mkstemp(dir=directory, prefix=".tmp_", suffix=".json")
    try:
        with os.fdopen(fd, "w", encoding="utf-8") as handle:
            handle.write(text)
        os.replace(tmp, path)
    except Exception:
        try:
            os.unlink(tmp)
        except OSError:
            pass
        raise


def _translate_manifest_path(path: str) -> str:
    mapping = {
        "<home>": "~",
        "<winAppData>": "%APPDATA%",
        "<winLocalAppData>": "%LOCALAPPDATA%",
        "<winLocalAppDataLow>": "%USERPROFILE%\\AppData\\LocalLow",
        "<winDocuments>": "%USERPROFILE%\\Documents",
        "<winPublic>": "%PUBLIC%",
        "<winProgramData>": "%PROGRAMDATA%",
        "<winDir>": "%WINDIR%",
        "<root>": "%INSTALLATION_PATH%",
        "<base>": "%INSTALLATION_PATH%",
        "<storeUserId>": "<user-id>",
    }
    output = path
    for src, dst in mapping.items():
        output = re.sub(re.escape(src), dst, output, flags=re.IGNORECASE)
    return output.replace("/", "\\")


def _is_windows_save_entry(file_meta: dict) -> bool:
    tags = file_meta.get("tags")
    if not isinstance(tags, (list, tuple)):
        return False
    tag_strs = [str(t).strip().lower() for t in tags if t is not None]
    if "save" not in tag_strs:
        return False

    when_obj = file_meta.get("when")
    if when_obj is None:
        return True
    if isinstance(when_obj, list):
        for item in when_obj:
            if not isinstance(item, dict):
                continue
            os_val = item.get("os")
            if os_val is None:
                return True
            os_str = str(os_val).strip().lower()
            if not os_str or os_str == "windows":
                return True
        return False
    return True


def compile_yaml_to_compact_manifest(yaml_text: str) -> dict:
    root = yaml.safe_load(yaml_text)
    if not isinstance(root, dict):
        raise ValueError("manifest root must be a mapping")

    name_index: Dict[str, List[str]] = {}
    steam_index: Dict[str, List[str]] = {}
    aliases: Dict[str, str] = {}
    total_games = 0

    for game_name_raw, entry in root.items():
        game_name = str(game_name_raw or "")
        if not isinstance(entry, dict):
            continue
        total_games += 1

        alias = entry.get("alias")
        if isinstance(alias, str) and alias.strip():
            aliases[game_name] = alias.strip()
            continue

        files = entry.get("files")
        if not isinstance(files, dict):
            continue

        save_paths: List[str] = []
        for fp, fm in files.items():
            path = str(fp or "").strip()
            if not path:
                continue
            if not isinstance(fm, dict) or not _is_windows_save_entry(fm):
                continue
            translated = _translate_manifest_path(path)
            if not any(p.lower() == translated.lower() for p in save_paths):
                save_paths.append(translated)

        if not save_paths:
            continue

        norm_name = normalize_manifest_game_name(game_name)
        if norm_name:
            name_index[norm_name] = list(save_paths)

        steam = entry.get("steam")
        if isinstance(steam, dict):
            sid = str(steam.get("id") or "").strip()
            if sid and sid.isdigit():
                steam_index[sid] = list(save_paths)

    for from_name, to_name in aliases.items():
        src = normalize_manifest_game_name(from_name)
        dst = normalize_manifest_game_name(to_name)
        if dst in name_index:
            name_index[src] = list(name_index[dst])

    return {
        "version": 1,
        "generated_at_unix": int(time.time()),
        "source_url": MANIFEST_URL,
        "stats": {
            "games_total_in_yaml": total_games,
            "games_with_windows_save_paths": len(name_index),
            "steam_ids_indexed": len(steam_index),
        },
        "name_index": name_index,
        "steam_index": steam_index,
    }


class LudusaviManifestProvider:
    def __init__(
        self,
        data_dir: Optional[str] = None,
        bundled_manifest_path: Optional[str] = None,
        http_client: Optional[requests.Session] = None,
    ):
        self._data_dir = data_dir or get_pyqt_user_data_dir()
        self._manifest_path = os.path.join(self._data_dir, MANIFEST_FILENAME)
        self._meta_path = os.path.join(self._data_dir, META_FILENAME)
        self._bundled_manifest_path = bundled_manifest_path or default_bundled_manifest_path()
        self._http = http_client or requests.Session()
        self._lock = threading.Lock()
        self._cache: Optional[dict] = None

    def load_manifest_offline_only(self) -> dict:
        with self._lock:
            if self._cache is not None:
                return self._cache

            doc = self._load_manifest_document_from_disk() or self._seed_manifest_from_bundle()
            if doc is None:
                doc = self._create_empty_manifest()
            self._cache = doc
            return self._cache

    def refresh_now(self) -> str:
        headers: Dict[str, str] = {}
        meta = self._load_meta()
        etag = meta.get("etag", "")
        if etag:
            headers["If-None-Match"] = etag

        try:
            resp = self._http.get(
                MANIFEST_URL,
                headers=headers,
                timeout=MANIFEST_HTTP_TIMEOUT_SEC,
            )
        except requests.RequestException:
            return "network_error"

        if resp.status_code == 304:
            current = self._load_manifest_document_from_disk()
            if current is None:
                return "not_modified_without_cache"
            meta["fetched_at_unix"] = str(int(time.time()))
            self._save_meta(meta)
            with self._lock:
                self._cache = current
            return "not_modified"

        if not resp.ok:
            return f"http_{resp.status_code}"

        if len(resp.content) > MAX_MANIFEST_DOWNLOAD_BYTES:
            return "manifest_too_large"

        try:
            yaml_text = resp.text
        except requests.RequestException:
            return "network_error"

        try:
            compiled = compile_yaml_to_compact_manifest(yaml_text)
        except Exception:
            return "yaml_error"

        self._save_manifest(compiled)
        self._save_meta(
            {
                "fetched_at_unix": str(int(time.time())),
                "etag": resp.headers.get("ETag", ""),
            }
        )
        with self._lock:
            self._cache = compiled
        return "updated"

    def find_save_paths(
        self,
        game_name: str,
        steam_app_id: Optional[str] = None,
        strict_steam_indexing: bool = False,
    ) -> List[str]:
        return self.find_save_paths_with_meta(game_name, steam_app_id, strict_steam_indexing).paths

    def find_save_paths_with_meta(
        self,
        game_name: str,
        steam_app_id: Optional[str] = None,
        strict_steam_indexing: bool = False,
    ) -> LudusaviSaveLookup:
        manifest = self.load_manifest_offline_only()
        app_key = str(steam_app_id or "").strip()

        steam_index = manifest.get("steam_index") or {}
        if app_key and app_key in steam_index:
            steam_entry = steam_index[app_key]
            if isinstance(steam_entry, list):
                paths = [str(x).strip() for x in steam_entry if str(x).strip()]
                return LudusaviSaveLookup(paths, LudusaviMatchKind.STEAM_ID)
            if strict_steam_indexing:
                return LudusaviSaveLookup([], LudusaviMatchKind.STEAM_ID)

        norm = normalize_manifest_game_name(game_name)
        name_index = manifest.get("name_index") or {}
        name_paths = name_index.get(norm)
        if isinstance(name_paths, list):
            paths = [str(x).strip() for x in name_paths if str(x).strip()]
            return LudusaviSaveLookup(paths, LudusaviMatchKind.NAME_INDEX)

        return LudusaviSaveLookup([], LudusaviMatchKind.NONE)

    def _seed_manifest_from_bundle(self) -> Optional[dict]:
        path = self._bundled_manifest_path
        if not path or not os.path.isfile(path):
            return None
        try:
            with open(path, encoding="utf-8") as handle:
                raw = json.load(handle)
            self._save_manifest(raw)
            self._save_meta({"fetched_at_unix": str(int(time.time())), "etag": ""})
            return raw
        except (OSError, json.JSONDecodeError):
            return None

    def _load_manifest_document_from_disk(self) -> Optional[dict]:
        if not os.path.isfile(self._manifest_path):
            return None
        try:
            with open(self._manifest_path, encoding="utf-8") as handle:
                return json.load(handle)
        except (OSError, json.JSONDecodeError):
            return None

    @staticmethod
    def _create_empty_manifest() -> dict:
        return {
            "version": 1,
            "generated_at_unix": 0,
            "source_url": MANIFEST_URL,
            "stats": {},
            "name_index": {},
            "steam_index": {},
        }

    def _load_meta(self) -> Dict[str, str]:
        if not os.path.isfile(self._meta_path):
            return {}
        try:
            with open(self._meta_path, encoding="utf-8") as handle:
                data = json.load(handle)
            return {str(k): str(v) for k, v in data.items()} if isinstance(data, dict) else {}
        except (OSError, json.JSONDecodeError):
            return {}

    def _save_meta(self, meta: Dict[str, str]) -> None:
        _atomic_write_text(self._meta_path, json.dumps(meta, indent=2))

    def _save_manifest(self, manifest: dict) -> None:
        _atomic_write_text(self._manifest_path, json.dumps(manifest, indent=2))
