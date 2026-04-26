## Реализация SGD DDA

Этот документ описывает, как архитектурные идеи SGD DDA отражены в коде `SgdEngine/`, и даёт качественное объяснение используемой математической модели.

### Обзор уровней реализации

#### Runtime‑драйвер

- Файл: `SgdEngine/SgdRuntimeDriver.cs`
- Основные обязанности:
  - Регистрировать хуки сенсоров в `Run.Start`:
    - `SgdSensorsHooks.RegisterHooks();`
  - При старте забега:
    - сбрасывать состояние решения и актуаторов:
      - `SgdDecisionRuntimeState.Reset();`
      - `SgdActuatorsRuntimeState.Reset();`
    - применять актуаторы ко всем живым монстрам:
      - `SgdActuatorsApplier.ApplyToAllLivingMonsters();`
    - навешивать на объект `Run` компонент `SgdRuntimeDriver`.
  - В `Update()`:
    - выбирать подходящее тело игрока (`CharacterBody`) и отслеживать смену тела;
    - вычислять сглаженную виртуальную мощность игрока `SgdVirtualPowerEstimator.ComputeSmoothed(...)`;
    - обновлять `SgdRuntimeState` и `SgdSensorsRuntimeState`;
    - при активном SGD и `NetworkServer.active` вызывать `SgdDecisionDriver.Tick(playerBody, dt)`.

#### Virtual Power

- Файлы:
  - `SgdEngine/SgdVirtualPowerEstimator.cs`
  - `SgdEngine/SgdVirtualPowerSample.cs`
- Назначение:
  - Оценить текущую «виртуальную мощность» игрока  V_p(t) , учитывая его атакующие и защитные возможности.
  - Сгладить резкие всплески за счёт экспоненциального сглаживания/скользящих окон.
- Использование:
  - `SgdRuntimeDriver` каждый кадр получает `SgdVirtualPowerSample` и сохраняет в `SgdRuntimeState`.
  - Сенсоры (`SgdSensorsEstimator`) используют этот сигнал как дополнительную опору при интерпретации эффективности игрока.

#### Sensors

- Каталог: `SgdEngine/Sensors/`
- Основные файлы:
  - `SgdSensorsHooks.cs` — регистрация хуков, маршрутизация событий.
  - `SgdSensorsEstimator.cs` — накопление и нормализация телеметрии.
  - `SgdSensorsSample.cs` — структура с набором нормализованных метрик.
  - `SgdSensorsRuntimeState.cs` — хранение последней выборки.
- Ключевые идеи:
  - Сенсоры должны быть **минимальными**, **стабильными** и **геймплейно осмысленными**:
    - **HitRateOnPlayerNorm01** — нормализованная доля попаданий по игроку.
    - **IncomingDamageNorm01** — нормализованный входящий урон.
    - **OutgoingDamageNorm01** — нормализованный исходящий урон.
    - **LowHealthUptime** — доля времени, когда игрок был на низком здоровье.
    - **DeathsPerWindowNorm01** — нормализованное число смертей за окно времени.
    - **AvgTtkSeconds / AvgTtkSecondsNorm01** — среднее время убийства монстров.
  - Все показатели приводятся к диапазону [0, 1], чтобы использовать единый механизм обучения по осям.

#### Decision (SGD по осям сложности)

- Каталог: `SgdEngine/Decision/`
- Основные файлы:
  - `SgdDecisionDriver.cs` — логика шага SGD по каждой оси.
  - `SgdDecisionRuntimeState.cs` — хранилище состояния SGD (значения  \theta , скорости, статистика).
- Гиперпараметры:
  - Общие:
    - `DefaultMomentum` — коэффициент momentum.
    - `DefaultGradientClip` — клиппинг градиента.
    - `DefaultVelocityClip` — клиппинг скорости.
    - `DefaultErrorDeadZone` — «мёртвая зона» по ошибке (малые ошибки игнорируются).
  - Для осей:
    - HP: `HpLearningRate`, `HpMaxDeltaTheta`.
    - MoveSpeed: `MsLearningRate`, `MsMaxDeltaTheta`.
    - AttackSpeed: `AsLearningRate`, `AsMaxDeltaTheta`.
    - AttackDamage: `DmgLearningRate`, `DmgMaxDeltaTheta`.

Логика `Tick`:

