param(
    [ValidateSet('simulate','serial')][string]$Mode = 'simulate',
    [string]$SerialPort = '',
    [int]$Port = 13856,
    [int]$Attention = 70,
    [int]$Signal = 0,
    [switch]$Auto,
    [int]$DurationSeconds = 0
)
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
$tools = Join-Path $projectRoot 'Builds\BrainLinkTools'
$pythonPath = Join-Path $tools 'python-env\python.exe'
if (-not (Test-Path -LiteralPath $pythonPath)) { throw 'Run Setup-BrainLink.ps1 first.' }
$arguments = @((Join-Path $PSScriptRoot 'brainlink_bridge.py'), '--sdk-dir', (Join-Path $tools 'sdk'), '--mode', $Mode, '--port', "$Port", '--attention', "$Attention", '--signal', "$Signal", '--duration', "$DurationSeconds")
if ($Mode -eq 'serial') { $arguments += @('--serial-port', $SerialPort) }
if ($Auto) { $arguments += '--auto' }
& $pythonPath @arguments
if ($LASTEXITCODE -ne 0) { throw ('BrainLink bridge exited with code ' + $LASTEXITCODE) }
