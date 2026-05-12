"""
Session-level survey aggregates for hypotheses H5 and H6 from PostHog JSONL exports.

Source events (mod TelemetrySampleBuilder):
- dda_post_session_survey — primary row; props fairness_likert_1_7, continuity_likert_1_7 (1..7), survey_comment
- dda_session_end — copies survey fields when survey was submitted; used only if no post_session_survey row for session

H5 (fairness): H1 claims Fairness_SGD > Fairness_FLS and Fairness_SGD > Fairness_GA (higher Likert better).
H6 (continuity): same pattern for continuity_likert_1_7.

Statistical notes:
- Mann–Whitney U (one-sided, SGD > other) on Likert scores when scipy is available;
- Otherwise reports descriptive stats + bootstrap CI for mean(SGD) - mean(other) (same spirit as analyze_hypotheses_h1_h4.py).
"""

from __future__ import annotations

import argparse
import csv
import glob
import json
import math
import os
import random
import statistics
from dataclasses import dataclass, field
from datetime import datetime


def _iter_jsonl(path: str):
    with open(path, "r", encoding="utf-8", errors="replace") as f:
        for line_no, line in enumerate(f, start=1):
            line = line.strip()
            if not line:
                continue
            try:
                yield line_no, json.loads(line)
            except json.JSONDecodeError:
                yield line_no, None


def _to_int(v) -> int | None:
    try:
        if v is None or v == "":
            return None
        return int(float(v))
    except Exception:
        return None


def _parse_ts(s: str | None) -> datetime | None:
    if not s:
        return None
    try:
        return datetime.fromisoformat(str(s).replace("Z", "+00:00"))
    except Exception:
        return None


def _valid_likert(v: int | None) -> bool:
    return v is not None and 1 <= v <= 7


def _bootstrap_diff(
    a: list[float],
    b: list[float],
    *,
    iters: int = 20_000,
    alpha: float = 0.05,
    seed: int = 42,
) -> tuple[float | None, float | None, float | None]:
    if not a or not b:
        return None, None, None
    rng = random.Random(seed)

    def sample_mean(xs: list[float]) -> float:
        return statistics.fmean(rng.choice(xs) for _ in range(len(xs)))

    diffs = [sample_mean(a) - sample_mean(b) for _ in range(iters)]
    diffs.sort()
    mean_diff = statistics.fmean(a) - statistics.fmean(b)
    lo = diffs[int((alpha / 2) * iters)]
    hi = diffs[int((1 - alpha / 2) * iters) - 1]
    return mean_diff, lo, hi


def _mann_whitney_greater(
    x: list[float], y: list[float]
) -> tuple[float | None, float | None]:
    """
    One-sided MWU: test if x is stochastically greater than y (higher values = better for Likert).
    Returns (U statistic, asymptotic p-value one-sided) or (None, None).
    """
    try:
        from scipy.stats import mannwhitneyu
    except ImportError:
        return None, None
    if len(x) < 1 or len(y) < 1:
        return None, None
    res = mannwhitneyu(x, y, alternative="greater", use_continuity=True)
    return float(res.statistic), float(res.pvalue)


@dataclass
class SurveyRow:
    session_id: str
    dda_mode: str
    participant_id: str = ""
    source_event: str = ""
    event_timestamp: str = ""
    fairness_likert_1_7: int = 0
    continuity_likert_1_7: int = 0
    survey_comment: str = ""
    telemetry_schema_version: int | None = None


@dataclass
class SessionSurveyAccum:
    post: SurveyRow | None = None
    end: SurveyRow | None = None


def _row_from_props(
    *,
    event: str,
    obj: dict,
    props: dict,
) -> SurveyRow | None:
    sid = props.get("session_id")
    if not sid:
        return None
    f = _to_int(props.get("fairness_likert_1_7"))
    c = _to_int(props.get("continuity_likert_1_7"))
    if not _valid_likert(f) or not _valid_likert(c):
        return None
    mode = str(props.get("dda_mode") or "").strip()
    pid = str(props.get("participant_id") or props.get("distinct_id") or "").strip()
    ts = obj.get("timestamp") or ""
    comment = str(props.get("survey_comment") or "")
    sch = _to_int(props.get("telemetry_schema_version"))
    return SurveyRow(
        session_id=str(sid),
        dda_mode=mode,
        participant_id=pid,
        source_event=event,
        event_timestamp=str(ts),
        fairness_likert_1_7=int(f),
        continuity_likert_1_7=int(c),
        survey_comment=comment,
        telemetry_schema_version=sch,
    )


