# vp-cursor-portal Development Notes

This document keeps the development-oriented material that used to live in the README. The README is now focused on release, install, and product usage; this file covers architecture, runtime behavior, test expectations, publishing, and release process.

## At A Glance

| Area | Current decision |
|---|---|
| Product name | `vp-cursor-portal` |
| Target OS | Windows x64 |
| UI | WPF desktop app |
| Runtime | C# / .NET 10 |
| Target device | NovaStar H Series / H2 |
| Device protocol | UDP JSON |
| Cursor model | Polling-based Win32 cursor routing |
| Packaging | Portable ZIP and Inno Setup installer from GitHub Actions |
| Config path | `%AppData%\vp-cursor-portal\config.json` |
| Safety baseline | Routing starts disabled; emergency unlock is available in the UI and requests a global hotkey |
| App lifecycle | One instance per Windows user session, including installed and portable copies |

## Current MVP Status

This document describes the `v0.1.8` source and release procedure. See [release notes](releases/v0.1.8.md) for the change summary and validation limits.

| Status | Capability |
|---|---|
| Done | Single-instance startup gate and repeated-launch window restore |
| Done | WPF desktop app targeting `.NET 10` / `net10.0-windows` |
| Done | Separated Core, H2, Windows, App, and test projects |
| Done | H2 `W0605` preset load and `R0600` preset enum commands |
| Done | UDP timeout handling and one in-flight H2 command at a time |
| Done | ACK parsing for success, failure, malformed JSON, and unexpected responses |
| Done | Pure cursor routing engine for visible, hidden, outside, portal-only visible-zone transitions, full-edge, segmented, and different-size visual mapping cases |
| Done | Polling-based Win32 cursor routing runtime |
| Done | Emergency unlock hotkey and UI action |
| Done | Monitor topology detection and topology-change safety shutdown |
| Done | Dashboard-first WPF workflow for profiles, H2 status, routing status, emergency unlock, layout editing, diagnostics, and logs |
| Done | Layout editor with detected coordinates, scaled canvas, drag/resize, snapping, and auto portal generation |
| Done | Display Identify overlays positioned with Win32 physical pixels for negative-coordinate and mixed-DPI monitor layouts |
| Done | Display aliases with connected/not-detected history while preserving Windows display IDs for routing |
| Done | Display remap diagnostics for field monitoring without automatic layout changes |
| Done | Profile execution for H2-only, cursor-layout-only, or combined actions |
| Done | Portable ZIP and installer artifact generation through GitHub Actions |
| Deferred | X100 Pro support |
| Deferred | Generic TCP/UDP HEX console |
| Deferred | HTTP API / Stream Deck integration |
| Deferred | Low-level mouse hook mode |
| Deferred | Auto-discovery of the H2 visual layout from the processor |
| Deferred | Signed installer |

## Repository Layout

The repository name and product name are `vp-cursor-portal`. The solution and C# project names still use `H2CursorRouter.*` because they were created before the final product name was settled.

```text
H2CursorRouter.sln
README.md
LICENSE

.github/workflows/
  windows-build.yml

docs/
  development.md

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

installer/inno/
  vp-cursor-portal.iss

scripts/
  publish-windows.ps1

src/
  H2CursorRouter.Core/
  H2CursorRouter.H2/
  H2CursorRouter.Windows/
  H2CursorRouter.App/

tests/
  H2CursorRouter.Core.Tests/
  H2CursorRouter.H2.Tests/
  H2CursorRouter.App.Tests/
```

Files intentionally not tracked:

- `AGENTS.md` - local agent/developer operating notes.
- `config.sample.json` - local-only sample or field-test config, if needed.
- `.gitkeep` - local placeholder files.
- runtime `config.json`, logs, and build artifacts.

The app no longer requires a tracked `config.sample.json`. On a clean install it uses the built-in empty configuration from `SampleConfiguration.Create()`.

## Project Responsibilities

