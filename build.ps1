$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet build MoreMacros/MoreMacros.csproj -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    $source = Join-Path $PSScriptRoot 'MoreMacros/bin/Release'
    $destination = Join-Path $PSScriptRoot 'artifacts/plugin'
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    $imageDestination = Join-Path $destination 'images'
    New-Item -ItemType Directory -Force -Path $imageDestination | Out-Null
    Copy-Item -LiteralPath (Join-Path $source 'images/icon.png') -Destination (Join-Path $imageDestination 'icon.png') -Force
    # Copy dependencies first; updating the entry DLL can trigger Dalamud's reload watcher.
    foreach ($name in @('MoreMacros.Core.dll', 'MoreMacros.deps.json', 'MoreMacros.json', 'MoreMacros.dll')) {
        Copy-Item -LiteralPath (Join-Path $source $name) -Destination $destination -Force
    }
    $manifest = Get-Content -LiteralPath (Join-Path $destination 'MoreMacros.json') -Raw | ConvertFrom-Json
    if ($manifest.DalamudApiLevel -ne 15) { throw 'Unexpected Dalamud API level.' }
    $version = $manifest.AssemblyVersion
    Compress-Archive -Path (Join-Path $destination '*') -DestinationPath (Join-Path $PSScriptRoot "artifacts/MoreMacros-$version.zip") -Force
    Write-Host "Ready: $(Join-Path $destination 'MoreMacros.dll')"
} finally { Pop-Location }
