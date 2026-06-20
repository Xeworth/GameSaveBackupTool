"""
Background workers: game detection, manifest save lookup, backup, compress, and optional watchdog handler.

Kept separate from ``main_window`` to keep the UI module focused on layout and signals.
"""

from __future__ import annotations

import math
import os
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
from core.ludusavi_manifest_provider import LudusaviManifestProvider
from core.scan_service import ScanService
from core.backup_compression_service import collect_relative_entries
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


class ManifestRefreshWorker(QThread):
    finished = pyqtSignal(str)
    error = pyqtSignal(str)

    def __init__(self, manifest_provider: LudusaviManifestProvider, parent=None):
        super().__init__(parent)
        self.manifest_provider = manifest_provider

    def run(self):
        try:
            status = self.manifest_provider.refresh_now()
            self.finished.emit(status)
        except Exception as exc:
            self.error.emit(str(exc))


class SaveLocationFetcherWorker(QThread):
    game_save_fetched = pyqtSignal(dict)
    save_fetch_progress = pyqtSignal(int)
    all_fetching_finished = pyqtSignal()
    games_dropped_from_dedup = pyqtSignal(list)
    error = pyqtSignal(str)
    save_fetch_metrics = pyqtSignal(object)
    save_fetch_trace = pyqtSignal(str)

    def __init__(
        self,
        games_to_fetch,
        steam_ids,
        manifest_provider: LudusaviManifestProvider,
        save_manager: SaveManager,
        *,
        deduplicate_shared_save_folders: bool = True,
        parent=None,
    ):
        super().__init__(parent)
        self.games_to_fetch = games_to_fetch
        self.steam_ids = steam_ids if steam_ids else {}
        self.manifest_provider = manifest_provider
        self.save_manager = save_manager
        self.deduplicate_shared_save_folders = deduplicate_shared_save_folders
        self.completed_count = 0
        self.total_count = len(games_to_fetch)
        self.is_cancelled = False

    def run(self):
        scan = ScanService(self.manifest_provider, self.save_manager)
        kept = scan.run_save_fetch_parallel(
            self.games_to_fetch,
            self.steam_ids,
            deduplicate_shared_save_folders=self.deduplicate_shared_save_folders,
            on_progress=lambda n: self.save_fetch_progress.emit(n),
            on_trace=lambda msg: self.save_fetch_trace.emit(msg),
            on_metrics=lambda m: self.save_fetch_metrics.emit(m),
            on_dropped_from_dedup=lambda names: self.games_dropped_from_dedup.emit(names),
            is_cancelled=lambda: self.is_cancelled,
        )
        for result in kept:
            self.game_save_fetched.emit(result)
        self.all_fetching_finished.emit()

    def cancel(self):
        self.is_cancelled = True


class BackupEstimateWorker(QThread):
    """Compute folder sizes for a dry-run estimate (runs off the UI thread)."""

    finished_ok = pyqtSignal(dict)
    failed = pyqtSignal(str)

    def __init__(self, games_to_backup, destination_folder, trusted_names=None, parent=None):
        super().__init__(parent)
        self.games = games_to_backup
        self.destination_folder = destination_folder
        self.trusted_names = set(trusted_names or [])

    def run(self):
        from core.backup_folder_size_estimator import compute_backup_estimate
        from utils.backup_estimate import estimate_backup_batch

        try:
            summary = compute_backup_estimate(
                self.games,
                self.destination_folder,
                trusted_large_save_paths=self.trusted_names,
            )
            legacy = estimate_backup_batch(self.games, self.destination_folder)
            legacy["summary"] = summary
            self.finished_ok.emit(legacy)
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


def _collect_files_for_zip(
    backup_root: str,
    *,
    subfolder_per_game: bool = True,
    sanitized_game_names: Optional[List[str]] = None,
) -> Tuple[List[Tuple[str, str]], int, int]:
    return collect_relative_entries(
        backup_root,
        subfolder_per_game=subfolder_per_game,
        sanitized_game_names=sanitized_game_names,
    )


def _seven_zip_ui_percent(zip_size: int, total_uncompressed: int, elapsed_sec: float, arch_fmt: str) -> int:
    """
    Progress while 7-Zip runs: blends archive growth on disk with elapsed time.

    In-flight percent is capped at **95**; the worker then emits **100** on success so the last
    tick before completion reads ~95% rather than the mid‑80s.
    """
    if total_uncompressed <= 0:
        blended = int(78 * (1.0 - math.exp(-elapsed_sec / 12.0)))
        return min(95, max(0, int(blended * 1.115 + 1.5)))

    lo = max(int(total_uncompressed * 0.012), 256 * 1024)
    hi_ratio = 0.32 if arch_fmt == "7z" else 0.55
    hi = max(int(total_uncompressed * hi_ratio), lo * 2)

    if zip_size <= 0:
        sz_pct = 0
    elif zip_size >= hi:
        sz_pct = 90
    elif zip_size <= lo:
        sz_pct = max(1, min(16, int(15 * zip_size / max(lo, 1))))
    else:
        sz_pct = int(16 + 76 * (zip_size - lo) / max(hi - lo, 1))

    total_mb = max(1e-9, total_uncompressed / (1024.0 * 1024.0))
    wall_guess = max(6.0, min(90.0, 4.5 + (total_mb**0.5) * 2.4 + total_mb * 0.05))
    est_sec = max(3.2, min(36.0, wall_guess * 0.42))
    ratio = 1.0 - math.exp(-elapsed_sec / est_sec)
    time_pct = int(94 * (ratio**0.48))

    blended = max(sz_pct, time_pct)
    # Map mid‑high raw blend to ~95 as the last in-flight tick (mx 6–8 was ending ~84–88%).
    scaled = int(blended * 1.115 + 1.5)
    return min(95, max(0, scaled))


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

    def __init__(
        self,
        backup_folder_path: str,
        options: Optional[CompressionOptions] = None,
        parent=None,
        *,
        detailed_7z_status: bool = False,
        subfolder_per_game: bool = True,
        sanitized_game_names: Optional[List[str]] = None,
    ):
        super().__init__(parent)
        self.backup_folder_path = backup_folder_path
        self.options = options or CompressionOptions.default_zip_balanced()
        self._cancelled = False
        self._seven_proc: Optional[subprocess.Popen] = None
        self._detailed_7z_status = detailed_7z_status
        self.subfolder_per_game = subfolder_per_game
        self.sanitized_game_names = sanitized_game_names

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
        last_ui_emit_wall = t0
        monotonic_floor = 0

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
                now = time.perf_counter()
                elapsed = now - t0
                combined = _seven_zip_ui_percent(zsz, total_bytes, elapsed, arch_fmt)
                combined = max(combined, monotonic_floor)
                monotonic_floor = combined
                if (
                    combined > last_ui_pct
                    or (combined == last_ui_pct and now - last_ui_emit_wall >= 0.22)
                ):
                    last_ui_pct = combined
                    last_ui_emit_wall = now
                    self.progress_percent.emit(combined)
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
                    pct_shown = max(last_ui_pct, 0)
                    if self._detailed_7z_status:
                        self.progress.emit(
                            f"7-Zip… {pct_shown}% (~{zsz // (1024 * 1024)} MiB on disk)"
                        )
                    else:
                        self.progress.emit(f"Compressing... {pct_shown}%")
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

            file_entries, total_bytes, total = _collect_files_for_zip(
                self.backup_folder_path,
                subfolder_per_game=self.subfolder_per_game,
                sanitized_game_names=self.sanitized_game_names,
            )
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
