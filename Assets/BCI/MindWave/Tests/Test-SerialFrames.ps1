param([string]$ConnectorDirectory = (Join-Path $PSScriptRoot '..\..\..\..\Builds\MindWaveTools\Connector'))
$ErrorActionPreference = 'Stop'
$ConnectorDirectory = (Resolve-Path -LiteralPath $ConnectorDirectory).Path
# The supplied .NET ThinkGear library is x86; its parser is independent of Unity's native x64 SDK.
if ([IntPtr]::Size -eq 8) {
    & "$env:WINDIR\SysWOW64\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath -ConnectorDirectory $ConnectorDirectory
    exit $LASTEXITCODE
}
$simulatorPath = Join-Path $PSScriptRoot '..\Simulator\Start-MindWaveSimulator.ps1'
$scriptText = Get-Content -LiteralPath $simulatorPath -Raw
# Compile the production packet builder without starting a server or opening a COM port.
& ([scriptblock]::Create($scriptText.Substring(0, $scriptText.LastIndexOf('[MindWaveSimulator]::Run('))))
foreach ($name in @('NLog.dll','ThinkGear.dll')) {
    [void][Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $ConnectorDirectory $name)))
}
$sdk = [AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.FullName -like 'ThinkGear,*' } | Select-Object -First 1
$owner = [Activator]::CreateInstance($sdk.GetType('NeuroSky.ThinkGear.Connector'))
$checks = 0
function Require([bool]$condition, [string]$message) {
    $script:checks++
    if (-not $condition) { throw $message }
}
function Payload([byte[]]$packet) {
    Require ($packet[0] -eq 170 -and $packet[1] -eq 170) 'Invalid sync bytes'
    Require ($packet.Length -eq $packet[2] + 4) 'Invalid payload length'
    $sum = 0
    for ($i = 3; $i -lt $packet.Length; $i++) { $sum += $packet[$i] }
    Require (($sum -band 255) -eq 255) 'Invalid checksum'
    return ,([byte[]]$packet[3..($packet.Length - 2)])
}
try {
    $connectionType = $sdk.GetType('NeuroSky.ThinkGear.Connector+Connection')
    # Construction initializes parser state; no Connect/Open call is made.
    $connection = $connectionType.GetConstructors([Reflection.BindingFlags]'Public,NonPublic,Instance')[0].Invoke(@($owner,'COM999'))
    $parse = $connectionType.GetMethod('ParsePayload',[Reflection.BindingFlags]'Public,NonPublic,Instance')
    [byte[]]$raw = Payload ([MindWaveSimulator]::RawPacket(-123))
    Require ($raw[0] -eq 128 -and $raw[1] -eq 2 -and $raw[2] -eq 255 -and $raw[3] -eq 133) 'Raw sample must be signed, big-endian 16-bit'
    for ($i = 0; $i -lt 768; $i++) { [void]$parse.Invoke($connection,@(,$raw)) }
    $parser = [Activator]::CreateInstance($sdk.GetType('NeuroSky.ThinkGear.TGParser'))
    foreach ($value in @(0,39,40,70,100)) {
        [byte[]]$payload = Payload ([MindWaveSimulator]::Packet($value,0))
        $rows = $parse.Invoke($connection,@(,$payload))
        $attention = $null; $signal = $null
        foreach ($dictionary in $parser.Read($rows)) {
            if ($dictionary.ContainsKey('Attention')) { $attention = $dictionary['Attention'] }
            if ($dictionary.ContainsKey('PoorSignal')) { $signal = $dictionary['PoorSignal'] }
        }
        Require ($attention -eq $value) ('Official SDK did not decode attention=' + $value)
        Require ($signal -eq 0) 'Official SDK did not decode good signal'
    }
    [byte[]]$poorPayload = Payload ([MindWaveSimulator]::Packet(70,200))
    $rows = $parse.Invoke($connection,@(,$poorPayload)); $signal = $null
    foreach ($dictionary in $parser.Read($rows)) { if ($dictionary.ContainsKey('PoorSignal')) { $signal = $dictionary['PoorSignal'] } }
    Require ($signal -eq 200) 'Official SDK did not decode poor signal'
    Write-Output ('[MindWaveSerialFrames] PASS ' + $checks + ' checks; production frames decoded by official .NET SDK after 768 raw packets. No COM ports opened.')
} finally { $owner.Close() }
