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
        $version = ([xml](Get-Content -LiteralPath 'MCSkin3D.csproj' -Raw)).Project.PropertyGroup.Version | Select-Object -First 1
        $publishRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'artifacts/publish'))
        $stage = Join-Path $publishRoot ('staging-' + [Guid]::NewGuid().ToString('N'))
        $minimal = Join-Path $stage 'minimal'
        $full = Join-Path $stage 'full'
        & $dotnetCommand publish MCSkin3D.csproj -c $Configuration -r win-x64 --self-contained true -o $minimal -p:RestoreLockedMode=true
        if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
        $exe = Join-Path $minimal 'MCSkin3D.exe'
        if ((Get-Item -LiteralPath $exe).VersionInfo.ProductVersion -ne $version) { throw 'Executable and package versions differ.' }
        # The installer manages just these two files, regardless of which download was installed.
        $files = [ordered]@{}
        foreach ($name in @('MCSkin3D.exe', 'Languages/English.lang')) {
            $files[$name] = (Get-FileHash -LiteralPath (Join-Path $minimal $name) -Algorithm SHA256).Hash.ToLowerInvariant()
        }
        if (@(Get-ChildItem -LiteralPath $minimal -Recurse -File -Force).Count -ne $files.Count) {
            throw 'Unexpected extra files in Minimal.'
        }
        $manifest = [ordered]@{ Version = $version; Files = $files }
        [IO.File]::WriteAllText((Join-Path $minimal 'update-manifest.json'), ($manifest | ConvertTo-Json -Depth 3 -Compress))
        Copy-Item -LiteralPath $minimal -Destination $full -Recurse
        $null = New-Item -ItemType Directory -Path (Join-Path $full 'Skins')
        Copy-Item -LiteralPath 'bin_samples/Skins' -Destination (Join-Path $full 'Skins/Samples') -Recurse
        Copy-Item -LiteralPath 'bin_samples/Overlays','setup_files/Swatches','bin_samples/samples license.txt' -Destination $full -Recurse
        Copy-Item -LiteralPath 'setup_files/changes.txt','setup_files/license.txt','setup_files/thanks.txt','setup_files/privacy.txt' -Destination $full
        Copy-Item -LiteralPath 'setup_files/Languages/README.md' -Destination (Join-Path $full 'Languages')
        [IO.File]::WriteAllText((Join-Path $full 'readme.txt'), "MCSkin3D $version - Full`r`n`r`nExtract into a new writable folder and run MCSkin3D.exe. The Windows x64 executable includes the .NET runtime.`r`nFor a 1.7.2 upgrade, close the editor and copy the contents of Minimal into your existing folder. Keep a customized English.lang when prompted.`r`nSettings are created or upgraded when the editor starts. Your skins are stored in Skins or your configured library.`r`n")
        $sums = foreach ($kind in @('minimal', 'full')) {
            $from = Join-Path $stage $kind
            $releaseDirectory = Join-Path $publishRoot $kind
            # Keep any files users created while running an earlier build.
            if (Test-Path -LiteralPath $releaseDirectory) {
                $previous = Join-Path $publishRoot ('previous-' + $kind + '-' + [Guid]::NewGuid().ToString('N'))
                if ([IO.Path]::GetDirectoryName($releaseDirectory) -ne $publishRoot -or
                    [IO.Path]::GetDirectoryName($previous) -ne $publishRoot) { throw 'Invalid publish path.' }
                Move-Item -LiteralPath $releaseDirectory -Destination $previous
            }
            if ([IO.Path]::GetDirectoryName($from) -ne $stage -or [IO.Path]::GetDirectoryName($releaseDirectory) -ne $publishRoot) { throw 'Invalid staging path.' }
            Move-Item -LiteralPath $from -Destination $releaseDirectory
            $name = "MCSkin3D-$version-win-x64-$kind.zip"
            $archive = Join-Path $PSScriptRoot "artifacts/$name"
            $temporary = Join-Path $stage ($name + '.tmp')
            [IO.Compression.ZipFile]::CreateFromDirectory($releaseDirectory, $temporary, [IO.Compression.CompressionLevel]::Optimal, $false)
            Move-Item -LiteralPath $temporary -Destination $archive -Force
            (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $name
            Write-Host "Published $name"
        }
        [IO.Directory]::Delete($stage, $false)
        [IO.File]::WriteAllText((Join-Path $PSScriptRoot 'artifacts/SHA256SUMS.txt'), (($sums -join "`n") + "`n"))
    }
}
finally { Pop-Location }
