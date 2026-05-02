**Languages:** [English](#english) · [Русский](#russian)

---

<a id="english"></a>
# Project rules: DDA (SGD) for Risk of Rain 2

> Short user-facing project description, SGD DDA doc links, and install steps are in the root `README.md`. This file is the **design document and internal development rulebook**.

This mod is a research implementation of **dynamic difficulty adjustment (DDA)** for a master’s thesis.

- **Repository reference**: existing genetic algorithm (`GeneticEngine/` folder).
- **Thesis target algorithm**: DDA via **stochastic gradient descent (SGD)** on *Sensors → Decision module → Actuators*.

---

## DDA architecture

```
Sensors (player metrics) → Decision module (SGD) → Actuators (difficulty parameters)
```

- **Sensors**: collect telemetry and estimate player skill components \(S_{p_i}\) and virtual power \(V_p\) in a sliding time window.
- **Decision module (SGD)**: minimizes a balance objective (below), approximates the gradient, updates difficulty parameters (vector \(\theta\)).
- **Actuators**: apply \(\theta\) to the game world. Current implementation is mostly **numeric monster stat modifiers** (`GeneStat`), i.e. controlling \(V_c\).

---

## Terms and difficulty model (Project Horseshoe)

We use **perceived difficulty** from **Project Horseshoe** ([site](https://www.projecthorseshoe.com/), [report archive](https://www.projecthorseshoe.com/reports/)):

\[
C = (V_c + S_c) - (V_p + S_p)
\]

- **\(S_p\) (skill of player)**: measurable skill in control and decisions (mechanics, tactics, resource use, adaptation). In the mod this feeds **sensors**.
- **\(V_p\) (virtual power)**: numeric build strength (items, levels, stacks, synergies, procs, survivor/gear choice) so DDA does not treat a strong build as high \(S_p\) by mistake.
- **\(V_c\) (virtual challenge)**: numeric difficulty from multipliers and enemy parameters. **The mod mainly controls \(V_c\)** via `GeneStat` multipliers (HP/damage/speeds).
- **\(S_c\) (skill required by challenge)**: tactical/behavioral demand (attack patterns, aggression/coordination, enemy types, wave structure, counterplay to kiting/jumps, etc.). Treated as future architecture extension in this version.

Sign of \(C\):

- **\(C > 0\)**: feels too hard → adaptation should lower \(V_c\) and/or \(S_c\).
- **\(C < 0\)**: feels too easy → adaptation may raise \(V_c\) and/or \(S_c\) within safe bounds.

---

## Stack and dependencies

- **BepInEx** — mod loader.
- **R2API** — ArtifactCode, ContentManagement, Items, Language, RecalculateStats, CommandHelper.
- **RoR2 API** — Run, CharacterBody, HealthComponent, TeamIndex, RunArtifactManager, Stage.
- **On.** — hooks/patching (e.g. `On.RoR2.HealthComponent.TakeDamage`).

---

## Key files and roles

| File | Purpose |
|------|---------|
| `GeneticsArtifactPlugin.cs` | Entry point, `Awake`, subsystem init |
| `CheatManager/DdaAlgorithmState.cs` | Runtime: `IsGeneticAlgorithmEnabled`, `ActiveAlgorithm` (Genetic/Sgd), `IsDebugOverlayEnabled` |
| `CheatManager/DdaCheatManager.cs` | Console: `dda_genetics`, `dda_algorithm`, `dda_debug_overlay`, `dda_param` |
| `GeneticEngine/GeneEngineDriver.cs` | GA driver; Run_Start, CharacterBody_Start, HealthComponent_TakeDamage hooks |
| `GeneticEngine/MonsterGeneBehaviour.cs` | Per-monster: `currentGenes`, `damageDealt`, `timeAlive`, `timeEngaged`, `score` |
| `GeneticEngine/MasterGeneBehaviour.cs` | Master gene copy per monster type; `MutateFromChildren` learns from score |
| `GeneticEngine/GeneTokenCalc.cs` | `RecalculateStatsAPI`: genes → stat modifiers |
| `GeneticEngine/GeneTokens.cs` | ItemDef for GeneStat (MaxHealth, MoveSpeed, AttackSpeed, AttackDamage) |
| `ArtifactResources/ConfigManager.cs` | BepInEx config: `timeLimit`, `deathLimit`, `geneFloor`, `geneCap`, etc., plus SGD axis limits (`sgdHpFloor/Cap`, …) |
| `SgdEngine/SgdAxisLimitProvider.cs` | Safe SGD axis limits from config, used in decision and actuators |

---

## Code patterns (integration invariants)

1. **Server check**: DDA logic runs only when `NetworkServer.active`.
2. **Artifact check**: `RunArtifactManager.instance.IsArtifactEnabled(ArtifactOfGenetics.artifactDef)`.
3. **Algorithm branch**: SGD paths guarded by `DdaAlgorithmState.ActiveAlgorithm == DdaAlgorithmType.Sgd`.
4. **Monsters**: `teamIndex == TeamIndex.Monster` and `inventory != null`.
5. **Logs**: `GeneticsArtifactPlugin.geneticLogSource.LogInfo/LogWarning/LogError`.
6. **Players**: `TeamIndex.Player`; for sensors — `CharacterBody` with `isPlayerControlled` or `CharacterMaster`.

---

## DDA module implementation

### Sensor module

For **MVP adaptation with 4 `GeneStat` actuators** (HP/MoveSpeed/AttackSpeed/AttackDamage), sensors must give the decision module a minimal stable signal to:

- separate “too easy” vs “too hard”;
- account for **build virtual power** \(V_p(t)\);
- attribute actuator effects (HP vs DMG/AS vs MS) without chasing RNG noise.

#### Required sensor list (for 4 actuators)

1) **Incoming damage and death risk** (drives DMG/AS, partly MS)
   - `IncomingDamageRate` — incoming DPS in combat
   - `LowHealthUptime` — fraction of time below HP% threshold (e.g. <30%)
   - `DeathsPerWindow` — deaths/knockdowns in observation window

2) **Kill efficiency** (drives HP)
   - `OutgoingDamageRate` — outgoing DPS to monsters
   - `AvgTTK` — mean time to kill (or proxy via encounter duration/KPM)

3) **Contact and evasion** (separates MS vs AS)
   - `HitRateOnPlayer` — hit rate on player in combat
   - `CombatUptime` — fraction of time in combat (not outOfCombat)

