# 灵动岛 安装脚本（自定义目录 · 不写注册表 · 不写 C 盘）
#
# 用法（任选其一）：
#   powershell -ExecutionPolicy Bypass -File .\install.ps1 -InstallDir "E:\DynamicIsland"
#   或右键「使用 PowerShell 运行」后按提示输入目录（默认 E:\DynamicIsland）
#
# 本脚本先调用 build.ps1 编译最新源码，再把文件复制到指定目录、迁移旧电量缓存、启动程序。
# 不写任何注册表项，不创建启动项，不在 C 盘落文件（数据/缓存都在 <安装目录>\data 里）。

param(
    [string]$InstallDir = "",
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$src = Split-Path -Parent $MyInvocation.MyCommand.Path

if ([string]::IsNullOrWhiteSpace($InstallDir)) {
    $InstallDir = Read-Host '安装目录（例 E:\DynamicIsland，不要选 C 盘）'
}
if ([string]::IsNullOrWhiteSpace($InstallDir)) { throw '没有指定安装目录' }

# 统一成绝对路径，并且拒绝装到 C 盘（用户要求不在 C 盘写入）
if (-not [System.IO.Path]::IsPathRooted($InstallDir)) { $InstallDir = Join-Path (Get-Location).Path $InstallDir }
$InstallDir = [System.IO.Path]::GetFullPath($InstallDir)
$drive = [System.IO.Path]::GetPathRoot($InstallDir)
if ($drive -ieq $env:SystemDrive + '\') {
    throw "安装目录在系统盘（$drive）。按要求不写 C 盘，请换一个盘，例如 E:\DynamicIsland"
}
if ($InstallDir.TrimEnd('\') -eq $drive.TrimEnd('\')) {
    throw "安装目录不能为盘符根目录（$InstallDir），请指定具体子目录，例如 E:\DynamicIsland"
}
if ($InstallDir.TrimEnd('\') -eq $env:SystemDrive) { throw '安装目录在系统盘根目录，请换盘' }

Write-Host "安装到：$InstallDir"
if (-not $Force -and (Test-Path -LiteralPath (Join-Path $InstallDir '灵动岛.exe'))) {
    $ans = Read-Host '该目录已有灵动岛，覆盖？(Y/n)'
    if ($ans -eq 'n' -or $ans -eq 'N') { throw '已取消' }
}

# 1) 先停止可能正在运行的目标实例，避免文件被锁定导致覆盖复制失败
Get-Process -Name 灵动岛 -ErrorAction SilentlyContinue | ForEach-Object {
    try {
        Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
    } catch {}
}
Start-Sleep -Milliseconds 600

# 1.5) 自动编译最新源码，确保安装的 EXE 包含最新修复
$buildScript = Join-Path $src 'build.ps1'
if (Test-Path -LiteralPath $buildScript) {
    Write-Host "正在编译最新源码..."
    & $buildScript
}

New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null

# 2) 复制程序并记录安装清单（不复制安装/卸载脚本，生成清单以便卸载时精确清理）
$manifest = [System.Collections.Generic.List[string]]::new()
Get-ChildItem -LiteralPath $src -Force | Where-Object {
    $_.Name -ne 'install.ps1' -and 
    $_.Name -ne 'uninstall.ps1' -and 
    -not $_.Name.StartsWith('.island_') -and
    -not $_.Name.StartsWith('.test_') -and
    $_.FullName -ine $InstallDir -and
    -not $_.FullName.StartsWith($InstallDir + [System.IO.Path]::DirectorySeparatorChar)
} | ForEach-Object {
    $relName = $_.Name
    Copy-Item -LiteralPath $_.FullName -Destination $InstallDir -Recurse -Force
    $manifest.Add($relName)
}

# 写入应用归属标记与清单文件
Set-Content -LiteralPath (Join-Path $InstallDir '.island_installed') -Value "DynamicIsland Installation Marker - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -Encoding UTF8
Set-Content -LiteralPath (Join-Path $InstallDir '.island_manifest') -Value ($manifest -join "`r`n") -Encoding UTF8

# 3) 数据目录：<安装目录>\data（缓存和临时日志都放这里，绝不写 C 盘）
$dataDir = Join-Path $InstallDir 'data'
New-Item -ItemType Directory -Force -Path $dataDir | Out-Null

# 4) 迁移旧版在 C 盘的缓存（若存在，静默容错不中断安装）
try {
    $oldCacheDir = Join-Path $env:LOCALAPPDATA 'DynamicIsland'
    $oldCache = Join-Path $oldCacheDir 'btbattery.txt'
    $newCache = Join-Path $dataDir 'btbattery.txt'
    if ((Test-Path -LiteralPath $oldCache -ErrorAction SilentlyContinue) -and -not (Test-Path -LiteralPath $newCache -ErrorAction SilentlyContinue)) {
        Copy-Item -LiteralPath $oldCache -Destination $newCache -Force -ErrorAction SilentlyContinue
        Write-Host "已迁移旧电量缓存：$oldCache → $newCache"
    }
} catch { }

# 5) 清掉旧版在 C 盘的安装（程序目录 / 缓存目录 / 旧的注册表自启 / 旧快捷方式）
try {
    $oldProgram = Join-Path $env:LOCALAPPDATA 'Programs\DynamicIsland'
    if (Test-Path -LiteralPath $oldProgram -ErrorAction SilentlyContinue) {
        Remove-Item -LiteralPath $oldProgram -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "已移除旧安装：$oldProgram"
    }
    if ($oldCacheDir -and (Test-Path -LiteralPath $oldCacheDir -ErrorAction SilentlyContinue)) {
        Remove-Item -LiteralPath $oldCacheDir -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "已移除旧缓存目录：$oldCacheDir"
    }
    Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'DynamicIsland' -ErrorAction SilentlyContinue
    foreach ($lnk in @(
        (Join-Path ([Environment]::GetFolderPath('Desktop')) '灵动岛.lnk'),
        (Join-Path ([Environment]::GetFolderPath('StartMenu')) 'Programs\灵动岛.lnk')
    )) {
        if (Test-Path -LiteralPath $lnk -ErrorAction SilentlyContinue) { 
            Remove-Item -LiteralPath $lnk -Force -ErrorAction SilentlyContinue
            Write-Host "已删除旧快捷方式：$lnk" 
        }
    }
} catch { }

# 6) 启动
$exe = Join-Path $InstallDir '灵动岛.exe'
Start-Process -FilePath $exe -WorkingDirectory $InstallDir
Write-Host ""
Write-Host "完成。程序在 $InstallDir，数据在 $dataDir。"
Write-Host "不写注册表、不设自启——开机后手动双击「$exe」即可。"
Write-Host "卸载：powershell -ExecutionPolicy Bypass -File .\uninstall.ps1 -InstallDir `"$InstallDir`""
