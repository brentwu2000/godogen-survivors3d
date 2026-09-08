# Takes a downloaded model from a file on disk to a body in the game.
#
#   powershell art-src/models/intake.ps1 -Model ~/Downloads/thing.glb -Slot walker
#   powershell art-src/models/intake.ps1 -Model ~/Downloads/pack.glb  -Slot brute `
#       -Node rig_CharRoot007 -Pose "Take 001@1.0" -Yaw 180
#
# ART.md section 7 lists these steps and every one of them exists because
# something failed without it. This is that list, in order, with the two
# arguments nobody gets right on the first try taken out of the caller's hands:
# the destination path and the height. Both come from the slot.
#
# It stops at the first step that fails, and it prints the intake checklist from
# ART.md section 8 at the end with what it measured already filled in. What it
# cannot fill in is the licence, which is why those two lines are left blank
# rather than omitted.
#
# The one thing this does not do is decide. Step 4 writes a lineup shot and step
# 5 a screenshot of the running game, and looking at both is the whole intake.
# Two rounds of authored humanoids entered this game and were deleted, and the
# lineup is what caught them both, late, because nobody took it.

[CmdletBinding()]
param(
    # The .glb as downloaded. Anywhere on disk: it is copied into assets/models/
    # if it is not there already, because the importer only sees res://.
    [Parameter(Mandatory = $true)][string] $Model,

    # Which body this is. One of the nine horde variants or the three survivors.
    [Parameter(Mandatory = $true)][string] $Slot,

    # A character inside a pack. "A pack is not a model": the Polyart set is ten
    # zombies and a floor tile in one file, and without this the baker merges all
    # of it and refuses for having several skeletons. Names come from step 2's
    # tree, and the character is the node that CONTAINS a skeleton.
    [string] $Node = "",

    # An animation, and optionally a time in it: "Take 001@1.0". A rigged model's
    # bind pose is a T-pose, and a T-pose is not a body.
    [string] $Pose = "",

    # Degrees about Y. This game's bodies face -Z; a model facing +Z walks at you
    # backwards, which reads as a rig bug rather than as a sign convention.
    [double] $Yaw = 0,

    # rrggbb per surface, in surface order. Only for a model that arrived white:
    # anything with a palette texture is sampled per vertex and does not want
    # this.
    [string] $Tint = "",

    # Leg swing, arm swing and bob, in the baker's own order. The defaults are
    # the walker's and everything is read against the walker.
    [double] $Swing = 0.55,
    [double] $ArmSwing = 0.30,
    [double] $Bob = 0.035,

    # Bake a model that is over its tier's triangle budget anyway. The budget is
    # a horde number and the check prints the decimate command; a survivor or a
    # boss is one body on screen and this is the flag that says so.
    [switch] $Force,

    # Skip the two capture steps. For a re-bake of something already judged --
    # not for a first intake, where the captures ARE the intake.
    [switch] $NoShots
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Set-Location $root

if (-not (Get-Command godot -ErrorAction SilentlyContinue)) {
    Write-Host "godot is not on PATH." -ForegroundColor Red
    exit 1
}

# Every Godot run goes through cmd, and the reason is PowerShell 5.1 rather than
# Godot. Redirecting a native command's stderr inside PowerShell wraps each line
# in a NativeCommandError, which under ErrorActionPreference = Stop kills the
# script on the first benign warning -- and Godot's first line on this machine is
# "Unable to open Android 'build-tools' directory", which is not about anything.
# Merging the streams inside cmd hands PowerShell one clean stdout.
#
# Quoted per argument rather than by the caller, because a pose is "Take 001@1.0"
# and an unquoted space there silently becomes two arguments and a bake in a
# T-pose.
function Godot([string[]] $Arguments) {
    $quoted = $Arguments | ForEach-Object {
        if ($_ -match '\s') { '"' + $_ + '"' } else { $_ }
    }

    & cmd /c ('godot ' + ($quoted -join ' ') + ' 2>&1')
}

# On screen at once, and the ceiling each tier can afford, from ART.md section 2.
# Cost is triangles times instances and there is no skinning, animation or
# per-enemy node cost, so this table is the whole performance model.
$tiers = @{
    walker  = @{ OnScreen = 150; Budget = 2000 }
    runner  = @{ OnScreen = 150; Budget = 2000 }
    spitter = @{ OnScreen = 150; Budget = 2000 }
    stalker = @{ OnScreen = 150; Budget = 2000 }
    bloater = @{ OnScreen = 150; Budget = 2000 }
    bulwark = @{ OnScreen = 150; Budget = 2000 }
    lantern = @{ OnScreen = 150; Budget = 2000 }
    brute   = @{ OnScreen =  20; Budget = 6000 }
    boss    = @{ OnScreen =   2; Budget = 40000 }
    # The survivors' ceiling is the size of the committed bake, not the frame
    # time. 27,488 triangles and 51,353 measure 1.30 ms and 1.32 as the player,
    # which is noise; their .res files are 1.6 MB and 2.9 MB, which is not. See
    # ART.md section 2.
    drifter = @{ OnScreen =   1; Budget = 120000 }
    courier = @{ OnScreen =   1; Budget = 120000 }
    warden  = @{ OnScreen =   1; Budget = 120000 }
}

$slot = $Slot.ToLower()
if (-not $tiers.ContainsKey($slot)) {
    Write-Host "'$Slot' is not a body slot. One of:" -ForegroundColor Red
    Write-Host ("  " + (($tiers.Keys | Sort-Object) -join ', '))
    exit 1
}

$survivor = @('drifter', 'courier', 'warden') -contains $slot

function Step($n, $what) {
    Write-Host ""
    Write-Host "[$n] $what" -ForegroundColor Cyan
}

function Fail($why) {
    Write-Host ""
    Write-Host "intake stopped: $why" -ForegroundColor Red
    exit 1
}

# --- 0. Into the project, and hashed on the way -----------------------------
#
# The sha256 is printed rather than written anywhere. assets/models/SOURCE.md is
# the register and it wants a URL beside the hash, and only the person who
# downloaded the file has that.

Step 0 "Into assets/models/, and hashed"

if (-not (Test-Path $Model)) { Fail "no such file: $Model" }

$source = Get-Item $Model
$inTree = Join-Path $root "assets\models\$($source.Name)"

if ($source.FullName -ne $inTree) {
    Copy-Item $source.FullName $inTree -Force
    Write-Host "  copied to assets/models/$($source.Name)"
} else {
    Write-Host "  already in assets/models/"
}

$hash = (Get-FileHash $inTree -Algorithm SHA256).Hash.ToLower()
$size = [math]::Round((Get-Item $inTree).Length / 1MB, 2)
Write-Host "  sha256 $hash"
Write-Host "  $size MB"

# Whether the register already knows this file. Checked by hash rather than by
# name, because a re-download under a different name is the same asset and a
# different file under the same name is not -- and the checklist at the end
# would otherwise report a properly registered pack as NOT RECORDED and teach
# whoever reads it to ignore that line.
$registered = $false
$sourceMd = Join-Path $root "assets\models\SOURCE.md"
if (Test-Path $sourceMd) {
    $registered = (Select-String -Path $sourceMd -Pattern $hash -SimpleMatch -Quiet) -eq $true
}

if ($registered) {
    Write-Host "  already in assets/models/SOURCE.md by hash" -ForegroundColor Green
} else {
    Write-Host "  not in assets/models/SOURCE.md -- see the checklist at the end" -ForegroundColor Yellow
}

$resPath = "res://assets/models/$($source.Name)"

# --- 1. Import ---------------------------------------------------------------
#
# A .glb with no .import file does not load and the error is a C# stack trace
# about ResourceLoader rather than anything about importing. First step, every
# time, because it is the first thing that goes wrong.

Step 1 "Import"
Godot @('--headless', '--import') | Select-String -Pattern 'reimport|ERROR: Cannot' | ForEach-Object { Write-Host "  $_" }

# --- 2. What is actually in the file ----------------------------------------

Step 2 "ModelReport"
$reportLog = Join-Path $root "screenshots\_intake_$slot.txt"
$report = Godot @('--headless', '--script', 'test/ModelReport.cs', '--', $resPath, 'tree')
$report | Out-File -FilePath $reportLog -Encoding utf8

$totals = $report | Select-String -Pattern '^(meshes|triangles|surfaces|skeletons|skinned meshes|animation players|bounds|verdict) '
if (-not $totals) { Fail "ModelReport said nothing about $resPath -- see $reportLog" }
$totals | ForEach-Object { Write-Host "  $_" }

$triangleLine = @($report | Select-String -Pattern '^triangles\s+(\d+)')
if ($triangleLine.Count -eq 0) { Fail "ModelReport printed no triangle count -- see $reportLog" }
$triangles = [int] $triangleLine[0].Matches[0].Groups[1].Value

$skeletons = 0
$skeletonLine = @($report | Select-String -Pattern '^skeletons\s+(\d+)')
if ($skeletonLine.Count -gt 0) { $skeletons = [int] $skeletonLine[0].Matches[0].Groups[1].Value }

if ($skeletons -gt 1 -and $Node -eq "") {
    Write-Host ""
    Write-Host "  $skeletons skeletons and no -Node. This is a pack, not a model." -ForegroundColor Yellow
    Write-Host "  The characters are the nodes that CONTAIN a Skeleton3D:"
    $report | Select-String -Pattern 'Skeleton3D \(Skeleton3D\)' -Context 2, 0 |
        ForEach-Object { $_.Context.PreContext | Where-Object { $_ -match '\S' } | ForEach-Object { Write-Host "    $_" } }
    Write-Host "  Tree written to screenshots/_intake_$slot.txt"
    Fail "pass -Node <the rig node for one character>"
}

# --- 3. The budget ----------------------------------------------------------
#
# Triangles times instances, and the note is per tier because the answer is
# completely different for a horde variant and for the player. A 24,000-triangle
# body is forty times over budget in the horde and free as a survivor.
#
# **A pack's total describes the file and no draw call ever pays it.** The
# Polyart set is ten zombies in one .glb: 18,840 triangles, which is nine times
# the horde ceiling and refuses an intake that would have been fine, because the
# body being baked is one of the ten at 1,650. With -Node given, the count that
# matters is the subtree's.

function SubtreeTriangles($lines, $node) {
    $indent = -1
    $sum = 0

    foreach ($line in $lines) {
        $text = [string] $line

        if ($indent -lt 0) {
            if ($text -match "^(\s*)$([regex]::Escape($node))\s*\(") {
                $indent = $Matches[1].Length
            }

            continue
        }

        if ($text.Trim() -eq '') { continue }

        # Back out to the node's own level or shallower: the subtree is over.
        $here = $text.Length - $text.TrimStart().Length
        if ($here -le $indent) { break }

        if ($text -match '(\d+) tris') { $sum += [int] $Matches[1] }
    }

    return $sum
}

Step 3 "Budget"
$tier = $tiers[$slot]

if ($Node -ne "") {
    $subtree = SubtreeTriangles $report $Node

    if ($subtree -le 0) {
        Fail "no node called $Node in $resPath -- see $reportLog for the tree"
    }

    Write-Host "  $Node is $subtree of the file's $triangles triangles"
    $triangles = $subtree
}

$total = $triangles * $tier.OnScreen
Write-Host ("  $slot draws {0} on screen: {1:N0} x {0} = {2:N0} triangles" -f $tier.OnScreen, $triangles, $total)

if ($triangles -gt $tier.Budget) {
    Write-Host "  over the tier ceiling of $($tier.Budget)" -ForegroundColor Yellow
    Write-Host "  decimate first (skeleton and skins survive; the cost is silhouette detail):"
    Write-Host "    blender --background --python art-src/models/decimate.py -- ``"
    Write-Host "      assets/models/$($source.Name) assets/models/$($slot)_lowpoly.glb $($tier.Budget)"
    if (-not $Force) { Fail "over budget; decimate, or pass -Force if this tier can afford it" }
    Write-Host "  -Force given, baking anyway" -ForegroundColor Yellow
} else {
    Write-Host "  within the tier ceiling of $($tier.Budget)" -ForegroundColor Green
}

# --- 4. Bake ----------------------------------------------------------------
#
# slot: rather than a path and a height. BakeBody resolves both from the slot's
# own resource, so the destination is where BodyBakes looks and the height is
# what the enemy table or the survivor roster says -- never what the model
# measures, which is not a design decision.

Step 4 "Bake"
$bake = @($resPath, "slot:$slot", $Swing, $ArmSwing, $Bob)
if ($Tint -ne "") { $bake += $Tint }
if ($Node -ne "") { $bake += "node:$Node" }
if ($Pose -ne "") { $bake += "pose:$Pose" }
if ($Yaw -ne 0)   { $bake += "yaw:$Yaw" }

Write-Host "  godot --headless --script scripts/tools/BakeBody.cs -- $($bake -join ' ')"
$baked = Godot (@('--headless', '--script', 'scripts/tools/BakeBody.cs', '--') + $bake)
$baked | Select-String -Pattern 'slot |skipped|surface \d|merged into|hip at|bones:|% of vertices|triangles,|-> res|ERROR|refus' |
    ForEach-Object { Write-Host "  $_" }

$bakePath = Join-Path $root "resources\bodies\$slot.res"
if (-not (Test-Path $bakePath)) { Fail "no bake at resources/bodies/$slot.res" }

Write-Host ("  wrote resources/bodies/$slot.res, {0} KB" -f [math]::Round((Get-Item $bakePath).Length / 1KB))

# Nothing else has to change. BodyBakes reads the shelf by slot name, so the
# body is in the game from here -- no BakedBodyPath, no rebuild of the resource
# tables, no code edit at all.
Write-Host "  in the game: BodyBakes picks res://resources/bodies/$slot.res up by name" -ForegroundColor Green

if ($NoShots) {
    Write-Host ""
    Write-Host "-NoShots given; skipping the lineup and the screenshot." -ForegroundColor Yellow
    exit 0
}

# --- 5. Look at it next to what it is replacing ------------------------------
#
# This is the decision and it is not ceremony. Every judgement made about a model
# viewed on its own has been wrong: scale, proportion and palette only mean
# anything side by side.

Step 5 "Lineup"
#
# baked: as well as the slot, and this was wrong first: BodyShot draws the
# procedural body for a named variant and the bake only when it is handed one.
# A lineup of the thing being replaced, captioned as the replacement, is worse
# than no lineup -- so both stand in the row and the picture is the comparison.
$shelved = "baked:res://resources/bodies/$slot.res"

if ($survivor) {
    $shot = @('roster', 'front', $shelved)
} else {
    # raw as well: BodyShot draws the shelf for a named variant now, so without
    # it the candidate would stand next to itself.
    $shot = @("one:$slot", 'raw', 'front', $shelved)
}

Godot (@('--script', 'test/BodyShot.cs', '--') + $shot) | Select-String -Pattern 'Wrote|ERROR' | ForEach-Object { Write-Host "  $_" }
Copy-Item (Join-Path $root "screenshots\bodies.png") (Join-Path $root "screenshots\_intake_$slot.png") -Force
Write-Host "  screenshots/_intake_$slot.png"

Step 6 "The running game"
Godot @('--script', 'test/Screenshot.cs', '--', 'mixed') | Select-String -Pattern '^bodies:|Wrote|ERROR' | ForEach-Object { Write-Host "  $_" }
Copy-Item (Join-Path $root "screenshots\main.png") (Join-Path $root "screenshots\_intake_$($slot)_game.png") -Force
Write-Host "  screenshots/_intake_$($slot)_game.png"

# --- The checklist ----------------------------------------------------------

Write-Host ""
Write-Host "ART.md section 8, filled in as far as a script can:" -ForegroundColor Cyan
Write-Host ""
if ($registered) {
    Write-Host "| Name and author           | $($source.Name) -- registered, see SOURCE.md"
    Write-Host "| URL                       | in SOURCE.md"
    Write-Host "| Licence, verbatim         | in SOURCE.md"
    Write-Host "| Commit the source?        | already decided; see .gitignore"
} else {
    Write-Host "| Name and author           | $($source.Name) / NOT RECORDED"
    Write-Host "| URL                       | NOT RECORDED"
    Write-Host "| Licence, verbatim         | NOT RECORDED"
    Write-Host "| Commit the source?        | decide from the licence -- ART.md section 6"
}
Write-Host "| sha256                    | $hash"
Write-Host "| Size                      | $size MB"
Write-Host "| Triangles                 | $triangles"
Write-Host "| Skeletons in the file     | $skeletons"
Write-Host "| Slot / tier               | $slot, $($tier.OnScreen) on screen, ceiling $($tier.Budget)"
Write-Host "| Total on screen           | $('{0:N0}' -f $total) triangles"
Write-Host ""
Write-Host "The three fields a script cannot fill are the three that decide whether the"
Write-Host "file may be committed. assets/models/SOURCE.md is the register; an entry with"
Write-Host "the licence blank means the .glb goes in .gitignore and only the bake is"
Write-Host "committed, which is what every licence in section 6 permits and none forbid."
Write-Host ""
Write-Host "Then look at the two pictures. That is the intake." -ForegroundColor Cyan
