# Genera el instalador MSI de StarSeaPOS.
#   .\installer\build-installer.ps1 -Version 1.0.0
# Resultado: artifacts\installer\StarSeaPOS-<versión>.msi
param(
    [string]$Version = "1.0.0"
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

Write-Host "Listo: $(Join-Path $output "StarSeaPOS-$Version.msi")"
