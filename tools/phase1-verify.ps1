[CmdletBinding()]
param(
    [string] $ProjectRoot = '',
    [string] $ReferenceWorkspaceRoot = '',
    [string] $Cs2Install = '',
    [string] $WorkshopItemId = '3361075564',
    [string] $ExpectedWorkshopSha256 = '052BB4A46E7B80BF509F70CE53425185D4E35A6F59E600C8DF21651B46EAA6CC'
)

$ErrorActionPreference = 'Stop'

if (-not $ProjectRoot) {
    $ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
}
if (-not $ReferenceWorkspaceRoot) {
    $ReferenceWorkspaceRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
}
$ProjectRoot = (Resolve-Path -LiteralPath $ProjectRoot).Path
$ReferenceWorkspaceRoot = (Resolve-Path -LiteralPath $ReferenceWorkspaceRoot).Path

$phase0Arguments = @{
    WorkspaceRoot = $ReferenceWorkspaceRoot
}
if ($Cs2Install) {
    $phase0Arguments.Cs2Install = $Cs2Install
}

$phase0Json = & (Join-Path $PSScriptRoot 'phase0-verify.ps1') @phase0Arguments
$phase0 = $phase0Json | ConvertFrom-Json
$install = $phase0.Cs2.Install

$workshopVpk = ''
$publishData = ''
if ($install) {
    $commonDirectory = Split-Path -Parent $install
    $steamAppsDirectory = Split-Path -Parent $commonDirectory
    $libraryRoot = Split-Path -Parent $steamAppsDirectory
    $itemDirectory = Join-Path $libraryRoot (
        "steamapps\workshop\content\730\$WorkshopItemId"
    )
    $workshopVpk = Join-Path $itemDirectory "$WorkshopItemId.vpk"
    $publishData = Join-Path $itemDirectory 'publish_data.txt'
}

$workshopPresent = [bool]($workshopVpk -and (Test-Path -LiteralPath $workshopVpk -PathType Leaf))
$workshopHash = if ($workshopPresent) {
    (Get-FileHash -Algorithm SHA256 -LiteralPath $workshopVpk).Hash
} else { '' }
$workshopHashMatches = $workshopPresent -and (
    $workshopHash -eq $ExpectedWorkshopSha256
)

$nodeCandidates = @()
$nodeCommand = Get-Command node -ErrorAction SilentlyContinue
if ($nodeCommand) {
    $nodeCandidates += $nodeCommand.Source
}
if ($env:USERPROFILE) {
    $nodeCandidates += Join-Path $env:USERPROFILE '.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe'
}
$nodeCandidates += 'C:\Program Files\nodejs\node.exe'
$nodePath = $nodeCandidates |
    Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) } |
    Select-Object -First 1

$testExitCode = $null
$testPassCount = $null
$testFailCount = $null
$testOutputTail = @()
$requiredTestRelativePaths = @(
    'test\acceptance.test.js',
    'test\bundle.test.js',
    'test\cap.test.js',
    'test\goal.test.js',
    'test\kick.test.js',
    'test\layout.test.js',
    'test\live-gate-runner.test.js',
    'test\live-run-analyzer.test.js',
    'test\match.test.js',
    'test\physics-diagnostics.test.js',
    'test\physics-run-analyzer.test.js',
    'test\reset.test.js',
    'test\vector.test.js',
    'test\vmap-generator.test.js'
)
$requiredTestPaths = @($requiredTestRelativePaths | ForEach-Object {
    Join-Path $ProjectRoot $_
})
$missingTestPaths = @($requiredTestPaths | Where-Object {
    -not (Test-Path -LiteralPath $_ -PathType Leaf)
})
$testRoot = Join-Path $ProjectRoot 'test'
$allTestPaths = @()
if (Test-Path -LiteralPath $testRoot -PathType Container) {
    $allTestPaths = @(Get-ChildItem -LiteralPath $testRoot -Recurse -File -Filter '*.test.js' |
        Sort-Object FullName |
        ForEach-Object { $_.FullName })
}
if ($nodePath -and $missingTestPaths.Count -eq 0 -and $allTestPaths.Count -gt 0) {
    Push-Location $ProjectRoot
    try {
        $testLines = @(& $nodePath --test --test-reporter=tap @allTestPaths 2>&1)
        $testExitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }
    $testText = [string]::Join([Environment]::NewLine, [string[]]$testLines)
    if ($testText -match '(?m)^# pass (\d+)\s*$') {
        $testPassCount = [int]$Matches[1]
    }
    if ($testText -match '(?m)^# fail (\d+)\s*$') {
        $testFailCount = [int]$Matches[1]
    }
    $testOutputTail = @($testLines | Select-Object -Last 8 | ForEach-Object {
        [string] $_
    })
}

$contentRoot = if ($install) { Join-Path $install 'content' } else { '' }
$resourceCompiler = if ($install) {
    Join-Path $install 'game\bin\win64\resourcecompiler.exe'
} else { '' }
$toolsLauncher = if ($install) {
    Join-Path $install 'game\bin\win64\csgocfg.exe'
} else { '' }

