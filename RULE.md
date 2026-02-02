# Project Rules: DDA Algorithm for Risk of Rain 2

The mod implements a Dynamic Difficulty Adaptation (DDA) algorithm based on gradient descent for a master's thesis. The existing genetic algorithm is the reference; the target is SGD.

---

## DDA Architecture

```
Sensors (player metrics) → Decision module (gradient descent) → Actuators (monster parameters)
```

- **Sensors:** collect data on player behavior (accuracy, damage, survivability, time in combat, etc.) [Sensors list TODO from author]
- **Decision module:** analyzes metrics, computes gradient, updates difficulty parameters
- **Actuators:** apply changes to monsters (HP, damage, speed, spawn)

---

## Stack and Dependencies

- **BepInEx** — mod loader
- **R2API** — ArtifactCode, ContentManagement, Items, Language, RecalculateStats, CommandHelper
- **RoR2 API** — Run, CharacterBody, HealthComponent, TeamIndex, RunArtifactManager, Stage
- **On.** — Harmony patching (e.g., `On.RoR2.HealthComponent.TakeDamage`)

---

## Key Files and Their Roles

| File | Purpose |
|------|---------|
| `GeneticsArtifactPlugin.cs` | Entry point, Awake, Init of all subsystems |
| `CheatManager/DdaAlgorithmState.cs` | State: `IsGeneticAlgorithmEnabled`, `ActiveAlgorithm` (Genetic/Sgd), `IsDebugOverlayEnabled` |
| `CheatManager/DdaCheatManager.cs` | Console commands: `dda_genetics`, `dda_algorithm`, `dda_debug_overlay`, `dda_param` |
| `GeneticEngine/GeneEngineDriver.cs` | Genetic algorithm driver; patches Run_Start, CharacterBody_Start, HealthComponent_TakeDamage |
| `GeneticEngine/MonsterGeneBehaviour.cs` | Monster data: `currentGenes`, `damageDealt`, `timeAlive`, `timeEngaged`, `score` |
| `GeneticEngine/MasterGeneBehaviour.cs` | Gene template per monster type; `MutateFromChildren` — learning by score |
| `GeneticEngine/GeneTokenCalc.cs` | RecalculateStatsAPI: converts genes to stat modifiers |
| `GeneticEngine/GeneTokens.cs` | ItemDef for GeneStat (MaxHealth, MoveSpeed, AttackSpeed, AttackDamage) |
| `ArtifactResources/ConfigManager.cs` | BepInEx Config: timeLimit, deathLimit, geneFloor, geneCap, etc. |

---

## Code Patterns

1. **Server check:** DDA logic only when `NetworkServer.active`
2. **Artifact check:** `RunArtifactManager.instance.IsArtifactEnabled(ArtifactOfGenetics.artifactDef)`
3. **Algorithm check:** `DdaAlgorithmState.ActiveAlgorithm == DdaAlgorithmType.Sgd` for SGD branches
4. **Monsters:** `self.teamComponent.teamIndex == TeamIndex.Monster` and `self.inventory != null`
5. **Logging:** `GeneticsArtifactPlugin.geneticLogSource.LogInfo/LogWarning/LogError`
6. **Players:** `TeamIndex.Player`; for sensors — `CharacterBody` with `isPlayerControlled` or `CharacterMaster`

---

## DDA Module Implementation

### Sensors Module

- Collect player metrics: accuracy, damage dealt, damage taken, time in combat, deaths
- Use hooks: `HealthComponent.TakeDamage`, `CharacterBody` (Update/fixed intervals)
- Store data in a structure/class accessible to the decision module
- Reference: `MonsterGeneBehaviour.damageDealt`, `timeAlive`, `timeEngaged`, `score`

### Actuators Module

- Modify monster parameters via `GeneTokenCalc` / `RecalculateStatsAPI` or directly via `CharacterBody`
- Reference: `MonsterGeneBehaviour.AdaptToNewGenes`, `GeneTokenCalc.GetTokensToAdd`, `RecalculateStatsAPI_GetStatCoefficients`
- Parameters: MaxHealth, MoveSpeed, AttackSpeed, AttackDamage (GeneStat)

### Decision Module (SGD)

- Input: metric vector from sensors
- Output: parameter vector for actuators (multipliers per GeneStat)
- Gradient descent formula: [add from thesis]
- Loss function: [add from thesis]
- Update: by time (timeLimit) or events (deathLimit) — similar to `GeneEngineDriver.Learn()`

---

## Algorithm Math (fill in from thesis)

```
Loss function: L(θ) = ...
Gradient: ∇L(θ) = ...
Parameter update: θ_new = θ_old - η * ∇L(θ)
Learning rate η: ...
Input metrics: x = [metric1, metric2, ...]
Output parameters: θ = [MaxHealth_mult, MoveSpeed_mult, AttackSpeed_mult, AttackDamage_mult]
```

---

## Folder Structure

- **All classes of the author's algorithm (SGD/DDA)** must be in a **separate folder** in the project (e.g., `SgdEngine/`).
- Sensors, actuators, and SGD decision module — only in this folder.
- `CheatManager/` — shared infrastructure (console, overlay, state).
- `GeneticEngine/` — genetic algorithm source code (reference, do not touch).

## Genetic Algorithm Protection

- **Do not modify** source files in `GeneticEngine/`: `GeneEngineDriver.cs`, `MasterGeneBehaviour.cs`, `MonsterGeneBehaviour.cs`, `GeneTokenCalc.cs`, `GeneTokens.cs`.
- Exception: only **critically necessary** changes (e.g., fixing a bug that blocks the project, or a minimal integration point at explicit user request).
- SGD integration — via `DdaAlgorithmState.ActiveAlgorithm`, a separate driver, hooks in `GeneticsArtifactPlugin`; **not** by modifying `GeneEngineDriver` and related classes.

---

## Style and Principles

- **SOLID, KISS:** one class — one responsibility, simple interfaces
- **Naming:** prefix `Sgd` for new DDA classes; `Gene` — for genetic algorithm
- **Namespace:** `GeneticsArtifact` for main code; `GeneticsArtifact.CheatManager` for CheatManager
- **Cleanup:** remove unused code
- **Commits:** after each logical part (sensors, actuators, decision module)

---

## Debugging

- Console: `dda_genetics 1`, `dda_algorithm sgd`, `dda_debug_overlay 1`
- Logs: `GeneticsArtifactPlugin.geneticLogSource` in BepInEx/LogOutput.log
- Debug overlay: `DebugOverlayBehaviour` — extend to display sensor metrics and SGD parameters
- `#if DEBUG` — additional logging in debug builds

---

## Integration with Existing Code

- When `ActiveAlgorithm == Sgd` — use a **separate SGD driver** in its own folder, without modifying `GeneEngineDriver`.
- Entry point: `GeneticsArtifactPlugin.Awake` — initialize SGD driver when `ActiveAlgorithm == Sgd`; Run_Start, CharacterBody_Start hooks — in a separate class.
- Artifact `ArtifactOfGenetics` remains shared; enable/disable — via `dda_genetics` and `RunArtifactManager`.
