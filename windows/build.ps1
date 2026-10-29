# DashKey Windows 构建脚本
#
#   .\build.ps1                     # 依赖已装的 .NET 8 桌面运行时，单文件
#   .\build.ps1 -SelfContained      # 自带运行时，体积大但目标机器无需装 .NET
#   .\build.ps1 -Runtime win-arm64  # 指定架构
#
# 需要 .NET 8 SDK。

param(
    [string]$Runtime = "win-x64",
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"
Push-Location $PSScriptRoot

try {
    $out = Join-Path (Split-Path -Parent $PSScriptRoot) "Build\DashKey-$Runtime"

    Write-Host "==> 发布 $Runtime (SelfContained=$SelfContained)"

    dotnet publish .\DashKey.Windows.csproj `
        -c Release `
        -r $Runtime `
        --self-contained $SelfContained.IsPresent `
        -p:PublishSingleFile=true `
        -o $out

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish 失败（exit $LASTEXITCODE）"
    }

    Write-Host "==> 完成: $out\DashKey.exe"
}
finally {
    Pop-Location
}
