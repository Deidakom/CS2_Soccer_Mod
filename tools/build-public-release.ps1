[CmdletBinding()]
param(
    [string]$Version = "v1.1.0"
)

$ErrorActionPreference = "Stop"
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot "artifacts\releases"))
$packageName = "CS2-SoccerMod-$($Version.TrimStart('v'))-server"
$stageRoot = [IO.Path]::GetFullPath((Join-Path $artifactsRoot $packageName))
$zipPath = Join-Path $artifactsRoot "$packageName.zip"
$zipHashPath = "$zipPath.sha256"

if (-not $stageRoot.StartsWith($artifactsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to stage outside the release artifacts directory: $stageRoot"
}

$dll = Join-Path $repoRoot "src\server-plugin\SoccerModMvp\bin\Release\net10.0\SoccerModNativeHull.dll"
if (-not (Test-Path -LiteralPath $dll -PathType Leaf)) {
    throw "Missing release input: $dll (run: dotnet build src/server-plugin/SoccerModMvp/SoccerModMvp.csproj -c Release)"
}

$payloadSource = Join-Path $repoRoot "deploy\release\payload\game"
if (-not (Test-Path -LiteralPath $payloadSource -PathType Container)) {
    throw "Missing committed release payload: $payloadSource"
}

New-Item -ItemType Directory -Force -Path $artifactsRoot | Out-Null
if (Test-Path -LiteralPath $stageRoot) {
    Remove-Item -LiteralPath $stageRoot -Recurse -Force
}
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
if (Test-Path -LiteralPath $zipHashPath) {
    Remove-Item -LiteralPath $zipHashPath -Force
}
New-Item -ItemType Directory -Force -Path $stageRoot | Out-Null

# Committed payload (native plugin, ball model, menu/radar resources) first,
# then the freshly built managed plugin DLL on top of it. Copy-Item -Recurse
# with a not-yet-existing destination copies the SOURCE folder itself into
# that destination (i.e. Destination becomes a copy of Source, not
# Source's contents) - passing the payload's own "game" folder as the
# source (not its parent) avoids doubling that path segment.
Copy-Item -LiteralPath $payloadSource -Destination (Join-Path $stageRoot "game") -Recurse
$dllTarget = Join-Path $stageRoot "game\csgo\addons\counterstrikesharp\plugins\SoccerModNativeHull\SoccerModNativeHull.dll"
New-Item -ItemType Directory -Force -Path (Split-Path $dllTarget -Parent) | Out-Null
Copy-Item -LiteralPath $dll -Destination $dllTarget -Force

Copy-Item -LiteralPath (Join-Path $repoRoot "deploy\release\README.md") -Destination (Join-Path $stageRoot "README.md")
Copy-Item -LiteralPath (Join-Path $repoRoot "deploy\release\install.sh") -Destination (Join-Path $stageRoot "install.sh")
Copy-Item -LiteralPath (Join-Path $repoRoot "deploy\release\verify.sh") -Destination (Join-Path $stageRoot "verify.sh")
New-Item -ItemType Directory -Force -Path (Join-Path $stageRoot "examples") | Out-Null
Copy-Item -LiteralPath (Join-Path $repoRoot "deploy\release\soccermod_server.cfg") -Destination (Join-Path $stageRoot "examples\soccermod_server.cfg")

foreach ($script in @("install.sh", "verify.sh")) {
    $path = Join-Path $stageRoot $script
    $content = [IO.File]::ReadAllText($path).Replace("`r`n", "`n")
    [IO.File]::WriteAllText($path, $content, [Text.UTF8Encoding]::new($false))
}

$commit = (& git -C $repoRoot rev-parse HEAD).Trim()
[IO.File]::WriteAllText((Join-Path $stageRoot "VERSION"), "$Version`ncommit=$commit`n", [Text.UTF8Encoding]::new($false))

# Substring instead of [IO.Path]::GetRelativePath - that method needs
# .NET Core/5+ (pwsh); this script also has to run on Windows PowerShell
# 5.1 (.NET Framework), which does not have it.
$stageRootWithSep = $stageRoot.TrimEnd('\') + '\'
$manifestFiles = Get-ChildItem -LiteralPath $stageRoot -Recurse -File | Sort-Object FullName
$manifest = foreach ($file in $manifestFiles) {
    $relative = $file.FullName.Substring($stageRootWithSep.Length).Replace('\', '/')
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $relative"
}
[IO.File]::WriteAllText(
    (Join-Path $stageRoot "SHA256SUMS"),
    ($manifest -join "`n") + "`n",
    [Text.UTF8Encoding]::new($false))

Compress-Archive -LiteralPath $stageRoot -DestinationPath $zipPath -CompressionLevel Optimal
$zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($zipHashPath, "$zipHash  $packageName.zip`n", [Text.UTF8Encoding]::new($false))

Write-Output "package=$zipPath"
Write-Output "sha256=$zipHash"
Write-Output "files=$($manifestFiles.Count)"
