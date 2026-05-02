**Languages:** [English](#english) · [Русский](#russian)

---

<a id="english"></a>
# GeneticsArtifact (research build)

## What this is

A **Risk of Rain 2** mod on **BepInEx**: adds the **Genetics** artifact and a system that adjusts monster stats as fights progress. This build extends the original idea for a master’s thesis on dynamic difficulty (DDA).

## Relation to the original

Based on **GeneticArtifact** by **Rico** on Thunderstore:  
https://thunderstore.io/package/Rico/GeneticArtifact/

The artifact, genetic mode, and core “genes on monsters” idea remain; extra adaptation modes and research features are added. For code details see **GitHub** (link on the Thunderstore package page / repository field).

## What players should know

- The mod loads like any BepInEx plugin — install via a mod manager (or follow manual layout). Nothing extra to “launch.”
- **By default, adaptation strategy rotates between new runs**: fixed difficulty, Rico-style genetics, and an extra adaptive mode. You do not need console commands to switch unless you turn that off (see below).
- In-run, the **Genetics artifact** still matters — enable it the same way as in the original (challenge, portal code, etc.; see Rico’s page).

## In-game settings (**Risk of Options**)

With **Risk of Options**, the mod appears in the game menu (**Genetics** section): learning limits, gene floors/caps, separate limits for adaptive HP/speed/damage axes, and other balance knobs.

## If you don’t want auto mode rotation or data collection

Extra toggles (auto mode rotation, research telemetry, etc.) live in the **BepInEx config file**, not only Risk of Options:

`Risk of Rain 2\BepInEx\config\com.RicoValdezio.ArtifactOfGenetics.cfg`

Edit in a text editor if needed:

- **Research DDA Rotation** — set **Auto Rotate Algorithms** = `false` to stop automatic mode changes between runs;
- **Research Telemetry** — **Telemetry Enabled** = `false` to disable anonymous research uploads.

Save and restart the game (or start a new run) as usual for BepInEx configs.

## Dependencies

Required mods match the Thunderstore package card (BepInEx, R2API, Risk of Options, etc.) — the mod manager installs them.

## Install

Use **r2modman / Thunderstore Mod Manager** or manual layout from the package archive (see Thunderstore docs).

---

*Full technical docs, experiment design, and source — on GitHub (link in the package description).*

---

<a id="russian"></a>
# GeneticsArtifact (исследовательская сборка)

## Что это

Мод для **Risk of Rain 2** на базе **BepInEx**: добавляет артефакт **Genetics** и систему, которая подстраивает характеристики монстров под ход боя. Эта сборка развивает оригинальную идею для магистерской работы по динамической сложности (DDA).

## Связь с оригиналом

Основа — мод **GeneticArtifact** автора **Rico** на Thunderstore:  
https://thunderstore.io/package/Rico/GeneticArtifact/

Здесь сохранены артефакт, генетический режим и общая идея «генов» у монстров; дополнительно добавлены режимы адаптации сложности и исследовательские функции. Как именно это устроено в коде — см. **GitHub** (ссылка в карточке пакета на Thunderstore, поле сайта / репозитория).

## Как это работает для вас

- **Мод подключается сам**, как обычный плагин BepInEx: достаточно установить пакет через менеджер модов (или положить файлы по инструкции менеджера). Отдельно «запускать» мод не нужно.
- **По умолчанию между новыми забегами сама меняется стратегия адаптации сложности**: по очереди используются три режима — фиксированная сложность, классическая генетика как у Rico, и дополнительный адаптивный режим. Вам не нужно вводить команды, чтобы переключаться между ними, если вы специально не отключите эту логику (см. ниже).
- Внутри забега по-прежнему важен **артефакт Genetics**: включайте его так же, как в оригинальном моде (челлендж, код портала и т.д. — см. страницу Rico по ссылке выше).

## Настройки в игре (**Risk of Options**)

Если установлен **Risk of Options**, в меню игры появятся настройки мода (раздел мода **Genetics**): лимиты обучения, потолки и полы для генов, отдельные ограничения для адаптивного режима по здоровью, скорости и урону и другие параметры баланса. Меняйте их, если хотите смягчить или усилить эффект.

## Если не нужна автосмена режимов или сбор данных

Дополнительные переключатели (автосмена режима между забегами, телеметрия исследования и т.п.) лежат в **файле конфигурации BepInEx**, а не в Risk of Options:

`Risk of Rain 2\BepInEx\config\com.RicoValdezio.ArtifactOfGenetics.cfg`

Откройте файл текстовым редактором и при необходимости выставьте:

- секция **Research DDA Rotation** — опция **Auto Rotate Algorithms** = `false`, если хотите, чтобы режим адаптации **не** менялся сам между забегами;
- секция **Research Telemetry** — **Telemetry Enabled** = `false`, если не хотите отправку анонимных данных для исследования.

После правок сохраните файл и перезапустите игру (или смените забег — как обычно для конфигов BepInEx).

## Зависимости

Список обязательных модов совпадает с карточкой пакета на Thunderstore (BepInEx, R2API, Risk of Options и др.) — менеджер модов подтянет их сам.

## Установка

Через **r2modman / Thunderstore Mod Manager** или вручную по структуре архива пакета (см. документацию Thunderstore).

---

*Подробная техническая документация, эксперимент и исходный код — в репозитории на GitHub (ссылка в описании пакета).*
