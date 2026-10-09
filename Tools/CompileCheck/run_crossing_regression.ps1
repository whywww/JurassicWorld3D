param([string]$CorridorSource, [string]$UnityEditorPath)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $CorridorSource) { $CorridorSource = Join-Path $projectRoot 'Assets\Scripts\MovementCorridor.cs' }
. (Join-Path $PSScriptRoot 'Resolve-UnityEditorPath.ps1')
$unityEditor = Resolve-UnityEditorPath -UnityEditorPath $UnityEditorPath -ProjectRoot $projectRoot
$unityRoot = Join-Path $unityEditor 'Data'
$mono = Join-Path $unityRoot 'MonoBleedingEdge\bin\mono.exe'
$compiler = Join-Path $unityRoot 'MonoBleedingEdge\lib\mono\msbuild\Current\bin\Roslyn\csc.exe'
$core = Join-Path $unityRoot 'Managed\UnityEngine\UnityEngine.CoreModule.dll'
$standard = Join-Path $unityRoot 'MonoBleedingEdge\lib\mono\4.5\Facades\netstandard.dll'
$executable = Join-Path $env:TEMP 'JurassicCrossingRegression.exe'
& $mono $compiler -nologo -langversion:9.0 "-out:$executable" "-r:$core" "-r:$standard" $CorridorSource (Join-Path $PSScriptRoot 'CrossingRegression.cs')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$previousMonoPath = $env:MONO_PATH
try {
    $env:MONO_PATH = Split-Path -Parent $core
    & $mono $executable
    $regressionExit = $LASTEXITCODE
} finally { $env:MONO_PATH = $previousMonoPath }
exit $regressionExit
