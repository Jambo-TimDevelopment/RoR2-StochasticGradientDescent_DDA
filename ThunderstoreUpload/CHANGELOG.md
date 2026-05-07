# Changelog (fork vs upstream)

Upstream reference: **[Rico / GeneticArtifact](https://thunderstore.io/package/Rico/GeneticArtifact/)** (last Thunderstore release noted there: **4.5.3**). This package keeps the same core idea—**Artifact of Genetics** and per-monster **gene multipliers** (health, move speed, attack speed, damage)—but adds a **research DDA** path on top.

## 4.5.11 (this fork)

### Changed

- Added H3 decision-step instrumentation for all modes (`SGD`, `GA`, `FLS`) with explicit `h3_is_decision_step`, step index/reason/interval, and schema bump to `telemetry_schema_version = 6`.
- Unified axis virtual challenge mapping to `ln(clamped_multiplier)` and added per-axis virtual-gap epsilon flags for `hp`, `move_speed`, `attack_speed`, `attack_damage`.
- Updated H3 analysis script to consume decision-step semantics (`schema >= 6`), keep legacy fallback for schema 5, and report new axis metrics (sign-match, response gain, per-axis gap/epsilon coverage).

## 4.5.10 (this fork)

### Changed

- H3 virtual power/challenge now uses axis-aware schema (`hp`, `move_speed`, `attack_speed`, `attack_damage`) instead of relying on a single total-only path.
- Added item-aware build power estimation from inventory (`ItemCatalog`/`ItemDef.tags` + known-item whitelist) to improve axis attribution for SGD DDA telemetry and diagnostics.
- Telemetry schema updated to **v5** with new H3 axis fields and `h3_axis_schema=vp_vc_4_axes_v1`; legacy total fields are retained for transition compatibility.

## 4.5.4 (this fork)

### Added (vs Rico 4.5.3)

- **SGD DDA pipeline** (`SgdEngine/`): **Sensors → decision (stochastic gradient–style update) → actuators** that still drive the same `GeneStat` / `RecalculateStats` hooks as the genetic engine.
- **Runtime algorithm selection** (`DdaAlgorithmState`): **Genetic** (original GA loop), **SGD**, or **Fixed** baseline; default in source is oriented toward SGD experiments (switch via in-game cheats / rotation).
- **Cheat / debug**: console commands for algorithm mode, debug overlay, and parameter tweaks (`dda_*` family in `DdaCheatManager`).
- **Config extensions**: SGD normalization targets, per-axis **floor/cap** for gene stats, optional **diagnostic hook toggles** (SGD / actuators / telemetry / rotator), **research rotator** options (auto cycle algorithms and run seeds across runs).
- **Optional research telemetry** (PostHog; off by default, configurable—see mod config).
- **Repository documentation**: architecture and integration notes under `Docs/SgdDda/` and `RULE.md` (not shipped inside the Thunderstore zip unless you add them).

### Unchanged / compatibility notes

- **BepInEx plugin GUID** remains `com.RicoValdezio.ArtifactOfGenetics` for network / dependency consistency with the original mod family.
- **Genetic engine** (`GeneticEngine/`) behavior is preserved for when **Genetic** mode is active; SGD runs through parallel hooks/drivers.
---

For upstream history (4.5.3 and earlier), see the changelog on [Rico’s package page](https://thunderstore.io/package/Rico/GeneticArtifact/).

## Thunderstore icon (AI disclosure)

The package **icon** (`icon.png`) is the **256×256** neon DNA / chart motif for **PainGradient: Gradient-Descent DDA**. Some earlier Thunderstore revisions used a separate AI-assisted “MERCY / pain meter” graphic; uploads now use this DNA-style artwork by default.
