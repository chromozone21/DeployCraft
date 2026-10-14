# Construit l'installeur : dist\DeployCraft-Setup-<version>.exe
#   .\build.ps1            (version 0.1.0)
#   .\build.ps1 -Version 0.2.0
param([string]$Version = "0.2.0")

$ErrorActionPreference = "Stop"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
Set-Location $PSScriptRoot

Remove-Item publish -Recurse -Force -ErrorAction SilentlyContinue

# Runner (.NET Framework 4.8) puis serveur autonome (.NET embarqué : rien à installer sur la VM).
dotnet build src\DeployCraft.Runner -c Release -v q
if ($LASTEXITCODE) { throw "Échec de la compilation du runner" }

dotnet publish src\DeployCraft.Server -c Release -r win-x64 --self-contained true `
    -p:Version=$Version -p:PublishDir="$PSScriptRoot\publish\" -v q
if ($LASTEXITCODE) { throw "Échec de la publication du serveur" }

# Console Windows, publiée dans le même dossier (les fichiers du runtime .NET sont partagés).
dotnet publish src\DeployCraft.Console -c Release -r win-x64 --self-contained true `
    -p:Version=$Version -p:PublishDir="$PSScriptRoot\publish\" -v q
if ($LASTEXITCODE) { throw "Échec de la publication de la console" }

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 introuvable (winget install JRSoftware.InnoSetup)" }

& $iscc /Q "/DAppVersion=$Version" installer\DeployCraft.iss
if ($LASTEXITCODE) { throw "Échec de la création de l'installeur" }

Get-Item "dist\DeployCraft-Setup-$Version.exe" | Select-Object FullName, @{n = "Mo"; e = { [math]::Round($_.Length / 1MB, 1) } }
