# Terraform Costs (`TerraformCost`)

**Version 1.0.0** (developer notes; see the repository README for an overview). Every terrain-tool brush stroke costs money, like Cities: Skylines (1)'s classic
"Terraform Tool" mod - priced by the **real volume of earth moved**, not by the brush's nominal
size/strength. Runs out of money → the brush simply stops (no debt). Building, road, water-tool and
other automatic terrain smoothing is never charged — only deliberate player use of the Landscaping
terrain tool.

## What it does

- Patches `Game.Simulation.TerrainSystem.ApplyBrush` (Harmony prefix) to charge money for each applied
  brush stroke, and blocks the stroke (leaves the terrain unchanged) if you can't afford it.
- Only charges brushes that came from the vanilla terrain tool's own pipeline
  (`ApplyBrushesSystem.ApplyHeight`). A second, tightly-scoped patch sets a flag around exactly that one
  call site; the charging patch only ever charges while that flag is set. Any other mod that calls
  `ApplyBrush` directly (for example a disaster mod carving a meteor crater) is **never** charged, by
  construction — no cooperation needed from that mod.
- **Measured-volume pricing (0.9.1).** At the start of a stroke, the mod snapshots the CPU terrain-height
  mirror inside the brush's footprint; while the stroke continues (and the region the brush has touched
  keeps growing) it re-measures that whole region every frame and charges
  `Σ|height_now − height_at_stroke_start| × cellArea × PricePerCubicMeter` incrementally, for the delta
  since it was last charged. This applies identically to all four modes (Raise/Lower, Level, Soften,
  Slope) — it doesn't matter how much internal strength-shaping the vanilla tool applies before its own
  `ApplyBrush` call, the mod only ever cares what the terrain actually did. All CPU height reads are
  non-blocking (never a synchronous GPU stall); a short settle window after the mouse is released lets
  the one-or-more-frame-lagged CPU mirror catch up before the stroke's final charge is settled.
- Free in the map editor and unlimited-money games are unaffected in the number you see (the internal
  ledger still moves, exactly like vanilla tools do, but the displayed 2,000,000,000 balance never
  drops).
- Raising and lowering (Shift) are priced the same; Level, Soften and Slope cost a configurable
  fraction of that (default 100%, i.e. the same price per m3) for the same measured volume. Lowering has a configurable refund
  percentage (default 0%, i.e. no refund), applied only to the part of a stroke's volume that lowered
  terrain — a stroke that both raises and lowers within the same brush (Level/Soften routinely do) is
  charged/refunded per direction, not as a whole.
- Blocking is a **prediction**: the real cost of the *next* brush application isn't knowable until the
  terrain has actually changed and the CPU height mirror has caught up, so the mod blocks the next call
  when money can't cover the most recently measured per-touch charge (the "recent charge rate"). This
  keeps money from ever going negative without needing to see the future.
- A mouse tooltip while the terrain tool is active shows, for the stroke in progress: **Moved** (m³) and
  **Terraform cost** (₡) live, plus the current mode's **price / 100 m³**, and a red "Not enough money"
  warning for about half a second after a stroke gets blocked.
- Options page: enable/disable, price multiplier (0–10x), Level/Soften/Slope price factor, lowering
  refund %, a "reset statistics" button, and a read-only warning if Hard Mode Continued (which also
  charges for terraforming) is also loaded.
- English and German localisation.
- Nothing is ever saved by this mod — removing it is always safe, and pausing costs nothing extra (the
  charging system runs every real frame regardless of pause, T-1 fix — see below — but a paused game
  still only charges for real, mouse-driven height changes; nothing accrues on its own while idle).

## How the price is calibrated

