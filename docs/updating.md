# GitHub release updates

The published portable Windows x64 edition checks `https://api.github.com/repos/karilaa-dev/mcskin3d/releases/latest` after the editor opens. Only a newer numeric stable version is offered. Network failures do not interrupt startup; **Help > Check for updates** reports them and includes a previously skipped version.

The update window shows the GitHub release body as plain text. **Update and restart** downloads and installs the portable package automatically. **Close** leaves the editor running. **Skip this update** persists the exact version, without suppressing future versions. Downloading can be canceled. Before restart, edited skins have a **Save / Don't save / Cancel** decision; failed saves or canceled shutdown prevent installation.

## First upgrade from 1.7.2

The 1.7.1 and 1.7.2 update checker opens the release page but cannot install this first upgrade. For the supplied `MCSkin3D-1.7.2-win-x64.zip`, the exact file changes are four replacements and two additions. No files need to be removed.

1. Save your work, close **all** MCSkin3D instances, and back up the complete existing application folder to a separate location.
2. Download `MCSkin3D-2.0.0.0-win-x64.zip` from [GitHub Releases](https://github.com/karilaa-dev/mcskin3d/releases). Extract it into a separate folder.
3. Copy only the files in this table into the corresponding locations in your existing application folder. Keep customized files as indicated.

| Action | Path in the ZIP and existing folder | Needed for |
| --- | --- | --- |
| Replace | `MCSkin3D.exe` | The updated editor and bundled .NET runtime. |
| Add | `update-channel.txt` | Automatic updates; identifies the portable package. |
| Add | `update-manifest.json` | Automatic updates; records the factory file hashes. |
| Replace if unmodified | `Languages/English.lang` | Updated English interface labels. Keep a customized catalog; embedded English supplies missing labels for new controls. |
| Replace, optional | `changes.txt` | Release notes only. |
| Replace, optional | `readme.txt` | Package instructions only. |

The required application replacement is **`MCSkin3D.exe`**. Add **both metadata files** to enable the automatic installer. The catalog and documentation replacements are recommended when those files are unmodified, but are not required to launch the new editor.

Leave `settings.ini`, `Skins`, `Swatches`, `Models`, `Overlays`, custom language files and all other existing files untouched. Do not extract the complete ZIP over your existing folder. The 175 other files in the original 1.7.2 package are byte-identical, including all bundled models, skins, swatches, overlays, textures and license files. The new package contains 181 files; 1.7.2 contained 179.

Start the replaced `MCSkin3D.exe`. Your existing skins and preferences remain in their original locations. Minecraft lighting starts at **15** when no light level has previously been saved; an existing saved value is retained. Keep the backup until you have checked the new editor.

## File preservation and recovery

- The updater never installs or removes files in `Skins`, `Swatches`, legacy `Data`, or `settings.ini`. External skin libraries are not traversed.
- Other existing files are replaced only if their contents still match the last installed factory manifest. Customized or conflicting files are recorded as preserved and remain protected across subsequent releases. Deleted factory assets stay deleted. Files absent from a later package are retained.
- The executable, edition marker, and installation manifest are application-owned. Each replaced file is backed up with an atomic replacement; new files cannot overwrite a file that appeared during installation.
- Settings writes are atomic, including saving the skip preference.
- Downloads and extraction occur in a unique `.mcskin3d-update-<id>` folder beside the application. A separate instance of the verified new executable waits for the old process to exit. Running editors share an installation lock; installation requires exclusive access. The parent checks that the helper has started successfully before exiting.
- If applying files fails, the helper restores the files it replaced. Recovery files remain in the staging folder's `backup` directory. Successful updates retain that backup too. A crash or power failure may require restoring the backup manually with all editor instances closed. No automatic cleanup deletes recovery backups.
- Diagnostics are appended to `%LOCALAPPDATA%\MCSkin3D\Logs\update.log`.

Source builds offer the GitHub download page instead of replacing the build output. For a fresh installation, extract the complete portable archive into a new writable folder. The selective upgrade table above applies specifically to the supplied 1.7.2 package.

## Publishing a compatible release

1. Update `MCSkin3D.csproj`, `Properties/AssemblyInfo.cs`, the README, and changelog to the same four-part version.
2. Run `./build.ps1 -Publish -GraphicsTests`. Packaging checks the executable's product version and produces the portable ZIP, a SHA-256 sidecar, `update-channel.txt`, and `update-manifest.json`.
3. Publish a stable GitHub release tagged `v<version>` with release notes and the exact package name `MCSkin3D-<version>-win-x64.zip`.
4. Upload the archive before publishing the draft, and mark the release latest. Attach `SHA256SUMS.txt` for manual verification. Publish only the portable edition.

Automatic installation requires the [GitHub asset SHA-256 digest](https://docs.github.com/en/rest/releases/assets), verifies download length, and validates the archive and every manifest file. ZIP entries cannot escape staging or use links, duplicates, device names, or oversized expansion. The executable version and edition must match. Integrity relies on HTTPS and this repository's release metadata; this is not an independent publisher code signature. An asset without a digest offers manual download only.

The manifest contains `Version`, `Edition` (`portable`), and a `Files` object mapping relative forward-slash paths to SHA-256 hashes. Installed manifests additionally contain `PreservedFiles` to remember user-owned paths. The manifest itself is not listed in `Files`.

## Explicit release verification

Offline smoke tests run by default. This opt-in test downloads the actual latest GitHub release, verifies skip/manual/current-version behavior, installs into a new isolated copy, checks user-data bytes, and observes the updated editor restart. It never targets an existing installation. Use a directory outside the Windows temporary folder because the editor rejects temporary-folder launches.

```powershell
./artifacts/bin/MCSkin3D.SmokeTests/Release/net10.0-windows/MCSkin3D.SmokeTests.exe --verify-release 'path/to/original-package.zip' 'C:/path/to/new-isolated-test-folder'
```

Before publishing, append `--local-package 'path/to/new-package.zip'` to exercise the real helper and restart using a local release package. The isolated test files and recovery backup are retained for inspection.