4) **Build virtual power \(V_p(t)\)** (for \(\alpha (V_c - V_p)^2\) term)
   - `V_p.offense`, `V_p.defense`, `V_p.mobility`, `V_p.total` (formula below)

#### \(V_p(t)\) formula (implemented in `SgdEngine/`)

Absolute proxy + log compression + EMA smoothing:

- `OffenseRaw = damage * attackSpeed * (1 + critChance)`
- `DefenseRaw = EHP + RegenWeight * regen`, where `EHP ≈ (maxHealth + maxShield) * clamp((100 + armor)/100, 0.05, 10)`
- `MobilityRaw = moveSpeed`

Compression:
- `Offense = log(1 + OffenseRaw)`
- `Defense = log(1 + DefenseRaw)`
- `Mobility = log(1 + MobilityRaw)`
- `Total = 0.50*Offense + 0.35*Defense + 0.15*Mobility`

EMA smoothing:
- `xEma = lerp(xEma, x, 1 - exp(-dt/τ))`, default \(\tau\) ~7.5 s.

### Actuator module

Actuators apply difficulty parameters \(\theta\) to the world — levers for \(V_c\) and (when extended) \(S_c\).

- **Current lever (also used by GA)** — numeric monster stat modifiers via `GeneStat`: `MaxHealth`, `MoveSpeed`, `AttackSpeed`, `AttackDamage`. Application: `RecalculateStatsAPI` (`GeneTokenCalc`) and/or direct `CharacterBody` tweaks.
- **Potential \(S_c\) levers** (extension direction): attack tempo/windows, wave coordination/composition, resource pressure.

#### Mapping thesis components → actuators

The thesis objective couples \(S_{p_i}\) ↔ \(S_{c_i}\) and \(V_p\) ↔ \(V_c\). Practical mapping:

- **Mechanical \(S_{p,mech}\)** ↔ **mechanical \(S_{c,mech}\)** (projectile speed/accuracy, reaction windows, attack rate); MVP often approximated via \(V_c\) (enemy damage/attack/move speed).
- **Tactical \(S_{p,tactic}\)** ↔ **tactical \(S_{c,tactic}\)** (aggression, coordination, wave mix); explicit \(S_c\) levers not in code yet.
- **Resource \(S_{p,res}\)** ↔ **resource \(S_{c,res}\)** (healing/ammo/ buff availability); extension direction.
- **\(V_p\)** ↔ **\(V_c\)**: build power vs numeric world multipliers the mod **already applies** via `GeneStat`.

**Current limitation**: main actuator path is `GeneStat` multipliers (\(V_c\)). \(S_c\) and \(S_{c_i}\) are documented as thesis/future actuator work, not shipped behavior.

### Decision module (SGD)

