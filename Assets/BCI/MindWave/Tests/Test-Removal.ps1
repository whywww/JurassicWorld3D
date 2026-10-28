param([string]$UnityEditor = 'C:\Program Files\Unity 2022.3.62f3c1\Editor\Unity.exe')
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..\..'))
$copyRoot = Join-Path $projectRoot ('Builds\MindWaveRemoval-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
[void][IO.Directory]::CreateDirectory($copyRoot)
# A fresh copy is created without the optional module. The user's project is never deleted or moved.
foreach ($folder in @('Assets','Packages','ProjectSettings','Library')) {
    & robocopy (Join-Path $projectRoot $folder) (Join-Path $copyRoot $folder) /E /R:1 /W:1 /NFL /NDL /NJH /NJS /NP /XD (Join-Path $projectRoot 'Assets\BCI\MindWave') /XF MindWave.meta
    if ($LASTEXITCODE -ge 8) { throw ('Copy failed: ' + $folder) }
}
if (Test-Path -LiteralPath (Join-Path $copyRoot 'Assets\BCI\MindWave')) { throw 'Removal copy still contains module.' }
$buildSource = @'
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
public static class MindWaveRemovalBuild
{
    public static void Run()
    {
        if (JurassicWorld.PlayerInputs.PlayerMovementInput.HasProvider)
            throw new System.Exception("Provider remains after module removal");
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        if (Camera.main == null || Camera.main.GetComponent<JurassicWorld.SmoothExplorerCamera>() == null
            || Camera.main.GetComponent<JurassicWorld.EmotivPlayerInput>() == null)
            throw new System.Exception("Original player inputs missing");
        UnityEditor.GameObjectUtility.RemoveMonoBehavioursWithMissingScript(Camera.main.gameObject);
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        var report = BuildPipeline.BuildPlayer(new[] { "Assets/Scenes/Main.unity" },
            "Builds/Removal/JurassicWorld.exe", BuildTarget.StandaloneWindows64, BuildOptions.Development);
        bool ok = report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded;
        Debug.Log("[MindWaveRemoval] " + (ok ? "PASS" : "FAIL") + " main scene and Windows build without module");
        EditorApplication.Exit(ok ? 0 : 1);
    }
}
'@
Set-Content -LiteralPath (Join-Path $copyRoot 'Assets\Editor\MindWaveRemovalBuild.cs') -Value $buildSource -Encoding UTF8
foreach ($method in @('JurassicWorld.Editor.EmotivInputAcceptance.RunPlayerMovementRegression','MindWaveRemovalBuild.Run')) {
    $log = Join-Path $copyRoot (($method.Split('.')[-1]) + '.log')
    $arguments = '-batchmode -nographics -projectPath "' + $copyRoot + '" -executeMethod ' + $method + ' -logFile "' + $log + '"'
    $process = Start-Process -FilePath $UnityEditor -ArgumentList $arguments -PassThru -WindowStyle Hidden
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw ('Unity failed. Inspect ' + $log) }
}
Write-Output ('Removal verification passed; copy/build/logs: ' + $copyRoot)
