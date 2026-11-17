# Windows smoke checks

Run from the repository root with the .NET 10 SDK on Windows:

```powershell
dotnet run --project tests/MCSkin3D.SmokeTests -c Release
```

The executable returns a nonzero exit code on failure. It exercises the real WinForms controls on an STA thread without starting the application, contacting the updater, or modifying saved user settings. It checks mouse and keyboard picker interaction, rendering, primary/secondary colors, hexadecimal and numeric inputs, swatches, square/wheel preference and synchronization across both color tabs, XML/TCN model import and PNG pixels/metadata. Offline HTTP fixtures verify the Minecraft profile/session/texture download flow, HTTPS upgrades, both standard skin sizes, and rejected names, profile IDs, texture addresses and images. No actual network requests are made. Generated files use a unique temporary directory that is removed after execution.

Renderer checks also verify exact geometry, UV orientation, index winding and ghost opacity, and enforce reuse of color buffers without allocations during repeated visibility changes.

3D skin layer checks cover Steve/Alex and legacy hats at standard and doubled resolution, the mod's corner and back-face rules, translucent voxels, UV orientation, side-wall picking, alpha/settings-driven cache updates, large index buffers, ghost shading, toolbar/View toggles, advanced setting persistence/reset and allocation-free hit testing. Graphics integration compares flat/raised framebuffer pixels and exercises painting, erasing, undo/redo, combined cape previews and both renderers. Graphics checks require an active display.

The graphics suite also checks Minecraft lighting against independently calculated RGB values on base and raised faces, live resolution/MSAA changes, GPU buffer reuse and cleanup, unchanged 2D texture views, and painting/undo with scaled multisampling. Run just these checks with `--render-quality`. Smoke checks cover quality-setting persistence, 0-15 bounds, old percentage preference handling, reset and Cancel isolation. The graphics checks also exercise all tool switches and wrapping at three window widths.

Combined-workspace checks run without an OpenGL context: chronological history across documents, redo branching and pruning, independent preview models, cape attachment, symmetric folded/spread wings, preserved UV coordinates, transformed bounds, scaled 2D picking, 3D occlusion, and wrapping native controls. Geometry illustrations and toolbar layout PNGs are retained beside the smoke executable. These illustrations verify model placement and control layout; they are not GPU framebuffer captures.

Skin move checks cover root/subfolder destinations, filename conflicts, locked and missing files, invalid names, new folders, multiple libraries, and junctions inside/outside the source library. Control-level checks select destinations in the expandable folder tree, retain visible ancestors and siblings, submit and cancel name prompts, recover from a deleted destination, and render normal, compact, and scaled dialog layouts. PNG artifacts are retained in `move-dialog-checks` beside the smoke executable. These checks do not replace live keyboard/mouse and monitor-DPI verification. The smoke executable has a 60-second watchdog and reports UI exceptions as failures rather than showing blocking error dialogs.

For live verification on an active desktop, this optional mode opens only the move dialog with disposable skins and a filename conflict in Favorites. Its temporary directory is retained for inspection:

```powershell
dotnet run --project tests/MCSkin3D.SmokeTests -c Release -- --move-dialog
```

To also verify splash-time OpenTK initialization before a form is visible, run:

```powershell
dotnet run --project tests/MCSkin3D.SmokeTests -c Release -- --startup
```

This optional check needs an active Windows desktop and an OpenGL 3.3 compatibility driver. It creates a hidden control, renders a quad using the editor's legacy OpenGL commands, and verifies GPU pixel readback. A missing primary monitor is reported as unavailable with exit code 2, never as a pass. A watchdog also terminates the process with exit code 2 if this check exceeds 20 seconds. It does not invoke application startup or replace interactive verification of the editor viewport.

# Full application integration checks

```powershell
dotnet run --project tests/MCSkin3D.IntegrationTests -c Release
```

