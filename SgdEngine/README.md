**Languages:** [English](#english) · [Русский](#russian)

---

<a id="english"></a>
# SgdEngine — DDA via stochastic gradient descent

This folder implements **difficulty adaptation through SGD** for the GeneticsArtifact mod. Architecture:

```text
Sensors → Decision (SGD) → Actuators
```

- **Sensors** — how the player is performing; smoothed combat signals (damage, hits, TTK, etc.).
- **Decision** — optimization steps on four axes: multipliers `MaxHealth`, `MoveSpeed`, `AttackSpeed`, `AttackDamage` with \(\theta = \ln(\text{multiplier})\).
- **Actuators** — write multipliers into monster inventory via the same **gene tokens** as the genetic engine (`GeneTokenCalc` / RecalculateStats) **without** editing `GeneticEngine/`.

Math, hypotheses H1–H4, and telemetry integration: `Docs/SgdDda/` (`Architecture.md`, `Implementation.md`, `Integration.md`). This file is a **code and file** overview.

## Directory layout

| Path | Role |
|------|------|
| `SgdRuntimeDriver.cs` | `Run.Start` / `Run.BeginGameOver` hooks, component on `Run`: each frame updates \(V_p\), sensors, and (on host) the decision module. |
| `SgdRuntimeState.cs` | Last virtual-power estimate \(V_p\) and body it was computed for. |
| `SgdVirtualPowerEstimator.cs` | Compute and smooth \(V_p\) components (offense / defense / mobility / total). |
| `SgdVirtualPowerSample.cs` | Immutable \(V_p\) snapshot for sensors and debugging. |
| `SgdAxisLimitProvider.cs` | Shared **floor/cap** per axis from `ConfigManager` (`sgdHpFloor`/`sgdHpCap`, …). |
| `Sensors/SgdSensorsHooks.cs` | Harmony: `TakeDamage`, `CharacterBody.OnDestroy`; sensor tick; reset on player body change. |
| `Sensors/SgdSensorsEstimator.cs` | Sliding windows, EMA, normalization; damage/death events. |
| `Sensors/SgdSensorsSample.cs` | Sensor field struct (DPS, hit rate, combat uptime, TTK, …). |
| `Sensors/SgdSensorsRuntimeState.cs` | Latest published sample for Decision and telemetry. |
| `Decision/SgdDecisionDriver.cs` | SGD step logic for all enabled axes: error, gradient, momentum, clipping, actuator sync. |
| `Decision/SgdDecisionRuntimeState.cs` | \(\theta\), velocities, **combat** timer until step, axis flags, last-step telemetry. |
| `Actuators/SgdActuatorsRuntimeState.cs` | Current HP/MS/AS/DMG multipliers (after clamp). |
| `Actuators/SgdGeneStatTokenApplier.cs` | Map multiplier to ±1% token counts and set exact item stacks in `Inventory`. |
| `Actuators/SgdActuatorsApplier.cs` | Walk all living monsters, apply current multipliers + `RecalculateStats`. |
| `Actuators/SgdActuatorsHooks.cs` | `CharacterBody.Start`: new monster spawns get current multipliers. |

## Entry point: `SgdRuntimeDriver`

**Registration:** `SgdRuntimeDriver.RegisterHooks()` is called from the plugin when SGD diagnostic hooks are enabled (`diagnosticsEnableSgdHooks`).

**`Run.Start`:** resets `SgdDecisionRuntimeState` and `SgdActuatorsRuntimeState`, then `SgdActuatorsApplier.ApplyToAllLivingMonsters()` for a deterministic run start; attaches `SgdRuntimeDriver` to `Run` (one per run); if telemetry is on, attaches `TelemetryRuntimeDriver` to the same object to avoid duplicate `Run.Start` detours.

**`Run.BeginGameOver`:** notifies telemetry of run end when enabled.

**Per-frame `Update`:** (1) find player body (`isPlayerControlled`, else `Player` team); (2) on switching to SGD, optionally reset decision state (debug convenience); (3) `SgdVirtualPowerEstimator` → `SgdRuntimeState.SetVirtualPower`; (4) `SgdSensorsHooks.Tick`; (5) if `ActiveAlgorithm == Sgd` and **`NetworkServer.active`**, `SgdDecisionDriver.Tick`.

**\(V_p\) and sensors** may run locally; **SGD steps and mass monster updates** run on the host only.

## Sensors (`Sensors/`)

Goal: one coherent `SgdSensorsSample` with normalized \([0,1]\) fields where marked `*Norm01`.

**Hooks:** `HealthComponent.TakeDamage` (incoming to tracked player, outgoing from player to monsters); `CharacterBody.OnDestroy` (player deaths proxy, monster deaths for TTK/counters).

**`SgdSensorsEstimator`:** damage rates, hit rate on player, combat/low-HP uptime, deaths in window, mean TTK, etc.; EMA and time windows (see code).

**`SgdSensorsHooks.Reset(CharacterBody)`** resets estimator and `SgdSensorsRuntimeState` when the controlled body changes (`Tick` detects `playerBody` change).

## Player virtual power \(V_p\)

**`SgdVirtualPowerEstimator`** builds `SgdVirtualPowerSample` from body stats and inventory (log-like compression, time smoothing).

**`SgdRuntimeState`** stores the latest sample and body name — debug overlay, telemetry, consistency with sensors (`GetCurrentSample(vp)` in the estimator–sensor link).

## Decision (`Decision/`)

**`SgdDecisionDriver`** is the SGD core.

**When it steps:** `DdaAlgorithmType.Sgd`, host, valid `dt`, at least one axis enabled; player **in combat** (`!outOfCombat`); `SgdSensorsRuntimeState.HasSample`.

**Step timer:** only **combat** time accumulates (`AddCombatSeconds`). Interval: `SgdDecisionRuntimeState.StepSeconds` (config + console `dda_sgd_step_time`). Large `dt` can trigger multiple steps (`ConsumeDueSteps`).

**Parameterization:** per-axis \(m \in [m_{\min}, m_{\max}]\) from `SgdAxisLimitProvider`; optimization uses \(\theta = \ln m\).

**Per-axis signal (HP example):** `challenge01` — normalized lever position of \(\theta\); `skill01` — weighted sensors (`Estimate*Skill01` differs per axis); `error = challenge01 - skill01` (positive → decrease \(\theta\), see `Step*`).

**Optimization:** gradient from quadratic error form, **momentum**, gradient/velocity clipping, per-axis `deltaTheta` caps (`HpLearningRate`, `HpMaxDeltaTheta`, …).

**Actuator sync:** if multipliers changed externally (`dda_actuator_*`), \(\theta\) and velocity resync from `SgdActuatorsRuntimeState` (`Ensure*StateSynced`).

After a step, if multipliers change, `SgdActuatorsApplier.ApplyToAllLivingMonsters()` runs and affected body count is recorded.

## Actuators (`Actuators/`)

**`SgdActuatorsRuntimeState`** is the source of truth for the four multipliers (clamped via `SgdAxisLimitProvider` on set).

**`SgdGeneStatTokenApplier`:** clamp multiplier; map \(m\) to token count (**1 token ≈ ±1%** from base, `(m-1)*100`, rounding); set exact plus/minus stacks from `GeneTokens.tokenDict` (idempotent). Keeps compatibility with `GeneTokenCalc` without duplicating percentage logic.

**Two application paths:** (1) **`SgdActuatorsHooks`** — new monsters on `CharacterBody.Start`; (2) **`SgdActuatorsApplier`** — mass refresh after SGD or console. Both require **`NetworkServer.active`** and `Sgd` mode (see code guards).

## Axis limits: `SgdAxisLimitProvider`

Reads BepInEx **SGD Axis Limits** (`ConfigManager`): separate floor/cap for HP, MoveSpeed, AttackSpeed, AttackDamage. Swaps if cap < floor. Used in Decision (\(\theta\) bounds) and `SgdGeneStatTokenApplier.Clamp`.

## Diagnostic flags

In `GeneticsArtifactPlugin.Awake`: SGD hooks (`SgdRuntimeDriver`, sensors); optionally **`SgdActuatorsHooks`** (`diagnosticsEnableSgdActuatorsHooks`) — if off, new spawns do not auto-patch (isolate bugs).

More debugging: [`CheatManager/README.md`](../CheatManager/README.md#english), [`Docs/SgdDda/ToolsAndDebug.md`](../Docs/SgdDda/ToolsAndDebug.md#english).

## Relation to the rest of the mod

Mode switching: `DdaAlgorithmState` (`CheatManager`), console `dda_algorithm`, run rotation `DdaRunModeRotator`. GA code is **not** rewritten; SGD uses the same `GeneStat` and tokens. Telemetry reads sensor/decision/actuator/\(V_p\) state without deciding difficulty — `Telemetry/` module.

## Quick source links

| Task | File |
|------|------|
| Run lifecycle and Update | `SgdRuntimeDriver.cs` |
| Step formulas and skill01 | `Decision/SgdDecisionDriver.cs` |
| Sensor sample contents | `Sensors/SgdSensorsSample.cs`, `SgdSensorsEstimator.cs` |
| Applying to a monster | `Actuators/SgdGeneStatTokenApplier.cs`, `SgdActuatorsApplier.cs`, `SgdActuatorsHooks.cs` |

---

<a id="russian"></a>
# SgdEngine — DDA на стохастическом градиентном спуске

Папка содержит реализацию **адаптации сложности через SGD** для мода GeneticsArtifact. Архитектура следует цепочке:

```text
Sensors → Decision (SGD) → Actuators
```

- **Sensors** — оценка «как играет игрок» и сглаженные сигналы боя (урон, попадания, TTK и т.д.).
- **Decision** — шаги оптимизации по четырём осям: множители `MaxHealth`, `MoveSpeed`, `AttackSpeed`, `AttackDamage` в параметризации \(\theta = \ln(\text{multiplier})\).
- **Actuators** — запись множителей в инвентарь монстров через те же **GeneToken**, что и генетический движок (`GeneTokenCalc` / RecalculateStats), без правок кода в `GeneticEngine/`.

Математика целевой функции, гипотезы H1–H4 и интеграция с телеметрией описаны в `Docs/SgdDda/` (в частности `Architecture.md`, `Implementation.md`, `Integration.md`). Здесь — обзор **кода и файлов**.

---

## Структура каталога

| Путь | Роль |
|------|------|
| `SgdRuntimeDriver.cs` | Хуки `Run.Start` / `Run.BeginGameOver`, компонент на объекте `Run`: каждый кадр обновляет \(V_p\), сенсоры и (на хосте) модуль решений. |
| `SgdRuntimeState.cs` | Хранение последней оценки виртуальной силы игрока \(V_p\) и тела, для которого она посчитана. |
| `SgdVirtualPowerEstimator.cs` | Расчёт и сглаживание компонентов \(V_p\) (offense / defense / mobility / total). |
| `SgdVirtualPowerSample.cs` | Неизменяемый снимок \(V_p\) для передачи в сенсоры и отладку. |
| `SgdAxisLimitProvider.cs` | Единые **пол и потолок** множителей по осям из `ConfigManager` (`sgdHpFloor`/`sgdHpCap`, …). |
| `Sensors/SgdSensorsHooks.cs` | Harmony: `TakeDamage`, `CharacterBody.OnDestroy`; тик сенсоров; сброс при смене тела игрока. |
| `Sensors/SgdSensorsEstimator.cs` | Скользящие окна, EMA, нормализации; события урона/смертей. |
| `Sensors/SgdSensorsSample.cs` | Структура полей сенсоров (DPS, hit rate, combat uptime, TTK, …). |
| `Sensors/SgdSensorsRuntimeState.cs` | Последний опубликованный сэмпл для Decision и телеметрии. |
| `Decision/SgdDecisionDriver.cs` | Логика шага SGD по всем включённым осям: ошибка, градиент, momentum, клиппинг, синхронизация с актуаторами. |
| `Decision/SgdDecisionRuntimeState.cs` | \(\theta\), скорости, таймер **боевого** времени до шага, флаги осей, телеметрия последнего шага. |
| `Actuators/SgdActuatorsRuntimeState.cs` | Текущие множители HP/MS/AS/DMG (после клампа). |
| `Actuators/SgdGeneStatTokenApplier.cs` | Перевод множителя в число токенов ±1% и выставление точного количества предметов в `Inventory`. |
| `Actuators/SgdActuatorsApplier.cs` | Пройти всех живых монстров и применить текущие множители + `RecalculateStats`. |
| `Actuators/SgdActuatorsHooks.cs` | `CharacterBody.Start`: новые спавны монстров получают актуальные множители. |

---

## Точка входа: `SgdRuntimeDriver`

**Регистрация:** `SgdRuntimeDriver.RegisterHooks()` вызывается из плагина при включённом флаге диагностики SGD (`diagnosticsEnableSgdHooks`).

**`Run.Start`:**

- Сбрасывает `SgdDecisionRuntimeState` и `SgdActuatorsRuntimeState`, затем `SgdActuatorsApplier.ApplyToAllLivingMonsters()` — детерминированный старт забега.
- Вешает на `Run` компонент `SgdRuntimeDriver` (один на забег).
- При включённой телеметрии подключает `TelemetryRuntimeDriver` на тот же объект (чтобы не дублировать детур `Run.Start`).

**`Run.BeginGameOver`:** уведомление телеметрии о конце забега (если телеметрия включена).

**`Update` каждый кадр:**

1. Находит тело игрока (`isPlayerControlled`, иначе команда `Player`).
2. При смене активного алгоритма на SGD сбрасывает состояние решения (удобство отладки).
3. `SgdVirtualPowerEstimator` → `SgdRuntimeState.SetVirtualPower`.
4. `SgdSensorsHooks.Tick` обновляет `SgdSensorsRuntimeState`.
5. Если `ActiveAlgorithm == Sgd` и **`NetworkServer.active`**, вызывается `SgdDecisionDriver.Tick`.

Итог: **оценка \(V_p\) и сенсоры** могут считаться локально; **шаг SGD и массовое применение к монстрам** выполняются только на хосте.

---

## Sensors (`Sensors/`)

**Идея:** из потока боя получить один согласованный `SgdSensorsSample` с нормализованными величинами в \([0,1]\) там, где это `*Norm01`.

**Хуки:**

- `HealthComponent.TakeDamage` — входящий урон по отслеживаемому игроку; исходящий — от игрока по монстру.
- `CharacterBody.OnDestroy` — смерть игрока (прокси) и монстров (TTK и счётчики).

**`SgdSensorsEstimator`:** накапливает скорости урона, частоту попаданий по игроку, долю боя / низкого HP, смерти в окне, среднее время убийства монстров и т.д.; сглаживание через EMA и окна времени (детали в коде).

**`SgdSensorsHooks.Reset(CharacterBody)`** — при смене контролируемого тела сбрасываются оценщик и `SgdSensorsRuntimeState` (вызывается из `Tick` при смене `playerBody`).

---

## Виртуальная сила игрока \(V_p\)

**`SgdVirtualPowerEstimator`** строит `SgdVirtualPowerSample` из характеристик тела и инвентаря (сжатие вроде `log1p`, сглаживание по времени).

**`SgdRuntimeState`** хранит последний сэмпл и имя тела — для дебаг-оверлея, телеметрии и согласованности с сенсорами (`GetCurrentSample(vp)` в связке оценщика сенсоров).

---

## Decision (`Decision/`)

**`SgdDecisionDriver`** — ядро SGD.

**Когда шагает:**

- Режим `DdaAlgorithmType.Sgd`, хост, есть валидный `dt`, включена хотя бы одна ось.
- Игрок **в бою** (`!outOfCombat`).
- Есть актуальный сэмпл сенсоров (`SgdSensorsRuntimeState.HasSample`).

**Таймер шага:** накапливается только **боевое** время (`AddCombatSeconds`). Интервал задаётся `SgdDecisionRuntimeState.StepSeconds` (конфиг + консоль `dda_sgd_step_time`). За один кадр может выполниться несколько шагов, если `dt` большой (`ConsumeDueSteps`).

**Параметризация:** для каждой оси множитель \(m \in [m_{\min}, m_{\max}]\) из `SgdAxisLimitProvider`; внутри оптимизации \(\theta = \ln m\).

**Сигнал на оси (пример для HP):**

- `challenge01` — нормализованная «виртуальная сложность» по оси: положение \(\theta\) в диапазоне \([\theta_{\min}, \theta_{\max}]\).
- `skill01` — оценка навыка игрока по этой оси из сенсоров (разные веса для HP / MS / AS / DMG — см. `Estimate*Skill01` в `SgdDecisionDriver`).
- `error = challenge01 - skill01` (положительный — игроку тяжелее целевому балансу → уменьшение \(\theta\), см. знак в `Step*`).

**Оптимизация:** градиент с квадратичной формой по ошибке, **momentum**, клиппинг градиента и скорости, ограничение `deltaTheta` per axis (константы `HpLearningRate`, `HpMaxDeltaTheta`, …).

**Синхронизация с актуаторами:** если множители менялись снаружи (консоль `dda_actuator_*`), \(\theta\) и скорость пересчитываются из текущего `SgdActuatorsRuntimeState` (`Ensure*StateSynced`).

После шага при изменении множителей вызывается `SgdActuatorsApplier.ApplyToAllLivingMonsters()` и записывается число затронутых тел в runtime state шага.

---

## Actuators (`Actuators/`)

**`SgdActuatorsRuntimeState`** — источник истины для текущих четырёх множителей (с клампом через `SgdAxisLimitProvider` при установке).

**`SgdGeneStatTokenApplier`:**

- Клампит множитель по оси.
- Переводит \(m\) в чистое число ген-токенов: **1 токен ≈ ±1%** от базы (`(m - 1) * 100`, округление).
- Выставляет точное количество plus/minus предметов из `GeneTokens.tokenDict` (идемпотентно).

Так достигается совместимость с существующим пайплайном `GeneTokenCalc` без дублирования логики процентов в другом месте.

**Два пути применения:**

1. **`SgdActuatorsHooks`** — каждый новый монстр в `CharacterBody.Start` получает актуальные множители.
2. **`SgdActuatorsApplier`** — массовое обновление уже заспавненных монстров после шага SGD или ручных команд.

Оба пути требуют **`NetworkServer.active`** и режима `Sgd` (см. проверки в коде).

---

## Лимиты осей: `SgdAxisLimitProvider`

Читает из BepInEx-конфига секцию **SGD Axis Limits** (`ConfigManager`): отдельные пары floor/cap для HP, MoveSpeed, AttackSpeed, AttackDamage. При некорректном порядке (cap < floor) значения нормализуются. Используется и в Decision (границы \(\theta\)), и в `SgdGeneStatTokenApplier.Clamp`.

---

## Флаги диагностики

В `GeneticsArtifactPlugin.Awake` отдельно включаются:

- хуки SGD (`SgdRuntimeDriver`, сенсоры),
- опционально **`SgdActuatorsHooks`** (`diagnosticsEnableSgdActuatorsHooks`) — если выключено, новые монстры не получают авто-патч при спавне (удобно для изоляции багов).

Подробнее про отладку и консоль: [`CheatManager/README.md`](../CheatManager/README.md#russian), [`Docs/SgdDda/ToolsAndDebug.md`](../Docs/SgdDda/ToolsAndDebug.md#russian).

---

## Связь с остальным модом

- Переключение режима: `DdaAlgorithmState` (`CheatManager`), консоль `dda_algorithm`, ротация забегов `DdaRunModeRotator`.
- Генетический алгоритм **не** переписывается; SGD использует те же `GeneStat` и токены, что и GA.
- Телеметрия читает `SgdSensorsRuntimeState`, состояния Decision/Actuators и \(V_p\) без участия в решении — модуль `Telemetry/`.

---

## Быстрые ссылки на исходники

| Задача | Файл |
|--------|------|
| Жизненный цикл забега и Update | `SgdRuntimeDriver.cs` |
| Формулы шага и skill01 по осям | `Decision/SgdDecisionDriver.cs` |
| Что именно в сэмпле сенсоров | `Sensors/SgdSensorsSample.cs`, `SgdSensorsEstimator.cs` |
| Применение к монстру | `Actuators/SgdGeneStatTokenApplier.cs`, `SgdActuatorsApplier.cs`, `SgdActuatorsHooks.cs` |
