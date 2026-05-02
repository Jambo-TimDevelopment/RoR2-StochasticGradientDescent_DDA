## Реализация SGD DDA

Документ связывает идеи SGD DDA с файлами `SgdEngine/` и даёт сжатое качественное описание шага оптимизации. **Покомпонентная семантика четырёх осей**, веса `Estimate*Skill01`, ключи конфига и поля телеметрии `axis_*` вынесены в [`Axes.md`](Axes.md).

### Runtime-драйвер

`SgdEngine/SgdRuntimeDriver.cs` в `Run.Start` регистрирует хуки сенсоров, сбрасывает `SgdDecisionRuntimeState` и `SgdActuatorsRuntimeState`, один раз применяет актуаторы ко всем живым монстрам и вешает на `Run` компонент драйвера. В `Update()` выбирается тело игрока, считается сглаженный \(V_p\) через `SgdVirtualPowerEstimator`, обновляются `SgdRuntimeState` и сенсоры; при `NetworkServer.active` и активном SGD вызывается `SgdDecisionDriver.Tick`.

### Virtual power

`SgdVirtualPowerEstimator.cs` и `SgdVirtualPowerSample.cs` оценивают «виртуальную мощность» билда игрока и сглаживают её; результат используется в рантайме и в телеметрии (`virtual_power_*`), а сенсоры могут опираться на эти же шкалы при нормализации.

### Сенсоры

Каталог `SgdEngine/Sensors/`: хуки (`SgdSensorsHooks`), накопление и нормализация (`SgdSensorsEstimator`), структура выборки (`SgdSensorsSample`), хранилище (`SgdSensorsRuntimeState`). Показатели приводятся к \([0,1]\): доля попаданий по игроку, входящий и исходящий урон, доля времени на низком HP, смерти за окно, TTK. Те же величины входят в разные линейные комбинации для `skill01` по осям (см. [`Axes.md`](Axes.md)).

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

`SgdAxisLimitProvider` читает `ConfigManager.sgdHpFloor`/`sgdHpCap` и парные ключи для MS, AS, DMG, нормализует floor ≤ cap. Методы `Ensure*StateSynced` подтягивают \(\theta\) к фактическим множителям актуаторов после внешних правок; подробности — [`Axes.md`](Axes.md).

### Телеметрия v2 и исследовательские поля

Модуль `Telemetry/` логирует по каждой оси `skill01`, `challenge01`, ошибку, множитель и дельты; агрегат `virtual_challenge_total` строится из логарифмов четырёх множителей в `TelemetrySampleBuilder.ComputeVirtualChallenge`. В схеме фиксируются пороги `tau_jump`, `epsilon_v`, `epsilon_stable`, сегментация эксперимента, снимок части гиперпараметров SGD/GA, качество сессии. Событие опроса H5/H6 — `dda_post_session_survey` (UI в `Telemetry/TelemetrySurveyWidget.cs`). Подробные протоколы H1–H4 — [`ToolsAndDebug.md`](ToolsAndDebug.md).