`cost = movedVolumeM3 × PricePerCubicMeter × modeFactor × multiplier` (with the lowering refund applied
per-direction, see above), measured directly from the CPU terrain-height mirror rather than derived from
brush parameters. `PricePerCubicMeter` (**0.05950**) is carried over unchanged from the 0.9.0 "Session 2
- F1" real-play recalibration: a real, mouse-driven stroke measured with a
9×9 height grid showed the true moved volume is only ~1/2.78 of what a nominal-disk-area formula assumes
(real brushes have a soft edge falloff), and 0.05950 was the constant that made a real stroke's charge
track that real grid-measured volume within 3%. Since 0.9.1's `StrokeVolume` tracker *is* that same
grid-sampling technique — generalised, and run automatically every frame instead of as a one-off manual
test — the F1 constant carried over directly rather than needing a fresh guess, and was re-verified for
0.9.1 against the same "raise a ~40 m circular plot by 2 m ≈ 100 m of Small Road" target.

## T-1/T-2 root causes fixed in 0.9.1

- **T-1 ("Not enough money" with ₡1M in the bank, Shift, paused or not).** `TerraformChargeSystem` (the
  only place that refreshes the published money balance and drains approved charges into real
  `PlayerMoney`) was registered at `SystemUpdatePhase.GameSimulation`. `Game.Simulation.SimulationSystem`
  skips that phase **entirely** while the game is paused (`selectedSpeed == 0` ⇒ the whole per-frame
  simulation-step loop, including `GameSimulation`, never runs — decompiled/Game source, not a guess).
  The terrain tool itself applies through `SystemUpdatePhase.ApplyTool`
  (`Game.Tools.ToolOutputSystem`/`ToolSystem`), which — like every `Tool*` phase — runs every real frame
  regardless of pause. So while paused, money was never refreshed/drained, leaving a stale balance that
  could easily be too low (or, symmetrically, undercharge) relative to the real city. Fix: moved
  `TerraformChargeSystem` to `SystemUpdatePhase.PreTool`, which — like `ToolUpdate`/`ApplyTool` — runs
  every real frame including while paused, and runs earlier the same frame so published money is fresh
  before the charge/block check.
- **T-2 (Level/Soften/Slope charged far less than intended, easily read as "nothing").** Not a missing
  gate — `ApplyBrushesSystem.ApplyHeight` (which the gate wraps) is the single dispatch point for *all
  four* `TerraformingType` values with `TerraformingTarget.Height`, confirmed in the decompile. The real
  cause: `Game.Tools.TerrainToolSystem.UpdateDefinitions` applies its own strength shaping
  (`lerp(0.4, 1, EaseUtils.OutSine(clamp(brushSize/5000, 0, 1)))`) for Shift/Soften before the value ever
  reaches `ApplyBrush`, on top of the brush's own falloff — the old analytic cost formula trusted the raw
  `brush.m_Strength` it saw and had no way to know about that upstream reshaping, so it under-charged
  relative to what the designer intended, independent of the T-3 falloff issue above. The 0.9.1 rewrite
  measures the real terrain change instead of reasoning about brush parameters, so it is correct for all
  four modes regardless of whatever shaping the vanilla tool applies before `ApplyBrush`.

## Files

| File | Role |
|---|---|
| `Mod.cs` | Entry point: Harmony patch, settings/localisation registration, system registration, Hard Mode Continued warning |
| `Patches/ApplyBrushesGatePatch.cs` | Sets `PipelineGate.InPipeline` tightly around `ApplyBrushesSystem.ApplyHeight` |
| `Patches/ApplyBrushPatch.cs` | The charging prefix on `TerrainSystem.ApplyBrush` - delegates to `StrokeVolume` |
| `StrokeVolume.cs` | T-3: measured-volume stroke tracker (growing region snapshot, incremental charge, predictive blocking) |
| `Budget.cs` | Per-frame reservation against the published money balance; money is only ever *written* by `TerraformChargeSystem` |
| `CostCalculator.cs` | The shared pricing constants (`PricePerCubicMeter`, `DefaultModeFactor`) |
| `PipelineGate.cs` | The gate flag itself |
| `GameMoney.cs`, `TerrainHeight.cs` | Small helpers for reading/nudging money and sampling terrain height (used by TestApi and StrokeVolume) |
| `Systems/TerraformChargeSystem.cs` | Every real frame (`SystemUpdatePhase.PreTool`, paused or not - T-1 fix): publishes available money, drains the approved charge into real `PlayerMoney`, ticks `StrokeVolume`'s settle-timeout finaliser |
| `Systems/TerraformCostTooltipSystem.cs` | The mouse tooltip (live Moved/Cost + price per 100 m³) |
| `Systems/TestStrokeSystem.cs` | Drives a brush through the real charging path for `TestApi.TestStroke` (see below) |
| `Settings.cs`, `LocaleEN.cs`, `LocaleDE.cs` | Options page and localisation |
| `Compat.cs` | Hard Mode Continued detection (by loaded-assembly name) |
| `TestApi.cs` | Static methods for `ModTestHarness`'s `invoke` command (see below) |
| `Diagnostics.cs` | Debug-only helpers (`TestApi.DebugFind/DebugCreateOne/DebugCounts/DebugRefresh`) used while tracking down why the first `TestStroke` design never reached `ApplyBrush` - not part of the shipped feature, harmless to leave in, safe to delete later |

