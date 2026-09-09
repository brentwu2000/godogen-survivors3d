# Runs every headless probe and reports the ones that did not pass.
#
#   pwsh test/sweep.ps1
#
# The sweep existed as a habit and not as a file, which is how it kept being run
# by hand against a list from memory. Two probes were missed for a whole phase
# that way.
#
# A probe passes by printing "PROBE OK". Nothing here parses further: a probe
# that crashes, hangs its stage machine, or exits before its last stage does not
# print it, and every one of those is a failure worth stopping for.

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

# Not probes, listed by hand because nothing in the source distinguishes them.
#
# AutoPlay and BalanceSweep are balance instruments and take minutes each.
# HordePerf prints timings and "PERF DONE" — it has no pass or fail. BotDrive and
# Fresh are helper classes with no SceneTree, so Godot cannot run them at all.
# ModelReport describes a `.glb` and answers no question on its own. Display is
# the guard itself.
$skip = @(
    # RouteMemory joined Fresh and BotDrive as a plain helper class with no
    # SceneTree, and was not added here when it arrived — so the sweep tried to
    # run it, Godot answered "the associated class could not be found", and the
    # result read as a failing probe rather than as a file that is not one.
    'Display', 'Fresh', 'BotDrive', 'RouteMemory', 'ModelReport',
    'AutoPlay', 'BalanceSweep', 'HordePerf',

    # DeckMatrix fires every weapon under every growth option — 276 trials of
    # four seconds — and takes about a quarter of an hour. It is a balance
    # instrument on the same footing as BalanceSweep, and it lives here for the
    # same reason: a sweep somebody stops running because it takes twenty minutes
    # is worth less than a sweep that runs.
    #
    # It answers a question that only changes when the *content* changes, so run
    # it when a weapon is added, a growth option is added, or a trait changes what
    # a weapon consumes. WEAPONS.md says so too, which is the second place to
    # forget it — hence this note being specific about the trigger rather than
    # just saying "slow".
    'DeckMatrix',

    # TouchProbe needs a display server that dispatches GUI input, and the
    # headless one does not.
    #
    # This entry said the opposite for several phases — "runs headless perfectly
    # well, on this list since before there was a reason written down for it" —
    # and the reason it gave for leaving it alone was that finding out would cost
    # more than the probe is worth. It cost one run. Headless it does not hang and
    # it does not pass: three of its five stages fail, because a synthetic finger
    # on the stick moves the player 0.00 m, a button that should be enabled is
    # not, and a tap on a level-up card changes no pick. The two that do pass are
    # the ones asking about wiring rather than about input.
    #
    # So it stays skipped, for a reason that is now measured rather than assumed —
    # and the README entry saying it needs a real display was right all along.
    'TouchProbe'
)

# The capture scripts, found rather than listed.
#
# **A name missing from a hand-kept list is not a caught error — it is a sweep
# that hangs.** A capture script run headless spins at 100% of a core forever
# printing nothing (see test/Display.cs), and a probe that has hung looks exactly
# like a probe that is slow. `PropShot` was added to this folder and not to the
# list, and two sweeps died at the entry alphabetically before it while being
# read as "the long run probes are slow".
#
# So the list is derived: anything that calls `Display.Required` has *said* it
# needs a display, and that declaration is in the file where somebody writing a
# capture script cannot forget it. One place to get right instead of two.
$needsDisplay = Get-ChildItem "$root\test\*.cs" |
                Where-Object { (Get-Content $_.FullName -Raw) -match 'Display\.Required' } |
                ForEach-Object { $_.BaseName }

$names = Get-ChildItem "$root\test\*.cs" |
         ForEach-Object { $_.BaseName } |
         Where-Object { $skip -notcontains $_ -and $needsDisplay -notcontains $_ } |
         Sort-Object

Write-Output "skipping $($needsDisplay.Count) capture script(s): $($needsDisplay -join ', ')"
Write-Output ""

$failed = @()

foreach ($name in $names) {
    $output = & godot --headless --script "test/$name.cs" 2>&1 | Out-String

    if ($output -match 'PROBE OK') {
        Write-Output "  ok    $name"
    }
    else {
        Write-Output "  FAIL  $name"
        $failed += $name
    }
}

Write-Output ''

if ($failed.Count -eq 0) {
    Write-Output "sweep clean: $($names.Count) probes"
    exit 0
}

Write-Output "sweep FAILED: $($failed -join ', ')"
Write-Output ''
Write-Output 'Re-run one on its own to see its stages:'
Write-Output "  godot --headless --script test/$($failed[0]).cs"
exit 1
