# Kiểm chứng chế độ MFT (R5) — CHẠY TRONG POWERSHELL "Run as administrator".
# Quét cả ổ bằng MFT và bằng chế độ thường, in số liệu hai bên cạnh nhau + bảng đối chiếu ổ, ghi vào _fixtures\mft-report-<ổ>.txt (UTF-8).
param([string]$Drive = "D:")
$ErrorActionPreference = "Stop"
$id = [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not (New-Object Security.Principal.WindowsPrincipal $id).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "Cần chạy trong PowerShell Administrator (Run as administrator)." -ForegroundColor Red; exit 4
}
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root "tools\FspVerify\bin\Release\net10.0-windows\FspVerify.exe"
if (-not (Test-Path $exe)) { dotnet build (Join-Path $root "tools\FspVerify") -c Release | Out-Null }
$report = Join-Path $root ("_fixtures\mft-report-" + $Drive.TrimEnd(':') + ".txt")
New-Item -ItemType Directory -Force (Split-Path -Parent $report) | Out-Null
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$lines = New-Object System.Collections.Generic.List[string]
& $exe mft $Drive 2>&1 | ForEach-Object { $s = "$_"; Write-Host $s; $lines.Add($s) }
[IO.File]::WriteAllLines($report, $lines, (New-Object Text.UTF8Encoding $true))
Write-Host "`nĐã ghi báo cáo: $report"
