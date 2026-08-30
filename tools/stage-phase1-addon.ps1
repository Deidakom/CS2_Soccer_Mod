[CmdletBinding()]
param(
    [string] $ProjectRoot = '',
    [string] $Cs2Install = ''
)

$ErrorActionPreference = 'Stop'
$AddonName = 'soccermod_phase1'
$MapName = 'soccermod_phase1_lab'
$ExpectedBuildId = '24957633'
$ExpectedTemplateSha256 = '3514A445F23C54427A37CDB8776D7BAC44738A653A3FBA0EBFA99E05762E485C'
$ExpectedApiSha256 = '2DA5D7D10FFCEA1AAC52E668CF153974A3D973AEB8E7DC9A15FB8A2227B50BF9'
$ExpectedTsconfigSha256 = 'C923105C41BC5020828D32E60B9212A3D6C012E65F6AA8786D1ED72B11DF718C'
$ExpectedDmxConvertSha256 = '4FFFAB89C45624F251B376C6256F55FF1BC77D4FF48258DC19143FDA295EE3EA'

function Get-FirstExistingFile {
    param([string[]] $Candidates)
    return $Candidates |
        Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) } |
        Select-Object -First 1
}

function Get-FileDisposition {
    param(
        [string] $Source,
        [string] $Destination,
        [string[]] $KnownPriorSha256 = @()
    )

    $sourceHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $Source).Hash
    if (-not (Test-Path -LiteralPath $Destination -PathType Leaf)) {
        return [PSCustomObject]@{
            Source = $Source
            Destination = $Destination
            Sha256 = $sourceHash
            Action = 'create'
        }
    }

    $destinationHash = (
        Get-FileHash -Algorithm SHA256 -LiteralPath $Destination
    ).Hash
    if ($sourceHash -eq $destinationHash) {
        return [PSCustomObject]@{
            Source = $Source
            Destination = $Destination
            Sha256 = $sourceHash
            Action = 'unchanged'
        }
    }
    if ($KnownPriorSha256 -contains $destinationHash) {
        return [PSCustomObject]@{
            Source = $Source
            Destination = $Destination
            Sha256 = $sourceHash
            Action = 'replace_known_generated_revision'
        }
    }
    throw "Refusing to overwrite changed or unmanaged file '$Destination'. Review it before changing the staging manifest or source."
}

function Copy-AndVerify {
    param([pscustomobject] $Disposition)

    if ($Disposition.Action -eq 'unchanged') { return }
    $parent = Split-Path -Parent $Disposition.Destination
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }
    Copy-Item -LiteralPath $Disposition.Source -Destination $Disposition.Destination -Force
    $writtenHash = (
        Get-FileHash -Algorithm SHA256 -LiteralPath $Disposition.Destination
    ).Hash
    if ($writtenHash -ne $Disposition.Sha256) {
        throw "Hash verification failed after writing '$($Disposition.Destination)'."
    }
}

if (-not $ProjectRoot) {
    $ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
}
$ProjectRoot = (Resolve-Path -LiteralPath $ProjectRoot).Path

$phase1Arguments = @{ ProjectRoot = $ProjectRoot }
if ($Cs2Install) { $phase1Arguments.Cs2Install = $Cs2Install }
$phase1Json = & (Join-Path $PSScriptRoot 'phase1-verify.ps1') @phase1Arguments
$phase1 = $phase1Json | ConvertFrom-Json
if ($phase1.Readiness.Phase1PreconditionsReady -ne $true) {
    throw "Phase 1 preconditions are not ready: $([string]::Join(', ', [string[]]$phase1.AllBlockers))"
}
if ($phase1.Cs2.BuildId -ne $ExpectedBuildId -or
    $phase1.Cs2.TargetBuildId -ne $ExpectedBuildId) {
    throw "CS2 build drift detected. Expected audited build '$ExpectedBuildId'; found build '$($phase1.Cs2.BuildId)' and target '$($phase1.Cs2.TargetBuildId)'. Re-audit before staging."
}
if (-not $Cs2Install) { $Cs2Install = $phase1.Cs2.Install }
if (-not $Cs2Install -or -not (Test-Path -LiteralPath $Cs2Install -PathType Container)) {
    throw 'The CS2 installation could not be resolved.'
}
$Cs2Install = (Resolve-Path -LiteralPath $Cs2Install).Path

