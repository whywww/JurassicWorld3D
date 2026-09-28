param(
    [string]$AssetId = 'island_tree_02',
    [string]$Destination = 'Assets/ThirdParty/VegetationDiversity/PolyHaven/IslandTree02'
)

$ErrorActionPreference = 'Stop'
$files = Invoke-RestMethod -Uri "https://api.polyhaven.com/files/$AssetId"
$model = $files.fbx.'1k'.fbx
$root = Join-Path (Get-Location) $Destination
New-Item -ItemType Directory -Force -Path $root | Out-Null

function Save-Verified($entry, $relative) {
    $target = Join-Path $root $relative
    New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
    if (-not (Test-Path -LiteralPath $target) -or
        (Get-FileHash -LiteralPath $target -Algorithm MD5).Hash.ToLowerInvariant() -ne $entry.md5) {
        Invoke-WebRequest -Uri $entry.url -OutFile $target
    }
    $hash = (Get-FileHash -LiteralPath $target -Algorithm MD5).Hash.ToLowerInvariant()
    if ($hash -ne $entry.md5) { throw "MD5 mismatch: $target" }
    Write-Output "$relative $((Get-Item -LiteralPath $target).Length) bytes"
}

Save-Verified $model "$AssetId`_1k.fbx"
foreach ($property in $model.include.PSObject.Properties) {
    if ($property.Name -match '(?:_diff_|_alpha_|_nor_gl_)') {
        Save-Verified $property.Value $property.Name
    }
}
