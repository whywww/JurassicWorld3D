param(
    [string]$SdkZip = (Join-Path $PSScriptRoot '..\..\..\..\Windows-Developer-Tools-3.2.zip'),
    [string]$Destination = (Join-Path $env:TEMP 'JurassicWorld-MindWave-Connector')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$connectorRoot = [IO.Path]::GetFullPath($Destination)
[void][IO.Directory]::CreateDirectory($connectorRoot)
$sdkArchive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $SdkZip).Path)
try {
    $prefix = 'Windows Developer Tools 3.2/ThinkGear_Connector/ThinkGear_Connector/'
    foreach ($entry in $sdkArchive.Entries) {
        if (-not $entry.FullName.StartsWith($prefix) -or $entry.FullName.EndsWith('/')) { continue }
        $relative = $entry.FullName.Substring($prefix.Length)
        $target = [IO.Path]::GetFullPath((Join-Path $connectorRoot $relative))
        if (-not $target.StartsWith($connectorRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Archive path is outside extraction directory.'
        }
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))
        if (-not [IO.File]::Exists($target)) { [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target) }
    }
} finally { $sdkArchive.Dispose() }
Write-Output (Join-Path $connectorRoot 'ThinkGear Connector.exe')
Write-Output 'No application was started and no driver was installed. See ../README.md for serial setup.'
