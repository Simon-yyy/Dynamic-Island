# 灵动岛 / Liquid Island —— 一键重新构建
# 用法：在本目录打开 PowerShell，执行  .\build.ps1
# 依赖：Windows 自带的 .NET Framework 4.x（csc.exe），不需要安装 SDK / Visual Studio
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src  = Join-Path $root 'src'
$out  = Join-Path $root '灵动岛.exe'

# 编译前先关闭系统中所有正在运行的 灵动岛 旧实例（无论从哪个目录启动），避免旧进程占屏或锁死输出文件
Get-Process -Name 灵动岛 -ErrorAction SilentlyContinue | ForEach-Object {
  try {
    Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
  } catch {}
}
Start-Sleep -Milliseconds 350

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path -LiteralPath $csc)) { throw '未找到 csc.exe，需要 .NET Framework 4.x（Win10/11 自带）' }
$fw = Split-Path -Parent $csc

function Find-Assembly([string]$name) {
  $cand = @(
    (Join-Path $fw ("WPF\{0}.dll" -f $name)),
    (Join-Path $fw ("{0}.dll" -f $name))
  )
  foreach ($c in $cand) { if (Test-Path -LiteralPath $c) { return $c } }
  $gac = Join-Path $env:WINDIR 'Microsoft.NET\assembly'
  $hit = Get-ChildItem -LiteralPath $gac -Recurse -Filter ("{0}.dll" -f $name) -ErrorAction SilentlyContinue | Select-Object -First 1
  if ($hit) { return $hit.FullName }
  throw ("找不到引用程序集 {0}.dll" -f $name)
}

$names = @('PresentationCore','PresentationFramework','WindowsBase','System.Xaml','System.Drawing','System.Windows.Forms','System.Core','System','System.Xml','System.Runtime.WindowsRuntime','System.Web.Extensions')
$refs  = @()
foreach ($n in $names) { $refs += ('/r:' + (Find-Assembly $n)) }

# WinRT 引用：蓝牙电量和媒体会话（读歌名）都要用。
# 优先用工程自带的合约 winmd（winmd 目录，6 MB，只在编译时用）；没有的话退回系统自带的 WinMetadata。
$facade = Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\Facades\System.Runtime.dll'
if (-not (Test-Path -LiteralPath $facade)) {
  $facade = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\System.Runtime.dll'
}
if (Test-Path -LiteralPath $facade) { $refs += ('/r:' + $facade) }

$winmd = Join-Path $root 'winmd'
$contract = @('Windows.WinMD','Windows.Foundation.FoundationContract.winmd','Windows.Foundation.UniversalApiContract.winmd')
$hasContract = $true
foreach ($m in $contract) { if (-not (Test-Path -LiteralPath (Join-Path $winmd $m))) { $hasContract = $false } }

if ($hasContract) {
  foreach ($m in $contract) { $refs += ('/r:' + (Join-Path $winmd $m)) }
} else {
  $winmdDir = Join-Path $env:WINDIR 'System32\WinMetadata'
  if (Test-Path -LiteralPath $winmdDir) {
    foreach ($m in @('Windows.Foundation.winmd','Windows.Devices.winmd','Windows.Storage.winmd','Windows.Networking.winmd','Windows.ApplicationModel.winmd')) {
      $p = Join-Path $winmdDir $m
      if (Test-Path -LiteralPath $p) { $refs += ('/r:' + $p) }
    }
  }
}

# 如果目标输出文件已被正在运行的进程占用锁定，先安全转存为 .old 以便 csc 生成新文件
function Safe-PrepareOutput([string]$targetPath) {
  if (Test-Path -LiteralPath $targetPath) {
    try {
      $testStream = [System.IO.File]::Open($targetPath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
      $testStream.Close()
    } catch {
      $oldPath = $targetPath + ".old"
      if (Test-Path -LiteralPath $oldPath) {
        try { Remove-Item -LiteralPath $oldPath -Force -ErrorAction SilentlyContinue } catch {}
      }
      try {
        Move-Item -LiteralPath $targetPath -Destination $oldPath -Force
        Write-Host ("目标文件正被运行占用，已安全转存为：{0}" -f $oldPath)
      } catch {
        Write-Warning ("无法重命名被占用的文件：{0}" -f $_.Exception.Message)
      }
    }
  }
}

$ico = Join-Path $root 'island.ico'

# 写入可核对的构建标识，编译进主程序，避免继续误把旧 EXE 当成修复版
$buildVersion = '2026.11.17.batt-multi13'
$buildStamp = Get-Date -Format 'yyyy-MM-ddTHH:mm:sszzz'
$buildInfo = Join-Path $src 'BuildInfo.cs'
$buildInfoText = @"
namespace DynamicIsland
{
    internal static class BuildInfo
    {
        public const string Version = "$buildVersion";
        public const string Stamp = "$buildStamp";
        public const string Feature = "pnp-cfgmgr32+winsock-rfcomm-ch1+multi-device+freebuds-pro5";
        public static string Display
        {
            get { return "灵动岛 " + Version + " (" + Stamp + ")"; }
        }
    }
}
"@
Set-Content -LiteralPath $buildInfo -Value $buildInfoText -Encoding UTF8
Write-Host ("构建标识：{0}  时间：{1}" -f $buildVersion, $buildStamp)
Write-Host '编译完成后请在托盘图标提示或右键菜单中核对上述标识；标识不符即仍在运行旧程序。'

$files = @(Get-ChildItem -LiteralPath $src -Filter *.cs | Sort-Object Name | ForEach-Object { $_.FullName })
if ($files.Count -eq 0) { throw ('src 目录里没有找到 .cs 文件：' + $src) }

Safe-PrepareOutput $out
Write-Host ("将编译 {0} 个源文件 -> {1}" -f $files.Count, $out)
$files | ForEach-Object { Write-Host ("  src: " + $_) }
if ($files.Count -lt 1) { throw 'src 数量异常' }

$icoArg = @()
if (Test-Path -LiteralPath $ico) { $icoArg = @(("/win32icon:" + $ico)) }
& $csc /nologo /optimize+ /target:winexe /platform:anycpu ("/out:" + $out) $refs $icoArg $files | ForEach-Object { Write-Host $_ }
if ($LASTEXITCODE -ne 0) { throw ("编译失败，退出码 " + $LASTEXITCODE) }
Write-Host ('编译成功：' + $out)
$stampFile = Join-Path $root 'BUILD-STAMP.txt'
@(
  'version=' + $buildVersion
  'stamp=' + $buildStamp
  'feature=pnp-cfgmgr32+winsock-rfcomm-ch1+multi-device+freebuds-pro5'
  'main=' + $out
  'note=托盘提示应为：灵动岛 ' + $buildVersion + ' (' + $buildStamp + ')'
) | Set-Content -LiteralPath $stampFile -Encoding UTF8
Write-Host ('已写入构建戳：' + $stampFile)

$btCs = Join-Path $root 'BtBattery.cs'
$btOut = Join-Path $root 'BtBattery.exe'
if (Test-Path -LiteralPath $btCs) {
    Safe-PrepareOutput $btOut
    Write-Host ("编译独立辅助工具 -> {0}" -f $btOut)
    & $csc /nologo /optimize+ /target:exe /platform:anycpu ("/out:" + $btOut) $refs $btCs | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) { throw ("BtBattery 编译失败，退出码 " + $LASTEXITCODE) }
    Write-Host ('编译成功：' + $btOut)
}
