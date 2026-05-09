"""
Generate per-hypothesis PNG charts (H1, H2, H3, H4) from session_metrics_h1_h4.csv.

Output layout:
tools/analyze_data_scripts/hypotheses_chart_reports/
  runs/
    YYYYMMDD_HHMMSS/
      h1_alignment_mae.png
      h2_smoothness_core.png
      h3_axis_speed_by_algorithm_and_axis.png
      h3_axis_accuracy_by_algorithm_and_axis.png
      h3_axis_within_epsilon_by_algorithm_and_axis.png
      h4_recovery_time.png
      h4_degradation_signal.png
      report_meta.json
"""

from __future__ import annotations

import argparse
import csv
import json
import math
import os
from dataclasses import dataclass
from datetime import datetime
from typing import Iterable

H3_VIRTUAL_AXES = ("hp", "move_speed", "attack_speed", "attack_damage")
H3_VIRTUAL_AXIS_LABELS = {
    "hp": "HP",
    "move_speed": "Move Speed",
    "attack_speed": "Attack Speed",
    "attack_damage": "Attack Damage",
}


def _safe_float(value: str | None) -> float | None:
    if value is None:
        return None
    text = str(value).strip()
    if text == "":
        return None
    try:
        val = float(text)
        if math.isnan(val):
            return None
        return val
    except Exception:
        return None


@dataclass
class SessionRow:
    dda_mode: str
    h1_mae_align: float | None
    h2_error_jump_rate_tau: float | None
    h2_abs_error_jump_rate_tau: float | None
    h3_axis_corr_dvp_dvc_mean: float | None
    h3_axis_corr_dvp_dvc_mean_lag1: float | None
    h3_axis_corr_dvp_dvc_mean_nonzero_dvc: float | None
    h3_axis_corr_hp: float | None
    h3_axis_corr_move_speed: float | None
    h3_axis_corr_attack_speed: float | None
    h3_axis_corr_attack_damage: float | None
    h3_axis_mean_virtual_gap_abs: float | None
    h3_axis_mean_gap_hp: float | None
    h3_axis_mean_gap_move_speed: float | None
    h3_axis_mean_gap_attack_speed: float | None
    h3_axis_mean_gap_attack_damage: float | None
    h3_axis_within_epsilon_rate: float | None
    h3_axis_within_epsilon_rate_hp: float | None
    h3_axis_within_epsilon_rate_move_speed: float | None
    h3_axis_within_epsilon_rate_attack_speed: float | None
    h3_axis_within_epsilon_rate_attack_damage: float | None
    h4_mean_recovery_seconds: float | None
    h4_degradation_sample_rate: float | None


def _load_rows(csv_path: str) -> list[SessionRow]:
    rows: list[SessionRow] = []
    with open(csv_path, "r", encoding="utf-8", newline="") as f:
        reader = csv.DictReader(f)
        for rec in reader:
            mode = (rec.get("dda_mode") or "").strip()
            if mode not in {"FLS", "GA", "SGD"}:
                continue
            rows.append(
                SessionRow(
                    dda_mode=mode,
                    h1_mae_align=_safe_float(rec.get("h1_mae_align")),
                    h2_error_jump_rate_tau=_safe_float(rec.get("h2_error_jump_rate_tau")),
                    h2_abs_error_jump_rate_tau=_safe_float(rec.get("h2_abs_error_jump_rate_tau")),
                    h3_axis_corr_dvp_dvc_mean=_safe_float(rec.get("h3_axis_corr_dvp_dvc_mean")),
                    h3_axis_corr_dvp_dvc_mean_lag1=_safe_float(rec.get("h3_axis_corr_dvp_dvc_mean_lag1")),
                    h3_axis_corr_dvp_dvc_mean_nonzero_dvc=_safe_float(rec.get("h3_axis_corr_dvp_dvc_mean_nonzero_dvc")),
                    h3_axis_corr_hp=_safe_float(rec.get("h3_axis_corr_hp")),
                    h3_axis_corr_move_speed=_safe_float(rec.get("h3_axis_corr_move_speed")),
                    h3_axis_corr_attack_speed=_safe_float(rec.get("h3_axis_corr_attack_speed")),
                    h3_axis_corr_attack_damage=_safe_float(rec.get("h3_axis_corr_attack_damage")),
                    h3_axis_mean_virtual_gap_abs=_safe_float(rec.get("h3_axis_mean_virtual_gap_abs")),
                    h3_axis_mean_gap_hp=_safe_float(rec.get("h3_axis_mean_gap_hp")),
                    h3_axis_mean_gap_move_speed=_safe_float(rec.get("h3_axis_mean_gap_move_speed")),
                    h3_axis_mean_gap_attack_speed=_safe_float(rec.get("h3_axis_mean_gap_attack_speed")),
                    h3_axis_mean_gap_attack_damage=_safe_float(rec.get("h3_axis_mean_gap_attack_damage")),
                    h3_axis_within_epsilon_rate=_safe_float(rec.get("h3_axis_within_epsilon_rate")),
                    h3_axis_within_epsilon_rate_hp=_safe_float(rec.get("h3_axis_within_epsilon_rate_hp")),
                    h3_axis_within_epsilon_rate_move_speed=_safe_float(rec.get("h3_axis_within_epsilon_rate_move_speed")),
                    h3_axis_within_epsilon_rate_attack_speed=_safe_float(rec.get("h3_axis_within_epsilon_rate_attack_speed")),
                    h3_axis_within_epsilon_rate_attack_damage=_safe_float(rec.get("h3_axis_within_epsilon_rate_attack_damage")),
                    h4_mean_recovery_seconds=_safe_float(rec.get("h4_mean_recovery_seconds")),
                    h4_degradation_sample_rate=_safe_float(rec.get("h4_degradation_sample_rate")),
                )
            )
    return rows


