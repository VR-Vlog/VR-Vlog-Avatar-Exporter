$ErrorActionPreference = 'Stop'
$taskRepo = Split-Path -Parent $PSScriptRoot
Add-Type -Path @(
    (Join-Path $taskRepo 'Editor/Compatibility/DependencyPolicy.cs'),
    (Join-Path $taskRepo 'Editor/Compatibility/DependencyPolicy.Generated.cs'),
    (Join-Path $PSScriptRoot 'DependencyCompatibilityTests.cs')
)
[DependencyCompatibilityTests]::Run()
Add-Type -Path @(
    (Join-Path $taskRepo 'Formats/Runtime/LilToon234Catalogue.cs'),
    (Join-Path $taskRepo 'Editor/LilToonStructureCheck.cs'),
    (Join-Path $PSScriptRoot 'LilToonStructureCheckTests.cs')
)
[LilToonStructureCheckTests]::Run()
