# Đóng gói Folder Size Pro bản portable: FolderSizePro.exe (giao diện) + fsp.exe (dòng lệnh) trong một thư mục,
# kèm runtime .NET (self-contained) để chạy trên máy chưa cài .NET. Kết quả: _deliverables\FolderSizePro-<version>-win-x64.zip
param(
    [string]$Version = "1.0.0"
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "publish\FolderSizePro"
$deliver = Join-Path $root "_deliverables"

if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Force $out, $deliver | Out-Null

$common = @("-c", "Release", "-r", "win-x64", "--self-contained", "true", "-p:Version=$Version", "-p:DebugType=none", "-o", $out)
dotnet publish (Join-Path $root "src\FolderSizePro.App\FolderSizePro.App.csproj") @common
if ($LASTEXITCODE -ne 0) { throw "publish app thất bại" }
dotnet publish (Join-Path $root "src\FolderSizePro.Cli\FolderSizePro.Cli.csproj") @common
if ($LASTEXITCODE -ne 0) { throw "publish fsp thất bại" }

Copy-Item (Join-Path $root "README.md") $out
Copy-Item (Join-Path $root "README.vi.md") $out
Copy-Item (Join-Path $root "LICENSE") $out
Copy-Item (Join-Path $root "use-cases.md") $out

$zip = Join-Path $deliver "FolderSizePro-$Version-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path (Join-Path $out "*") -DestinationPath $zip -CompressionLevel Optimal
$size = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host "Xong: $zip ($size MB)"