def _mean(values: Iterable[float]) -> float | None:
    xs = list(values)
    if not xs:
        return None
    return sum(xs) / len(xs)


def _mode_mean_and_n(rows: list[SessionRow], field_name: str) -> dict[str, tuple[float | None, int]]:
    result: dict[str, tuple[float | None, int]] = {}
    for mode in ("FLS", "GA", "SGD"):
        vals = []
        for row in rows:
            if row.dda_mode != mode:
                continue
            v = getattr(row, field_name)
            if isinstance(v, float):
                vals.append(v)
        result[mode] = (_mean(vals), len(vals))
    return result


def _autolabel(ax, bars, mode_stats: dict[str, tuple[float | None, int]], modes: list[str]) -> None:
    for i, bar in enumerate(bars):
        mode = modes[i]
        val, n = mode_stats[mode]
        label = "NA" if val is None else f"{val:.3f}\n(n={n})"
        ax.text(
            bar.get_x() + bar.get_width() / 2,
            bar.get_height(),
            label,
            ha="center",
            va="bottom",
            fontsize=9,
        )


def _plot_simple_bar(
    *,
    out_path: str,
    title: str,
    ylabel: str,
    mode_stats: dict[str, tuple[float | None, int]],
    lower_is_better: bool,
):
    import matplotlib.pyplot as plt

    modes = ["FLS", "GA", "SGD"]
    values = [mode_stats[m][0] if mode_stats[m][0] is not None else 0.0 for m in modes]
    colors = ["#4C78A8", "#72B7B2", "#F58518"]

    fig, ax = plt.subplots(figsize=(9, 5), dpi=140)
    bars = ax.bar(modes, values, color=colors)
    _autolabel(ax, bars, mode_stats, modes)
    ax.set_title(title)
    ax.set_ylabel(ylabel)
    ax.grid(axis="y", linestyle="--", alpha=0.3)
    direction = "lower is better" if lower_is_better else "higher is better"
    ax.text(0.01, 0.98, direction, transform=ax.transAxes, va="top", fontsize=9)
    fig.tight_layout()
    fig.savefig(out_path)
    plt.close(fig)


def _plot_h2(out_path: str, rows: list[SessionRow]) -> None:
    import matplotlib.pyplot as plt

    modes = ["FLS", "GA", "SGD"]
    stats_jump = _mode_mean_and_n(rows, "h2_error_jump_rate_tau")
    stats_abs_jump = _mode_mean_and_n(rows, "h2_abs_error_jump_rate_tau")
    y1 = [stats_jump[m][0] if stats_jump[m][0] is not None else 0.0 for m in modes]
    y2 = [stats_abs_jump[m][0] if stats_abs_jump[m][0] is not None else 0.0 for m in modes]

    fig, ax = plt.subplots(figsize=(10, 5), dpi=140)
    x = list(range(len(modes)))
    w = 0.35
    b1 = ax.bar([i - w / 2 for i in x], y1, width=w, label="P(|Δe| > tau)")
    b2 = ax.bar([i + w / 2 for i in x], y2, width=w, label="P(|Δabs_error| > tau)")

    ax.set_xticks(x, modes)
    ax.set_title("H2.3: Smoothness of error trajectory")
    ax.set_ylabel("Session mean rate")
    ax.grid(axis="y", linestyle="--", alpha=0.3)
    ax.legend()

    for bars in (b1, b2):
        for bar in bars:
            ax.text(
                bar.get_x() + bar.get_width() / 2,
                bar.get_height(),
                f"{bar.get_height():.3f}",
                ha="center",
                va="bottom",
                fontsize=8,
            )

    fig.tight_layout()
    fig.savefig(out_path)
    plt.close(fig)


