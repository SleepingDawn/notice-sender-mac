param(
    [string]$DotNetPath = '',
    [switch]$Publish,
    [switch]$SkipTests,
    [string]$OcrRuntimeDirectory = $env:NOTICE_SENDER_OCR_RUNTIME,
    [switch]$RequireBotReceiptRuntime
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$version = ([xml](Get-Content -LiteralPath (Join-Path $repository 'Windows\Directory.Build.props') -Raw)).Project.PropertyGroup.Version
if (!$version -or $version -notmatch '^[0-9A-Za-z.-]+$') { throw 'Invalid Windows package version.' }
$output = Join-Path $repository 'dist\NoticeSender-Windows'
$ocrHelper = Join-Path $PSScriptRoot 'copy-windows-ocr-runtime.ps1'
if ($RequireBotReceiptRuntime -and !$OcrRuntimeDirectory) { throw 'OCR runtime is required: provide -OcrRuntimeDirectory or NOTICE_SENDER_OCR_RUNTIME before building a bot-receipt package.' }
if ($OcrRuntimeDirectory) { & $ocrHelper -RuntimeDirectory $OcrRuntimeDirectory -VerifyOnly }
elseif ($Publish -and (Test-Path -LiteralPath (Join-Path $output 'ocr'))) { throw 'Existing output/ocr cannot be reused without -OcrRuntimeDirectory; provide its verified source for this package.' }
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
    Invoke-DotNet publish $appProject --configuration Release --runtime win-x64 --self-contained true '-p:PublishSingleFile=false' --output $output
    if ($OcrRuntimeDirectory) { & $ocrHelper -RuntimeDirectory $OcrRuntimeDirectory -PublishDirectory $output }
    else { Write-Warning 'OCR runtime is not bundled; automatic attachment receipt checks are unavailable. Use -OcrRuntimeDirectory and -RequireBotReceiptRuntime for a receipt-enabled package.' }
    Copy-Item -LiteralPath (Join-Path $repository 'Windows\README.md') -Destination (Join-Path $output 'Windows-Guide.md') -Force
    Copy-Item -LiteralPath (Join-Path $repository 'Windows\TESTING.md') -Destination (Join-Path $output 'TESTING.md') -Force
    Copy-Item -LiteralPath (Join-Path $repository 'Windows\SIGNING.md') -Destination (Join-Path $output 'SIGNING.md') -Force
    Copy-Item -LiteralPath (Join-Path $repository 'Windows\OCR-RUNTIME.md') -Destination (Join-Path $output 'OCR-RUNTIME.md') -Force
    Copy-Item -LiteralPath (Join-Path $repository 'LICENSE') -Destination (Join-Path $output 'LICENSE.txt') -Force
    Copy-Item -LiteralPath (Join-Path $repository 'PRIVACY.md') -Destination (Join-Path $output 'PRIVACY.md') -Force
    Copy-Item -LiteralPath (Join-Path $repository 'CODE_SIGNING_POLICY.md') -Destination (Join-Path $output 'CODE_SIGNING_POLICY.md') -Force
    if (!$SkipTests) {
        $publishedSmoke = Start-Process -FilePath (Join-Path $output 'NoticeSender.exe') -ArgumentList '--smoke-test' -PassThru -WindowStyle Hidden
        if (!$publishedSmoke.WaitForExit(30000)) { $publishedSmoke.Kill(); throw '자체 포함 배포본 검사 시간 초과' }
        if ($publishedSmoke.ExitCode -ne 0) { throw "자체 포함 배포본 검사 실패: $($publishedSmoke.ExitCode)" }
        Write-Output '자체 포함 배포본 격리 스모크 검사 통과'
    }
    $archive = Join-Path $repository ('dist\NoticeSender-' + $version + '-Windows-x64.zip')
    Compress-Archive -LiteralPath $output -DestinationPath $archive -Force
    Write-Output "배포 파일: $archive"
}
