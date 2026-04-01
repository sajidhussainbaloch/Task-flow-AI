param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot "..")
$projectPath = Join-Path $repoRoot "ZayFlow.App\ZayFlow.App.csproj"
$publishPath = Join-Path $repoRoot "publish"

Write-Host "Publishing ZayFlow AI..." -ForegroundColor Cyan
Write-Host "Project: $projectPath"
Write-Host "Output : $publishPath"

dotnet publish $projectPath `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $publishPath

Write-Host ""
Write-Host "Publish complete." -ForegroundColor Green
Write-Host "Next step: open installer\installer.iss in Inno Setup and compile it." -ForegroundColor Yellow