# PowerShell 脚本：在 Windows 上构建与发布 WPF 应用

param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

Write-Host "==> Restore"
dotnet restore "WeflyUpgradeTool/WeflyUpgradeTool.csproj"

Write-Host "==> Build ($Configuration)"
dotnet build "WeflyUpgradeTool/WeflyUpgradeTool.csproj" -c $Configuration

Write-Host "==> Publish self-contained x64"
dotnet publish "WeflyUpgradeTool/WeflyUpgradeTool.csproj" -c $Configuration -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true

$publishDir = Join-Path "WeflyUpgradeTool" "bin/$Configuration/net8.0-windows/win-x64/publish"
Write-Host "发布目录: $publishDir"

# 拷贝 7z.exe/7z.dll（若存在）
$toolsZipDir = Join-Path (Get-Location) "Tools/7zip"
if (Test-Path (Join-Path $toolsZipDir "7z.exe")) {
    Copy-Item (Join-Path $toolsZipDir "7z.exe") $publishDir -Force
}
if (Test-Path (Join-Path $toolsZipDir "7z.dll")) {
    Copy-Item (Join-Path $toolsZipDir "7z.dll") $publishDir -Force
}

# 拷贝 CH341SER 驱动目录，方便分发
$driverSrc = Join-Path (Get-Location) "CH341SER"
if (Test-Path $driverSrc) {
    Copy-Item $driverSrc (Join-Path $publishDir "CH341SER") -Recurse -Force
}

Write-Host "完成。可分发目录：$publishDir"

