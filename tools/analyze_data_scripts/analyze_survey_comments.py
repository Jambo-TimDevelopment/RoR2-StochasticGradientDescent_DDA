"""Extract user prose from survey_comment in PostHog JSONL exports."""
from __future__ import annotations

import argparse
import collections
import glob
import json
import os
import re
import statistics


def extract_user_prose(sc: str) -> tuple[str | None, str | None]:
    if not sc or not isinstance(sc, str):
        return None, None
    s = sc.strip()
    trig = None
    m = re.search(r"ui_trigger=([^\s|;]+)", s)
    if m:
        trig = m.group(1)
    if "|" in s:
        left, right = s.split("|", 1)
        left = left.strip()
        if "ui_trigger" in right:
            if left and not left.lower().startswith("ui_trigger"):
                return left, trig
            return None, trig
    if re.fullmatch(r"ui_trigger=\S+", s.strip()):
        return None, trig
    s2 = re.sub(r"\s*\|\s*ui_trigger=\S+\s*$", "", s).strip()
    s2 = re.sub(r"\s+ui_trigger=\S+\s*$", "", s2).strip()
    if s2 and not re.fullmatch(r"ui_trigger=\S+", s2):
        return s2, trig
    return None, trig


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument(
        "inputs",
        nargs="+",
        help="JSONL paths or glob under posthog_exports",
    )
    args = ap.parse_args()
    paths: list[str] = []
    for inp in args.inputs:
        paths.extend(glob.glob(inp) or ([inp] if os.path.isfile(inp) else []))

    machine_only: collections.Counter[str] = collections.Counter()
    with_prose: collections.Counter[str] = collections.Counter()
    prose_by_mode: collections.Counter[str] = collections.Counter()
    prose_lens: list[int] = []
    survey_rows: list[tuple[str, str, str | None, str]] = []

    for fp in paths:
        with open(fp, encoding="utf-8", errors="replace") as f:
            for line in f:
                try:
                    o = json.loads(line)
                except json.JSONDecodeError:
                    continue
                e = o.get("event") or ""
                if e not in (
                    "dda_post_session_survey",
                    "dda_post_session_survey_skipped",
                    "dda_session_end",
                ):
                    continue
                p = o.get("properties") or {}
                sc = p.get("survey_comment")
                if not sc or not str(sc).strip():
                    continue
                prose, trig = extract_user_prose(str(sc))
                mode = str(p.get("dda_mode") or "?")
                if prose and prose.strip():
                    lp = prose.strip()
                    with_prose[e] += 1
                    prose_by_mode[mode] += 1
                    prose_lens.append(len(lp))
                    survey_rows.append((e, mode, trig, lp))
                else:
                    machine_only[trig or "(no_trigger)"] += 1

    total_mc = sum(machine_only.values())
    total_pr = sum(with_prose.values())
    print(f"files={len(paths)} survey_comment_non_empty={total_mc + total_pr}")
    print(f"machine_only_ui_trigger={total_mc} {dict(machine_only.most_common(12))}")
    print(f"with_user_prose={total_pr} by_event={dict(with_prose)}")
    print(f"prose_by_dda_mode={dict(prose_by_mode)}")
    if prose_lens:
        print(
            f"prose_len n={len(prose_lens)} median={statistics.median(prose_lens):.0f} "
            f"mean={statistics.mean(prose_lens):.1f}"
        )

    # Unique prose by (normalized text, mode): same string is often sent on both
    # dda_session_end and dda_post_session_survey for one session.
    by_key: dict[tuple[str, str], dict[str, object]] = {}
    for e, mode, trig, text in survey_rows:
        key = (text.lower(), mode)
        rec = by_key.setdefault(
            key,
            {"text": text, "mode": mode, "triggers": set(), "events": collections.Counter()},
        )
        if trig:
            rec["triggers"].add(trig)
        rec["events"][e] += 1

    print("\n--- Unique user comments (dedup by text+mode; all events) ---")
    for i, rec in enumerate(
        sorted(by_key.values(), key=lambda r: -len(str(r["text"]))),
        1,
    ):
        text = str(rec["text"])
        mode = str(rec["mode"])
        triggers = sorted(rec["triggers"])  # type: ignore[arg-type]
        ev: collections.Counter[str] = rec["events"]  # type: ignore[assignment]
        trig_s = ", ".join(triggers) if triggers else "?"
        ev_s = ", ".join(f"{k}={v}" for k, v in sorted(ev.items()))
        print(f"{i}. [{mode}] ui_trigger: {trig_s}; counts: {ev_s}")
        print(f"   {text[:800]}{'…' if len(text) > 800 else ''}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
