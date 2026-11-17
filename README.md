# MCSkin3D 2.0.0.0

A Windows editor for painting Minecraft skins in 3D and 2D, migrated to .NET 10 and OpenTK 4.

Version 2.0.0.0 adds an integrated GitHub updater, raised skin layers, first-person painting, Minecraft lighting controls and faster preview rendering. See [all changes since 1.7.2](docs/releases/v2.0.0.0.md).

## Build and run

Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (10.0.404 or a newer patch), then run on Windows:

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

- **Portable:** `artifacts/MCSkin3D-2.0.0.0-win-x64.zip`, also ready to run in `artifacts/publish/compact-win-x64`. Includes the .NET runtime.
- **Lightweight:** `artifacts/MCSkin3D-2.0.0.0-win-x64-light.zip`, also in `artifacts/publish/lightweight-win-x64`. Uses an installed **x64 .NET 10 Desktop Runtime**. This reduces download/application-folder size by sharing the runtime, rather than removing editor features. The runtime still occupies disk space wherever it is installed.

Both editions place `Languages`, `Models`, `Swatches`, `Skins`, `Overlays`, grass texture and documentation beside `MCSkin3D.exe`, with no `Data` wrapper. Native graphics libraries stay bundled. Extract the whole archive into a writable folder before running. Use `-Package Portable` or `-Package Lightweight` with `-Publish` to produce only one edition. A source build also needs the .NET 10 Desktop Runtime.

Official installer for the lightweight edition: [.NET 10 Desktop Runtime 10.0.11 — Windows x64](https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/runtime-desktop-10.0.11-windows-x64-installer). The Desktop Runtime includes the base .NET Runtime; the SDK is only needed to build from source.

Both editions save preferences in `settings.ini` and skins in `Skins` beside the executable. To migrate a previous build that used `Data`, back up that folder and copy its contents beside the new executable. Source builds use the same adjacent-assets layout. Bundled native libraries are extracted automatically into the user's temporary .NET cache at launch. Publishing uses a fresh staging folder and preserves prior releases outside the final folder, avoiding stale DLLs and keeping test edits safe. ZIP compression is performed at build time and does not change runtime execution.

`-GraphicsTests` additionally checks rendered pixels, opens the actual splash and editor, and exercises rendering, painting, undo/redo, saving, skin import, tabs, languages and swatches using isolated sample copies. It requires an active Windows desktop and OpenGL 3.3 support; an unavailable display is reported separately from passing tests. Modern Visual Studio with .NET 10 support or the CLI can build the projects.

The desktop compatibility target is Windows 10 x64 (22H2) and Windows 11 x64, with an OpenGL 3.3 compatibility driver. The executable declares Windows 10 compatibility and runs without administrator privileges. The code audit found no Windows 11-only APIs; WinForms handles the available DPI APIs on each Windows version. Actual startup and graphics tests ran on Windows 11 build 26200, so Windows 10 execution still needs a Windows 10 test machine. Microsoft's servicing support is a separate matter: its current [.NET Windows support matrix](https://learn.microsoft.com/en-us/dotnet/core/install/windows#supported-versions) lists supported Windows 10 LTSC/Enterprise releases rather than ordinary 22H2 editions.

## Updates

After the editor opens, MCSkin3D checks this repository's latest stable GitHub release in the background. A newer version opens a window with its changelog, **Update and restart**, **Close**, and **Skip this update**. Installing begins only when you choose Update and restart. Skipping remembers that version; later versions can still be offered. **Help > Check for updates** also shows versions you previously skipped.

The updater downloads the package for your edition, verifies its size and SHA-256 checksum, and stages it before closing the editor. Unsaved edits go through the normal save prompt; canceling that prompt keeps the editor open. Installation preserves skins, settings, swatches and custom files. Unmodified factory files can be refreshed using the package manifest, while modified bundled assets are preserved. User files are never deleted. Replaced files are backed up, and an installation failure triggers rollback.

Automatic installation is available in the published portable and lightweight editions, extracted into a writable folder. Source builds use a manual download instead. Both editions keep the same features; the lightweight edition still requires the x64 .NET 10 Desktop Runtime.

