**Languages:** [English](#english) · [Русский](#russian)

---

<a id="english"></a>
# Four SGD DDA difficulty axes

In SGD mode the mod maintains **four levers** that scale enemy stats through the same `GeneStat` values as the genetic algorithm: **MaxHealth**, **MoveSpeed**, **AttackSpeed**, **AttackDamage**. In [`RULE.md`](../../RULE.md#english) terms this is the main handle on **\(V_c\)** — numeric world difficulty: each multiplier directly changes monster stats. An axis is not a separate “sensor”; it is a **one-dimensional adaptation parameter** with its own \(\theta\), target `skill01 ≈ challenge01`, and config limits.

\(\theta\) lives in **log space**: for multiplier \(m > 0\), \(m = e^{\theta}\). Relative stat changes stay multiplicative in-game while optimizer steps are stable in \(\theta\). Per-axis min/max \(m\) are **floor / cap**; in code \(\theta_{\min} = \ln(\text{floor})\), \(\theta_{\max} = \ln(\text{cap})\). `SgdAxisLimitProvider` centralizes this, reading `ConfigManager` (BepInEx section **SGD Axis Limits**).

**challenge01** for an axis is the normalized position of \(\theta\) on \([\theta_{\min}, \theta_{\max}]\):

\[
\text{challenge01} = \mathrm{clamp}_{[0,1]}\left(\frac{\theta - \theta_{\min}}{\theta_{\max} - \theta_{\min}}\right).
\]

Interpretation: **how far the difficulty lever is turned up** on that axis within the allowed range. Zero is floor multiplier, one is cap.

**skill01** estimates player “performance” **for that axis**: a linear mix of the **same** normalized sensors in `SgdSensorsSample` with **different weights** in `Estimate*Skill01` inside `SgdEngine/Decision/SgdDecisionDriver.cs`. Every axis on a step sees the **same sensor sample** (latest `SgdSensorsRuntimeState`), so combat signals are shared; what differs is **which signals matter** for HP, move speed, attack speed, and damage. In code these are **four separate 1D SGD problems**: no single joint gradient over all \(\theta\) at once, but axes are statistically linked through shared combat telemetry.

Per-axis error: \(\text{error} = \text{challenge01} - \text{skill01}\) (in `SgdDecisionDriver`, positive error drives **decreasing** \(\theta\), i.e. easing). Small |error| inside `DefaultErrorDeadZone` is zeroed.

Axis summary (telemetry names are `dda_sample` field prefixes; see `Telemetry/TelemetrySampleBuilder.cs`):

| Axis | `GeneStat` | Skill estimate | Dominant sensors in skill01 (weights) | Floor / cap in `ConfigManager` | `axis_*` prefix | `axis_*_plane` |
|------|------------|----------------|----------------------------------------|----------------------------------|-----------------|-----------------|
| Enemy HP | `MaxHealth` | `EstimateMaxHealthSkill01` | outgoing DPS 0.45; TTK 0.45; low HP 0.10 | `sgdHpFloor`, `sgdHpCap` | `axis_max_health_` | `hp` |
| Move speed | `MoveSpeed` | `EstimateMoveSpeedSkill01` | evasion 0.45; incoming 0.25; outgoing 0.20; low HP 0.10 | `sgdMsFloor`, `sgdMsCap` | `axis_move_speed_` | `moveSpeed` |
| Attack speed | `AttackSpeed` | `EstimateAttackSpeedSkill01` | evasion 0.40; incoming 0.35; low HP 0.20; deaths 0.05 | `sgdAsFloor`, `sgdAsCap` | `axis_attack_speed_` | `attackSpeed` |
| Attack damage | `AttackDamage` | `EstimateAttackDamageSkill01` | incoming 0.45; low HP 0.30; deaths 0.20; evasion 0.05 | `sgdDmgFloor`, `sgdDmgCap` | `axis_attack_damage_` | `damage` |

Per axis, events include multiplier, `skill01`, `challenge01`, `error`, `abs_error`, multiplier and \(\Delta\theta\) deltas, `is_jump`, etc. Same logic as H1–H4 reporting in [`ToolsAndDebug.md`](ToolsAndDebug.md#english).

**`virtual_challenge_total`** in telemetry is a separate scalar “multiplier difficulty” summary: weighted sum of \(\ln m\) over the four current multipliers in the difficulty snapshot (`ComputeVirtualChallenge`). It pairs with **`virtual_power_total`** (\(V_p\)) for reporting; **SGD binding to axes** uses per-component `challenge01` and per-axis steps, not this aggregate alone. Why H3 is checked per-axis, not only via \(V_p\) / \(V_c\), see [`ToolsAndDebug.md`](ToolsAndDebug.md#english).

### MaxHealth (HP)

HP mainly affects **horde durability**: higher multiplier means longer kills. `EstimateMaxHealthSkill01` targets **kill efficiency**: normalized outgoing damage, TTK proxy, and low-health uptime. High skill means the player **deals damage and clears quickly** without living on the edge.

`challenge01` is how far the HP multiplier sits in `[sgdHpFloor, sgdHpCap]`. Error compares that lever position to observed kill performance.

Step hyperparameters in `SgdDecisionDriver`: **`HpLearningRate = 0.22`**, **`HpMaxDeltaTheta = 0.060`** (~6.2% relative multiplier step cap in worst case per code comment).

### MoveSpeed (MS)

Enemy move speed pressures **positioning and kiting**. `EstimateMoveSpeedSkill01` emphasizes **evasion** (low normalized hit rate on player) and **survivability** vs incoming damage; outgoing damage adds a small “fighting while moving” term.

Config: **`sgdMsFloor`**, **`sgdMsCap`**. Step constants: **`MsLearningRate = 0.20`**, **`MsMaxDeltaTheta = 0.050`**.

### AttackSpeed (AS)

Attack speed sets **pressure frequency**. Code comments tie the axis to “stress”: `EstimateAttackSpeedSkill01` blends evasion, incoming damage, low-HP uptime, and deaths — **overload from combat tempo**.

Config: **`sgdAsFloor`**, **`sgdAsCap`**. Step constants: **`AsLearningRate = 0.25`**, **`AsMaxDeltaTheta = 0.075`** (largest per-step cap among axes).

### AttackDamage (DMG)

Per-hit damage mainly hits **survivability under burst**. `EstimateAttackDamageSkill01` weights incoming damage, low HP, deaths, and a little evasion — focus on **whether the player survives pressure**, not clear speed.

Config: **`sgdDmgFloor`**, **`sgdDmgCap`**. Step constants: **`DmgLearningRate = 0.18`**, **`DmgMaxDeltaTheta = 0.050`**.

### Actuator sync and world step

Actual multipliers applied to monsters live in `SgdActuatorsRuntimeState`. Before learning, `SgdDecisionDriver.EnsureAxisStatesSynced()` (four `Ensure*StateSynced` methods) aligns \(\theta\) and momentum with those multipliers: if \(e^{\theta}\) disagrees with the actuator beyond `ExternalSyncEpsilon`, \(\theta\) is recomputed from the current multiplier and **velocity is cleared**. Needed after **manual edits** (console, debug) so SGD does not continue from a stale internal state.

Each due step runs `StepAllAxes`: for each axis with adaptation enabled in `SgdDecisionRuntimeState`, its `Step*` runs; multipliers write back to actuators; if any change exceeds `AxisApplyEpsilon`, **`SgdActuatorsApplier.ApplyToAllLivingMonsters()`** refreshes spawned enemies (new spawns still get multipliers in `SgdActuatorsHooks` on `CharacterBody.Start`).

Runtime axis toggles: `Is*AdaptationEnabled` flags (default all true after `Reset`). RoR2 console: **`dda_sgd_axis_hp`**, **`dda_sgd_axis_ms`**, **`dda_sgd_axis_as`**, **`dda_sgd_axis_dmg`** with optional `0|1` — see `CheatManager/DdaCheatManager.cs` and [`CheatManager/README.md`](../../CheatManager/README.md#english).

Combat-time step interval: `SgdDecisionRuntimeState.StepSeconds` (default 10 s combat accumulated); console `dda_sgd_step_time` can change it for experiments.

### Related docs

Overall Sensors → Decision → Actuators flow: [`Architecture.md`](Architecture.md#english). `SgdEngine/` file details and gradient/momentum narrative: [`Implementation.md`](Implementation.md#english). Run integration and GA: [`Integration.md`](Integration.md#english).

---

<a id="russian"></a>
# Четыре оси сложности SGD DDA

В режиме SGD мод одновременно держит **четыре рычага**, которые масштабируют статы врагов через те же `GeneStat`, что и генетический алгоритм: **MaxHealth**, **MoveSpeed**, **AttackSpeed**, **AttackDamage**. В терминах [`RULE.md`](../../RULE.md#russian) это основной способ воздействия на **\(V_c\)** — «числовую» сложность мира: каждый множитель напрямую меняет характеристики монстров. Отдельная ось — не отдельный «сенсор», а **одномерный параметр адаптации** со своим \(\theta\), своей целью `skill01 ≈ challenge01` и своими пределами из конфига.

Параметр \(\theta\) задаётся в **логарифмическом масштабе**: для множителя \(m > 0\) выполняется \(m = e^{\theta}\). Так относительные изменения статов остаются мультипликативными в игре, а шаги оптимизатора — устойчивыми по \(\theta\). Минимум и максимум \(m\) на оси задаются парой **floor / cap**; в коде им соответствуют \(\theta_{\min} = \ln(\text{floor})\), \(\theta_{\max} = \ln(\text{cap})\). Централизованно это делает `SgdAxisLimitProvider`, читая значения из `ConfigManager` (секция BepInEx **«SGD Axis Limits»**).

**challenge01** для оси — нормализованное положение \(\theta\) на отрезке \([\theta_{\min}, \theta_{\max}]\):

\[
\text{challenge01} = \mathrm{clamp}_{[0,1]}\left(\frac{\theta - \theta_{\min}}{\theta_{\max} - \theta_{\min}}\right).
\]

Интерпретация: **насколько «выкручен вверх» рычаг сложности по этой оси** в допустимом диапазоне. Ноль соответствует нижнему множителю (floor), единица — верхнему (cap).

**skill01** — оценка «уровня игры» игрока **в разрезе этой оси**: линейная комбинация **одних и тех же** нормализованных сенсоров из `SgdSensorsSample`, но с **разными весами** в `Estimate*Skill01` внутри `SgdEngine/Decision/SgdDecisionDriver.cs`. На каждом шаге все оси видят **один и тот же сэмпл сенсоров** (последнее состояние `SgdSensorsRuntimeState`), поэтому показатели боя общие; различается только то, **какие сигналы считаются релевантными** для HP, скорости передвижения, скорости атаки и урона. Сами оси в коде — **четыре отдельных одномерных SGD**: нет одного общего градиента по всем \(\theta\) сразу, но статистически оси связаны через общую телеметрию боя.

Ошибка по оси: \(\text{error} = \text{challenge01} - \text{skill01}\) (в `SgdDecisionDriver` знак согласован с направлением шага: положительная ошибка ведёт к **снижению** \(\theta\), то есть к облегчению). Малые \(|\text{error}|\) внутри `DefaultErrorDeadZone` обнуляются.

Сводка по осям (имена в телеметрии — префиксы полей `dda_sample`, см. `Telemetry/TelemetrySampleBuilder.cs`):

| Ось | `GeneStat` | Оценка skill | Доминирующие сенсоры в skill01 (веса) | Floor / cap в `ConfigManager` | Префикс `axis_*` в телеметрии | Поле `axis_*_plane` |
|-----|------------|--------------|----------------------------------------|-------------------------------|------------------------------|---------------------|
| Здоровье врагов | `MaxHealth` | `EstimateMaxHealthSkill01` | исходящий урон 0.45; TTK 0.45; низкое HP 0.10 | `sgdHpFloor`, `sgdHpCap` | `axis_max_health_` | `hp` |
| Скорость движения | `MoveSpeed` | `EstimateMoveSpeedSkill01` | уклонение 0.45; входящий урон 0.25; исходящий 0.20; низкое HP 0.10 | `sgdMsFloor`, `sgdMsCap` | `axis_move_speed_` | `moveSpeed` |
| Скорость атаки | `AttackSpeed` | `EstimateAttackSpeedSkill01` | уклонение 0.40; входящий урон 0.35; низкое HP 0.20; смерти 0.05 | `sgdAsFloor`, `sgdAsCap` | `axis_attack_speed_` | `attackSpeed` |
| Урон атаки | `AttackDamage` | `EstimateAttackDamageSkill01` | входящий урон 0.45; низкое HP 0.30; смерти 0.20; уклонение 0.05 | `sgdDmgFloor`, `sgdDmgCap` | `axis_attack_damage_` | `damage` |

Для каждой оси в событиях дублируются множитель, `skill01`, `challenge01`, `error`, `abs_error`, приращения множителя и \(\Delta\theta\), флаг `is_jump` и др. Это та же логика, что и в отчётах H1–H4 в `ToolsAndDebug.md`.

Агрегат **`virtual_challenge_total`** в телеметрии — отдельная скалярная сводка «сложности по множителям»: взвешенная сумма \(\ln m\) по четырём текущим множителям из снимка сложности (`ComputeVirtualChallenge`). Её удобно сопоставлять с **`virtual_power_total`** (\(V_p\)) на уровне отчётности; **привязка SGD к осям** задаётся не этой суммой, а покомпонентными `challenge01` и шагами по каждой оси. Подробнее о том, почему H3 проверяют по осям, а не только по паре \(V_p\) / \(V_c\), см. `ToolsAndDebug.md`.

### MaxHealth (HP)

В бою ось HP в первую очередь влияет на **стойкость толпы**: выше множитель — дольше убиваются враги при прочих равных. `EstimateMaxHealthSkill01` опирается на **эффективность добивания**: нормализованный исходящий урон, прокси времени убийства (TTK) и долю времени на низком здоровье игрока. Таким образом, «skill» по этой оси высок, когда игрок **уверенно наносит урон и быстро чистит монстров** и при этом не держится постоянно на грани смерти.

`challenge01` здесь — насколько поднят множитель HP внутри `[sgdHpFloor, sgdHpCap]`. Ошибка `challenge01 - skill01` сравнивает эту «выкрученность» с наблюдаемой эффективностью убийства.

Гиперпараметры шага в `SgdDecisionDriver`: **`HpLearningRate = 0.22`**, **`HpMaxDeltaTheta = 0.060`** (~6.2% относительного изменения множителя на шаг в худшем случае по модулю, см. комментарий в коде).

### MoveSpeed (MS)

Скорость движения врагов давит на **позиционирование и кайтинг**: быстрее монстры — сложнее удерживать дистанцию. `EstimateMoveSpeedSkill01` смещён в сторону **уклонения** (низкая нормализованная доля попаданий по игроку) и **переживаемости** по входящему урону; исходящий урон даёт небольшой вклад «умения вести бой в движении».

Конфиг: **`sgdMsFloor`**, **`sgdMsCap`**. Константы шага: **`MsLearningRate = 0.20`**, **`MsMaxDeltaTheta = 0.050`**.

### AttackSpeed (AS)

Скорость атаки врагов задаёт **частоту давления** (больше ударов в единицу времени). Комментарий в коде связывает ось со «стрессом»: `EstimateAttackSpeedSkill01` смешивает уклонение, входящий урон, долю времени на низком HP и смерти в окне — то есть сигналы **перегруза входящим темпом боя**.

Конфиг: **`sgdAsFloor`**, **`sgdAsCap`**. Константы шага: **`AsLearningRate = 0.25`**, **`AsMaxDeltaTheta = 0.075`** (самый крупный допустимый шаг среди осей).

### AttackDamage (DMG)

Урон с удара врага в первую очередь бьёт по **выживаемости под разовыми/пакетными попаданиями**. `EstimateAttackDamageSkill01` взвешивает входящий урон, низкое здоровье, смерти и чуть-чуть уклонение — акцент на том, **держится ли игрок под уроном**, а не на скорости клира.

Конфиг: **`sgdDmgFloor`**, **`sgdDmgCap`**. Константы шага: **`DmgLearningRate = 0.18`**, **`DmgMaxDeltaTheta = 0.050`**.

### Синхронизация с актуаторами и шаг по миру

Фактические множители, которые получают монстры, хранятся в `SgdActuatorsRuntimeState`. Перед обучением `SgdDecisionDriver.EnsureAxisStatesSynced()` (через четыре метода `Ensure*StateSynced`) выравнивает \(\theta\) и скорость momentum с этими множителями: если ожидаемое \(e^{\theta}\) расходится с актуатором больше чем на `ExternalSyncEpsilon`, \(\theta\) пересчитывается из текущего множителя, **velocity обнуляется**. Это нужно после **ручного вмешательства** (консоль, отладка), чтобы SGD не продолжал «как будто» старое значение.

На каждом due-step вызывается `StepAllAxes`: для каждой оси, у которой в `SgdDecisionRuntimeState` включена адаптация, выполняется свой `Step*`; множитель записывается обратно в актуаторы; затем, если хотя бы по одной оси изменение превысило `AxisApplyEpsilon`, вызывается **`SgdActuatorsApplier.ApplyToAllLivingMonsters()`**, чтобы обновить уже заспавненных врагов (новые и так получают множители в `SgdActuatorsHooks` на `CharacterBody.Start`).

Включение и выключение осей на лету: флаги `Is*AdaptationEnabled` в `SgdDecisionRuntimeState` (по умолчанию все true после `Reset`). Из консоли RoR2: **`dda_sgd_axis_hp`**, **`dda_sgd_axis_ms`**, **`dda_sgd_axis_as`**, **`dda_sgd_axis_dmg`** с необязательным аргументом `0|1` — см. `CheatManager/DdaCheatManager.cs` и [`CheatManager/README.md`](../../CheatManager/README.md#russian).

Интервал шага по времени боя задаётся `SgdDecisionRuntimeState.StepSeconds` (по умолчанию 10 с накопленного боя); отдельная консольная команда `dda_sgd_step_time` позволяет менять его для экспериментов.

### Связь с остальной документацией

Общий поток Sensors → Decision → Actuators — в `Architecture.md`. Детали файлов `SgdEngine/` и качественное описание градиента и momentum — в `Implementation.md`. Практическая интеграция с забегом и GA — в `Integration.md`.
