param(
    [string]$Prefix = ('alltiers-' + (Get-Date -Format yyyyMMdd-HHmmss)),
    [string]$BaselinePlayer = 'Builds/AllTierBaseline/JurassicWorld.exe',
    [string]$OptimizedPlayer = 'Builds/AllTierClean/JurassicWorld.exe',
    [ValidateSet('Low','Medium','High')][string]$StartTier = 'Low'
)
$ErrorActionPreference = 'Stop'
foreach ($tier in 'Low','Medium','High') {
    if ([array]::IndexOf(@('Low','Medium','High'),$tier) -lt [array]::IndexOf(@('Low','Medium','High'),$StartTier)) { continue }
    & "$PSScriptRoot/Run-Benchmark.ps1" -Mode Player -PlayerPath $BaselinePlayer -Quality $tier -VegetationReduction 0 -RenderScale 1 -Label "$Prefix-before-$tier" -Visible
    & "$PSScriptRoot/Run-Benchmark.ps1" -Mode Player -PlayerPath $OptimizedPlayer -Quality $tier -VegetationReduction -1 -RenderScale 1 -Label "$Prefix-native-$tier" -Visible
    if ($tier -ne 'High') {
        $scale = if ($tier -eq 'Low') { 0.75 } else { 0.85 }
        & "$PSScriptRoot/Run-Benchmark.ps1" -Mode Player -PlayerPath $OptimizedPlayer -Quality $tier -VegetationReduction -1 -RenderScale $scale -Label "$Prefix-scaled-$tier" -Visible
    }
}
Write-Host "MATRIX_COMPLETE $Prefix"
