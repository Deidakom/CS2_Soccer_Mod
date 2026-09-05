[CmdletBinding()]
param(
    [string]$Cs2ToolsRoot = 'E:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive',
    [switch]$UpdateReleasePayload
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$sourceRoot = Join-Path $repoRoot 'src\workshop-addon\soccermod_classic_ui'
$contentAddonRoot = Join-Path $Cs2ToolsRoot 'content\csgo_addons\soccermod_classic_ui'
$gameCsgoRoot = Join-Path $Cs2ToolsRoot 'game\csgo'
$gameAddonRoot = Join-Path $Cs2ToolsRoot 'game\csgo_addons\soccermod_classic_ui'
$compiler = Join-Path $Cs2ToolsRoot 'game\bin\win64\resourcecompiler.exe'

if (-not (Test-Path -LiteralPath $compiler)) {
    throw "resourcecompiler.exe not found at $compiler"
}
if (-not (Test-Path -LiteralPath $sourceRoot)) {
    throw "Classic menu addon source not found at $sourceRoot"
}

$expectedContentParent = (Join-Path $Cs2ToolsRoot 'content\csgo_addons') + '\'
if (-not $contentAddonRoot.StartsWith($expectedContentParent, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing unexpected content target: $contentAddonRoot"
}

$inputs = @(
    @{ Source = 'panorama\layout\custom_game\soccermod_classic_menu.xml'; Destination = 'panorama\layout\custom_game\soccermod_classic_menu.xml' },
    @{ Source = 'panorama\styles\custom_game\soccermod_classic_menu.css'; Destination = 'panorama\styles\custom_game\soccermod_classic_menu.css' },
    @{ Source = 'maps\scripts\soccermod_classic_menu.js'; Destination = 'scripts\vscripts\soccermod_classic_menu.js' }
)

New-Item -ItemType Directory -Force -Path $contentAddonRoot | Out-Null
Copy-Item -LiteralPath (Join-Path $sourceRoot 'addoninfo.txt') -Destination (Join-Path $contentAddonRoot 'addoninfo.txt') -Force

$compilerInputs = foreach ($input in $inputs) {
    $source = Join-Path $sourceRoot $input.Source
    $destination = Join-Path $contentAddonRoot $input.Destination
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination -Force
    $destination
}

& $compiler -nop4 -f -game $gameCsgoRoot -i @compilerInputs
if ($LASTEXITCODE -ne 0) {
    throw "Classic menu resource compilation failed with exit code $LASTEXITCODE"
}

$expectedOutputs = @(
    'panorama\layout\custom_game\soccermod_classic_menu.vxml_c',
    'panorama\styles\custom_game\soccermod_classic_menu.vcss_c',
    'scripts\vscripts\soccermod_classic_menu.vjs_c'
)
foreach ($relative in $expectedOutputs) {
    $output = Join-Path $gameAddonRoot $relative
    if (-not (Test-Path -LiteralPath $output)) {
        throw "Expected compiled resource was not produced: $output"
    }
    if ($UpdateReleasePayload) {
        $payload = Join-Path $repoRoot (Join-Path 'deploy\release\payload\game\csgo' $relative)
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $payload) | Out-Null
        Copy-Item -LiteralPath $output -Destination $payload -Force
        if ((Get-FileHash -LiteralPath $output).Hash -ne (Get-FileHash -LiteralPath $payload).Hash) {
            throw "Release payload mismatch: $relative"
        }
    }
}

Write-Host "Classic menu addon compiled successfully: $gameAddonRoot"
