[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [switch]$Publish,
    [switch]$GraphicsTests
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
Push-Location $PSScriptRoot
try {
    $dotnetCommand = (Get-Command dotnet -ErrorAction Stop).Source
    $localSdk = Join-Path $env:LOCALAPPDATA 'MCSkin3D\dotnet\dotnet.exe'
    & $dotnetCommand --version 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0 -and (Test-Path -LiteralPath $localSdk)) {
        $dotnetCommand = $localSdk
    }
    & $dotnetCommand build MCSkinner.sln -c $Configuration -p:RestoreLockedMode=true
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    $testArgs = @('run', '--project', 'tests/MCSkin3D.SmokeTests', '-c', $Configuration, '--no-build')
    if ($GraphicsTests) { $testArgs += @('--', '--startup') }
    & $dotnetCommand @testArgs
    if ($LASTEXITCODE -ne 0) { throw "Smoke checks failed or graphics unavailable (exit $LASTEXITCODE)." }
    if ($GraphicsTests) {
        & $dotnetCommand run --project tests/MCSkin3D.IntegrationTests -c $Configuration --no-build
        if ($LASTEXITCODE -ne 0) { throw "Full editor integration checks failed (exit $LASTEXITCODE)." }
    }
    if ($Publish) {
        $releaseName = 'compact-win-x64'
        $expectedVersion = ([xml](Get-Content -LiteralPath 'MCSkin3D.csproj' -Raw)).Project.PropertyGroup.Version | Select-Object -First 1
        $archiveName = "MCSkin3D-$expectedVersion-win-x64.zip"
        $publishRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'artifacts/publish'))
        $stagingDirectory = Join-Path $publishRoot ('staging-' + [Guid]::NewGuid().ToString('N'))
        $releaseDirectory = Join-Path $publishRoot $releaseName
        foreach ($directory in @($stagingDirectory, $releaseDirectory)) {
            if (-not [IO.Path]::GetFullPath($directory).StartsWith($publishRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
                throw 'Publish directory must stay inside the workspace publish folder.'
            }
        }
        & $dotnetCommand publish MCSkin3D.csproj -c $Configuration -r win-x64 --self-contained true -o $stagingDirectory -p:RestoreLockedMode=true
        if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
        $looseBinaries = Get-ChildItem -LiteralPath $stagingDirectory -File | Where-Object { $_.Extension -in '.dll', '.pdb', '.json' }
        if ($looseBinaries) { throw 'Single-file publishing unexpectedly left runtime files beside the executable.' }
        if ((Test-Path -LiteralPath (Join-Path $stagingDirectory 'OfficialCapes')) -or
            (Test-Path -LiteralPath (Join-Path $stagingDirectory 'Assets/OfficialCapes'))) {
            throw 'Official cape previews must stay embedded; the portable file layout cannot include a cape asset folder.'
        }
        $runtimeNote = 'The .NET runtime is included; no separate installation is needed.'
        $applicationVersion = (Get-Item -LiteralPath (Join-Path $stagingDirectory 'MCSkin3D.exe')).VersionInfo.ProductVersion
        if ($applicationVersion -ne $expectedVersion) { throw "Executable version $applicationVersion does not match package version $expectedVersion." }
        [IO.File]::WriteAllText((Join-Path $stagingDirectory 'readme.txt'), "MCSkin3D $applicationVersion - Portable edition`r`n`r`nExtract the entire archive to a writable folder and run MCSkin3D.exe.`r`nKeep Languages, Models, Skins, Swatches and Overlays beside the executable.`r`nSettings are saved as settings.ini in the same folder.`r`n$runtimeNote`r`n`r`nUpgrading from 1.7.2: close the editor and back up your folder. Replace only MCSkin3D.exe, changes.txt, readme.txt and Languages/English.lang; add update-channel.txt and update-manifest.json. If English.lang is customized, keep it or merge the new keys instead.`r`nKeep settings.ini and all skins, swatches and custom assets. No files need to be deleted.`r`n")
        $edition = 'portable'
        [IO.File]::WriteAllText((Join-Path $stagingDirectory 'update-channel.txt'), $edition)
        $manifestFiles = [ordered]@{}
        foreach ($file in (Get-ChildItem -LiteralPath $stagingDirectory -Recurse -File | Sort-Object FullName)) {
            $relative = $file.FullName.Substring($stagingDirectory.Length + 1).Replace('\', '/')
            $manifestFiles[$relative] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
        $manifest = [ordered]@{ Version = $applicationVersion; Edition = $edition; Files = $manifestFiles }
        [IO.File]::WriteAllText((Join-Path $stagingDirectory 'update-manifest.json'), ($manifest | ConvertTo-Json -Depth 4))
        if (Test-Path -LiteralPath $releaseDirectory) {
            # Preserve prior releases, including any skins/settings created while testing them.
            $previousDirectory = Join-Path $publishRoot ('previous-' + [Guid]::NewGuid().ToString('N'))
            Move-Item -LiteralPath $releaseDirectory -Destination $previousDirectory
        }
        Move-Item -LiteralPath $stagingDirectory -Destination $releaseDirectory
        $temporaryArchive = Join-Path $publishRoot ('package-' + [Guid]::NewGuid().ToString('N') + '.zip')
        $compressionLevel = [IO.Compression.CompressionLevel]::Optimal
        if ([Enum]::GetNames([IO.Compression.CompressionLevel]) -contains 'SmallestSize') {
            $compressionLevel = [Enum]::Parse([IO.Compression.CompressionLevel], 'SmallestSize')
        }
        [IO.Compression.ZipFile]::CreateFromDirectory($releaseDirectory, $temporaryArchive, $compressionLevel, $false)
        Move-Item -LiteralPath $temporaryArchive -Destination (Join-Path $PSScriptRoot "artifacts/$archiveName") -Force
        $archivePath = Join-Path $PSScriptRoot "artifacts/$archiveName"
        $checksumLine = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $archiveName + "`n"
        [IO.File]::WriteAllText(($archivePath + '.sha256'), $checksumLine)
        [IO.File]::WriteAllText((Join-Path $PSScriptRoot 'artifacts/SHA256SUMS.txt'), $checksumLine)
        Write-Host "Release: $releaseDirectory (MCSkin3D.exe and adjacent assets)"
    }
}
finally { Pop-Location }