**Online optimization**: each adaptation step updates \(\theta\) so challenge matches current player capability, with psychological safety (smooth, predictable changes).

- **Input**: sensor metrics (\(S_{p_i}(t)\) estimates), \(V_p(t)\), stability/noise helpers.
- **Output**: \(\theta(t)\) for actuators (MVP: `GeneStat` multipliers; extension: \(S_{c_i}\) params).
- **Update cadence**: fixed interval or event-driven (like GA `timeLimit`/`deathLimit`).

#### Adaptation iteration (thesis §3.3)

Parallel per-component adaptation on a fixed schedule:

1) **Assess skill state** — measure \(S_{p_i}\) over window \(W\), normalize/smooth, estimate \((S_{c_i}(t) - S_{p_i}(t))\) and \((V_c(t) - V_p(t))\).
2) **Gradients** — partials of objective w.r.t. \(\theta\), step direction/magnitude, optional cross-effects via \(S_c(\theta)\), \(V_c(\theta)\).
3) **Update difficulty parameters** — anti-gradient step, constraints/smoothing, coordinated caps (e.g. don’t spike damage and attack speed together beyond a jump budget).
4) **Monitor** — player response, optional weight \(w_i\) tuning, convergence to acceptable balance.

#### SGD game adaptations (thesis §3.2.1)

- **Gradient/step clipping** — avoid spikes from outlier metrics.
- **Adaptive \(\eta_t\)** — lower LR under noise, higher when stable.
- **Momentum** — smoother, more predictable trajectory.
- **Early stop/freeze** — pause when error is small and oscillating.
- **Cap/floor projection** — hard bounds on \(\theta\) and/or \(\Delta\theta\) for safety.

---

## Algorithm math (from thesis)

```
Balance objective:
F(t) = Σ_i [ w_i · (S_c_i(t) - S_p_i(t))² ] + α · (V_c(t) - V_p(t))²

Notation:
- S_p_i(t): i-th observed player skill component
- S_c_i(t): matching tactical challenge component
- V_p(t): virtual power (build strength)
- V_c(t): virtual challenge (numeric multipliers)
- w_i: skill importance weights
- α: weight for V_p vs V_c alignment

Parameterization:
- θ(t): difficulty vector the decision module actually changes
  - current MVP: θ = [MaxHealth_mult, MoveSpeed_mult, AttackSpeed_mult, AttackDamage_mult]
  - extension: θ may include S_c parameters (aggression, coordination, waves, ...)

SGD update:
θ_{t+1} = Project( θ_t - η_t · ∇_θ F(t) )

η_t: learning rate (smoothness)
Project(·): cap/floor projection for safe changes

Gradient note:
- Generally S_c and V_c depend on θ through actuators, so ∇_θ F includes ∂S_{c_i}/∂θ and ∂V_c/∂θ.
- MVP direct parameterization (e.g. S_{c_i}(t) ≡ θ_i(t)) gives simple partials:
  - ∂F/∂θ_i = 2 w_i (θ_i - S_{p_i}),
  - ∂F/∂θ_v = 2 α (θ_v - V_p).
- Enables parallel coordinate descent.
```

---

## Folder structure

- **All author SGD/DDA classes** live in a **dedicated folder** (e.g. `SgdEngine/`).
- Sensors, actuators, and SGD decision code — only there.
- `CheatManager/` — shared infra (console, overlay, state).
- `GeneticEngine/` — reference GA (do not modify casually).

## Protecting the genetic engine

- **Do not modify** `GeneticEngine/`: `GeneEngineDriver.cs`, `MasterGeneBehaviour.cs`, `MonsterGeneBehaviour.cs`, `GeneTokenCalc.cs`, `GeneTokens.cs`.
- **Exception**: critically necessary fixes (blocking bug) or minimal integration at explicit request.
- SGD integrates via `DdaAlgorithmState.ActiveAlgorithm`, separate driver, `GeneticsArtifactPlugin` hooks — **not** by editing `GeneEngineDriver`.

---

## Style and principles

- **SOLID, KISS** — one responsibility per class; simple interfaces.
- **Naming**: `Sgd` prefix for new DDA code; `Gene` for GA.
- **Namespace**: `GeneticsArtifact`; `GeneticsArtifact.CheatManager` for CheatManager.
- **Cleanup**: remove dead code.
- **Commits**: one logical chunk per commit (sensors, actuators, decision).

---

## Debugging

- Console: `dda_genetics 1`, `dda_algorithm sgd`, `dda_debug_overlay 1`.
- Logs: `GeneticsArtifactPlugin.geneticLogSource` in `BepInEx/LogOutput.log`.
- Overlay: extend `DebugOverlayBehaviour` for sensor metrics and \(\theta\).
- `#if DEBUG` — extra logging in debug builds.