## TestApi (for `ModTestHarness`'s `invoke` command)

- `Ping()` — mod/version/patch status, whether the charging gate is currently open, whether the city has
  `PlayerMoney`, whether Hard Mode Continued is loaded.
- `GetMoney()` / `AddMoney(int delta)` — read/nudge city money directly (test-only; bypasses charging).
- `Stats()` — total charged, blocked-stroke count, last stroke's cost, running `ApplyBrush` call count,
  and (0.9.1) the live `StrokeVolume` tracker's `strokeActive`/`strokeVolumeM3`/`strokeCostSoFar`.
- `TestStroke(x, z, size, strength, frames, type)` — drives a brush of the given size/strength/type
  ("Shift"/"Level"/"Soften"/"Slope") at world position (x,z) through the real
  `ApplyBrushesSystem.ApplyHeight → TerrainSystem.ApplyBrush` call chain for `frames` ticks, so the real
  gate+charge patches (and the real, GPU-computed terrain change `StrokeVolume` measures) are exercised
  exactly as for a player stroke. Starts the run and returns immediately; poll `LastTestResult()` for
  `{money_before, money_after, charged, apply_brush_calls, sum_abs_strength_dt, ...}`.
- `LastTestResult()` — `{"running":true}` while a `TestStroke` is in progress, else the last result.
- `HeightAt(x, z)` — forces a synchronous heightmap readback and samples world height (calibration/testing
  only; `StrokeVolume`'s own per-frame reads are always non-blocking, see `TerrainHeight.cs`).
- `ActivateTerrainTool(mode, size, strength)` — activates the real vanilla `Game.Tools.TerrainToolSystem`
  with the given `TerraformingType` ("Shift"/"Level"/"Soften"/"Slope") and brush size/strength, exactly the
  toolbar's own activation path (`TerrainToolSystem.SetPrefab` + `brushSize`/`brushStrength` +
  `ToolSystem.activeTool`) — used to drive a real, mouse-controlled stroke for calibration/verification
  rather than the synthetic `TestStroke` chain.

## Known open issues

- `TestStroke` bypasses the vanilla `CreationDefinition`/`GenerateBrushesSystem` entity chain and instead
  calls `ApplyBrushesSystem.ApplyHeight` directly via reflection (Harmony still patches it normally, so
  the gate+charge path is fully exercised) (see the comment in `TestApi.cs`).
  This doesn't affect real players (who go through the real vanilla pipeline, which was never touched),
  only the synthetic test driver.
- `StrokeVolume`'s tracked region is capped at 20,000 sampling cells per stroke (adaptive cell size, see
  the class doc comment) - an extremely long single drag with a huge brush could stop growing the region
  before covering everywhere it went, undercharging the overflow area. Not expected to matter for normal
  play (brush size ≤ 1000 in the tool UI); untested at the extreme end.
- The predictive block (using the most recently measured per-touch increment as the "recent charge rate")
  can occasionally let one more frame through than strictly optimal, or block one frame earlier than
  strictly necessary, right at the money boundary - it is a prediction by design (T-3), not an exact
  pre-computation, and money is still guaranteed to never go negative (the retroactive charge for
  already-happened terrain change only ever reserves up to what's available, see `Budget.ReserveUpTo`).
