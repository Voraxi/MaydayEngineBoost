# Builds both release ZIPs into .\dist:
#   MaydayFlightControls-Full.zip  - drag-and-drop package: BepInEx + the mod, laid out like the game folder
#   MaydayEngineBoost.zip          - the mod DLL only, for people who already have BepInEx
# Usage: powershell -ExecutionPolicy Bypass -File .\build-release.ps1 [-Tag v1.8.0] [-GameDir "<Mayday Airways Demo folder>"]
#   -Tag builds that git tag from a clean export, so unfinished work in this folder never ships.
#   Without -Tag it builds the working folder as it is.
param(
    [string]$Tag = "",
    [string]$GameDir = "C:\Program Files (x86)\Steam\steamapps\common\Mayday Airways Demo"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem

# Pinned BepInEx build. This is the version the mod is tested with.
$BepInExUrl = "https://builds.bepinex.dev/projects/bepinex_be/793/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.793%2Bdbc82f0.zip"
$BepInExSha256 = "5b63d7d0e92a2448ccd13b738ff95ff1cc1026c6d718555d4df8e6c824953ac4"

# Zip a folder with forward-slash entry names. Windows PowerShell's CreateFromDirectory writes
# backslashes, which some unzip tools treat as part of the file name.
function New-PortableZip([string]$Folder, [string]$Destination) {
    $base = (Resolve-Path $Folder).Path.TrimEnd('\') + '\'
    $zip = [IO.Compression.ZipFile]::Open($Destination, [IO.Compression.ZipArchiveMode]::Create)
    try {
        Get-ChildItem $Folder -Recurse -Force -File | Sort-Object FullName | ForEach-Object {
            $name = $_.FullName.Substring($base.Length).Replace('\', '/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $_.FullName, $name,
                [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    }
    finally {
        $zip.Dispose()
    }
}

$root = $PSScriptRoot
$dist = Join-Path $root "dist"
$cache = Join-Path $dist "cache"
$stage = Join-Path $dist "stage-full"
New-Item -ItemType Directory -Force $cache | Out-Null

# 1. Build the mod, from a clean export of the tag when one is given.
$source = $root
if ($Tag) {
    $source = Join-Path $dist "src-$Tag"
    if (Test-Path $source) { Remove-Item $source -Recurse -Force }
    New-Item -ItemType Directory $source | Out-Null
    $archive = Join-Path $dist "src-$Tag.zip"
    git -C $root archive --format=zip -o $archive $Tag
    if ($LASTEXITCODE -ne 0) { throw "git archive failed for $Tag." }
    [IO.Compression.ZipFile]::ExtractToDirectory($archive, $source)
    Remove-Item $archive
}
$dotnet = Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) { $dotnet = "dotnet" }
# PathMap keeps local folder paths (and the Windows user name) out of the DLL,
# so the same source always gives the same bytes.
$sourceFull = (Resolve-Path $source).Path.TrimEnd('\') + '\'
& $dotnet build (Join-Path $source "MaydayEngineBoost.csproj") -c Release "-p:MaydayGameDir=$GameDir" "-p:PathMap=$sourceFull=MaydayEngineBoost/" -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Build failed." }
$dll = Join-Path $source "bin\Release\net6.0\MaydayEngineBoost.dll"

# 2. Fetch BepInEx once and check it is exactly the pinned file.
$bepinexZip = Join-Path $cache "BepInEx-be.793.zip"
if (-not (Test-Path $bepinexZip)) {
    Invoke-WebRequest $BepInExUrl -OutFile $bepinexZip -UseBasicParsing
}
$hash = (Get-FileHash $bepinexZip -Algorithm SHA256).Hash.ToLowerInvariant()
if ($hash -ne $BepInExSha256) {
    Remove-Item $bepinexZip
    throw "BepInEx download has an unexpected SHA-256 ($hash). Deleted it; not packaging."
}

# 3. Stage the full package exactly as it should land in the game folder.
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
[IO.Compression.ZipFile]::ExtractToDirectory($bepinexZip, $stage)
Remove-Item (Join-Path $stage "changelog.txt") -ErrorAction SilentlyContinue
Copy-Item $dll (Join-Path $stage "BepInEx\plugins\") -Force
Copy-Item (Join-Path $root "package\full\*") $stage -Force

# 4. Zip both packages (Get-ChildItem -Force keeps hidden files such as .doorstop_version).
$full = Join-Path $dist "MaydayFlightControls-Full.zip"
$pluginOnly = Join-Path $dist "MaydayEngineBoost.zip"
Remove-Item $full, $pluginOnly -ErrorAction SilentlyContinue
New-PortableZip $stage $full

$pluginStage = Join-Path $dist "stage-plugin"
if (Test-Path $pluginStage) { Remove-Item $pluginStage -Recurse -Force }
New-Item -ItemType Directory $pluginStage | Out-Null
Copy-Item $dll, (Join-Path $source "MaydayEngineBoost.cs") $pluginStage
Copy-Item (Join-Path $root "package\plugin-only\*") $pluginStage
New-PortableZip $pluginStage $pluginOnly

# The repository keeps a copy of the plugin-only ZIP at its root.
Copy-Item $pluginOnly (Join-Path $root "MaydayEngineBoost.zip") -Force

Write-Host "Built:"
Get-Item $full, $pluginOnly | ForEach-Object { Write-Host ("  {0}  {1:N0} bytes" -f $_.Name, $_.Length) }
