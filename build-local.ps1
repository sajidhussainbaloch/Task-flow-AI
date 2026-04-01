param(
    [string]$Target = "ZayFlow.sln",
    [switch]$LaunchApp,
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$sdkVersion = "8.0.418"
$sdkRoot = Join-Path $env:ProgramFiles "dotnet\sdk\$sdkVersion"
$dotnetRoot = Join-Path $env:ProgramFiles "dotnet"
$fallbackRoot = Join-Path $repoRoot ".sdk-fallback"
$fallbackSdks = Join-Path $fallbackRoot "Sdks"

if (-not (Test-Path $sdkRoot))
{
    throw ".NET SDK $sdkVersion was not found at $sdkRoot"
}

New-Item -ItemType Directory -Force $fallbackSdks | Out-Null

Get-ChildItem (Join-Path $sdkRoot "Sdks") -Directory | ForEach-Object {
    $targetPath = Join-Path $fallbackSdks $_.Name
    if (-not (Test-Path $targetPath))
    {
        New-Item -ItemType Junction -Path $targetPath -Target $_.FullName | Out-Null
    }
}

Get-ChildItem $sdkRoot -Directory | Where-Object Name -ne "Sdks" | ForEach-Object {
    $targetPath = Join-Path $fallbackRoot $_.Name
    if (-not (Test-Path $targetPath))
    {
        New-Item -ItemType Junction -Path $targetPath -Target $_.FullName | Out-Null
    }
}

Get-ChildItem $sdkRoot -File | ForEach-Object {
    $targetPath = Join-Path $fallbackRoot $_.Name
    if (-not (Test-Path $targetPath))
    {
        Copy-Item $_.FullName $targetPath
    }
}

$autoImportSdk = Join-Path $fallbackSdks "Microsoft.NET.SDK.WorkloadAutoImportPropsLocator\Sdk"
$manifestSdk = Join-Path $fallbackSdks "Microsoft.NET.SDK.WorkloadManifestTargetsLocator\Sdk"
New-Item -ItemType Directory -Force $autoImportSdk | Out-Null
New-Item -ItemType Directory -Force $manifestSdk | Out-Null

$emptyProject = @"
<Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
</Project>
"@

Set-Content (Join-Path $autoImportSdk "AutoImport.props") $emptyProject
Set-Content (Join-Path $manifestSdk "WorkloadManifest.targets") $emptyProject

$env:MSBuildSDKsPath = $fallbackSdks

$buildArgs = @(
    "build",
    $Target,
    "-m:1",
    "-verbosity:minimal",
    "-p:BuildInParallel=false",
    "-p:NetCoreRoot=$dotnetRoot\",
    "-p:NETCoreSdkVersion=$sdkVersion",
    "-p:NETCoreSdkRuntimeIdentifier=win-x64",
    "-p:NetCoreTargetingPackRoot=$dotnetRoot\packs"
)

if ($NoRestore)
{
    $buildArgs += "--no-restore"
}

& dotnet @buildArgs
if ($LASTEXITCODE -ne 0)
{
    exit $LASTEXITCODE
}

if ($LaunchApp)
{
    $appExe = Join-Path $repoRoot "ZayFlow.App\bin\x64\Debug\net8.0-windows\ZayFlow.App.exe"
    if (-not (Test-Path $appExe))
    {
        throw "Built app not found at $appExe"
    }

    Start-Process $appExe | Out-Null
}
