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

# Not probes.
#
# The five capture scripts and TouchProbe need a real display; they refuse
# headless now (see test/Display.cs) rather than spinning a core forever, which
# is what they used to do. AutoPlay and BalanceSweep are balance instruments and
# take minutes each. HordePerf prints timings and "PERF DONE" — it has no pass
# or fail. BotDrive is a helper class with no SceneTree, so Godot cannot run it
# at all. Display is the guard itself.
$skip = @(
    'Display', 'TouchProbe', 'ScaleProbe', 'Screenshot', 'BaseShot',
    'DebriefShot', 'BillboardCompare', 'Presentation', 'BodyShot',
    'AutoPlay', 'BalanceSweep', 'HordePerf', 'BotDrive', 'Fresh', 'ModelReport'
)

$names = Get-ChildItem "$root\test\*.cs" |
         ForEach-Object { $_.BaseName } |
         Where-Object { $skip -notcontains $_ } |
         Sort-Object

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
