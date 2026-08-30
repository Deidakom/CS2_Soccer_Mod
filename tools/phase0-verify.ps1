[CmdletBinding()]
param(
    [string] $WorkspaceRoot = '',
    [string] $Cs2Install = ''
)

$ErrorActionPreference = 'Stop'

if (-not $WorkspaceRoot) {
    $WorkspaceRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
}

function Get-IncludeClosure {
    param(
        [Parameter(Mandatory)] [string] $SourceRoot,
        [Parameter(Mandatory)] [string] $EntryPoint
    )

    $seen = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase
    )

    function Visit-SourceFile([string] $Path) {
        $resolved = [System.IO.Path]::GetFullPath($Path)
        if (-not $seen.Add($resolved)) {
            return
        }

        foreach ($line in Get-Content -LiteralPath $resolved) {
            if ($line -notmatch '^\s*#include\s+"([^"]+)"') {
                continue
            }

            $include = $Matches[1].Replace(
                '/', [System.IO.Path]::DirectorySeparatorChar
            )
            $rootCandidate = Join-Path $SourceRoot $include
            $localCandidate = Join-Path (
                [System.IO.Path]::GetDirectoryName($resolved)
            ) $include

            if (Test-Path -LiteralPath $rootCandidate) {
                Visit-SourceFile $rootCandidate
            }
            elseif (Test-Path -LiteralPath $localCandidate) {
                Visit-SourceFile $localCandidate
            }
            else {
                throw "Unresolved include '$include' from '$resolved'."
            }
        }
    }

    Visit-SourceFile $EntryPoint
    return @($seen | Sort-Object)
}

