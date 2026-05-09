"""
Session-level aggregates for hypotheses H1–H4 from PostHog JSONL exports.

Hypothesis mapping (see also module constant `_RU_HYPOTHESES_H1_H3_MD` written into summary):
- H1: session mean of per-axis |challenge01 - skill01| (MAE_align from axis_*_abs_error).
- H2: primary thesis check is H2.3 — smoothness of the skill–challenge misalignment trajectory
  (fewer/larger-threshold jumps in e_i = challenge01 - skill01, plus mirrored abs_error jump stats).
  H2.1 (actuator / m_i jumps) and H2.2 (target challenge01 jumps) are auxiliary decompositions.
- H3: axis-first virtual power/challenge compensation on 4 planes
  (`hp`, `move_speed`, `attack_speed`, `attack_damage`): coupling corr(ΔV_p_i, ΔV_c_i),
  gap |V_c_i - V_p_i|, epsilon band rate per-axis. Legacy total metrics remain sanity diagnostics.
- H4: degradation/recovery events and recovery_elapsed_seconds.

All survivors are included by default (`player_body` is recorded per session but not used
to filter). Use `--only-mode`, time windows, or schema filters when you need subsets.

Schema version is the event property `telemetry_schema_version` (see `TelemetrySampleBuilder`
in the mod). To restrict exports on the server to `telemetry_schema_version >= N`, use
`tools/export_data_scripts/posthog_export_all.ps1 -TelemetrySchemaVersion N` (PostHog `gte` filter); this script
can still apply `--min-schema-version` when mixing JSONL from several exports.
"""

# Injected into Russian summary_h1_h4.md (hypothesis wording H1–H3; H2 split into three smoothness checks).
_RU_HYPOTHESES_H1_H3_MD = """### H1 — точность согласования (alignment)

Проверяется, насколько близко по осям предъявляемый вызов соответствует оцениваемому навыку игрока.

**Обозначения:** `challenge01_i(t)` — нормализованный вызов по оси `i`, `skill01_i(t)` — нормализованный навык, ошибка `e_i(t) = challenge01_i(t) - skill01_i(t)`.

**Формальная проверка:** `E[|e_i(t)|]_SGD < E[|e_i(t)|]_FLS` и то же относительно GA.

**В этом скрипте:** по телеметрии считается сессионный `MAE_align` — среднее значений `axis_*_abs_error` по всем осям сэмпла (прокси к средней величине рассогласования по осям).

### H2 — плавность траектории рассогласования skill–challenge

**Связь с H1:** если **H1** про **величину** среднего рассогласования (`E[|e_i(t)|]` по осям), то **H2** про **динамику** этого рассогласования: насколько редко траектория ошибки между навыком и предъявляемым вызовом делает **резкие скачки** (а не про «средний уровень» ошибки).

**Обозначения:** `e_i(t) = challenge01_i(t) - skill01_i(t)` как в H1; `m_i(t)` — применяемый множитель сложности по оси `i`; `τ_jump` — порог резкого шага (поле `tau_jump` в сэмпле).

#### H2.3 — основная формулировка гипотезы H2

Проверяется **плавность траектории ошибки** `e_i(t)`: не «рвётся» ли баланс skill–challenge по времени.

Простыми словами: «не переводит ли система игрока резко из зоны «слишком легко» в «слишком тяжело» и обратно?»

**Формальная проверка (ядро H2):** доля скачков `P(|Δe_i(t)| > τ_jump)` у SGD ниже, чем у FLS и GA; в том же духе — ниже среднее и p95 `|Δe_i(t)|`. Дополнительно: зеркальные показатели по изменению **абсолютной** ошибки по осям (`|Δ abs_error| > τ_jump`, среднее/p95 `|Δ(|e_i(t)|)|`), чтобы видеть стабильность «близости» вызова к навыку.

**В этом скрипте:** `h2_error_jump_rate_tau`, `h2_abs_error_jump_rate_tau`, `h2_mean_abs_delta_error`, `h2_p95_abs_delta_error`, `h2_mean_abs_delta_abs_error`, `h2_p95_abs_delta_abs_error`.

#### H2.1 — вспомогательная диагностика (актуаторы, множители `m_i`)

Показывает, насколько резко меняются **реально применяемые** множители сложности — «дёргаются ли ручки» независимо от того, как ведёт себя ошибка `e_i`.

**Формальная проверка (вторичная):** `P(|Δm_i(t)| > τ_jump)` и связанные средние/p95 по `|Δm|` — **не подменяют** основную H2, но помогают локализовать причину, если H2.3 плохая при «тихих» актуаторах или наоборот.

**В этом скрипте:** `h2_jump_rate_tau`, `h2_jump_rate_flag`, среднее и p95 `|Δm|` по сессии.

#### H2.2 — вспомогательная диагностика (целевой `challenge01`)

Показывает плавность **целевого** уровня вызова, который система стремится выставить, до отображения в актуаторах.

**Формальная проверка (вторичная):** `P(|Δchallenge01_i(t)| > τ_jump)`, среднее и p95 `|Δchallenge01|` — для ответа на вопрос, не идёт ли рваность **из целевой кривой**, а не из траектории `e_i`.

**В этом скрипте:** `h2_challenge_jump_rate_tau`, `h2_mean_abs_delta_challenge01`, `h2_p95_abs_delta_challenge01`.

#### Как интерпретировать H2.3 vs H2.1–H2.2

- **H2.3 плохая** — игрок ощущает рваную динамику баланса skill–challenge по времени (частые большие шаги ошибки).
- **H2.1 плохая при хорошей H2.3** — траектория ошибки сглажена, но актуаторы всё ещё делают крупные шаги (имеет смысл смотреть лимиты и маппинг в актуаторы).
- **H2.2 плохая при хорошей H2.3** — целевой вызов скачет, но ошибка `e_i` остаётся относительно гладкой (возможен компенсирующий `skill01` или иной эффект — разбирать по данным).

**Важно:** при чтении отчёта и диссертации **основной** критерий плавности DDA в рамках H2 — **H2.3**. **H2.1** и **H2.2** — **вспомогательные** разложения; они **не заменяют** формулировку H2, а объясняют, **где** возникает резкость, если траектория `e_i` или актуаторы ведут себя по-разному.

### H3 — компенсация роста мощности сборки

Проверяется, успевает ли DDA повышать предъявляемую сложность при росте силы сборки игрока и удерживать виртуальную сложность близко к виртуальной мощности.

**Формальная проверка (axis-first, как в разделе 2.5):** `corr(ΔV_p_i, ΔV_c_i)_SGD > 0`, `E[|V_c_i - V_p_i|]_SGD ≤ ε_v`, `E[|V_c_i - V_p_i|]_SGD < E[|V_c_i - V_p_i|]_GA` по 4 осям (`hp`, `move_speed`, `attack_speed`, `attack_damage`), где `i` — ось.

**В этом скрипте:** основными метриками H3 считаются осевые агрегаты `h3_axis_corr_dvp_dvc_mean`, `h3_axis_corr_dvp_dvc_mean_lag1`, `h3_axis_corr_dvp_dvc_mean_nonzero_dvc`, `h3_axis_mean_virtual_gap_abs`, `h3_axis_within_epsilon_rate`. Legacy total-поля (`h3_corr_dvp_dvc`, `h3_mean_virtual_gap_abs`, `h3_within_epsilon_rate`) остаются sanity-check и не подменяют axis-first критерий.

---
"""

import argparse
import csv
import glob
import json
import math
import os
import random
import statistics
from dataclasses import dataclass, field
from datetime import datetime, timedelta, timezone


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


AXIS_TO_PLANE = {
    "attack_damage": "damage",
    "attack_speed": "attackSpeed",
    "max_health": "hp",
    "move_speed": "moveSpeed",
}

H3_PLANES = ("damage", "attackSpeed", "hp", "moveSpeed")
H3_VIRTUAL_AXES = ("hp", "move_speed", "attack_speed", "attack_damage")
H3_LAG_STEPS = 1
H3_NONZERO_DVC_EPS = 1e-6
H3_RESPONSE_DVP_TAU = 1e-6
H3_NO_PAIRS = "no_pairs"
H3_ZERO_VARIANCE = "zero_variance"
H3_ALL_ZERO_DVC = "all_zero_dvc"
H3_NO_DECISION_STEPS = "no_decision_steps"


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


def _pearson_corr_with_lag(xs: list[float], ys: list[float], lag_steps: int) -> float | None:
    if lag_steps <= 0:
        return _pearson_corr(xs, ys)
    if len(xs) <= lag_steps or len(ys) <= lag_steps:
        return None
    return _pearson_corr(xs[:-lag_steps], ys[lag_steps:])


def _pearson_corr_nonzero_dvc(
    dvp: list[float],
    dvc: list[float],
    eps: float = H3_NONZERO_DVC_EPS,
) -> float | None:
    pairs = []
    for x, y in zip(dvp, dvc):
        if abs(y) > eps:
            pairs.append((x, y))
    if len(pairs) < 2:
        return None
    xs = [x for x, _ in pairs]
    ys = [y for _, y in pairs]
    return _pearson_corr(xs, ys)


def _sign(value: float, eps: float = 1e-9) -> int:
    if value > eps:
        return 1
    if value < -eps:
        return -1
    return 0


def _sign_match_rate(
    dvp: list[float],
    dvc: list[float],
    tau: float = H3_RESPONSE_DVP_TAU,
) -> float | None:
    pairs = [(x, y) for x, y in zip(dvp, dvc) if abs(x) > tau]
    if not pairs:
        return None
    return statistics.fmean(1 if _sign(x) == _sign(y) else 0 for x, y in pairs)


def _response_gain(
    dvp: list[float],
    dvc: list[float],
    tau: float = H3_RESPONSE_DVP_TAU,
) -> float | None:
    ratios = [y / x for x, y in zip(dvp, dvc) if abs(x) > tau]
    return statistics.fmean(ratios) if ratios else None


