param([string]$VerifiedRuntimeDirectory = '')
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$helper = Join-Path $repository 'scripts\copy-windows-ocr-runtime.ps1'
if (!(Test-Path -LiteralPath $helper)) { throw 'OCR packaging helper is missing; supplied runtime bytes cannot be validated or packaged.' }
. $helper -FunctionsOnly
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('NoticeSender-OcrPackageTest-' + [guid]::NewGuid().ToString('N'))
$source = Join-Path $fixtureRoot 'source'
$publish = Join-Path $fixtureRoot 'publish'
$failures = [Collections.Generic.List[string]]::new()
$passed = 0
function Assert-True($Condition, [string]$Message) { if (!$Condition) { throw $Message } }
function Expect-Rejection([scriptblock]$Action, [string]$Pattern) {
    try { & $Action | Out-Null } catch {
        Assert-True ($_.Exception.Message -match $Pattern) "Unexpected rejection: $($_.Exception.Message)"
        return
    }
    throw "Expected rejection matching: $Pattern"
}
function Run-Case([string]$Name, [scriptblock]$Action) {
    try { & $Action; $script:passed++; Write-Output "PASS $Name" }
    catch { $failures.Add("${Name}: $($_.Exception.Message)"); Write-Output "FAIL ${Name}: $($_.Exception.Message)" }
}
function Write-Fixture {
    $resolvedSource = [IO.Path]::GetFullPath($source)
    if ([IO.Path]::GetDirectoryName($resolvedSource) -ne [IO.Path]::GetFullPath($fixtureRoot) -or [IO.Path]::GetFileName($resolvedSource) -ne 'source') { throw 'Unexpected fixture reset target' }
    if (Test-Path -LiteralPath $source) { Remove-Item -LiteralPath $source -Recurse -Force }
    New-Item -ItemType Directory -Path (Join-Path $source 'doc') -Force | Out-Null
    $rows = @()
    $script:pins = @{}
    foreach ($name in @('tesseract.exe', 'support.dll', 'eng.traineddata', 'kor.traineddata')) {
        $path = Join-Path $source $name
        [IO.File]::WriteAllText($path, "synthetic OCR packaging fixture $name; never execute")
        $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        $rows += [pscustomobject]@{ name=$name; length=(Get-Item -LiteralPath $path).Length; sha256=$hash; source='C:\untrusted-source-path-that-must-not-be-followed' }
        if ($name -ne 'support.dll') { $script:pins[$name] = $hash }
    }
    foreach ($name in @('LICENSE', 'AUTHORS', 'README.md')) { [IO.File]::WriteAllText((Join-Path $source "doc\$name"), "synthetic notice $name") }
    $manifest = [ordered]@{ engine='synthetic fixture, never execute'; files=$rows; engine_license='C:\ignored\LICENSE'; model_source='fixture'; note='fixture' }
    [IO.File]::WriteAllText((Join-Path $source 'runtime-dependency-manifest.json'), ($manifest | ConvertTo-Json -Depth 6))
}
function Change-Manifest([scriptblock]$Change) {
    $path = Join-Path $source 'runtime-dependency-manifest.json'
    $manifest = [IO.File]::ReadAllText($path) | ConvertFrom-Json
    & $Change $manifest
    [IO.File]::WriteAllText($path, ($manifest | ConvertTo-Json -Depth 6))
}
try {
    New-Item -ItemType Directory -Path $fixtureRoot | Out-Null
    Run-Case 'copy exact manifest bytes, canonical model paths and all notices without machine paths' {
        Write-Fixture
        $plan = Get-WindowsOcrRuntimePlan -RuntimeDirectory $source -PinnedHashes $pins
        Copy-WindowsOcrRuntime -Plan $plan -PublishDirectory $publish | Out-Null
        $output = Join-Path $publish 'ocr'
        $record = [IO.File]::ReadAllText((Join-Path $output 'runtime-dependency-manifest.json')) | ConvertFrom-Json
        Assert-True ($record.files.Count -eq 7) 'Expected four declared payloads plus three notice files'
        Assert-True (!(Test-Path -LiteralPath (Join-Path $output 'eng.traineddata'))) 'Duplicated flat model should not be packaged'
        foreach ($row in $record.files) {
            $path = Join-Path $output $row.path
            Assert-True ((Get-Item -LiteralPath $path).Length -eq $row.length -and (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -eq $row.sha256) "Copied bytes differ: $($row.path)"
            Assert-True ($row.path -notmatch '^[A-Za-z]:|\.\.') 'Non-relative canonical path'
        }
        Assert-True (([IO.File]::ReadAllText((Join-Path $output 'runtime-dependency-manifest.json'))) -notmatch 'untrusted-source|ignored|"source"') 'Source machine paths leaked into distributable manifest'
        [IO.File]::WriteAllText((Join-Path $output 'stale.dll'), 'stale synthetic fixture')
        Copy-WindowsOcrRuntime -Plan $plan -PublishDirectory $publish | Out-Null
        Assert-True (!(Test-Path -LiteralPath (Join-Path $output 'stale.dll'))) 'Stale runtime DLL survived replacement'
    }
    Run-Case 'missing either required language model fails' {
        foreach ($model in @('eng.traineddata', 'kor.traineddata')) {
            Write-Fixture
            Remove-Item -LiteralPath (Join-Path $source $model)
            Expect-Rejection { Get-WindowsOcrRuntimePlan -RuntimeDirectory $source -PinnedHashes $pins } 'missing|not found'
        }
    }
    Run-Case 'changed dependency bytes fail their manifest hash or length' {
        Write-Fixture
        [IO.File]::AppendAllText((Join-Path $source 'support.dll'), 'changed')
        Expect-Rejection { Get-WindowsOcrRuntimePlan -RuntimeDirectory $source -PinnedHashes $pins } 'hash|length'
    }
    Run-Case 'both source copies of a language model must match the manifest' {
        Write-Fixture
        New-Item -ItemType Directory -Path (Join-Path $source 'tessdata') | Out-Null
        [IO.File]::Copy((Join-Path $source 'kor.traineddata'), (Join-Path $source 'tessdata\kor.traineddata'))
        [IO.File]::AppendAllText((Join-Path $source 'tessdata\kor.traineddata'), 'changed duplicate')
        Expect-Rejection { Get-WindowsOcrRuntimePlan -RuntimeDirectory $source -PinnedHashes $pins } 'hash|length'
    }
    Run-Case 'source mutation during packaging preserves the earlier complete output' {
        Write-Fixture
        $plan = Get-WindowsOcrRuntimePlan -RuntimeDirectory $source -PinnedHashes $pins
        Copy-WindowsOcrRuntime -Plan $plan -PublishDirectory $publish | Out-Null
        $priorManifest = [IO.File]::ReadAllText((Join-Path $publish 'ocr\runtime-dependency-manifest.json'))
        [IO.File]::AppendAllText((Join-Path $source 'support.dll'), 'changed after validation')
        Expect-Rejection { Copy-WindowsOcrRuntime -Plan $plan -PublishDirectory $publish } 'changed during copy'
        Assert-True ([IO.File]::ReadAllText((Join-Path $publish 'ocr\runtime-dependency-manifest.json')) -ceq $priorManifest) 'Failed copy replaced the previous OCR output'
    }
    Run-Case 'manifest traversal and duplicate names are rejected' {
        Write-Fixture
        Change-Manifest { param($m) $m.files[1].name = '..\escape.dll' }
        Expect-Rejection { Get-WindowsOcrRuntimePlan -RuntimeDirectory $source -PinnedHashes $pins } 'name|path'
        Write-Fixture
        Change-Manifest { param($m) $m.files += $m.files[0] }
        Expect-Rejection { Get-WindowsOcrRuntimePlan -RuntimeDirectory $source -PinnedHashes $pins } 'duplicate'
    }
    Run-Case 'an undeclared source DLL is never silently copied' {
        Write-Fixture
        [IO.File]::WriteAllText((Join-Path $source 'undeclared.dll'), 'never execute')
        Expect-Rejection { Get-WindowsOcrRuntimePlan -RuntimeDirectory $source -PinnedHashes $pins } 'undeclared|unexpected'
    }
    Run-Case 'a rewritten manifest cannot replace the recognized engine pin' {
        Write-Fixture
        [IO.File]::WriteAllText((Join-Path $source 'tesseract.exe'), 'different synthetic engine')
        Change-Manifest { param($m) $m.files[0].length=(Get-Item -LiteralPath (Join-Path $source 'tesseract.exe')).Length; $m.files[0].sha256=(Get-FileHash -LiteralPath (Join-Path $source 'tesseract.exe') -Algorithm SHA256).Hash }
        Expect-Rejection { Get-WindowsOcrRuntimePlan -RuntimeDirectory $source -PinnedHashes $pins } 'pin|recognized'
    }
    Run-Case 'missing bundled license and output overlap are rejected' {
        Write-Fixture
        Remove-Item -LiteralPath (Join-Path $source 'doc\LICENSE')
        Expect-Rejection { Get-WindowsOcrRuntimePlan -RuntimeDirectory $source -PinnedHashes $pins } 'notice|LICENSE|missing'
        Write-Fixture
        $plan = Get-WindowsOcrRuntimePlan -RuntimeDirectory $source -PinnedHashes $pins
        Expect-Rejection { Copy-WindowsOcrRuntime -Plan $plan -PublishDirectory $source } 'overlap'
    }
    Run-Case 'production pins are read from the three recognizer requirements' {
        $actual = Get-WindowsOcrPinnedHashes -RecognizerPath (Join-Path $repository 'Windows\NoticeSender.Windows\AttachmentVisualRecognizer.cs')
        Assert-True ($actual.Count -eq 3 -and $actual['tesseract.exe'] -match '^[0-9A-F]{64}$' -and $actual['eng.traineddata'] -match '^[0-9A-F]{64}$' -and $actual['kor.traineddata'] -match '^[0-9A-F]{64}$') 'Recognizer identity pins were not all read'
    }
    Run-Case 'required OCR runtime fails before any dotnet build or publish' {
        $prior = $env:NOTICE_SENDER_OCR_RUNTIME
        try {
            $env:NOTICE_SENDER_OCR_RUNTIME = ''
            Expect-Rejection { & (Join-Path $repository 'scripts\build-windows.ps1') -Publish -SkipTests -RequireBotReceiptRuntime -DotNetPath (Join-Path $fixtureRoot 'must-not-run-dotnet.exe') } 'OCR runtime.*required|OcrRuntimeDirectory'
        } finally { $env:NOTICE_SENDER_OCR_RUNTIME = $prior }
    }
    if ($VerifiedRuntimeDirectory) {
        Run-Case 'supplied verified runtime copies with production recognizer pins' {
            $actual = Get-WindowsOcrPinnedHashes -RecognizerPath (Join-Path $repository 'Windows\NoticeSender.Windows\AttachmentVisualRecognizer.cs')
            $plan = Get-WindowsOcrRuntimePlan -RuntimeDirectory $VerifiedRuntimeDirectory -PinnedHashes $actual
            Copy-WindowsOcrRuntime -Plan $plan -PublishDirectory (Join-Path $fixtureRoot 'verified-publish') | Out-Null
        }
    } else { Write-Output 'Supplied-runtime integration not run; use -VerifiedRuntimeDirectory for that separate check.' }
} finally {
    $resolved = [IO.Path]::GetFullPath($fixtureRoot)
    $tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (!$resolved.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'NoticeSender-OcrPackageTest-*') { throw 'Unexpected temporary cleanup target' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
if ($failures.Count) { throw "$($failures.Count) OCR packaging test(s) failed: $($failures -join '; ')" }
Write-Output "OCR packaging tests: $passed passed. Synthetic payloads were not executed; copied notice bytes do not establish runtime trust."
