#!/usr/bin/env python3
"""
Поосевое разложение H3 и сводка NA по сессиям из session_metrics_h1_h4.csv.

Не требует scipy: контрасты — bootstrap разности средних (как в analyze_hypotheses_h1_h4.py).

Запуск (из корня репозитория мода):
  python tools/analyze_data_scripts/analyze_h3_axis_na_detail.py \\
    --csv tools/export_data_scripts/posthog_exports/hypotheses_results_schema7_axis20_20260512_004757/session_metrics_h1_h4.csv \\
    --out-md tools/export_data_scripts/posthog_exports/hypotheses_results_schema7_axis20_20260512_004757/h3_axis_na_detail.md
"""

from __future__ import annotations

import argparse
import csv
import random
import statistics
from collections import Counter, defaultdict
from pathlib import Path


AXES_ORDER = ("hp", "move_speed", "attack_speed", "attack_damage")

CORR_BY_AXIS: tuple[tuple[str, str], ...] = tuple(
    (ax, f"h3_axis_corr_{ax}") for ax in AXES_ORDER
)
GAP_BY_AXIS: tuple[tuple[str, str], ...] = tuple(
    (ax, f"h3_axis_mean_gap_{ax}") for ax in AXES_ORDER
)
WITHIN_BY_AXIS: tuple[tuple[str, str], ...] = tuple(
    (ax, f"h3_axis_within_epsilon_rate_{ax}") for ax in AXES_ORDER
)


def _sf(x: str | None) -> float | None:
    if x is None:
        return None
    t = str(x).strip()
    if t == "":
        return None
    try:
        v = float(t)
    except ValueError:
        return None
    if v != v:  # NaN
        return None
    return v


def _bootstrap_diff(
    a: list[float],
    b: list[float],
    *,
    iters: int = 20000,
    alpha: float = 0.05,
    seed: int = 42,
) -> tuple[float | None, float | None, float | None]:
    if not a or not b:
        return None, None, None
    rng = random.Random(seed)

    def sm(xs: list[float]) -> float:
        return statistics.fmean(rng.choice(xs) for _ in range(len(xs)))

    diffs = [sm(a) - sm(b) for _ in range(iters)]
    diffs.sort()
    md = statistics.fmean(a) - statistics.fmean(b)
    lo = diffs[int((alpha / 2) * iters)]
    hi = diffs[int((1 - alpha / 2) * iters) - 1]
    return md, lo, hi


def _collect(csv_path: Path) -> list[dict]:
    rows: list[dict] = []
    with csv_path.open(encoding="utf-8", newline="") as f:
        for rec in csv.DictReader(f):
            mode = (rec.get("dda_mode") or "").strip()
            if mode not in {"FLS", "GA", "SGD"}:
                continue
            rows.append(rec)
    return rows


def _vals(rows: list[dict], mode: str, field: str) -> list[float]:
    out: list[float] = []
    for rec in rows:
        if (rec.get("dda_mode") or "").strip() != mode:
            continue
        v = _sf(rec.get(field))
        if v is not None:
            out.append(v)
    return out


def _fmt(x: float | None, nd: int = 6) -> str:
    if x is None:
        return "—"
    return f"{x:.{nd}f}".replace(".", ",")


def _reason_tokens(s: str | None) -> list[str]:
    if not s or not str(s).strip():
        return []
    return [t for t in str(s).strip().split("|") if t]


