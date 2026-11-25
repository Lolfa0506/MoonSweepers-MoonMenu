# Baut das eigenständige Release-Zip.
#
#   .\tools\package.ps1 -BepInExDir <entpacktes BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.793+dbc82f0.zip>
#
# Aus dem BepInEx-Paket werden nur übernommen: winhttp.dll (Doorstop, als version.dll), .doorstop_version,
# dotnet\ und BepInEx\core\dobby.dll. Ergebnis: dist\MoonMenu-v<Version>.zip
param(
    [Parameter(Mandatory = $true)][string]$BepInExDir
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

[xml]$proj = Get-Content (Join-Path $root 'MoonSweepersMenu.csproj')
$version = ($proj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) { throw 'Version nicht im csproj gefunden.' }

foreach ($f in 'winhttp.dll', '.doorstop_version', 'dotnet\coreclr.dll', 'BepInEx\core\dobby.dll') {
    if (-not (Test-Path (Join-Path $BepInExDir $f))) { throw "Fehlt im BepInEx-Paket: $f" }
}

& dotnet build (Join-Path $root 'MoonSweepersMenu.csproj') -c Release -p:GameDir=__none__ | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'Build fehlgeschlagen.' }
$dll = Join-Path $root 'bin\Release\net6.0\MoonMenu.dll'

$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
$zipPath = Join-Path $dist "MoonMenu-v$version.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

# Zip-Pfade mit "/" (Compress-Archive in PowerShell 5.1 schreibt "\")
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$fs = [IO.File]::Open($zipPath, 'Create')
$zip = New-Object IO.Compression.ZipArchive($fs, [IO.Compression.ZipArchiveMode]::Create)
function Add-Entry([string]$src, [string]$name) {
    [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $src, $name, [IO.Compression.CompressionLevel]::Optimal)
}
try {
    Add-Entry (Join-Path $BepInExDir 'winhttp.dll') 'version.dll'
    Add-Entry (Join-Path $BepInExDir '.doorstop_version') '.doorstop_version'
    Add-Entry (Join-Path $root 'install\doorstop_config.ini') 'doorstop_config.ini'
    $dn = Join-Path $BepInExDir 'dotnet'
    Get-ChildItem $dn -Recurse -File | ForEach-Object {
        Add-Entry $_.FullName ('dotnet/' + $_.FullName.Substring($dn.Length + 1).Replace('\', '/'))
    }
    Add-Entry $dll 'MoonMenu/MoonMenu.dll'
    Add-Entry (Join-Path $BepInExDir 'BepInEx\core\dobby.dll') 'MoonMenu/dobby.dll'
    foreach ($f in 'README.md', 'LICENSE', 'CHANGELOG.md', 'THIRD-PARTY-NOTICES.md') { Add-Entry (Join-Path $root $f) "MoonMenu/$f" }
    Get-ChildItem (Join-Path $root 'licenses') -File | ForEach-Object { Add-Entry $_.FullName "MoonMenu/licenses/$($_.Name)" }
}
finally { $zip.Dispose(); $fs.Dispose() }

$hash = (Get-FileHash $zipPath -Algorithm SHA256).Hash
'{0}  {1:N1} MB  SHA-256 {2}' -f $zipPath, ((Get-Item $zipPath).Length / 1MB), $hash
