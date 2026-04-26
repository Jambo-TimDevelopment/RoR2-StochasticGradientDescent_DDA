## Инструменты и отладка SGD DDA

Этот документ описывает, как управлять SGD‑алгоритмом в рантайме, какие есть точки входа для отладки и как интерпретировать его поведение.

### Управление алгоритмом и сброс состояния

Выбор активного алгоритма DDA вынесен в общий модуль состояния:

- `DdaAlgorithmState.ActiveAlgorithm` — перечисление, задающее текущий алгоритм:
  - `DdaAlgorithmType.Fixed`
  - `DdaAlgorithmType.Genetic`
  - `DdaAlgorithmType.Sgd`
- `DdaAlgorithmState.Activate(...)` — единая точка переключения режима DDA.
- `DdaAlgorithmState.IsGeneticAlgorithmEnabled` — флаг для обратной совместимости с исходным поведением GA.
- `DdaAlgorithmState.IsDebugOverlayEnabled` — флаг включения отладочного overlay и сбора телеметрии даже при неактивном SGD.

При включении SGD‑режима важно корректно сбрасывать состояние:

- **Сброс при старте забега**:
  - В `SgdRuntimeDriver.Run_Start` вызываются:
    - `SgdDecisionRuntimeState.Reset();`
    - `SgdActuatorsRuntimeState.Reset();`
    - `SgdActuatorsApplier.ApplyToAllLivingMonsters();`
- **Сброс при смене активного тела игрока**:
  - `SgdSensorsHooks.Reset(newPlayerBody)` сбрасывает:
    - отслеживаемое тело игрока;
    - внутреннее состояние `SgdSensorsEstimator`;
    - `SgdSensorsRuntimeState`.
- **Ручной сброс для отладки** (на уровне кода или консольных команд):
  - `SgdRuntimeState.Clear();`
  - `SgdSensorsRuntimeState.Clear();`
  - `SgdDecisionRuntimeState.Reset();`
  - `SgdActuatorsRuntimeState.Reset();`

Рекомендуется группировать эти вызовы в одну консольную команду/чит для «жёсткого» перезапуска SGD‑алгоритма без рестарта забега.

### Использование CheatManager и консольных команд

Пространство имён `GeneticsArtifact.CheatManager` используется для интеграции с консолью RoR2 и тестовыми инструментами мода.

Типичные сценарии, которые удобно оборачивать в команды:

- **Переключение алгоритма DDA**:
  - `dda_algorithm fixed|genetic|sgd` (синонимы: `fls`, `ga`).
- **Принудительное обновление актуаторов**:
  - команда, которая вызывает `SgdActuatorsApplier.ApplyToAllLivingMonsters()` для моментального применения текущих множителей после ручной корректировки.
- **Диагностика состояний**:
  - команда, печатающая в лог:
    - текущее значение виртуальной мощности игрока (`SgdRuntimeState.VirtualPower`);
    - основные поля `SgdSensorsRuntimeState.Sample`;
    - значения множителей из `SgdActuatorsRuntimeState`;
    - параметры \\( \\theta \\) и velocity из `SgdDecisionRuntimeState`.

Конкретные имена команд зависят от того, как уже устроен ваш `CheatManager`. При добавлении новых команд важно не нарушать существующую структуру и придерживаться стиля остальных читов мода.

Дополнительно для экспериментов:

- **Анкета H5/H6 из консоли**:
  - `dda_survey <fairness 1-7> <continuity 1-7> [comment]`
  - отправляет событие `dda_post_session_survey`.

- **Шаг SGD по времени боя**:
  - `dda_sgd_step_time [seconds]`

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


