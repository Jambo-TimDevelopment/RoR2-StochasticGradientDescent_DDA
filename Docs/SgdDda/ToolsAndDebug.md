**Languages:** [English](#english) · [Русский](#russian)

---

<a id="english"></a>
## Tools and debugging SGD DDA

How to control SGD at runtime, where to read state, and how debugging ties to telemetry. Theory of the **four axes**, commands `dda_sgd_axis_*`, and `axis_*` fields: [`Axes.md`](Axes.md#english).

### Algorithm control and state reset

`DdaAlgorithmState.ActiveAlgorithm` selects `Fixed`, `Genetic`, or `Sgd`; use `Activate(...)`. `IsGeneticAlgorithmEnabled` preserves GA expectations; `IsDebugOverlayEnabled` enables overlay and telemetry collection even when SGD is not the active algorithm.

On run start, `SgdRuntimeDriver.Run_Start` resets `SgdDecisionRuntimeState` and `SgdActuatorsRuntimeState` and runs `SgdActuatorsApplier.ApplyToAllLivingMonsters()`. On player body change, `SgdSensorsHooks.Reset` clears the tracked body, `SgdSensorsEstimator` internals, and `SgdSensorsRuntimeState`. For a hard reset without a new run, call `SgdRuntimeState.Clear`, `SgdSensorsRuntimeState.Clear`, `SgdDecisionRuntimeState.Reset`, `SgdActuatorsRuntimeState.Reset` — wrap in one console command if useful.

### CheatManager and console

`GeneticsArtifact.CheatManager` wires the RoR2 console. Examples: `dda_algorithm fixed|genetic|sgd` (aliases `fls`, `ga`); force `SgdActuatorsApplier.ApplyToAllLivingMonsters()` after manual multiplier edits; log `VirtualPower`, `SgdSensorsRuntimeState.Sample`, multipliers, and \(\theta\)/velocity from `SgdDecisionRuntimeState`. Full command list and axes: [`CheatManager/README.md`](../../CheatManager/README.md#english) and [`Axes.md`](Axes.md#english).

Experiments: `dda_survey <fairness 1-7> <continuity 1-7> [comment]` → `dda_post_session_survey`; `dda_sgd_step_time [seconds]` — combat-time accumulation interval between SGD steps.

### Debug overlay and visualization

`DdaAlgorithmState.IsDebugOverlayEnabled` turns on extended debugging:

- Even if `ActiveAlgorithm != DdaAlgorithmType.Sgd`, sensors and the runtime driver can keep collecting data.
- You can evaluate sensor and virtual-power behavior without changing difficulty, and compare SGD vs GA on the same session.

Possible overlay contents (depends on your UI):

- **Telemetry**: current player virtual power \(V_p(t)\); normalized sensors (HitRateOnPlayerNorm01, IncomingDamageNorm01, OutgoingDamageNorm01, LowHealthUptime, DeathsPerWindowNorm01, AvgTtkSecondsNorm01).
- **Decision state**: per-axis multipliers (MaxHealth, MoveSpeed, AttackSpeed, AttackDamage); skill01/challenge01 per axis; error and direction of change.

Implement as a simple text HUD fed from `SgdRuntimeState`, `SgdSensorsRuntimeState`, `SgdDecisionRuntimeState`, `SgdActuatorsRuntimeState`.

### Logging and behavior analysis

Use `GeneticsArtifactPlugin.geneticLogSource` (or the mod’s `ManualLogSource`). Useful log points: per-axis `Record*Step` (skill01, challenge01, error, gradient, velocity, deltaTheta, new multiplier); `RecordGlobalStep(appliedMonsters)`; mode toggles, CheatManager interventions, resets. For offline analysis, parse logs (Python/R) for multiplier evolution, skill/challenge balance, vs virtual power.

### Typical debug scenarios

**Too hard:** Enable SGD and overlay; force high incoming damage, deaths, long TTK; watch IncomingDamageNorm01, LowHealthUptime, DeathsPerWindowNorm01 and per-axis skill/challenge; check HP/AttackDamage multipliers decrease. Tune learning rates or `Estimate*Skill01` weights if needed.

**Too easy:** Low incoming damage, few deaths, short TTK; multipliers should rise toward caps without violent oscillation.

**Transitional:** Stage changes, build spikes, long win streaks — difficulty should track improved builds and not trap the player after a bad room.

### PostHog export scripts

`tools/export_data_scripts/posthog_export_events.ps1` / `.bat`, `tools/export_data_scripts/posthog_export_all.ps1` / `.bat` export to `tools/export_data_scripts/posthog_exports/` (gitignored).

### H1–H4: verification protocol and metrics

From `telemetry_schema_version = 3`, H1–H4 use session aggregates and four planes: `damage` ↔ `AttackDamage`, `attackSpeed` ↔ `AttackSpeed`, `hp` ↔ `MaxHealth`, `moveSpeed` ↔ `MoveSpeed`. One actuator per plane; do not fold them into one “monster difficulty” scalar vs \(V_p\). `regen` is auxiliary inside `hp`, not a fifth axis. `axis_*` fields and skill weights: [`Axes.md`](Axes.md#english).

**v4** adds: (1) applied SGD axis limits on each event — `sgd_{hp,ms,as,dmg}_floor_applied`, `sgd_*_cap_applied`, `sgd_*_theta_min_applied`, `sgd_*_theta_max_applied`, `sgd_*_theta_range_applied`, `sgd_*_neutral_challenge01`; (2) virtual-power compensation diagnostics from the SGD decision step — `sgd_vp_has_baseline`, `sgd_vp_baseline`, `sgd_vp_delta_for_decision`, `sgd_vc_decision_current`, `sgd_virtual_error_for_decision`, `sgd_virtual_power_scale`, `sgd_virtual_loss_weight`, `sgd_{hp,ms,as,dmg}_virtual_error_contribution`, `sgd_*_virtual_challenge_weight`. Analyses that need these fields should filter `telemetry_schema_version >= 4`.

**v5** replaces the formal H3 signal with four-axis build power and virtual challenge fields aligned with the SGD actuators: `virtual_power_{hp,move_speed,attack_speed,attack_damage}`, `virtual_challenge_*`, `delta_virtual_power_*`, `delta_virtual_challenge_*`, `virtual_gap_*_abs`, `virtual_gap_axes_mean_abs`, `is_within_virtual_gap_axes_epsilon`, `h3_axis_schema = vp_vc_4_axes_v1`. Legacy total fields (`virtual_power_total`, `virtual_challenge_total`, `virtual_gap_abs`, `delta_virtual_power`, `delta_virtual_challenge`) remain for compatibility. Analyses for the corrected H3 should filter `telemetry_schema_version >= 5`.

**v6** adds decision-step semantics for H3 axis data: `h3_is_decision_step`, `h3_decision_step_index`, `h3_decision_step_reason`, `h3_decision_step_interval_seconds`. For `telemetry_schema_version >= 6`, H3 coupling should be calculated from decision steps only.

**v7** standardizes `Vc_i` provenance for all modes and adds runtime quality diagnostics for NA causes:
- `h3_challenge_source` (`sgd_actuators`, `ga_master_genes_avg`, `ga_living_genes_avg`, `ga_fallback_unity`, `fls_fixed`);
- `h3_vc_semantics` (currently `ln_clamped_multiplier` / `ln_clamped_multiplier_fixed_unity`);
- mode-neutral decision aliases `dda_is_decision_step`, `dda_decision_step_*`;
- per-axis quality counters and flags:
  - `h3_axis_pair_count_{hp,move_speed,attack_speed,attack_damage}`
  - `h3_axis_nonzero_dvc_count_{hp,move_speed,attack_speed,attack_damage}`
  - `h3_axis_has_variance_{hp,move_speed,attack_speed,attack_damage}`.

#### Session quality

The analysis unit is a **session**, not each `dda_sample`. Samples are a time series and autocorrelated — do not inflate N by treating every tick as independent.

Quality markers: `telemetry_schema_version >= 3`; `duration_seconds >= 300`; `is_quality_excluded_short_session = false`; `missed_sample_intervals = 0` or documented tolerance; filled `player_body`, `dda_mode`, `runtime_run_seed`, `stage_name`, `run_attempt_index`; `dda_session_end` present; for H5/H6 also `dda_post_session_survey` or `dda_post_session_survey_skipped`.

For strict mode comparison: same survivor where possible, comparable run conditions, repeat `FLS`, `GA`, `SGD`; fix or balance `condition_order` across participants.

#### H1: alignment accuracy

H1 asks whether SGD lowers mean absolute misalignment vs FLS and GA. Telemetry: `axis_attack_damage_abs_error`, `axis_attack_speed_abs_error`, `axis_max_health_abs_error`, `axis_move_speed_abs_error`.

```text
MAE_align_session = mean(axis_*_abs_error)
```

Compare distributions so \(E[\text{MAE\_align}]_{\text{SGD}} < E[\text{MAE\_align}]_{\text{FLS}}\) and vs GA. Report counts, mean/median, plots, CIs for SGD−FLS and SGD−GA; bootstrap CIs are illustrative only in pilots.

#### H2: smoothness of the skill–challenge misalignment **trajectory**

Thesis-level **H2** is the **dynamics** of misalignment, not only its level (that is **H1**): fewer / less severe **jumps** in the error between perceived challenge and estimated skill, i.e. the trajectory of `e_i = challenge01_i - skill01_i`.

**Primary checks (H2.3 in `tools/analyze_data_scripts/analyze_hypotheses_h1_h4.py`):** session aggregates such as `P(|Δe_i| > τ_jump)`, `P(|Δ abs_error| > τ_jump)` over axes, plus mean/p95 of `|Δe|` and mirrored `abs_error` step stats. Lower jump rates / smaller step magnitudes ⇒ smoother **error trajectory**.

**Auxiliary splits:** **H2.1** — actuator / `delta_multiplier` jump stats (where roughness enters the knobs); **H2.2** — target `challenge01` jump stats (where roughness enters the setpoint). They **do not replace** the primary H2 wording; they explain *where* roughness comes from if H2.3 vs H2.1/H2.2 disagree.

`axis_*_is_jump` on multipliers remains supplementary. SGD can yield near-zero multiplier jump rates while still adapting — always interpret multiplier jump metrics **together with** the **error-trajectory** metrics above.

Schema v3 fields (non-exhaustive): per-axis `delta_challenge01`, `delta_skill01` / errors, `axis_*_delta_multiplier`, `axis_*_abs_delta_multiplier`, `axis_*_delta_theta`, `axis_*_relative_delta_multiplier`, `axis_*_is_jump`, `tau_jump`.

```text
# Primary H2.3-style (see script for exact pooling across axes/samples)
ErrorJumpRate_session ≈ mean( |Δe_axis| > tau_jump )
MeanAbsDeltaError_session = mean(|Δe_axis|)

# Auxiliary H2.1-style
JumpRate_tau_session ≈ mean( |axis_*_delta_multiplier| > tau_jump )
MeanAbsDelta_session = mean(axis_*_abs_delta_multiplier)
```

#### H3: compensating power growth across four planes

Do **not** validate H3 only via `virtual_power_total` vs `virtual_challenge_total` (different scales/spaces). From schema v5, primary H3 is the four-axis virtual power/challenge compensation over `hp`, `move_speed`, `attack_speed`, `attack_damage`. Fields: `virtual_power_*`, `virtual_challenge_*`, `delta_virtual_power_*`, `delta_virtual_challenge_*`, `virtual_gap_*_abs`.

```text
H3_axis_gap_session = mean(virtual_gap_*_abs)
H3_axis_coupling_session = mean(corr(delta_virtual_power_i, delta_virtual_challenge_i))
H3_axis_within_epsilon = mean(virtual_gap_*_abs <= epsilon_v)
```

Interpret coupling cautiously; consider lagged correlation `corr(delta_virtual_power_i(t), delta_virtual_challenge_i(t+1))`. Keep `virtual_power_total`, `virtual_challenge_total`, `virtual_gap_abs`, `delta_virtual_power`, `delta_virtual_challenge` as diagnostic legacy only, not the main H3 criterion.

#### H4: recovery after degradation

H4 applies to sessions with a real degradation episode. If `degradation_events_count = 0`, the session describes stable play, not recovery time.

Fields: `degradation_signal`, `is_degraded`, `is_degraded_050`/`060`/`070`, time above thresholds, `degradation_signal_below_recovery_seconds`, events `dda_degradation_start`, `dda_degradation_end`, `dda_recovery`, `recovery_elapsed_seconds`.

```text
T_recovery_session = mean(recovery_elapsed_seconds)
```

Exclude sessions missing degradation start/recovery from `T_recovery` comparisons; report missingness rates. Pilot: inspect max `degradation_signal` and time above 0.50/0.60/0.70; fix `telemetryDegradationThreshold` and `telemetryRecoveryThreshold` before the main study.

#### H1–H4 analysis commands

```bash
python tools/analyze_data_scripts/analyze_hypotheses_h1_h4.py tools/export_data_scripts/posthog_exports/ALL_events_*.jsonl
python tools/analyze_data_scripts/analyze_hypotheses_h1_h4.py tools/export_data_scripts/posthog_exports/ALL_events_*.jsonl --min-schema-version 3
```

Session-level tools include **all survivors** in the export (`player_body` is not filtered). For subsets, use `--only-mode`, time filters, or split exports manually.

```bash
python tools/analyze_data_scripts/inspect_dda_sessions.py tools/export_data_scripts/posthog_exports/ALL_events_*.jsonl
```

Outputs: `tools/export_data_scripts/posthog_exports/hypotheses_results/session_metrics_h1_h4.csv`, `summary_h1_h4.md`.

Sensor calibration:

```bash
python tools/analyze_data_scripts/calibrate_sgd_sensors.py tools/export_data_scripts/posthog_exports/ALL_events_*.jsonl
```

Produces `sensor_calibration_hints.md` — candidate thresholds only; finalize manually in experiment config.

### H5/H6: survey protocol and export validation

Subjective Likert 1–7: **H5 fairness** (1 unfair … 7 fair); **H6 continuity** (1 jagged … 7 smooth). Telemetry: `dda_post_session_survey` with `fairness_likert_1_7`, `continuity_likert_1_7`, `survey_comment`; duplicated in `dda_session_end`. Close without submit → `dda_post_session_survey_skipped`.

Validator:

```bash
python tools/analyze_data_scripts/validate_posthog_survey.py tools/export_data_scripts/posthog_exports/ALL_events_*.jsonl
python tools/analyze_data_scripts/validate_posthog_survey.py tools/export_data_scripts/posthog_exports/ALL_events_*.jsonl --show-ok
```

---

<a id="russian"></a>
## Инструменты и отладка SGD DDA

Как управлять SGD в рантайме, где смотреть состояние и как связаны отладка с телеметрией. Теория **четырёх осей**, команды `dda_sgd_axis_*` и поля `axis_*` — в [`Axes.md`](Axes.md#russian).

### Управление алгоритмом и сброс состояния

Режим DDA задаёт `DdaAlgorithmState.ActiveAlgorithm` (`Fixed`, `Genetic`, `Sgd`); переключение — `Activate(...)`. Флаг `IsGeneticAlgorithmEnabled` сохраняет ожидания для GA; `IsDebugOverlayEnabled` включает overlay и сбор телеметрии независимо от того, выбран ли сейчас SGD.

При старте забега `SgdRuntimeDriver.Run_Start` сбрасывает `SgdDecisionRuntimeState` и `SgdActuatorsRuntimeState` и сразу прогоняет `SgdActuatorsApplier.ApplyToAllLivingMonsters()`. При смене тела игрока `SgdSensorsHooks.Reset` обнуляет отслеживаемое тело, внутренности `SgdSensorsEstimator` и `SgdSensorsRuntimeState`. Для «жёсткого» ресета без нового забега вручную можно вызвать `SgdRuntimeState.Clear`, `SgdSensorsRuntimeState.Clear`, `SgdDecisionRuntimeState.Reset`, `SgdActuatorsRuntimeState.Reset` — удобно обернуть одной консольной командой.

### Использование CheatManager и консольных команд

`GeneticsArtifact.CheatManager` подключает консоль RoR2. Примеры: `dda_algorithm fixed|genetic|sgd` (синонимы `fls`, `ga`); принудительное `SgdActuatorsApplier.ApplyToAllLivingMonsters()` после ручной правки множителей; вывод в лог `VirtualPower`, `SgdSensorsRuntimeState.Sample`, множителей и \(\theta\)/velocity из `SgdDecisionRuntimeState`. Полный список и оси — [`CheatManager/README.md`](../../CheatManager/README.md#russian) и [`Axes.md`](Axes.md#russian).

Для экспериментов: `dda_survey <fairness 1-7> <continuity 1-7> [comment]` → `dda_post_session_survey`; `dda_sgd_step_time [seconds]` — интервал накопления боя между шагами SGD.

### Debug‑overlay и визуализация

Флаг `DdaAlgorithmState.IsDebugOverlayEnabled` служит для включения расширенного режима отладки:

- даже если `ActiveAlgorithm != DdaAlgorithmType.Sgd`, сенсоры и runtime‑драйвер могут продолжать собирать данные;
- это позволяет:
  - оценивать поведение сенсоров и виртуальной мощности без фактического изменения сложности;
  - сравнивать реакции SGD‑алгоритма и генетического алгоритма на одну и ту же игровую сессию.

Возможные элементы overlay (зависят от вашей реализации UI):

- **Telemetry**:
  - текущая виртуальная мощность игрока \( V_p(t) \);
  - нормализованные сенсоры:
    - HitRateOnPlayerNorm01
    - IncomingDamageNorm01
    - OutgoingDamageNorm01
    - LowHealthUptime
    - DeathsPerWindowNorm01
    - AvgTtkSecondsNorm01.
- **Decision state**:
  - значения множителей по осям:
    - MaxHealthMultiplier
    - MoveSpeedMultiplier
    - AttackSpeedMultiplier
    - AttackDamageMultiplier;
  - оценки skill01/challenge01 по каждой оси;
  - значение ошибки и знак изменения (усложняем/упрощаем).

Overlay удобно реализовать как простой текстовый HUD поверх экрана, который обновляется по данным из:

- `SgdRuntimeState`
- `SgdSensorsRuntimeState`
- `SgdDecisionRuntimeState`
- `SgdActuatorsRuntimeState`.

### Логирование и анализ поведения

Для аналитики поведения SGD DDA и тонкой настройки гиперпараметров полезно использовать лог:

- Источник лога:
  - `GeneticsArtifactPlugin.geneticLogSource` (или аналогичный `ManualLogSource`, уже применяемый в моде).
- Рекомендуемые точки логирования:
  - **Шаги SGD по осям**:
    - запись в `SgdDecisionRuntimeState.Record*Step(...)` можно дополнять логами:
      - skill01, challenge01, error;
      - gradient, velocity;
      - deltaTheta, новый множитель.
  - **Глобальные шаги**:
    - результат `RecordGlobalStep(appliedMonsters: count)`:
      - сколько монстров было обновлено;
      - текущий номер шага/время боя.
  - **События смены состояния**:
    - включение/выключение SGD;
    - ручные вмешательства через CheatManager;
    - сбросы состояния.

Для последующего анализа (NIR/исследование) логи можно:

- собирать в отдельный файл;
- парсить внешним скриптом (Python/R) для построения графиков:
  - эволюция множителей по осям;
  - соотношение skill/challenge;
  - зависимость от виртуальной мощности игрока.

### Типичные сценарии отладки

Ниже несколько практических сценариев, полезных при калибровке SGD DDA.

#### Сценарий 1: «Слишком сложно»

1. Включить SGD и debug‑overlay.
2. Создать ситуацию, где игрок явно не справляется:
   - высокий входящий урон;
   - частые смерти;
   - длинный TTK по монстрам.
3. Наблюдать:
   - растёт ли `IncomingDamageNorm01`, `LowHealthUptime`, `DeathsPerWindowNorm01`;
   - каковы значения skill01/challenge01 по каждой оси;
   - уменьшаются ли множители HP/AttackDamage с течением боя.
4. При необходимости:
   - уменьшить learning rate для осей, которые слишком агрессивно «проваливают» сложность;
   - скорректировать веса в функциях `Estimate*Skill01`.

#### Сценарий 2: «Слишком легко»

1. Включить SGD и debug‑overlay.
2. Создать ситуацию, где игрок доминирует:
   - низкий входящий урон;
   - редкие или отсутствующие смерти;
   - короткий TTK.
3. Проверить:
   - низкие значения `IncomingDamageNorm01`, `DeathsPerWindowNorm01`, `LowHealthUptime`;
   - рост множителей HP/AttackDamage/MoveSpeed до верхних границ;
   - отсутствие «пилы» (частых резких колебаний множителей).

#### Сценарий 3: «Переходные режимы»

1. Начать забег с включённым SGD.
2. Наблюдать поведение при:
   - смене стадии;
   - смене билда игрока (например, резкий рост DPS);
   - длительных сериях успешных боёв без смертей.
3. Убедиться, что:
   - SGD успевает поднять сложность после улучшения билда;
   - при ухудшении ситуации (случайная плохая комната, неудачный босс) алгоритм не «запирает» игрока в слишком высокой сложности.

### PostHog экспорт данных (инструментарий исследования)

В `tools/` добавлены скрипты для выгрузки событий/персон из PostHog в `tools/export_data_scripts/posthog_exports/`:

- `tools/export_data_scripts/posthog_export_events.ps1` / `.bat`
- `tools/export_data_scripts/posthog_export_all.ps1` / `.bat`

Папка `tools/export_data_scripts/posthog_exports/` добавлена в `.gitignore` и не должна коммититься.

### H1-H4: протокол проверки и метрики

Начиная с `telemetry_schema_version = 3`, H1–H4 опираются на сессионные агрегаты и четыре плоскости: `damage` ↔ `AttackDamage`, `attackSpeed` ↔ `AttackSpeed`, `hp` ↔ `MaxHealth`, `moveSpeed` ↔ `MoveSpeed`. У каждой — один актуатор; смешивать их в один скаляр «сложности монстров» для сравнения с \(V_p\) нельзя. `regen` только как вспомогательный сигнал внутри `hp`, не как пятая ось. Соответствие полей `axis_*` и весов skill — [`Axes.md`](Axes.md#russian).

**v4** добавляет: (1) снимки применённых лимитов SGD на каждом событии — `sgd_{hp,ms,as,dmg}_floor_applied`, `sgd_*_cap_applied`, `sgd_*_theta_min_applied`, `sgd_*_theta_max_applied`, `sgd_*_theta_range_applied`, `sgd_*_neutral_challenge01`; (2) диагностику компенсации виртуальной мощности из шага SGD — `sgd_vp_has_baseline`, `sgd_vp_baseline`, `sgd_vp_delta_for_decision`, `sgd_vc_decision_current`, `sgd_virtual_error_for_decision`, `sgd_virtual_power_scale`, `sgd_virtual_loss_weight`, `sgd_{hp,ms,as,dmg}_virtual_error_contribution`, `sgd_*_virtual_challenge_weight`. Для анализов, где эти поля обязательны, фильтруйте `telemetry_schema_version >= 4`.

**v5** переводит формальную H3 на четыре оси виртуальной мощности и вызова, совпадающие с актуаторами SGD: `virtual_power_{hp,move_speed,attack_speed,attack_damage}`, `virtual_challenge_*`, `delta_virtual_power_*`, `delta_virtual_challenge_*`, `virtual_gap_*_abs`, `virtual_gap_axes_mean_abs`, `is_within_virtual_gap_axes_epsilon`, `h3_axis_schema = vp_vc_4_axes_v1`. Legacy total-поля (`virtual_power_total`, `virtual_challenge_total`, `virtual_gap_abs`, `delta_virtual_power`, `delta_virtual_challenge`) остаются для совместимости. Для исправленной H3 фильтруйте `telemetry_schema_version >= 5`.

**v6** добавляет decision-step семантику для H3 axis-полей: `h3_is_decision_step`, `h3_decision_step_index`, `h3_decision_step_reason`, `h3_decision_step_interval_seconds`. Для `telemetry_schema_version >= 6` сцепление H3 нужно считать только по decision-step сэмплам.

**v7** фиксирует происхождение `Vc_i` для всех режимов и добавляет runtime-диагностику причин NA:
- `h3_challenge_source` (`sgd_actuators`, `ga_master_genes_avg`, `ga_living_genes_avg`, `ga_fallback_unity`, `fls_fixed`);
- `h3_vc_semantics` (сейчас `ln_clamped_multiplier` / `ln_clamped_multiplier_fixed_unity`);
- mode-neutral alias для decision шага: `dda_is_decision_step`, `dda_decision_step_*`;
- осевые quality-поля:
  - `h3_axis_pair_count_{hp,move_speed,attack_speed,attack_damage}`
  - `h3_axis_nonzero_dvc_count_{hp,move_speed,attack_speed,attack_damage}`
  - `h3_axis_has_variance_{hp,move_speed,attack_speed,attack_damage}`.

#### Качество сессии

Для магистерского отчета единица анализа - игровая сессия, а не отдельный `dda_sample`. Сэмплы внутри сессии являются временным рядом и автокоррелированы, поэтому по ним нельзя искусственно увеличивать размер выборки.

Минимальные признаки качественной сессии:

- `telemetry_schema_version >= 3`;
- `duration_seconds >= 300`;
- `is_quality_excluded_short_session = false`;
- `missed_sample_intervals = 0` или явно описанное допустимое значение;
- заполнены `player_body`, `dda_mode`, `runtime_run_seed`, `stage_name`, `run_attempt_index`;
- есть `dda_session_end`;
- для H5/H6 дополнительно есть `dda_post_session_survey` или `dda_post_session_survey_skipped`.

Для строгого сравнения режимов желательно использовать сбалансированный протокол: один и тот же герой, сопоставимые условия забега, одинаковая длительность/стадия/seed где возможно, и повторение режимов `FLS`, `GA`, `SGD` для каждого участника. Порядок режимов нужно фиксировать в `condition_order` или балансировать между участниками.

#### H1: точность согласования

Гипотеза H1 проверяет, снижает ли SGD среднюю абсолютную ошибку рассогласования относительно `FLS` и `GA`.

В телеметрии используются поля:

- `axis_attack_damage_abs_error`;
- `axis_attack_speed_abs_error`;
- `axis_max_health_abs_error`;
- `axis_move_speed_abs_error`.

На уровне сессии считается:

```text
MAE_align_session = mean(axis_*_abs_error)
```

Затем сравниваются распределения `MAE_align_session` между режимами:

```text
E[MAE_align]_SGD < E[MAE_align]_FLS
E[MAE_align]_SGD < E[MAE_align]_GA
```

Для отчета показывать: число сессий по режимам, среднее/медиану, box/violin plot, доверительный интервал разности `SGD - FLS` и `SGD - GA`. В пилотных данных bootstrap-ДИ можно использовать только как визуализацию неопределенности, не как строгий статистический вывод.

#### H2: плавность **траектории** рассогласования skill–challenge

**H1** отвечает за **величину** среднего рассогласования (`MAE` по `axis_*_abs_error`). **H2** — за **динамику** рассогласования: насколько редко траектория ошибки `e_i = challenge01_i - skill01_i` делает **резкие скачки** (меньше `P(|Δe_i| > τ_jump)` и связанных средних/p95 шага `|Δe|`). Это **основная** операционализация H2 (**H2.3** в `analyze_hypotheses_h1_h4.py`).

**Вспомогательно:** **H2.1** — скачки применённых множителей `m_i` по осям; **H2.2** — скачки целевого `challenge01`. Они **не заменяют** формулировку H2, а показывают, **где** возникает резкость, если траектория `e_i` и актуаторы ведут себя по-разному.

Бинарное `axis_*_is_jump` по множителям остаётся дополнительным индикатором; у SGD часто малые шаги множителя и `JumpRate = 0` при ненулевой адаптации — поэтому по **основной** H2 ориентируйтесь на метрики **траектории ошибки** (H2.3), а множители и `challenge01` читайте вместе с ними.

В телеметрии schema v3 (фрагмент): приращения `challenge01`/`skill01`/ошибки по оси, `axis_*_delta_multiplier`, `axis_*_abs_delta_multiplier`, `axis_*_delta_theta`, `axis_*_relative_delta_multiplier`, `axis_*_is_jump`, `tau_jump`.

```text
# Ядро H2.3 (в скрипте — точное объединение по осям/сэмплам)
ErrorJumpRate_session ≈ mean( |Δe_axis| > tau_jump )

# Вспомогательное H2.1
JumpRate_tau_session ≈ mean( |axis_*_delta_multiplier| > tau_jump )
MeanAbsDelta_session = mean(axis_*_abs_delta_multiplier)
```

Меньшие доли скачков и меньшие средние/p95 шага для **ошибки** означают более плавную траекторию рассогласования. `JumpRate = 0` только по множителю **не** доказывает выполнение H2.

#### H3: компенсация роста мощности по четырем плоскостям

H3 нельзя проверять через прямое сравнение `virtual_power_total` и `virtual_challenge_total`, потому что это разные шкалы и разные пространства признаков. Начиная со schema v5, основная проверка H3 проводится по четырем осям виртуальной мощности/сложности: `hp`, `move_speed`, `attack_speed`, `attack_damage`.

В телеметрии используются `virtual_power_*`, `virtual_challenge_*`, `delta_virtual_power_*`, `delta_virtual_challenge_*`, `virtual_gap_*_abs`.

Основные показатели:

```text
H3_axis_gap_session = mean(virtual_gap_*_abs)
H3_axis_coupling_session = mean(corr(delta_virtual_power_i, delta_virtual_challenge_i))
H3_axis_within_epsilon = mean(virtual_gap_*_abs <= epsilon_v)
```

Дополнительно старые axis skill/challenge поля полезны для связи с H1/H2:

```text
StableRate_session = count(axis_*_abs_error <= epsilon_stable) / count(axis observations)
AxisCoupling_session = corr(delta_skill_i, delta_challenge_i)
```

`H3_axis_coupling_session` интерпретируется осторожно: SGD реагирует дискретно и с задержкой, поэтому для финального анализа желательно также проверять лаговую связь:

```text
corr(delta_virtual_power_i(t), delta_virtual_challenge_i(t + 1))
```

Поля `virtual_power_total`, `virtual_challenge_total`, `virtual_gap_abs`, `delta_virtual_power`, `delta_virtual_challenge` можно оставлять в отчете только как диагностические legacy-показатели. Они не должны быть главным критерием H3.

#### H4: восстановление после деградации

H4 проверяется только на сессиях, где реально возник эпизод деградации. Если `degradation_events_count = 0`, сессия полезна для описания стабильного прохождения, но не проверяет время восстановления.

В телеметрии используются:

- `degradation_signal`;
- `is_degraded`;
- `is_degraded_050`;
- `is_degraded_060`;
- `is_degraded_070`;
- `degradation_signal_above_050_seconds`;
- `degradation_signal_above_060_seconds`;
- `degradation_signal_above_070_seconds`;
- `degradation_signal_below_recovery_seconds`;
- события `dda_degradation_start`, `dda_degradation_end`, `dda_recovery`;
- `recovery_elapsed_seconds`.

Основная величина:

```text
T_recovery_session = mean(recovery_elapsed_seconds)
```

Сессии без `dda_degradation_start` или `dda_recovery` не должны включаться в сравнение `T_recovery`, но должны учитываться в отчете как missingness:

```text
degradation_start_sessions / total_sessions
recovery_sessions / degradation_start_sessions
```

Для пилотной калибровки полезно смотреть максимальный `degradation_signal` и длительности выше порогов `0.50`, `0.60`, `0.70`. Финальные `telemetryDegradationThreshold` и `telemetryRecoveryThreshold` нужно зафиксировать до основного эксперимента.

#### Команды анализа H1-H4

Основной анализатор:

```bash
python tools/analyze_data_scripts/analyze_hypotheses_h1_h4.py tools/export_data_scripts/posthog_exports/ALL_events_*.jsonl
```

По умолчанию учитываются **все персонажи** в выгрузке (фильтра по `player_body` нет). Для подмножеств используйте `--only-mode`, временные окна или разделите JSONL вручную.

Просмотр сессий и полей H2/H4:

```bash
python tools/analyze_data_scripts/inspect_dda_sessions.py tools/export_data_scripts/posthog_exports/ALL_events_*.jsonl
```

Анализ только новой схемы v3:

```bash
python tools/analyze_data_scripts/analyze_hypotheses_h1_h4.py tools/export_data_scripts/posthog_exports/ALL_events_*.jsonl --min-schema-version 3
```

Результаты сохраняются в:

- `tools/export_data_scripts/posthog_exports/hypotheses_results/session_metrics_h1_h4.csv`;
- `tools/export_data_scripts/posthog_exports/hypotheses_results/summary_h1_h4.md`.

Для проверки распределений сенсоров и подбора диагностических порогов:

```bash
python tools/analyze_data_scripts/calibrate_sgd_sensors.py tools/export_data_scripts/posthog_exports/ALL_events_*.jsonl
```

Этот скрипт формирует:

- `tools/export_data_scripts/posthog_exports/hypotheses_results/sensor_calibration_hints.md`.

Калибровочный отчет не фиксирует финальные параметры автоматически. Он показывает распределения сенсоров и кандидатные пороги, которые затем нужно вручную зафиксировать в конфигурации эксперимента до финального сбора данных.

### H5/H6: протокол опроса и проверка выгрузки

В исследовании H5–H6 измеряются субъективно, через пост‑сессионный опросник (Likert 1–7):

- **H5 fairness**: «насколько справедливой ощущалась сложность» (1 — совсем несправедливо, 7 — полностью справедливо).
- **H6 continuity**: «насколько плавной/непрерывной ощущалась кривая сложности» (1 — очень рвано, 7 — очень плавно).

В телеметрии это приходит как:

- событие `dda_post_session_survey` с полями:
  - `fairness_likert_1_7`
  - `continuity_likert_1_7`
  - `survey_comment` (включает `ui_trigger=...`)
- а также дублируется в `dda_session_end` для удобной агрегации.

Если пользователь закрывает окно без отправки, логируется `dda_post_session_survey_skipped` (и сессия завершается обычным `dda_session_end`).

#### Валидатор экспорта (JSONL)

Чтобы быстро проверить, что после каждого `dda_session_end` в экспорте есть либо survey, либо skipped, используйте:

```bash
python tools/analyze_data_scripts/validate_posthog_survey.py tools/export_data_scripts/posthog_exports/ALL_events_*.jsonl
```

Опционально вывести «OK»‑сессии:

```bash
python tools/analyze_data_scripts/validate_posthog_survey.py tools/export_data_scripts/posthog_exports/ALL_events_*.jsonl --show-ok
```
