# Construit dans dist\ :
#   AniSync-Setup-<version>.exe   installeur (recommandé)
#   AniSync-Setup-<version>.msi   le même installeur au format Windows Installer
#   Portable\AniSync.exe          version sans installation
# Usage : powershell -ExecutionPolicy Bypass -File build.ps1
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$dist = Join-Path $root "dist"
$work = Join-Path $dist "work"

$version = ([xml](Get-Content (Join-Path $root "src\AniSync\AniSync.csproj"))).Project.PropertyGroup.Version |
    Where-Object { $_ } | Select-Object -First 1
Write-Host "== AniSync $version ==" -ForegroundColor Cyan

Write-Host "`n[1/4] Tests" -ForegroundColor Cyan
dotnet test (Join-Path $root "AniSync.slnx") -c Release --nologo -v q
if ($LASTEXITCODE) { throw "Des tests échouent : build annulé." }

Write-Host "`n[2/4] Exécutable autonome" -ForegroundColor Cyan
# On vide dist\ sans supprimer le dossier (il peut être ouvert dans l'Explorateur).
if (Test-Path $dist) { Get-ChildItem $dist -Force | Remove-Item -Recurse -Force }
$publish = Join-Path $work "publish"
dotnet publish (Join-Path $root "src\AniSync\AniSync.csproj") -c Release -r win-x64 --self-contained true --nologo `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none -o $publish
if ($LASTEXITCODE) { throw "Échec de la publication." }
$appExe = Join-Path $publish "AniSync.exe"

Write-Host "`n[3/4] Installeur MSI" -ForegroundColor Cyan
$msiOut = Join-Path $work "msi"
dotnet build (Join-Path $root "installer\AniSync.Installer.wixproj") -c Release --nologo `
    "-p:ProductVersion=$version" "-p:AppExe=$appExe" -o $msiOut
if ($LASTEXITCODE) { throw "Échec du .msi." }
$msi = Join-Path $dist "AniSync-Setup-$version.msi"
Copy-Item (Get-ChildItem $msiOut -Recurse -Filter *.msi | Select-Object -First 1).FullName $msi

Write-Host "`n[4/4] Installeur EXE" -ForegroundColor Cyan
$exeOut = Join-Path $work "bundle"
dotnet build (Join-Path $root "installer\bundle\AniSync.Bundle.wixproj") -c Release --nologo `
    "-p:ProductVersion=$version" "-p:MsiPath=$msi" -o $exeOut
if ($LASTEXITCODE) { throw "Échec du Setup.exe." }
Copy-Item (Join-Path $exeOut "AniSync-Setup.exe") (Join-Path $dist "AniSync-Setup-$version.exe")

New-Item -ItemType Directory (Join-Path $dist "Portable") | Out-Null
Copy-Item $appExe (Join-Path $dist "Portable\AniSync.exe")
Remove-Item $work -Recurse -Force

Write-Host "`nTerminé :" -ForegroundColor Green
Get-ChildItem $dist -Recurse -File | ForEach-Object {
    "  {0,-34} {1,6:N1} Mo" -f $_.FullName.Substring($dist.Length + 1), ($_.Length / 1MB)
}