```mermaid
flowchart TB
    App["H2CursorRouter.App<br/>WPF UI, dialogs, ViewModels, orchestration"]
    Windows["H2CursorRouter.Windows<br/>Win32 cursor, monitors, hotkeys, startup"]
    H2["H2CursorRouter.H2<br/>UDP commands, ACK parsing, preset enum"]
    Core["H2CursorRouter.Core<br/>Pure domain, geometry, validation, profile planning"]
    Tests["tests/*<br/>Core, H2, App behavior coverage"]

    App --> Core
    App --> H2
    App --> Windows
    Windows --> Core
    H2 --> Core
    Tests --> Core
    Tests --> H2
    Tests --> App
```

| Project | Owns | Must avoid |
|---|---|---|
| `H2CursorRouter.Core` | Domain models, geometry, routing decisions, validation, profile planning | WPF, Win32, sockets, real hardware |
| `H2CursorRouter.H2` | NovaStar H2 UDP JSON commands and responses | UI state and cursor movement |
| `H2CursorRouter.Windows` | Win32 cursor, monitor topology, hotkeys, startup registration | Business rules that belong in Core |
| `H2CursorRouter.App` | WPF shell, composition, single-instance lifecycle, ViewModels, dialogs, user workflows | Geometry decisions that cannot be tested outside the UI |
| `tests/*` | Behavior coverage around routing, H2, mapping, XAML bindings, log policy | Real H2 devices or real cursor movement |

### `H2CursorRouter.Core`

Pure domain, geometry, profile, and validation logic. This project must not depend on WPF, Win32, sockets, or real hardware.

Important folders and types:

- `Domain/`
  - `H2DeviceConfig`
  - `H2PresetRef`
- `Geometry/`
  - `CursorLayout`
  - `CursorZone`
  - `CursorPortal`
  - `CursorRoutingEngine`
  - `RoutingDecision`
- `Profiles/`
  - `ExecutionProfile`
  - `ProfileExecutionPlanner`
- `Validation/`
  - `AppConfigurationValidator`
  - `CursorLayoutValidator`
- `Configuration/`
  - runtime config models and JSON document mapping

Rule: add or update Core tests whenever cursor routing math, validation rules, profile planning, or config contracts change.

### `H2CursorRouter.H2`

NovaStar H Series / H2 UDP JSON communication.

Important types:

- `H2CommandBuilder` builds JSON commands:
  - `W0605` load preset
  - `R0600` get preset enum
- `H2ResponseParser` parses ACK responses.
- `H2PresetEnumParser` parses preset lists.
- `H2DeviceClient` sends UDP commands and serializes command execution.
- `IH2DeviceClient` is the app/test boundary.

Current H2 assumptions:

- UDP transport.
- Default H2 port `6000`.
- `ack:"Ok"` means success.
- `ack:"Error"`, missing ACK, timeout, malformed JSON, or unexpected command means failure.
- ACK comparison is case-insensitive and trimmed.

Rule: do not apply a cursor layout after H2 failure when the profile requires ACK.

### `H2CursorRouter.Windows`

Win32 integration behind interfaces.

Important types:

- `ICursorService` / `Win32CursorService`
- `IMonitorTopologyService` / `Win32MonitorTopologyService`
- `IHotkeyService` / `Win32HotkeyService`
- `IStartupRegistrationService` / `WindowsStartupRegistrationService`
- `ICursorRoutingRuntime` / `CursorRoutingRuntime`

The runtime:

- starts disabled,
- polls cursor position,
- evaluates the active layout through the pure `CursorRoutingEngine`,
- moves the cursor with `SetCursorPos`,
- clips the cursor only for single-visible-zone layouts,
- releases clipping on stop, topology change, emergency unlock, and app exit.

Reusable cursor, monitor, hotkey, and startup-registration integration belongs in this project. The App project also contains window-specific native calls for display-identification overlays and foreground activation. Core logic stays pure and testable.

### `H2CursorRouter.App`

WPF shell, app composition, row view models, dialogs, execution orchestration, and UI-facing services.

Important files:

- `App.xaml.cs`
  - checks single-instance ownership before creating services or loading configuration,
  - creates Win32/H2 services,
  - loads user config,
  - wires `MainViewModel`,
  - starts monitor topology watching.
