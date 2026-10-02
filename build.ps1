# Construit dans dist\ :
#   AniSync-Setup-<version>.exe      installeur (recommandé), en anglais ou en français selon Windows
#   AniSync-Setup-<version>-en.msi   le même installeur au format Windows Installer, en anglais
#   AniSync-Setup-<version>-fr.msi   ... et en français
#   AniSync-Portable-<version>.exe   version sans installation
#   AniSync-Extension-<version>.zip  l'extension navigateur, à envoyer au Chrome Web Store et à Edge Add-ons
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
# Extension navigateur : tests de la logique des sites (Node.js)
if (Get-Command node -ErrorAction SilentlyContinue) {
    node --test (Get-ChildItem (Join-Path $root "tests\extension") -Filter *.test.js).FullName
    if ($LASTEXITCODE) { throw "Des tests de l'extension échouent : build annulé." }
} else {
    Write-Host "Node.js absent : tests de l'extension non lancés." -ForegroundColor Yellow
}

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
# Un .msi par langue (installer\Package.<culture>.wxl), rangés par WiX dans un sous-dossier par culture.
foreach ($culture in "en-US", "fr-FR") {
    $lang = $culture.Substring(0, 2)
    Copy-Item (Join-Path $msiOut "$culture\AniSync-Setup.msi") (Join-Path $dist "AniSync-Setup-$version-$lang.msi")
}
# Le Setup.exe embarque le .msi anglais ; ses propres écrans suivent la langue de Windows.
$msi = Join-Path $dist "AniSync-Setup-$version-en.msi"

Write-Host "`n[4/4] Installeur EXE" -ForegroundColor Cyan
$exeOut = Join-Path $work "bundle"
dotnet build (Join-Path $root "installer\bundle\AniSync.Bundle.wixproj") -c Release --nologo `
    "-p:ProductVersion=$version" "-p:MsiPath=$msi" -o $exeOut
if ($LASTEXITCODE) { throw "Échec du Setup.exe." }
Copy-Item (Join-Path $exeOut "AniSync-Setup.exe") (Join-Path $dist "AniSync-Setup-$version.exe")

Copy-Item $appExe (Join-Path $dist "AniSync-Portable-$version.exe")
Remove-Item $work -Recurse -Force

# Extension à envoyer au Chrome Web Store et à Edge Add-ons (manifest.json à la racine du .zip).
$extDir = Join-Path $root "browser-extension"
$extVersion = (Get-Content (Join-Path $extDir "manifest.json") -Raw -Encoding UTF8 | ConvertFrom-Json).version
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::Open((Join-Path $dist "AniSync-Extension-$extVersion.zip"), "Create")
try {
    foreach ($file in Get-ChildItem $extDir -Recurse -File) {
        $entry = $file.FullName.Substring($extDir.Length + 1).Replace('\', '/')
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, $entry, "Optimal")
    }
} finally {
    $zip.Dispose()
}

Write-Host "`nTerminé :" -ForegroundColor Green
Get-ChildItem $dist -Recurse -File | ForEach-Object {
    "  {0,-34} {1,6:N1} Mo" -f $_.FullName.Substring($dist.Length + 1), ($_.Length / 1MB)
}
