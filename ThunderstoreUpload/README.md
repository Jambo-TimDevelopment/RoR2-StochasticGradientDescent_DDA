**Languages:** [English](#english) · [Русский](#russian)

---

<a id="english"></a>
# PainGradient: Gradient-Descent DDA

**PainGradient** is a **stochastic gradient descent** DDA fork (not a genetic-algorithm mod): monster **HP, damage, and speeds** adapt as the run goes on. You still enable the in-game **Genetics** artifact Rico-style; the research build adds extra modes and optional telemetry.

## Quick start (what you do in-game)

1. Install with **r2modman / Thunderstore Mod Manager** (dependencies install automatically).
2. Start a run and **turn on the Genetics artifact** the same way as in **Rico’s** original mod (challenge, portal code, etc. — see [Rico’s page](https://thunderstore.io/package/Rico/GeneticArtifact/)).
3. Play normally. Monster **health, damage, and movement/attack speed** can change over time — the mod is built to **squeeze** difficulty, not to coddle.
4. Optional: open **Risk of Options** → **PainGradient: Gradient-Descent DDA** for balance sliders (limits, floors/caps, per-axis tuning).

**You do not need to “launch” anything extra** — it is a normal BepInEx plugin.

## What you’ll notice

- **In a run**, gene-style multipliers on monsters can adapt as combat goes on.
- **Between runs**, the mod may **rotate how adaptation works** (fixed-style baseline, classic Rico-like genetics, and an extra adaptive path). That way you get variety without console commands — unless you turn rotation off (below).

## Settings

| Where | What |
|--------|------|
| **Risk of Options** → **PainGradient: Gradient-Descent DDA** | Most gameplay knobs: learning limits, gene min/max, separate caps for HP / speed / damage style axes. |
| **BepInEx config** | Advanced / research toggles: auto algorithm rotation, telemetry, etc. |

Config file path:

`Risk of Rain 2\BepInEx\config\com.RicoValdezio.ArtifactOfGenetics.cfg`

Useful options:

- **Research DDA Rotation** → **Auto Rotate Algorithms** = `false` — stop automatic mode changes between runs.
- **Research Telemetry** → **Telemetry Enabled** = `false` — opt out of anonymous research uploads to PostHog (does not affect gameplay). The default in a fresh config may be **on** — set to `false` if you want no uploads.

After edits, restart the game or start a new run as usual for BepInEx configs.

## Relation to the original mod

Fork / research build based on **[GeneticArtifact by Rico](https://thunderstore.io/package/Rico/GeneticArtifact/)**: same in-game **Genetics** artifact and “genes on monsters” presentation. **PainGradient: Gradient-Descent DDA** drives adaptation with **stochastic gradient descent** (and can expose other research modes); it is **not** a genetic-algorithm mod. Details are on **GitHub** (repository link on the Thunderstore package page).

## Dependencies

Match the Thunderstore package card (BepInEx, R2API modules, Risk of Options, etc.).

## Install

**r2modman / Thunderstore Mod Manager**, or manual layout from the package zip (see [Thunderstore package format](https://thunderstore.io/package/create/docs/)).

---

*Full technical docs and source — GitHub (link in the package description / website field).*

---

<a id="russian"></a>
# PainGradient: DDA на градиентном спуске

**PainGradient** — это DDA на **стохастическом градиентном спуске** (не генетический алгоритм): **здоровье, урон и скорости** монстров подстраиваются по ходу забега. В игре по-прежнему включается артефакт **Genetics** в духе Rico; в исследовательской сборке есть дополнительные режимы и опциональная телеметрия.

## Быстрый старт (что делать в игре)

1. Установите через **r2modman / Thunderstore Mod Manager** (зависимости подтянутся сами).
2. В забеге **включите артефакт Genetics** так же, как в **оригинальном моде Rico** (челлендж, код портала и т.д. — см. [страницу Rico](https://thunderstore.io/package/Rico/GeneticArtifact/)).
3. Играйте как обычно. У монстров со временем могут меняться **здоровье, урон и скорости** — мод заточен **поджимать** сложность, а не обнимать.
4. По желанию: **Risk of Options** → **PainGradient: Gradient-Descent DDA** — ползунки баланса (лимиты, пол/потолок, отдельные оси).

**Отдельно ничего «запускать» не нужно** — это обычный плагин BepInEx.

## Что изменится в игре

- **Внутри забега** множители «генов» у монстров могут подстраиваться по ходу боя.
- **Между забегами** мод может **по очереди менять способ адаптации** (базовый режим, классическая генетика в духе Rico, дополнительный адаптивный режим). Так можно получить разнообразие без консоли — если не отключить ротацию (ниже).

## Настройки

| Где | Что |
|-----|-----|
| **Risk of Options** → **PainGradient: Gradient-Descent DDA** | Основные игровые параметры: лимиты обучения, мин/макс генов, отдельные ограничения по здоровью / скорости / урону. |
| **Конфиг BepInEx** | Расширенные / исследовательские опции: автосмена режима, телеметрия и т.д. |

Путь к конфигу:

`Risk of Rain 2\BepInEx\config\com.RicoValdezio.ArtifactOfGenetics.cfg`

Полезные опции:

- **Research DDA Rotation** → **Auto Rotate Algorithms** = `false` — режим адаптации **не** меняется сам между забегами.
- **Research Telemetry** → **Telemetry Enabled** = `false` — отказ от анонимной отправки данных исследования (PostHog); на геймплей не влияет. По умолчанию в конфиге может быть включено — проверьте файл, если хотите отключить.

После правок сохраните файл и перезапустите игру или начните новый забег, как обычно для конфигов BepInEx.

## Связь с оригиналом

Исследовательский форк на базе **[GeneticArtifact (Rico)](https://thunderstore.io/package/Rico/GeneticArtifact/)**: тот же внутриигровой артефакт **Genetics** и подача «генов» у монстров. **PainGradient: Gradient-Descent DDA** задаёт адаптацию через **стохастический градиентный спуск** (и может включать другие исследовательские режимы); это **не** мод на генетическом алгоритме. Подробности — в **GitHub** (ссылка в карточке пакета).

## Зависимости

Как в карточке пакета на Thunderstore (BepInEx, модули R2API, Risk of Options и др.).

## Установка

**r2modman / Thunderstore Mod Manager** или ручная раскладка из архива (см. [формат пакета Thunderstore](https://thunderstore.io/package/create/docs/)).

---

*Техническая документация и исходный код — GitHub (ссылка в описании пакета / website).*