- `SingleInstanceService.cs`
  - owns the named mutex and named-pipe activation listener,
  - sends repeated-launch requests to the existing instance.
- `MainWindow.xaml`
  - dashboard-first WPF UI.
- `MainWindow.xaml.cs`
  - hotkey registration, tray behavior, event handlers.
- `ViewModels/MainViewModel.cs`
  - root facade/coordinator for UI bindings.
- `ViewModels/DevicePresetViewModel.cs`
  - H2 device rows, preset cache rows, preset fetch, H2 status.
- `ViewModels/LayoutEditorViewModel.cs`
  - layout, zone, portal, monitor, and canvas state.
- `ViewModels/ProfileListViewModel.cs`
  - profile add/edit/remove/filter/dashboard list.
- `ViewModels/RuntimeLogViewModel.cs`
  - runtime status, last routing event, visible log list, validation messages.
- `Services/ProfileExecutionService.cs`
  - profile execution workflow.
- `Services/ConfigurationCoordinator.cs`
  - row-to-config build, validation, and save coordination.
- `Services/ConfigurationRowMapper.cs`
  - converts runtime config to UI rows and back.
- `Services/LayoutEditingService.cs`
  - canvas move/resize/snap helpers.
- `Services/MonitorZoneMatcher.cs`
  - maps UI zones to detected monitor coordinates.
- `Assets/`
  - Windows app icon, tray icon, and icon source artwork.

`MainViewModel` intentionally remains a facade for existing WPF bindings. New behavior should usually go into a child ViewModel or service first, then be exposed through the facade only when the XAML needs it.

## Single-Instance Startup And Exit

`SingleInstanceService` acquires a named `Local\` mutex before configuration access, monitor watching, hotkey registration, or cursor-runtime creation. Its name includes the Windows user SID and session ID, but not the executable path or version. Installed and portable copies in the same user session therefore share ownership. Separate Windows sessions are outside this guard's scope.

The primary instance starts a current-user-only named-pipe listener. A duplicate launch waits up to five seconds to connect and exchange an activation request. It grants foreground permission to the primary process before sending the request, then exits without initializing the controller. If notification fails, it shows an already-running message and exits anyway.

Activation is dispatched to the WPF thread. Requests arriving before window creation are remembered. Restoring the window cancels any pending startup-to-tray hide, restores a minimized window, and preserves a maximized window's state. Windows may still restrict foreground focus; notification delivery does not itself prove that a user saw the window.

Closing the window with **X** hides it to the tray. **Exit** stops routing, releases hotkeys and monitor watching, stops the pipe listener, and finally releases the mutex. Acquisition and disposal must remain on the application thread because mutex ownership is thread-affine. An abandoned mutex is accepted after an unexpected exit.

This guard is introduced in `v0.1.8`. Exit all older copies before upgrading; an already-running older binary does not participate in the new protocol. Copies in different privilege contexts may fail to exchange activation requests and show the fallback message instead.

## Runtime Configuration

Runtime data is per-user:

```text
%AppData%\vp-cursor-portal\config.json
%AppData%\vp-cursor-portal\logs\
```

Configuration load order:

```mermaid
flowchart TD
    A["%AppData% config.json"] --> B{"Valid?"}
    B -- yes --> Z["Use user config"]
    B -- no / missing --> E["Optional config.sample.json beside exe"]
    E --> F{"Valid?"}
    F -- yes --> Z
    F -- no / missing --> G["Built-in empty SampleConfiguration"]
