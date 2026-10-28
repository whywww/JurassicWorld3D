param([string]$Conda = (Join-Path $env:USERPROFILE 'miniforge3\Scripts\conda.exe'))
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
$tools = Join-Path $projectRoot 'Builds\BrainLinkTools'
$environmentPath = Join-Path $tools 'python-env'
$sdkPath = Join-Path $tools 'sdk'
New-Item -ItemType Directory -Path $sdkPath -Force | Out-Null
if (-not (Test-Path -LiteralPath (Join-Path $environmentPath 'python.exe'))) {
    if (-not (Test-Path -LiteralPath $Conda)) { throw 'Conda not found. Pass -Conda with the path to conda.exe.' }
    & $Conda create --yes --prefix $environmentPath --override-channels --channel conda-forge 'python=3.11' 'pyserial=3.5'
    if ($LASTEXITCODE -ne 0) { throw 'Conda environment creation failed.' }
}
$binary = Join-Path $sdkPath 'BrainLinkParser.pyd'
$expected = 'd9340d07031993cca7b68e6bd720cb6cc8ac4e795c26d93b18c7202a8d835319'
if (-not (Test-Path -LiteralPath $binary) -or (Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) {
    $download = Join-Path $sdkPath 'BrainLinkParser.download'
    Invoke-WebRequest -UseBasicParsing -Uri 'https://raw.githubusercontent.com/Macrotellect/BrainLinkParser-Python/066af71079bc9eb87a6b9dcd4517b8817715ef38/BrainLinkParser.pyd' -OutFile $download
    if ((Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) { throw 'SDK SHA-256 mismatch.' }
    Move-Item -LiteralPath $download -Destination $binary -Force
}
& (Join-Path $environmentPath 'python.exe') -c "import sys,struct; sys.path.insert(0,sys.argv[1]); from BrainLinkParser import BrainLinkParser; assert sys.version_info[:2]==(3,11) and struct.calcsize('P')==8; import serial; print('BrainLink SDK / Python 3.11 x64 / pyserial ready')" $sdkPath
if ($LASTEXITCODE -ne 0) { throw 'SDK import/environment verification failed.' }
Write-Output ('Ready: ' + $tools)