---

## Integration with current code

- When `ActiveAlgorithm == Sgd`, use the **SGD driver** in `SgdEngine/` without modifying `GeneEngineDriver`.
- `GeneticsArtifactPlugin.Awake` initializes SGD only for `Sgd`; Run/body hooks live in `SgdRuntimeDriver`, `SgdSensorsHooks`, actuators, etc.
- `ArtifactOfGenetics` stays shared: `dda_genetics` and `RunArtifactManager`.
- `dda_algorithm sgd` sets `DdaAlgorithmState.ActiveAlgorithm = Sgd` and runs the **implemented** SGD path; details in `Docs/SgdDda/*`.

---

## Deploy after build

- Copy built **.dll** to RoR2 `BepInEx/plugins` after a successful build.
- Post-build step `tools/InstallToRor2.ps1` tries to auto-install `GeneticsArtifact.dll` after `dotnet build`.

### Auto-install behavior
- If `ROR2_PLUGINS_PATH` is set — copy there (Thunderstore/r2modman profiles).
- Else resolve game folder via Steam `libraryfolders.vdf` or `ROR2_GAME_PATH` / `ROR2_DIR` / `ROR2_PATH`.
- If unresolved — build **does not fail**; warning only.

### Environment variables

Set one of:

- `ROR2_PLUGINS_PATH` — target `BepInEx\plugins` (game or `...\profiles\<profile>\BepInEx\plugins`).
- `ROR2_GAME_PATH` / `ROR2_DIR` / `ROR2_PATH` — game root, e.g. `...\Risk of Rain 2`.

---

<a id="russian"></a>
# Правила проекта: DDA (SGD) для Risk of Rain 2

> Краткое пользовательское описание проекта, ссылки на документацию по SGD DDA и инструкции по установке находятся в корневом файле `README.md`. Настоящий документ используется как **дизайн‑док и набор внутренних правил разработки**.

Этот мод — исследовательская реализация **системы динамической адаптации уровня сложности** (Dynamic Difficulty Adjustment, DDA) для магистерской диссертации.

- **Референс в репозитории**: существующий генетический алгоритм (папка `GeneticEngine/`).
- **Целевой алгоритм диссертации**: DDA на основе **стохастического градиентного спуска** (SGD) поверх архитектуры *Sensors → Decision module → Actuators*.

---

## Архитектура DDA

```
Sensors (метрики игрока) → Decision module (SGD) → Actuators (параметры сложности)
```

- **Sensors (сенсоры)**: собирают телеметрию и оценивают компоненты навыка игрока \(S_{p_i}\) и виртуальной силы \(V_p\) в скользящем временном окне.
- **Decision module (модуль решений, SGD)**: минимизирует целевую функцию баланса (см. ниже), вычисляет/аппроксимирует градиент и обновляет параметры сложности (вектор \(\theta\)).
- **Actuators (актуаторы)**: применяют \(\theta\) к игровому миру. В текущей реализации это в основном **числовые модификаторы статов монстров** (`GeneStat`), т.е. управление \(V_c\).

---

## Термины и модель сложности (Project Horseshoe)

