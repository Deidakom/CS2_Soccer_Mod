# Builds soccermod_native.dll (Windows x64) against the same pinned
# Metamod:Source, hl2sdk and AMBuild revisions as build-linux.sh.
#
#   pwsh src/native-plugin/soccermod_native/build-windows.ps1            # build
#   pwsh src/native-plugin/soccermod_native/build-windows.ps1 -Install   # and copy into the Windows payload
#
# Run from a Visual Studio developer shell (cl.exe on PATH; in CI the
# ilammy/msvc-dev-cmd action sets it up). Needs git and python 3. Sources are
# cached under $env:SOCCERMOD_NATIVE_DEPS (default %LOCALAPPDATA%\soccermod-native).
param([switch]$Install)
$ErrorActionPreference = 'Stop'

# Keep these three in step with build-linux.sh.
$metamodCommit = 'fa6f80e4662e5b96cc2e97722d812f374581dfd8'
$hl2sdkCommit = '625bfd4e39816ad0e1eae3b3144a3b5425c5c7a0'
$ambuildCommit = '01212cb57c96561f664b6dfb2ee10e66de6f81e4'

$here = $PSScriptRoot
$repoRoot = (Resolve-Path (Join-Path $here '../../..')).Path
$deps = if ($env:SOCCERMOD_NATIVE_DEPS) { $env:SOCCERMOD_NATIVE_DEPS } else { Join-Path $env:LOCALAPPDATA 'soccermod-native' }
$payload = Join-Path $repoRoot 'deploy/release/payload-windows/game/csgo/addons/soccermod_native/bin/win64/soccermod_native.dll'

function Invoke-Git { git @args; if ($LASTEXITCODE) { throw "git $args failed" } }

# Shallow checkout of one commit, reused while it stays the pinned one.
function Checkout($dir, $url, $commit) {
    if (-not (Test-Path (Join-Path $dir '.git'))) {
        Invoke-Git init -q $dir
        Invoke-Git -C $dir remote add origin $url
    }
    $head = git -C $dir rev-parse -q --verify 'HEAD^{commit}' 2>$null
    if ($head -ne $commit) {
        Invoke-Git -C $dir fetch -q --depth 1 origin $commit
        Invoke-Git -C $dir checkout -q --force --detach FETCH_HEAD
    }
}

New-Item -ItemType Directory -Force $deps | Out-Null
Checkout (Join-Path $deps 'metamod-source') 'https://github.com/alliedmodders/metamod-source' $metamodCommit
Invoke-Git -C (Join-Path $deps 'metamod-source') submodule update -q --init --depth 1 --recursive
Checkout (Join-Path $deps 'hl2sdk-root/hl2sdk-cs2') 'https://github.com/alliedmodders/hl2sdk' $hl2sdkCommit
Checkout (Join-Path $deps 'ambuild') 'https://github.com/alliedmodders/ambuild' $ambuildCommit

$build = Join-Path $deps 'build'
if (Test-Path $build) { Remove-Item -Recurse -Force $build }
New-Item -ItemType Directory -Force $build | Out-Null
Push-Location $build
try {
    $env:PYTHONPATH = Join-Path $deps 'ambuild'
    python (Join-Path $here 'configure.py') -s cs2 --targets=x86_64 --enable-optimize `
        --mms_path (Join-Path $deps 'metamod-source') --hl2sdk-root (Join-Path $deps 'hl2sdk-root') `
        --hl2sdk-manifests (Join-Path $deps 'metamod-source/hl2sdk-manifests')
    if ($LASTEXITCODE) { throw 'configure.py failed' }
    python -c 'from ambuild2 import run; run.cli_run()'
    if ($LASTEXITCODE) { throw 'ambuild failed' }
}
finally { Pop-Location }

$dll = Join-Path $build 'package/cs2/addons/soccermod_native/bin/win64/soccermod_native.dll'
if (-not (Test-Path $dll)) { throw "build produced no $dll" }
$hash = (Get-FileHash -Algorithm SHA256 $dll).Hash.ToLowerInvariant()
Write-Host "Built $dll, sha256 $($hash.Substring(0, 12))"
if ($Install) {
    New-Item -ItemType Directory -Force (Split-Path $payload) | Out-Null
    Copy-Item -Force $dll $payload
    Write-Host "Installed into $payload"
}
