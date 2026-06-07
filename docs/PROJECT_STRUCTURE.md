# Project Structure

This document is the working map for file organization and later code optimization.
Keep this file updated when folders are moved or new modules are added.

## Root Layout

| Path | Purpose | Notes |
| --- | --- | --- |
| `DevelopmentTools.Addin/` | Revit add-in entry point, commands, WPF UI, Revit services, and core tiling logic. | Keep Revit-facing files here unless a move is paired with build validation. |
| `DevelopmentTools.Test/` | Console test harness that references the add-in project. | Useful for non-Revit checks, but cannot fully simulate Revit API behavior. |
| `DevelopmentTools.Updater/` | Local updater executable used by the auto-update flow. | Its package contract expects `DevelopmentTools.Addin.dll` in the update ZIP. |
| `installer/` | Active install scripts and Inno Setup script. | Paths inside `.iss` and `install.bat` are release-sensitive. |
| `release/` | Release automation script. | `release.ps1` builds, packages, writes manifest, tags, and creates GitHub releases. |
| `Archive_Unused_2026-06-05/` | Archived legacy backups, obsolete one-click packaging files, and generated release artifacts. | Kept for traceability; not part of the active build or release path. |
| `local-data/` | Local working data such as spreadsheet references. | Ignored by git; not part of runtime packaging. |
| `docs/` | Repository notes, structure maps, and refactoring records. | Non-runtime documentation only. |

## Add-in Layout

| Path | Purpose |
| --- | --- |
| `App.cs` | Revit `IExternalApplication`, Ribbon registration, updater registration, document event wiring. |
| `Commands/` | Revit `IExternalCommand` entry points. Keep command files thin where possible. |
| `Core/` | Cross-module services: auth, updater, shared parameters, extensible storage, DMU updater, data models. |
| `Algorithms/` | Geometry and tiling algorithms that should remain UI-independent. |
| `Generators/` | DirectShape/export/preview/quantity generation. |
| `Modules/FloorTools/` | Floor-to-room snapping feature implementation. |
| `Modules/SheetTools/` | Sheet, view, dimension, and room finish tools. |
| `Modules/TileElevationGenerator/` | Tile elevation generation UI and services. |
| `UI/` | Main floating control panel, feedback UI, and external event bridge. |
| `Resources/RibbonIcons/` | Active PNG icons copied to the add-in output folder for Ribbon buttons. |

## Safe Organization Rules

1. Do not move `DevelopmentTools.addin` without checking all installation scripts and local Revit add-in paths.
2. Do not move `platform_config.json`, `appsettings.json`, or `version.json` without updating `.csproj`, installer scripts, and update packaging.
3. Do not move XAML files without validating their `x:Class`, code-behind namespace, and generated `.g.cs` build output.
4. Do not rename command classes without updating `App.cs` Ribbon `PushButtonData` class names.
5. Do not change updater package filenames without updating `DevelopmentTools.Updater` and `release/release.ps1`.
6. Treat `bin/`, `obj/`, `dist/`, and one-click installer folders as generated output.

## Current Cleanup Notes

| Item | Status | Action |
| --- | --- | --- |
| Generated build output | Ignored | No source changes needed. |
| Legacy one-click package script/template | Archived | Old `RoomTileSystem.Addin` packaging flow moved under `Archive_Unused_2026-06-05/legacy-one-click/`. |
| Legacy backups and generated release packages | Archived | Moved under `Archive_Unused_2026-06-05/legacy-backups/` and `Archive_Unused_2026-06-05/generated-release-artifacts/`. |
| Source icon folder | Archived | Root `icon/` moved under `Archive_Unused_2026-06-05/source-icons/`; active copies live in `DevelopmentTools.Addin/Resources/RibbonIcons/`. |
| Local workbook `CDCBIM*.xlsx` | Moved to local data | Stored under `local-data/` unless intentionally promoted to docs/test data. |
| Existing dirty working tree | Present | Preserve current edits; refactor in small verified steps. |
| `MainWindow.xaml` image reference | Needs review | `Resources/banner_main.png` is referenced but no matching asset was found in the add-in project. |
| `System.Text.Json` 8.0.3 warnings | Needs dependency pass | Build succeeds, but package has high-severity advisories. |

## Recommended Next Refactor Order

1. Split large ViewModels by feature service boundaries, starting with `RoomFinishConfiguratorViewModel`.
2. Move repeated WPF styles into shared resource dictionaries.
3. Extract auth/update dialogs from `App.cs` and `GoogleAuthManager` into smaller services.
4. Add focused non-Revit tests for pure geometry and naming utilities.
5. Upgrade vulnerable dependencies after confirming .NET Framework 4.8 compatibility.