def _as_bool(value) -> bool | None:
    if value is None:
        return None
    if isinstance(value, bool):
        return value
    if isinstance(value, (int, float)):
        return bool(value)
    text = str(value).strip().lower()
    if text in {"true", "1", "yes"}:
        return True
    if text in {"false", "0", "no"}:
        return False
    return None


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
    duration_seconds: float | None = None

    # H1 alignment: mean |e_i| over axes (e_i = challenge01_i - skill01_i); see axis_*_abs_error.
    h1_mae_align: float | None = None
    samples_count: int = 0
    axis_obs_count: int = 0

    # H2.1 actuators: auxiliary — applied multiplier jump stats (not the primary H2 hypothesis).
    h2_jump_rate_flag: float | None = None
    h2_jump_rate_tau: float | None = None
    h2_mean_abs_delta_multiplier: float | None = None
    h2_p95_abs_delta_multiplier: float | None = None
    h2_max_abs_delta_multiplier: float | None = None
    h2_mean_abs_delta_theta: float | None = None
    h2_mean_abs_relative_delta_multiplier: float | None = None

    # H2.2 target challenge: auxiliary — target challenge01 trajectory (distinct from H2.1).
    h2_challenge_jump_rate_tau: float | None = None
    h2_mean_abs_delta_challenge01: float | None = None
    h2_p95_abs_delta_challenge01: float | None = None

    # H2.3 skill–challenge mismatch: primary H2 — smoothness of e_i trajectory and abs_error jumps.
    h2_error_jump_rate_tau: float | None = None
    h2_abs_error_jump_rate_tau: float | None = None
    h2_mean_abs_delta_error: float | None = None
    h2_p95_abs_delta_error: float | None = None
    h2_mean_abs_delta_abs_error: float | None = None
    h2_p95_abs_delta_abs_error: float | None = None
    tau_jump: float | None = None

    # H3 build-power compensation: V_p/V_c and gap; axis fields below are implementation diagnostics, not formal H3.
    h3_axis_mean_abs_error: float | None = None
    h3_axis_within_stable_rate: float | None = None
    h3_axis_corr_delta_skill_challenge: float | None = None
    h3_axis_corr_dvp_dvc_mean: float | None = None
    h3_axis_corr_hp: float | None = None
    h3_axis_corr_move_speed: float | None = None
    h3_axis_corr_attack_speed: float | None = None
    h3_axis_corr_attack_damage: float | None = None
    h3_axis_corr_dvp_dvc_mean_lag1: float | None = None
    h3_axis_corr_dvp_dvc_mean_nonzero_dvc: float | None = None
    h3_axis_sign_match_rate_mean: float | None = None
    h3_axis_response_gain_mean: float | None = None
    h3_axis_mean_virtual_gap_abs: float | None = None
    h3_axis_mean_gap_hp: float | None = None
    h3_axis_mean_gap_move_speed: float | None = None
    h3_axis_mean_gap_attack_speed: float | None = None
    h3_axis_mean_gap_attack_damage: float | None = None
    h3_axis_within_epsilon_rate: float | None = None
    h3_axis_within_epsilon_rate_hp: float | None = None
    h3_axis_within_epsilon_rate_move_speed: float | None = None
    h3_axis_within_epsilon_rate_attack_speed: float | None = None
    h3_axis_within_epsilon_rate_attack_damage: float | None = None
    h3_axis_coverage_rate: float | None = None
    h3_axis_corr_na_reason: str = ""
    h3_axis_corr_lag1_na_reason: str = ""
    h3_axis_corr_nonzero_na_reason: str = ""
    h3_corr_dvp_dvc: float | None = None
    h3_corr_dvp_dvc_lag1: float | None = None
    h3_corr_dvp_dvc_nonzero_dvc: float | None = None
    h3_mean_virtual_gap_abs: float | None = None
    epsilon_v: float | None = None
    h3_within_epsilon_rate: float | None = None

    # H4 degradation / recovery: recovery_elapsed_seconds from dda_recovery events.
    h4_recovery_times: list[float] = field(default_factory=list)
    h4_mean_recovery_seconds: float | None = None
    h4_recovery_events_count: int = 0
    h4_degradation_events_count: int = 0
    h4_degradation_sample_rate: float | None = None
    h4_max_degradation_signal: float | None = None
    h4_max_time_above_050_seconds: float | None = None
    h4_max_time_above_060_seconds: float | None = None
    h4_max_time_above_070_seconds: float | None = None


def _join_h3_na_reasons(reasons: list[str]) -> str:
    if not reasons:
        return ""
    seen: list[str] = []
    for reason in reasons:
        if reason and reason not in seen:
            seen.append(reason)
    return "|".join(seen)


def _detect_axes(props: dict) -> list[str]:
    axes = set()
    for k in props.keys():
        if k.startswith("axis_") and k.endswith("_abs_error"):
            axes.add(k[len("axis_") : -len("_abs_error")])
    return sorted(axes)


def _percentile(values: list[float], q: float) -> float | None:
    if not values:
        return None
    if len(values) == 1:
        return values[0]
    xs = sorted(values)
    pos = (len(xs) - 1) * q
    lo = int(math.floor(pos))
    hi = int(math.ceil(pos))
    if lo == hi:
        return xs[lo]
    return xs[lo] + ((xs[hi] - xs[lo]) * (pos - lo))


def _to_int(v):
    try:
        if v is None:
            return None
        return int(v)
    except Exception:
        return None


def _parse_iso_ts(s: str) -> datetime | None:
    """
    Parses PostHog timestamp like '2026-04-27T18:53:28.386000+00:00'.
    Returns timezone-aware datetime in UTC (or the timestamp's own tz).
    """
    if not s:
        return None
    try:
        # Python 3.11: supports "+00:00" offsets.
        return datetime.fromisoformat(str(s))
    except Exception:
        return None


def _summary_strings(lang: str) -> dict[str, str]:
    lang = (lang or "ru").strip().lower()
    if lang not in {"ru", "en"}:
        lang = "ru"

    if lang == "en":
        return {
            "title": "# Pilot check: H1-H4 on PostHog export",
            "filter_schema": "- filter: telemetry_schema_version >= {v}",
            "filter_axis_obs": "- filter: axis_obs_count >= {v}",
            "files": "- files: {n}",
            "parsed_events": "- parsed_events: {n}",
            "bad_json_lines": "- bad_json_lines: {n}",
            "sessions_total": "- sessions_total: {n}",
            "sessions_by_mode": "Sessions by mode",
            "contrast_na": "\n- Contrast SGD - {other}: NA (insufficient sessions)\n",
            "contrast_line": "\n- Contrast SGD - {other}: mean_diff={md} 95%CI[{lo}, {hi}] -> {verdict}\n",
            "h4_missingness": "H4 missingness / episode counts",
            "h4_missingness_line": "- {mode}: sessions={sessions} degradation_start_sessions={deg} recovery_sessions={rec}\n",
            "notes": "Notes / limitations",
            "note_pilot": "- This is a *pilot* check: all statistics are computed on **session-level aggregates**; bootstrap CI is shown only as an uncertainty visualization for tiny n.\n",
            "note_h2_split": "- H2 primary: H2.3 (error-trajectory jumps); H2.1/H2.2 are auxiliary actuators/target-challenge diagnostics; binary zero jump-rate alone is not evidence *for* smoothness.\n",
            "note_h4": "- For H4, we rely on `dda_recovery` events and `recovery_elapsed_seconds`; if sessions contain no recovery events, H4 is not testable there.\n",
            "note_h3": "- H3 primary metrics are axis-based over the four fixed planes: `damage`, `attackSpeed`, `hp`, `moveSpeed`; legacy virtual gap metrics are diagnostic only.\n",
            "verdict_supports": "supports",
            "verdict_not": "does_not_support",
        }

    return {
        "title": "# Пилотная проверка гипотез H1–H4 по экспорту PostHog",
        "filter_schema": "- фильтр: telemetry_schema_version >= {v}",
        "filter_axis_obs": "- фильтр: axis_obs_count >= {v}",
        "files": "- файлов: {n}",
        "parsed_events": "- разобрано событий: {n}",
        "bad_json_lines": "- битых JSON-строк: {n}",
        "sessions_total": "- всего сессий: {n}",
        "sessions_by_mode": "Сессии по режимам",
        "contrast_na": "\n- Сравнение SGD − {other}: недостаточно сессий\n",
        "contrast_line": "\n- Сравнение SGD − {other}: mean_diff={md} 95% ДИ[{lo}, {hi}] -> {verdict}\n",
        "h4_missingness": "H4: покрытие данных и число эпизодов",
        "h4_missingness_line": "- {mode}: сессий={sessions} с_degradation_start={deg} с_recovery={rec}\n",
        "notes": "Замечания и ограничения",
        "note_pilot": "- Это *пилотная* проверка: все статистики считаются по **агрегатам на уровне сессии**; bootstrap 95% ДИ показан только как визуализация неопределённости при маленьком n.\n",
        "note_h2_split": "- Основная H2 — H2.3 (плавность траектории ошибки skill–challenge); H2.1 и H2.2 — вспомогательные разложения; нулевая доля скачков сама по себе не доказывает «плавность».\n",
        "note_h4": "- Для H4 используются события `dda_recovery` и поле `recovery_elapsed_seconds`; если в сессии нет recovery-событий, H4 там формально не проверяется.\n",
        "note_h3": "- Основные метрики H3 — осевые в четырёх плоскостях: `damage`, `attackSpeed`, `hp`, `moveSpeed`; legacy-метрики virtual gap остаются диагностическими.\n",
        "verdict_supports": "поддерживает",
        "verdict_not": "не_поддерживает",
    }


