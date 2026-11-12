param(
    [ValidateSet('Editor','Player','Build','Audit','Materials','Visibility','Navigation','NavigationLow','Animation','Follow','Return','Traffic','Foliage','Emotiv','MindWave','BrainLink','FrameDebugger')][string]$Mode = 'Editor',
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
    [string]$Views = 'forest,creek,dinosaur-close,route',
    [ValidateSet('Low','Medium','High')][string]$Quality = 'Medium',
    [ValidateSet(-1,0,10,20,30,50)][int]$VegetationReduction = -1,
    [ValidateRange(0.5,1.0)][double]$RenderScale = 1,
    [switch]$Development,
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
    Visibility = 'JurassicWorld.Editor.DinosaurInfoCardAcceptance.Run';
    Navigation = 'JurassicWorld.Editor.NavigationAcceptance.InstallAndVerify';
    NavigationLow = 'JurassicWorld.Editor.NavigationAcceptance.VerifyExistingLow';
    Animation = 'JurassicWorld.Editor.DinosaurAnimationTimingAcceptance.Run';
    Follow = 'JurassicWorld.Editor.TRexOpenFollowAcceptance.Run';
    Return = 'JurassicWorld.Editor.TRexReturnWaitAcceptance.Run';
    Traffic = 'JurassicWorld.Editor.DinosaurGroundTrafficAcceptance.Run';
    Foliage = 'JurassicWorld.Editor.FoliageInteractionAcceptance.VerifyAndRun';
    Emotiv = 'JurassicWorld.Editor.EmotivInputAcceptance.Run';
    MindWave = 'JurassicWorld.BCI.MindWave.Tests.MindWaveAcceptance.Run';
    BrainLink = 'JurassicWorld.BCI.BrainLink.Tests.BrainLinkAcceptance.Run';
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
    if ($Mode -in @('Editor','FrameDebugger') -and $Visible) { $arguments = @($arguments | Where-Object { $_ -ne '-batchmode' }) }
    if ($Mode -eq 'Build' -and $Development) { $arguments += '-jwPerfDevelopment' }
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
        '-jwPerfRepeats',$Repeats,'-jwPerfCases',$Cases,'-jwPerfViews',$Views,'-jwPerfQuality',$Quality,
        '-jwRenderScale',$RenderScale.ToString([Globalization.CultureInfo]::InvariantCulture),'-jwPerfNoSceneView','-jwPerfExit','-jwLowVegetationReduction',$VegetationReduction)
}
$arguments += @('-logFile',(& $quotePath $logPath))
Write-Host "Running $Mode; log: $logPath"
$windowStyle = if ($Visible) { 'Normal' } else { 'Hidden' }
if ($Mode -eq 'Player' -and $Visible -and -not ('JwBenchmarkWindowState' -as [type])) {
    Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class JwBenchmarkWindowState {
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] public static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll")] public static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("kernel32.dll")] public static extern uint SetThreadExecutionState(uint flags);
    public static bool DesktopAvailable() {
        var desktop = OpenInputDesktop(0, false, 1);
        if (desktop == IntPtr.Zero) return false;
        CloseDesktop(desktop); return true;
    }
}
"@
}
if ($Mode -eq 'Player' -and $Visible -and -not [JwBenchmarkWindowState]::DesktopAvailable()) {
    throw 'Visible performance capture needs an unlocked desktop.'
}
$keepAwake = $Mode -eq 'Player' -and $Visible
if ($keepAwake) { [JwBenchmarkWindowState]::SetThreadExecutionState(2147483651) | Out-Null }
try {
$process = Start-Process -FilePath $executable -ArgumentList $arguments -WorkingDirectory $ProjectRoot -WindowStyle $windowStyle -PassThru
$timeoutSeconds = if ($Mode -in @('Editor','Player')) { 600 + ($Warmup + $Seconds) * $Repeats * $Views.Split(',').Count * $Cases.Split(',').Count } else { 1800 }
$captureClock = [Diagnostics.Stopwatch]::StartNew()
while (-not $process.WaitForExit(1000)) {
    $process.Refresh()
    $invalid = $null
    if ($captureClock.Elapsed.TotalSeconds -gt $timeoutSeconds) { $invalid = 'Test timed out.' }
    if ($Mode -eq 'Player' -and $Visible) {
        if ($process.MainWindowHandle -ne [IntPtr]::Zero -and [JwBenchmarkWindowState]::IsIconic($process.MainWindowHandle)) {
            $invalid = 'Player was minimized during capture.'
        }
        if (-not [JwBenchmarkWindowState]::DesktopAvailable()) { $invalid = 'Desktop was locked or unavailable.' }
    }
    if ($invalid) {
        Stop-Process -Id $process.Id -ErrorAction SilentlyContinue
        $invalidDirectory = Join-Path $logDirectory $Label
        [IO.Directory]::CreateDirectory($invalidDirectory) | Out-Null
        [IO.File]::WriteAllText((Join-Path $invalidDirectory 'INVALID.md'), $invalid + ' Partial samples cannot be used for acceptance.')
        throw "$invalid Capture is invalid. See $logPath."
    }
}
if ($process.ExitCode -ne 0) { throw "Unity exited with $($process.ExitCode). See $logPath for the compilation, fixture or startup failure." }
Write-Host "Completed. Reports: $logDirectory"
} finally {
    if ($keepAwake) { [JwBenchmarkWindowState]::SetThreadExecutionState(2147483648) | Out-Null }
}
