**Languages:** [English](#english) · [Русский](#russian)

---

<a id="english"></a>
## SGD DDA implementation

This document connects SGD DDA ideas to files under `SgdEngine/` and gives a compact qualitative description of the optimization step. **Per-axis semantics**, `Estimate*Skill01` weights, config keys, and `axis_*` telemetry are in [`Axes.md`](Axes.md#english).

### Runtime driver

`SgdEngine/SgdRuntimeDriver.cs` on `Run.Start` registers sensor hooks, resets `SgdDecisionRuntimeState` and `SgdActuatorsRuntimeState`, applies actuators once to all living monsters, and attaches the driver component to `Run`. In `Update()` it picks the player body, computes smoothed \(V_p\) via `SgdVirtualPowerEstimator`, updates `SgdRuntimeState` and sensors; with `NetworkServer.active` and SGD on, it calls `SgdDecisionDriver.Tick`.

### Virtual power

`SgdVirtualPowerEstimator.cs` and `SgdVirtualPowerSample.cs` estimate and smooth the player’s “virtual power”; results feed runtime and telemetry (`virtual_power_*`), and sensors may use the same scales when normalizing.

### Sensors

Folder `SgdEngine/Sensors/`: hooks (`SgdSensorsHooks`), accumulation/normalization (`SgdSensorsEstimator`), sample struct (`SgdSensorsSample`), store (`SgdSensorsRuntimeState`). Metrics are mapped to \([0,1]\): hit rate on player, incoming/outgoing damage, low-HP uptime, deaths in window, TTK. The same quantities enter different linear combinations for per-axis `skill01` (see [`Axes.md`](Axes.md#english)).

### Decision

Folder `SgdEngine/Decision/`: `SgdDecisionDriver` implements per-axis steps; `SgdDecisionRuntimeState` holds \(\theta\), momentum velocities, axis enable flags, last-step metrics, counters. Shared hyperparameters: `DefaultMomentum`, `DefaultGradientClip`, `DefaultVelocityClip`, `DefaultErrorDeadZone`. Per-axis learning rates and \(\Delta\theta\) caps: `HpLearningRate` / `HpMaxDeltaTheta` and MS/AS/DMG analogs in `SgdDecisionDriver`.

`Tick` flow: server/mode/body/`dt` checks; `EnsureAxisStatesSynced()` for all four axes; exit if every axis is off; learn only in combat with `HasSample`; add combat seconds and `ConsumeDueSteps()`; for each due step, `StepAllAxes(sample)`.

`StepAllAxes` calls `StepMaxHealth` / `StepMoveSpeed` / `StepAttackSpeed` / `StepAttackDamage` for enabled axes only. If any multiplier changes beyond `AxisApplyEpsilon`, `SgdActuatorsApplier.ApplyToAllLivingMonsters()` then `RecordGlobalStep(appliedMonsters)`.

### Actuators

Folder `SgdEngine/Actuators/`: spawn hooks, `SgdActuatorsApplier` for live monsters, `SgdActuatorsRuntimeState` with four multipliers, `SgdGeneStatTokenApplier` for `GeneStat` tokens.

### Step math (qualitative)

Per axis \(m = e^{\theta}\), \(\theta \in [\theta_{\min}, \theta_{\max}]\) from multiplier floor/cap. **challenge01** linearly normalizes \(\theta\) on that segment (lever position). **skill01** is a weighted sensor sum from `Estimate*Skill01`, clipped to \([0,1]\).

Error \(\text{error} = \text{challenge01} - \text{skill01}\): positive means difficulty is “too high” vs observed skill; the step decreases \(\theta\) and eases the fight. Small |error| is zeroed in the dead zone.

Then: gradient \(\propto \text{error} / (\theta_{\max}-\theta_{\min})\) with clipping, momentum update with clipping, \(\Delta\theta = \text{lr} \cdot v\) capped, \(\theta_{\text{new}} = \text{clamp}(\theta - \Delta\theta)\), multiplier \(e^{\theta_{\text{new}}}\) to `SgdActuatorsRuntimeState` and sync in `SgdDecisionRuntimeState`. Coefficients match `SgdDecisionDriver`.

### Limits and sync

`SgdAxisLimitProvider` reads `ConfigManager.sgdHpFloor`/`sgdHpCap` and MS/AS/DMG pairs; normalizes floor ≤ cap. `Ensure*StateSynced` realigns \(\theta\) to actuator multipliers after external edits; details in [`Axes.md`](Axes.md#english).

### Telemetry v2 and research fields

`Telemetry/` logs per axis `skill01`, `challenge01`, error, multiplier, deltas; `virtual_challenge_total` aggregates log multipliers in `TelemetrySampleBuilder.ComputeVirtualChallenge`. The schema records `tau_jump`, `epsilon_v`, `epsilon_stable`, experiment segmentation, partial SGD/GA hyperparameter snapshots, session quality. H5/H6 survey event `dda_post_session_survey` (UI in `Telemetry/TelemetrySurveyWidget.cs`). H1–H4 protocols: [`ToolsAndDebug.md`](ToolsAndDebug.md#english).

---

<a id="russian"></a>
## Реализация SGD DDA

Документ связывает идеи SGD DDA с файлами `SgdEngine/` и даёт сжатое качественное описание шага оптимизации. **Покомпонентная семантика четырёх осей**, веса `Estimate*Skill01`, ключи конфига и поля телеметрии `axis_*` вынесены в [`Axes.md`](Axes.md#russian).

### Runtime-драйвер

`SgdEngine/SgdRuntimeDriver.cs` в `Run.Start` регистрирует хуки сенсоров, сбрасывает `SgdDecisionRuntimeState` и `SgdActuatorsRuntimeState`, один раз применяет актуаторы ко всем живым монстрам и вешает на `Run` компонент драйвера. В `Update()` выбирается тело игрока, считается сглаженный \(V_p\) через `SgdVirtualPowerEstimator`, обновляются `SgdRuntimeState` и сенсоры; при `NetworkServer.active` и активном SGD вызывается `SgdDecisionDriver.Tick`.

### Virtual power

`SgdVirtualPowerEstimator.cs` и `SgdVirtualPowerSample.cs` оценивают «виртуальную мощность» билда игрока и сглаживают её; результат используется в рантайме и в телеметрии (`virtual_power_*`), а сенсоры могут опираться на эти же шкалы при нормализации.

### Сенсоры

Каталог `SgdEngine/Sensors/`: хуки (`SgdSensorsHooks`), накопление и нормализация (`SgdSensorsEstimator`), структура выборки (`SgdSensorsSample`), хранилище (`SgdSensorsRuntimeState`). Показатели приводятся к \([0,1]\): доля попаданий по игроку, входящий и исходящий урон, доля времени на низком HP, смерти за окно, TTK. Те же величины входят в разные линейные комбинации для `skill01` по осям (см. [`Axes.md`](Axes.md#russian)).

### Decision

Каталог `SgdEngine/Decision/`: `SgdDecisionDriver` реализует шаги по осям; `SgdDecisionRuntimeState` хранит \(\theta\), скорости momentum, флаги включения осей, метрики последнего шага и счётчики. Общие гиперпараметры — `DefaultMomentum`, `DefaultGradientClip`, `DefaultVelocityClip`, `DefaultErrorDeadZone`. Пер-осевые скорости обучения и потолки \(\Delta\theta` — константы `HpLearningRate` / `HpMaxDeltaTheta` и аналоги для MS, AS, DMG в `SgdDecisionDriver`.

Цикл `Tick`: проверки сервера, режима SGD, тела и `dt`; `EnsureAxisStatesSynced()` для всех четырёх осей; выход, если все оси выключены; обучение только в бою и при `HasSample`; инкремент секунд боя и `ConsumeDueSteps()`; для каждого due-step — `StepAllAxes(sample)`.

`StepAllAxes` вызывает `StepMaxHealth` / `StepMoveSpeed` / `StepAttackSpeed` / `StepAttackDamage` только для включённых осей. Если хотя бы по одной оси множитель изменился существеннее `AxisApplyEpsilon`, вызывается `SgdActuatorsApplier.ApplyToAllLivingMonsters()`, затем `RecordGlobalStep(appliedMonsters)`.

### Actuators

Каталог `SgdEngine/Actuators/`: хуки на спавн, `SgdActuatorsApplier` для живых монстров, `SgdActuatorsRuntimeState` с четырьмя множителями, `SgdGeneStatTokenApplier` для записи токенов `GeneStat`.

### Математическая модель шага (качественно)

На каждой оси \(m = e^{\theta}\), \(\theta \in [\theta_{\min}, \theta_{\max}]\) из floor/cap множителя. **challenge01** — линейная нормализация \(\theta\) на этом отрезке (интерпретация — положение рычага сложности). **skill01** — взвешенная сумма сенсоров из `Estimate*Skill01`, значение в \([0,1]\).

Ошибка \(\text{error} = \text{challenge01} - \text{skill01}\): при превышении «вкрученности» над наблюдаемым скиллом ошибка положительна, шаг уменьшает \(\theta\) и облегчает бой. Малая ошибка по модулю обнуляется в dead zone.

Далее: градиент \(\propto \text{error} / (\theta_{\max}-\theta_{\min})\) с клиппингом, обновление скорости с momentum и клиппингом, \(\Delta\theta = \text{lr} \cdot v\) с ограничением по модулю, \(\theta_{\text{new}} = \text{clamp}(\theta - \Delta\theta)\), множитель \(e^{\theta_{\text{new}}}\) в `SgdActuatorsRuntimeState` и синхронизация в `SgdDecisionRuntimeState`. Точные коэффициенты совпадают с `SgdDecisionDriver`.

### Лимиты и синхронизация

`SgdAxisLimitProvider` читает `ConfigManager.sgdHpFloor`/`sgdHpCap` и парные ключи для MS, AS, DMG, нормализует floor ≤ cap. Методы `Ensure*StateSynced` подтягивают \(\theta\) к фактическим множителям актуаторов после внешних правок; подробности — [`Axes.md`](Axes.md#russian).

### Телеметрия v2 и исследовательские поля

Модуль `Telemetry/` логирует по каждой оси `skill01`, `challenge01`, ошибку, множитель и дельты; агрегат `virtual_challenge_total` строится из логарифмов четырёх множителей в `TelemetrySampleBuilder.ComputeVirtualChallenge`. В схеме фиксируются пороги `tau_jump`, `epsilon_v`, `epsilon_stable`, сегментация эксперимента, снимок части гиперпараметров SGD/GA, качество сессии. Событие опроса H5/H6 — `dda_post_session_survey` (UI в `Telemetry/TelemetrySurveyWidget.cs`). Подробные протоколы H1–H4 — [`ToolsAndDebug.md`](ToolsAndDebug.md#russian).
