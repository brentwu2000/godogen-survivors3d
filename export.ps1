# Exports the Windows build, and refuses to call a corrupt one done.
#
#   .\export.ps1
#
# Two things here are not ceremony.
#
# The clean. Godot's .NET export shells out to `dotnet publish` against
# `.godot/mono/temp/{bin,obj}/ExportRelease`, and MSBuild's incremental targets
# trust whatever is already sitting there. An export that is interrupted — a
# crash, a power cut, a killed terminal — can leave a file behind at the right
# length with nothing in it, and NTFS will hand back that length forever. The
# next export sees an output newer than its inputs, skips regenerating it, and
# copies the hole into the build. That is exactly how a 372-byte
# `Survivors3D.runtimeconfig.json` full of zeroes shipped: hostfxr refused it,
# no C# assembly loaded, and the game opened a window that did nothing.
#
# The verify. Every failure in that story passed a build. `dotnet` said it
# succeeded, the export said DONE, the file was on disk at a plausible size, and
# the only way to find out was to run it. So the last thing this script does is
# read the bytes back and check them, because a length is not content.
#
# The same applies to the assembly: without `Survivors3D.sln` the export prints
# an error per C# file and *finishes with exit code 0*, producing a game-less
# build. A green export is not evidence of an exported assembly.

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$exe = Join-Path $root 'build\survivors3d.exe'
$pck = Join-Path $root 'build\survivors3d.pck'
$data = Join-Path $root 'build\data_Survivors3D_windows_x86_64'

foreach ($stale in @(
    (Join-Path $root '.godot\mono\temp\bin\ExportRelease'),
    (Join-Path $root '.godot\mono\temp\obj\ExportRelease'),
    $data)) {
    if (Test-Path $stale) { Remove-Item $stale -Recurse -Force }
}

# Godot will not create the directory its export path points into. It fails with
# "the given export path does not exist" and a non-zero code, which is at least
# honest — but `build/` is gitignored, so a fresh clone never has one and the
# first export anybody runs after cloning would hit this.
$null = New-Item -ItemType Directory -Force -Path (Join-Path $root 'build')

Write-Host 'exporting...'

# Not under `-ErrorActionPreference Stop`. Godot writes a harmless "Unable to
# open Android 'build-tools' directory" to stderr on every export — there is an
# Android preset in the file and no SDK on this machine — and Windows PowerShell
# turns any native stderr line into a terminating error under Stop. The export
# would abort on a warning about a platform this script is not building.
$ErrorActionPreference = 'Continue'
& godot --headless --export-release 'Windows Desktop' 'build/survivors3d.exe'
$ErrorActionPreference = 'Stop'

$failures = @()

function Test-Head($path, [byte[]]$magic, $what) {
    if (-not (Test-Path $path)) { return "$what missing: $path" }
    $fs = [System.IO.File]::OpenRead($path)
    try {
        $head = New-Object byte[] $magic.Length
        $null = $fs.Read($head, 0, $magic.Length)
    } finally { $fs.Close() }
    for ($i = 0; $i -lt $magic.Length; $i++) {
        if ($head[$i] -ne $magic[$i]) { return "$what has the wrong magic — $path is not what it claims to be" }
    }
    return $null
}

# MZ, and then something in it. A DOS header on a file of zeroes would pass a
# magic check and fail to launch, so the body is sampled too.
$failures += Test-Head $exe ([byte[]](0x4D, 0x5A)) 'executable'
if (Test-Path $exe) {
    $fs = [System.IO.File]::OpenRead($exe)
    try {
        $sample = New-Object byte[] 65536
        $nonzero = 0
        foreach ($offset in 0, ($fs.Length / 2), ($fs.Length - 65536)) {
            $fs.Position = [Math]::Max(0, [int64]$offset)
            $read = $fs.Read($sample, 0, $sample.Length)
            for ($i = 0; $i -lt $read; $i++) { if ($sample[$i] -ne 0) { $nonzero++ } }
        }
    } finally { $fs.Close() }
    if ($nonzero -eq 0) { $failures += 'executable is the right size and entirely zeroes — the write was lost' }
}

# GDPC. Without the pack the exe boots into a black window and says nothing.
$failures += Test-Head $pck ([byte[]](0x47, 0x44, 0x50, 0x43)) 'pack'

# The runtime config, parsed rather than measured. This is the file that broke.
$config = Join-Path $data 'Survivors3D.runtimeconfig.json'
if (-not (Test-Path $config)) {
    $failures += "runtimeconfig missing: $config"
} else {
    try {
        $null = (Get-Content $config -Raw) | ConvertFrom-Json
    } catch {
        $failures += "runtimeconfig is not valid JSON — hostfxr will refuse it and no C# will run: $config"
    }
}

# The game's own assembly, which is the whole of the game.
if (-not (Test-Path (Join-Path $data 'Survivors3D.dll'))) {
    $failures += 'Survivors3D.dll missing from the data folder — is Survivors3D.sln still there?'
}

# The font's licence, inside the pack with the font.
#
# `export_filter="all_resources"` means *resources* — files Godot imports. A
# `.txt` is not one, so the first export after the UI font landed shipped
# `ui.otf` and left `OFL.txt` behind, which is a redistribution of an OFL face
# without the licence it requires. Nothing failed; the build was correct in every
# way this script already checked.
#
# Checked by reading the pack rather than the source tree, because the question
# is not "is the licence in the repository" — it was — but "did it reach the
# thing somebody is handed". `include_filter` in export_presets.cfg is what puts
# it there, and a filter is exactly the kind of line that gets cleared by an
# editor round-trip without anyone noticing.
if (Test-Path $pck) {
    $bytes = [System.IO.File]::ReadAllBytes($pck)
    $needle = [System.Text.Encoding]::UTF8.GetBytes('assets/fonts/OFL.txt')
    $found = $false
    for ($i = 0; $i -le $bytes.Length - $needle.Length -and -not $found; $i++) {
        if ($bytes[$i] -ne $needle[0]) { continue }
        $match = $true
        for ($j = 1; $j -lt $needle.Length; $j++) {
            if ($bytes[$i + $j] -ne $needle[$j]) { $match = $false; break }
        }
        $found = $match
    }
    if (-not $found) {
        $failures += 'assets/fonts/OFL.txt is not in the pack — the UI font ships under the SIL OFL and the licence has to travel with it. See include_filter in export_presets.cfg.'
    }
}

$failures = $failures | Where-Object { $_ }
if ($failures.Count -gt 0) {
    foreach ($failure in $failures) { Write-Host "FAIL  $failure" }
    exit 1
}

Write-Host "ok    $exe"
Write-Host "ok    $pck"
Write-Host "ok    $data"
