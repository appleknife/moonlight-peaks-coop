param([string]$GameDir)
$ErrorActionPreference = 'Stop'
if (-not $GameDir) {
    $candidates = @(
        'C:\Program Files (x86)\Steam\steamapps\common\Moonlight Peaks',
        (Join-Path $env:LOCALAPPDATA 'MoonlightPeaksCoop\game')
    )
    foreach ($c in $candidates) {
        if (Test-Path (Join-Path $c 'BepInEx\coop-install.json')) { $GameDir = $c; break }
    }
}
if (-not $GameDir) { throw 'Pass -GameDir.' }
Get-Process 'Moonlight Peaks' -ErrorAction SilentlyContinue | ForEach-Object { $_ | Stop-Process -Force }
Remove-Item (Join-Path $GameDir 'BepInEx\plugins\MoonlightPeaksCoop.dll') -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $GameDir 'Play Moonlight Peaks Co-op.bat') -Force -ErrorAction SilentlyContinue
$backup = Join-Path $GameDir 'BepInEx\coop-backup'
foreach ($f in 'winhttp.dll', 'doorstop_config.ini', '.doorstop_version') {
    $b = Join-Path $backup $f
    if (Test-Path $b) { Copy-Item $b (Join-Path $GameDir $f) -Force }
}
Write-Output "Removed the co-op plugin from $GameDir. The BepInEx folder and coop saves were kept."