Используем модель **воспринимаемой сложности** в терминах материалов **Project Horseshoe** ([сайт](https://www.projecthorseshoe.com/), [архив отчётов](https://www.projecthorseshoe.com/reports/)):

\[
C = (V_c + S_c) - (V_p + S_p)
\]

Где:

- **\(S_p\) (skill of player / навык игрока)**: измеримые проявления умения игрока управлять персонажем и принимать решения (механика, тактика, управление ресурсами, адаптивность). В моде это источник данных для **сенсоров**.
- **\(V_p\) (virtual power / виртуальная сила игрока)**: «числовая мощь» игрока, не связанная напрямую с умением (набор предметов, уровни, стаки, синергии, проки, выбор персонажа/снаряжения). Это важно учитывать, чтобы DDA не «наказывал» сильный билд, принимая его за высокий \(S_p\).
- **\(V_c\) (virtual challenge / виртуальная сложность)**: «числовая» сложность, задаваемая множителями и параметрами врагов. **Текущая реализация мода в основном управляет именно \(V_c\)** через множители `GeneStat` (HP/урон/скорости).
- **\(S_c\) (skill required by challenge / требуемый навык)**: тактическая/поведенческая сложность, т.е. какие навыки требует игра (паттерны атак, агрессия/координация, типы врагов, плотность/структура волн, контр‑игра против кайт/прыжков и т.п.). В текущей версии это рассматривается как направление расширения архитектуры (см. заметки по интеграции).

Интерпретация знака \(C\):

- **\(C > 0\)**: игра воспринимается сложнее желаемого (челлендж «перекрывает» силу и навык игрока) → адаптация должна снижать \(V_c\) и/или \(S_c\).
- **\(C < 0\)**: игра воспринимается слишком простой → адаптация может повышать \(V_c\) и/или \(S_c\) в безопасных пределах.

---

## Стек и зависимости

- **BepInEx** — загрузчик модов.
- **R2API** — ArtifactCode, ContentManagement, Items, Language, RecalculateStats, CommandHelper.
- **RoR2 API** — Run, CharacterBody, HealthComponent, TeamIndex, RunArtifactManager, Stage.
- **On.** — хуки/патчинг (например, `On.RoR2.HealthComponent.TakeDamage`).

---

## Ключевые файлы и их роли

| Файл | Назначение |
|------|------------|
| `GeneticsArtifactPlugin.cs` | Точка входа, `Awake`, инициализация подсистем |
| `CheatManager/DdaAlgorithmState.cs` | Рантайм‑состояние: `IsGeneticAlgorithmEnabled`, `ActiveAlgorithm` (Genetic/Sgd), `IsDebugOverlayEnabled` |
| `CheatManager/DdaCheatManager.cs` | Консольные команды: `dda_genetics`, `dda_algorithm`, `dda_debug_overlay`, `dda_param` |
| `GeneticEngine/GeneEngineDriver.cs` | Драйвер генетического алгоритма; хуки Run_Start, CharacterBody_Start, HealthComponent_TakeDamage |
| `GeneticEngine/MonsterGeneBehaviour.cs` | Данные по монстру: `currentGenes`, `damageDealt`, `timeAlive`, `timeEngaged`, `score` |
| `GeneticEngine/MasterGeneBehaviour.cs` | «Мастер‑копия» генов для типа монстра; `MutateFromChildren` — обучение по score |
| `GeneticEngine/GeneTokenCalc.cs` | `RecalculateStatsAPI`: перевод генов в модификаторы статов |
| `GeneticEngine/GeneTokens.cs` | ItemDef для GeneStat (MaxHealth, MoveSpeed, AttackSpeed, AttackDamage) |
| `ArtifactResources/ConfigManager.cs` | BepInEx‑конфиг: `timeLimit`, `deathLimit`, `geneFloor`, `geneCap` и др., а также отдельные лимиты для осей SGD (`sgdHpFloor/Cap`, `sgdMsFloor/Cap`, `sgdAsFloor/Cap`, `sgdDmgFloor/Cap`) |
| `SgdEngine/SgdAxisLimitProvider.cs` | Источник безопасных лимитов по осям SGD (на основе конфига), используемых в модуле решений и актуаторах |

---

## Код‑паттерны (инварианты интеграции)

1. **Проверка сервера**: DDA‑логика исполняется только при `NetworkServer.active`.
2. **Проверка артефакта**: `RunArtifactManager.instance.IsArtifactEnabled(ArtifactOfGenetics.artifactDef)`.
3. **Выбор алгоритма**: ветвления под SGD защищаются `DdaAlgorithmState.ActiveAlgorithm == DdaAlgorithmType.Sgd`.
4. **Монстры**: `teamIndex == TeamIndex.Monster` и `inventory != null`.
5. **Логи**: `GeneticsArtifactPlugin.geneticLogSource.LogInfo/LogWarning/LogError`.
6. **Игроки**: `TeamIndex.Player`; для сенсоров — `CharacterBody` с `isPlayerControlled` или `CharacterMaster`.

---

## Реализация модулей DDA

### Модуль сенсоров

Цель сенсоров в рамках **MVP‑адаптации через 4 актуатора `GeneStat`** (HP/MoveSpeed/AttackSpeed/AttackDamage) — дать модулю решений минимальный набор устойчивых сигналов, чтобы:

- отличать «слишком легко» от «слишком сложно»;
- корректно учитывать **виртуальную силу билда** \(V_p(t)\);
- разруливать вклад актуаторов (HP vs DMG/AS vs MS) без реакции на RNG‑шум.

#### Обязательный список сенсоров (под 4 актуатора)

1) **Входящий урон и риск смерти** (контролирует DMG/AS, частично MS)
   - `IncomingDamageRate` — входящий урон в секунду (в бою)
   - `LowHealthUptime` — доля времени с HP% ниже порога (например, <30%)
   - `DeathsPerWindow` — число смертей/нокдаунов за окно наблюдения

2) **Эффективность уничтожения врагов** (контролирует HP)
   - `OutgoingDamageRate` — исходящий урон по монстрам в секунду
   - `AvgTTK` — среднее время убийства (или прокси через длительность столкновений/KPM)