```

1. `%AppData%\vp-cursor-portal\config.json`
2. optional `config.sample.json` beside the executable, if a developer or tester manually placed one there
3. built-in empty configuration from `SampleConfiguration.Create()`

When the user config cannot be loaded, the app attempts to move it aside with an `.invalid-{timestamp}` suffix, reports the recovery warning, and falls back to the next source. A failed backup is reported as `backup failed`.

Clean install behavior:

- no H2 devices,
- no cursor layouts,
- no profiles,
- routing disabled.

The app writes `config.json` only when save or auto-save succeeds. ZIP replacement or installer upgrade should not overwrite existing user config.

## Profile Execution Flow

A profile can reference:

| Profile binding | Behavior |
|---|---|
| H2 preset only | Send preset command and update H2 status; success stops routing without clearing the active layout reference, failure preserves existing routing |
| Cursor layout only | Activate cursor layout and routing without H2 communication |
| H2 preset + cursor layout | Load preset first, then apply cursor layout if ACK policy allows it |

For a profile with both H2 preset and cursor layout:

```mermaid
sequenceDiagram
    participant User
    participant App
    participant H2
    participant Routing

    User->>App: Execute profile
    App->>App: Validate configuration (abort if invalid)
    App->>H2: Send W0605 preset load
    H2-->>App: ACK / timeout / error
    alt ACK required and not Ok
        App->>Routing: Preserve existing routing; do not apply requested layout
    else ACK Ok or ACK not required
        App->>App: Wait PostAckDelayMs only after successful ACK
        App->>App: Resolve requested layout and start position
        App->>Routing: Stop previous routing and clear layout
        App->>Routing: Validate and activate requested layout
        App->>Routing: Move cursor and start polling if valid
    end
```

The UI shows the active profile/layout, H2 status, routing state, last routing event, and logs.

## Cursor Routing Model

A cursor layout contains:

| Concept | Meaning |
|---|---|
| Visible zone | Monitor/source area that is visible in the active H2 layout |
| Hidden zone | Windows monitor area that should not accept cursor travel for the active layout |
| Windows rectangle | Real virtual-screen coordinates from Windows |
| Visual rectangle | Where that source appears in the H2 output layout |
| Portal edge | Ratio-based mapping from one zone edge segment to another |
| Default start position | Safe fallback cursor position when a profile does not specify one |

```mermaid
flowchart LR
    P["Previous point"] --> Engine["CursorRoutingEngine"]
    C["Current point"] --> Engine
    L["Active cursor layout"] --> Engine
    V["Last valid point"] --> Engine
    Engine --> K["Keep"]
    Engine --> M["Move to portal target"]
    Engine --> R["Revert to last valid point"]
    Engine --> X["Reject unsafe layout"]
