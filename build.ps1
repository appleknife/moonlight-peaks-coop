param(
    [string]$GameDir = (Join-Path $PSScriptRoot 'game'),
    [string]$Csc = (Join-Path $PSScriptRoot 'tools\roslyn\csc.exe')
)
$ErrorActionPreference = 'Stop'
$src = Join-Path $PSScriptRoot 'src'
$out = Join-Path $PSScriptRoot 'out'
New-Item -ItemType Directory -Force $out | Out-Null
$managed = Join-Path $GameDir 'Moonlight Peaks_Data\Managed'
$core = Join-Path $GameDir 'BepInEx\core'
if (-not (Test-Path $managed)) { throw "No Managed folder under '$GameDir'. Pass -GameDir pointing at a game copy." }
if (-not (Test-Path $core)) { throw "No BepInEx\core under '$GameDir'. Run install.ps1 -Copy first, or install BepInEx there." }
if (-not (Test-Path $Csc)) { throw "Compiler not found at '$Csc'. Pass -Csc or restore tools\roslyn." }
$netstd = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\netstandard.dll'

$refs = @(
    (Join-Path $core 'BepInEx.dll'),
    (Join-Path $core '0Harmony.dll'),
    (Join-Path $managed 'UnityEngine.dll'),
    (Join-Path $managed 'UnityEngine.CoreModule.dll'),
    (Join-Path $managed 'UnityEngine.AnimationModule.dll'),
    (Join-Path $managed 'UnityEngine.PhysicsModule.dll'),
    (Join-Path $managed 'UnityEngine.IMGUIModule.dll'),
    (Join-Path $managed 'UnityEngine.InputLegacyModule.dll'),
    (Join-Path $managed 'UnityEngine.UI.dll'),
    (Join-Path $managed 'Unity.TextMeshPro.dll'),
    (Join-Path $managed 'Unity.Addressables.dll'),
    (Join-Path $managed 'Rewired_Core.dll'),
    (Join-Path $managed 'Rewired.dll'),
    (Join-Path $managed 'Vampire.Runtime.dll'),
    (Join-Path $managed 'littlechickengamecompany.chicken-utilities.runtime.dll'),
    (Join-Path $managed 'littlechickengamecompany.chicken-ui.runtime.dll')
)
if (Test-Path $netstd) { $refs += $netstd }
$refArgs = $refs | ForEach-Object { '/r:' + $_ }
$sources = Get-ChildItem $src -Filter *.cs | ForEach-Object { $_.FullName }
& $Csc /nologo /target:library /optimize+ /debug+ "/out:$(Join-Path $out 'MoonlightPeaksCoop.dll')" $refArgs $sources
if ($LASTEXITCODE -ne 0) { throw "build failed" }
Write-Output "built $(Join-Path $out 'MoonlightPeaksCoop.dll')"
