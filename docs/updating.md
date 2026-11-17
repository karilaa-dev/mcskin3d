# GitHub release updates

Published portable and lightweight editions check `https://api.github.com/repos/karilaa-dev/mcskin3d/releases/latest` after the editor opens. Only a newer numeric stable version is offered. Network failures do not interrupt startup; **Help > Check for updates** reports them and includes a previously skipped version.

The update window shows the GitHub release body as plain text. **Update and restart** downloads and installs the matching edition automatically. **Close** leaves the editor running. **Skip this update** persists the exact version, without suppressing future versions. Downloading can be canceled. Before restart, edited skins have a **Save / Don't save / Cancel** decision; failed saves or canceled shutdown prevent installation.

## File preservation and recovery

- The updater never installs or removes files in `Skins`, `Swatches`, legacy `Data`, or `settings.ini`. External skin libraries are not traversed.
- Other existing files are replaced only if their contents still match the last installed factory manifest. Customized or conflicting files are recorded as preserved and remain protected across subsequent releases. Deleted factory assets stay deleted. Files absent from a later package are retained.
- The executable, edition marker, and installation manifest are application-owned. Each replaced file is backed up with an atomic replacement; new files cannot overwrite a file that appeared during installation.
- Settings writes are atomic, including saving the skip preference.
- Downloads and extraction occur in a unique `.mcskin3d-update-<id>` folder beside the application. A separate instance of the verified new executable waits for the old process to exit. Running editors share an installation lock; installation requires exclusive access. The parent checks that the helper has started successfully before exiting.
- If applying files fails, the helper restores the files it replaced. Recovery files remain in the staging folder's `backup` directory. Successful updates retain that backup too. A crash or power failure may require restoring the backup manually with all editor instances closed. No automatic cleanup deletes recovery backups.
- Diagnostics are appended to `%LOCALAPPDATA%\MCSkin3D\Logs\update.log`.

An initial upgrade from 1.7.1 or 1.7.2 must be downloaded manually; those builds cannot acquire new updater code until upgraded. Extract into a new folder, then copy your settings, skins, swatches and custom assets while retaining the old folder as a backup. Source builds offer the GitHub download page instead of replacing the build output.

## Publishing a compatible release

1. Update `MCSkin3D.csproj`, `Properties/AssemblyInfo.cs`, the README, and changelog to the same four-part version.
2. Run `./build.ps1 -Publish -GraphicsTests`. Packaging checks the executable's product version and produces separate portable and lightweight ZIPs, SHA-256 sidecars, `update-channel.txt`, and `update-manifest.json`.
3. Publish a stable GitHub release tagged `v<version>` with release notes and these exact assets:
   - `MCSkin3D-<version>-win-x64.zip`
   - `MCSkin3D-<version>-win-x64-light.zip`
4. Upload both archives before publishing the draft, and mark the release latest. Attach `SHA256SUMS.txt` for manual verification.

Automatic installation requires the [GitHub asset SHA-256 digest](https://docs.github.com/en/rest/releases/assets), verifies download length, and validates the archive and every manifest file. ZIP entries cannot escape staging or use links, duplicates, device names, or oversized expansion. The executable version and edition must match. Integrity relies on HTTPS and this repository's release metadata; this is not an independent publisher code signature. An asset without a digest offers manual download only.

The manifest contains `Version`, `Edition` (`portable` or `lightweight`), and a `Files` object mapping relative forward-slash paths to SHA-256 hashes. Installed manifests additionally contain `PreservedFiles` to remember user-owned paths. The manifest itself is not listed in `Files`.

## Explicit release verification

Offline smoke tests run by default. This opt-in test downloads the actual latest GitHub release, verifies skip/manual/current-version behavior, installs into a new isolated copy, checks user-data bytes, and observes the updated editor restart. It never targets an existing installation. Use a directory outside the Windows temporary folder because the editor rejects temporary-folder launches.

```powershell
./artifacts/bin/MCSkin3D.SmokeTests/Release/net10.0-windows/MCSkin3D.SmokeTests.exe --verify-release 'path/to/original-package.zip' 'C:/path/to/new-isolated-test-folder'
```

Before publishing, append `--local-package 'path/to/new-package.zip'` to exercise the real helper and restart using a local release package. The isolated test files and recovery backup are retained for inspection.