3) **Контактность и уклонение** (разделяет MS vs AS)
   - `HitRateOnPlayer` — частота попаданий по игроку в бою
   - `CombatUptime` — доля времени «в бою» (не outOfCombat)

4) **Виртуальная сила билда \(V_p(t)\)** (для члена \(\alpha (V_c - V_p)^2\))
   - `V_p.offense`, `V_p.defense`, `V_p.mobility`, `V_p.total` (см. формулу ниже)

#### Формула \(V_p(t)\) (реализовано в `SgdEngine/`)
Используется абсолютная прокси‑оценка + лог‑сжатие диапазона + EMA‑сглаживание:

- `OffenseRaw = damage * attackSpeed * (1 + critChance)`
- `DefenseRaw = EHP + RegenWeight * regen`, где `EHP ≈ (maxHealth + maxShield) * clamp((100 + armor)/100, 0.05, 10)`
- `MobilityRaw = moveSpeed`

Далее (компрессия):
- `Offense = log(1 + OffenseRaw)`
- `Defense = log(1 + DefenseRaw)`
- `Mobility = log(1 + MobilityRaw)`
- `Total = 0.50*Offense + 0.35*Defense + 0.15*Mobility`

Сглаживание (EMA):
- `xEma = lerp(xEma, x, 1 - exp(-dt/τ))`, где \(\tau\) по умолчанию ~7.5 сек.

### Модуль актуаторов

Актуаторы применяют параметры сложности \(\theta\) к миру игры. В терминах модели это рычаги для управления \(V_c\) и (в расширении) \(S_c\).

- **Текущий “рычаг” (реализовано генетическим движком)** — числовые модификаторы статов монстров через `GeneStat`:
  - `MaxHealth` (HP), `MoveSpeed`, `AttackSpeed`, `AttackDamage`.
  - Точка применения: `RecalculateStatsAPI` (`GeneTokenCalc`) и/или прямые модификации `CharacterBody`.
- **Потенциальные рычаги \(S_c\)** (архитектурно поддерживается как направление расширения):
  - агрессивность/временные окна атак (реакция на механическое мастерство),
  - координация/состав волн (требования к тактике и контролю территории),
  - распределение ресурсов (боеприпасы/лечение) как часть «ресурсного вызова».

#### Маппинг компонентов сложности (диссертация → актуаторы)
Целевая функция диссертации оперирует парами \(S_{p_i}\) ↔ \(S_{c_i}\) и дополнительно согласует \(V_p\) ↔ \(V_c\). На практике это означает следующий «перевод» в рычаги игры:

- **Механическое мастерство \(S_{p,mech}\)** ↔ **механический вызов \(S_{c,mech}\)**:
  - примеры \(S_{c,mech}\): точность/скорость снарядов противников, окна реакции, частота атак;
  - в текущем MVP‑контуре мода это чаще всего приближённо выражается через \(V_c\) (урон/скорость атаки/скорость движения монстров).
- **Тактическое мышление \(S_{p,tactic}\)** ↔ **тактический вызов \(S_{c,tactic}\)**:
  - примеры \(S_{c,tactic}\): агрессивность, координация, состав волн, фланги/окружения;
  - в текущем коде явные рычаги \(S_c\) ещё не реализованы и рассматриваются как расширение.
- **Ресурсный менеджмент \(S_{p,res}\)** ↔ **ресурсный вызов \(S_{c,res}\)**:
  - примеры \(S_{c,res}\): доступность лечения/боеприпасов/усилений и их распределение;
  - в текущем коде это также направление расширения.
- **Виртуальная сила \(V_p\)** ↔ **виртуальная сложность \(V_c\)**:
  - \(V_p\): «мощность билда» (предметы/стаки/проки),
  - \(V_c\): числовые множители мира, **которые мод уже умеет применять** через `GeneStat`.

Ограничение текущей реализации: на данный момент основной механизм воздействия — это `GeneStat`‑множители (т.е. \(V_c\)). Поэтому термины \(S_c\) и компоненты \(S_{c_i}\) фиксируются в документации как часть целевой модели диссертации и будущего расширения актуаторов, а не как уже существующая функциональность.

### Модуль решений (SGD)

Модуль решений выполняет **онлайн‑оптимизацию**: на каждом шаге адаптации обновляет \(\theta\) так, чтобы текущий вызов соответствовал текущим возможностям игрока, сохраняя психологическую безопасность (плавность и предсказуемость изменений).

