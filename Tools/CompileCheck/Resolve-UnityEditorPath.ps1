# Resolve an Editor directory or Unity.exe from configuration or generated project references.
function Resolve-UnityEditorPath {
    param([string]$UnityEditorPath, [string]$ProjectRoot)

    if (-not $UnityEditorPath) { $UnityEditorPath = $env:UNITY_EDITOR_PATH }
    if (-not $UnityEditorPath) {
        $projectFile = Join-Path $ProjectRoot 'Assembly-CSharp.csproj'
        if (Test-Path -LiteralPath $projectFile) {
            [xml]$project = Get-Content -LiteralPath $projectFile -Raw
            foreach ($reference in $project.SelectNodes('//Reference/HintPath')) {
                if ($reference.InnerText -match '^(.*[\\/]Editor)[\\/]Data[\\/]Managed[\\/]UnityEngine[\\/]UnityEngine.CoreModule\.dll$') {
                    $UnityEditorPath = $Matches[1]
                    break
                }
            }
        }
    }
    if (-not $UnityEditorPath) {
        throw 'Cannot locate Unity. Generate IDE project files in Unity, or set -UnityEditorPath / UNITY_EDITOR_PATH to the Editor directory or Unity.exe.'
    }
    $UnityEditorPath = (Resolve-Path -LiteralPath $UnityEditorPath -ErrorAction Stop).Path
    if (Test-Path -LiteralPath $UnityEditorPath -PathType Leaf) {
        if ([IO.Path]::GetFileName($UnityEditorPath) -ne 'Unity.exe') { throw 'Expected Unity.exe or its Editor directory.' }
        $UnityEditorPath = Split-Path -Parent $UnityEditorPath
    }
    foreach ($relative in @('Unity.exe','Data\MonoBleedingEdge\bin\mono.exe','Data\MonoBleedingEdge\lib\mono\msbuild\Current\bin\Roslyn\csc.exe')) {
        if (-not (Test-Path -LiteralPath (Join-Path $UnityEditorPath $relative) -PathType Leaf)) {
            throw "Missing Unity tool: $relative. Check -UnityEditorPath / UNITY_EDITOR_PATH."
        }
    }
    return $UnityEditorPath
}