**Upgrading from 1.7.1 or 1.7.2:** download 2.0.0.0 from [GitHub Releases](https://github.com/karilaa-dev/mcskin3d/releases) and extract it into a new writable folder. Those versions can open the release page but cannot install this first upgrade. Copy your `settings.ini`, skins, swatches and other custom assets from the old folder, keeping a backup. Future updates can be installed from the new update window.

## First-person preview

Choose **First Person** on the viewport toolbar or **View > Mode > First Person** to see the selected Steve or Alex skin's empty hand, including its sleeve. **Right hand** is the initial selection; the hand selector switches to the skin's actual left arm. Modern, legacy and higher-resolution player skins are supported, including Skin + Cape workspaces. Other models return to the regular 3D view.

The tool-options area includes a compact hand selector, and the choice is remembered between launches. Switching back to 3D restores the previous orbit camera. Painting and undo work directly on the visible hand. First-person sleeves stay flat even when **3D Skin Layers** is enabled; returning to 3D preserves that setting.

The pose follows Minecraft Java's resting empty hand, rendered at a fixed 70° independently of world FOV. Toggling **3D Skin Layers** or **Minecraft Lighting** keeps the hand in the same place. The compact Brightness group remains visible but disabled when lighting is off, preserving the viewport size. See [first-person rendering details](docs/first-person-preview.md).

## 3D skin layers

Click **3D Skin Layers** on the viewport toolbar, or enable **View > 3D Skin Layers**, to render player overlays using the [3D Skin Layers mod](https://github.com/tr7zw/3d-Skin-Layers)'s default geometry, UVs and proportions. Hats, jackets, sleeves and trouser overlays have full pixel depth and correctly joined corners. Transparent holes stay open and translucent pixels retain their alpha. Legacy skins support their hat layer, and higher-resolution skins use their actual pixels.

**Minecraft Lighting** on the viewport toolbar toggles vanilla lighting independently of 3D Skin Layers. It lights ordinary skins, flat overlays, raised layers, first-person hands and combined accessories. Turning it off previews the original texture colors and hides the lighting controls while retaining their values, except in First Person where the disabled controls remain visible to keep the hand stationary. The toggle appears in 3D, hybrid and first-person views.

**Brightness** and **Lighting angle** occupy two compact rows beside Fill/Brush options. They wrap at narrow widths and hide in 2D view or when lighting is off, retaining their values; First Person keeps its disabled lighting controls visible. Brightness uses Minecraft's **0-15 daylight levels**, defaulting to **8** (the nearest midpoint), with Overworld ambient light and game brightness fixed at 100%. Level 0 retains the game's ambient light. **Surroundings**, enabled by default, also dims the background and grass; clear it to keep the environment fully bright. The separate **Elytra** spread control appears only in combined elytra view.

**Lighting angle** rotates the light directions from **-90° to 90°**. Drag the slider or enter a degree value; **Default** restores **0°** without changing brightness. The angle and surroundings preference are saved between launches.

Advanced controls are under **Options > 3D Skin Layers settings**: pixel/head/body scales, individual layers, render distance, **50–200% render resolution**, and **Off/2x/4x/8x antialiasing**. Quality changes apply immediately to the 3D view; the texture sheet stays at native resolution. **Reset to defaults** restores layer and quality settings while preserving lighting preferences; **Apply** saves the settings. See [rendering details](docs/skin-layer-rendering.md).

The option is off by default and remembered between launches. It works in 3D and hybrid views, including Skin + Cape. Paint raised pixels directly or use the 2D sheet; painting, erasing and undo refresh the geometry. The option changes the preview only, preserving the skin's PNG and model.

## Skin + Cape workspace

Enable **Skin + Cape** from the View menu or viewport toolbar. Choose a player skin and a cape from the two library selectors, then use **Edit Skin** or **Edit Cape** to choose the texture to paint. Import new PNGs with the existing library commands first; the selectors list compatible Steve/Alex and Cape/Elytra documents based on their assigned models.

The 2D canvas places the cape directly above the skin with a small two-pixel gap, sharing pan and zoom. Labels sit above and below the pair, leaving the gap clear. A gold outline identifies the editable sheet. The 3D view shows the accessory on the player; switch between **Cape** and **Elytra**, then drag the wing slider from **Folded** to **Spread**. Every intermediate position previews instantly and keeps the same endpoints as the original pose buttons. Hybrid view shows both sheets and the combined character. The companion remains protected from painting. Use Reset Camera to fit the workspace, and rotate the player to inspect its back.

Changing a slot preserves unsaved edits and the other slot. Preview and pose switches preserve the PNG's pixels, dimensions, and saved model. **Save** writes the active texture; **Save All** writes every dirty texture separately. The selected pair and pose are remembered during the current application session; combined mode starts off on the next launch.

Undo and Redo now follow one chronological history throughout the editor. A skin stroke followed by a cape stroke is undone cape-first, then skin. Undo focuses the affected document and restores it to its slot if it was swapped out. Saving, switching views, moving, and renaming preserve history; a new stroke after Undo clears the redo branch. Deleting or resizing a document removes only that document's history. History is kept in memory for the current session.

## Moving skins

Right-click a skin and choose **Move to...**. Select a destination in the expandable folder tree, then choose **Move here**. Parent folders remain visible above their children, and the path starts at Skins. Use the arrow keys to navigate the tree or **Up** to select the parent folder. The browser stays inside that skin's library. **New Folder** creates and selects a destination immediately; created folders remain if you cancel the move.

If the destination already has that filename, enter a different name without `.png`. Existing files are never overwritten. Moving keeps the skin selected and preserves unsaved edits and undo history; the next save uses its new location. Drag-and-drop remains available.

## Official cape previews

Click the **Official Capes** button on the viewport toolbar or in the Skin + Cape workspace. The bundled gallery works offline and shows 51 verified Java and Bedrock cape designs, ordered newest to oldest. Developer API test textures are excluded. Search by name, then choose **Use cape** to preview one on your skin. The **Cape** dropdown contains only custom library files; official capes are selected exclusively through the button. The chosen official cape's name appears beside the button. Both kinds support the 2D, 3D and hybrid views, plus Cape / Elytra and the continuous wing slider.

Official references are read-only: they cannot be painted, resized, renamed, moved, deleted, saved over, or added to undo history. Your selected skin stays editable. The original texture artwork is embedded separately from the user library; swapping a reference preserves unsaved skin/custom cape edits and the camera.

There are no cape selection settings or account filters: the gallery always includes the full catalog, including when upgrading from settings that hid capes. The selected cape and wing pose remain session-only. Choose a custom cape from the dropdown to replace an official preview, or its empty selection to clear the cape slot.

The accessory attaches at Minecraft's two-pixel offset behind the torso, with the game's resting cape tilt and mirrored elytra pivots. [Rendering notes and reference sources](docs/accessory-rendering.md) explain the coordinates and pose checks.

The catalog's dates, verified texture checksums and sources are recorded in [the cape manifest](Assets/OfficialCapes/catalog.xml); artwork attribution and verification notes are in [the asset notes](Assets/OfficialCapes/README.md). The textures and catalog are embedded in the executable. The portable archive retains its existing files and folders, with no loose official cape PNGs or extra catalog folder.

## Color selection and languages

The default square picker controls saturation horizontally and brightness vertically, with hue on the adjacent strip. Drag to select; arrow keys make fine adjustments, Shift makes larger adjustments, and Ctrl adjusts hue. Opacity stays synchronized with the alpha controls. Clear **Square** to use the original color wheel; the choice is saved in settings.

English is included. Additional language catalogs can be placed in the `Languages` folder; see [catalog format](setup_files/Languages/README.md).

## Dependencies and compatibility

- .NET 10; SDK 10.0.404.
- OpenTK graphics, mathematics and windowing 4.9.4; GLControl 4.0.2; GLFW native package 3.4.0.47.
- SharpZipLib 1.4.2 replaces the bundled legacy ZIP source in the build.

The existing fixed-function renderer runs in an OpenGL compatibility context. The project keeps its model, skin and encrypted settings formats. Vendored controls and utility code without maintained package replacements remain source dependencies. Legacy embedded DLLs and .NET Framework configuration files are no longer used by the modern build.

Minecraft skin imports use Mojang's profile/session services and validated HTTPS texture downloads. Update checks use this repository's GitHub releases over HTTPS. Failures are reported when checked manually. Startup errors are recorded in `%LOCALAPPDATA%\MCSkin3D\Logs\last-error.log`.

Automatic update checks begin asynchronously after the editor opens, so a slow network does not delay skin/model loading. Available updates still appear on the UI thread; closing the editor prevents a late notification.

DPI awareness is established before the splash or graphics windows are created. This prevents Windows error 5023 (`Failed to set Win32 parent window of the Control`) when the editor opens. Startup failures stop the loader and report one error.

See [the smoke-check documentation](tests/README.md) for verification coverage.
