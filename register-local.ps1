$ErrorActionPreference = 'Stop'
if (Get-Process -Name ffxiv_dx11 -ErrorAction SilentlyContinue) {
    throw 'FFXIV is running. Add artifacts/plugin/MoreMacros.dll in Dalamud Settings > Experimental > Dev Plugin Locations, or close FFXIV before running this script.'
}
$pluginPath = Join-Path $PSScriptRoot 'artifacts/plugin/MoreMacros.dll'
if (-not (Test-Path -LiteralPath $pluginPath)) { throw 'Run build.ps1 first.' }
$pluginPath = (Resolve-Path -LiteralPath $pluginPath).Path
$configPath = Join-Path $env:APPDATA 'XIVLauncher/dalamudConfig.json'
if (-not (Test-Path -LiteralPath $configPath)) { throw 'International XIVLauncher Dalamud configuration was not found.' }
$config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
$locations = $config.DevPluginLoadLocations
if ($null -eq $locations -or $null -eq $locations.'$values') {
    throw 'Unrecognized Dalamud configuration layout. Add the path in Dalamud Settings instead.'
}
$existing = @($locations.'$values' | Where-Object { $_.Path -eq $pluginPath })
if ($existing.Count -gt 0) { Write-Host "Already registered: $pluginPath"; exit 0 }
$backup = $configPath + '.MoreMacros-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.bak'
Copy-Item -LiteralPath $configPath -Destination $backup
$locations.'$values' = @($locations.'$values') + @([pscustomobject]@{
    '$type' = 'Dalamud.Configuration.DevPluginLocationSettings, Dalamud'
    Path = $pluginPath
    IsEnabled = $true
    Nickname = 'MoreMacros prototype'
})
$json = $config | ConvertTo-Json -Depth 100
$null = $json | ConvertFrom-Json
$temp = $configPath + '.MoreMacros.tmp'
[IO.File]::WriteAllText($temp, $json, [Text.UTF8Encoding]::new($false))
[IO.File]::Replace($temp, $configPath, $null)
Write-Host "Registered: $pluginPath"
Write-Host "Configuration backup: $backup"
