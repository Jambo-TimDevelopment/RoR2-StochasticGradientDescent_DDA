import argparse
import csv
import glob
import json
import math
import os
import random
import statistics
from dataclasses import dataclass, field


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
        return float(v)
    except Exception:
        return None


def _normalize_mojibake(text: str) -> str:
    """
    Best-effort fix for common UTF-8-as-Latin1 mojibake (e.g. 'ÑÐµÑÑ' -> 'тест').
    Safe to call on normal Unicode; it will typically return the input unchanged.
    """
    if not text:
        return ""

    markers = ("Ã", "Ð", "Ñ", "Â", "â", "€", "™")
    if not any(m in text for m in markers):
        return text

    try:
        return text.encode("latin-1", errors="strict").decode("utf-8", errors="strict")
    except Exception:
        return text


def _pearson_corr(xs: list[float], ys: list[float]) -> float | None:
    if len(xs) != len(ys) or len(xs) < 2:
        return None
    mx = statistics.fmean(xs)
    my = statistics.fmean(ys)
    num = 0.0
    dx2 = 0.0
    dy2 = 0.0
    for x, y in zip(xs, ys):
        dx = x - mx
        dy = y - my
        num += dx * dy
        dx2 += dx * dx
        dy2 += dy * dy
    den = math.sqrt(dx2 * dy2)
    if den <= 0:
        return None
    return num / den


def _bootstrap_ci_diff(
    a: list[float],
    b: list[float],
    *,
    iters: int = 20_000,
    alpha: float = 0.05,
    seed: int = 42,
) -> tuple[float | None, float | None, float | None]:
    """
    Returns (mean_diff, lo, hi) for mean(a) - mean(b) using simple bootstrap.
    Uses independent resampling within each group (session-level).
    """
    if not a or not b:
        return None, None, None

    rng = random.Random(seed)

    def _sample_mean(xs: list[float]) -> float:
        return statistics.fmean(rng.choice(xs) for _ in range(len(xs)))

    diffs = []
    for _ in range(iters):
        diffs.append(_sample_mean(a) - _sample_mean(b))
    diffs.sort()
    mean_diff = statistics.fmean(a) - statistics.fmean(b)
    lo_idx = int((alpha / 2) * iters)
    hi_idx = int((1 - alpha / 2) * iters) - 1
    return mean_diff, diffs[lo_idx], diffs[hi_idx]


@dataclass
class SessionMetrics:
    session_id: str
    dda_mode: str = ""
    participant_id: str = ""
    telemetry_schema_version: int | None = None
    player_body: str = ""

    # H1
    h1_mae_align: float | None = None
    samples_count: int = 0
    axis_obs_count: int = 0

    # H2
    h2_jump_rate_flag: float | None = None
    h2_jump_rate_tau: float | None = None
    tau_jump: float | None = None

    # H3
    h3_corr_dvp_dvc: float | None = None
    h3_mean_virtual_gap_abs: float | None = None
    epsilon_v: float | None = None
    h3_within_epsilon_rate: float | None = None

    # H4
    h4_recovery_times: list[float] = field(default_factory=list)
    h4_mean_recovery_seconds: float | None = None
    h4_recovery_events_count: int = 0


def _detect_axes(props: dict) -> list[str]:
    axes = set()
    for k in props.keys():
        if k.startswith("axis_") and k.endswith("_abs_error"):
            axes.add(k[len("axis_") : -len("_abs_error")])
    return sorted(axes)


def _to_int(v):
    try:
        if v is None:
            return None
        return int(v)
    except Exception:
        return None


