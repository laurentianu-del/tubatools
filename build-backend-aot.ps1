# 主动拦截后端 NativeAOT 发布辅助脚本。
# 用途：定位 MSVC vcvars64.bat（VS2022 各版本/目录），在其环境下以
#   IlcUseEnvironmentalTools=true 发布 NativeAOT 单文件 exe，
#   兼容 vswhere 元数据缺失（仅装 BuildTools）的机器与 CI。
# 用法：
#   powershell -NoProfile -ExecutionPolicy Bypass -File build-backend-aot.ps1 -Rid win-x64 -OutputDir <dir>
param(
    [Parameter(Mandatory = $true)]
    [string]$Rid,
    [Parameter(Mandatory = $true)]
    [string]$OutputDir,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$backendProj = Join-Path $repoRoot "TubaWinUI3.BackEnd\TubaWinUi3.BackEnd.csproj"

# 定位 vcvars64.bat：覆盖 VS2022 各版本（Enterprise/Professional/Community/BuildTools）
# 与自定义目录。找不到则直接发布（依赖机器上已配置的 link.exe/lib.exe 环境）。
$vcvarsCandidates = @()
foreach ($vsEdition in @("BuildTools", "Enterprise", "Professional", "Community")) {
    foreach ($base in @("${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022",
                        "${env:ProgramFiles}\Microsoft Visual Studio\2022")) {
        if ($base -and (Test-Path $base)) {
            $vcvarsCandidates += (Join-Path $base (Join-Path $vsEdition "VC\Auxiliary\Build\vcvars64.bat"))
        }
    }
}
# 也允许通过环境变量显式指定
if ($env:VCVARS64_PATH) { $vcvarsCandidates += $env:VCVARS64_PATH }

$vcvars = $vcvarsCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

if ($vcvars) {
    Write-Host "使用 vcvars64: $vcvars"
    $cmd = "call `"$vcvars`" >nul 2>&1 && dotnet publish `"$backendProj`" -c $Configuration -r $Rid -p:IlcUseEnvironmentalTools=true -o `"$OutputDir`""
    cmd /c $cmd
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败 (exit $LASTEXITCODE)" }
}
else {
    Write-Host "未找到 vcvars64.bat，尝试直接发布（依赖现有环境）"
    & dotnet publish $backendProj -c $Configuration -r $Rid -o $OutputDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败 (exit $LASTEXITCODE)" }
}

$exe = Join-Path $OutputDir "TubaWinUI3.BackEnd.exe"
if (-not (Test-Path $exe)) { throw "后端产物不存在: $exe" }
Write-Host "后端 AOT 产物: $exe ($((Get-Item $exe).Length) bytes)"
