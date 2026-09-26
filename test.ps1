$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet run --project tools/NativeSmoke/NativeSmoke.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Native hotbar write regression failed.' }
    dotnet run --project MoreMacros.Tests/MoreMacros.Tests.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Storage checks failed.' }
    dotnet run --project tools/UiSmoke/UiSmoke.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'UI checks failed.' }
    dotnet run --project tools/UiSmoke/UiSmoke.csproj -c Release -- --icon-repro
    if ($LASTEXITCODE -ne 0) { throw 'Missing-icon regression failed.' }
    dotnet run --project tools/UiSmoke/UiSmoke.csproj -c Release -- --drag-repro
    if ($LASTEXITCODE -ne 0) { throw 'Hotbar drag regression failed.' }
    dotnet run --project tools/UiSmoke/UiSmoke.csproj -c Release -- --text-repro
    if ($LASTEXITCODE -ne 0) { throw 'Auto-translate editor regression failed.' }
    dotnet run --project tools/UiSmoke/UiSmoke.csproj -c Release -- --delete-repro
    if ($LASTEXITCODE -ne 0) { throw 'Right-click deletion regression failed.' }
    dotnet run --project tools/UiSmoke/UiSmoke.csproj -c Release -- --micon-repro
    if ($LASTEXITCODE -ne 0) { throw 'Macro icon regression failed.' }
} finally { Pop-Location }