$contentAddonsRoot = Join-Path $Cs2Install 'content\csgo_addons'
$gameAddonsRoot = Join-Path $Cs2Install 'game\csgo_addons'
$contentAddon = Join-Path $contentAddonsRoot $AddonName
$gameAddon = Join-Path $gameAddonsRoot $AddonName

if (-not (Test-Path -LiteralPath $contentAddon -PathType Container) -or
    -not (Test-Path -LiteralPath $gameAddon -PathType Container)) {
    throw @"
Valve has not created both roots for '$AddonName'. In CS2 Workshop Tools,
create an empty addon (or duplicate addon_template) named exactly '$AddonName',
then run this script again. No addon metadata was fabricated.
"@
}

$resolvedContentRoot = (Resolve-Path -LiteralPath $contentAddonsRoot).Path
$resolvedGameRoot = (Resolve-Path -LiteralPath $gameAddonsRoot).Path
$resolvedContentAddon = (Resolve-Path -LiteralPath $contentAddon).Path
$resolvedGameAddon = (Resolve-Path -LiteralPath $gameAddon).Path
if ((Split-Path -Parent $resolvedContentAddon) -ne $resolvedContentRoot -or
    (Split-Path -Parent $resolvedGameAddon) -ne $resolvedGameRoot) {
    throw 'Resolved addon roots escaped the expected CS2 addon directories.'
}
foreach ($rootToCheck in @(
    $resolvedContentRoot,
    $resolvedGameRoot,
    $resolvedContentAddon,
    $resolvedGameAddon
)) {
    if ((Get-Item -LiteralPath $rootToCheck).Attributes -band
        [IO.FileAttributes]::ReparsePoint) {
        throw "Reparse-point addon path is outside this stager's safety boundary: '$rootToCheck'."
    }
}

$addonInfo = Join-Path $resolvedGameAddon 'addoninfo.txt'
if (-not (Test-Path -LiteralPath $addonInfo -PathType Leaf)) {
    throw "Valve-generated addon metadata is missing: '$addonInfo'."
}
$addonInfoText = Get-Content -LiteralPath $addonInfo -Raw
if ($addonInfoText -match '"IsTemplate"\s+"1"') {
    throw "'$AddonName' is still marked as a template; create it through Addon Manager instead of copying template files manually."
}
if ($addonInfoText -match '"HideInTools"\s+"1"') {
    throw "'$AddonName' is hidden from Workshop Tools; recreate it through Addon Manager."
}

$nodePath = Get-FirstExistingFile @(
    (Get-Command node -ErrorAction SilentlyContinue).Source,
    (Join-Path $env:USERPROFILE '.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe'),
    'C:\Program Files\nodejs\node.exe'
)
$dmxConvert = Join-Path $Cs2Install 'game\bin\win64\dmxconvert.exe'
$generator = Join-Path $ProjectRoot 'tools\generate-phase1-vmap.mjs'
$bundler = Join-Path $ProjectRoot 'tools\bundle-phase1-adapter.mjs'
$templateMap = Join-Path $contentAddonsRoot 'addon_template\maps\xxx_mapname_xxx.vmap'
$apiSource = Join-Path $contentAddonsRoot 'cs_script_demo\maps\scripts\point_script.d.ts'
$tsconfigSource = Join-Path $contentAddonsRoot 'cs_script_demo\maps\scripts\tsconfig.json'

foreach ($required in @(
    $nodePath,
    $dmxConvert,
    $generator,
    $bundler,
    $templateMap,
    $apiSource,
    $tsconfigSource
)) {
    if (-not $required -or -not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Required Phase 1 input is missing: '$required'."
    }
}

$pinnedInputs = @(
    [PSCustomObject]@{ Name = 'Valve addon template'; Path = $templateMap; Expected = $ExpectedTemplateSha256 },
    [PSCustomObject]@{ Name = 'point_script API'; Path = $apiSource; Expected = $ExpectedApiSha256 },
    [PSCustomObject]@{ Name = 'Valve script tsconfig'; Path = $tsconfigSource; Expected = $ExpectedTsconfigSha256 },
    [PSCustomObject]@{ Name = 'DMXConvert'; Path = $dmxConvert; Expected = $ExpectedDmxConvertSha256 }
)
foreach ($pinnedInput in $pinnedInputs) {
    $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $pinnedInput.Path).Hash
    if ($actualHash -ne $pinnedInput.Expected) {
        throw "$($pinnedInput.Name) drifted. Expected '$($pinnedInput.Expected)', found '$actualHash'. Re-audit before staging."
    }
}

