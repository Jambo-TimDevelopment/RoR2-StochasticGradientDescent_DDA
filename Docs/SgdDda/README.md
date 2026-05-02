**Languages:** [English](#english) · [Русский](#russian)

---

<a id="english"></a>
## SGD DDA overview

The mod extends the Genetics artifact with an alternative **dynamic difficulty adjustment (DDA)** mode based on **stochastic gradient descent (SGD)**. The goal is to tune enemy pressure to the current run so the game stays tense but manageable. Unlike the genetic algorithm, SGD does not evolve a gene population over generations; it **continuously** adjusts difficulty parameters over time as online learning.

Pipeline: **sensors** collect combat telemetry; the **decision module (SGD)** compares observed “skill” with per-axis “difficulty” and takes gradient steps; **actuators** write new multipliers into monster `GeneStat` values. The four axes — `MaxHealth`, `MoveSpeed`, `AttackSpeed`, `AttackDamage` — are fully documented in [`Axes.md`](Axes.md#english): \(\theta = \ln m\), `challenge01` and `skill01`, config, console, and `axis_*` telemetry.

Mod-wide rules and `GeneticEngine` integration: [`RULE.md`](../../RULE.md#english).

### Documentation map

- [`Axes.md`](Axes.md#english) — **four difficulty axes**: model, per-axis sensors, limits, actuator sync, `axis_*` telemetry.
- [`Architecture.md`](Architecture.md#english) — subsystems, data flow, PostHog overview.
- [`Implementation.md`](Implementation.md#english) — mapping ideas to `SgdEngine/`, qualitative SGD step.
- [`Integration.md`](Integration.md#english) — RoR2 lifecycle, networking, deploy, coexistence with GA.
- [`ToolsAndDebug.md`](ToolsAndDebug.md#english) — CheatManager, overlay, H1–H6 protocols, analysis scripts.

### Research tooling

**Telemetry schema v2+** adds experiment segmentation, thresholds, hyperparameter snapshots, and data-quality fields; degradation episodes and H5/H6 survey events. **Post-run survey** — Likert 1–7 widget (RU/EN). Thunderstore dev install: `build_and_install_thunderstore_devfornir.bat` (profile `DevForNIR`, duplicate-DLL guard).

---

<a id="russian"></a>
## Обзор алгоритма SGD DDA

Мод расширяет артефакт «Genetics» альтернативным режимом динамической адаптации сложности (DDA) на **стохастическом градиентном спуске (SGD)**. Цель — подстраивать давление со стороны врагов под текущую игру так, чтобы баланс оставался напряжённым, но управляемым. В отличие от генетического алгоритма, SGD не эволюционирует популяцию генов по поколениям, а **непрерывно** подкручивает параметры сложности во времени как онлайн-обучение.

По конвейеру данные идут так: **сенсоры** собирают телеметрию боя игрока, **модуль решения (SGD)** сравнивает наблюдаемый «скилл» с выставленной по каждой оси «сложностью» и делает шаги градиентного спуска, **актуаторы** записывают новые множители в `GeneStat` монстров. Четыре оси — `MaxHealth`, `MoveSpeed`, `AttackSpeed`, `AttackDamage` — описаны целиком в [`Axes.md`](Axes.md#russian): параметр \(\theta = \ln m\), величины `challenge01` и `skill01`, конфиг, консоль и телеметрия.

Общие правила мода и интеграция с `GeneticEngine` — в [`RULE.md`](../../RULE.md#russian).

### Навигация по документации

- [`Axes.md`](Axes.md#russian) — **четыре оси сложности**: модель, сенсоры по осям, лимиты, синхронизация с актуаторами, телеметрия `axis_*`.
- [`Architecture.md`](Architecture.md#russian) — подсистемы, поток данных, обзор телеметрии PostHog.
- [`Implementation.md`](Implementation.md#russian) — соответствие идей коду в `SgdEngine/`, качественная модель шага SGD.
- [`Integration.md`](Integration.md#russian) — жизненный цикл RoR2, сеть, деплой, сосуществование с GA.
- [`ToolsAndDebug.md`](ToolsAndDebug.md#russian) — CheatManager, overlay, протоколы H1–H6 и скрипты анализа.

### Новое в исследовательском инструментарии

**Telemetry schema v2+** дополняет события полями сегментации эксперимента, порогов, гиперпараметров и качества данных; добавлены события эпизодов деградации и анкета H5/H6. **Post-run survey** — виджет Likert 1–7 (RU/EN). Для установки в профиль Thunderstore используется скрипт `build_and_install_thunderstore_devfornir.bat` (профиль `DevForNIR`, защита от дубликатов DLL).
