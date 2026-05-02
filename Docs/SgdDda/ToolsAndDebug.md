## Инструменты и отладка SGD DDA

Как управлять SGD в рантайме, где смотреть состояние и как связаны отладка с телеметрией. Теория **четырёх осей**, команды `dda_sgd_axis_*` и поля `axis_*` — в [`Axes.md`](Axes.md).

### Управление алгоритмом и сброс состояния

Режим DDA задаёт `DdaAlgorithmState.ActiveAlgorithm` (`Fixed`, `Genetic`, `Sgd`); переключение — `Activate(...)`. Флаг `IsGeneticAlgorithmEnabled` сохраняет ожидания для GA; `IsDebugOverlayEnabled` включает overlay и сбор телеметрии независимо от того, выбран ли сейчас SGD.

При старте забега `SgdRuntimeDriver.Run_Start` сбрасывает `SgdDecisionRuntimeState` и `SgdActuatorsRuntimeState` и сразу прогоняет `SgdActuatorsApplier.ApplyToAllLivingMonsters()`. При смене тела игрока `SgdSensorsHooks.Reset` обнуляет отслеживаемое тело, внутренности `SgdSensorsEstimator` и `SgdSensorsRuntimeState`. Для «жёсткого» ресета без нового забега вручную можно вызвать `SgdRuntimeState.Clear`, `SgdSensorsRuntimeState.Clear`, `SgdDecisionRuntimeState.Reset`, `SgdActuatorsRuntimeState.Reset` — удобно обернуть одной консольной командой.

### Использование CheatManager и консольных команд

`GeneticsArtifact.CheatManager` подключает консоль RoR2. Примеры: `dda_algorithm fixed|genetic|sgd` (синонимы `fls`, `ga`); принудительное `SgdActuatorsApplier.ApplyToAllLivingMonsters()` после ручной правки множителей; вывод в лог `VirtualPower`, `SgdSensorsRuntimeState.Sample`, множителей и \(\theta\)/velocity из `SgdDecisionRuntimeState`. Полный список и оси — `CheatManager/README.md` и [`Axes.md`](Axes.md).

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

В `tools/` добавлены скрипты для выгрузки событий/персон из PostHog в `tools/posthog_exports/`:

- `tools/posthog_export_events.ps1` / `.bat`
- `tools/posthog_export_all.ps1` / `.bat`

Папка `tools/posthog_exports/` добавлена в `.gitignore` и не должна коммититься.

### H1-H4: протокол проверки и метрики

Начиная с `telemetry_schema_version = 3`, H1–H4 опираются на сессионные агрегаты и четыре плоскости: `damage` ↔ `AttackDamage`, `attackSpeed` ↔ `AttackSpeed`, `hp` ↔ `MaxHealth`, `moveSpeed` ↔ `MoveSpeed`. У каждой — один актуатор; смешивать их в один скаляр «сложности монстров» для сравнения с \(V_p\) нельзя. `regen` только как вспомогательный сигнал внутри `hp`, не как пятая ось. Соответствие полей `axis_*` и весов skill — [`Axes.md`](Axes.md).

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

#### H2: плавность динамики сложности

Бинарное поле `axis_*_is_jump` оставлено как дополнительный индикатор, но основная проверка H2 должна использовать непрерывные метрики плавности. Причина: SGD обновляет множители малыми шагами, поэтому порог `telemetryJumpThreshold = 0.10` может давать `JumpRate = 0` даже при реальной адаптации.

В телеметрии schema v3 используются поля:

- `axis_*_delta_multiplier`;
- `axis_*_abs_delta_multiplier`;
- `axis_*_delta_theta`;
- `axis_*_abs_delta_theta`;
- `axis_*_relative_delta_multiplier`;
- `axis_*_abs_relative_delta_multiplier`;
- `axis_*_is_jump`.

На уровне сессии считать:

```text
JumpRate_session = count(axis_*_is_jump = true) / count(axis observations)
MeanAbsDelta_session = mean(axis_*_abs_delta_multiplier)
P95AbsDelta_session = p95(axis_*_abs_delta_multiplier)
MeanAbsDeltaTheta_session = mean(axis_*_abs_delta_theta)
MeanAbsRelativeDelta_session = mean(axis_*_abs_relative_delta_multiplier)
```

Для H2 меньшее значение непрерывных метрик означает более плавную адаптацию. `JumpRate = 0` сам по себе не доказывает плавность; он должен интерпретироваться вместе с `mean/p95/max` изменений.

#### H3: компенсация роста мощности по четырем плоскостям

H3 нельзя проверять через прямое сравнение `virtual_power_total` и `virtual_challenge_total`, потому что это разные шкалы и разные пространства признаков. Основная проверка H3 проводится только внутри четырех плоскостей:

- `damage`;
- `attackSpeed`;
- `hp`;
- `moveSpeed`.

В телеметрии используются:

- `axis_*_plane`;
- `axis_*_skill01`;
- `axis_*_challenge01`;
- `axis_*_error`;
- `axis_*_abs_error`;
- `axis_*_delta_skill01`;
- `axis_*_delta_challenge01`.

Основной показатель близости:

```text
H3_axis_MAE_session = mean(axis_*_abs_error)
```

Дополнительно:

```text
StableRate_session = count(axis_*_abs_error <= epsilon_stable) / count(axis observations)
AxisCoupling_session = corr(delta_skill_i, delta_challenge_i)
```

`AxisCoupling_session` интерпретируется осторожно: SGD реагирует дискретно и с задержкой, поэтому для финального анализа желательно также проверять лаговую связь:

```text
corr(delta_skill_i(t), delta_challenge_i(t + 1))
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
python tools/analyze_hypotheses_h1_h4.py tools/posthog_exports/ALL_events_*.jsonl
```

Анализ только сессий Лучницы/Охотницы:

```bash
python tools/analyze_hypotheses_h1_h4.py tools/posthog_exports/ALL_events_*.jsonl --only-huntress
```

Анализ только новой схемы v3:

```bash
python tools/analyze_hypotheses_h1_h4.py tools/posthog_exports/ALL_events_*.jsonl --min-schema-version 3
```

Результаты сохраняются в:

- `tools/posthog_exports/hypotheses_results/session_metrics_h1_h4.csv`;
- `tools/posthog_exports/hypotheses_results/summary_h1_h4.md`.

Для проверки распределений сенсоров и подбора диагностических порогов:

```bash
python tools/calibrate_sgd_sensors.py tools/posthog_exports/ALL_events_*.jsonl --only-huntress
```

Этот скрипт формирует:

- `tools/posthog_exports/hypotheses_results/sensor_calibration_hints.md`.

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
python tools/validate_posthog_survey.py tools/posthog_exports/ALL_events_*.jsonl
```

Опционально вывести «OK»‑сессии:

```bash
python tools/validate_posthog_survey.py tools/posthog_exports/ALL_events_*.jsonl --show-ok
```