def _pick_session_row(acc: SessionSurveyAccum) -> SurveyRow | None:
    if acc.post is not None:
        return acc.post
    return acc.end


def main() -> int:
    ap = argparse.ArgumentParser(
        description="Extract H5/H6 Likert survey rows from PostHog JSONL and summarize by dda_mode."
    )
    ap.add_argument(
        "inputs",
        nargs="+",
        help="JSONL files or globs (e.g. posthog_exports/ALL_events_*.jsonl)",
    )
    ap.add_argument(
        "--out-dir",
        default=os.path.join("tools", "export_data_scripts", "posthog_exports", "hypotheses_results"),
        help="Directory for CSV and summary markdown.",
    )
    ap.add_argument("--bootstrap-iters", type=int, default=20_000)
    ap.add_argument("--seed", type=int, default=42)
    args = ap.parse_args()

    paths: list[str] = []
    for inp in args.inputs:
        paths.extend(glob.glob(inp) or ([inp] if os.path.isfile(inp) else []))
    paths = [os.path.normpath(p) for p in paths]

    by_session: dict[str, SessionSurveyAccum] = {}
    skipped_count = 0
    bad_lines = 0
    seen_events = 0

    for path in paths:
        if not os.path.exists(path):
            print(f"[missing] {path}")
            continue
        for _ln, obj in _iter_jsonl(path):
            if obj is None:
                bad_lines += 1
                continue
            seen_events += 1
            event = str(obj.get("event") or "")
            props = obj.get("properties") or {}
            if event == "dda_post_session_survey_skipped":
                skipped_count += 1
                continue
            if event not in ("dda_post_session_survey", "dda_session_end"):
                continue
            row = _row_from_props(event=event, obj=obj, props=props)
            if row is None:
                continue
            acc = by_session.setdefault(row.session_id, SessionSurveyAccum())
            if event == "dda_post_session_survey":
                # Prefer latest survey submit if duplicates (rare)
                prev = acc.post
                if prev is None:
                    acc.post = row
                else:
                    t0 = _parse_ts(prev.event_timestamp)
                    t1 = _parse_ts(row.event_timestamp)
                    if t1 and t0 and t1 >= t0:
                        acc.post = row
                    elif t0 is None:
                        acc.post = row
            else:
                prev = acc.end
                if prev is None:
                    acc.end = row
                else:
                    t0 = _parse_ts(prev.event_timestamp)
                    t1 = _parse_ts(row.event_timestamp)
                    if t1 and t0 and t1 >= t0:
                        acc.end = row
                    elif t0 is None:
                        acc.end = row

    rows: list[SurveyRow] = []
    for acc in by_session.values():
        r = _pick_session_row(acc)
        if r is not None:
            rows.append(r)

    # Filter known modes
    rows = [r for r in rows if r.dda_mode in {"FLS", "GA", "SGD"}]

    out_dir = os.path.normpath(args.out_dir)
    os.makedirs(out_dir, exist_ok=True)
    csv_path = os.path.join(out_dir, "session_survey_h5_h6.csv")
    with open(csv_path, "w", newline="", encoding="utf-8") as w:
        wr = csv.writer(w)
        wr.writerow(
            [
                "session_id",
                "dda_mode",
                "participant_id",
                "source_event",
                "event_timestamp",
                "fairness_likert_1_7",
                "continuity_likert_1_7",
                "survey_comment",
                "telemetry_schema_version",
            ]
        )
        for r in sorted(rows, key=lambda x: (x.dda_mode, x.session_id)):
            wr.writerow(
                [
                    r.session_id,
                    r.dda_mode,
                    r.participant_id,
                    r.source_event,
                    r.event_timestamp,
                    r.fairness_likert_1_7,
                    r.continuity_likert_1_7,
                    r.survey_comment.replace("\r\n", " ").replace("\n", " ")[:2000],
                    r.telemetry_schema_version if r.telemetry_schema_version is not None else "",
                ]
            )

    by_mode: dict[str, list[SurveyRow]] = {m: [] for m in ("FLS", "GA", "SGD")}
    for row in rows:
        by_mode.setdefault(row.dda_mode, []).append(row)

    def vals(mode: str, attr: str) -> list[float]:
        return [float(getattr(r, attr)) for r in by_mode.get(mode, [])]

    def stat_block(mode: str, attr: str) -> str:
        xs = vals(mode, attr)
        if not xs:
            return "n=0"
        sd = statistics.pstdev(xs) if len(xs) > 1 else 0.0
        return (
            f"n={len(xs)}, mean={statistics.fmean(xs):.3f}, median={statistics.median(xs):.3f}, "
            f"stdev={sd:.3f}"
        )

    def fmt_contrast(attr: str, other: str) -> str:
        sgd = vals("SGD", attr)
        oth = vals(other, attr)
        md, lo, hi = _bootstrap_diff(
            sgd, oth, iters=args.bootstrap_iters, seed=args.seed
        )
        mw_u, mw_p = _mann_whitney_greater(sgd, oth)
        parts = []
        if md is not None and lo is not None and hi is not None:
            parts.append(f"bootstrap mean(SGD)−mean({other})={md:.3f}, 95% CI [{lo:.3f}, {hi:.3f}]")
        if mw_u is not None and mw_p is not None:
            parts.append(f"MWU one-sided (SGD>{other}) p={mw_p:.4f}")
        if not parts:
            return f"недостаточно данных для SGD vs {other}"
        verdict = "различие неустойчиво (CI пересекает 0)"
        if md is not None and lo is not None and hi is not None:
            if lo > 0:
                verdict = "SGD выше по среднему"
            elif hi < 0:
                verdict = "SGD ниже по среднему"
        parts.append(f"→ {verdict}")
        return "; ".join(parts)

    n_post = sum(1 for r in rows if r.source_event == "dda_post_session_survey")
    n_end_only = sum(1 for r in rows if r.source_event == "dda_session_end")

    md_path = os.path.join(out_dir, "summary_h5_h6.md")
    with open(md_path, "w", encoding="utf-8") as f:
        f.write("# H5 и H6: пост-сессионный опрос (Likert 1–7)\n\n")
        f.write("Источник: экспорт PostHog JSONL. Одна строка на `session_id`: приоритет события `dda_post_session_survey`, иначе поля из `dda_session_end`.\n\n")
        f.write(f"- разобрано событий (строк JSONL): {seen_events}\n")
        f.write(f"- битых строк: {bad_lines}\n")
        f.write(f"- сырое число `dda_post_session_survey_skipped` в файлах: {skipped_count}\n")
        f.write(f"- уникальных сессий с валидной парой шкал: **{len(rows)}**\n")
        f.write(f"  - из них чисто `dda_post_session_survey`: {n_post}, только `dda_session_end`: {n_end_only}\n\n")

        f.write("## Описательная статистика по режимам\n\n")
        for m in ("FLS", "GA", "SGD"):
            f.write(f"### {m}\n\n")
            f.write(f"- **H5** (fairness): {stat_block(m, 'fairness_likert_1_7')}\n")
            f.write(f"- **H6** (continuity): {stat_block(m, 'continuity_likert_1_7')}\n\n")

        f.write("## Сравнение с SGD (H1: SGD выше обоих baseline)\n\n")
        f.write("Для ординальной шкалы предпочтителен ранговый тест; ниже — односторонний Mann–Whitney «SGD > другой» (если установлен `scipy`), плюс bootstrap по **средним** на уровне сессий (как ориентир, не замена рангового теста).\n\n")
        for attr, title in (
            ("fairness_likert_1_7", "H5 — справедливость"),
            ("continuity_likert_1_7", "H6 — непрерывность"),
        ):
            f.write(f"### {title}\n\n")
            for other in ("FLS", "GA"):
                f.write(f"- **SGD vs {other}:** {fmt_contrast(attr, other)}\n")
            f.write("\n")

        f.write("## Интерпретация (рабочие выводы)\n\n")
        f.write(
            "См. текст в канвас-панели; при малых n ориентируйтесь на ранги/MWU и ширину bootstrap-ДИ, а не только на средние.\n"
        )

    print(f"[ok] {csv_path}")
    print(f"[ok] {md_path}")
    print(f"[ok] survey sessions={len(rows)} skipped_raw_events={skipped_count}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
