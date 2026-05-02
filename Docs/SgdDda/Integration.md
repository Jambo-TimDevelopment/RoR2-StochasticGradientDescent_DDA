## Практическая интеграция SGD DDA

Документ описывает встраивание SGD в цикл RoR2, сосуществование с GA и практическое использование. **Четыре оси множителей** (`MaxHealth`, `MoveSpeed`, `AttackSpeed`, `AttackDamage`), консольные переключатели `dda_sgd_axis_*`, ключи BepInEx `sgd*Floor`/`sgd*Cap` и связь с \(V_c\) — в [`Axes.md`](Axes.md).

### Связь с артефактом и состоянием DDA

Главная точка входа — `DdaAlgorithmState`. Поле `ActiveAlgorithm` выбирает режим: `Fixed` (FLS для сравнения), `Genetic` (исходный GA) или `Sgd`. Переключение — `Activate(DdaAlgorithmType)`. `IsGeneticAlgorithmEnabled` сохраняет ожидаемое поведение артефакта Genetics; `ShouldRunGeneticEngine()` решает, крутить ли GA; `IsDebugOverlayEnabled` позволяет собирать сенсоры и телеметрию даже без активного SGD.

Правила включения артефакта в забеге те же, что в [`RULE.md`](../../RULE.md): SGD не подменяет условия появления Genetics. Выбор GA vs SGD — только через `ActiveAlgorithm` и команды поверх него.

### Интеграция с жизненным циклом RoR2

На **`Run.Start`** (`SgdRuntimeDriver.Run_Start`) сбрасываются `SgdDecisionRuntimeState` и `SgdActuatorsRuntimeState`, множители применяются ко всем живым монстрам, на `Run` вешается `SgdRuntimeDriver`.

На **`CharacterBody.Start`** (`SgdActuatorsHooks`) при `NetworkServer.active`, режиме `Sgd`, команде монстров и наличии `inventory` к телу применяются четыре множителя через `SgdGeneStatTokenApplier` и `RecalculateStats()`.

**`HealthComponent.TakeDamage`** в `SgdSensorsHooks` кормит сенсоры входящим уроном по игроку и исходящим по монстрам. **`CharacterBody.OnDestroy`** фиксирует смерти игрока (окна смертей) и монстров (TTK). Отдельные хуки не меняют базовый геймплей RoR2.

### Интеграция с GeneticEngine

По [`RULE.md`](../../RULE.md) не следует править `GeneEngineDriver`, `MasterGeneBehaviour`, `MonsterGeneBehaviour`, `GeneTokenCalc`, `GeneTokens` ради SGD. Общий слой — те же `GeneStat` и токены; SGD пишет множители через `SgdGeneStatTokenApplier`. Так сохраняются совместимость с артефактом, изоляция кода GA и простое A/B переключение режимов.

### Сетевой аспект

`SgdDecisionDriver.Tick` и применение актуаторов выполняются при `NetworkServer.active`, чтобы адаптация и множители были едины для всех клиентов.

### Деплой и использование

Сборка и установка мода не меняются по сравнению с базовым артефактом Genetics:

- Соберите проект, чтобы получить:
  - `bin/Debug/netstandard2.1/GeneticsArtifact.dll` (или соответствующую сборку Release).
- Установите мод в игру:
  - поместите `GeneticsArtifact.dll` в папку плагинов BepInEx:
    - `BepInEx/plugins` внутри директории RoR2;
  - или воспользуйтесь скриптом `tools/InstallToRor2.ps1`, описанным в [`RULE.md`](../../RULE.md).
- Переменные окружения для автопоиска пути игры/плагинов также описаны в `RULE.md`:
  - `ROR2_PLUGINS_PATH`
  - `ROR2_GAME_PATH` / `ROR2_DIR` / `ROR2_PATH`

После установки: запустить игру с модом, включить Genetics как обычно, переключить DDA на SGD через настройки/меню или консоль CheatManager. Для диагностики — `DdaAlgorithmState.IsDebugOverlayEnabled` (сенсоры, множители, skill/challenge по осям; см. [`Axes.md`](Axes.md)).

### Research rotation (FLS → GA → SGD)

`CheatManager/DdaRunModeRotator.cs` и конфиги **Research DDA Rotation** (`Auto Rotate Algorithms`, `Last Run Algorithm`) задают автосмену режима на старте забега для сопоставимых сессий.

### Рекомендации по дальнейшей интеграции

Имеет смысл вынести переключение алгоритма DDA в UI (например Risk of Options) и по мере необходимости расширять конфиг гиперпараметрами SGD; для NIR — логи, PostHog и overlay как источники временных рядов по осям и сенсорам.