1. Проверка условий (сервер, активный алгоритм SGD, тело игрока, корректный `dt`).
2. Синхронизация состояния осей с актуаторами через методы `Ensure*StateSynced()`:
  - `EnsureMaxHealthStateSynced()`
  - `EnsureMoveSpeedStateSynced()`
  - `EnsureAttackSpeedStateSynced()`
  - `EnsureAttackDamageStateSynced()`
3. Проверка, что хотя бы одна ось включена.
4. Проверка, что игрок в бою (`!outOfCombat`) и есть валидный сэмпл сенсоров.
5. Инкремент «секунд боя» и вычисление `dueSteps` — сколько шагов SGD нужно сделать.
6. Для каждого шага вызов `StepAllAxes(SgdSensorsRuntimeState.Sample)`.

Внутри `StepAllAxes`:

- по каждой включённой оси вызывается соответствующий `Step`*‑метод;
- если хотя бы по одной оси множитель заметно изменился, вызывается:
  - `SgdActuatorsApplier.ApplyToAllLivingMonsters();`
  - `SgdDecisionRuntimeState.RecordGlobalStep(appliedMonsters: appliedCount);`

#### Actuators

- Каталог: `SgdEngine/Actuators/`
- Основные файлы:
  - `SgdActuatorsHooks.cs` — применение множителей к новым монстрам.
  - `SgdActuatorsApplier.cs` — применение множителей ко всем живым монстрам.
  - `SgdActuatorsRuntimeState.cs` — текущие значения множителей.
  - `SgdGeneStatTokenApplier.cs` — низкоуровневое применение множителей через Gene‑токены.
- Принцип работы:
  - `SgdActuatorsRuntimeState` содержит по одной величине множителя на каждую ось (`MaxHealthMultiplier`, `MoveSpeedMultiplier` и т.д.).
  - При изменении этих значений:
    - новые монстры получают актуальные множители при `CharacterBody.Start`;
    - уже живые монстры обновляются через `SgdActuatorsApplier.ApplyToAllLivingMonsters()`.

### Математическая модель (качественно)

#### Оси сложности и параметр ( theta )

Для каждой оси сложности (HP, MoveSpeed, AttackSpeed, AttackDamage) поддерживается свой параметр ( theta ) в лог‑пространстве:

- Пусть ( m ) — множитель параметра (например, множитель HP).
- Тогда ( m = e^{theta} ).
- Ограничения множителя задаются диапазоном ([m_{min}, m_{max}]), который приходит из `SgdAxisLimitProvider`:
  - `GetMaxHealthLimits(out floor, out cap)` и аналогичные методы.
- В лог‑пространстве это диапазон ([theta_{min}, theta_{max}]) с:
  - ( theta_{min} = log(m_{min}) )
  - ( theta_{max} = log(m_{max}) )

Использование лог‑пространства приводит к более естественной и стабильной адаптации, поскольку относительные изменения в мультипликативных величинах становятся аддитивными по ( theta ).

#### Оценка skill и challenge

Для каждой оси определяется своя оценка **skill01** (насколько хорошо игрок справляется по этой оси) и **challenge01** (эффективная сложность по оси).

- **Challenge**:
  - Challenge выражается через нормированное положение ( theta ) в диапазоне:
    - [
    challenge01 = mathrm{clamp01}left( frac{theta - theta_{min}}{theta_{max} - theta_{min}} right)
    ]
  - Чем выше ( theta ), тем выше challenge.
- **Skill**:
  - Для каждой оси есть соответствующая функция `Estimate*Skill01(in SgdSensorsSample s)`:
    - `EstimateMaxHealthSkill01` — опирается на исходящий урон, TTK и безопасность (low HP).
    - `EstimateMoveSpeedSkill01` — использует уклонение, выживаемость и частично исходящий урон.
    - `EstimateAttackSpeedSkill01` — использует «стресс» игрока: частоту попаданий по нему, входящий урон, low HP, смерти.
    - `EstimateAttackDamageSkill01` — фокусируется на выживаемости и смертях под давлением.
  - Каждая функция строит линейную комбинацию нормализованных сенсоров с весами, далее результат ограничивается ([0, 1]).

#### Функция ошибки и шаг SGD

Для каждой оси вычисляется ошибка:

- [
error = challenge01 - skill01
]
- Интерпретация:
  - ( error > 0 ) — сложность выше, чем «уровень игры» игрока ⇒ нужно упростить (уменьшить множитель).
  - ( error < 0 ) — игроку слишком легко ⇒ нужно усложнить (увеличить множитель).
  - Малая по модулю ошибка в пределах `DefaultErrorDeadZone` считается нулевой.