$contentRootPresent = [bool]($contentRoot -and (Test-Path -LiteralPath $contentRoot -PathType Container))
$toolsLauncherPresent = [bool]($toolsLauncher -and (Test-Path -LiteralPath $toolsLauncher -PathType Leaf))
$resourceCompilerPresent = [bool]($resourceCompiler -and (Test-Path -LiteralPath $resourceCompiler -PathType Leaf))
$hammerPresent = [bool]$phase0.Cs2.HammerPresent

$referenceBlockers = @()
if (-not $workshopPresent) { $referenceBlockers += 'subscribed_workshop_package_missing' }
elseif (-not $workshopHashMatches) { $referenceBlockers += 'workshop_package_hash_drift' }

$toolchainBlockers = @()
if (-not $install) { $toolchainBlockers += 'cs2_install_missing' }
elseif (-not $phase0.Cs2.ExecutablePresent) { $toolchainBlockers += 'cs2_executable_missing' }
if ($phase0.Cs2.WorkshopToolsDisabled -eq $true) {
    $toolchainBlockers += 'workshop_tools_dlc_disabled'
}
elseif ($null -eq $phase0.Cs2.WorkshopToolsDisabled) {
    $toolchainBlockers += 'workshop_tools_dlc_status_unknown'
}
if (-not $contentRootPresent) { $toolchainBlockers += 'content_root_missing' }
if (-not $toolsLauncherPresent) { $toolchainBlockers += 'workshop_tools_launcher_missing' }
if (-not $resourceCompilerPresent) { $toolchainBlockers += 'resource_compiler_missing' }
if (-not $hammerPresent) { $toolchainBlockers += 'hammer_missing' }
if (-not $phase0.Cs2.PointScriptApiPresent) { $toolchainBlockers += 'point_script_api_missing' }

$pureCoreBlockers = @()
if ($missingTestPaths.Count -gt 0) { $pureCoreBlockers += 'pure_logic_tests_missing' }
if ($allTestPaths.Count -eq 0) { $pureCoreBlockers += 'pure_logic_tests_not_discovered' }
if (-not $nodePath) { $pureCoreBlockers += 'node_runtime_missing' }
elseif ($missingTestPaths.Count -eq 0 -and $allTestPaths.Count -gt 0 -and (
    $testExitCode -ne 0 -or
    $null -eq $testPassCount -or
    $testPassCount -le 0 -or
    $null -eq $testFailCount -or
    $testFailCount -ne 0
)) { $pureCoreBlockers += 'pure_logic_tests_failed' }

$result = [PSCustomObject]@{
    TimestampUtc = [DateTime]::UtcNow.ToString('o')
    Phase = 'phase-1'
    ProjectRoot = $ProjectRoot
    Cs2 = [PSCustomObject]@{
        Install = $install
        ExecutablePresent = $phase0.Cs2.ExecutablePresent
        BuildId = $phase0.Cs2.BuildId
        TargetBuildId = $phase0.Cs2.TargetBuildId
        WorkshopToolsDisabled = $phase0.Cs2.WorkshopToolsDisabled
        ContentRootPresent = $contentRootPresent
        ToolsLauncherPresent = $toolsLauncherPresent
        ResourceCompilerPresent = $resourceCompilerPresent
        HammerPresent = $hammerPresent
        HammerPaths = @($phase0.Cs2.HammerPaths | Where-Object { $_ })
        PointScriptApiPresent = $phase0.Cs2.PointScriptApiPresent
        PointScriptApi = $phase0.Cs2.PointScriptApi
        PointScriptApiSha256 = $phase0.Cs2.PointScriptApiSha256
    }
    Stadium = [PSCustomObject]@{
        WorkshopItemId = $WorkshopItemId
        PackagePath = $workshopVpk
        PackagePresent = $workshopPresent
        PackageSha256 = $workshopHash
        ExpectedPackageSha256 = $ExpectedWorkshopSha256
        PackageHashMatchesAudit = $workshopHashMatches
        PublishDataPath = $publishData
        PublishDataPresent = [bool]($publishData -and (Test-Path -LiteralPath $publishData -PathType Leaf))
        RuntimeMapName = 'soccer_cssl_stadium_v8'
    }
    Tests = [PSCustomObject]@{
        NodePath = $nodePath
        ExitCode = $testExitCode
        Pass = $testPassCount
        Fail = $testFailCount
        RequiredFiles = $requiredTestPaths
        DiscoveredFiles = $allTestPaths
        MissingFiles = $missingTestPaths
        OutputTail = $testOutputTail
    }
    Readiness = [PSCustomObject]@{
        CsScriptToolchainReady = ($toolchainBlockers.Count -eq 0)
        PureCoreReady = ($pureCoreBlockers.Count -eq 0)
        ReferenceAuditCurrent = ($referenceBlockers.Count -eq 0)
        ReadyForEngineLab = (
            $toolchainBlockers.Count -eq 0 -and
            $pureCoreBlockers.Count -eq 0
        )
        Phase1PreconditionsReady = (
            $toolchainBlockers.Count -eq 0 -and
            $pureCoreBlockers.Count -eq 0 -and
            $referenceBlockers.Count -eq 0
        )
    }
    Blockers = [PSCustomObject]@{
        CsScriptToolchain = $toolchainBlockers
        PureCore = $pureCoreBlockers
        ReferenceAudit = $referenceBlockers
    }
    AllBlockers = @($toolchainBlockers + $pureCoreBlockers + $referenceBlockers)
}

$result | ConvertTo-Json -Depth 7
