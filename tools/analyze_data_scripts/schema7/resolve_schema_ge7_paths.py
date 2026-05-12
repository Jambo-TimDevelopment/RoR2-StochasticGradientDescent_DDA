#!/usr/bin/env python3
"""
Pick the latest schema-ge7 run (by YYYYMMDD_HHMMSS tag) and print paths for .bat drivers.

Tags are collected from:
- directories  posthog_exports/hypotheses_results_schema_ge7_<tag>
- files        **/ALL_events_schema_ge7_<tag>.jsonl  (recursive under posthog_exports)

The newest tag is max(tags) — lexicographic order matches time order for the stamp format.

Emits two lines:
  EVENTS_FILE=<path relative to cwd>
  OUT_DIR=<path relative to cwd>
"""

from __future__ import annotations

import argparse
import os
from pathlib import Path

DIR_PREFIX = "hypotheses_results_schema_ge7_"
FILE_PREFIX = "ALL_events_schema_ge7_"


def _collect_tags(exports_root: Path) -> set[str]:
    tags: set[str] = set()
    if not exports_root.is_dir():
        return tags

    for p in exports_root.iterdir():
        if p.is_dir() and p.name.startswith(DIR_PREFIX):
            tags.add(p.name[len(DIR_PREFIX) :])

    for p in exports_root.rglob(f"{FILE_PREFIX}*.jsonl"):
        if not p.is_file():
            continue
        name = p.name
        if not name.endswith(".jsonl"):
            continue
        stem = name[: -len(".jsonl")]
        if stem.startswith(FILE_PREFIX):
            tags.add(stem[len(FILE_PREFIX) :])
    return tags


def _find_events_file(exports_root: Path, tag: str) -> Path | None:
    needle = f"{FILE_PREFIX}{tag}.jsonl"
    found: list[Path] = []
    for p in exports_root.rglob(needle):
        if p.is_file() and p.name == needle:
            found.append(p)
    if not found:
        return None
    # Prefer file closest to exports root (e.g. top-level export over a deep copy)
    return min(found, key=lambda x: (len(x.relative_to(exports_root).parts), str(x)))


def resolve_paths(exports_root: Path) -> tuple[Path, Path]:
    exports_root = exports_root.resolve()
    tags = _collect_tags(exports_root)
    if not tags:
        raise SystemExit(
            f"[resolve_schema_ge7_paths] No {DIR_PREFIX}* dirs and no {FILE_PREFIX}*.jsonl under {exports_root}"
        )

    best_tag = max(tags)
    events = _find_events_file(exports_root, best_tag)
    if events is None:
        raise SystemExit(
            f"[resolve_schema_ge7_paths] Missing events JSONL for newest tag {best_tag!r}: "
            f"expected file named {FILE_PREFIX}{best_tag}.jsonl under {exports_root}"
        )

    out_dir = exports_root / f"{DIR_PREFIX}{best_tag}"
    return events, out_dir


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument(
        "--exports-root",
        default=os.path.join("tools", "export_data_scripts", "posthog_exports"),
        help="PostHog exports root (default: relative to current working directory).",
    )
    args = ap.parse_args()

    root = Path(args.exports_root)
    if not root.is_absolute():
        root = (Path.cwd() / root).resolve()

    events, out_dir = resolve_paths(root)
    cwd = Path.cwd().resolve()

    try:
        rel_ev = os.path.relpath(events, cwd)
    except ValueError:
        rel_ev = str(events)
    try:
        rel_out = os.path.relpath(out_dir, cwd)
    except ValueError:
        rel_out = str(out_dir)

    print(f"EVENTS_FILE={rel_ev}")
    print(f"OUT_DIR={rel_out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
