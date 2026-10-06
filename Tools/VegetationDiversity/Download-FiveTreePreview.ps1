$ErrorActionPreference = 'Stop'
$assets = [ordered]@{
    island_tree_01 = 'IslandTree01'
    island_tree_03 = 'IslandTree03'
    tree_small_02 = 'TreeSmall02'
    jacaranda_tree = 'JacarandaTree'
    fir_tree_01 = 'FirTree01'
}
foreach ($id in $assets.Keys) {
    $files = Invoke-RestMethod -TimeoutSec 30 -Uri "https://api.polyhaven.com/files/$id"
    $model = $files.fbx.'1k'.fbx
    if (-not $model) { throw "No 1K FBX for $id" }
    $root = Join-Path (Get-Location) "Assets/ThirdParty/VegetationDiversity/PolyHaven/$($assets[$id])"
    New-Item -ItemType Directory -Force -Path $root | Out-Null
    $entries = [ordered]@{}
    $entries["${id}_1k.fbx"] = $model
    foreach ($property in $model.include.PSObject.Properties) {
        if ($property.Name -match '(?:_diff_|_alpha_|_nor_gl_)') { $entries[$property.Name] = $property.Value }
    }
    foreach ($relative in $entries.Keys) {
        $entry = $entries[$relative]
        $target = Join-Path $root $relative
        New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
        if (-not (Test-Path -LiteralPath $target) -or
            (Get-FileHash -LiteralPath $target -Algorithm MD5).Hash.ToLowerInvariant() -ne $entry.md5) {
            & curl.exe --fail --location --silent --show-error --retry 3 --connect-timeout 20 --max-time 300 $entry.url --output $target
            if ($LASTEXITCODE -ne 0) { throw "Download failed: $relative" }
        }
        if ((Get-FileHash -LiteralPath $target -Algorithm MD5).Hash.ToLowerInvariant() -ne $entry.md5) {
            throw "MD5 mismatch: $target"
        }
        Write-Output "$id $relative $($entry.size) bytes verified"
    }
    $record = [ordered]@{ asset = $id; source = "https://polyhaven.com/a/$id"; license = 'CC0'; resolution = '1k'; files = $entries }
    $record | ConvertTo-Json -Depth 15 | Set-Content -Encoding utf8 (Join-Path $root 'download-manifest.json')
}
