**Languages:** [English](#english) · [Русский](#russian)

---

<a id="english"></a>
## Practical SGD DDA integration

How SGD fits into the RoR2 loop, coexists with GA, and how to use the mod in practice. **Four multiplier axes** (`MaxHealth`, `MoveSpeed`, `AttackSpeed`, `AttackDamage`), console toggles `dda_sgd_axis_*`, BepInEx keys `sgd*Floor`/`sgd*Cap`, and the link to \(V_c\) are covered in [`Axes.md`](Axes.md#english).

### Artifact and DDA state

Main entry point: `DdaAlgorithmState`. `ActiveAlgorithm` selects `Fixed` (FLS for comparison), `Genetic` (reference GA), or `Sgd`. Switch with `Activate(DdaAlgorithmType)`. `IsGeneticAlgorithmEnabled` preserves expected Genetics behavior; `ShouldRunGeneticEngine()` decides whether GA runs; `IsDebugOverlayEnabled` allows sensors/telemetry collection even without active SGD.

Artifact enable rules in a run are unchanged from [`RULE.md`](../../RULE.md#english): SGD does not replace Genetics unlock rules. GA vs SGD is chosen only via `ActiveAlgorithm` and commands built on it.

### RoR2 lifecycle

On **`Run.Start`** (`SgdRuntimeDriver.Run_Start`), `SgdDecisionRuntimeState` and `SgdActuatorsRuntimeState` reset, multipliers apply to all living monsters, `SgdRuntimeDriver` attaches to `Run`.

On **`CharacterBody.Start`** (`SgdActuatorsHooks`) with `NetworkServer.active`, `Sgd` mode, monster team, and `inventory`, the four multipliers apply via `SgdGeneStatTokenApplier` and `RecalculateStats()`.

**`HealthComponent.TakeDamage`** in `SgdSensorsHooks` feeds incoming damage to the player and outgoing to monsters. **`CharacterBody.OnDestroy`** records player deaths (windows) and monster deaths (TTK). Hooks do not alter base RoR2 gameplay.

### GeneticEngine

Per [`RULE.md`](../../RULE.md#english), avoid editing `GeneEngineDriver`, `MasterGeneBehaviour`, `MonsterGeneBehaviour`, `GeneTokenCalc`, `GeneTokens` for SGD. Shared layer: same `GeneStat` and tokens; SGD writes multipliers via `SgdGeneStatTokenApplier`. This keeps artifact compatibility, GA code isolation, and simple A/B mode switching.

### Networking

`SgdDecisionDriver.Tick` and actuator application run under `NetworkServer.active` so adaptation and multipliers stay consistent for all clients.

### Deploy and usage

Build produces `GeneticsArtifact.dll` (e.g. `bin/Debug/netstandard2.1/` or Release). Install into BepInEx `BepInEx/plugins` or use `tools/InstallToRor2.ps1` from [`RULE.md`](../../RULE.md#english). Environment variables: `ROR2_PLUGINS_PATH`, `ROR2_GAME_PATH` / `ROR2_DIR` / `ROR2_PATH`.

After install: launch with the mod, enable Genetics as usual, switch DDA to SGD via UI/menu or CheatManager console. For diagnostics, `DdaAlgorithmState.IsDebugOverlayEnabled` (sensors, multipliers, per-axis skill/challenge; see [`Axes.md`](Axes.md#english)).

### Research rotation (FLS → GA → SGD)

`CheatManager/DdaRunModeRotator.cs` and **Research DDA Rotation** config (`Auto Rotate Algorithms`, `Last Run Algorithm`) auto-switch mode at run start for comparable sessions.

### Further integration notes

Consider exposing DDA mode in UI (e.g. Risk of Options) and extending config with SGD hyperparameters as needed; for thesis work, logs, PostHog, and overlay provide time series over axes and sensors.

---

<a id="russian"></a>
## Практическая интеграция SGD DDA

Документ описывает встраивание SGD в цикл RoR2, сосуществование с GA и практическое использование. **Четыре оси множителей** (`MaxHealth`, `MoveSpeed`, `AttackSpeed`, `AttackDamage`), консольные переключатели `dda_sgd_axis_*`, ключи BepInEx `sgd*Floor`/`sgd*Cap` и связь с \(V_c\) — в [`Axes.md`](Axes.md#russian).

### Связь с артефактом и состоянием DDA

Главная точка входа — `DdaAlgorithmState`. Поле `ActiveAlgorithm` выбирает режим: `Fixed` (FLS для сравнения), `Genetic` (исходный GA) или `Sgd`. Переключение — `Activate(DdaAlgorithmType)`. `IsGeneticAlgorithmEnabled` сохраняет ожидаемое поведение артефакта Genetics; `ShouldRunGeneticEngine()` решает, крутить ли GA; `IsDebugOverlayEnabled` позволяет собирать сенсоры и телеметрию даже без активного SGD.

Правила включения артефакта в забеге те же, что в [`RULE.md`](../../RULE.md#russian): SGD не подменяет условия появления Genetics. Выбор GA vs SGD — только через `ActiveAlgorithm` и команды поверх него.

### Интеграция с жизненным циклом RoR2

На **`Run.Start`** (`SgdRuntimeDriver.Run_Start`) сбрасываются `SgdDecisionRuntimeState` и `SgdActuatorsRuntimeState`, множители применяются ко всем живым монстрам, на `Run` вешается `SgdRuntimeDriver`.

На **`CharacterBody.Start`** (`SgdActuatorsHooks`) при `NetworkServer.active`, режиме `Sgd`, команде монстров и наличии `inventory` к телу применяются четыре множителя через `SgdGeneStatTokenApplier` и `RecalculateStats()`.

**`HealthComponent.TakeDamage`** в `SgdSensorsHooks` кормит сенсоры входящим уроном по игроку и исходящим по монстрам. **`CharacterBody.OnDestroy`** фиксирует смерти игрока (окна смертей) и монстров (TTK). Отдельные хуки не меняют базовый геймплей RoR2.

### Интеграция с GeneticEngine

По [`RULE.md`](../../RULE.md#russian) не следует править `GeneEngineDriver`, `MasterGeneBehaviour`, `MonsterGeneBehaviour`, `GeneTokenCalc`, `GeneTokens` ради SGD. Общий слой — те же `GeneStat` и токены; SGD пишет множители через `SgdGeneStatTokenApplier`. Так сохраняются совместимость с артефактом, изоляция кода GA и простое A/B переключение режимов.

### Сетевой аспект

`SgdDecisionDriver.Tick` и применение актуаторов выполняются при `NetworkServer.active`, чтобы адаптация и множители были едины для всех клиентов.

### Деплой и использование

Сборка и установка мода не меняются по сравнению с базовым артефактом Genetics:

- Соберите проект, чтобы получить:
  - `bin/Debug/netstandard2.1/GeneticsArtifact.dll` (или соответствующую сборку Release).
- Установите мод в игру:
  - поместите `GeneticsArtifact.dll` в папку плагинов BepInEx:
    - `BepInEx/plugins` внутри директории RoR2;
  - или воспользуйтесь скриптом `tools/InstallToRor2.ps1`, описанным в [`RULE.md`](../../RULE.md#russian).
- Переменные окружения для автопоиска пути игры/плагинов также описаны в `RULE.md`:
  - `ROR2_PLUGINS_PATH`
  - `ROR2_GAME_PATH` / `ROR2_DIR` / `ROR2_PATH`

После установки: запустить игру с модом, включить Genetics как обычно, переключить DDA на SGD через настройки/меню или консоль CheatManager. Для диагностики — `DdaAlgorithmState.IsDebugOverlayEnabled` (сенсоры, множители, skill/challenge по осям; см. [`Axes.md`](Axes.md#russian)).

### Research rotation (FLS → GA → SGD)

`CheatManager/DdaRunModeRotator.cs` и конфиги **Research DDA Rotation** (`Auto Rotate Algorithms`, `Last Run Algorithm`) задают автосмену режима на старте забега для сопоставимых сессий.

### Рекомендации по дальнейшей интеграции

Имеет смысл вынести переключение алгоритма DDA в UI (например Risk of Options) и по мере необходимости расширять конфиг гиперпараметрами SGD; для NIR — логи, PostHog и overlay как источники временных рядов по осям и сенсорам.
