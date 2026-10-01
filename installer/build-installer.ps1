# Genera el instalador MSI de StarSeaPOS.
#   .\installer\build-installer.ps1 -Version 1.0.0
# Resultado: artifacts\installer\StarSeaPOS-<versión>.msi
param(
    [string]$Version = "1.0.0",
    # CFG-06: dirección pública donde se subirá el MSI (para update.json).
    [string]$DownloadUrl = "https://github.com/jiahao0408/ProyPOS/releases/download/v$Version/StarSeaPOS-$Version.msi"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$publish = Join-Path $root "artifacts\publish"
$output = Join-Path $root "artifacts\installer"

Write-Host "1/3 Tests..."
dotnet test (Join-Path $root "StarSeaPOS.sln") -c Release
if ($LASTEXITCODE -ne 0) { throw "Hay tests que fallan: no se genera el instalador." }

Write-Host "2/3 Publicando la app (self-contained, win-x64)..."
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish (Join-Path $root "src\Pos.App\Pos.App.csproj") -c Release -r win-x64 --self-contained true `
    -p:Version=$Version -o $publish
if ($LASTEXITCODE -ne 0) { throw "La publicación ha fallado." }

Write-Host "3/3 Generando el MSI..."
dotnet build (Join-Path $PSScriptRoot "StarSeaPOS.Installer.wixproj") -c Release `
    -p:ProductVersion=$Version -p:PublishDir=$publish -o $output
if ($LASTEXITCODE -ne 0) { throw "La generación del MSI ha fallado." }

$msi = Join-Path $output "StarSeaPOS-$Version.msi"
# CFG-06: update.json para las actualizaciones automáticas. Se publica junto al MSI.
$sha = (Get-FileHash $msi -Algorithm SHA256).Hash
@{ version = $Version; url = $DownloadUrl; sha256 = $sha } | ConvertTo-Json | Set-Content (Join-Path $output "update.json") -Encoding utf8
Write-Host "Listo: $msi"
Write-Host "Sube el MSI y update.json a la publicación (release) v$Version."
