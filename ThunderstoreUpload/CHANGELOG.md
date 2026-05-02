# Changelog (fork vs upstream)

Upstream reference: **[Rico / GeneticArtifact](https://thunderstore.io/package/Rico/GeneticArtifact/)** (last Thunderstore release noted there: **4.5.3**). This package keeps the same core idea—**Artifact of Genetics** and per-monster **gene multipliers** (health, move speed, attack speed, damage)—but adds a **research DDA** path on top.

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

The package **icon** (`icon.png`) was created with **generative AI** (AI-assisted image generation), then cropped and resized to **256×256** for Thunderstore.
