param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$publishDir = Join-Path $root "publish"
$selfContainedValue = if ($SelfContained) { "true" } else { "false" }

if (Test-Path -LiteralPath $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}

New-Item -ItemType Directory -Path $publishDir | Out-Null

$projects = @(
    "WindowsServiceManager.App.csproj",
    "WindowsServiceManager.Cli\WindowsServiceManager.Cli.csproj",
    "WindowsServiceManager.Tray\WindowsServiceManager.Tray.csproj",
    "WindowsServiceManager.Service\WindowsServiceManager.Service.csproj"
)

foreach ($project in $projects) {
    $projectPath = Join-Path $root $project
    Write-Host "Publishing $project"
    dotnet publish $projectPath `
        -c $Configuration `
        -r $Runtime `
        --self-contained $selfContainedValue `
        -o $publishDir `
        --nologo
}

$configPath = Join-Path $publishDir "service-manager.config.json"
@'
{
  "databasePath": "",
  "mainAppPath": ""
}
'@ | Set-Content -LiteralPath $configPath -Encoding UTF8

Write-Host ""
Write-Host "Package created:"
Write-Host $publishDir
Write-Host ""
Write-Host "Executables:"
Get-ChildItem -LiteralPath $publishDir -Filter "*.exe" | Select-Object -ExpandProperty Name