def _metric_title(lang: str, key: str) -> str:
    lang = (lang or "ru").strip().lower()
    if lang not in {"ru", "en"}:
        lang = "ru"

    en = {
        "h1_mae_align": "H1 alignment accuracy (session MAE_align = mean(axis_*_abs_error))",
        "h2_jump_rate_flag": "H2.1 actuator smoothness (JumpRate by axis_*_is_jump)",
        "h2_jump_rate_tau": "H2.1 actuator smoothness (JumpRate by |axis_*_delta_multiplier| > tau_jump)",
        "h2_mean_abs_delta_multiplier": "H2.1 actuator smoothness (mean abs delta_multiplier per session)",
        "h2_p95_abs_delta_multiplier": "H2.1 actuator smoothness (p95 abs delta_multiplier per session)",
        "h2_mean_abs_delta_theta": "H2.1 actuator smoothness (mean abs delta_theta per session)",
        "h2_mean_abs_relative_delta_multiplier": "H2.1 actuator smoothness (mean abs relative delta_multiplier per session)",
        "h2_challenge_jump_rate_tau": "H2.2 challenge smoothness (JumpRate by |delta_challenge01| > tau_jump)",
        "h2_mean_abs_delta_challenge01": "H2.2 challenge smoothness (mean abs delta_challenge01 per session)",
        "h2_p95_abs_delta_challenge01": "H2.2 challenge smoothness (p95 abs delta_challenge01 per session)",
        "h2_error_jump_rate_tau": "H2.3 alignment-error smoothness (JumpRate by |delta error| > tau_jump)",
        "h2_abs_error_jump_rate_tau": "H2.3 alignment-error smoothness (JumpRate by |delta abs_error| > tau_jump)",
        "h2_mean_abs_delta_error": "H2.3 alignment-error smoothness (mean abs delta error per session)",
        "h2_p95_abs_delta_error": "H2.3 alignment-error smoothness (p95 abs delta error per session)",
        "h2_mean_abs_delta_abs_error": "H2.3 alignment-error smoothness (mean abs delta abs_error per session)",
        "h2_p95_abs_delta_abs_error": "H2.3 alignment-error smoothness (p95 abs delta abs_error per session)",
        "h3_axis_mean_abs_error": "H3 axis closeness (4-plane mean axis abs error: damage/attackSpeed/hp/moveSpeed)",
        "h3_axis_within_stable_rate": "H3 axis stability (rate axis abs error <= epsilon_stable)",
        "h3_axis_corr_delta_skill_challenge": "H3 axis coupling (corr(delta_skill_i, delta_challenge_i) within 4 planes)",
        "h3_axis_corr_dvp_dvc_mean": "H3 axis virtual coupling (mean corr(delta Vp_i, delta Vc_i))",
        "h3_axis_corr_dvp_dvc_mean_lag1": "H3 axis virtual coupling (mean corr(delta Vp_i(t), delta Vc_i(t+1)))",
        "h3_axis_corr_dvp_dvc_mean_nonzero_dvc": "H3 axis virtual coupling on non-zero delta Vc_i ticks",
        "h3_axis_sign_match_rate_mean": "H3 axis response direction (mean sign match rate)",
        "h3_axis_response_gain_mean": "H3 axis response gain (mean delta Vc_i / delta Vp_i)",
        "h3_axis_mean_virtual_gap_abs": "H3 axis virtual closeness (mean |Vc_i - Vp_i|)",
        "h3_axis_within_epsilon_rate": "H3 axis virtual closeness (rate within epsilon_v)",
        "h3_axis_coverage_rate": "H3 axis telemetry coverage rate",
        "h3_corr_dvp_dvc": "Legacy H3 coupling (corr(delta_virtual_power, delta_virtual_challenge) per session)",
        "h3_corr_dvp_dvc_lag1": "Legacy H3 coupling (corr(delta_virtual_power(t), delta_virtual_challenge(t+1)) per session)",
        "h3_corr_dvp_dvc_nonzero_dvc": "Legacy H3 coupling on non-zero delta_virtual_challenge ticks",
        "h3_mean_virtual_gap_abs": "Legacy H3 closeness (mean virtual_gap_abs per session)",
        "h3_within_epsilon_rate": "Legacy H3 closeness (rate is_within_virtual_gap_epsilon per session)",
        "h4_degradation_sample_rate": "H4 degradation presence (degraded sample rate)",
        "h4_max_degradation_signal": "H4 degradation signal (max degradation_signal per session)",
        "h4_max_time_above_060_seconds": "H4 diagnostics (max consecutive seconds above 0.60)",
        "h4_max_time_above_070_seconds": "H4 diagnostics (max consecutive seconds above 0.70)",
        "h4_mean_recovery_seconds": "H4 recovery (mean recovery_elapsed_seconds per session)",
    }

    ru = {
        "h1_mae_align": "H1 — точность согласования (MAE_align сессии = mean(axis_*_abs_error))",
        "h2_jump_rate_flag": "H2.1 — плавность актуаторов (JumpRate по axis_*_is_jump)",
        "h2_jump_rate_tau": "H2.1 — плавность актуаторов (JumpRate по |axis_*_delta_multiplier| > tau_jump)",
        "h2_mean_abs_delta_multiplier": "H2.1 — плавность актуаторов (среднее |delta_multiplier| за сессию)",
        "h2_p95_abs_delta_multiplier": "H2.1 — плавность актуаторов (p95 |delta_multiplier| за сессию)",
        "h2_mean_abs_delta_theta": "H2.1 — плавность актуаторов (среднее |delta_theta| за сессию)",
        "h2_mean_abs_relative_delta_multiplier": "H2.1 — плавность актуаторов (среднее |relative_delta_multiplier| за сессию)",
        "h2_challenge_jump_rate_tau": "H2.2 — плавность вызова (JumpRate по |delta_challenge01| > tau_jump)",
        "h2_mean_abs_delta_challenge01": "H2.2 — плавность вызова (среднее |delta_challenge01| за сессию)",
        "h2_p95_abs_delta_challenge01": "H2.2 — плавность вызова (p95 |delta_challenge01| за сессию)",
        "h2_error_jump_rate_tau": "H2.3 — плавность рассогласования (JumpRate по |delta error| > tau_jump), error=challenge01−skill01",
        "h2_abs_error_jump_rate_tau": "H2.3 — плавность рассогласования (JumpRate по |delta abs_error| > tau_jump)",
        "h2_mean_abs_delta_error": "H2.3 — плавность рассогласования (среднее |delta error| за сессию)",
        "h2_p95_abs_delta_error": "H2.3 — плавность рассогласования (p95 |delta error| за сессию)",
        "h2_mean_abs_delta_abs_error": "H2.3 — плавность рассогласования (среднее |delta abs_error| за сессию)",
        "h2_p95_abs_delta_abs_error": "H2.3 — плавность рассогласования (p95 |delta abs_error| за сессию)",
        "h3_axis_mean_abs_error": "H3 — близость по осям (среднее axis abs error по 4 плоскостям: damage/attackSpeed/hp/moveSpeed)",
        "h3_axis_within_stable_rate": "H3 — стабильность по осям (доля axis abs error <= epsilon_stable)",
        "h3_axis_corr_delta_skill_challenge": "H3 — сцепление по осям (corr(delta_skill_i, delta_challenge_i) внутри 4 плоскостей)",
        "h3_axis_corr_dvp_dvc_mean": "H3 — осевое сцепление мощности и вызова (mean corr(delta Vp_i, delta Vc_i))",
        "h3_axis_corr_dvp_dvc_mean_lag1": "H3 — осевое сцепление с лагом 1 (mean corr(delta Vp_i(t), delta Vc_i(t+1)))",
        "h3_axis_corr_dvp_dvc_mean_nonzero_dvc": "H3 — осевое сцепление на шагах с ненулевым delta Vc_i",
        "h3_axis_mean_virtual_gap_abs": "H3 — осевая близость (mean |Vc_i - Vp_i|)",
        "h3_axis_within_epsilon_rate": "H3 — осевая близость (доля внутри epsilon_v)",
        "h3_axis_coverage_rate": "H3 — покрытие осевой телеметрии",
        "h3_corr_dvp_dvc": "Legacy H3 — сцепление (corr(delta_virtual_power, delta_virtual_challenge) на сессию)",
        "h3_corr_dvp_dvc_lag1": "Legacy H3 — сцепление с лагом 1 (corr(delta_virtual_power(t), delta_virtual_challenge(t+1)))",
        "h3_corr_dvp_dvc_nonzero_dvc": "Legacy H3 — сцепление на шагах с ненулевым delta_virtual_challenge",
        "h3_mean_virtual_gap_abs": "Legacy H3 — близость (mean virtual_gap_abs на сессию)",
        "h3_within_epsilon_rate": "Legacy H3 — близость (доля is_within_virtual_gap_epsilon на сессию)",
        "h4_degradation_sample_rate": "H4 — наличие деградации (доля сэмплов с is_degraded)",
        "h4_max_degradation_signal": "H4 — сигнал деградации (max degradation_signal на сессию)",
        "h4_max_time_above_060_seconds": "H4 — диагностика (макс. секунд подряд degradation_signal > 0.60)",
        "h4_max_time_above_070_seconds": "H4 — диагностика (макс. секунд подряд degradation_signal > 0.70)",
        "h4_mean_recovery_seconds": "H4 — восстановление (mean recovery_elapsed_seconds на сессию)",
    }

    return en[key] if lang == "en" else ru[key]