- **Вход**: вектор метрик/оценок из сенсоров (оценки \(S_{p_i}(t)\), оценка \(V_p(t)\), вспомогательные статистики стабильности/шума).
- **Выход**: вектор параметров сложности \(\theta(t)\) для актуаторов (в текущей реализации — множители для `GeneStat`; в расширении — параметры \(S_{c_i}\)).
- **Частота обновления**: итеративно с фиксированным интервалом или по событию (аналогично `timeLimit`/`deathLimit` из генетического драйвера).

#### Итерация адаптации (по главе 3.3)
Алгоритм выполняет параллельную адаптацию по компонентам сложности и повторяет цикл с фиксированным интервалом:

1) **Оценка текущего состояния навыков**
   - измерение метрик для каждого \(S_{p_i}\) в окне \(W\),
   - нормализация и сглаживание,
   - оценка текущих отклонений \((S_{c_i}(t) - S_{p_i}(t))\) и \((V_c(t) - V_p(t))\).
2) **Вычисление градиентов**
   - вычисление/аппроксимация частных производных целевой функции по параметрам \(\theta\),
   - определение направления и величины корректировок,
   - при необходимости учёт взаимовлияния компонентов (в общем случае — через связь \(S_c(\theta)\), \(V_c(\theta)\)).
3) **Корректировка параметров сложности**
   - шаг в направлении антиградиента,
   - применение ограничений и сглаживания обновлений,
   - согласование обновлений между компонентами (например, не усиливать одновременно урон и скорость атаки сверх допустимого «скачка»).
4) **Мониторинг и обратная связь**
   - оценка реакции игрока на изменения,
   - при необходимости корректировка весов \(w_i\) (что именно считаем более важным),
   - контроль сходимости к «приемлемому балансу».

#### Модификации SGD для игровой задачи (по главе 3.2.1)
Для психологически безопасной и устойчивой адаптации используются следующие «надстройки» над базовым SGD:

- **Ограничение шага/градиента (clipping)**: предотвращает резкие скачки сложности при выбросах метрик.
- **Адаптивная скорость обучения \(\eta_t\)**: уменьшать \(\eta_t\) при высокой нестабильности/шуме метрик и увеличивать при стабильной картине.
- **Инерционный член (momentum)**: сглаживает траекторию оптимизации и делает изменения более предсказуемыми.
- **Ранняя остановка/заморозка**: если ошибка уже «достаточно мала» и колеблется вокруг нуля, временно прекращать обновления.
- **Cap/Floor и проекция**: жёсткие границы на \(\theta\) (и/или скорость изменения \(\Delta\theta\)) как защита от психологически небезопасных изменений.

---

## Математика алгоритма (из диссертации)

```
Целевая функция (ошибка баланса):
F(t) = Σ_i [ w_i · (S_c_i(t) - S_p_i(t))² ] + α · (V_c(t) - V_p(t))²

Обозначения:
- S_p_i(t): i‑тая компонента реального навыка игрока (точность, позиционирование, ресурсный менеджмент, ...)
- S_c_i(t): соответствующая компонента тактической сложности (требуемый навык)
- V_p(t): виртуальная сила (мощность билда)
- V_c(t): виртуальная сложность (числовые множители)
- w_i: вес важности навыка
- α: вес согласования виртуальной силы и виртуальной сложности

Параметризация:
- θ(t): вектор параметров сложности, которые реально изменяет модуль решений
  - текущий MVP: θ = [MaxHealth_mult, MoveSpeed_mult, AttackSpeed_mult, AttackDamage_mult]
  - при расширении: θ может включать параметры S_c (агрессия, координация, состав волн, ...)

Правило обновления (SGD):
θ_{t+1} = Project( θ_t - η_t · ∇_θ F(t) )

где:
- η_t: скорость обучения (контролирует плавность адаптации)
- Project(·): проекция на допустимые пределы (cap/floor), чтобы изменения были безопасными

Примечание про градиент:
- В общем случае \(S_c\) и \(V_c\) зависят от параметров \(\theta\) через маппинг актуаторов, поэтому \(\nabla_\theta F\) включает производные \(\partial S_{c_i}/\partial \theta\) и \(\partial V_c/\partial \theta\).
- В MVP‑варианте, когда отдельные компоненты сложности параметризуются напрямую (например, \(S_{c_i}(t) \equiv \theta_i(t)\) или \(V_c(t) \equiv \theta_v(t)\)), квадратичная форма даёт простой градиент вида:
  - \(\frac{\partial F}{\partial \theta_i} = 2 w_i (\theta_i - S_{p_i})\),
  - \(\frac{\partial F}{\partial \theta_v} = 2 \alpha (\theta_v - V_p)\).
- Это удобно для реализации «параллельного градиентного спуска» по компонентам.
```