```

The routing engine is pure. It receives the active layout, previous cursor position, current cursor position, and last valid cursor position. It returns a `RoutingDecision`:

- keep current position,
- move to mapped portal target,
- revert to last valid position,
- reject unsafe layout.

Portal mapping uses visual-ratio mapping rather than copying raw pixels. This lets a large visual source map correctly to a smaller or segmented target source.

When the cursor moves from one visible zone into another visible zone, the transition must match a configured portal. If no portal matches, the engine reverts to the last valid position instead of allowing Windows' native monitor adjacency to decide the destination.

High-risk behavior:

- hidden monitor handling,
- non-portal visible-zone transition rejection,
- virtual desktop boundary crossings,
- segmented edge portals,
- single-visible-zone clipping,
- topology-change shutdown.

Any change in these areas should include focused Core or Windows/App tests.

## Logging Policy

Logs are meant for field diagnosis, not raw cursor tracing.

Logged:

- app startup and recovery warnings,
- H2 command failures and important H2 successes,
- profile execution start and routing start/failure,
- emergency unlock,
- topology changes,
- config validation/save failures,
- layout/profile/device changes.

Not accumulated in the visible log list or log files:

- high-frequency `Portal move` diagnostics,
- high-frequency `Cursor revert` diagnostics,
- repeated identical display-detection results,
- successful auto-save noise,
- successful raw H2 ACK JSON.

High-frequency routing diagnostics still update `LastRoutingEvent` so the dashboard can show the latest movement/revert without flooding the log.

Visible UI logs are capped at 300 entries. File logs are written under AppData and files older than 30 days are deleted on startup.

## Safety Requirements

Safety is mandatory.

- Routing starts disabled.
- Emergency unlock hotkey: `Ctrl+Alt+Shift+Esc`.
- Emergency unlock button is available in the UI.
- Emergency unlock disables routing, releases cursor clipping, clears active layout, and logs the event.
- App exit releases routing and clipping.
- Monitor topology changes disable routing.
- Invalid layouts are refused.
- H2 failure prevents the requested cursor-layout activation when ACK is required; existing routing is preserved.
- Duplicate launches must exit before any controller or configuration initialization.
- A hotkey registration failure is logged; the UI emergency-unlock control remains available.

Do not remove or hide emergency controls from normal operation paths.

## Build, Test, Run

| Task | Command |
|---|---|
| Restore | `dotnet restore H2CursorRouter.sln` |
| Build | `dotnet build H2CursorRouter.sln` |
| Test | `dotnet test H2CursorRouter.sln` |
| Run WPF app | `dotnet run --project src\H2CursorRouter.App\H2CursorRouter.App.csproj` |

Windows command block:

```powershell
dotnet restore H2CursorRouter.sln
dotnet build H2CursorRouter.sln
dotnet test H2CursorRouter.sln
dotnet run --project src\H2CursorRouter.App\H2CursorRouter.App.csproj
```

With Windows reference packs available, `EnableWindowsTargeting` allows solution cross-compilation on non-Windows machines. WPF execution and the full App test suite require Windows. Validate cross-platform logic directly:

```bash
dotnet test tests/H2CursorRouter.Core.Tests/H2CursorRouter.Core.Tests.csproj
dotnet test tests/H2CursorRouter.H2.Tests/H2CursorRouter.H2.Tests.csproj
```

Do not downgrade the target framework from .NET 10 to make a local machine pass.

## Publishing

| Output | Command | Path |
|---|---|---|
| Portable app folder | `.\scripts\publish-windows.ps1` | `artifacts\vp-cursor-portal-win-x64\` |
| Installer | `.\scripts\publish-windows.ps1 -BuildInstaller` | `artifacts\installer\vp-cursor-portal-setup.exe` |

Local Windows publish:

```powershell
.\scripts\publish-windows.ps1
```

Output:

```text
artifacts\vp-cursor-portal-win-x64\
```

Run:

```text
artifacts\vp-cursor-portal-win-x64\vp-cursor-portal.exe
```

Local installer build:

```powershell
.\scripts\publish-windows.ps1 -BuildInstaller
```

Output:

```text
artifacts\installer\vp-cursor-portal-setup.exe
```

The installer:

- installs under `Program Files`,
- creates a Start Menu shortcut,
- optionally creates a desktop shortcut,
- asks during uninstall whether to delete `%AppData%\vp-cursor-portal`.

## GitHub Actions Artifacts

Workflow: `.github/workflows/windows-build.yml`

| Trigger | Result |
|---|---|
| Push to `main` or `master` | Build, test, publish ZIP and installer artifacts |
| Pull request | Build, test, publish artifacts for review |
| Manual dispatch | On-demand build |
| Version tag matching `v*` | Build, test, publish artifacts, and upload GitHub Release assets |

The workflow:

1. installs .NET 10,
2. restores,
3. builds,
4. tests,
5. publishes a self-contained Windows x64 app,
6. builds the Inno Setup installer,
7. uploads artifacts.

| Artifact | Contains |
|---|---|
| `vp-cursor-portal-win-x64` | Portable self-contained app folder |
| `vp-cursor-portal-setup` | Program Files installer |

GitHub Release assets are uploaded only for tags like `v0.1.8`.

## Release Checklist

Automated release checks:

1. Review changes and commit only the intended source, tests, docs, and version metadata. Keep field configuration, protocol reference PDFs, and build outputs out of the commit.
2. Update `Version`, `AssemblyVersion`, and `FileVersion` in the App project, the installer default version, and workflow default/fallback versions together.
3. Add `docs/releases/<tag>.md` and update README download links and the current development notes. Keep historical release notes as records of their own versions.
4. Run `git diff --check`, build, and focused tests. Record which tests actually ran and which require Windows.
5. Push the release commit and confirm its `Windows Build` workflow passes, including the full test suite and installer packaging.
6. Create and push the new tag on that tested commit, for example:

```bash
git tag v0.1.8
git push origin v0.1.8
```

7. Wait for the tag workflow to succeed. Verify that the published release contains both the installer and ZIP, is marked latest, and that README download links work.

Manual Windows/field checks must be recorded separately from automated CI results:

- Install/upgrade, Start Menu shortcut, About version, and uninstall behavior.
- Normal duplicate launch, minimized window, tray-hidden window, and repeated launch during `--tray` startup.
- Confirm only one controller remains and that existing routing/configuration is not reset by a duplicate launch.
- Exit/relaunch and restart after an unexpected process exit.
- Emergency unlock, H2 communication, and actual multi-monitor routing on the target PC.
- Preservation of existing `%AppData%\vp-cursor-portal\config.json` during upgrade.

If these manual checks have not been performed, state that in the release notes; a successful workflow does not establish field verification.

For version tags, the release body is read from `docs/releases/<tag>.md`, for example `docs/releases/v0.1.8.md`.

## Code Signing And SmartScreen

The MVP installer is not code-signed yet. Microsoft Defender SmartScreen may show an unknown publisher warning when users run the installer or executable.

Code signing requires a certificate from a trusted certificate authority. The project can support signing in the build pipeline after a certificate is available, but the certificate purchase, identity verification, and secret storage must be handled by the project owner.

Recommended path:

| Stage | Action |
|---|---|
| MVP / field test | Keep unsigned artifacts and mention the SmartScreen warning in release notes |
| Public release | Buy an OV or EV code signing certificate |
| CI integration | Store signing certificate/password as GitHub Actions secrets |
| Installer build | Sign the app executable and installer during the Windows workflow |

Code signing identifies the publisher but does not guarantee removal of SmartScreen warnings for a new build. EV certificates no longer provide an automatic SmartScreen reputation bypass. See [Microsoft SmartScreen guidance](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation).

## Development Guidelines

Use this order when changing behavior:

1. Add or update tests in Core/H2/App as close to the behavior as possible.
2. Change pure logic first when possible.
3. Keep Win32 and WPF integration behind interfaces.
4. Keep `MainViewModel` as a facade; place new responsibility in a child ViewModel or service.
5. Preserve safety behavior before visual polish.
6. Run `dotnet test H2CursorRouter.sln` on Windows or rely on GitHub Actions if local OS lacks WPF support.

Avoid:

- real H2 or real cursor movement in automated tests,
- UI-only validation for routing math,
- applying cursor layouts after failed H2 ACK when ACK is required,
- replacing existing user config during install/update,
- logging high-frequency cursor movement into files.

## Test Coverage Map

| Test project | Primary coverage |
|---|---|
| `H2CursorRouter.Core.Tests` | Cursor routing decisions, hidden/outside-zone rejection, portal selection, full-edge and segmented mapping, validation, profile planning |
| `H2CursorRouter.H2.Tests` | Command serialization, ACK parsing, malformed responses, fake UDP integration cases |
| `H2CursorRouter.App.Tests` | Row/config mapping, profile execution service, layout editing helpers, monitor-zone matching, XAML binding surface, log retention/noise policy, MainViewModel facade behavior, single-instance ownership and activation IPC |

```mermaid
flowchart LR
    CoreTests["Core tests"] --> Routing["Routing math"]
    CoreTests --> Validation["Validation"]
    H2Tests["H2 tests"] --> Protocol["UDP JSON protocol"]
    AppTests["App tests"] --> Workflow["UI-facing workflow services"]
    AppTests --> Bindings["XAML binding surface"]
    AppTests --> Lifecycle["Single-instance ownership and activation IPC"]
```

`SingleInstanceServiceTests` covers duplicate rejection, ownership release/recovery, activation requests, startup connection waiting, disconnected clients, and notification timeout. These service tests do not instantiate the WPF window or verify visible foreground focus.

## License, Notices, And About Tab

`vp-cursor-portal` is released as open-source software under the MIT License. See [LICENSE](../LICENSE).

The app includes an About tab below Advanced. It currently shows:

- product name: `vp-cursor-portal`,
- app version,
- MIT license summary,
- config/log path,
- app icon.

Before a broader public release, consider adding publisher/company name, support/contact details, and a third-party notices link or bundled notice file.

The project currently uses the .NET runtime, WPF/WinForms platform libraries, GitHub Actions, and Inno Setup for installer generation. Review their license requirements before a formal public release.
