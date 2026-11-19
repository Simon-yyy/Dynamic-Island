# 灵动岛 卸载脚本（自定义目录 · 不写注册表）
#
# 用法：powershell -ExecutionPolicy Bypass -File .\uninstall.ps1 -InstallDir "E:\DynamicIsland"
#       不带参数时按提示输入目录；同时会清理旧版可能残留在 C 盘的自启项（只做删除，不新增）。

param(
    [string]$InstallDir = "",
    [switch]$KeepData
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($InstallDir)) {
    $InstallDir = Read-Host '安装目录（例 E:\DynamicIsland）'
}
if ([string]::IsNullOrWhiteSpace($InstallDir)) { throw '没有指定安装目录' }

# 规范化路径与安全路径守卫
$InstallDir = [System.IO.Path]::GetFullPath($InstallDir)
$rootPath = [System.IO.Path]::GetPathRoot($InstallDir)
if ($InstallDir.TrimEnd('\') -eq $rootPath.TrimEnd('\')) {
    throw "拒绝卸载：目标路径为盘符根目录（$InstallDir），操作已中止以防止系统数据丢失！"
}

# 拒绝关键系统路径
$sysDirs = @(
    $env:SystemRoot,
    $env:ProgramFiles,
    ${env:ProgramFiles(x86)},
    $env:UserProfile,
    [Environment]::GetFolderPath('Desktop')
)
foreach ($sd in $sysDirs) {
    if ($sd -and ($InstallDir.TrimEnd('\') -ieq [System.IO.Path]::GetFullPath($sd).TrimEnd('\'))) {
        throw "拒绝卸载：目标路径为系统核心目录（$InstallDir），操作已中止！"
    }
}

if (-not (Test-Path -LiteralPath $InstallDir)) {
    Write-Host "目标目录不存在：$InstallDir"
    return
}

# 检查归属标记与核心文件，防止误删无关目录
$markerFile = Join-Path $InstallDir '.island_installed'
$progExe = Join-Path $InstallDir '灵动岛.exe'
if (-not (Test-Path -LiteralPath $markerFile) -and -not (Test-Path -LiteralPath $progExe)) {
    throw "安全校验失败：$InstallDir 中未检测到灵动岛安装标记或主程序，拒绝执行删除以保护无关文件！"
}

# 1) 停止运行中程序
Get-Process -Name 灵动岛 -ErrorAction SilentlyContinue | ForEach-Object {
    try {
        if ($_.Path -and $_.Path.StartsWith($InstallDir, [System.StringComparison]::OrdinalIgnoreCase)) {
            Stop-Process -Id $_.Id -Force
        }
    } catch {
        Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
    }
}
Start-Sleep -Milliseconds 600

# 2) 顺手清掉旧版本留在 C 盘的自启项和快捷方式（不新增任何注册表项，静默容错）
try {
    Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'DynamicIsland' -ErrorAction SilentlyContinue
    foreach ($lnk in @(
        (Join-Path ([Environment]::GetFolderPath('Desktop')) '灵动岛.lnk'),
        (Join-Path ([Environment]::GetFolderPath('StartMenu')) 'Programs\灵动岛.lnk')
    )) {
        if (Test-Path -LiteralPath $lnk -ErrorAction SilentlyContinue) { Remove-Item -LiteralPath $lnk -Force -ErrorAction SilentlyContinue }
    }
    $oldProgram = Join-Path $env:LOCALAPPDATA 'Programs\DynamicIsland'
    if (Test-Path -LiteralPath $oldProgram -ErrorAction SilentlyContinue) { Remove-Item -LiteralPath $oldProgram -Recurse -Force -ErrorAction SilentlyContinue }
    if (-not $KeepData) {
        $oldCacheDir = Join-Path $env:LOCALAPPDATA 'DynamicIsland'
        if (Test-Path -LiteralPath $oldCacheDir -ErrorAction SilentlyContinue) { Remove-Item -LiteralPath $oldCacheDir -Recurse -Force -ErrorAction SilentlyContinue }
    }
} catch { }

# 3) 按清单安全清理应用专属文件
$manifestFile = Join-Path $InstallDir '.island_manifest'
$ownedItems = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)

if (Test-Path -LiteralPath $manifestFile) {
    Get-Content -LiteralPath $manifestFile -Encoding UTF8 | ForEach-Object {
        $t = $_.Trim()
        if ($t) { [void]$ownedItems.Add($t) }
    }
} else {
    # 兼容没有 manifest 的旧版默认文件列表
    $defaultFiles = @('灵动岛.exe', 'island.ico', 'build.ps1', 'BtBattery.cs', 'BtBattery.exe', 'src', 'winmd', 'screenshots', 'README.md', '导出日志（发给对方）.bat')
    foreach ($df in $defaultFiles) { [void]$ownedItems.Add($df) }
}

# 清理清单文件
foreach ($item in $ownedItems) {
    if ($KeepData -and ($item -ieq 'data')) { continue }
    $p = Join-Path $InstallDir $item
    if (Test-Path -LiteralPath $p) {
        Remove-Item -LiteralPath $p -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "已删除受管项：$item"
    }
}

# 清理标记文件自身
if (Test-Path -LiteralPath $markerFile) { Remove-Item -LiteralPath $markerFile -Force -ErrorAction SilentlyContinue }
if (Test-Path -LiteralPath $manifestFile) { Remove-Item -LiteralPath $manifestFile -Force -ErrorAction SilentlyContinue }

# 处理数据目录
$dataDir = Join-Path $InstallDir 'data'
if (-not $KeepData) {
    if (Test-Path -LiteralPath $dataDir) {
        Remove-Item -LiteralPath $dataDir -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "已删除数据目录：$dataDir"
    }
} else {
    Write-Host "保留数据目录：$dataDir"
}

# 4) 若目录内已无任何剩余文件，安全清理安装目录；若有用户遗留文件则保留
$remaining = @(Get-ChildItem -LiteralPath $InstallDir -Force -ErrorAction SilentlyContinue)
if ($remaining.Count -eq 0) {
    Remove-Item -LiteralPath $InstallDir -Force -ErrorAction SilentlyContinue
    Write-Host "已清理安装目录：$InstallDir"
} else {
    Write-Host "提示：安装目录中存在非应用或保留文件（共 $($remaining.Count) 项），已予以安全保留：$InstallDir"
}

Write-Host "卸载完成。"
