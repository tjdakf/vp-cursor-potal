# vp-cursor-portal

Windows cursor routing for NovaStar H Series / H2 video-wall layouts.

`vp-cursor-portal` is a Windows desktop app for one control PC connected to a NovaStar H Series / H2 processor. It loads H2 presets and applies a matching cursor-routing layout so operators can move the mouse through the visual wall layout instead of Windows' raw monitor order.

## Download

Latest release: [v0.1.7](https://github.com/tjdakf/vp-cursor-portal/releases/tag/v0.1.7)

| Asset | Use when |
|---|---|
| [`vp-cursor-portal-setup.exe`](https://github.com/tjdakf/vp-cursor-portal/releases/download/v0.1.7/vp-cursor-portal-setup.exe) | You want the normal Windows installer under `Program Files` |
| [`vp-cursor-portal-win-x64.zip`](https://github.com/tjdakf/vp-cursor-portal/releases/download/v0.1.7/vp-cursor-portal-win-x64.zip) | You want a portable self-contained folder |

The installer and executable are not code-signed yet. Microsoft Defender SmartScreen may show an unknown publisher warning.

## What's New In v0.1.7

This release adds field diagnostics for Windows display remapping without changing routing behavior.

- Display refresh logs now include source, target, connector, EDID, friendly name, and monitor device path details when Windows provides them.
- The app logs possible display remaps when a saved layout rectangle matches a different current `DISPLAYx` name.
- Runtime refreshes can also log when the same rectangle appears under a different `DISPLAYx` since the previous refresh.
- Existing layouts, profiles, aliases, and routing behavior are not automatically changed by these diagnostics.
- Existing `%AppData%\vp-cursor-portal\config.json` files remain compatible.

Full release notes: [docs/releases/v0.1.7.md](docs/releases/v0.1.7.md)

## Why This Exists

Windows monitor topology often does not match the layout currently shown by an H2 preset.

```text
Windows may see:
[1] [2] [3] [4] [5] [6] [7] [8]

The H2 output may show:
[1]             [6]
[2]     [4] [5] [7]
[3]             [8]
```

In that situation, the cursor should move through the visual layout, not through Windows' linear display arrangement. `vp-cursor-portal` solves this by routing cursor movement through configured portal edges.

## Core Features

| Area | Capability |
|---|---|
| H2 control | Load NovaStar H Series / H2 presets over UDP JSON |
| Profiles | Bind hotkeys to H2-only, cursor-layout-only, or combined actions |
| Layouts | Build custom cursor layouts from detected Windows displays |
| Portals | Auto-generate ratio-mapped portal edges from the layout canvas |
| Safety | Emergency unlock hotkey and button disable routing immediately |
| Diagnostics | Display detection, runtime status, logs, and validation messages |
| Display aliases | Name detected displays without changing the Windows IDs used internally |
| Packaging | Windows x64 installer and portable ZIP from GitHub Actions |

## Cursor Routing Model

A cursor layout separates Windows coordinates from the visual H2 layout:

| Concept | Meaning |
|---|---|
| Windows rectangle | The real display bounds reported by Windows |
| Visual rectangle | Where that source appears in the H2 output layout |
| Visible zone | A display/source area that should accept cursor movement |
| Hidden zone | A Windows display area that should reject cursor movement |
| Portal | A ratio-mapped edge segment connecting two zones |

The routing engine follows these rules:

1. If movement matches a configured portal, move to the mapped target edge by ratio.
2. If the cursor enters a hidden zone, revert to the last valid position.
3. If the cursor leaves all known zones, revert to the last valid position.
4. If the cursor moves from one visible zone into another without a matching portal, revert to the last valid position.
5. Otherwise, keep the current cursor position.

This keeps custom layouts independent from Windows' physical or linear monitor adjacency.

## Typical Workflow

1. Connect the control PC to the H2 processor network.
2. Add the H2 device host, port, device ID, and timeout settings.
3. Fetch or enter H2 presets.
4. Refresh detected Windows displays.
5. Add optional display aliases for easier field identification.
6. Load displays to the layout canvas.
7. Arrange the canvas to match the H2 visual output.
8. Save the layout and create profiles that bind presets, layouts, and hotkeys.
9. Test routing and emergency unlock before field operation.

## Safety

Safety behavior is intentionally conservative:

- Routing starts disabled.
- Emergency unlock hotkey: `Ctrl+Alt+Shift+Esc`.
- Emergency unlock is also available from the UI.
- Monitor topology changes disable routing.
- Invalid layouts are refused.
- H2 preset failure prevents cursor layout activation when ACK is required.
- The app releases cursor clipping on stop, emergency unlock, topology change, and exit.

## Requirements

| Use case | Requirement |
|---|---|
| Runtime OS | Windows x64 |
| Target device | NovaStar H Series / H2 reachable over UDP, usually port `6000` |
| Installed app runtime | None; release artifacts are self-contained |
| Local development | .NET 10 SDK |
| Full app build/run | Windows, because the WPF app targets `net10.0-windows` |
| Installer build | Inno Setup 6 |

Runtime data is stored per user:

```text
%AppData%\vp-cursor-portal\config.json
%AppData%\vp-cursor-portal\logs\
```

Installer or ZIP updates should not replace existing user configuration.

## Build From Source

```powershell
dotnet restore H2CursorRouter.sln
dotnet build H2CursorRouter.sln
dotnet test H2CursorRouter.sln
dotnet run --project src\H2CursorRouter.App\H2CursorRouter.App.csproj
```

On non-Windows machines, the WPF app may not build or run. Core and protocol tests can still be run separately when the required .NET SDK is installed:

```bash
dotnet test tests/H2CursorRouter.Core.Tests/H2CursorRouter.Core.Tests.csproj
dotnet test tests/H2CursorRouter.H2.Tests/H2CursorRouter.H2.Tests.csproj
```

## Publish Locally

Portable Windows build:

```powershell
.\scripts\publish-windows.ps1
```

Installer build:

```powershell
.\scripts\publish-windows.ps1 -BuildInstaller
```

The GitHub Actions workflow also builds, tests, publishes artifacts, and uploads release assets for version tags matching `v*`.

## Repository Layout

```text
src/
  H2CursorRouter.Core/     Pure routing, geometry, validation, and profile logic
  H2CursorRouter.H2/       NovaStar H2 UDP commands and response parsing
  H2CursorRouter.Windows/  Win32 cursor, monitor topology, hotkeys, and startup
  H2CursorRouter.App/      WPF UI, view models, dialogs, and orchestration

tests/
  H2CursorRouter.Core.Tests/
  H2CursorRouter.H2.Tests/
  H2CursorRouter.App.Tests/

docs/releases/
  v0.1.0.md
  v0.1.1.md
  v0.1.2.md
  v0.1.3.md
  v0.1.4.md
  v0.1.5.md
  v0.1.6.md
  v0.1.7.md
```

Development architecture notes, diagrams, test guidance, publishing details, and release checklist are kept in [docs/development.md](docs/development.md).

## License

`vp-cursor-portal` is released under the MIT License. See [LICENSE](LICENSE).