This executable invokes the actual application's DPI initialization and `MCSkin3DAppContext`, runs the splash loader and Windows message loop, and waits for the real editor to become visible. It requires an active Windows desktop with an OpenGL compatibility driver. It copies the packaged data into a new temporary directory, disables update checks in that copy, and saves all edits and preferences there. An optional first positional argument specifies a new output directory instead. The directory is retained with `integration.log`, three GPU framebuffer PNGs, edited skins and swatches for review. No installed settings or original skin files are changed.

Coverage includes:

- Model and skin loading, real editor HWND creation, selected skin GPU texture, and clean shutdown.
- Perspective, texture and hybrid rendering with OpenGL error checks and framebuffer readback.
- Texture upload readback for straight/premultiplied alpha, 24/32-bit RGB, indexed images and negative stride; reload, disposal and allocation checks.
- All tab creation, checkbox layout on both RGB/HSV tabs, square/wheel switching with preserved RGBA, and English language menu selection.
- Pencil strokes through viewport hit testing and mouse handlers, undo/redo, exact saved PNG pixel verification, and skin switching/reloading.
- The skin context menu opens and cancels the move browser. Moving an edited skin preserves its GPU pixels, dirty state, undo/redo, selection and texture object; saving and reloading use the new filename and location.
- Eraser, eyedropper and flood fill strokes through the viewport, including undo after erasing and filling.
- Selection of every tool and options panel, camera rotation/zoom/reset, and model part visibility. Dodge/burn, darken/lighten, noise and stamp are selected but their strokes are not covered here.
- Swatch color editing through the picker, save/reload persistence, cloning, and PNG import into the actual skin tree. Import supplies the selected file path directly to the application's import method; the native file dialog is not automated.
- Deferred automatic update checks: a controlled pending response does not block editor messages, notifications return to the UI thread, and closed windows receive no late dialog.
- Combined Skin + Cape workspace: separate document pixels and saves, target and slot switches, chronological undo/redo across swapped-out files and view modes, scaled textures, cape and folded/spread elytra strokes in all views, empty slots, and preview instance reuse. The run retains combined-view GPU framebuffer captures.

Application exceptions are captured without opening error dialogs. Any exception or failed assertion returns exit code 1. A separate 90-second watchdog returns 124 for a stalled startup, modal dialog or deadlock. The test never marks unavailable graphics as passing. Minecraft web downloads remain covered by the offline smoke fixtures; online services and interactive dialogs are not covered by these checks.

Painting checks temporarily isolate the test thread's keyboard-state table and restore it afterward, so live desktop modifier keys do not change the requested tool behavior. This sends no keystrokes and does not alter keyboard input in other applications. The message-pumping update test runs afterward.

To reproduce the reported startup failure using the old DPI initialization:

```powershell
dotnet run --project tests/MCSkin3D.IntegrationTests -c Release -- --legacy-dpi
```

This diagnostic is expected to fail on the affected configuration. It reproduced Win32 error 5023, `Failed to set Win32 parent window of the Control`, through `TabControl` and the splash-to-editor transition on the AMD Radeon RX 9070 XT. The default initialization passes on the same machine. The log includes the actual display DPI and OpenGL driver information.

To measure picking and enforce its allocation budget, add `--benchmark`. Each of the three view modes is sampled at two camera positions with 20,000 queries, after warmup. The log records elapsed time, allocated bytes, hit count and a pixel checksum. Compare checksums only with the same skin, viewport size and camera settings. Timing is diagnostic rather than a pass/fail threshold.

```powershell
dotnet run --project tests/MCSkin3D.IntegrationTests -c Release -- --benchmark
```

Official cape smoke coverage decodes every embedded asset, verifies source checksums and newest-first chronology, excludes developer API tests, blocks editing and history mutation, checks that the cape dropdown contains only custom files, and renders the full gallery at normal and compact sizes. Accessory checks compare every vertex with scalar Minecraft transforms at both endpoints and halfway spread, exercise the native slider, and verify attachment pivots and compact 2D picking. Graphics integration also captures official capes in each view and pose, attempts brush/fill/stamp strokes on a reference, checks every reference pixel remains unchanged, and verifies clearing an active reference safely. Portable publishing rejects loose official-cape folders.
