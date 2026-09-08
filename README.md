# MCSkin3D 1.7.1

A Windows editor for painting Minecraft skins in 3D and 2D, migrated to .NET 10 and OpenTK 4.

## Build and run

Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (10.0.400 or a newer patch), then run on Windows:

```powershell
./build.ps1
./artifacts/bin/MCSkin3D/Release/net10.0-windows/MCSkin3D.exe
```

The script builds the editor, language editor and regression checks with locked NuGet dependencies, then runs the offline smoke checks. `MCSkinner.sln` contains all five projects. Build output goes to `artifacts`; the historical `bin` and supplied 1.6.0 builds are not overwritten.

```powershell
./build.ps1 -Publish
./build.ps1 -GraphicsTests
```

`-Publish` produces two editions with identical editor features and assets:

- **Portable:** `artifacts/MCSkin3D-win-x64.zip`, also ready to run in `artifacts/publish/compact-win-x64`. Includes the .NET runtime.
- **Lightweight:** `artifacts/MCSkin3D-win-x64-light.zip`, also in `artifacts/publish/lightweight-win-x64`. Uses an installed **x64 .NET 10 Desktop Runtime**. This reduces download/application-folder size by sharing the runtime, rather than removing editor features. The runtime still occupies disk space wherever it is installed.

Both editions place `Languages`, `Models`, `Swatches`, `Skins`, `Overlays`, grass texture and documentation beside `MCSkin3D.exe`, with no `Data` wrapper. Native graphics libraries stay bundled. Extract the whole archive into a writable folder before running. Use `-Package Portable` or `-Package Lightweight` with `-Publish` to produce only one edition. A source build also needs the .NET 10 Desktop Runtime.

Official installer for the lightweight edition: [.NET 10 Desktop Runtime 10.0.11 — Windows x64](https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/runtime-desktop-10.0.11-windows-x64-installer). The Desktop Runtime includes the base .NET Runtime; the SDK is only needed to build from source.

Both editions save preferences in `settings.ini` and skins in `Skins` beside the executable. To migrate a previous build that used `Data`, back up that folder and copy its contents beside the new executable. Source builds use the same adjacent-assets layout. Bundled native libraries are extracted automatically into the user's temporary .NET cache at launch. Publishing uses a fresh staging folder and preserves prior releases outside the final folder, avoiding stale DLLs and keeping test edits safe. ZIP compression is performed at build time and does not change runtime execution.

`-GraphicsTests` additionally checks rendered pixels, opens the actual splash and editor, and exercises rendering, painting, undo/redo, saving, skin import, tabs, languages and swatches using isolated sample copies. It requires an active Windows desktop and OpenGL 3.3 support; an unavailable display is reported separately from passing tests. Modern Visual Studio with .NET 10 support or the CLI can build the projects.

The desktop compatibility target is Windows 10 x64 (22H2) and Windows 11 x64, with an OpenGL 3.3 compatibility driver. The executable declares Windows 10 compatibility and runs without administrator privileges. The code audit found no Windows 11-only APIs; WinForms handles the available DPI APIs on each Windows version. Actual startup and graphics tests ran on Windows 11 build 26200, so Windows 10 execution still needs a Windows 10 test machine. Microsoft's servicing support is a separate matter: its current [.NET Windows support matrix](https://learn.microsoft.com/en-us/dotnet/core/install/windows#supported-versions) lists supported Windows 10 LTSC/Enterprise releases rather than ordinary 22H2 editions.

## Moving skins

Right-click a skin and choose **Move to...**. Select a destination in the expandable folder tree, then choose **Move here**. Parent folders remain visible above their children, and the path starts at Skins. Use the arrow keys to navigate the tree or **Up** to select the parent folder. The browser stays inside that skin's library. **New Folder** creates and selects a destination immediately; created folders remain if you cancel the move.

If the destination already has that filename, enter a different name without `.png`. Existing files are never overwritten. Moving keeps the skin selected and preserves unsaved edits and undo history; the next save uses its new location. Drag-and-drop remains available.

## Color selection and languages

The default square picker controls saturation horizontally and brightness vertically, with hue on the adjacent strip. Drag to select; arrow keys make fine adjustments, Shift makes larger adjustments, and Ctrl adjusts hue. Opacity stays synchronized with the alpha controls. Clear **Square** to use the original color wheel; the choice is saved in settings.

English is included. Additional language catalogs can be placed in the `Languages` folder; see [catalog format](setup_files/Languages/README.md).

## Dependencies and compatibility

- .NET 10; SDK 10.0.400.
- OpenTK graphics, mathematics and windowing 4.9.4; GLControl 4.0.2; GLFW native package 3.4.0.47.
- SharpZipLib 1.4.2 replaces the bundled legacy ZIP source in the build.

The existing fixed-function renderer runs in an OpenGL compatibility context. The project keeps its model, skin and encrypted settings formats. Vendored controls and utility code without maintained package replacements remain source dependencies. Legacy embedded DLLs and .NET Framework configuration files are no longer used by the modern build.

Minecraft skin imports use Mojang's profile/session services and validated HTTPS texture downloads. Update checks use this repository's GitHub releases over HTTPS. Failures are reported when checked manually. Startup errors are recorded in `%LOCALAPPDATA%\MCSkin3D\Logs\last-error.log`.

Automatic update checks begin asynchronously after the editor opens, so a slow network does not delay skin/model loading. Available updates still appear on the UI thread; closing the editor prevents a late notification.

DPI awareness is established before the splash or graphics windows are created. This prevents Windows error 5023 (`Failed to set Win32 parent window of the Control`) when the editor opens. Startup failures stop the loader and report one error.

See [the smoke-check documentation](tests/README.md) for verification coverage.
