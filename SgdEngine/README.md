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

Подробнее про отладку и консоль: `CheatManager/README.md`, `Docs/SgdDda/ToolsAndDebug.md`.

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
