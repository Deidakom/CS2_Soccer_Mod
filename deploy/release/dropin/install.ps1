# CS2 SoccerMod drop-in installer (Windows). Started by install.bat. Run it
# once after copying the release into your server folder, and again after
# every CS2 update (the update restores gameinfo.gi and removes Metamod's line).
#
# It only adds Metamod's search path to game\csgo\gameinfo.gi (backup first,
# nothing else is changed) and checks that the mod files are in place.
param([string]$Root = $PSScriptRoot)
$ErrorActionPreference = 'Stop'

$gi = Join-Path $Root 'game\csgo\gameinfo.gi'
if (-not (Test-Path -LiteralPath $gi)) {
    Write-Host "gameinfo.gi not found at $gi"
    Write-Host "Copy the release into your CS2 server folder (the one that contains 'game') and run install.bat there."
    exit 1
}

$missing = @(
    'game\csgo\addons\metamod\bin\win64\metamod.2.cs2.dll',
    'game\csgo\addons\counterstrikesharp\bin\win64\counterstrikesharp.dll',
    'game\csgo\addons\multiaddonmanager\bin\multiaddonmanager.dll',
    'game\csgo\addons\soccermod_native\bin\win64\soccermod_native.dll',
    'game\csgo\addons\counterstrikesharp\plugins\SoccerModNativeHull\SoccerModNativeHull.dll',
    'game\csgo\cfg\multiaddonmanager\multiaddonmanager.cfg'
) | Where-Object { -not (Test-Path -LiteralPath (Join-Path $Root $_)) }
if ($missing) {
    $missing | ForEach-Object { Write-Host "missing: $_" }
    Write-Host "Copy the WHOLE release (the game folder included) into $Root and run this again."
    exit 1
}

$text = [IO.File]::ReadAllText($gi)
if ($text -match '(?m)^[ \t]*Game[ \t]+csgo/addons/metamod[ \t]*(//.*)?\r?$') {
    Write-Host 'gameinfo.gi: Metamod line already present.'
}
else {
    if (-not (Test-Path -LiteralPath "$gi.soccermod.bak")) { Copy-Item -LiteralPath $gi -Destination "$gi.soccermod.bak" }
    $nl = if ($text.Contains("`r`n")) { "`r`n" } else { "`n" }
    # Metamod's documented place: right after Game_LowViolence, else before
    # "Game csgo". Same indentation and line ending as that line.
    $after = [regex]::Match($text, '(?m)^([ \t]*)Game_LowViolence\b[^\r\n]*\r?\n')
    if ($after.Success) {
        $text = $text.Insert($after.Index + $after.Length, "$($after.Groups[1].Value)Game`tcsgo/addons/metamod$nl")
    }
    else {
        $before = [regex]::Match($text, '(?m)^([ \t]*)Game[ \t]+csgo[ \t]*(//[^\r\n]*)?\r?$')
        if (-not $before.Success) {
            Write-Host "gameinfo.gi layout not recognised; add 'Game csgo/addons/metamod' under SearchPaths by hand."
            exit 1
        }
        $text = $text.Insert($before.Index, "$($before.Groups[1].Value)Game`tcsgo/addons/metamod$nl")
    }
    [IO.File]::WriteAllText($gi, $text, [Text.UTF8Encoding]::new($false))
    Write-Host 'gameinfo.gi: Metamod line added (backup: gameinfo.gi.soccermod.bak).'
}

Write-Host ''
Write-Host 'CS2 SoccerMod is installed. Start the server with any map - SoccerMod'
Write-Host 'switches to the stadium by itself. Players need the Workshop item'
Write-Host '3797479770 (the server tells them). Run install.bat again after every CS2 update.'
