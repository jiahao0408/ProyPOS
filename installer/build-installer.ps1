# Genera el instalador de StarSeaPOS.
#   .\installer\build-installer.ps1 -Version 1.0.0
# Resultado en artifacts\installer:
#   StarSeaPOS-Setup-<versión>.exe   instalador para la tienda (comprueba Windows 11, instala y abre la app)
#   StarSeaPOS-<versión>.msi         el mismo instalador en MSI (despliegue silencioso / actualizaciones)
#   update.json                      CFG-06: lo que leen las instalaciones para actualizarse
param(
    [string]$Version = "1.0.0",
    # CFG-06: dirección pública donde se subirá el MSI (para update.json).
    [string]$DownloadUrl = "https://github.com/jiahao0408/ProyPOS/releases/download/v$Version/StarSeaPOS-$Version.msi",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$publish = Join-Path $root "artifacts\publish"
$output = Join-Path $root "artifacts\installer"

if (-not (Test-Path (Join-Path $root "src\Pos.App\producer.json"))) {
    Write-Warning "No hay src\Pos.App\producer.json: el instalador llevará la plantilla y la app avisará de que faltan los datos del productor."
}

if (-not $SkipTests) {
    Write-Host "1/4 Tests..."
    dotnet test (Join-Path $root "StarSeaPOS.sln") -c Release
    if ($LASTEXITCODE -ne 0) { throw "Hay tests que fallan: no se genera el instalador." }
}

Write-Host "2/4 Publicando la app (self-contained, win-x64: lleva su propio .NET 8)..."
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish (Join-Path $root "src\Pos.App\Pos.App.csproj") -c Release -r win-x64 --self-contained true `
    -p:Version=$Version -o $publish
if ($LASTEXITCODE -ne 0) { throw "La publicación ha fallado." }

Write-Host "3/4 Generando el MSI..."
if (Test-Path $output) { Remove-Item $output -Recurse -Force }
$msiBuild = Join-Path $root "artifacts\msi"
dotnet build (Join-Path $PSScriptRoot "StarSeaPOS.Installer.wixproj") -c Release `
    -p:ProductVersion=$Version -p:PublishDir=$publish -o $msiBuild
if ($LASTEXITCODE -ne 0) { throw "La generación del MSI ha fallado." }
# WiX puede dejarlo en una subcarpeta de idioma (en-us): se busca y se copia a la carpeta final.
New-Item -ItemType Directory -Force $output | Out-Null
$msi = Join-Path $output "StarSeaPOS-$Version.msi"
Copy-Item (Get-ChildItem $msiBuild -Recurse -Filter "StarSeaPOS-$Version.msi" | Select-Object -First 1).FullName $msi
Remove-Item $msiBuild -Recurse -Force

Write-Host "4/4 Generando el instalador .exe..."
dotnet build (Join-Path $PSScriptRoot "Bundle\StarSeaPOS.Bundle.wixproj") -c Release `
    -p:ProductVersion=$Version -p:MsiPath=$msi -o $output
if ($LASTEXITCODE -ne 0) { throw "La generación del .exe ha fallado." }
$exe = Join-Path $output "StarSeaPOS-Setup-$Version.exe"

# CFG-06: update.json para las actualizaciones automáticas. Se publica junto al MSI.
$sha = (Get-FileHash $msi -Algorithm SHA256).Hash
@{ version = $Version; url = $DownloadUrl; sha256 = $sha } | ConvertTo-Json | Set-Content (Join-Path $output "update.json") -Encoding utf8

Get-ChildItem $output -Recurse -File | Where-Object { $_.Extension -in ".pdb", ".wixpdb" } | Remove-Item
Write-Host ""
Write-Host "Listo:"
Write-Host "  $exe"
Write-Host "  $msi"
Write-Host "Para las actualizaciones automáticas, sube el MSI y update.json a la publicación (release) v$Version."
