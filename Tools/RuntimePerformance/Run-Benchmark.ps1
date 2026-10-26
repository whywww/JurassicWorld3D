param(
    [ValidateSet('Editor','Player','Build','Audit','Materials','Budgets','Visibility','Navigation','Animation','Follow','Return','Traffic','Foliage','Emotiv','MindWave','FrameDebugger')][string]$Mode = 'Editor',
    [string]$Label = 'measurement',
    [string]$ProjectRoot = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)),
    [string]$UnityEditorPath,
    [string]$PlayerPath,
    [int]$Width = 1920,
    [int]$Height = 1080,
    [double]$Warmup = 15,
    [double]$Seconds = 60,
    [int]$Repeats = 3,
    [string]$Cases = 'normal',
    [string]$Views = 'forest,creek,dinosaur,route',
    [switch]$Visible
)
$ErrorActionPreference = 'Stop'
$ProjectRoot = (Resolve-Path -LiteralPath $ProjectRoot).Path
if ($Label.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0) { throw 'Label must be a filename.' }
if ($Width -lt 64 -or $Height -lt 64 -or $Warmup -le 0 -or $Seconds -le 0 -or $Repeats -lt 1) { throw 'Invalid sampling configuration.' }
$logDirectory = Join-Path $ProjectRoot 'Logs\RuntimePerformance'
[IO.Directory]::CreateDirectory($logDirectory) | Out-Null
$logPath = Join-Path $logDirectory ($Label + '-' + $Mode + '.log')
$quotePath = { param($path) '"' + $path + '"' }
$methods = @{
    Editor = 'Run'; Build = 'Build'; Audit = 'Audit'; Materials = 'OptimizeMaterials';
    Budgets = 'JurassicWorld.Editor.RuntimePerformanceAcceptance.Run';
    Visibility = 'JurassicWorld.Editor.DinosaurInfoCardAcceptance.Run';
    Navigation = 'JurassicWorld.Editor.NavigationAcceptance.InstallAndVerify';
    Animation = 'JurassicWorld.Editor.DinosaurAnimationTimingAcceptance.Run';
    Follow = 'JurassicWorld.Editor.TRexOpenFollowAcceptance.Run';
    Return = 'JurassicWorld.Editor.TRexReturnWaitAcceptance.Run';
    Traffic = 'JurassicWorld.Editor.DinosaurGroundTrafficAcceptance.Run';
    Foliage = 'JurassicWorld.Editor.FoliageInteractionAcceptance.VerifyAndRun';
    Emotiv = 'JurassicWorld.Editor.EmotivInputAcceptance.Run';
    MindWave = 'JurassicWorld.BCI.MindWave.Tests.MindWaveAcceptance.Run';
    FrameDebugger = 'JurassicWorld.Editor.RuntimeFrameDebuggerCapture.Run'
}
if ($Mode -eq 'Player') {
    if (-not $Visible) { throw 'Player measurements require -Visible: hidden windows can skip camera rendering. Keep the test window visible until capture finishes.' }
    if (-not $PlayerPath) { $PlayerPath = Join-Path $ProjectRoot 'Builds\Performance\JurassicWorld.exe' }
    $executable = (Resolve-Path -LiteralPath $PlayerPath).Path
    $arguments = @('-force-d3d11','-force-gfx-mt','-screen-fullscreen','0','-screen-width',$Width,'-screen-height',$Height)
} else {
    $resolver = Join-Path (Split-Path -Parent $PSScriptRoot) 'CompileCheck\Resolve-UnityEditorPath.ps1'
    . $resolver
    $editor = Resolve-UnityEditorPath -UnityEditorPath $UnityEditorPath -ProjectRoot $ProjectRoot
    $executable = Join-Path $editor 'Unity.exe'
    $method = $methods[$Mode]
    if ($method -notmatch '^JurassicWorld\.') { $method = 'JurassicWorld.Editor.RuntimePerformanceTools.' + $method }
    $arguments = @('-batchmode','-force-d3d11','-force-gfx-mt','-projectPath',(& $quotePath $ProjectRoot),'-executeMethod',$method)
    if ($Mode -eq 'FrameDebugger' -and $Visible) { $arguments = @($arguments | Where-Object { $_ -ne '-batchmode' }) }
    if ($Mode -eq 'FrameDebugger') { $arguments += '-jwFrameDebuggerExit' }
    if ($Mode -in @('Build','Audit','Materials')) { $arguments += '-quit' }
}
if ($Mode -in @('Animation','Follow','Return')) {
    # Existing animation fixtures write video/report files into this project-relative directory.
    [IO.Directory]::CreateDirectory((Join-Path $ProjectRoot 'Tools\VegetationDiversity\RainforestSample')) | Out-Null
}
if ($Mode -in @('Editor','Player')) {
    $arguments += @('-jwPerf','-jwPerfLabel',$Label,'-jwPerfWidth',$Width,'-jwPerfHeight',$Height,
        '-jwPerfWarmup',$Warmup.ToString([Globalization.CultureInfo]::InvariantCulture),
        '-jwPerfSeconds',$Seconds.ToString([Globalization.CultureInfo]::InvariantCulture),
        '-jwPerfRepeats',$Repeats,'-jwPerfCases',$Cases,'-jwPerfViews',$Views)
}
$arguments += @('-logFile',(& $quotePath $logPath))
Write-Host "Running $Mode; log: $logPath"
$windowStyle = if ($Visible) { 'Normal' } else { 'Hidden' }
$process = Start-Process -FilePath $executable -ArgumentList $arguments -WorkingDirectory $ProjectRoot -WindowStyle $windowStyle -PassThru
$process.WaitForExit()
if ($process.ExitCode -ne 0) { throw "Unity exited with $($process.ExitCode). See $logPath. A live Editor cannot share its project with a batch process; use a separate test copy." }
Write-Host "Completed. Reports: $logDirectory"
