"""
Generate PNG charts for H5/H6 (Likert survey) from session_survey_h5_h6.csv.

Output:
  hypotheses_chart_reports/runs/YYYYMMDD_HHMMSS/
    h5_fairness_likert_by_mode.png
    h6_continuity_likert_by_mode.png
    h5_h6_likert_dual.png
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
class SurveyCsvRow:
    dda_mode: str
    fairness: float | None
    continuity: float | None


def _load_rows(csv_path: str) -> list[SurveyCsvRow]:
    rows: list[SurveyCsvRow] = []
    with open(csv_path, "r", encoding="utf-8", newline="") as f:
        reader = csv.DictReader(f)
        for rec in reader:
            mode = (rec.get("dda_mode") or "").strip()
            if mode not in {"FLS", "GA", "SGD"}:
                continue
            rows.append(
                SurveyCsvRow(
                    dda_mode=mode,
                    fairness=_safe_float(rec.get("fairness_likert_1_7")),
                    continuity=_safe_float(rec.get("continuity_likert_1_7")),
                )
            )
    return rows


def _mean(values: Iterable[float]) -> float | None:
    xs = list(values)
    if not xs:
        return None
    return sum(xs) / len(xs)


def _mode_stats(rows: list[SurveyCsvRow], field: str) -> dict[str, tuple[float | None, int]]:
    out: dict[str, tuple[float | None, int]] = {}
    for mode in ("FLS", "GA", "SGD"):
        vals = []
        for r in rows:
            if r.dda_mode != mode:
                continue
            v = getattr(r, field)
            if isinstance(v, float):
                vals.append(v)
        out[mode] = (_mean(vals), len(vals))
    return out


def _autolabel(ax, bars, stats: dict[str, tuple[float | None, int]], modes: list[str]) -> None:
    for i, bar in enumerate(bars):
        mode = modes[i]
        val, n = stats[mode]
        label = "NA" if val is None else f"{val:.2f}\n(n={n})"
        ax.text(
            bar.get_x() + bar.get_width() / 2,
            bar.get_height(),
            label,
            ha="center",
            va="bottom",
            fontsize=9,
        )


def _plot_likert_bar(
    *,
    out_path: str,
    title: str,
    ylabel: str,
    mode_stats: dict[str, tuple[float | None, int]],
) -> None:
    import matplotlib.pyplot as plt

    modes = ["FLS", "GA", "SGD"]
    values = [mode_stats[m][0] if mode_stats[m][0] is not None else 0.0 for m in modes]
    colors = ["#4C78A8", "#72B7B2", "#F58518"]

    fig, ax = plt.subplots(figsize=(8, 5), dpi=140)
    bars = ax.bar(modes, values, color=colors)
    _autolabel(ax, bars, mode_stats, modes)
    ax.set_ylim(1.0, 7.0)
    ax.axhline(4.0, color="gray", linewidth=0.8, linestyle="--", alpha=0.5)
    ax.set_title(title)
    ax.set_ylabel(ylabel)
    ax.grid(axis="y", linestyle="--", alpha=0.3)
    ax.text(0.01, 0.98, "Likert 1–7 (higher = more agreement)", transform=ax.transAxes, va="top", fontsize=9)
    fig.tight_layout()
    fig.savefig(out_path)
    plt.close(fig)


def _plot_dual(out_path: str, rows: list[SurveyCsvRow]) -> None:
    import matplotlib.pyplot as plt

    modes = ["FLS", "GA", "SGD"]
    h5 = _mode_stats(rows, "fairness")
    h6 = _mode_stats(rows, "continuity")
    x = list(range(len(modes)))
    w = 0.35
    y1 = [h5[m][0] if h5[m][0] is not None else 0.0 for m in modes]
    y2 = [h6[m][0] if h6[m][0] is not None else 0.0 for m in modes]

    fig, ax = plt.subplots(figsize=(9, 5), dpi=140)
    b1 = ax.bar([i - w / 2 for i in x], y1, width=w, label="H5 fairness")
    b2 = ax.bar([i + w / 2 for i in x], y2, width=w, label="H6 continuity")
    ax.set_xticks(x, modes)
    ax.set_ylim(1.0, 7.0)
    ax.axhline(4.0, color="gray", linewidth=0.8, linestyle="--", alpha=0.5)
    ax.set_title("H5/H6: mean Likert by adaptation mode")
    ax.set_ylabel("Mean score (1–7)")
    ax.grid(axis="y", linestyle="--", alpha=0.3)
    ax.legend()

    for bars in (b1, b2):
        for bar in bars:
            ax.text(
                bar.get_x() + bar.get_width() / 2,
                bar.get_height(),
                f"{bar.get_height():.2f}",
                ha="center",
                va="bottom",
                fontsize=8,
            )
    fig.tight_layout()
    fig.savefig(out_path)
    plt.close(fig)


def _require_matplotlib() -> None:
    try:
        import matplotlib  # noqa: F401
    except Exception as exc:
        raise RuntimeError("matplotlib required: pip install matplotlib") from exc


def main() -> int:
    ap = argparse.ArgumentParser(description="Charts for H5/H6 survey CSV")
    ap.add_argument(
        "--survey-csv",
        required=True,
        help="session_survey_h5_h6.csv from analyze_hypotheses_h5_h6.py",
    )
    ap.add_argument(
        "--reports-root",
        default=os.path.join("tools", "analyze_data_scripts", "hypotheses_chart_reports"),
    )
    args = ap.parse_args()

    _require_matplotlib()
    csv_path = os.path.normpath(args.survey_csv)
    if not os.path.isfile(csv_path):
        raise FileNotFoundError(csv_path)

    rows = _load_rows(csv_path)
    if not rows:
        raise RuntimeError("No valid rows in survey CSV.")

    ts = datetime.now().strftime("%Y%m%d_%H%M%S")
    run_dir = os.path.join(os.path.normpath(args.reports_root), "runs", ts)
    os.makedirs(run_dir, exist_ok=True)

    _plot_likert_bar(
        out_path=os.path.join(run_dir, "h5_fairness_likert_by_mode.png"),
        title="H5: Perceived fairness (mean Likert)",
        ylabel="Fairness (1–7)",
        mode_stats=_mode_stats(rows, "fairness"),
    )
    _plot_likert_bar(
        out_path=os.path.join(run_dir, "h6_continuity_likert_by_mode.png"),
        title="H6: Perceived continuity (mean Likert)",
        ylabel="Continuity (1–7)",
        mode_stats=_mode_stats(rows, "continuity"),
    )
    _plot_dual(os.path.join(run_dir, "h5_h6_likert_dual.png"), rows)

    meta = {
        "generated_at": datetime.now().isoformat(),
        "survey_csv": csv_path,
        "run_dir": run_dir,
        "n_rows": len(rows),
        "charts": sorted(x for x in os.listdir(run_dir) if x.endswith(".png")),
    }
    with open(os.path.join(run_dir, "report_meta.json"), "w", encoding="utf-8") as f:
        json.dump(meta, f, ensure_ascii=False, indent=2)

    print(f"[ok] {run_dir}")
    for c in meta["charts"]:
        print(f"[ok] {os.path.join(run_dir, c)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