function Find-Cs2Installation {
    $steamRoots = @(
        'C:\Program Files (x86)\Steam',
        'C:\Program Files\Steam'
    ) | Where-Object { Test-Path -LiteralPath $_ }

    foreach ($steamRoot in $steamRoots) {
        $libraries = @($steamRoot)
        $libraryFile = Join-Path $steamRoot 'steamapps\libraryfolders.vdf'
        if (Test-Path -LiteralPath $libraryFile) {
            $raw = Get-Content -Raw -LiteralPath $libraryFile
            foreach ($match in [regex]::Matches($raw, '"path"\s+"([^"]+)"')) {
                $libraries += $match.Groups[1].Value.Replace('\\', '\')
            }
        }

        foreach ($library in $libraries | Select-Object -Unique) {
            $manifest = Join-Path $library 'steamapps\appmanifest_730.acf'
            if (-not (Test-Path -LiteralPath $manifest)) {
                continue
            }

            $manifestText = Get-Content -Raw -LiteralPath $manifest
            if ($manifestText -notmatch '"installdir"\s+"([^"]+)"') {
                continue
            }

            $candidate = Join-Path $library (
                'steamapps\common\' + $Matches[1]
            )
            if (Test-Path -LiteralPath (Join-Path $candidate 'game\bin\win64\cs2.exe')) {
                return [PSCustomObject]@{
                    Install = $candidate
                    Manifest = $manifest
                    ManifestText = $manifestText
                }
            }
        }
    }

    return $null
}

$referenceRoot = Join-Path $WorkspaceRoot 'ball-reference-analysis'
$sourceRoot = Join-Path $referenceRoot 'jersey-system\source'
$entryPoint = Join-Path $sourceRoot 'soccer_mod.sp'

if (-not (Test-Path -LiteralPath $entryPoint)) {
    throw "CSS source entry point not found: $entryPoint"
}

$closure = Get-IncludeClosure -SourceRoot $sourceRoot -EntryPoint $entryPoint
$allSource = @(
    Get-ChildItem -LiteralPath $sourceRoot -Recurse -File -Filter '*.sp'
)
$lineCount = ($closure | ForEach-Object {
    (Get-Content -LiteralPath $_ | Measure-Object -Line).Lines
} | Measure-Object -Sum).Sum

$versionLine = Select-String -LiteralPath $entryPoint -Pattern '^#define PLUGIN_VERSION "([^"]+)"' |
    Select-Object -First 1
$cssVersion = if ($versionLine) { $versionLine.Matches[0].Groups[1].Value } else { '' }

$keyRelativePaths = @(
    'somoe19-original\addons\sourcemod\scripting\soccer_mod.sp',
    'soccer_mod_patch\soccer_mod.sp',
    'jersey-system\source-v4-reference\soccer_mod.sp',
    'jersey-system\source\soccer_mod.sp',
    'ka_soccer_titans_club_2026.bsp',
    'natsu_xsl_arena\ka_soccer_xsl_natsu_arena_v1.vmf',
    'jersey-system\baseline-live\soccermod_jersey_pre_20260824T1845Z\files\home\gameserver\css\cstrike\cfg\sm_soccermod\soccer_mod.cfg'
)

$keyFiles = foreach ($relativePath in $keyRelativePaths) {
    $path = Join-Path $referenceRoot $relativePath
    [PSCustomObject]@{
        Path = $relativePath.Replace('\', '/')
        Exists = Test-Path -LiteralPath $path
        Sha256 = if (Test-Path -LiteralPath $path) {
            (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash.ToLowerInvariant()
        } else { '' }
    }
}

$discovered = if ($Cs2Install) {
    $resolvedInstall = (Resolve-Path -LiteralPath $Cs2Install).Path
    $commonDirectory = Split-Path -Parent $resolvedInstall
    $steamAppsDirectory = Split-Path -Parent $commonDirectory
    $manifest = Join-Path $steamAppsDirectory 'appmanifest_730.acf'
    [PSCustomObject]@{
        Install = $resolvedInstall
        Manifest = if (Test-Path -LiteralPath $manifest) { $manifest } else { '' }
        ManifestText = if (Test-Path -LiteralPath $manifest) {
            Get-Content -Raw -LiteralPath $manifest
        } else { '' }
    }
} else {
    Find-Cs2Installation
}

$cs2 = $null
if ($discovered) {
    $manifestText = $discovered.ManifestText
    $buildId = if ($manifestText -match '"buildid"\s+"([^"]+)"') { $Matches[1] } else { '' }
    $targetBuildId = if ($manifestText -match '"TargetBuildID"\s+"([^"]+)"') { $Matches[1] } else { '' }
    $disabledDlc = if ($manifestText -match '"DisabledDLC"\s+"([^"]+)"') { $Matches[1] } else { '' }
    $hammerModule = Join-Path $discovered.Install 'game\bin\win64\tools\hammer.dll'
    [string[]] $hammerPaths = if (Test-Path -LiteralPath $hammerModule -PathType Leaf) {
        @($hammerModule)
    } else { @() }
    $apiCandidates = @(
        'content\csgo_addons\cs_script_demo\maps\scripts\point_script.d.ts',
        'content\csgo\maps\editor\zoo\scripts\point_script.d.ts'
    ) | ForEach-Object {
        $candidatePath = Join-Path $discovered.Install $_
        [PSCustomObject]@{
            Path = $candidatePath
            Present = Test-Path -LiteralPath $candidatePath -PathType Leaf
            Sha256 = if (Test-Path -LiteralPath $candidatePath -PathType Leaf) {
                (Get-FileHash -Algorithm SHA256 -LiteralPath $candidatePath).Hash.ToLowerInvariant()
            } else { '' }
        }
    }
    $installedApi = $apiCandidates | Where-Object Present | Select-Object -First 1
    $cs2 = [PSCustomObject]@{
        Install = $discovered.Install
        Manifest = $discovered.Manifest
        BuildId = $buildId
        TargetBuildId = $targetBuildId
        WorkshopToolsDisabled = if ($manifestText) {
            $disabledDlc.Split(',') -contains '2279721'
        } else { $null }
        ExecutablePresent = Test-Path -LiteralPath (Join-Path $discovered.Install 'game\bin\win64\cs2.exe') -PathType Leaf
        HammerPresent = ($hammerPaths.Count -gt 0)
        HammerPaths = @($hammerPaths)
        PointScriptApi = if ($installedApi) { $installedApi.Path } else { '' }
        PointScriptApiPresent = [bool]$installedApi
        PointScriptApiSha256 = if ($installedApi) { $installedApi.Sha256 } else { '' }
        PointScriptApiCandidates = $apiCandidates
    }
}

$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
$dotnetSdks = if ($dotnetCommand) { @(& dotnet --list-sdks) } else { @() }

$result = [PSCustomObject]@{
    TimestampUtc = [DateTime]::UtcNow.ToString('o')
    WorkspaceRoot = $WorkspaceRoot
    Css = [PSCustomObject]@{
        DeclaredVersion = $cssVersion
        MainSourceSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $entryPoint).Hash.ToLowerInvariant()
        SourcePawnFilesPresent = $allSource.Count
        ActiveIncludeFiles = $closure.Count
        ActiveIncludeLines = $lineCount
        KeyFiles = $keyFiles
    }
    Cs2 = $cs2
    Tools = [PSCustomObject]@{
        Git = [bool](Get-Command git -ErrorAction SilentlyContinue)
        Dotnet = [bool]$dotnetCommand
        DotnetSdkCount = $dotnetSdks.Count
        DotnetSdks = $dotnetSdks
        SteamCmd = [bool](Get-Command steamcmd -ErrorAction SilentlyContinue)
        CMake = [bool](Get-Command cmake -ErrorAction SilentlyContinue)
        Clang = [bool](Get-Command clang -ErrorAction SilentlyContinue)
        MsvcCompiler = [bool](Get-Command cl -ErrorAction SilentlyContinue)
    }
}

$result | ConvertTo-Json -Depth 8
