# Tạo bộ cài: publish (self-contained) → Inno Setup → _deliverables\FolderSizePro-Setup-<version>.exe
param(
    [string]$Version = "1.0.0"
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Không tìm thấy Inno Setup 6 (ISCC.exe)." }

& (Join-Path $PSScriptRoot "publish.ps1") -Version $Version
if ($LASTEXITCODE -ne 0) { throw "publish thất bại" }

& $iscc "/DAppVersion=$Version" "/Q" (Join-Path $root "installer\FolderSizePro.iss")
if ($LASTEXITCODE -ne 0) { throw "Inno Setup thất bại (mã $LASTEXITCODE)" }

$setup = Join-Path $root "_deliverables\FolderSizePro-Setup-$Version.exe"
Write-Host ("Xong: {0} ({1} MB)" -f $setup, [math]::Round((Get-Item $setup).Length / 1MB, 1))