---

## Структура папок

- **Все классы алгоритма автора (SGD/DDA)** должны находиться в **отдельной папке** проекта (например, `SgdEngine/` или `DdaEngine/`).
- Сенсоры, актуаторы и модуль решений SGD — только в этой папке.
- `CheatManager/` — общая инфраструктура (консольные команды, оверлей, состояние).
- `GeneticEngine/` — исходники генетического алгоритма (референс, не трогать).

## Защита генетического движка

- **Не модифицировать** файлы в `GeneticEngine/`: `GeneEngineDriver.cs`, `MasterGeneBehaviour.cs`, `MonsterGeneBehaviour.cs`, `GeneTokenCalc.cs`, `GeneTokens.cs`.
- Исключение: только **критически необходимые** изменения (блокирующий баг или минимальная точка интеграции по явному запросу).
- Интеграция SGD — через `DdaAlgorithmState.ActiveAlgorithm`, отдельный драйвер и хуки в `GeneticsArtifactPlugin`, **без** вмешательства в `GeneEngineDriver` и связанные классы.

---

## Стиль и принципы

- **SOLID, KISS**: один класс — одна ответственность; простые интерфейсы.
- **Именование**: префикс `Sgd` для новых DDA‑классов; `Gene` — для генетического алгоритма.
- **Namespace**: `GeneticsArtifact` для основного кода; `GeneticsArtifact.CheatManager` для CheatManager.
- **Чистка**: удалять неиспользуемый код.
- **Коммиты**: после каждого логического блока (сенсоры, актуаторы, модуль решений).

---

## Отладка

- Консоль: `dda_genetics 1`, `dda_algorithm sgd`, `dda_debug_overlay 1`.
- Логи: `GeneticsArtifactPlugin.geneticLogSource` в `BepInEx/LogOutput.log`.
- Оверлей: `DebugOverlayBehaviour` — расширять для вывода метрик сенсоров и параметров \(\theta\).
- `#if DEBUG` — дополнительное логирование в debug‑сборках.

---

## Интеграция с текущим кодом

- Когда `ActiveAlgorithm == Sgd` — использовать **отдельный SGD‑драйвер** в собственной папке (`SgdEngine/`), не модифицируя `GeneEngineDriver`.
- Точка входа: `GeneticsArtifactPlugin.Awake` — инициализировать SGD‑драйвер только для режима `Sgd`; хуки Run_Start, CharacterBody_Start и т.п. — находятся в отдельных классах драйвера и сенсоров (`SgdRuntimeDriver`, `SgdSensorsHooks`, актуаторы и др.).
- Артефакт `ArtifactOfGenetics` остаётся общим: включение/выключение — через `dda_genetics` и `RunArtifactManager`.
- Команда `dda_algorithm sgd` переключает состояние (`DdaAlgorithmState.ActiveAlgorithm = Sgd`) и активирует **реализованную** SGD‑логику; подробная архитектура и описание реализации находятся в `Docs/SgdDda/*`.

---

## Деплой после сборки

- После **успешной сборки** мода нужно копировать собранный **.dll** в папку плагинов RoR2 (`BepInEx/plugins`), чтобы сразу запускать игру с обновлённым модом.
- В проекте настроен пост‑сборочный шаг (`tools/InstallToRor2.ps1`), который **пытается автоматически** установить `GeneticsArtifact.dll` после `dotnet build`.

### Как работает авто‑установка
- Если задана `ROR2_PLUGINS_PATH` — `.dll` копируется туда (удобно для Thunderstore/r2modman профилей).
- Иначе скрипт пытается найти папку RoR2 через Steam (libraryfolders.vdf) или через переменные `ROR2_GAME_PATH` / `ROR2_DIR` / `ROR2_PATH`.
- Если путь не найден — сборка **не падает**, выводится предупреждение.

### Переменные окружения
Достаточно задать одну:

- `ROR2_PLUGINS_PATH` — папка `BepInEx\plugins` (или `...\plugins`) в которую надо копировать мод.
  - Пример (игра): `C:\Program Files (x86)\Steam\steamapps\common\Risk of Rain 2\BepInEx\plugins`
  - Пример (Thunderstore/r2modman профиль): укажите `...\profiles\<profile>\BepInEx\plugins`
- `ROR2_GAME_PATH` / `ROR2_DIR` / `ROR2_PATH` — корневая папка игры, например: `C:\Program Files (x86)\Steam\steamapps\common\Risk of Rain 2`