Далее происходит шаг SGD (качественно, без точной ссылки на код):

1. Нормализованный градиент:
  - [
   gradient = 2 cdot error cdot frac{1}{theta_{max} - theta_{min}}
   ]
  - Градиент клиппируется по модулю `DefaultGradientClip`.
2. Обновление скорости (momentum):
  - [
   v leftarrow momentum cdot v + gradient
   ]
  - Затем ( v ) также клиппируется до `DefaultVelocityClip`.
3. Обновление ( theta ) с учётом learning rate и ограничения на шаг:
  - [
   Delta theta = mathrm{clamp}(text{lr} cdot v, -Deltatheta_{max}, Deltatheta_{max})
   ]
  - Новое значение:
    - ( theta_{new} = mathrm{clamp}(theta - Delta theta, theta_{min}, theta_{max}) )
4. Перевод обратно к множителю:
  - ( m_{new} = e^{theta_{new}} )
  - Значение сохраняется в `SgdActuatorsRuntimeState` и синхронизируется в `SgdDecisionRuntimeState`.

Если изменение множителя по модулю меньше `AxisApplyEpsilon`, считается, что по этой оси «визуально» ничего не изменилось, и актуаторы могут не обновлять всех живых монстров по мелочи.

### Состояние и лимиты осей

#### SgdAxisLimitProvider

- Файл: `SgdEngine/SgdAxisLimitProvider.cs`
- Назначение:
  - Централизованно задаёт диапазоны `floor` / `cap` для множителей по каждой оси:
    - HP: `GetMaxHealthLimits(out floor, out cap)`
    - MoveSpeed: `GetMoveSpeedLimits(...)`
    - AttackSpeed: `GetAttackSpeedLimits(...)`
    - AttackDamage: `GetAttackDamageLimits(...)`
  - Эти диапазоны:
    - гарантируют, что монстры не станут «сломано» слабыми или сильными;
    - позволяют согласованно настраивать минимальную/максимальную сложность.

#### Синхронизация ( theta ) и актуаторов

Каждая ось имеет два источника истины:

- **Решение (SGD)**:
  - хранит ( theta ) и скорость (velocity) для обучения.
- **Актуаторы**:
  - хранят текущий множитель, реально применяемый к монстрам.

Методы `Ensure*StateSynced()` выполняют следующее:

- Если состояния оси в `SgdDecisionRuntimeState` ещё нет:
  - инициализируют ( theta ) на основе текущего множителя из `SgdActuatorsRuntimeState`, приведённого к ([theta_{min}, theta_{max}]).
- Если различие между ожидаемым множителем (из ( theta )) и фактическим в актуаторах превышает `ExternalSyncEpsilon`:
  - пересчитывают ( theta ) из множителя, обнуляют скорость и синхронизируют состояние.

Это позволяет:

- безопасно изменять множители извне (например, через консольные команды или debug‑панель);
- сохранять корректность SGD даже при ручной подстройке сложности.

### Research telemetry v2 (для проверки гипотез H1–H6)

Модуль `Telemetry/` формирует события в PostHog так, чтобы их можно было напрямую использовать для метрик из отчёта:

- `e_i(t) = challenge01_i(t) - skill01_i(t)`:
  - логируется как `axis_*_error` и `axis_*_abs_error`.
- `m_i(t)`:
  - логируется как `axis_*_multiplier` и `axis_*_delta_multiplier`.
- `V_p(t)`:
  - логируется как `virtual_power_total` (+ компоненты offense/defense/mobility).
- `V_c(t)`:
  - логируется как `virtual_challenge_total`.

В `telemetry_schema_version = 2` дополнительно логируются:

- сегментация эксперимента: `participant_id`, `condition_order`, `run_attempt_index`, seeds;
- фиксация порогов проверки: `tau_jump`, `epsilon_v`, `epsilon_stable`, `degradation_threshold`, `recovery_threshold`;
- снимок части гиперпараметров SGD/GA (для воспроизводимости);
- качество данных: `missed_sample_intervals`, `minimum_session_seconds`, `is_quality_excluded_short_session`;
- события UX‑анкеты:
  - `dda_post_session_survey` с полями `fairness_likert_1_7` и `continuity_likert_1_7`;
  - UI реализован в `Telemetry/TelemetrySurveyWidget.cs`.

