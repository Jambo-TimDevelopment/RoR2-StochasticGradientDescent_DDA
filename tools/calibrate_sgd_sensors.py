import argparse
import glob
import json
import math
import os
import statistics
from collections import defaultdict


SENSOR_FIELDS = (
    "incoming_damage_norm01",
    "outgoing_damage_norm01",
    "hit_rate_on_player_norm01",
    "low_health_uptime",
    "deaths_per_window_norm01",
    "avg_ttk_seconds_norm01",
    "degradation_signal",
)


RAW_FIELDS = (
    "incoming_damage_rate",
    "outgoing_damage_rate",
    "hit_rate_on_player",
    "avg_ttk_seconds",
    "virtual_power_raw_offense",
    "virtual_power_raw_defense",
    "virtual_power_raw_mobility",
)


def _iter_jsonl(path: str):
    with open(path, "r", encoding="utf-8") as f:
        for line_no, line in enumerate(f, start=1):
            line = line.strip()
            if not line:
                continue
            try:
                yield line_no, json.loads(line)
            except Exception:
                yield line_no, None


def _safe_float(v):
    try:
        if v is None:
            return None
        x = float(v)
        if math.isnan(x) or math.isinf(x):
            return None
        return x
    except Exception:
        return None


def _normalize_mojibake(text: str) -> str:
    if not text:
        return ""
    text = str(text)
    markers = ("Ã", "Ð", "Ñ", "Â", "â", "€", "™")
    if not any(m in text for m in markers):
        return text
    try:
        return text.encode("latin-1", errors="strict").decode("utf-8", errors="strict")
    except Exception:
        return text


def _is_huntress(text: str) -> bool:
    t = _normalize_mojibake(text).strip().lower()
    return ("охотниц" in t) or ("huntress" in t)


def _percentile(values: list[float], q: float) -> float | None:
    if not values:
        return None
    xs = sorted(values)
    if len(xs) == 1:
        return xs[0]
    pos = (len(xs) - 1) * q
    lo = int(math.floor(pos))
    hi = int(math.ceil(pos))
    if lo == hi:
        return xs[lo]
    return xs[lo] + ((xs[hi] - xs[lo]) * (pos - lo))


def _fmt(x: float | None) -> str:
    if x is None:
        return "NA"
    return f"{x:.6g}"


def main() -> int:
    ap = argparse.ArgumentParser(
        description="Create calibration hints for SGD sensors and degradation thresholds from PostHog JSONL exports."
    )
    ap.add_argument("inputs", nargs="+", help="JSONL files or globs.")
    ap.add_argument(
        "--only-huntress",
        action="store_true",
        help="Restrict calibration hints to Huntress/Охотница sessions.",
    )
    ap.add_argument(
        "--out",
        default=os.path.join("tools", "posthog_exports", "hypotheses_results", "sensor_calibration_hints.md"),
        help="Output markdown report.",
    )
    args = ap.parse_args()

    files: list[str] = []
    for inp in args.inputs:
        matches = glob.glob(inp)
        files.extend(matches if matches else [inp])
    files = [os.path.normpath(p) for p in files]

    session_body: dict[str, str] = {}
    rows: list[dict] = []
    bad_json_lines = 0

    for path in files:
        if not os.path.exists(path):
            print(f"[missing] {path}")
            continue
        for _line_no, obj in _iter_jsonl(path):
            if obj is None:
                bad_json_lines += 1
                continue
            props = obj.get("properties") or {}
            session_id = props.get("session_id")
            if not session_id:
                continue
            body = props.get("player_body") or props.get("player_body_name") or ""
            if body and session_id not in session_body:
                session_body[session_id] = _normalize_mojibake(body)
            if obj.get("event") == "dda_sample":
                rows.append(props)

    values = defaultdict(list)
    sessions = set()
    for props in rows:
        session_id = props.get("session_id")
        if not session_id:
            continue
        if args.only_huntress and not _is_huntress(session_body.get(session_id, "")):
            continue
        sessions.add(session_id)
        for field in SENSOR_FIELDS + RAW_FIELDS:
            value = _safe_float(props.get(field))
            if value is not None:
                values[field].append(value)

    out_path = os.path.normpath(args.out)
    os.makedirs(os.path.dirname(out_path), exist_ok=True)

    with open(out_path, "w", encoding="utf-8") as f:
        f.write("# SGD sensor calibration hints\n\n")
        f.write(f"- files: {len(files)}\n")
        f.write(f"- bad_json_lines: {bad_json_lines}\n")
        f.write(f"- samples_used: {len(next(iter(values.values()), [])) if values else 0}\n")
        f.write(f"- sessions_used: {len(sessions)}\n")
        if args.only_huntress:
            f.write("- filter: only Huntress/Охотница sessions\n")

        f.write("\n## Normalized sensor distributions\n\n")
        for field in SENSOR_FIELDS:
            xs = values.get(field, [])
            if not xs:
                f.write(f"- {field}: n=0\n")
                continue
            f.write(
                f"- {field}: n={len(xs)} mean={_fmt(statistics.fmean(xs))} "
                f"p50={_fmt(_percentile(xs, 0.50))} p75={_fmt(_percentile(xs, 0.75))} "
                f"p90={_fmt(_percentile(xs, 0.90))} p95={_fmt(_percentile(xs, 0.95))} max={_fmt(max(xs))}\n"
            )

        f.write("\n## Raw sensor distributions\n\n")
        for field in RAW_FIELDS:
            xs = values.get(field, [])
            if not xs:
                f.write(f"- {field}: n=0\n")
                continue
            f.write(
                f"- {field}: n={len(xs)} mean={_fmt(statistics.fmean(xs))} "
                f"p50={_fmt(_percentile(xs, 0.50))} p75={_fmt(_percentile(xs, 0.75))} "
                f"p90={_fmt(_percentile(xs, 0.90))} p95={_fmt(_percentile(xs, 0.95))} max={_fmt(max(xs))}\n"
            )

        deg = values.get("degradation_signal", [])
        f.write("\n## Threshold hints\n\n")
        if deg:
            f.write(
                "- Candidate degradation thresholds from observed pilot distribution: "
                f"p75={_fmt(_percentile(deg, 0.75))}, "
                f"p90={_fmt(_percentile(deg, 0.90))}, "
                f"p95={_fmt(_percentile(deg, 0.95))}.\n"
            )
            f.write(
                "- Use these as diagnostics only: final `telemetryDegradationThreshold` and "
                "`telemetryRecoveryThreshold` should be fixed before the final experiment.\n"
            )
        else:
            f.write("- Not enough degradation_signal samples to suggest thresholds.\n")

        f.write("\n## Notes\n\n")
        f.write(
            "- This report does not mix DDA planes. It only summarizes sensor distributions used by the four fixed axes: damage, attackSpeed, hp, moveSpeed.\n"
        )
        f.write(
            "- Re-run after a balanced pilot to choose final sensor normalization constants and H4 thresholds.\n"
        )

    print(f"[ok] wrote {out_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

