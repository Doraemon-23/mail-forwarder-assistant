param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$localDotnet = Join-Path $projectRoot ".tools\dotnet\dotnet.exe"
$dotnet = if (Test-Path $localDotnet) { $localDotnet } else { "dotnet" }
$artifactsRoot = Join-Path $projectRoot "artifacts"
$output = Join-Path $artifactsRoot "邮件转发助手-绿色版-v1.0.10"
$zipOutput = Join-Path $artifactsRoot "邮件转发助手-绿色版-v1.0.10.zip"

& $dotnet test (Join-Path $projectRoot "MailForwarderAssistant.sln") -c $Configuration
if ($LASTEXITCODE -ne 0) { throw "测试失败。" }

$artifactsFullPath = [System.IO.Path]::GetFullPath($artifactsRoot).TrimEnd('\') + '\'
$outputFullPath = [System.IO.Path]::GetFullPath($output)
if (-not $outputFullPath.StartsWith($artifactsFullPath, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "发布目录不在 artifacts 文件夹内，已停止清理。"
}
if (Test-Path -LiteralPath $outputFullPath) { Remove-Item -LiteralPath $outputFullPath -Recurse -Force }

& $dotnet publish (Join-Path $projectRoot "src\MailForwarderAssistant\MailForwarderAssistant.csproj") -c $Configuration --no-self-contained -o $output
if ($LASTEXITCODE -ne 0) { throw "发布失败。" }

Copy-Item (Join-Path $projectRoot "使用说明.txt") $output -Force
if (Test-Path $zipOutput) { Remove-Item -LiteralPath $zipOutput -Force }
Compress-Archive -Path (Join-Path $output "*") -DestinationPath $zipOutput -CompressionLevel Optimal
Write-Host "绿色版已生成：$output"
Write-Host "压缩包已生成：$zipOutput"