def _plot_h3_speed(out_path: str, rows: list[SessionRow]) -> None:
    speed_field_by_axis = {
        "hp": "h3_axis_corr_hp",
        "move_speed": "h3_axis_corr_move_speed",
        "attack_speed": "h3_axis_corr_attack_speed",
        "attack_damage": "h3_axis_corr_attack_damage",
    }
    _plot_h3_grouped_by_axis(
        out_path=out_path,
        rows=rows,
        title="H3.1 (axis-first): Reaction speed / coupling by algorithm and axis",
        ylabel="Axis corr(delta Vp_i, delta Vc_i)",
        field_by_axis=speed_field_by_axis,
        lower_is_better=False,
        diagnostics_fields=(
            ("lag1 mean corr", "h3_axis_corr_dvp_dvc_mean_lag1"),
            ("lag0 mean corr", "h3_axis_corr_dvp_dvc_mean"),
            ("non-zero ΔVc mean corr", "h3_axis_corr_dvp_dvc_mean_nonzero_dvc"),
        ),
    )


def _mode_axis_mean_and_n(
    rows: list[SessionRow],
    field_by_axis: dict[str, str],
) -> dict[str, dict[str, tuple[float | None, int]]]:
    result: dict[str, dict[str, tuple[float | None, int]]] = {}
    for mode in ("FLS", "GA", "SGD"):
        mode_result: dict[str, tuple[float | None, int]] = {}
        for axis in H3_VIRTUAL_AXES:
            field_name = field_by_axis[axis]
            vals: list[float] = []
            for row in rows:
                if row.dda_mode != mode:
                    continue
                v = getattr(row, field_name)
                if isinstance(v, float):
                    vals.append(v)
            mode_result[axis] = (_mean(vals), len(vals))
        result[mode] = mode_result
    return result


def _plot_h3_grouped_by_axis(
    *,
    out_path: str,
    rows: list[SessionRow],
    title: str,
    ylabel: str,
    field_by_axis: dict[str, str],
    lower_is_better: bool,
    diagnostics_fields: tuple[tuple[str, str], ...] = (),
) -> None:
    import matplotlib.pyplot as plt

    modes = ["FLS", "GA", "SGD"]
    colors = {"FLS": "#4C78A8", "GA": "#72B7B2", "SGD": "#F58518"}
    mode_axis_stats = _mode_axis_mean_and_n(rows, field_by_axis)

    fig, ax = plt.subplots(figsize=(12, 6), dpi=140)
    x = list(range(len(H3_VIRTUAL_AXES)))
    width = 0.25
    offsets = {
        "FLS": -width,
        "GA": 0.0,
        "SGD": width,
    }

    for mode in modes:
        heights = []
        for axis in H3_VIRTUAL_AXES:
            value, _n = mode_axis_stats[mode][axis]
            heights.append(value if value is not None else 0.0)
        bars = ax.bar(
            [idx + offsets[mode] for idx in x],
            heights,
            width=width,
            label=mode,
            color=colors[mode],
            alpha=0.9,
        )

        for idx, bar in enumerate(bars):
            axis = H3_VIRTUAL_AXES[idx]
            value, n = mode_axis_stats[mode][axis]
            if value is None:
                bar.set_facecolor("#D3D3D3")
                bar.set_hatch("//")
                label = "NA"
                y = 0.0
            else:
                label = f"{value:.3f}\n(n={n})"
                y = value
            va = "bottom" if y >= 0 else "top"
            y_offset = 0.01 if y >= 0 else -0.01
            ax.text(
                bar.get_x() + bar.get_width() / 2,
                y + y_offset,
                label,
                ha="center",
                va=va,
                fontsize=8,
            )

    if not lower_is_better:
        ax.axhline(0.0, color="black", linewidth=1, alpha=0.5)
    ax.set_xticks(x, [H3_VIRTUAL_AXIS_LABELS[axis] for axis in H3_VIRTUAL_AXES])
    ax.set_title(title)
    ax.set_ylabel(ylabel)
    ax.grid(axis="y", linestyle="--", alpha=0.3)
    ax.legend()
    direction = "lower is better" if lower_is_better else "higher is better"
    ax.text(0.01, 0.98, direction, transform=ax.transAxes, va="top", fontsize=9)

    if diagnostics_fields:
        diagnostics_lines: list[str] = []
        for mode in modes:
            pieces = []
            for metric_label, field_name in diagnostics_fields:
                val, n = _mode_mean_and_n(rows, field_name)[mode]
                if val is None:
                    pieces.append(f"{metric_label}: NA")
                else:
                    pieces.append(f"{metric_label}: {val:.3f} (n={n})")
            diagnostics_lines.append(f"{mode}: " + ", ".join(pieces))
        ax.text(
            0.01,
            -0.24,
            "Diagnostics (algorithm-level):\n" + "\n".join(diagnostics_lines),
            transform=ax.transAxes,
            fontsize=8,
            va="top",
            ha="left",
        )

    fig.tight_layout()
    fig.savefig(out_path)
    plt.close(fig)


