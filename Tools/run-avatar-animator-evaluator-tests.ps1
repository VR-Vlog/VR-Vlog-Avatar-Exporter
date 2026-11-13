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
    (Join-Path $PSScriptRoot 'AvatarAnimatorEvaluatorTests.cs'))
[AvatarAnimatorEvaluatorTests]::Run()
$recordings = Join-Path $PSScriptRoot 'Fixtures/AvatarAnimator'
if (Test-Path $recordings) { [AvatarAnimatorComparisonReplay]::Run($recordings) } else { [AvatarAnimatorComparisonReplay]::Smoke() }
