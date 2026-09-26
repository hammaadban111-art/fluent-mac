# Builds Fluent for Windows: tests, the self-contained Fluent.exe (x64 and arm64) and the installer.
#   pwsh scripts/build.ps1 -Version 1.0.0
# Output in dist/: win-x64/Fluent.exe, win-arm64/Fluent.exe, Fluent-Setup-<version>.exe, Fluent-<version>-portable-x64.zip
param([string]$Version = "1.0.0")
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

dotnet test tests/Fluent.Core.Tests -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw "unit tests failed" }

foreach ($rid in @("win-x64", "win-arm64")) {
  dotnet publish src/Fluent/Fluent.csproj -c Release -r $rid --self-contained true `
    -p:PublishSingleFile=true -p:Version=$Version -p:FileVersion="$Version.0" -o "dist/$rid" --nologo
  if ($LASTEXITCODE -ne 0) { throw "publish $rid failed" }
}

$iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") |
  Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { choco install innosetup -y --no-progress | Out-Null; $iscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" }
& $iscc /Q "/DAppVersion=$Version" "/DSourceExe=$root\dist\win-x64\Fluent.exe" installer\Fluent.iss
if ($LASTEXITCODE -ne 0) { throw "installer build failed" }

Compress-Archive -Force -Path dist/win-x64/Fluent.exe -DestinationPath "dist/Fluent-$Version-portable-x64.zip"
Get-ChildItem dist -Recurse -Include *.exe, *.zip | ForEach-Object { "{0,-60} {1,8:N1} MB" -f $_.FullName.Replace("$root\", ""), ($_.Length / 1MB) }
