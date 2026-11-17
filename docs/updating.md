# GitHub release updates

The published portable Windows x64 edition checks `https://api.github.com/repos/karilaa-dev/mcskin3d/releases/latest` after the editor opens. Only a newer numeric stable version is offered. Network failures do not interrupt startup; **Help > Check for updates** reports them and includes a previously skipped version.

The update window shows the GitHub release body as plain text. **Update and restart** downloads and installs the portable package automatically. **Close** leaves the editor running. **Skip this update** persists the exact version, without suppressing future versions. Downloading can be canceled. Before restart, edited skins have a **Save / Don't save / Cancel** decision; failed saves or canceled shutdown prevent installation.

## Choosing a download

**Minimal** contains `MCSkin3D.exe`, `Languages/English.lang`, and `update-manifest.json`. The executable includes the Windows x64 runtime, built-in models and grass texture. On a fresh launch the editor creates settings, a local starter skin and a palette.

**Full** adds 93 sample skins, overlays, palettes, readmes, credits and license files. Both downloads run the same editor and use Minimal for future updates.

To upgrade from **1.7.2**, save your work, close MCSkin3D, and drag the **contents of Minimal** into the folder containing your existing `MCSkin3D.exe`. Allow the executable and stock English translation to be replaced. If you customized `Languages/English.lang`, keep your copy when prompted. Start the editor from that folder. It loads your saved preferences and adds missing settings automatically. Your skins, custom models, swatches and other files stay in place.

## File preservation and recovery

- The updater never installs or removes files in `Skins`, `Swatches`, legacy `Data`, or `settings.ini`. External skin libraries are not traversed.
- The installer updates the executable, its manifest, and the stock English catalog. An edited catalog is preserved across future updates.
- The executable and installation manifest are application-owned. Each replaced file is backed up with an atomic replacement; new files cannot overwrite a file that appeared during installation.
- Settings writes are atomic, including saving the skip preference.
- Downloads and extraction occur in a unique `.mcskin3d-update-<id>` folder beside the application. A separate instance of the verified new executable waits for the old process to exit. Running editors share an installation lock; installation requires exclusive access. The parent checks that the helper has started successfully before exiting.
- If applying files fails, the helper restores the files it replaced. Recovery files remain in the staging folder's `backup` directory. Successful updates retain that backup too. A crash or power failure may require restoring the backup manually with all editor instances closed. No automatic cleanup deletes recovery backups.
- Diagnostics are appended to `%LOCALAPPDATA%\MCSkin3D\Logs\update.log`.

## Publishing a release

Run `./build.ps1 -Publish -GraphicsTests` and upload `MCSkin3D-<version>-win-x64-minimal.zip`, `MCSkin3D-<version>-win-x64-full.zip`, and `SHA256SUMS.txt` to a stable release tagged `v<version>`. The archives share the same executable, translation and manifest.

The manifest records the version and SHA-256 hashes of the executable and English translation. The installer checks the [GitHub asset digest](https://docs.github.com/en/rest/releases/assets), archive size, allowed file paths, both file hashes and executable version before installing.

## Explicit release verification

Offline smoke tests run by default. This opt-in test downloads the actual latest GitHub release, verifies skip/manual/current-version behavior, installs into a new isolated copy, checks user-data bytes, and observes the updated editor restart. It never targets an existing installation.

```powershell
./artifacts/bin/MCSkin3D.SmokeTests/Release/net10.0-windows/MCSkin3D.SmokeTests.exe --verify-release 'path/to/original-package.zip' 'C:/path/to/new-isolated-test-folder'
```

Before publishing, append `--local-package 'path/to/new-package.zip'` to exercise the real helper and restart using a local release package. The isolated test files and recovery backup are retained for inspection.
