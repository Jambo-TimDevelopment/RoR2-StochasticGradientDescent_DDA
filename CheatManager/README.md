**Languages:** [English](#english) · [Русский](#russian)

---

<a id="english"></a>
# CheatManager: console commands and debugging

Commands register in `DdaCheatManager` via `[ConCommand]` and R2API `CommandHelper`. Enter them in the in-game RoR2 console (how to open depends on your build/mods; **DebugToolkit** or similar helps).

Command prefix: **`dda_`**.

## Debug overlay (`dda_debug_overlay`)

Semi-transparent text panel at the top: DDA mode, player virtual power `V_p` (offense/defense/mobility/total), SGD **actuators** (HP/MS/AS/DMG multipliers), SGD **decision** block (step interval, combat timer, enabled axes, step counters, last skill/challenge/error and gradients per axis), **sensors** (DPS, hits on player, combat uptime, etc.).

Overlay does **not** capture clicks.

Toggle: `dda_debug_overlay`; `dda_debug_overlay 1` on; `dda_debug_overlay 0` off.

If `V_p` shows `N/A`, you need an active run and virtual-power data (including SGD mode).

## Telemetry overlay (`dda_telemetry_overlay`)

Shows the **last queued** telemetry sample (`dda_sample`) — quick PostHog sanity check without the full debug overlay.

Toggle: `dda_telemetry_overlay`, `dda_telemetry_overlay 1` / `0`.

## DDA modes

| Command | Description |
|--------|-------------|
| `dda_algorithm` | No args — print current algorithm. |
| `dda_algorithm fixed` or `fls` | Fixed difficulty (FLS). |
| `dda_algorithm genetic` or `ga` | Genetic algorithm (reference mod). |
| `dda_algorithm sgd` | SGD DDA. |
| `dda_genetics` | Toggle “genetics” (no arg = flip; `dda_genetics 1` genetic, `0` fixed per mod semantics). |

## SGD: step and axes

| Command | Description |
|--------|-------------|
| `dda_sgd_step_time` | Show current step interval (combat seconds). |
| `dda_sgd_step_time 10` | Set step interval (**combat time** only). Setting this enables SGD mode. |
| `dda_sgd_axis_hp [0\|1]` | Enable/disable **MaxHealth** axis. No arg = toggle. |
| `dda_sgd_axis_ms [0\|1]` | **MoveSpeed**. |
| `dda_sgd_axis_as [0\|1]` | **AttackSpeed**. |
| `dda_sgd_axis_dmg [0\|1]` | **AttackDamage**. |

## SGD: manual multipliers (actuators)

Set monster stat multipliers; on **host**, applies to spawned and future monsters. Decimals: `1.5` or `1,5`.

| Command | Description |
|--------|-------------|
| `dda_actuator_hp` | Show current HP multiplier. |
| `dda_actuator_hp 1.25` | Set **MaxHealth** multiplier. |
| `dda_actuator_ms`, `dda_actuator_as`, `dda_actuator_dmg` | Move speed, attack speed, damage. |

Mod log tip: when tuning actuators manually, sometimes disable genetics interference: `dda_genetics 0`.

If you are not the server (`NetworkServer` inactive), the value is stored but application may warn in the log.

## Other

| Command | Description |
|--------|-------------|
| `dda_show_monster_hp` | **Client** HP numbers above monsters. No arg = toggle; `0`/`1` off/on. |
| `dda_survey <1-7> <1-7> [comment]` | Post-session survey (1–7 fairness & continuity). Needs active telemetry session. |
| `dda_param` | Stub: logs not implemented; use BepInEx config / Risk of Options. |

## Source files

- `DdaCheatManager.cs` — registration
- `DebugOverlayBehaviour.cs` — debug overlay text
- `TelemetryDebugOverlayBehaviour.cs` — telemetry overlay

SGD methodology: [`Docs/SgdDda/ToolsAndDebug.md`](../Docs/SgdDda/ToolsAndDebug.md#english).

---

<a id="russian"></a>
# CheatManager: консольные команды и отладка

Команды регистрируются в `DdaCheatManager` через атрибуты `[ConCommand]` и R2API `CommandHelper`. Их нужно вводить в **игровой консоли** RoR2 (способ открытия зависит от сборки и модов; для удобства часто ставят **DebugToolkit** или другой мод с консолью).

Префикс команд: **`dda_`**.

---

## Дебаг-оверлей (`dda_debug_overlay`)

**Назначение:** полупрозрачная текстовая панель вверху экрана с состоянием DDA: активный алгоритм, виртуальная сила игрока `V_p` (offense / defense / mobility / total), **актуаторы** SGD (множители HP, MS, AS, DMG), блок **решения SGD** (шаг по времени, таймер боя, включённые оси, счётчики шагов, последние skill/challenge/error и градиент по осям), **сенсоры** (DPS, попадания по игроку, аптайм боя и т.д.).

Оверлей **не перехватывает клики** (сквозной для UI).

**Включить / выключить:**

- `dda_debug_overlay` — переключить;
- `dda_debug_overlay 1` — включить;
- `dda_debug_overlay 0` — выключить.

Если `V_p` показывает `N/A`, нужен активный забег и данные по виртуальной силе (в т.ч. режим SGD).

---

## Оверлей телеметрии (`dda_telemetry_overlay`)

Показывает **последний поставленный в очередь** сэмпл телеметрии (`dda_sample`) — удобно проверять, что уходит в PostHog, без полного текста дебаг-оверлея.

**Включение:** `dda_telemetry_overlay`, `dda_telemetry_overlay 1` / `0` — по аналогии с дебаг-оверлеем.

---

## Режимы DDA

| Команда | Описание |
|--------|----------|
| `dda_algorithm` | Без аргументов — вывести текущий алгоритм. |
| `dda_algorithm fixed` или `fls` | Фиксированная сложность (FLS). |
| `dda_algorithm genetic` или `ga` | Генетический алгоритм (как в референс-моде). |
| `dda_algorithm sgd` | SGD DDA. |
| `dda_genetics` | Переключить «генетику» (без аргумента — тумблер; `dda_genetics 1` — genetic, `0` — fixed в смысле переключателя). |

---

## SGD: шаг и оси

| Команда | Описание |
|--------|----------|
| `dda_sgd_step_time` | Текущий интервал шага (секунды боя). |
| `dda_sgd_step_time 10` | Задать интервал шага (учитывается только **время боя**). При установке включается режим SGD. |
| `dda_sgd_axis_hp [0\|1]` | Вкл/выкл ось адаптации **MaxHealth**. Без аргумента — переключить. |
| `dda_sgd_axis_ms [0\|1]` | То же для **MoveSpeed**. |
| `dda_sgd_axis_as [0\|1]` | То же для **AttackSpeed**. |
| `dda_sgd_axis_dmg [0\|1]` | То же для **AttackDamage**. |

---

## SGD: ручные множители (актуаторы)

Задают множитель статов монстров; на **хосте** применяются к уже заспавненным монстрам и к будущим. Дробь: `1.5` или `1,5`.

| Команда | Описание |
|--------|----------|
| `dda_actuator_hp` | Показать текущий множитель HP. |
| `dda_actuator_hp 1.25` | Задать множитель **MaxHealth**. |
| `dda_actuator_ms`, `dda_actuator_as`, `dda_actuator_dmg` | Аналогично для скорости передвижения, скорости атаки и урона. |

Совет из лога мода: при ручной настройке актуаторов иногда полезно отключить вмешательство генетики: `dda_genetics 0`.

Если вы не на сервере (`NetworkServer` не активен), значение запомнится, но применение к монстрам в логе будет предупреждение.

---

## Прочее

| Команда | Описание |
|--------|----------|
| `dda_show_monster_hp` | **Клиентский** оверлей чисел HP над монстрами. Без аргумента — тумблер; `0` / `1` — выкл / вкл. |
| `dda_survey <1-7> <1-7> [комментарий]` | Отправить пост-сессионный опрос (шкала 1–7: справедливость и плавность сложности). Нужна активная телеметрическая сессия. |
| `dda_param` | Заглушка: в лог пишет, что не реализовано; параметры — через конфиг BepInEx / Risk of Options. |

---

## Где смотреть код

- Регистрация команд: `DdaCheatManager.cs`
- Текст дебаг-оверлея: `DebugOverlayBehaviour.cs`
- Телеметрический оверлей: `TelemetryDebugOverlayBehaviour.cs`

Подробная методика и консоль в контексте SGD: [`Docs/SgdDda/ToolsAndDebug.md`](../Docs/SgdDda/ToolsAndDebug.md#russian).