def main() -> int:
    ap = argparse.ArgumentParser(
        description="Aggregate PostHog JSONL telemetry to session metrics for hypotheses H1-H4."
    )
    ap.add_argument(
        "inputs",
        nargs="+",
        help="JSONL files or globs, e.g. tools/posthog_exports/ALL_events_*.jsonl",
    )
    ap.add_argument(
        "--out-dir",
        default=os.path.join("tools", "posthog_exports", "hypotheses_results"),
        help="Output directory (will be created).",
    )
    ap.add_argument(
        "--bootstrap-iters",
        type=int,
        default=20_000,
        help="Bootstrap iterations for 95%% CI of mean differences.",
    )
    ap.add_argument(
        "--seed",
        type=int,
        default=42,
        help="Random seed for bootstrap.",
    )
    ap.add_argument(
        "--only-huntress",
        action="store_true",
        help="Restrict analysis to sessions where player_body indicates Huntress/Охотница.",
    )
    args = ap.parse_args()

    files: list[str] = []
    for inp in args.inputs:
        matches = glob.glob(inp)
        files.extend(matches if matches else [inp])
    files = [os.path.normpath(p) for p in files]

    # Accumulators per session
    sess_axes_abs_errors: dict[str, list[float]] = {}
    sess_axes_jump_flags: dict[str, list[int]] = {}
    sess_axes_jump_tau: dict[str, list[int]] = {}
    sess_tau_jump: dict[str, float] = {}
    sess_eps_v: dict[str, float] = {}
    sess_dvp: dict[str, list[float]] = {}
    sess_dvc: dict[str, list[float]] = {}
    sess_vgap: dict[str, list[float]] = {}
    sess_within_eps: dict[str, list[int]] = {}
    sess_mode: dict[str, str] = {}
    sess_participant: dict[str, str] = {}
    sess_schema: dict[str, int] = {}
    sess_recovery: dict[str, list[float]] = {}
    sess_player_body: dict[str, str] = {}

    bad_json_lines = 0
    seen_events = 0

    for path in files:
        if not os.path.exists(path):
            print(f"[missing] {path}")
            continue

        for _line_no, obj in _iter_jsonl(path):
            if obj is None:
                bad_json_lines += 1
                continue
            seen_events += 1

            event = obj.get("event") or ""
            props = obj.get("properties") or {}

            session_id = props.get("session_id")
            if not session_id:
                continue

            dda_mode = props.get("dda_mode") or ""
            if dda_mode and not sess_mode.get(session_id):
                sess_mode[session_id] = dda_mode

            participant_id = props.get("participant_id") or props.get("distinct_id") or ""
            if participant_id and not sess_participant.get(session_id):
                sess_participant[session_id] = participant_id

            schema_v = _to_int(props.get("telemetry_schema_version"))
            if schema_v is not None and session_id not in sess_schema:
                sess_schema[session_id] = schema_v

            # Track hero per session (best-effort, first non-empty wins)
            body = props.get("player_body") or props.get("player_body_name") or ""
            if body and not sess_player_body.get(session_id):
                sess_player_body[session_id] = _normalize_mojibake(str(body))

            tau_jump = _safe_float(props.get("tau_jump"))
            if tau_jump is not None and session_id not in sess_tau_jump:
                sess_tau_jump[session_id] = tau_jump

            eps_v = _safe_float(props.get("epsilon_v"))
            if eps_v is not None and session_id not in sess_eps_v:
                sess_eps_v[session_id] = eps_v

            if event == "dda_sample":
                axes = _detect_axes(props)
                if axes:
                    for ax in axes:
                        ae = _safe_float(props.get(f"axis_{ax}_abs_error"))
                        if ae is not None:
                            sess_axes_abs_errors.setdefault(session_id, []).append(ae)

                        jf = props.get(f"axis_{ax}_is_jump")
                        if jf is not None:
                            sess_axes_jump_flags.setdefault(session_id, []).append(
                                1 if bool(jf) else 0
                            )

                        dm = _safe_float(props.get(f"axis_{ax}_delta_multiplier"))
                        if dm is not None and tau_jump is not None:
                            sess_axes_jump_tau.setdefault(session_id, []).append(
                                1 if abs(dm) > tau_jump else 0
                            )

                dvp = _safe_float(props.get("delta_virtual_power"))
                dvc = _safe_float(props.get("delta_virtual_challenge"))
                if dvp is not None:
                    sess_dvp.setdefault(session_id, []).append(dvp)
                if dvc is not None:
                    sess_dvc.setdefault(session_id, []).append(dvc)

                vgap = _safe_float(props.get("virtual_gap_abs"))
                if vgap is not None:
                    sess_vgap.setdefault(session_id, []).append(vgap)

                within = props.get("is_within_virtual_gap_epsilon")
                if within is not None:
                    sess_within_eps.setdefault(session_id, []).append(
                        1 if bool(within) else 0
                    )

            if event == "dda_recovery":
                rt = _safe_float(props.get("recovery_elapsed_seconds"))
                if rt is not None:
                    sess_recovery.setdefault(session_id, []).append(rt)

    # Build session rows
    sessions: list[SessionMetrics] = []
    all_session_ids = (sess_mode.keys() | sess_axes_abs_errors.keys() | sess_recovery.keys() | sess_player_body.keys())
    for session_id in sorted(all_session_ids):
        m = SessionMetrics(session_id=session_id)
        m.dda_mode = sess_mode.get(session_id, "")
        m.participant_id = sess_participant.get(session_id, "")
        m.telemetry_schema_version = sess_schema.get(session_id)
        m.player_body = sess_player_body.get(session_id, "")

        abs_errors = sess_axes_abs_errors.get(session_id, [])
        m.axis_obs_count = len(abs_errors)
        if abs_errors:
            m.h1_mae_align = statistics.fmean(abs_errors)

        jump_flags = sess_axes_jump_flags.get(session_id, [])
        if jump_flags:
            m.h2_jump_rate_flag = statistics.fmean(jump_flags)

        tau_jump = sess_tau_jump.get(session_id)
        m.tau_jump = tau_jump
        jump_tau = sess_axes_jump_tau.get(session_id, [])
        if jump_tau:
            m.h2_jump_rate_tau = statistics.fmean(jump_tau)

        vgap = sess_vgap.get(session_id, [])
        if vgap:
            m.h3_mean_virtual_gap_abs = statistics.fmean(vgap)

        eps_v = sess_eps_v.get(session_id)
        m.epsilon_v = eps_v
        within = sess_within_eps.get(session_id, [])
        if within:
            m.h3_within_epsilon_rate = statistics.fmean(within)

        dvp = sess_dvp.get(session_id, [])
        dvc = sess_dvc.get(session_id, [])
        # keep only aligned pairs
        n = min(len(dvp), len(dvc))
        if n >= 2:
            m.h3_corr_dvp_dvc = _pearson_corr(dvp[:n], dvc[:n])

        rec = sess_recovery.get(session_id, [])
        m.h4_recovery_times = rec
        m.h4_recovery_events_count = len(rec)
        if rec:
            m.h4_mean_recovery_seconds = statistics.fmean(rec)

        # approximate samples count: infer from axis obs count / axes count if possible
        # (purely informational)
        m.samples_count = 0
        sessions.append(m)

    if args.only_huntress:
        def _is_huntress(text: str) -> bool:
            if not text:
                return False
            t = _normalize_mojibake(text).strip().lower()
            return ("охотниц" in t) or ("huntress" in t)

        sessions = [s for s in sessions if _is_huntress(s.player_body)]

    out_dir = os.path.normpath(args.out_dir)
    os.makedirs(out_dir, exist_ok=True)

    # Write per-session csv
    csv_path = os.path.join(out_dir, "session_metrics_h1_h4.csv")
    with open(csv_path, "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(
            [
                "session_id",
                "dda_mode",
                "participant_id",
                "telemetry_schema_version",
                "player_body",
                "axis_obs_count",
                "h1_mae_align",
                "tau_jump",
                "h2_jump_rate_flag",
                "h2_jump_rate_tau",
                "epsilon_v",
                "h3_corr_dvp_dvc",
                "h3_mean_virtual_gap_abs",
                "h3_within_epsilon_rate",
                "h4_recovery_events_count",
                "h4_mean_recovery_seconds",
            ]
        )
        for s in sessions:
            w.writerow(
                [
                    s.session_id,
                    s.dda_mode,
                    s.participant_id,
                    s.telemetry_schema_version,
                    s.player_body,
                    s.axis_obs_count,
                    "" if s.h1_mae_align is None else s.h1_mae_align,
                    "" if s.tau_jump is None else s.tau_jump,
                    "" if s.h2_jump_rate_flag is None else s.h2_jump_rate_flag,
                    "" if s.h2_jump_rate_tau is None else s.h2_jump_rate_tau,
                    "" if s.epsilon_v is None else s.epsilon_v,
                    "" if s.h3_corr_dvp_dvc is None else s.h3_corr_dvp_dvc,
                    "" if s.h3_mean_virtual_gap_abs is None else s.h3_mean_virtual_gap_abs,
                    "" if s.h3_within_epsilon_rate is None else s.h3_within_epsilon_rate,
                    s.h4_recovery_events_count,
                    "" if s.h4_mean_recovery_seconds is None else s.h4_mean_recovery_seconds,
                ]
            )

    # Group for contrasts
    by_mode: dict[str, list[SessionMetrics]] = {}
    for s in sessions:
        if not s.dda_mode:
            continue
        by_mode.setdefault(s.dda_mode, []).append(s)

    def _vals(mode: str, attr: str) -> list[float]:
        xs = []
        for s in by_mode.get(mode, []):
            v = getattr(s, attr)
            if isinstance(v, (int, float)) and not math.isnan(v):
                xs.append(float(v))
        return xs

    def _fmt(x: float | None) -> str:
        if x is None:
            return "NA"
        return f"{x:.6g}"

    modes = sorted(by_mode.keys())

    # Write summary markdown
    md_path = os.path.join(out_dir, "summary_h1_h4.md")
    with open(md_path, "w", encoding="utf-8") as f:
        f.write("# Pilot check: H1-H4 on PostHog export\n\n")
        if args.only_huntress:
            f.write("- filter: only Huntress/Охотница sessions\n")
        f.write(f"- files: {len(files)}\n")
        f.write(f"- parsed_events: {seen_events}\n")
        f.write(f"- bad_json_lines: {bad_json_lines}\n")
        f.write(f"- sessions_total: {len(sessions)}\n")
        f.write("\n## Sessions by mode\n\n")
        for mode in modes:
            f.write(f"- {mode}: {len(by_mode[mode])}\n")

        def _write_metric_block(title: str, attr: str, *, lower_is_better: bool):
            f.write(f"\n## {title}\n\n")
            for mode in modes:
                xs = _vals(mode, attr)
                if xs:
                    f.write(
                        f"- {mode}: n={len(xs)} mean={_fmt(statistics.fmean(xs))} median={_fmt(statistics.median(xs))}\n"
                    )
                else:
                    f.write(f"- {mode}: n=0\n")

            # planned contrasts: SGD vs FLS, SGD vs GA
            for other in ("FLS", "GA"):
                a = _vals("SGD", attr)
                b = _vals(other, attr)
                if not a or not b:
                    f.write(f"\n- Contrast SGD - {other}: NA (insufficient sessions)\n")
                    continue
                mean_diff, lo, hi = _bootstrap_ci_diff(
                    a, b, iters=args.bootstrap_iters, seed=args.seed
                )
                direction_ok = None
                if mean_diff is not None:
                    if lower_is_better:
                        direction_ok = mean_diff < 0
                    else:
                        direction_ok = mean_diff > 0
                verdict = "supports" if direction_ok else "does_not_support"
                f.write(
                    f"\n- Contrast SGD - {other}: mean_diff={_fmt(mean_diff)} 95%CI[{_fmt(lo)}, {_fmt(hi)}] -> {verdict}\n"
                )

        _write_metric_block(
            "H1 alignment accuracy (session MAE_align = mean(axis_*_abs_error))",
            "h1_mae_align",
            lower_is_better=True,
        )
        _write_metric_block(
            "H2 smoothness (JumpRate by axis_*_is_jump)",
            "h2_jump_rate_flag",
            lower_is_better=True,
        )
        _write_metric_block(
            "H2 smoothness (JumpRate by |axis_*_delta_multiplier| > tau_jump)",
            "h2_jump_rate_tau",
            lower_is_better=True,
        )
        _write_metric_block(
            "H3 coupling (corr(delta_virtual_power, delta_virtual_challenge) per session)",
            "h3_corr_dvp_dvc",
            lower_is_better=False,
        )
        _write_metric_block(
            "H3 closeness (mean virtual_gap_abs per session)",
            "h3_mean_virtual_gap_abs",
            lower_is_better=True,
        )
        _write_metric_block(
            "H3 closeness (rate is_within_virtual_gap_epsilon per session)",
            "h3_within_epsilon_rate",
            lower_is_better=False,
        )
        _write_metric_block(
            "H4 recovery (mean recovery_elapsed_seconds per session)",
            "h4_mean_recovery_seconds",
            lower_is_better=True,
        )

        f.write("\n## Notes / limitations\n\n")
        f.write(
            "- This is a *pilot* check: all statistics are computed on **session-level aggregates**; bootstrap CI is shown only as an uncertainty visualization for tiny n.\n"
        )
        f.write(
            "- H2 may be trivially zero if the run produced no jumps in multipliers; in that case it is not evidence *for* smoothness, only lack of observed jumps.\n"
        )
        f.write(
            "- For H4, we rely on `dda_recovery` events and `recovery_elapsed_seconds`; if sessions contain no recovery events, H4 is not testable there.\n"
        )

    print(f"[ok] wrote {csv_path}")
    print(f"[ok] wrote {md_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

