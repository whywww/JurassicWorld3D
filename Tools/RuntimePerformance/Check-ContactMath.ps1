param([string]$BaselineSource, [string]$UnityEditorPath)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'CompileCheck\Resolve-UnityEditorPath.ps1')
$editor = Resolve-UnityEditorPath -UnityEditorPath $UnityEditorPath -ProjectRoot $projectRoot
$mono = Join-Path $editor 'Data\MonoBleedingEdge\bin\mono.exe'
$compiler = Join-Path $editor 'Data\MonoBleedingEdge\lib\mono\msbuild\Current\bin\Roslyn\csc.exe'
$core = Join-Path $editor 'Data\Managed\UnityEngine\UnityEngine.CoreModule.dll'
$standard = Join-Path $editor 'Data\MonoBleedingEdge\lib\mono\4.5\Facades\netstandard.dll'
$directory = Join-Path $env:TEMP 'JurassicContactMath'
[IO.Directory]::CreateDirectory($directory) | Out-Null
if ($BaselineSource) { $baseline = Get-Content -LiteralPath $BaselineSource -Raw }
else { $baseline = (git -C $projectRoot show HEAD:Assets/Scripts/DinosaurFoliageInteractor.cs) -join "`n" }
if (-not $baseline.Contains('namespace JurassicWorld')) { throw 'Cannot read baseline source.' }
$baselinePath = Join-Path $directory 'BaselineContact.cs'
[IO.File]::WriteAllText($baselinePath, $baseline.Replace('namespace JurassicWorld','namespace Baseline'))
$executable = Join-Path $directory 'ContactMathCheck.exe'
& $mono $compiler -nologo -langversion:9.0 "-out:$executable" "-r:$core" "-r:$standard" $baselinePath (Join-Path $projectRoot 'Assets\Scripts\DinosaurFoliageInteractor.cs') (Join-Path $projectRoot 'Assets\Scripts\FoliageSpringSolver.cs') (Join-Path $PSScriptRoot 'ContactMathCheck.cs')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$previousMonoPath = $env:MONO_PATH
try {
    $env:MONO_PATH = Split-Path -Parent $core
    & $mono $executable
    $result = $LASTEXITCODE
} finally { $env:MONO_PATH = $previousMonoPath }
exit $result
