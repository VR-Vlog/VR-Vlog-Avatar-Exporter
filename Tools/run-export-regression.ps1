#Requires -Version 7.0
# Exports the avatars of a job list with the exporter package installed in a Unity
# project, through the detailed transfer board, so that two package versions can be
# compared with Tools/compare-exports.py.
#
#   pwsh -File Tools/run-export-regression.ps1 -Unity <Unity.exe> -Project <project dir>
#        -Jobs <jobs.txt> -Out <results dir> [-Label <text>]
#
# Jobs: UTF-8, one job per line "ID|prefab path|runs" (runs defaults to 2; see the
# header of ExportRegressionRunner.cs). Results, progress.txt and unity.log go to -Out.
#
# The runner is not part of the package. This script copies it into a temporary
# Assets folder of the project together with an asmdef whose versionDefines define
# VRVLOG_EXPORT_REGRESSION_RUNNER while the exporter package is installed, runs Unity
# in batch mode (with graphics: the board renders previews), waits for it and removes
# the folder and its .meta again. The project must not be open in another Editor.
# Exit code: Unity's exit code (0 when every job ran), 2 for bad arguments.
param(
    [Parameter(Mandatory)][string]$Unity,
    [Parameter(Mandatory)][string]$Project,
    [Parameter(Mandatory)][string]$Jobs,
    [Parameter(Mandatory)][string]$Out,
    [string]$Label = 'x'
)
$ErrorActionPreference = 'Stop'
$folderName = '_VRVlogExportRegression'
$entry = 'VRVlog.LilToonExporter.ExportRegression.ExportRegressionRunner.Main'

if (-not (Test-Path -LiteralPath $Unity -PathType Leaf)) { Write-Error "Unity not found: $Unity" -ErrorAction Continue; exit 2 }
if (-not (Test-Path -LiteralPath (Join-Path $Project 'Assets') -PathType Container)) { Write-Error "Not a Unity project: $Project" -ErrorAction Continue; exit 2 }
if (-not (Test-Path -LiteralPath $Jobs -PathType Leaf)) { Write-Error "Job list not found: $Jobs" -ErrorAction Continue; exit 2 }
$Project = (Resolve-Path -LiteralPath $Project).Path
$Jobs = (Resolve-Path -LiteralPath $Jobs).Path
New-Item -ItemType Directory -Force -Path $Out | Out-Null
$Out = (Resolve-Path -LiteralPath $Out).Path
if (Test-Path -LiteralPath (Join-Path $Out 'progress.txt')) {
    Write-Host "Resuming in $Out : jobs that progress.txt shows as started are skipped. Use a new -Out for a new run."
}

$temp = Join-Path $Project "Assets/$folderName"
$asmdef = @'
{
  "name": "VRVlog.LilToonExporter.ExportRegression",
  "references": [],
  "includePlatforms": [ "Editor" ],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": false,
  "precompiledReferences": [],
  "autoReferenced": false,
  "defineConstraints": [],
  "versionDefines": [
    { "name": "com.vrvlog.liltoon-vrm-exporter", "expression": "0.0.0", "define": "VRVLOG_EXPORT_REGRESSION_RUNNER" }
  ],
  "noEngineReferences": false
}
'@

function Remove-RunnerFolder {
    if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Recurse -Force }
    if (Test-Path -LiteralPath "$temp.meta") { Remove-Item -LiteralPath "$temp.meta" -Force }
}

$process = $null
$code = 1
try {
    # A folder left by an interrupted run has the same fixed name; replace it.
    Remove-RunnerFolder
    $editorDir = Join-Path $temp 'Editor'
    New-Item -ItemType Directory -Force -Path $editorDir | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ExportRegressionRunner.cs') -Destination $editorDir
    Set-Content -LiteralPath (Join-Path $editorDir 'VRVlog.LilToonExporter.ExportRegression.asmdef') -Value $asmdef -Encoding ascii

    $start = [System.Diagnostics.ProcessStartInfo]::new($Unity)
    $start.UseShellExecute = $false
    foreach ($argument in @('-batchmode', '-projectPath', $Project, '-executeMethod', $entry,
            '-regressionOut', $Out, '-regressionJobs', $Jobs, '-regressionLabel', $Label,
            '-logFile', (Join-Path $Out 'unity.log'))) {
        $start.ArgumentList.Add($argument)
    }
    $started = Get-Date
    $process = [System.Diagnostics.Process]::Start($start)
    Write-Host "Unity PID $($process.Id); log $(Join-Path $Out 'unity.log')"
    $process.WaitForExit()
    $code = $process.ExitCode
    Write-Host "Unity exited with $code after $([int]((Get-Date) - $started).TotalSeconds) s"
}
finally {
    # Stop only the Unity this script started (by its process object), for example after Ctrl+C.
    if ($null -ne $process -and -not $process.HasExited) {
        $process.Kill($true)
        $process.WaitForExit()
    }
    Remove-RunnerFolder
}
exit $code
