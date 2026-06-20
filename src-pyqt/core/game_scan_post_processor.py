"""
Post-scan merge: collapse rows that share the same on-disk save folder (DLC / franchise duplicates).
"""

from __future__ import annotations

import os
from typing import Any, Dict, List, Tuple


def _normalize_disk_save_key(row: Dict[str, Any]) -> str | None:
    resolved = row.get("save_path_resolved")
    if not resolved or row.get("save_in_registry_only"):
        return None
    try:
        return os.path.normpath(str(resolved)).rstrip("\\/").lower()
    except (OSError, ValueError):
        return None


def _is_steam(row: Dict[str, Any]) -> bool:
    return str(row.get("platform") or "").lower() == "steam"


def _franchise_title(row: Dict[str, Any]) -> str:
    name = str(row.get("name") or "")
    idx = name.find(":")
    return (name if idx < 0 else name[:idx]).strip()


def _paths_overlap(a: str, b: str) -> bool:
    try:
        fa = os.path.normpath(a).rstrip("\\/")
        fb = os.path.normpath(b).rstrip("\\/")
        if fa.lower() == fb.lower():
            return True
        sep = os.sep
        return (
            fa.lower().startswith(fb.lower() + sep)
            or fb.lower().startswith(fa.lower() + sep)
        )
    except (OSError, ValueError):
        return False


def _pick_preferred_same_save_root(group: List[Dict[str, Any]]) -> Dict[str, Any]:
    if len(group) == 1:
        return group[0]

    for candidate in sorted(group, key=lambda x: (len(str(x.get("name") or "")), str(x.get("name") or "").lower())):
        name = str(candidate.get("name") or "")
        if all(
            str(o.get("name") or "").lower() == name.lower()
            or str(o.get("name") or "").lower().startswith(name.lower() + ":")
            for o in group
        ):
            return candidate

    best: Dict[str, Any] | None = None
    best_id = float("inf")
    for row in group:
        app_id = str(row.get("app_id") or "").strip()
        try:
            gid = int(app_id)
        except ValueError:
            continue
        if gid < best_id:
            best_id = gid
            best = row
    if best is not None:
        return best

    return sorted(group, key=lambda x: str(x.get("name") or "").lower())[0]


def deduplicate_by_shared_save_root(
    results: List[Dict[str, Any]],
) -> Tuple[List[Dict[str, Any]], List[str]]:
    """Return (kept rows, dropped catalog names)."""
    n = len(results)
    if n == 0:
        return results, []

    parent = list(range(n))

    def find(i: int) -> int:
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i

    def union(a: int, b: int) -> None:
        ra, rb = find(a), find(b)
        if ra != rb:
            parent[rb] = ra

    for i in range(n):
        for j in range(i + 1, n):
            ki = _normalize_disk_save_key(results[i])
            kj = _normalize_disk_save_key(results[j])
            if ki and kj and ki == kj:
                union(i, j)
                continue
            if not ki or not kj:
                continue
            if not _is_steam(results[i]) or not _is_steam(results[j]):
                continue
            if _franchise_title(results[i]).lower() != _franchise_title(results[j]).lower():
                continue
            ra = results[i].get("save_path_resolved")
            rb = results[j].get("save_path_resolved")
            if ra and rb and _paths_overlap(str(ra), str(rb)):
                union(i, j)

    by_root: Dict[int, List[int]] = {}
    for i in range(n):
        root = find(i)
        by_root.setdefault(root, []).append(i)

    dropped: set[str] = set()
    winner_row_id_by_root: Dict[int, str] = {}
    for root, indices in by_root.items():
        members = [results[ix] for ix in indices]
        winner = _pick_preferred_same_save_root(members)
        winner_row_id = str(winner.get("row_id") or "")
        winner_row_id_by_root[root] = winner_row_id
        for ix in indices:
            row_id = str(results[ix].get("row_id") or "")
            if row_id.lower() != winner_row_id.lower():
                dropped.add(str(results[ix].get("name") or ""))

    kept: List[Dict[str, Any]] = []
    for i in range(n):
        root = find(i)
        row_id = str(results[i].get("row_id") or "")
        if row_id.lower() == winner_row_id_by_root[root].lower():
            kept.append(results[i])

    return kept, [name for name in dropped if name]
