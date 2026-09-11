# vp-cursor-portal

Windows cursor routing for NovaStar H Series / H2 video-wall layouts.

`vp-cursor-portal` is a Windows desktop app for one control PC connected to a NovaStar H Series / H2 processor. It loads H2 presets and applies a matching cursor-routing layout so operators can move the mouse through the visual wall layout instead of Windows' raw monitor order.

## Download

Latest release: [v0.1.8](https://github.com/tjdakf/vp-cursor-portal/releases/tag/v0.1.8)

| Asset | Use when |
|---|---|
| [`vp-cursor-portal-setup.exe`](https://github.com/tjdakf/vp-cursor-portal/releases/download/v0.1.8/vp-cursor-portal-setup.exe) | You want the normal Windows installer under `Program Files` |
| [`vp-cursor-portal-win-x64.zip`](https://github.com/tjdakf/vp-cursor-portal/releases/download/v0.1.8/vp-cursor-portal-win-x64.zip) | You want a portable self-contained folder |

The installer and executable are not code-signed yet. Microsoft Defender SmartScreen may show an unknown publisher warning.

## What's New In v0.1.8

This release prevents duplicate launches from competing for hotkeys, cursor control, and configuration writes.

- Only one instance runs per Windows user session, including installed and portable copies.
- Launching the app again requests that the existing window reopen, including from the tray or a minimized state.
- Requests received during startup are retained until the window is ready.
- Existing `%AppData%\vp-cursor-portal\config.json` files remain compatible.

Full release notes: [docs/releases/v0.1.8.md](docs/releases/v0.1.8.md)

## Install And Update

For a first installation, run `vp-cursor-portal-setup.exe` to install under `Program Files`, or extract the entire portable ZIP and run `vp-cursor-portal.exe` from that folder. Neither download requires a separate .NET runtime installation.

To update an existing installation:

1. Stop routing and choose **Exit** from every running copy's system-tray menu. The window's **X** button only hides it to the tray. Older versions can still run in parallel until you exit them.
2. Back up `%AppData%\vp-cursor-portal\config.json` if it contains field settings.
3. Run the new installer, or extract the new ZIP to a fresh folder and update any shortcuts to the new executable. Keep the AppData configuration in place.
4. Open the updated app and confirm `0.1.8` in **About**, then check the saved devices, layouts, profiles, and emergency unlock before enabling routing.

Installed and portable copies use the same per-user AppData configuration. Separate ZIP folders do not create separate configuration profiles. If Windows startup is enabled, turn that option off before moving a portable copy, then enable it again from the new location.

## Window And Tray Behavior

- **X** hides the window; routing keeps its current state.
- **Open** or a double-click on the tray icon restores the window.
- Launching the executable again opens the existing instance instead of starting another controller.
- **Exit** in the tray menu stops routing and closes the application.
- If the existing instance cannot receive the open-window request, the new launch shows an already-running message and exits. Open the existing app from the tray.

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
| App lifecycle | One instance per Windows user session; tray and repeated-launch window restore |
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
- Duplicate launches exit before initializing cursor control, hotkeys, or configuration access.
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
| App execution and full test suite | Windows, because the WPF app targets `net10.0-windows` |
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

The projects enable Windows targeting, so a .NET 10 SDK with Windows reference packs can cross-compile the solution on macOS or Linux. Running the WPF app and the full App test suite still requires Windows. Core and protocol tests run separately on non-Windows machines:

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
  v0.1.8.md
```

Development architecture notes, diagrams, test guidance, publishing details, and release checklist are kept in [docs/development.md](docs/development.md).

## License

`vp-cursor-portal` is released under the MIT License. See [LICENSE](LICENSE).