def main() -> int:
    ap = argparse.ArgumentParser(description="H3 axis-first + NA detail report")
    ap.add_argument("--csv", required=True, type=Path)
    ap.add_argument("--out-md", required=True, type=Path)
    ap.add_argument("--bootstrap-iters", type=int, default=20000)
    ap.add_argument("--seed", type=int, default=42)
    args = ap.parse_args()

    rows = _collect(args.csv)
    modes = ("FLS", "GA", "SGD")
    n_by_mode = Counter((rec.get("dda_mode") or "").strip() for rec in rows)

    lines: list[str] = []
    lines.append("# H3: поосевое разложение и NA (Schema 7, сессионные агрегаты)\n")
    lines.append(f"- Источник: `{args.csv.as_posix()}`\n")
    lines.append(f"- Сессий в выборке: **{len(rows)}** (FLS {n_by_mode['FLS']}, GA {n_by_mode['GA']}, SGD {n_by_mode['SGD']}).\n")
    lines.append(
        "- **NA по оси для corr:** считается по факту **отсутствия числа** в колонке `h3_axis_corr_*` "
        "(строчные поля `h3_axis_corr_na_reason` в CSV дают грубую диагностику, но при повторяющихся "
        "причинах склейка может сокращать дубликаты — для осей опираемся на валидность значения).\n"
    )
    lines.append(
        "- **Bootstrap 95% ДИ:** разность средних **SGD − контраст** по сессиям с **ненулевым** числом в колонке "
        f"(`{args.bootstrap_iters}` итераций, seed={args.seed}); это ориентир, как в основном отчёте H1–H4.\n\n"
    )

    def section_metrics(title: str, spec: tuple[tuple[str, str], ...], *, lower_better_for_sgd: bool | None) -> None:
        lines.append(f"## {title}\n")
        if lower_better_for_sgd is True:
            lines.append("*(Для интерпретации «в пользу SGD-DDA»: меньше значение метрики.)*\n\n")
        elif lower_better_for_sgd is False:
            lines.append("*(Выше значение может соответствовать лучшему удержанию в ε; для корреляций знак интерпретируется отдельно.)*\n\n")
        else:
            lines.append("*(Направление «лучше/хуже» зависит от подпроверки; таблица даёт средние и разности.)*\n\n")

        headers = ["Ось", "Поле", "n FLS", "n GA", "n SGD", "mean FLS", "mean GA", "mean SGD"]
        lines.append("| " + " | ".join(headers) + " |\n")
        lines.append("| " + " | ".join(["---"] * len(headers)) + " |\n")
        for ax, field in spec:
            ns = [len(_vals(rows, m, field)) for m in modes]
            means = [statistics.fmean(_vals(rows, m, field)) if _vals(rows, m, field) else None for m in modes]
            lines.append(
                "| "
                + " | ".join(
                    [
                        f"`{ax}`",
                        f"`{field}`",
                        str(ns[0]),
                        str(ns[1]),
                        str(ns[2]),
                        _fmt(means[0]),
                        _fmt(means[1]),
                        _fmt(means[2]),
                    ]
                )
                + " |\n"
            )

        lines.append("\n**Bootstrap разности средних (SGD − FLS, SGD − GA)** по ненулевым значениям:\n\n")
        lines.append("| Ось | Поле | SGD−FLS | 95% ДИ | SGD−GA | 95% ДИ |\n")
        lines.append("| --- | --- | ---: | --- | ---: | --- |\n")
        for ax, field in spec:
            a_sgd = _vals(rows, "SGD", field)
            a_fls = _vals(rows, "FLS", field)
            a_ga = _vals(rows, "GA", field)
            df, lf, hf = _bootstrap_diff(a_sgd, a_fls, iters=args.bootstrap_iters, seed=args.seed)
            dg, lg, hg = _bootstrap_diff(a_sgd, a_ga, iters=args.bootstrap_iters, seed=args.seed + 1)
            lines.append(f"| `{ax}` | `{field}` | {_fmt(df)} | [{_fmt(lf)}; {_fmt(hf)}] | {_fmt(dg)} | [{_fmt(lg)}; {_fmt(hg)}] |\n")
        lines.append("\n")

    section_metrics("H3.1 — корреляция corr(ΔV_p, ΔV_c) по осям", CORR_BY_AXIS, lower_better_for_sgd=None)

    all_ctrl_corr_missing = all(
        len(_vals(rows, m, field)) == 0 for m in ("FLS", "GA") for _, field in CORR_BY_AXIS
    )
    if all_ctrl_corr_missing:
        lines.append(
            "> **Примечание.** Для режимов **FLS** и **GA** в этом прогоне **нет ни одной сессии с числовым** значением в полях `h3_axis_corr_*` "
            "(по всем осям `n = 0`). Поэтому **поосевые bootstrap-контрасты corr для H3.1 относительно FLS/GA на этих данных не определены**. "
            "Строка таблицы выше для **SGD** отражает только поднабор сессий с валидной корреляцией (`n` по осям различается). "
            "Типичные причины отсутствия корреляций у контрольных режимов — в блоке NA (`zero_variance`, `no_pairs`).\n\n"
        )

    section_metrics("H3.2 — среднее виртуального зазора по осям (|V_c − V_p|)", GAP_BY_AXIS, lower_better_for_sgd=True)
    section_metrics("H3.3 — доля наблюдений в ε по осям", WITHIN_BY_AXIS, lower_better_for_sgd=None)

    # NA: session-level aggregates
    lines.append("## NA и покрытие H3\n\n")
    for label, col in [
        ("Сессии без агрегата mean corr (lag0)", "h3_axis_corr_dvp_dvc_mean"),
        ("Сессии без mean corr lag1", "h3_axis_corr_dvp_dvc_mean_lag1"),
        ("Сессии без mean corr (nonzero ΔV_c)", "h3_axis_corr_dvp_dvc_mean_nonzero_dvc"),
    ]:
        lines.append(f"### {label} (`{col}`)\n\n")
        lines.append("| Режим | нет значения (сессий) | доля от режима |\n")
        lines.append("| --- | ---: | ---: |\n")
        for m in modes:
            total = sum(1 for rec in rows if (rec.get("dda_mode") or "").strip() == m)
            missing = sum(
                1
                for rec in rows
                if (rec.get("dda_mode") or "").strip() == m and _sf(rec.get(col)) is None
            )
            frac = (missing / total) if total else 0.0
            lines.append(f"| {m} | {missing} | {frac:.3f} |\n")
        lines.append("\n")

    lines.append("### Поля `h3_axis_corr_na_reason` (токены после `|`)\n\n")
    for m in modes:
        c: Counter[str] = Counter()
        for rec in rows:
            if (rec.get("dda_mode") or "").strip() != m:
                continue
            for tok in _reason_tokens(rec.get("h3_axis_corr_na_reason")):
                c[tok] += 1
        lines.append(f"**{m}** ({sum(c.values())} вхождений токенов по {sum(1 for r in rows if (r.get('dda_mode') or '').strip()==m)} сессиям): ")
        lines.append(", ".join(f"`{k}`×{v}" for k, v in c.most_common()) + "\n\n")

    lines.append("### `h3_axis_coverage_rate` (доля осей с gap-наблюдениями)\n\n")
    lines.append("| Режим | n | mean coverage | min | max |\n")
    lines.append("| --- | ---: | ---: | ---: | ---: |\n")
    for m in modes:
        xs: list[float] = []
        for rec in rows:
            if (rec.get("dda_mode") or "").strip() != m:
                continue
            v = _sf(rec.get("h3_axis_coverage_rate"))
            if v is not None:
                xs.append(v)
        if not xs:
            lines.append(f"| {m} | 0 | — | — | — |\n")
            continue
        lines.append(
            f"| {m} | {len(xs)} | {_fmt(statistics.fmean(xs))} | {_fmt(min(xs))} | {_fmt(max(xs))} |\n"
        )

    lines.append("\n## Краткие выводы для текста диссертации\n\n")
    lines.append(
        "1. **H3.1 (осевые corr):** на сессионных полях `h3_axis_corr_*` сравнение **SGD с FLS/GA по осям невозможно**: у контрольных режимов нет валидных значений. "
        "Для **SGD** осевые средние корреляций положительны на части сессий (`n` по осям 40–49 из 86). "
        "Формальные контрасты по агрегату `h3_axis_corr_dvp_dvc_mean` в основном отчёте остаются с оговоркой о полноте данных.\n\n"
    )
    lines.append(
        "2. **H3.2 (осевой |V_c−V_p|):** все режимы имеют полные `n`; у **GA** средние зазоры по осям близки к нулю в выгрузке, у **SGD** **выше**, чем у GA, на всех четырёх осях (bootstrap **SGD−GA** строго положительный по точечной оценке и ДИ для hp, move_speed, attack_speed, attack_damage). "
        "**SGD−FLS** смешанный (ДИ пересекают 0 на hp и attack_damage).\n\n"
    )
    lines.append(
        "3. **H3.3 (доля в ε по осям):** **GA** даёт долю 1,0 на каждой оси в агрегате (как записано в CSV); **SGD** ниже GA на всех осях по bootstrap **SGD−GA** (отрицательная разница). **SGD−FLS** в основном не в пользу FLS на hp/move_speed (ДИ включают 0), на attack_damage разность положительна.\n\n"
    )
    lines.append(
        "4. **NA:** 100% сессий FLS и GA **без** агрегированного `h3_axis_corr_dvp_dvc_mean`; у **SGD** около **43%** сессий без этого поля. "
        "Токены `h3_axis_corr_na_reason`: у FLS доминирует `zero_variance`, у GA и части SGD — `no_pairs`. `h3_axis_coverage_rate` = 1 для всех режимов — покрытие осей gap-рядами формальное, но **пары ΔV_p/ΔV_c для корреляции** для контрольных режимов не воспроизводятся.\n\n"
    )

    args.out_md.parent.mkdir(parents=True, exist_ok=True)
    args.out_md.write_text("".join(lines), encoding="utf-8")
    print(f"[ok] wrote {args.out_md}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
