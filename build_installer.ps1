# 一键发布 + 制作安装包

$ErrorActionPreference = "Stop"

Write-Host "==> Step 1: 发布 Windows 自包含应用"
./build_windows.ps1 -Configuration Release

Write-Host "==> Step 2: 查找 Inno Setup 编译器"
# 默认安装路径（可按需调整）
$innoCandidates = @(
    "$Env:ProgramFiles(x86)\Inno Setup 6\ISCC.exe",
    "$Env:ProgramFiles\Inno Setup 6\ISCC.exe"
)
$iscc = $innoCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    throw "未找到 Inno Setup ISCC.exe，请先安装 Inno Setup 6"
}

Write-Host "==> Step 3: 编译安装包"
& $iscc ".\installer\installer.iss" | Write-Host

Write-Host "==> 完成。安装包输出目录：installer/Output"