def _require_matplotlib() -> str:
    try:
        import matplotlib  # noqa: F401
    except Exception as exc:
        raise RuntimeError(
            "matplotlib is required. Install with: pip install matplotlib"
        ) from exc
    return "ok"


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Generate hypothesis charts (H1/H2/H3/H4) from session_metrics_h1_h4.csv"
    )
    parser.add_argument(
        "--session-csv",
        required=True,
        help="Path to session_metrics_h1_h4.csv produced by analyze_hypotheses_h1_h4.py",
    )
    parser.add_argument(
        "--reports-root",
        default=os.path.join("tools", "analyze_data_scripts", "hypotheses_chart_reports"),
        help="Root folder for chart reports.",
    )
    args = parser.parse_args()

    _require_matplotlib()

    session_csv = os.path.normpath(args.session_csv)
    if not os.path.exists(session_csv):
        raise FileNotFoundError(f"session csv not found: {session_csv}")

    rows = _load_rows(session_csv)
    if not rows:
        raise RuntimeError("No rows with dda_mode in {FLS, GA, SGD} found in session csv.")

    ts = datetime.now().strftime("%Y%m%d_%H%M%S")
    report_root = os.path.normpath(args.reports_root)
    run_dir = os.path.join(report_root, "runs", ts)
    os.makedirs(run_dir, exist_ok=True)

    # H1
    _plot_simple_bar(
        out_path=os.path.join(run_dir, "h1_alignment_mae.png"),
        title="H1: Alignment accuracy (MAE_align)",
        ylabel="Session mean MAE_align",
        mode_stats=_mode_mean_and_n(rows, "h1_mae_align"),
        lower_is_better=True,
    )

    # H2
    _plot_h2(os.path.join(run_dir, "h2_smoothness_core.png"), rows)

    # H3 (axis-first: by algorithm and by axis)
    _plot_h3_speed(os.path.join(run_dir, "h3_axis_speed_by_algorithm_and_axis.png"), rows)
    _plot_h3_grouped_by_axis(
        out_path=os.path.join(run_dir, "h3_axis_accuracy_by_algorithm_and_axis.png"),
        rows=rows,
        title="H3.2 (axis-first): Compensation accuracy by algorithm and axis",
        ylabel="E[|Vc_i - Vp_i|] by axis",
        field_by_axis={
            "hp": "h3_axis_mean_gap_hp",
            "move_speed": "h3_axis_mean_gap_move_speed",
            "attack_speed": "h3_axis_mean_gap_attack_speed",
            "attack_damage": "h3_axis_mean_gap_attack_damage",
        },
        lower_is_better=True,
    )
    _plot_h3_grouped_by_axis(
        out_path=os.path.join(run_dir, "h3_axis_within_epsilon_by_algorithm_and_axis.png"),
        rows=rows,
        title="H3.3 (axis-first): Within epsilon_v by algorithm and axis",
        ylabel="Rate within epsilon_v by axis",
        field_by_axis={
            "hp": "h3_axis_within_epsilon_rate_hp",
            "move_speed": "h3_axis_within_epsilon_rate_move_speed",
            "attack_speed": "h3_axis_within_epsilon_rate_attack_speed",
            "attack_damage": "h3_axis_within_epsilon_rate_attack_damage",
        },
        lower_is_better=False,
    )

    # H4
    _plot_simple_bar(
        out_path=os.path.join(run_dir, "h4_recovery_time.png"),
        title="H4: Recovery time after degradation",
        ylabel="T_recovery mean, sec",
        mode_stats=_mode_mean_and_n(rows, "h4_mean_recovery_seconds"),
        lower_is_better=True,
    )
    _plot_simple_bar(
        out_path=os.path.join(run_dir, "h4_degradation_signal.png"),
        title="H4 diagnostics: degradation sample rate",
        ylabel="Degradation sample rate",
        mode_stats=_mode_mean_and_n(rows, "h4_degradation_sample_rate"),
        lower_is_better=True,
    )

    meta = {
        "generated_at": datetime.now().isoformat(),
        "session_csv": session_csv,
        "run_dir": run_dir,
        "charts": sorted([name for name in os.listdir(run_dir) if name.endswith(".png")]),
    }
    with open(os.path.join(run_dir, "report_meta.json"), "w", encoding="utf-8") as f:
        json.dump(meta, f, ensure_ascii=False, indent=2)

    print(f"[ok] report root: {report_root}")
    print(f"[ok] run dir: {run_dir}")
    for chart_name in meta["charts"]:
        print(f"[ok] chart: {os.path.join(run_dir, chart_name)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

