# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

TubaWinUi3 ("图吧工具箱") is a WinUI 3 desktop app built on .NET 10 / Windows App SDK 1.8. It catalogs and launches third-party diagnostic/stress-test executables from a local `Tools/` folder, displays WMI/LibreHardwareMonitor hardware info, and provides built-in utility tools. The UI is entirely in Chinese.

## Build & Run

```bash
dotnet build                  # Debug, x64 (RuntimeIdentifier auto-detects arch)
dotnet run                    # Unpackaged mode (no MSIX registration needed)
dotnet publish -c Release -r win-x64   # Publish portable binary
```

- All commands go through `TubaWinUi3.WinUI3/TubaWinUi3.csproj` (the `.sln` contains both the main WinUI3 project and a compatibility project)
- Platforms: x86, x64, ARM64
- No test framework — verify with `dotnet build` only
- Publish: `PublishTrimmed=false`, `PublishReadyToRun=false` — trimming is never used
- The `.pri` file must be manually restored from `TubaWinUi3.WinUI3/bin/` to publish output after `dotnet publish`

## Architecture

```
App.xaml.cs          → creates MainWindow, registers BuiltinToolRegistry, auto-elevates to admin
MainWindow.xaml.cs   → TitleBar + NavigationView with Frame; populates nav from ToolCatalog categories
Pages/               → All UI pages (XAML + code-behind pairs)
Services/            → All business logic (static classes, no DI)
  BuiltinTools/      → 18 IBuiltinTool implementations
  ToolCatalog        → scans Tools/ for launchable files (.exe .bat .cmd .lnk .msc .ps1 .vbs)
  ToolMetadataService→ merges tools.json metadata + FileVersionInfo + readme.txt
  ToolIconService    → extracts .exe/.lnk icons to %LocalAppData%/TubaWinUi3/IconCache/
  HardwareInfoService→ WMI queries on Task.Run; results consumed on UI thread
  LiteMonitorService → LibreHardwareMonitor (vendor APIs/D3DKMT/WMI); no kernel driver; needs admin for FPS ETW
  BuiltinToolRegistry→ static registry of IBuiltinTool implementations
  UnifiedSearchService→ searches across external tools, built-in tools, settings, and quick actions
  AppSettings        → JSON settings at %LocalAppData%/TubaWinUi3/settings.json
  ConfigManager      → manages data directory location (AppData vs AppRoot)
Models/              → ToolItem, HardwareInfoItem, SearchResult, etc.
Metadata/tools.json  → tool descriptions/publishers/tags matched by "match" field (case-insensitive substring)
Tools/               → bundled third-party executables in Chinese-named category folders
```

## Adding a Built-in Tool

1. Create a new class in `Services/BuiltinTools/` implementing `IBuiltinTool`
2. Choose `BuiltinToolKind`: `Dialog` (popup UI), `BackgroundTask` (run silently), `ProgressTask` (progress bar), `InstantAction` (immediate)
3. Register in `BuiltinToolRegistry.RegisterDefaults()` — duplicate IDs throw
4. Use `context.CreateDialog(title)` to create themed ContentDialogs

## Key Conventions

- **Namespace**: `TubaWinUi3` / `TubaWinUi3.Pages` / `TubaWinUi3.Services` / `TubaWinUi3.Models`
- **Services are static classes** with no DI — called directly from pages
- **UI strings are in Chinese** (hardcoded in XAML/C#); no resource localization system
- **`Tools/` content is bundled** via `<Content Include="Tools\**\*">` with `CopyToOutputDirectory=PreserveNewest`
- **`Metadata/tools.json`** `"match"` field: case-insensitive substring match against tool filenames/paths
- **File naming**: PascalCase for C#; XAML + code-behind pairs
- **Commit format**: `feat:` / `fix:` / `docs:` / `refactor:`
- **Never commit**: `bin/`, `obj/`, `.pfx`, `.cer`

## Gotchas

- `Tools/` folder has Chinese directory names (处理器工具, 显卡工具, etc.); path handling must be Unicode-safe
- `ToolCatalog.FindToolsRoot()` walks up from `AppContext.BaseDirectory` to find `Tools/` — works both packaged and unpackaged
- `HardwareInfoService` runs WMI on `Task.Run` (background thread); results consumed on UI thread
- `LiteMonitorService` uses LibreHardwareMonitorLib (nvapi64/ATI ADL/D3DKMT/WMI) — installs no kernel driver; FPS overlay uses an ETW DxgKrnl trace session (`FpsService`), admin required
- App auto-elevates to admin on launch (`App.OnLaunched` checks `IsRunningAsAdmin()` and restarts with `runas` verb)
- `Package.appxmanifest` declares `runFullTrust` and `systemAIModels` capabilities
- `package.json` / `node_modules/` / `src/docs/` are for the VitePress docs site only — not part of the .NET app
- `build-setup.ps1` builds Inno Setup installer (x64 + ARM64); `build-store.ps1` builds MSIX for Store submission
- CI workflow (`.github/workflows/build-release.yml`) is manual dispatch only, publishes x64/x86/ARM64 portable zip + Inno installer
