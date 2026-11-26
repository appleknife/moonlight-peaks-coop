param(
    [string]$GameDir,
    [switch]$Copy,
    [switch]$NoLaunchShortcut
)
$ErrorActionPreference = 'Stop'
$AppId = '2209900'
$Here = $PSScriptRoot
$BepInExZip = Join-Path $Here 'dist\BepInEx_win_x64_5.4.23.2.zip'
$PluginDll = Join-Path $Here 'dist\MoonlightPeaksCoop.dll'
$ExpectedHash = 'F752CE4E838F4C305B9DA1404B6745F2CFF23B8BFD494F79F0C84D0A01F59B46'

function Find-Game {
    $libs = 'C:\Program Files (x86)\Steam\steamapps\libraryfolders.vdf'
    $roots = @('C:\Program Files (x86)\Steam\steamapps\common')
    if (Test-Path $libs) {
        foreach ($m in Select-String -Path $libs -Pattern '"path"\s+"(.+?)"' ) {
            $p = $m.Matches[0].Groups[1].Value -replace '\\\\', '\'
            $roots += (Join-Path $p 'steamapps\common')
        }
    }
    foreach ($r in $roots) {
        $c = Join-Path $r 'Moonlight Peaks\Moonlight Peaks.exe'
        if (Test-Path $c) { return (Split-Path $c) }
    }
    return $null
}

if (-not $GameDir) { $GameDir = Find-Game }
if (-not $GameDir -or -not (Test-Path (Join-Path $GameDir 'Moonlight Peaks.exe'))) {
    throw 'Moonlight Peaks was not found. Pass -GameDir "path\to\Moonlight Peaks".'
}
$hash = (Get-FileHash (Join-Path $GameDir 'Moonlight Peaks.exe') -Algorithm SHA256).Hash
Write-Output "Game: $GameDir"
Write-Output "Moonlight Peaks.exe SHA-256: $hash"

if ($Copy) {
    $dest = Join-Path $env:LOCALAPPDATA 'MoonlightPeaksCoop\game'
    Write-Output "Copying the game to $dest (about 13 GB)..."
    New-Item -ItemType Directory -Force $dest | Out-Null
    robocopy $GameDir $dest /E /MT:16 /NFL /NDL /NJH /NJS /R:1 /W:1 | Out-Null
    $GameDir = $dest
}

Get-Process 'Moonlight Peaks' -ErrorAction SilentlyContinue | ForEach-Object { $_ | Stop-Process -Force }

$backup = Join-Path $GameDir 'BepInEx\coop-backup'
New-Item -ItemType Directory -Force $backup | Out-Null
foreach ($f in 'winhttp.dll', 'doorstop_config.ini', '.doorstop_version', 'steam_appid.txt') {
    $src = Join-Path $GameDir $f
    if ((Test-Path $src) -and -not (Test-Path (Join-Path $backup $f))) {
        Copy-Item $src (Join-Path $backup $f)
    }
}

$actual = (Get-FileHash $BepInExZip -Algorithm SHA256).Hash
if ($actual -ne $ExpectedHash) { throw "BepInEx zip hash mismatch: $actual" }
Expand-Archive -Path $BepInExZip -DestinationPath $GameDir -Force
New-Item -ItemType Directory -Force (Join-Path $GameDir 'BepInEx\plugins') | Out-Null
Copy-Item $PluginDll (Join-Path $GameDir 'BepInEx\plugins\MoonlightPeaksCoop.dll') -Force
Set-Content (Join-Path $GameDir 'steam_appid.txt') $AppId -Encoding ASCII

$bat = Join-Path $GameDir 'Play Moonlight Peaks Co-op.bat'
$batBody = "@echo off`r`nset SteamAppId=$AppId`r`nset SteamGameId=$AppId`r`nstart `"`" `"%~dp0Moonlight Peaks.exe`" --coop-profile=default`r`n"
Set-Content $bat $batBody -Encoding ASCII

$manifest = @{ gameDir = $GameDir; installedAt = (Get-Date).ToString('o'); copy = [bool]$Copy }
$manifest | ConvertTo-Json | Set-Content (Join-Path $GameDir 'BepInEx\coop-install.json') -Encoding UTF8

Write-Output 'Installed. Start the game with:'
Write-Output "  $bat"
Write-Output 'Host: F8 -> Host. Friend: F8 -> type your IP -> Join. Forward UDP 47110 and TCP 47120.'