$mapsDirectory = Join-Path $resolvedContentAddon 'maps'
$destinationMap = Join-Path $mapsDirectory "$MapName.vmap"
$seedMap = $templateMap

$tempPrefix = "cs2-soccermod-phase1-stage-$PID-$([Guid]::NewGuid().ToString('N'))"
$tempDirectory = [IO.Path]::GetTempPath()
$seedText = Join-Path $tempDirectory "$tempPrefix-seed.vmap"
$generatedText = Join-Path $tempDirectory "$tempPrefix-generated-text.vmap"
$generatedBinary = Join-Path $tempDirectory "$tempPrefix-generated-binary.vmap"
$roundTripText = Join-Path $tempDirectory "$tempPrefix-roundtrip.vmap"
$bundledAdapter = Join-Path $tempDirectory "$tempPrefix-adapter.js"
$temporaryFiles = @(
    $seedText,
    $generatedText,
    $generatedBinary,
    $roundTripText,
    $bundledAdapter
)

try {
    $sourceScripts = Join-Path $ProjectRoot 'src\ball-lab'
    $javascriptSources = @(
        (Join-Path $sourceScripts 'engine\adapter.js'),
        (Join-Path $sourceScripts 'physics-diagnostics.js'),
        (Join-Path $sourceScripts 'layout.js'),
        (Join-Path $sourceScripts 'core\goal.js'),
        (Join-Path $sourceScripts 'core\kick.js'),
        (Join-Path $sourceScripts 'core\cap.js'),
        (Join-Path $sourceScripts 'core\match.js'),
        (Join-Path $sourceScripts 'core\reset.js'),
        (Join-Path $sourceScripts 'core\vector.js'),
        $generator,
        $bundler
    )
    foreach ($javascriptSource in $javascriptSources) {
        $syntaxOutput = @(& $nodePath --check $javascriptSource 2>&1)
        if ($LASTEXITCODE -ne 0) {
            throw "JavaScript syntax check failed for '$javascriptSource': $([string]::Join(' | ', [string[]]$syntaxOutput))"
        }
    }

    $bundleOutput = @(& $nodePath $bundler $ProjectRoot $bundledAdapter 2>&1)
    if ($LASTEXITCODE -ne 0 -or
        -not (Test-Path -LiteralPath $bundledAdapter -PathType Leaf)) {
        throw "Phase 1 runtime bundling failed: $([string]::Join(' | ', [string[]]$bundleOutput))"
    }
    $bundleSyntaxOutput = @(& $nodePath --check $bundledAdapter 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "Bundled adapter syntax check failed: $([string]::Join(' | ', [string[]]$bundleSyntaxOutput))"
    }
    $bundledAdapterText = Get-Content -LiteralPath $bundledAdapter -Raw
    if ($bundledAdapterText -match '\bfrom\s*["'']\.') {
        throw 'Bundled adapter still contains a relative module specifier.'
    }
    $expectedPointScriptImport = '(?ms)^import\s*\{\s*CSGearSlot,\s*CSInputs,\s*CSWeaponAttackType,\s*Instance,?\s*\}\s*from\s*["'']cs_script/point_script["''];'
    if (([regex]::Matches($bundledAdapterText, $expectedPointScriptImport)).Count -ne 1) {
        throw 'Bundled adapter does not contain the exact audited point_script import surface.'
    }

    $convertSeedOutput = @(& $dmxConvert -i $seedMap -o $seedText `
        -oe keyvalues2 -of vmap 2>&1)
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $seedText -PathType Leaf)) {
        throw "Valve DMX conversion of the seed map failed: $([string]::Join(' | ', [string[]]$convertSeedOutput))"
    }

    $generatorOutput = @(& $nodePath $generator $seedText $generatedText 2>&1)
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $generatedText -PathType Leaf)) {
        throw "Phase 1 map generation failed: $([string]::Join(' | ', [string[]]$generatorOutput))"
    }

    $parseOutput = @(& $dmxConvert -i $generatedText -ie keyvalues2 `
        -o $generatedBinary -oe binary -of vmap 2>&1)
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $generatedBinary -PathType Leaf)) {
        throw "Valve rejected the generated map: $([string]::Join(' | ', [string[]]$parseOutput))"
    }

    $roundTripOutput = @(& $dmxConvert -i $generatedBinary -o $roundTripText `
        -oe keyvalues2 -of vmap 2>&1)
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $roundTripText -PathType Leaf)) {
        throw "Valve could not round-trip the generated map: $([string]::Join(' | ', [string[]]$roundTripOutput))"
    }
    $roundTripMap = Get-Content -LiteralPath $roundTripText -Raw
    $requiredMapTokens = @(
        [PSCustomObject]@{ Token = '"classname" "string" "point_script"'; Count = 1 },
        [PSCustomObject]@{ Token = '"targetname" "string" "sm_ball_lab_script"'; Count = 1 },
        [PSCustomObject]@{ Token = '"classname" "string" "prop_physics_multiplayer"'; Count = 1 },
        [PSCustomObject]@{ Token = '"targetname" "string" "sm_ball"'; Count = 1 },
        [PSCustomObject]@{ Token = '"targetname" "string" "sm_ball_reset_marker"'; Count = 1 },
        [PSCustomObject]@{ Token = '"targetname" "string" "sm_goal_west_marker"'; Count = 1 },
        [PSCustomObject]@{ Token = '"targetname" "string" "sm_goal_east_marker"'; Count = 1 },
        [PSCustomObject]@{ Token = '"nodeID" "int" "10006"'; Count = 1 },
        [PSCustomObject]@{ Token = '"referenceID" "uint64" "0x51ab200000000006"'; Count = 1 },
        [PSCustomObject]@{ Token = '"id" "elementid" "5c325137-f715-49e0-99e8-0e14bdb71001"'; Count = 1 },
        [PSCustomObject]@{ Token = '"origin" "vector3" "4096 0 -8"'; Count = 1 },
        [PSCustomObject]@{ Token = '"-2048 -2048 8"'; Count = 1 },
        [PSCustomObject]@{ Token = '"nodeID" "int" "10007"'; Count = 1 },
        [PSCustomObject]@{ Token = '"referenceID" "uint64" "0x51ab200000000007"'; Count = 1 },
        [PSCustomObject]@{ Token = '"id" "elementid" "5c325137-f715-49e0-99e8-0e14bdb72001"'; Count = 1 },
        [PSCustomObject]@{ Token = '"origin" "vector3" "4096 1536 256"'; Count = 1 },
        [PSCustomObject]@{ Token = '"-1024 -8 256"'; Count = 1 },
        [PSCustomObject]@{ Token = '"physicsmode" "string" "1"'; Count = 1 },
        [PSCustomObject]@{ Token = '"model" "string" "models/props/de_dust/hr_dust/dust_soccerball/dust_soccer_ball001.vmdl"'; Count = 1 },
        [PSCustomObject]@{ Token = '"maps/scripts/ball_lab/adapter.vjs"'; Count = 2 }
    )
    foreach ($requiredMapToken in $requiredMapTokens) {
        $tokenCount = ([regex]::Matches(
            $roundTripMap,
            [regex]::Escape($requiredMapToken.Token)
        )).Count
        if ($tokenCount -ne $requiredMapToken.Count) {
            throw "Generated map semantic check expected $($requiredMapToken.Count) '$($requiredMapToken.Token)', found $tokenCount."
        }
    }
    $ballScaleMatch = [regex]::Match(
        $roundTripMap,
        '(?s)"targetname"\s+"string"\s+"sm_ball".{0,1800}?"scales"\s+"vector3"\s+"([^"]+)"'
    )
    if (-not $ballScaleMatch.Success) {
        throw 'Generated map semantic check could not recover the ball scale.'
    }
    $ballScaleParts = @($ballScaleMatch.Groups[1].Value -split '\s+' |
        Where-Object { $_ })
    if ($ballScaleParts.Count -ne 3) {
        throw "Generated ball scale is malformed: '$($ballScaleMatch.Groups[1].Value)'."
    }
    $expectedBallScale = 1.8987341772
    foreach ($ballScalePart in $ballScaleParts) {
        $ballScale = [double]::Parse(
            $ballScalePart,
            [Globalization.CultureInfo]::InvariantCulture
        )
        if ([double]::IsNaN($ballScale) -or
            [double]::IsInfinity($ballScale) -or
            [Math]::Abs($ballScale - $expectedBallScale) -gt 0.000001) {
            throw "Generated ball scale drifted after Valve's float normalization: '$($ballScaleMatch.Groups[1].Value)'."
        }
    }

    $destinationScripts = Join-Path $resolvedContentAddon 'maps\scripts'
    $artifacts = @(
        [PSCustomObject]@{
            Source = $generatedBinary
            Destination = $destinationMap
            KnownPriorSha256 = @(
                '831617F623B2E46F69BD1CE2BC8F0C57447C48B579AC0A5F53CB8EA57F13C8A9',
                '8F97311EDFB837968968C17D240FA862434C575EA34CAE6684A09D07543FBBF2',
                '34F07C7A6EB40EE6F9367929A2CD3D3F61BA8F3250427575338E2C808FF04653',
                '321138DECF4AE8F3F17A2BD183CC5152239D9D0D978EB46AB7449F348A577D8A',
                '00FD897C72A03CB5C592DD75724490F1F6896879C785B2D5AC00309ADEB289EE'
            )
        },
        [PSCustomObject]@{
            Source = $bundledAdapter
            Destination = (Join-Path $destinationScripts 'ball_lab\adapter.js')
            KnownPriorSha256 = @(
                '88EAD04C66A095B7E727010A52B6B9C4A28B12E5F971CD31827E851585D79BD9',
                'C1BE401CD2716ED4DEC6D01DF90B387E11E326CA15EF81256C66F47A852CDC3B',
                '69E80A8D27CF36397B38BD3643D94DD35C50478A9B6CC0A10EA3FA042BAF2F18',
                'B9BD21B8A3B96AF78B75A276EF7654BDC1F7F9452D944E9CEDB36956B7A77AE6',
                '6955A38EE9F4D9F99C3C662D908538BC66370D6C80B4C50A93C7D7CBDA74F416',
                '6EE023A824D445673059BEB879D9B1B986F0C0F64BD2603BAD6C5FE1439F80A4',
                '8C7B6069749E270225569993CBDDEA398A1D0B7DB65E905F045C4DD17A5711CC',
                '4A6E5EC2BC0F245C7B3E555B5E48D7279BA714F9D7754C000EB8397A910DF85E',
                '1068B8C6641EC23CCE092B1CDF6EA997B6D91E88B098E39A846064A1FBA7328B',
                '631D759B3B08707627829B461D7DCD132057556633928CF4325C487725E1CB4D',
                '647FD8BB44BB90547FD6195A386F3154F2CA793D60089E0D30A1C6BCD6922CA7',
                '1A15CEA5F5206DB6764D1AE5B1D5D518C748D1697791ABA66A6A51ACFCE5D90F',
                '8C824EAB3218B3AC6733A1D55D950D29BFDF9CD4284F917A66A5E5A946FD4620',
                'AFCEBE429B556A72EFDCA7C711A6136EAEAC2F2009D975F00FE56EB41900AA34',
                'CE24C08FE02A313FC753ED0D686EB2B71E4FEEC9C79558712566BEC509AC6179',
                '0CE4CEFF785302961AAE2A33D6EB97E0D3E55C400B7798796F5BCD3571DB8698',
                '05B92EDBB21A0B261F2312DDFA0103E3761E25D785FAEE0E96F2CD69E3970C1D',
                '10410B8701C3D88190A102AF46147A4FF727C565D0AE9E670593C51532EFAA4F',
                '293DF18FB5E415A8A42F415E53E291BE2144355E6A8A3F06CD1890FF72683BEB',
                '15876B0C84FCEDA899A2D669480A28C432B3D44A4D9FE762E05B354A1BA8F736',
                'ED68EB36CC21B5378344CFDC7158B3AAD9C8CB004A898FD2105E5F5BB510D969',
                '40BB850114A38926404261C83BC6F451D961C3ADBDB0A8E97768061A086736C3',
                '077DD09F42EC4521017245EB1730398C1E208836EE2794DEA6A1623E4031E21B',
                'FE3C0E98F15884B44244DF3AFBBA3F97E52F99A793EC996D30EAA0251ABA75D0'
            )
        },
        [PSCustomObject]@{
            Source = (Join-Path $sourceScripts 'physics-diagnostics.js')
            Destination = (Join-Path $destinationScripts 'ball_lab\physics-diagnostics.js')
            KnownPriorSha256 = @(
                'C9271A5D1186C68852BC8C74500E5E94883AEBDAA1EF4FDDFCCF10F5F966CC91',
                'E847044360AD59AB3361DBBFCF18EA048147AF3B466C251118A4B1E08B3171B7'
            )
        },
        [PSCustomObject]@{
            Source = (Join-Path $sourceScripts 'layout.js')
            Destination = (Join-Path $destinationScripts 'ball_lab\layout.js')
            KnownPriorSha256 = @(
                'B958639408FB70010CC63C640CD0AC1A04AFFA32A738A2A231E4B4C7F985B2B7',
                '1BAC2D8435C40811BCA8173B09CAB9383FACDA97C652005E0021D1B4592D0EC6',
                '4AD346B0F15FB38832668EADE74D0DA7B8FBF643F05937729F7C24EA380E2092'
            )
        },
        [PSCustomObject]@{ Source = (Join-Path $sourceScripts 'core\goal.js'); Destination = (Join-Path $destinationScripts 'ball_lab\core\goal.js') },
        [PSCustomObject]@{
            Source = (Join-Path $sourceScripts 'core\kick.js')
            Destination = (Join-Path $destinationScripts 'ball_lab\core\kick.js')
            KnownPriorSha256 = @(
                'F8B60F64A0218C25DA7EBB151CE02FA841E6E17B511E3823B54AE2330CEF4A99',
                '970DEC769387969A1CD69CB70EE20B467E70436A0111FA41C91BB3BC164FF6FB',
                '8ED2C14F2BEA4A74D0701A282DFB21B76323B5AED3386AD5F8C0991DD8C73099'
            )
        },
        [PSCustomObject]@{
            Source = (Join-Path $sourceScripts 'core\cap.js')
            Destination = (Join-Path $destinationScripts 'ball_lab\core\cap.js')
            KnownPriorSha256 = @(
                '0E43BC60A1A136CC84A10553E28228F7DB0789FC8435E04FD0B10E743F4D0BDF',
                '8DCADE4825E9C3937C2E331B09E8774E5B088558FC2115A366FF19A4F648C664'
            )
        },
        [PSCustomObject]@{ Source = (Join-Path $sourceScripts 'core\match.js'); Destination = (Join-Path $destinationScripts 'ball_lab\core\match.js') },
        [PSCustomObject]@{ Source = (Join-Path $sourceScripts 'core\reset.js'); Destination = (Join-Path $destinationScripts 'ball_lab\core\reset.js') },
        [PSCustomObject]@{ Source = (Join-Path $sourceScripts 'core\vector.js'); Destination = (Join-Path $destinationScripts 'ball_lab\core\vector.js') },
        [PSCustomObject]@{
            Source = $apiSource
            Destination = (Join-Path $destinationScripts 'point_script.d.ts')
            KnownPriorSha256 = @(
                'DBB8AE95F12C6F513909A527609A8DF498AE5BB54A2024445A27537B33D61752'
            )
        },
        [PSCustomObject]@{ Source = $tsconfigSource; Destination = (Join-Path $destinationScripts 'tsconfig.json') }
    )

    foreach ($artifact in $artifacts) {
        if (-not (Test-Path -LiteralPath $artifact.Source -PathType Leaf)) {
            throw "Project source is missing: '$($artifact.Source)'."
        }
    }

    $dispositions = @($artifacts | ForEach-Object {
        Get-FileDisposition -Source $_.Source -Destination $_.Destination `
            -KnownPriorSha256 @($_.KnownPriorSha256)
    })
    foreach ($disposition in $dispositions) {
        Copy-AndVerify -Disposition $disposition
    }

    [PSCustomObject]@{
        AddonName = $AddonName
        ContentRoot = $resolvedContentAddon
        GameRoot = $resolvedGameAddon
        SeedMap = $seedMap
        MapName = $MapName
        MapSource = $destinationMap
        MapSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $destinationMap).Hash
        RuntimeAdapterSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $bundledAdapter).Hash
        PointScriptApiSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $apiSource).Hash
        Files = @($dispositions | Select-Object Destination, Sha256, Action)
        NextAction = "Open '$MapName.vmap' in Hammer, press F9, use Fast plus Load in Engine, and retain the complete build log."
    } | ConvertTo-Json -Depth 5
}
finally {
    $normalizedTempDirectory = [IO.Path]::GetFullPath($tempDirectory).TrimEnd('\')
    foreach ($temporaryFile in $temporaryFiles) {
        $normalizedTemporaryFile = [IO.Path]::GetFullPath($temporaryFile)
        if ((Split-Path -Parent $normalizedTemporaryFile).TrimEnd('\') -eq $normalizedTempDirectory -and
            (Split-Path -Leaf $normalizedTemporaryFile).StartsWith($tempPrefix) -and
            (Test-Path -LiteralPath $normalizedTemporaryFile -PathType Leaf)) {
            Remove-Item -LiteralPath $normalizedTemporaryFile -Force
        }
    }
}
