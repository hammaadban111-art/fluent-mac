# Opens every screen of the real Fluent.exe in every theme (--demo) and saves a picture of each.
#   pwsh scripts/screenshots.ps1 -Exe dist\win-x64\Fluent.exe -Out ci-out\screens
param([string]$Exe, [string]$Out, [string[]]$Themes = @("aurora", "porcelain", "obsidian", "ember", "lagoon", "classic"))
$ErrorActionPreference = "Continue"
New-Item -ItemType Directory -Force -Path $Out | Out-Null
$screens = @("terms", "onboarding", "dictate", "history", "style", "settings", "capsule-listening", "capsule-writing", "capsule-inserted", "capsule-error", "bubble")
$ok = 0; $bad = @()
foreach ($theme in $Themes) {
  $dir = Join-Path $Out $theme
  New-Item -ItemType Directory -Force -Path $dir | Out-Null
  foreach ($s in $screens) {
    $p = Start-Process -FilePath $Exe -ArgumentList "--demo", $s, "--theme", $theme, "--snap-dir", "`"$dir`"" -PassThru
    $ready = Join-Path $dir "$s.ready"; $err = Join-Path $dir "$s.error"
    $t = 0
    while (-not (Test-Path $ready) -and -not (Test-Path $err) -and $t -lt 150 -and -not $p.HasExited) { Start-Sleep -Milliseconds 100; $t++ }
    if ((Test-Path $ready) -and (Test-Path (Join-Path $dir "app-$s.png"))) { $ok++ } else { $bad += "$theme/$s" }
    if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
    Remove-Item $ready -ErrorAction SilentlyContinue
  }
}
"screens ok: $ok, missing: $($bad.Count) $($bad -join ', ')"
if ($bad.Count -gt 0) { exit 1 }
