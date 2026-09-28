# Terraform Costs

A code mod for **Cities: Skylines II** that brings back the classic Cities: Skylines (1) "Terraform Tool" experience: every stroke of the Landscaping terrain tools costs money, priced by how much earth you actually move. Run out of money and the brush simply stops - no debt, no drama.

**Download / subscribe on Paradox Mods:** https://mods.paradoxplaza.com/mods/161185/Windows

## What it does

- Charges for all four terrain tools: Raise/Lower, Level, Soften and Slope.
- The price is based on the **measured volume of terrain moved** (m3), read from the terrain height data before and after each stroke - not on brush size and a timer.
- A live tooltip while you drag shows the moved volume (m3), the running cost of the current stroke and the price per 100 m3. A short red "Not enough money" warning appears when a stroke is blocked.
- If you cannot afford the next part of a stroke, the terrain is left unchanged and money never goes negative.
- Free in the map editor. Unlimited-money games are unaffected.
- Only deliberate use of the Landscaping terrain tool is charged. Terrain smoothing done automatically by the building, road or water tools is never charged.
- Nothing is saved to your save game, so the mod can be removed at any time.
- English and German localisation.

## Price model

```
cost = movedVolume[m3] x PricePerCubicMeter x toolFactor x multiplier
```

- `PricePerCubicMeter` is a fixed constant (0.0595, about 6 per 100 m3 at 1x), calibrated against real, mouse-driven strokes.
- Every cubic metre costs the same regardless of the tool, unless you lower the "other tools" factor in the options.
- The lowering refund (0% by default) applies only to the part of a stroke that lowers terrain, so a Level or Soften stroke that both raises and lowers is charged and refunded per direction.

## Options

Found in *Options > Mods > Terraform Costs*:

| Option | Range | Default |
|---|---|---|
| Enabled | on / off | on |
| Price multiplier | 0 - 10x | 1.0x |
| Price factor for Level / Soften / Slope | 0 - 1 | 1.0 (same price per m3 as Raise/Lower) |
| Refund for lowered terrain | 0 - 100 % | 0 % |
| Reset statistics | button | - |

The options page also shows how much the mod has charged in total and warns you if Hard Mode Continued is loaded.

## Compatibility

- Built for Cities: Skylines II **1.6.x**. The mod uses [Harmony](https://github.com/pardeike/Harmony) to patch `Game.Simulation.TerrainSystem.ApplyBrush` and `Game.Tools.ApplyBrushesSystem.ApplyHeight`, so a game update that changes those methods can break it until the mod is updated.
- **Hard Mode Continued** also charges for terraforming. If you run both you are charged twice, so pick one. The mod detects Hard Mode Continued and shows a warning in its options.
- Other mods that call `ApplyBrush` directly (for example a disaster mod carving a crater) are never charged, by construction: the charge is only active while the vanilla terrain tool's own pipeline is running.

## Building from source

Requirements: the [.NET SDK](https://dotnet.microsoft.com/download) (6.0 or newer, builds the `net48` target), a Windows machine with Cities: Skylines II installed. No Unity project or in-game toolchain is required. Harmony (`Lib.Harmony` 2.2.2) is restored from NuGet; no binaries are committed to this repository.

Tell the build where the game's managed assemblies are, with either an environment variable or a command-line property:

```powershell
# option A: environment variable (the official CS2 Modding Toolchain sets this one, too)
$env:CSII_MANAGEDPATH = "C:\Program Files (x86)\Steam\steamapps\common\Cities Skylines II\Cities2_Data\Managed"

# option B: MSBuild property
dotnet build TerraformCost\TerraformCost.csproj -c Release -p:ManagedPath="C:\Program Files (x86)\Steam\steamapps\common\Cities Skylines II\Cities2_Data\Managed"
```

If `Game.dll` cannot be found in that folder, the build stops with an explanatory error.

Useful switches:

| Switch | Effect |
|---|---|
| `-p:Deploy=false` | Compile only. Without it the build copies `TerraformCost.dll`, `.pdb` and `0Harmony.dll` to `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Mods\TerraformCost` (override the base folder with `CSII_USERDATAPATH` or `-p:UserDataPath=...`). |
| `-p:ReleasePublic=true` | Defines `RELEASE_PUBLIC` and strips the test-only helpers in `TestApi.cs`. Use this for builds you distribute. |

Build output paths are mapped (`PathMap`), so DLLs and PDBs you build do not contain your local folder layout.

To publish your own build, use the Paradox Mods publisher with `TerraformCost/Properties/PublishConfiguration.xml`. The content folder must contain `TerraformCost.dll` and `0Harmony.dll`.

## Repository layout

```
build/CS2Mod.props        shared build settings (game references, Harmony, deploy)
TerraformCost/            the mod (C# source, csproj, publish metadata and images)
  README.md               detailed developer notes on how the charging works
```

## License

MIT - see [LICENSE](LICENSE). Copyright (c) 2026 Erdgeist.
Harmony (`0Harmony.dll`) is a separate project by Andreas Pardeike, licensed under the MIT License.
