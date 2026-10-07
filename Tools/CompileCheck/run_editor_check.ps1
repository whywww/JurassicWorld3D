# Standalone Roslyn check for Assembly-CSharp-Editor using Unity's bundled compiler.
# Mirrors Assembly-CSharp-Editor.csproj references/defines; output goes to %TEMP%.
$ErrorActionPreference = 'Stop'
$projDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$csprojPath = Join-Path $projDir 'Assembly-CSharp-Editor.csproj'
$xml = New-Object System.Xml.XmlDocument
$xml.Load($csprojPath)

$sb = New-Object System.Text.StringBuilder
$dc = $xml.SelectSingleNode('//PropertyGroup/DefineConstants').InnerText
[void]$sb.AppendLine('-target:library')
[void]$sb.AppendLine('-langversion:9.0')
[void]$sb.AppendLine('-nostdlib+')
[void]$sb.AppendLine('-noconfig')
[void]$sb.AppendLine('-nowarn:0169,USG0001')
[void]$sb.AppendLine('-define:' + $dc)
[void]$sb.AppendLine('-out:"' + (Join-Path $env:TEMP 'Assembly-CSharp-Editor.check.dll') + '"')

foreach ($n in $xml.SelectNodes('//Reference/HintPath')) {
    $p = $n.InnerText
    if (-not [System.IO.Path]::IsPathRooted($p)) { $p = Join-Path $projDir $p }
    if (Test-Path -LiteralPath $p) {
        [void]$sb.AppendLine('-r:"' + $p + '"')
    } else {
        Write-Host "MISSING REF: $p"
    }
}

$ac = Join-Path $projDir 'Library\ScriptAssemblies\Assembly-CSharp.dll'
if (Test-Path -LiteralPath $ac) { [void]$sb.AppendLine('-r:"' + $ac + '"') }

# Project references compile into Unity's ScriptAssemblies, rather than HintPath entries.
foreach ($n in $xml.SelectNodes('//ProjectReference[@Include]')) {
    $assemblyName = [System.IO.Path]::GetFileNameWithoutExtension($n.GetAttribute('Include'))
    if ($assemblyName -eq 'Assembly-CSharp') { continue }
    $assemblyPath = Join-Path $projDir ('Library\ScriptAssemblies\' + $assemblyName + '.dll')
    if (-not (Test-Path -LiteralPath $assemblyPath)) { throw "Missing project assembly: $assemblyPath" }
    [void]$sb.AppendLine('-r:"' + $assemblyPath + '"')
}

foreach ($n in $xml.SelectNodes('//Compile[@Include]')) {
    $f = $n.GetAttribute('Include')
    if ($f -match '^Assets[\\/]Editor[\\/]') { continue }
    $p = Join-Path $projDir $f
    [void]$sb.AppendLine('"' + $p + '"')
}

# Unity-generated csproj files can lag behind editor script additions/deletions.
foreach ($source in Get-ChildItem -LiteralPath (Join-Path $projDir 'Assets\Editor') -Filter '*.cs' -Recurse) {
    [void]$sb.AppendLine('"' + $source.FullName + '"')
}

$rsp = Join-Path $env:TEMP 'jw_editor_check.rsp'
[System.IO.File]::WriteAllText($rsp, $sb.ToString(), [System.Text.Encoding]::UTF8)
Write-Host "RSP written: $rsp"

$mono = 'C:\Program Files\Unity 2022.3.62f3c1\Editor\Data\MonoBleedingEdge\bin\mono.exe'
$csc = 'C:\Program Files\Unity 2022.3.62f3c1\Editor\Data\MonoBleedingEdge\lib\mono\msbuild\Current\bin\Roslyn\csc.exe'
& $mono $csc "@$rsp"
Write-Host "CSC EXIT: $LASTEXITCODE"
exit $LASTEXITCODE