def main() -> int:
    ap = argparse.ArgumentParser(
        description="Aggregate PostHog JSONL telemetry to session metrics for hypotheses H1-H4."
    )
    ap.add_argument(
        "inputs",
        nargs="+",
        help="JSONL files or globs, e.g. tools/export_data_scripts/posthog_exports/ALL_events_*.jsonl",
    )
    ap.add_argument(
        "--out-dir",
        default=os.path.join("tools", "export_data_scripts", "posthog_exports", "hypotheses_results"),
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
        "--min-schema-version",
        type=int,
        default=0,
        help="Exclude sessions with telemetry_schema_version below this value.",
    )
    ap.add_argument(
        "--min-axis-obs",
        type=int,
        default=0,
        help="Exclude sessions with fewer axis observations.",
    )
    ap.add_argument(
        "--only-mode",
        default="",
        help="Restrict analysis to sessions with this dda_mode (e.g. SGD).",
    )
    ap.add_argument(
        "--last-minutes",
        type=int,
        default=0,
        help="Restrict analysis to sessions whose latest event timestamp is within the last N minutes (UTC).",
    )
    ap.add_argument(
        "--since-iso",
        default="",
        help="Restrict analysis to sessions whose latest event timestamp is >= this ISO timestamp (e.g. 2026-04-27T18:00:00+00:00).",
    )
    ap.add_argument(
        "--until-iso",
        default="",
        help="Restrict analysis to sessions whose latest event timestamp is <= this ISO timestamp (default: now, UTC).",
    )
    ap.add_argument(
        "--summary-lang",
        choices=("ru", "en"),
        default="ru",
        help="Language for summary_h1_h4.md (default: ru).",
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
    sess_abs_delta_multiplier: dict[str, list[float]] = {}
    sess_abs_delta_theta: dict[str, list[float]] = {}
    sess_abs_relative_delta_multiplier: dict[str, list[float]] = {}
    sess_challenge_jump_tau: dict[str, list[int]] = {}
    sess_abs_delta_challenge01: dict[str, list[float]] = {}
    sess_error_jump_tau: dict[str, list[int]] = {}
    sess_abs_error_jump_tau: dict[str, list[int]] = {}
    sess_abs_delta_error: dict[str, list[float]] = {}
    sess_abs_delta_abs_error: dict[str, list[float]] = {}
    sess_tau_jump: dict[str, float] = {}
    sess_eps_v: dict[str, float] = {}
    sess_eps_stable: dict[str, float] = {}
    sess_h3_axis_abs_errors: dict[str, list[float]] = {}
    sess_h3_axis_within_stable: dict[str, list[int]] = {}
    sess_h3_delta_skill: dict[str, list[float]] = {}
    sess_h3_delta_challenge: dict[str, list[float]] = {}
    sess_h3_axis_dvp: dict[tuple[str, str], list[float]] = {}
    sess_h3_axis_dvc: dict[tuple[str, str], list[float]] = {}
    sess_h3_axis_gap: dict[tuple[str, str], list[float]] = {}
    sess_h3_axis_within_eps: dict[tuple[str, str], list[int]] = {}
    sess_h3_decision_steps: dict[str, int] = {}
    sess_dvp: dict[str, list[float]] = {}
    sess_dvc: dict[str, list[float]] = {}
    sess_vgap: dict[str, list[float]] = {}
    sess_within_eps: dict[str, list[int]] = {}
    sess_degradation_flags: dict[str, list[int]] = {}
    sess_degradation_signals: dict[str, list[float]] = {}
    sess_time_above_050: dict[str, list[float]] = {}
    sess_time_above_060: dict[str, list[float]] = {}
    sess_time_above_070: dict[str, list[float]] = {}
    sess_degradation_events: dict[str, int] = {}
    sess_mode: dict[str, str] = {}
    sess_participant: dict[str, str] = {}
    sess_schema: dict[str, int] = {}
    sess_duration: dict[str, float] = {}
    sess_recovery: dict[str, list[float]] = {}
    sess_player_body: dict[str, str] = {}
    prev_axis_signals: dict[tuple[str, str], tuple[float, float]] = {}
    prev_axis_multiplier: dict[tuple[str, str], float] = {}
    sess_latest_ts: dict[str, datetime] = {}

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

            ts = _parse_iso_ts(obj.get("timestamp") or "")
            if ts is not None:
                prev = sess_latest_ts.get(session_id)
                if prev is None or ts > prev:
                    sess_latest_ts[session_id] = ts

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

            eps_stable = _safe_float(props.get("epsilon_stable"))
            if eps_stable is not None and session_id not in sess_eps_stable:
                sess_eps_stable[session_id] = eps_stable

            if event == "dda_session_end" or (props.get("event_kind") == "session_end"):
                duration = _safe_float(props.get("duration_seconds"))
                if duration is None:
                    duration = _safe_float(props.get("run_elapsed_seconds"))
                if duration is not None:
                    sess_duration[session_id] = duration

            if event == "dda_sample":
                axes = _detect_axes(props)
                if axes:
                    for ax in axes:
                        plane = str(props.get(f"axis_{ax}_plane") or AXIS_TO_PLANE.get(ax, ax))
                        if plane not in H3_PLANES:
                            continue

                        ae = _safe_float(props.get(f"axis_{ax}_abs_error"))
                        if ae is not None:
                            sess_axes_abs_errors.setdefault(session_id, []).append(ae)
                            sess_h3_axis_abs_errors.setdefault(session_id, []).append(ae)
                            stable = eps_stable if eps_stable is not None else 0.10
                            sess_h3_axis_within_stable.setdefault(session_id, []).append(
                                1 if ae <= stable else 0
                            )

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
                        if dm is not None:
                            sess_abs_delta_multiplier.setdefault(session_id, []).append(abs(dm))

                        dtheta = _safe_float(props.get(f"axis_{ax}_delta_theta"))
                        multiplier = _safe_float(props.get(f"axis_{ax}_multiplier"))
                        key = (session_id, ax)
                        if dtheta is None and multiplier is not None and key in prev_axis_multiplier:
                            prev = max(0.0001, prev_axis_multiplier[key])
                            dtheta = math.log(max(0.0001, multiplier)) - math.log(prev)
                        if dtheta is not None and key in prev_axis_multiplier:
                            sess_abs_delta_theta.setdefault(session_id, []).append(abs(dtheta))

                        rel_delta = _safe_float(props.get(f"axis_{ax}_relative_delta_multiplier"))
                        if rel_delta is None and dm is not None and key in prev_axis_multiplier:
                            rel_delta = dm / max(0.0001, prev_axis_multiplier[key])
                        if rel_delta is not None and key in prev_axis_multiplier:
                            sess_abs_relative_delta_multiplier.setdefault(session_id, []).append(abs(rel_delta))

                        skill = _safe_float(props.get(f"axis_{ax}_skill01"))
                        challenge = _safe_float(props.get(f"axis_{ax}_challenge01"))
                        error = _safe_float(props.get(f"axis_{ax}_error"))
                        if error is None and skill is not None and challenge is not None:
                            error = challenge - skill
                        dskill = _safe_float(props.get(f"axis_{ax}_delta_skill01"))
                        dchallenge = _safe_float(props.get(f"axis_{ax}_delta_challenge01"))
                        if (dskill is None or dchallenge is None) and skill is not None and challenge is not None and key in prev_axis_signals:
                            prev_skill, prev_challenge = prev_axis_signals[key]
                            dskill = skill - prev_skill
                            dchallenge = challenge - prev_challenge
                        if dchallenge is not None:
                            abs_dchallenge = abs(dchallenge)
                            sess_abs_delta_challenge01.setdefault(session_id, []).append(abs_dchallenge)
                            if tau_jump is not None:
                                sess_challenge_jump_tau.setdefault(session_id, []).append(
                                    1 if abs_dchallenge > tau_jump else 0
                                )
                        if dskill is not None and dchallenge is not None:
                            derror = dchallenge - dskill
                            abs_derror = abs(derror)
                            sess_abs_delta_error.setdefault(session_id, []).append(abs_derror)
                            if tau_jump is not None:
                                sess_error_jump_tau.setdefault(session_id, []).append(
                                    1 if abs_derror > tau_jump else 0
                                )
                            if error is not None:
                                prev_error = error - derror
                                abs_delta_abs_error = abs(abs(error) - abs(prev_error))
                                sess_abs_delta_abs_error.setdefault(session_id, []).append(abs_delta_abs_error)
                                if tau_jump is not None:
                                    sess_abs_error_jump_tau.setdefault(session_id, []).append(
                                        1 if abs_delta_abs_error > tau_jump else 0
                                    )
                        if dskill is not None and dchallenge is not None and key in prev_axis_signals:
                            sess_h3_delta_skill.setdefault(session_id, []).append(dskill)
                            sess_h3_delta_challenge.setdefault(session_id, []).append(dchallenge)

                        if skill is not None and challenge is not None:
                            prev_axis_signals[key] = (skill, challenge)
                        if multiplier is not None:
                            prev_axis_multiplier[key] = multiplier

                h3_is_step = _as_bool(props.get("h3_is_decision_step"))
                use_h3_virtual_sample = schema_v is None or schema_v < 6 or h3_is_step is True
                if use_h3_virtual_sample:
                    if h3_is_step is True or schema_v is None or schema_v < 6:
                        sess_h3_decision_steps[session_id] = sess_h3_decision_steps.get(session_id, 0) + 1
                    dvp = _safe_float(props.get("delta_virtual_power"))
                    dvc = _safe_float(props.get("delta_virtual_challenge"))
                    if dvp is not None:
                        sess_dvp.setdefault(session_id, []).append(dvp)
                    if dvc is not None:
                        sess_dvc.setdefault(session_id, []).append(dvc)

                    vgap = _safe_float(props.get("virtual_gap_abs"))
                    if vgap is not None:
                        sess_vgap.setdefault(session_id, []).append(vgap)

                    within_bool = _as_bool(props.get("is_within_virtual_gap_epsilon"))
                    if within_bool is not None:
                        sess_within_eps.setdefault(session_id, []).append(
                            1 if within_bool else 0
                        )

                    axis_within_fallback = _as_bool(props.get("is_within_virtual_gap_axes_epsilon"))
                    for axis in H3_VIRTUAL_AXES:
                        axis_key = (session_id, axis)
                        axis_dvp = _safe_float(props.get(f"delta_virtual_power_{axis}"))
                        axis_dvc = _safe_float(props.get(f"delta_virtual_challenge_{axis}"))
                        axis_gap = _safe_float(props.get(f"virtual_gap_{axis}_abs"))
                        axis_within = _as_bool(props.get(f"is_within_virtual_gap_{axis}_epsilon"))
                        if axis_within is None:
                            axis_within = axis_within_fallback
                        if axis_dvp is not None:
                            sess_h3_axis_dvp.setdefault(axis_key, []).append(axis_dvp)
                        if axis_dvc is not None:
                            sess_h3_axis_dvc.setdefault(axis_key, []).append(axis_dvc)
                        if axis_gap is not None:
                            sess_h3_axis_gap.setdefault(axis_key, []).append(axis_gap)
                        if axis_within is not None and axis_gap is not None:
                            sess_h3_axis_within_eps.setdefault(axis_key, []).append(
                                1 if axis_within else 0
                            )

                degraded = props.get("is_degraded")
                if degraded is not None:
                    sess_degradation_flags.setdefault(session_id, []).append(
                        1 if bool(degraded) else 0
                    )

                degradation_signal = _safe_float(props.get("degradation_signal"))
                if degradation_signal is not None:
                    sess_degradation_signals.setdefault(session_id, []).append(degradation_signal)

                for key_name, target in (
                    ("degradation_signal_above_050_seconds", sess_time_above_050),
                    ("degradation_signal_above_060_seconds", sess_time_above_060),
                    ("degradation_signal_above_070_seconds", sess_time_above_070),
                ):
                    value = _safe_float(props.get(key_name))
                    if value is not None:
                        target.setdefault(session_id, []).append(value)

            if event == "dda_recovery":
                rt = _safe_float(props.get("recovery_elapsed_seconds"))
                if rt is not None:
                    sess_recovery.setdefault(session_id, []).append(rt)

            if event == "dda_degradation_start":
                sess_degradation_events[session_id] = sess_degradation_events.get(session_id, 0) + 1

    # Build session rows
    sessions: list[SessionMetrics] = []
    all_session_ids = (
        sess_mode.keys()
        | sess_axes_abs_errors.keys()
        | sess_recovery.keys()
        | sess_player_body.keys()
        | sess_h3_axis_abs_errors.keys()
        | {sid for sid, _axis in sess_h3_axis_gap.keys()}
        | {sid for sid, _axis in sess_h3_axis_dvp.keys()}
        | sess_degradation_flags.keys()
        | sess_degradation_events.keys()
    )
    for session_id in sorted(all_session_ids):
        m = SessionMetrics(session_id=session_id)
        m.dda_mode = sess_mode.get(session_id, "")
        m.participant_id = sess_participant.get(session_id, "")
        m.telemetry_schema_version = sess_schema.get(session_id)
        m.player_body = sess_player_body.get(session_id, "")
        m.duration_seconds = sess_duration.get(session_id)

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

        abs_delta_multiplier = sess_abs_delta_multiplier.get(session_id, [])
        if abs_delta_multiplier:
            m.h2_mean_abs_delta_multiplier = statistics.fmean(abs_delta_multiplier)
            m.h2_p95_abs_delta_multiplier = _percentile(abs_delta_multiplier, 0.95)
            m.h2_max_abs_delta_multiplier = max(abs_delta_multiplier)

        abs_delta_theta = sess_abs_delta_theta.get(session_id, [])
        if abs_delta_theta:
            m.h2_mean_abs_delta_theta = statistics.fmean(abs_delta_theta)

        abs_relative_delta = sess_abs_relative_delta_multiplier.get(session_id, [])
        if abs_relative_delta:
            m.h2_mean_abs_relative_delta_multiplier = statistics.fmean(abs_relative_delta)

        abs_delta_challenge = sess_abs_delta_challenge01.get(session_id, [])
        if abs_delta_challenge:
            m.h2_mean_abs_delta_challenge01 = statistics.fmean(abs_delta_challenge)
            m.h2_p95_abs_delta_challenge01 = _percentile(abs_delta_challenge, 0.95)

        challenge_jump_tau = sess_challenge_jump_tau.get(session_id, [])
        if challenge_jump_tau:
            m.h2_challenge_jump_rate_tau = statistics.fmean(challenge_jump_tau)

        abs_delta_error = sess_abs_delta_error.get(session_id, [])
        if abs_delta_error:
            m.h2_mean_abs_delta_error = statistics.fmean(abs_delta_error)
            m.h2_p95_abs_delta_error = _percentile(abs_delta_error, 0.95)

        error_jump_tau = sess_error_jump_tau.get(session_id, [])
        if error_jump_tau:
            m.h2_error_jump_rate_tau = statistics.fmean(error_jump_tau)

        abs_delta_abs_error = sess_abs_delta_abs_error.get(session_id, [])
        if abs_delta_abs_error:
            m.h2_mean_abs_delta_abs_error = statistics.fmean(abs_delta_abs_error)
            m.h2_p95_abs_delta_abs_error = _percentile(abs_delta_abs_error, 0.95)

        abs_error_jump_tau = sess_abs_error_jump_tau.get(session_id, [])
        if abs_error_jump_tau:
            m.h2_abs_error_jump_rate_tau = statistics.fmean(abs_error_jump_tau)

        h3_axis_errors = sess_h3_axis_abs_errors.get(session_id, [])
        if h3_axis_errors:
            m.h3_axis_mean_abs_error = statistics.fmean(h3_axis_errors)

        h3_axis_within = sess_h3_axis_within_stable.get(session_id, [])
        if h3_axis_within:
            m.h3_axis_within_stable_rate = statistics.fmean(h3_axis_within)

        dskill_axis = sess_h3_delta_skill.get(session_id, [])
        dchallenge_axis = sess_h3_delta_challenge.get(session_id, [])
        n_axis_delta = min(len(dskill_axis), len(dchallenge_axis))
        if n_axis_delta >= 2:
            m.h3_axis_corr_delta_skill_challenge = _pearson_corr(
                dskill_axis[:n_axis_delta],
                dchallenge_axis[:n_axis_delta],
            )

        axis_corrs: dict[str, float] = {}
        axis_corrs_lag1: dict[str, float] = {}
        axis_corrs_nonzero_dvc: dict[str, float] = {}
        axis_sign_match_rates: dict[str, float] = {}
        axis_response_gains: dict[str, float] = {}
        axis_mean_gaps: dict[str, float] = {}
        axis_within_rates: dict[str, float] = {}
        axis_gaps: list[float] = []
        axis_within_values: list[int] = []
        covered_axes = 0
        corr_na_reasons: list[str] = []
        corr_lag1_na_reasons: list[str] = []
        corr_nonzero_na_reasons: list[str] = []
        for axis in H3_VIRTUAL_AXES:
            axis_key = (session_id, axis)
            axis_dvp = sess_h3_axis_dvp.get(axis_key, [])
            axis_dvc = sess_h3_axis_dvc.get(axis_key, [])
            axis_n = min(len(axis_dvp), len(axis_dvc))
            axis_nonzero_n = sum(
                1 for value in axis_dvc[:axis_n] if abs(value) > H3_NONZERO_DVC_EPS
            )
            if axis_n >= 2:
                corr = _pearson_corr(axis_dvp[:axis_n], axis_dvc[:axis_n])
                if corr is not None:
                    axis_corrs[axis] = corr
                else:
                    corr_na_reasons.append(H3_ZERO_VARIANCE)
                corr_lag1 = _pearson_corr_with_lag(
                    axis_dvp[:axis_n], axis_dvc[:axis_n], lag_steps=H3_LAG_STEPS
                )
                if corr_lag1 is not None:
                    axis_corrs_lag1[axis] = corr_lag1
                else:
                    corr_lag1_na_reasons.append(H3_ZERO_VARIANCE)
                corr_nonzero_dvc = _pearson_corr_nonzero_dvc(
                    axis_dvp[:axis_n], axis_dvc[:axis_n], eps=H3_NONZERO_DVC_EPS
                )
                if corr_nonzero_dvc is not None:
                    axis_corrs_nonzero_dvc[axis] = corr_nonzero_dvc
                else:
                    if axis_nonzero_n < 2:
                        corr_nonzero_na_reasons.append(H3_ALL_ZERO_DVC)
                    else:
                        corr_nonzero_na_reasons.append(H3_ZERO_VARIANCE)
                sign_match = _sign_match_rate(axis_dvp[:axis_n], axis_dvc[:axis_n])
                if sign_match is not None:
                    axis_sign_match_rates[axis] = sign_match
                gain = _response_gain(axis_dvp[:axis_n], axis_dvc[:axis_n])
                if gain is not None:
                    axis_response_gains[axis] = gain
            else:
                corr_na_reasons.append(H3_NO_PAIRS)
                corr_lag1_na_reasons.append(H3_NO_PAIRS)
                corr_nonzero_na_reasons.append(H3_NO_PAIRS)
            gaps = sess_h3_axis_gap.get(axis_key, [])
            if gaps:
                covered_axes += 1
                axis_gaps.extend(gaps)
                axis_mean_gaps[axis] = statistics.fmean(gaps)
            within_axis = sess_h3_axis_within_eps.get(axis_key, [])
            if within_axis:
                axis_within_rates[axis] = statistics.fmean(within_axis)
            axis_within_values.extend(within_axis)

        if axis_corrs:
            m.h3_axis_corr_dvp_dvc_mean = statistics.fmean(axis_corrs.values())
            m.h3_axis_corr_hp = axis_corrs.get("hp")
            m.h3_axis_corr_move_speed = axis_corrs.get("move_speed")
            m.h3_axis_corr_attack_speed = axis_corrs.get("attack_speed")
            m.h3_axis_corr_attack_damage = axis_corrs.get("attack_damage")
        else:
            m.h3_axis_corr_na_reason = _join_h3_na_reasons(corr_na_reasons)
        if axis_corrs_lag1:
            m.h3_axis_corr_dvp_dvc_mean_lag1 = statistics.fmean(axis_corrs_lag1.values())
        else:
            m.h3_axis_corr_lag1_na_reason = _join_h3_na_reasons(corr_lag1_na_reasons)
        if axis_corrs_nonzero_dvc:
            m.h3_axis_corr_dvp_dvc_mean_nonzero_dvc = statistics.fmean(axis_corrs_nonzero_dvc.values())
        else:
            m.h3_axis_corr_nonzero_na_reason = _join_h3_na_reasons(corr_nonzero_na_reasons)
        if sess_h3_decision_steps.get(session_id, 0) == 0:
            m.h3_axis_corr_na_reason = _join_h3_na_reasons([m.h3_axis_corr_na_reason, H3_NO_DECISION_STEPS])
            m.h3_axis_corr_lag1_na_reason = _join_h3_na_reasons([m.h3_axis_corr_lag1_na_reason, H3_NO_DECISION_STEPS])
            m.h3_axis_corr_nonzero_na_reason = _join_h3_na_reasons([m.h3_axis_corr_nonzero_na_reason, H3_NO_DECISION_STEPS])
        if axis_sign_match_rates:
            m.h3_axis_sign_match_rate_mean = statistics.fmean(axis_sign_match_rates.values())
        if axis_response_gains:
            m.h3_axis_response_gain_mean = statistics.fmean(axis_response_gains.values())
        if axis_gaps:
            m.h3_axis_mean_virtual_gap_abs = statistics.fmean(axis_gaps)
            m.h3_axis_mean_gap_hp = axis_mean_gaps.get("hp")
            m.h3_axis_mean_gap_move_speed = axis_mean_gaps.get("move_speed")
            m.h3_axis_mean_gap_attack_speed = axis_mean_gaps.get("attack_speed")
            m.h3_axis_mean_gap_attack_damage = axis_mean_gaps.get("attack_damage")
        if axis_within_values:
            m.h3_axis_within_epsilon_rate = statistics.fmean(axis_within_values)
            m.h3_axis_within_epsilon_rate_hp = axis_within_rates.get("hp")
            m.h3_axis_within_epsilon_rate_move_speed = axis_within_rates.get("move_speed")
            m.h3_axis_within_epsilon_rate_attack_speed = axis_within_rates.get("attack_speed")
            m.h3_axis_within_epsilon_rate_attack_damage = axis_within_rates.get("attack_damage")
        if covered_axes:
            m.h3_axis_coverage_rate = covered_axes / len(H3_VIRTUAL_AXES)

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
            m.h3_corr_dvp_dvc_lag1 = _pearson_corr_with_lag(
                dvp[:n], dvc[:n], lag_steps=H3_LAG_STEPS
            )
            m.h3_corr_dvp_dvc_nonzero_dvc = _pearson_corr_nonzero_dvc(
                dvp[:n], dvc[:n], eps=H3_NONZERO_DVC_EPS
            )

        rec = sess_recovery.get(session_id, [])
        m.h4_recovery_times = rec
        m.h4_recovery_events_count = len(rec)
        if rec:
            m.h4_mean_recovery_seconds = statistics.fmean(rec)

        m.h4_degradation_events_count = sess_degradation_events.get(session_id, 0)

        degradation_flags = sess_degradation_flags.get(session_id, [])
        if degradation_flags:
            m.h4_degradation_sample_rate = statistics.fmean(degradation_flags)

        degradation_signals = sess_degradation_signals.get(session_id, [])
        if degradation_signals:
            m.h4_max_degradation_signal = max(degradation_signals)

        if sess_time_above_050.get(session_id):
            m.h4_max_time_above_050_seconds = max(sess_time_above_050[session_id])
        if sess_time_above_060.get(session_id):
            m.h4_max_time_above_060_seconds = max(sess_time_above_060[session_id])
        if sess_time_above_070.get(session_id):
            m.h4_max_time_above_070_seconds = max(sess_time_above_070[session_id])

        # approximate samples count: infer from axis obs count / axes count if possible
        # (purely informational)
        m.samples_count = 0
        sessions.append(m)

    if args.min_schema_version > 0:
        sessions = [
            s for s in sessions
            if s.telemetry_schema_version is not None and s.telemetry_schema_version >= args.min_schema_version
        ]

    if args.min_axis_obs > 0:
        sessions = [s for s in sessions if s.axis_obs_count >= args.min_axis_obs]

    if args.only_mode:
        wanted = args.only_mode.strip()
        sessions = [s for s in sessions if (s.dda_mode or "").strip() == wanted]

    # Time filtering by session latest timestamp
    since_dt = _parse_iso_ts(args.since_iso) if args.since_iso else None
    until_dt = _parse_iso_ts(args.until_iso) if args.until_iso else None
    if until_dt is None:
        until_dt = datetime.now(timezone.utc)
    if args.last_minutes and args.last_minutes > 0:
        since_dt = until_dt - timedelta(minutes=int(args.last_minutes))

    if since_dt is not None or until_dt is not None:
        def _in_window(s: SessionMetrics) -> bool:
            ts = sess_latest_ts.get(s.session_id)
            if ts is None:
                return False
            if since_dt is not None and ts < since_dt:
                return False
            if until_dt is not None and ts > until_dt:
                return False
            return True

        sessions = [s for s in sessions if _in_window(s)]

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
                "duration_seconds",
                "axis_obs_count",
                "h1_mae_align",
                "tau_jump",
                "h2_jump_rate_flag",
                "h2_jump_rate_tau",
                "h2_mean_abs_delta_multiplier",
                "h2_p95_abs_delta_multiplier",
                "h2_max_abs_delta_multiplier",
                "h2_mean_abs_delta_theta",
                "h2_mean_abs_relative_delta_multiplier",
                "h2_challenge_jump_rate_tau",
                "h2_mean_abs_delta_challenge01",
                "h2_p95_abs_delta_challenge01",
                "h2_error_jump_rate_tau",
                "h2_abs_error_jump_rate_tau",
                "h2_mean_abs_delta_error",
                "h2_p95_abs_delta_error",
                "h2_mean_abs_delta_abs_error",
                "h2_p95_abs_delta_abs_error",
                "epsilon_v",
                "h3_axis_mean_abs_error",
                "h3_axis_within_stable_rate",
                "h3_axis_corr_delta_skill_challenge",
                "h3_axis_corr_dvp_dvc_mean",
                "h3_axis_corr_dvp_dvc_mean_lag1",
                "h3_axis_corr_dvp_dvc_mean_nonzero_dvc",
                "h3_axis_sign_match_rate_mean",
                "h3_axis_response_gain_mean",
                "h3_axis_corr_hp",
                "h3_axis_corr_move_speed",
                "h3_axis_corr_attack_speed",
                "h3_axis_corr_attack_damage",
                "h3_axis_mean_virtual_gap_abs",
                "h3_axis_mean_gap_hp",
                "h3_axis_mean_gap_move_speed",
                "h3_axis_mean_gap_attack_speed",
                "h3_axis_mean_gap_attack_damage",
                "h3_axis_within_epsilon_rate",
                "h3_axis_within_epsilon_rate_hp",
                "h3_axis_within_epsilon_rate_move_speed",
                "h3_axis_within_epsilon_rate_attack_speed",
                "h3_axis_within_epsilon_rate_attack_damage",
                "h3_axis_coverage_rate",
                "h3_axis_corr_na_reason",
                "h3_axis_corr_lag1_na_reason",
                "h3_axis_corr_nonzero_na_reason",
                "h3_corr_dvp_dvc",
                "h3_corr_dvp_dvc_lag1",
                "h3_corr_dvp_dvc_nonzero_dvc",
                "h3_mean_virtual_gap_abs",
                "h3_within_epsilon_rate",
                "h4_degradation_events_count",
                "h4_degradation_sample_rate",
                "h4_max_degradation_signal",
                "h4_max_time_above_050_seconds",
                "h4_max_time_above_060_seconds",
                "h4_max_time_above_070_seconds",
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
                    "" if s.duration_seconds is None else s.duration_seconds,
                    s.axis_obs_count,
                    "" if s.h1_mae_align is None else s.h1_mae_align,
                    "" if s.tau_jump is None else s.tau_jump,
                    "" if s.h2_jump_rate_flag is None else s.h2_jump_rate_flag,
                    "" if s.h2_jump_rate_tau is None else s.h2_jump_rate_tau,
                    "" if s.h2_mean_abs_delta_multiplier is None else s.h2_mean_abs_delta_multiplier,
                    "" if s.h2_p95_abs_delta_multiplier is None else s.h2_p95_abs_delta_multiplier,
                    "" if s.h2_max_abs_delta_multiplier is None else s.h2_max_abs_delta_multiplier,
                    "" if s.h2_mean_abs_delta_theta is None else s.h2_mean_abs_delta_theta,
                    "" if s.h2_mean_abs_relative_delta_multiplier is None else s.h2_mean_abs_relative_delta_multiplier,
                    "" if s.h2_challenge_jump_rate_tau is None else s.h2_challenge_jump_rate_tau,
                    "" if s.h2_mean_abs_delta_challenge01 is None else s.h2_mean_abs_delta_challenge01,
                    "" if s.h2_p95_abs_delta_challenge01 is None else s.h2_p95_abs_delta_challenge01,
                    "" if s.h2_error_jump_rate_tau is None else s.h2_error_jump_rate_tau,
                    "" if s.h2_abs_error_jump_rate_tau is None else s.h2_abs_error_jump_rate_tau,
                    "" if s.h2_mean_abs_delta_error is None else s.h2_mean_abs_delta_error,
                    "" if s.h2_p95_abs_delta_error is None else s.h2_p95_abs_delta_error,
                    "" if s.h2_mean_abs_delta_abs_error is None else s.h2_mean_abs_delta_abs_error,
                    "" if s.h2_p95_abs_delta_abs_error is None else s.h2_p95_abs_delta_abs_error,
                    "" if s.epsilon_v is None else s.epsilon_v,
                    "" if s.h3_axis_mean_abs_error is None else s.h3_axis_mean_abs_error,
                    "" if s.h3_axis_within_stable_rate is None else s.h3_axis_within_stable_rate,
                    "" if s.h3_axis_corr_delta_skill_challenge is None else s.h3_axis_corr_delta_skill_challenge,
                    "" if s.h3_axis_corr_dvp_dvc_mean is None else s.h3_axis_corr_dvp_dvc_mean,
                    "" if s.h3_axis_corr_dvp_dvc_mean_lag1 is None else s.h3_axis_corr_dvp_dvc_mean_lag1,
                    "" if s.h3_axis_corr_dvp_dvc_mean_nonzero_dvc is None else s.h3_axis_corr_dvp_dvc_mean_nonzero_dvc,
                    "" if s.h3_axis_sign_match_rate_mean is None else s.h3_axis_sign_match_rate_mean,
                    "" if s.h3_axis_response_gain_mean is None else s.h3_axis_response_gain_mean,
                    "" if s.h3_axis_corr_hp is None else s.h3_axis_corr_hp,
                    "" if s.h3_axis_corr_move_speed is None else s.h3_axis_corr_move_speed,
                    "" if s.h3_axis_corr_attack_speed is None else s.h3_axis_corr_attack_speed,
                    "" if s.h3_axis_corr_attack_damage is None else s.h3_axis_corr_attack_damage,
                    "" if s.h3_axis_mean_virtual_gap_abs is None else s.h3_axis_mean_virtual_gap_abs,
                    "" if s.h3_axis_mean_gap_hp is None else s.h3_axis_mean_gap_hp,
                    "" if s.h3_axis_mean_gap_move_speed is None else s.h3_axis_mean_gap_move_speed,
                    "" if s.h3_axis_mean_gap_attack_speed is None else s.h3_axis_mean_gap_attack_speed,
                    "" if s.h3_axis_mean_gap_attack_damage is None else s.h3_axis_mean_gap_attack_damage,
                    "" if s.h3_axis_within_epsilon_rate is None else s.h3_axis_within_epsilon_rate,
                    "" if s.h3_axis_within_epsilon_rate_hp is None else s.h3_axis_within_epsilon_rate_hp,
                    "" if s.h3_axis_within_epsilon_rate_move_speed is None else s.h3_axis_within_epsilon_rate_move_speed,
                    "" if s.h3_axis_within_epsilon_rate_attack_speed is None else s.h3_axis_within_epsilon_rate_attack_speed,
                    "" if s.h3_axis_within_epsilon_rate_attack_damage is None else s.h3_axis_within_epsilon_rate_attack_damage,
                    "" if s.h3_axis_coverage_rate is None else s.h3_axis_coverage_rate,
                    s.h3_axis_corr_na_reason,
                    s.h3_axis_corr_lag1_na_reason,
                    s.h3_axis_corr_nonzero_na_reason,
                    "" if s.h3_corr_dvp_dvc is None else s.h3_corr_dvp_dvc,
                    "" if s.h3_corr_dvp_dvc_lag1 is None else s.h3_corr_dvp_dvc_lag1,
                    "" if s.h3_corr_dvp_dvc_nonzero_dvc is None else s.h3_corr_dvp_dvc_nonzero_dvc,
                    "" if s.h3_mean_virtual_gap_abs is None else s.h3_mean_virtual_gap_abs,
                    "" if s.h3_within_epsilon_rate is None else s.h3_within_epsilon_rate,
                    s.h4_degradation_events_count,
                    "" if s.h4_degradation_sample_rate is None else s.h4_degradation_sample_rate,
                    "" if s.h4_max_degradation_signal is None else s.h4_max_degradation_signal,
                    "" if s.h4_max_time_above_050_seconds is None else s.h4_max_time_above_050_seconds,
                    "" if s.h4_max_time_above_060_seconds is None else s.h4_max_time_above_060_seconds,
                    "" if s.h4_max_time_above_070_seconds is None else s.h4_max_time_above_070_seconds,
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
    s = _summary_strings(args.summary_lang)

    def _mean(mode: str, attr: str) -> float | None:
        xs = _vals(mode, attr)
        return statistics.fmean(xs) if xs else None

    def _median(mode: str, attr: str) -> float | None:
        xs = _vals(mode, attr)
        return statistics.median(xs) if xs else None

    def _n(mode: str, attr: str) -> int:
        return len(_vals(mode, attr))

    def _mode_means(attr: str) -> str:
        parts = []
        for mode in ("FLS", "GA", "SGD"):
            n = _n(mode, attr)
            mean = _mean(mode, attr)
            if n == 0:
                parts.append(f"{mode}=нет данных")
            else:
                parts.append(f"{mode}={_fmt(mean)} (n={n})")
        return "; ".join(parts)

    def _contrast(attr: str, other: str, *, lower_is_better: bool):
        a = _vals("SGD", attr)
        b = _vals(other, attr)
        if not a or not b:
            return None, None, None, "нет данных"
        mean_diff, lo, hi = _bootstrap_ci_diff(
            a, b, iters=args.bootstrap_iters, seed=args.seed
        )
        if mean_diff is None:
            return mean_diff, lo, hi, "нет данных"
        better = mean_diff < 0 if lower_is_better else mean_diff > 0
        crosses_zero = (lo is None or hi is None) or (lo <= 0 <= hi)
        if crosses_zero:
            verdict = "примерно на уровне, статистически неустойчиво"
        elif better:
            verdict = "SGD лучше"
        else:
            verdict = "SGD хуже"
        return mean_diff, lo, hi, verdict

    def _contrast_text(attr: str, other: str, *, lower_is_better: bool) -> str:
        mean_diff, lo, hi, verdict = _contrast(attr, other, lower_is_better=lower_is_better)
        if mean_diff is None:
            return f"SGD vs {other}: {verdict}"
        return (
            f"SGD vs {other}: разница средних {_fmt(mean_diff)}, "
            f"95% ДИ [{_fmt(lo)}, {_fmt(hi)}] — {verdict}"
        )

    def _short_verdict(attr: str, *, lower_is_better: bool) -> str:
        verdicts = []
        for other in ("FLS", "GA"):
            mean_diff, lo, hi, verdict = _contrast(
                attr, other, lower_is_better=lower_is_better
            )
            if mean_diff is None:
                verdicts.append(f"с {other} сравнить нельзя")
                continue
            if lo is not None and hi is not None and lo <= 0 <= hi:
                verdicts.append(f"с {other} различие неустойчиво")
                continue
            if (mean_diff < 0 and lower_is_better) or (mean_diff > 0 and not lower_is_better):
                verdicts.append(f"лучше {other}")
            else:
                verdicts.append(f"хуже {other}")
        return "; ".join(verdicts)

    def _write_metric_line(f, label: str, attr: str):
        f.write(f"- {label}: {_mode_means(attr)}.\n")

    def _write_contrasts(f, attr: str, *, lower_is_better: bool):
        f.write(f"  - {_contrast_text(attr, 'FLS', lower_is_better=lower_is_better)}.\n")
        f.write(f"  - {_contrast_text(attr, 'GA', lower_is_better=lower_is_better)}.\n")

    h4_recovery_by_mode = {
        mode: sum(1 for row in by_mode.get(mode, []) if row.h4_recovery_events_count > 0)
        for mode in modes
    }
    h4_degradation_by_mode = {
        mode: sum(1 for row in by_mode.get(mode, []) if row.h4_degradation_events_count > 0)
        for mode in modes
    }
    h3_na_diag_by_mode = {}
    for mode in modes:
        rows = by_mode.get(mode, [])
        h3_na_diag_by_mode[mode] = {
            "lag0": sum(1 for row in rows if row.h3_axis_corr_dvp_dvc_mean is None),
            "lag1": sum(1 for row in rows if row.h3_axis_corr_dvp_dvc_mean_lag1 is None),
            "nonzero": sum(1 for row in rows if row.h3_axis_corr_dvp_dvc_mean_nonzero_dvc is None),
            "no_decision": sum(1 for row in rows if H3_NO_DECISION_STEPS in (row.h3_axis_corr_na_reason or "")),
            "zero_variance": sum(1 for row in rows if H3_ZERO_VARIANCE in (row.h3_axis_corr_na_reason or "")),
            "no_pairs": sum(1 for row in rows if H3_NO_PAIRS in (row.h3_axis_corr_na_reason or "")),
            "all_zero_dvc": sum(1 for row in rows if H3_ALL_ZERO_DVC in (row.h3_axis_corr_nonzero_na_reason or "")),
        }

    def _positive_condition_text(mode: str, attr: str) -> str:
        mean = _mean(mode, attr)
        if mean is None:
            return "нет данных"
        return "выполнено" if mean > 0 else "не выполнено"

    # Write a human-readable summary first. The full per-session data remains in CSV.
    md_path = os.path.join(out_dir, "summary_h1_h4.md")
    with open(md_path, "w", encoding="utf-8") as f:
        f.write("# Пилотная проверка гипотез H1–H6 по экспорту PostHog\n\n")
        f.write("Этот файл — короткий человекочитаемый отчёт по формулировкам из раздела 2.5 магистерской работы. Полные численные данные по каждой сессии лежат рядом в `session_metrics_h1_h4.csv`.\n\n")

        f.write("## Формулировки гипотез H1–H3\n\n")
        f.write(_RU_HYPOTHESES_H1_H3_MD)

        f.write("## Что проверяли (в этом прогоне)\n\n")
        f.write(
            "- **H1:** сессионный `MAE_align` и bootstrap SGD − FLS / SGD − GA (см. раздел «H1» ниже).\n"
        )
        f.write(
            "- **H2:** основная проверка — **H2.3** (плавность траектории ошибки `e_i`, меньше скачков `|Δe|>τ_jump` и связанные средние/p95); **H2.1** и **H2.2** — вспомогательные слои (актуаторы `m_i`, целевой `challenge01`), чтобы локализовать источник резкости.\n"
        )
        f.write(
            "- **H3 (axis-first):** основная проверка строится по 4 осям (`hp`, `move_speed`, `attack_speed`, `attack_damage`) и включает три подпроверки: (1) скорость реакции `corr(ΔV_p_i, ΔV_c_i)` (в т.ч. lag1), (2) точность `|V_c_i - V_p_i|`, (3) удержание в норме (доля внутри `ε_v`). Legacy total-поля используются только как sanity-check.\n"
        )
        f.write("- H4, восстановление после деградации: проверяем, быстрее ли SGD возвращает игру в стабильное состояние после провала перформанса. Формальная проверка: после ухудшения перформанса `T_recovery` у SGD ниже, чем у FLS и GA.\n")
        f.write("- H5, воспринимаемая справедливость: проверяем, кажется ли игрокам SGD более честным режимом сложности. Формальная проверка: субъективная оценка справедливости выше у SGD.\n")
        f.write("- H6, непрерывность вызова: проверяем, ощущается ли сложность в SGD более цельной и менее рваной. Формальная проверка: субъективная оценка непрерывности кривой сложности выше у SGD.\n\n")

        f.write("## Короткий вывод\n\n")
        f.write(
            f"- H1: по средней ошибке согласования (`MAE_align`) — {_short_verdict('h1_mae_align', lower_is_better=True)}.\n"
        )
        f.write(
            f"- **H2** (основная проверка — H2.3, скачки траектории `e_i`, `P(|Δe|>τ_jump)`): {_short_verdict('h2_error_jump_rate_tau', lower_is_better=True)}.\n"
        )
        f.write(
            f"- H2.1 (вспомогательно, актуаторы `P(|Δm|>τ_jump)`): {_short_verdict('h2_jump_rate_tau', lower_is_better=True)}.\n"
        )
        f.write(
            f"- H2.2 (вспомогательно, целевой `challenge01`, `|Δchallenge01|>τ_jump`): {_short_verdict('h2_challenge_jump_rate_tau', lower_is_better=True)}.\n"
        )
        f.write(
            f"- H3.1 (скорость реакции, axis-first): lag1 `mean corr(ΔV_p_i(t), ΔV_c_i(t+1))_SGD > 0` — {_positive_condition_text('SGD', 'h3_axis_corr_dvp_dvc_mean_lag1')}; "
            f"доп.диагностика: lag0 — {_positive_condition_text('SGD', 'h3_axis_corr_dvp_dvc_mean')}; "
            f"non-zero `ΔV_c_i` — {_positive_condition_text('SGD', 'h3_axis_corr_dvp_dvc_mean_nonzero_dvc')}.\n"
        )
        f.write(
            f"- H3.2 (точность компенсации, axis-first): по среднему осевому `|V_c_i - V_p_i|` — {_short_verdict('h3_axis_mean_virtual_gap_abs', lower_is_better=True)}.\n"
        )
        f.write(
            f"- H3.3 (удержание в норме, axis-first): по доле осевых сэмплов в пределах `ε_v` — {_short_verdict('h3_axis_within_epsilon_rate', lower_is_better=False)}.\n"
        )
        f.write(
            f"- H4: восстановление только по сессиям с recovery-событиями; по среднему `T_recovery` — {_short_verdict('h4_mean_recovery_seconds', lower_is_better=True)}.\n"
        )
        f.write(
            "- H5 и H6: по текущему PostHog-экспорту не проверяются, потому что для них нужны субъективные анкеты игроков о справедливости и непрерывности вызова.\n\n"
        )

        f.write("## Данные\n\n")
        if args.min_schema_version > 0:
            f.write(s["filter_schema"].format(v=args.min_schema_version) + "\n")
        if args.min_axis_obs > 0:
            f.write(s["filter_axis_obs"].format(v=args.min_axis_obs) + "\n")
        if args.since_iso:
            f.write(f"- окно данных: с {args.since_iso}\n")
        if args.until_iso:
            f.write(f"- окно данных: до {args.until_iso}\n")
        f.write(s["files"].format(n=len(files)) + "\n")
        f.write(s["parsed_events"].format(n=seen_events) + "\n")
        f.write(s["bad_json_lines"].format(n=bad_json_lines) + "\n")
        f.write(s["sessions_total"].format(n=len(sessions)) + "\n")
        f.write("- сессии по режимам: " + "; ".join(f"{mode}={len(by_mode[mode])}" for mode in modes) + "\n\n")

        f.write("## H1: точность согласования\n\n")
        f.write("Формулировка: `E[|e_i(t)|]_SGD < E[|e_i(t)|]_FLS` и `E[|e_i(t)|]_SGD < E[|e_i(t)|]_GA`, где `e_i(t)=challenge01_i(t)-skill01_i(t)`. Чем меньше средняя абсолютная ошибка, тем точнее DDA удерживает вызов около текущей оценки навыка.\n\n")
        _write_metric_line(f, "`E[|e_i(t)|]`, средняя абсолютная ошибка", "h1_mae_align")
        _write_contrasts(f, "h1_mae_align", lower_is_better=True)
        f.write("\nКомментарий: если доверительный интервал пересекает 0, это не уверенное отличие, а только направление в пилотных данных.\n\n")

        f.write("## H2: плавность траектории рассогласования\n\n")
        f.write(
            "Определения и связь H1/H2 — в разделе «Формулировки гипотез H1–H3». **Основная** проверка H2 — подраздел **H2.3**; H2.1 и H2.2 — вспомогательные. Ниже — агрегаты по сессиям и bootstrap-контрасты.\n\n"
        )
        f.write("### H2.3 — основная гипотеза H2 (`e_i`, траектория ошибки)\n\n")
        f.write(
            "Формулировка: меньше доля шагов с `|Δe_i| > τ_jump` для `e_i=challenge01−skill01` у SGD, чем у FLS и GA; зеркально — по `|Δ abs_error| > τ_jump` по осям; ниже средние/p95 шага `|Δe|` и `|Δ(|e|)|`.\n\n"
        )
        _write_metric_line(f, "`P(|Δ e_i| > τ_jump)`", "h2_error_jump_rate_tau")
        _write_metric_line(f, "`P(|Δ abs_error| > τ_jump)` по осям", "h2_abs_error_jump_rate_tau")
        _write_metric_line(f, "Среднее `|Δ e_i|` за сессию", "h2_mean_abs_delta_error")
        _write_metric_line(f, "Среднее `|Δ(|e_i(t)|)|` за сессию", "h2_mean_abs_delta_abs_error")
        _write_contrasts(f, "h2_error_jump_rate_tau", lower_is_better=True)
        _write_contrasts(f, "h2_abs_error_jump_rate_tau", lower_is_better=True)
        f.write("\n")

        f.write("### H2.1 — вспомогательно: актуаторы (`m_i`)\n\n")
        f.write(
            "Доля шагов с `|Δm_i| > τ_jump` по осям, флаг `axis_*_is_jump`, среднее и p95 `|Δm|` — чтобы отделить «дёрганье ручек» от плавности траектории `e_i`.\n\n"
        )
        _write_metric_line(f, "`P(|Δm_i(t)| > τ_jump)` (по `|axis_*_delta_multiplier|`)", "h2_jump_rate_tau")
        _write_metric_line(f, "`P(jump)` по флагу `axis_*_is_jump`", "h2_jump_rate_flag")
        _write_metric_line(f, "Средний размер `|Δm_i(t)|`", "h2_mean_abs_delta_multiplier")
        _write_metric_line(f, "95-й перцентиль `|Δm_i(t)|`", "h2_p95_abs_delta_multiplier")
        _write_contrasts(f, "h2_jump_rate_tau", lower_is_better=True)
        f.write("\n")

        f.write("### H2.2 — вспомогательно: целевой вызов (`challenge01`)\n\n")
        f.write(
            "`P(|Δ challenge01| > τ_jump)`, среднее и p95 `|Δchallenge01|` — насколько скачет **целевая** кривая вызова.\n\n"
        )
        _write_metric_line(f, "`P(|Δ challenge01| > τ_jump)`", "h2_challenge_jump_rate_tau")
        _write_metric_line(f, "Среднее `|Δ challenge01|` за сессию", "h2_mean_abs_delta_challenge01")
        _write_metric_line(f, "p95 `|Δ challenge01|` за сессию", "h2_p95_abs_delta_challenge01")
        _write_contrasts(f, "h2_challenge_jump_rate_tau", lower_is_better=True)
        f.write(
            "\nКомментарий: если **H2.3** выглядит хорошо, а **H2.1** или **H2.2** плохо, траектория ошибки skill–challenge относительно гладкая, но резкость переносится в актуаторы или в целевой `challenge01` — это разные измерения «плавности», не противоречие.\n\n"
        )

        f.write(
            "### Дополнительная диагностика к H2\n\n"
            "Нулевая доля бинарных скачков по множителю у FLS/GA при ненулевом среднем `|Δm|` у SGD часто означает: шаги укладываются в `τ_jump`, но по частоте/величине ведут себя иначе; для **основной** H2 ориентир — метрики **H2.3**.\n\n"
        )

        f.write("## H3: компенсация роста мощности сборки (axis-first)\n\n")
        f.write("Формулировка: при росте осевых компонент `V_p_i` система должна повышать соответствующие компоненты `V_c_i`, сохраняя `V_c_i` близко к `V_p_i`: `corr(ΔV_p_i, ΔV_c_i)_SGD > 0`, `E[|V_c_i - V_p_i|]_SGD <= ε_v` и `E[|V_c_i - V_p_i|]_SGD < E[|V_c_i - V_p_i|]_GA`. Оси schema v5+: `hp`, `move_speed`, `attack_speed`, `attack_damage`.\n\n")
        f.write(
            "### Интерпретация H3 (три подпроверки axis-first)\n\n"
            "- **H3.1 Скорость реакции:** `mean corr(ΔV_p_i(t), ΔV_c_i(t+1))` (lag1) как основной индикатор реакции контура; lag0 и non-zero `ΔV_c_i` — доп.диагностика.\n"
            "- **H3.2 Точность компенсации:** `E[|V_c_i - V_p_i|]` по 4 осям (ниже лучше).\n"
            "- **H3.3 Удержание в норме:** доля осевых сэмплов внутри `ε_v` (выше лучше).\n"
            "- **Legacy total-поля:** вторичный sanity-check, не основной критерий гипотезы H3.\n\n"
        )
        _write_metric_line(f, "`mean corr(ΔV_p_i, ΔV_c_i)` по 4 осям (lag0)", "h3_axis_corr_dvp_dvc_mean")
        _write_metric_line(f, "`mean corr(ΔV_p_i(t), ΔV_c_i(t+1))` по 4 осям (lag1)", "h3_axis_corr_dvp_dvc_mean_lag1")
        _write_metric_line(f, "`mean corr(ΔV_p_i, ΔV_c_i)` по 4 осям на шагах с `|ΔV_c_i|>eps`", "h3_axis_corr_dvp_dvc_mean_nonzero_dvc")
        _write_metric_line(f, "`E[|V_c_i - V_p_i|]`, средний осевой virtual gap", "h3_axis_mean_virtual_gap_abs")
        _write_metric_line(f, "Доля осевых сэмплов в пределах `ε_v`", "h3_axis_within_epsilon_rate")
        _write_metric_line(f, "Покрытие H3 axis fields", "h3_axis_coverage_rate")
        _write_contrasts(f, "h3_axis_mean_virtual_gap_abs", lower_is_better=True)
        f.write(
            "- Диагностика NA по режимам (corr lag0 / lag1 / nonzero): "
            + "; ".join(
                f"{mode}={h3_na_diag_by_mode[mode]['lag0']}/{h3_na_diag_by_mode[mode]['lag1']}/{h3_na_diag_by_mode[mode]['nonzero']}"
                for mode in modes
            )
            + ".\n"
        )
        f.write(
            "- Причины NA (по lag0): "
            + "; ".join(
                f"{mode}: no_decision={h3_na_diag_by_mode[mode]['no_decision']}, no_pairs={h3_na_diag_by_mode[mode]['no_pairs']}, zero_variance={h3_na_diag_by_mode[mode]['zero_variance']}"
                for mode in modes
            )
            + ".\n"
        )
        f.write(
            "- Причина NA для nonzero-метрики: "
            + "; ".join(
                f"{mode}: all_zero_dvc={h3_na_diag_by_mode[mode]['all_zero_dvc']}"
                for mode in modes
            )
            + ".\n"
        )
        f.write("\nLegacy diagnostics (старые total-поля):\n\n")
        _write_metric_line(f, "`corr(ΔV_p_total, ΔV_c_total)` (lag0)", "h3_corr_dvp_dvc")
        _write_metric_line(f, "`corr(ΔV_p_total(t), ΔV_c_total(t+1))` (lag1)", "h3_corr_dvp_dvc_lag1")
        _write_metric_line(f, "`corr(ΔV_p_total, ΔV_c_total)` на шагах с `|ΔV_c_total|>eps`", "h3_corr_dvp_dvc_nonzero_dvc")
        _write_metric_line(f, "`E[|V_c_total - V_p_total|]`, legacy virtual gap", "h3_mean_virtual_gap_abs")
        _write_metric_line(f, "Legacy доля сэмплов в пределах `ε_v`", "h3_within_epsilon_rate")
        f.write(
            "\nКомментарий: lag0 может быть занижен/отрицателен из-за дискретного шага решения и частых тиков с `ΔV_c≈0`; "
            "поэтому для интерпретации H3 дополнительно смотрите lag1 и фильтр non-zero `ΔV_c`.\n\n"
        )

        f.write("## H4: деградация и восстановление\n\n")
        f.write("Формулировка: после эпизодов ухудшения игры — роста входящего урона, смертности или TTK — время возврата в стабильную область у SGD должно быть меньше, чем у FLS и GA: `T_recovery_SGD < T_recovery_FLS` и `T_recovery_SGD < T_recovery_GA`.\n\n")
        _write_metric_line(f, "Доля сэмплов с деградацией", "h4_degradation_sample_rate")
        _write_metric_line(f, "Максимальный сигнал деградации", "h4_max_degradation_signal")
        _write_metric_line(f, "`T_recovery`, среднее время восстановления", "h4_mean_recovery_seconds")
        _write_contrasts(f, "h4_mean_recovery_seconds", lower_is_better=True)
        f.write(
            "- Покрытие событиями: "
            + "; ".join(
                f"{mode}: деградация у {h4_degradation_by_mode.get(mode, 0)}/{len(by_mode.get(mode, []))}, recovery у {h4_recovery_by_mode.get(mode, 0)}/{len(by_mode.get(mode, []))}"
                for mode in modes
            )
            + ".\n"
        )
        f.write("\nКомментарий: если у режима нет recovery-событий, отсутствие среднего времени восстановления не означает «быстро восстановился» — это означает, что данных для этой части H4 нет.\n\n")

        f.write("## H5 и H6: субъективные гипотезы\n\n")
        f.write("H5 проверяет воспринимаемую справедливость вызова, а H6 — воспринимаемую непрерывность и целостность кривой сложности. Эти гипотезы нельзя корректно подтвердить только объективной телеметрией PostHog: нужны ответы игроков из постсессионной анкеты или отдельного пользовательского исследования.\n\n")
        f.write("- H5: требуется сравнить субъективную оценку справедливости SGD с FLS и GA.\n")
        f.write("- H6: требуется сравнить субъективную оценку непрерывности вызова SGD с FLS и GA.\n\n")

        f.write("## Как читать числа\n\n")
        f.write("- `mean`/среднее чувствительно к редким большим скачкам; `median` часто равна нулю, если изменения происходят редко.\n")
        f.write("- `95% ДИ` — bootstrap-интервал по сессионным агрегатам. На маленьком числе сессий это ориентир, а не строгий статистический финал.\n")
        f.write("- Если интервал разницы пересекает 0, текущий пилотный срез не даёт уверенного отличия между режимами.\n")

    print(f"[ok] wrote {csv_path}")
    print(f"[ok] wrote {md_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

