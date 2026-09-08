[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [switch]$Publish,
    [switch]$GraphicsTests,
    [ValidateSet('Portable', 'Lightweight', 'Both')][string]$Package = 'Both'
)
$ErrorActionPreference = 'Stop'
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
        $packageKinds = if ($Package -eq 'Both') { @('Portable', 'Lightweight') } else { @($Package) }
        foreach ($packageKind in $packageKinds) {
            $selfContained = $packageKind -eq 'Portable'
            $releaseName = if ($selfContained) { 'compact-win-x64' } else { 'lightweight-win-x64' }
            $archiveName = if ($selfContained) { 'MCSkin3D-win-x64.zip' } else { 'MCSkin3D-win-x64-light.zip' }
            $publishRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'artifacts/publish'))
            $stagingDirectory = Join-Path $publishRoot ('staging-' + [Guid]::NewGuid().ToString('N'))
            $releaseDirectory = Join-Path $publishRoot $releaseName
            foreach ($directory in @($stagingDirectory, $releaseDirectory)) {
                if (-not [IO.Path]::GetFullPath($directory).StartsWith($publishRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
                    throw 'Publish directory must stay inside the workspace publish folder.'
                }
            }
            & $dotnetCommand publish MCSkin3D.csproj -c $Configuration -r win-x64 --self-contained $selfContained.ToString().ToLowerInvariant() -o $stagingDirectory -p:RestoreLockedMode=true
            if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
            $looseBinaries = Get-ChildItem -LiteralPath $stagingDirectory -File | Where-Object { $_.Extension -in '.dll', '.pdb', '.json' }
            if ($looseBinaries) { throw 'Single-file publishing unexpectedly left runtime files beside the executable.' }
            $runtimeNote = if ($selfContained) { 'The .NET runtime is included; no separate installation is needed.' } else { 'Requires the x64 .NET 10 Desktop Runtime. Official installer: https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/runtime-desktop-10.0.11-windows-x64-installer' }
            $applicationVersion = (Get-Item -LiteralPath (Join-Path $stagingDirectory 'MCSkin3D.exe')).VersionInfo.ProductVersion
            [IO.File]::WriteAllText((Join-Path $stagingDirectory 'readme.txt'), "MCSkin3D $applicationVersion - $packageKind edition`r`n`r`nExtract the entire archive to a writable folder and run MCSkin3D.exe.`r`nKeep Languages, Models, Skins, Swatches and Overlays beside the executable.`r`nSettings are saved as settings.ini in the same folder.`r`n$runtimeNote`r`nBoth editions contain the same editor features, languages and sample assets.`r`n")
            if (Test-Path -LiteralPath $releaseDirectory) {
                # Preserve prior releases, including any skins/settings created while testing them.
                $previousDirectory = Join-Path $publishRoot ('previous-' + [Guid]::NewGuid().ToString('N'))
                Move-Item -LiteralPath $releaseDirectory -Destination $previousDirectory
            }
            Move-Item -LiteralPath $stagingDirectory -Destination $releaseDirectory
            $temporaryArchive = Join-Path $publishRoot ('package-' + [Guid]::NewGuid().ToString('N') + '.zip')
            [IO.Compression.ZipFile]::CreateFromDirectory($releaseDirectory, $temporaryArchive, [IO.Compression.CompressionLevel]::SmallestSize, $false)
            Move-Item -LiteralPath $temporaryArchive -Destination (Join-Path $PSScriptRoot "artifacts/$archiveName") -Force
            Write-Host "Release: $releaseDirectory (MCSkin3D.exe and adjacent assets)"
        }
    }
}
finally { Pop-Location }
