param(
    [string]$DotNetPath = '',
    [switch]$Publish,
    [switch]$SkipTests
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
if (!$DotNetPath) {
    $installed = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($installed) { $DotNetPath = $installed.Source }
    elseif ($env:DOTNET_ROOT -and (Test-Path -LiteralPath (Join-Path $env:DOTNET_ROOT 'dotnet.exe'))) {
        $DotNetPath = Join-Path $env:DOTNET_ROOT 'dotnet.exe'
    } else {
        $localSdk = Join-Path (Split-Path $repository -Parent) 'research-tools\dotnet\dotnet.exe'
        if (Test-Path -LiteralPath $localSdk) { $DotNetPath = $localSdk }
        else { throw '.NET 10 SDK를 설치하거나 -DotNetPath로 dotnet.exe를 지정해주세요.' }
    }
}
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_ROOT = Split-Path $DotNetPath -Parent
$appProject = Join-Path $repository 'Windows\NoticeSender.Windows\NoticeSender.Windows.csproj'
function Invoke-DotNet {
    & $DotNetPath @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet 명령 실패 (종료 코드 $LASTEXITCODE)" }
}
Invoke-DotNet build $appProject --configuration Release
if (!$SkipTests) {
    Invoke-DotNet run --project (Join-Path $repository 'Windows\NoticeSender.Core.Tests') --configuration Release
    Invoke-DotNet run --project (Join-Path $repository 'Windows\NoticeSender.Automation.Tests') --configuration Release
    $smokeExe = Join-Path $repository 'Windows\NoticeSender.Windows\bin\Release\net10.0-windows\NoticeSender.exe'
    $smoke = Start-Process -FilePath $smokeExe -ArgumentList '--smoke-test' -PassThru -WindowStyle Hidden
    if (!$smoke.WaitForExit(30000)) { $smoke.Kill(); throw '앱 시작 검증 시간 초과' }
    if ($smoke.ExitCode -ne 0) { throw "앱 시작 검증 실패: $($smoke.ExitCode)" }
    Write-Output 'WPF 격리 스모크 검사 통과'
}
if ($Publish) {
    $output = Join-Path $repository 'dist\NoticeSender-Windows'
    Invoke-DotNet publish $appProject --configuration Release --runtime win-x64 --self-contained true '-p:PublishSingleFile=false' --output $output
    Copy-Item -LiteralPath (Join-Path $repository 'Windows\README.md') -Destination (Join-Path $output 'Windows-Guide.md') -Force
    Copy-Item -LiteralPath (Join-Path $repository 'Windows\TESTING.md') -Destination (Join-Path $output 'TESTING.md') -Force
    $archive = Join-Path $repository 'dist\NoticeSender-3.1.1-Windows-x64.zip'
    Compress-Archive -LiteralPath $output -DestinationPath $archive -Force
    Write-Output "배포 파일: $archive"
}
