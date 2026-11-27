# -Vrm: exported VRM files with VRVLOG_avatar_animator to also play for the settled-frame check (#3664).
param([string[]]$Vrm = @())
$ErrorActionPreference = 'Stop'
$taskRepo = Split-Path -Parent $PSScriptRoot
Add-Type -Path @(
    (Join-Path $taskRepo 'Editor/JsonDom.cs'),
    (Join-Path $taskRepo 'Formats/Runtime/JsonAccess.cs'),
    (Join-Path $taskRepo 'Formats/Runtime/ExpressionAnimationData.cs'),
    (Join-Path $taskRepo 'Formats/Runtime/AvatarAnimatorData.cs'),
    (Join-Path $taskRepo 'Formats/Runtime/AvatarAnimatorEvaluator.cs'),
    (Join-Path $taskRepo 'Tests/AnimatorComparison/AvatarAnimatorComparisonCases.cs'),
    (Join-Path $PSScriptRoot 'AvatarAnimatorComparisonReplay.cs'),
    (Join-Path $PSScriptRoot 'AvatarAnimatorSettledCheck.cs'),
    (Join-Path $PSScriptRoot 'AvatarAnimatorEvaluatorTests.cs'))
[AvatarAnimatorEvaluatorTests]::Run()
$recordings = Join-Path $PSScriptRoot 'Fixtures/AvatarAnimator'
# The recordings are committed: a missing folder is a broken checkout, not a reason to check less.
if (-not (Test-Path $recordings)) { throw "Missing $recordings" }
[AvatarAnimatorComparisonReplay]::Run($recordings)
[AvatarAnimatorSettledCheck]::RunCases()
if ($Vrm.Count -gt 0) { [AvatarAnimatorSettledCheck]::RunFiles([string[]]($Vrm | ForEach-Object { (Resolve-Path $_).Path })) }
