param()
$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$audit = Join-Path $repository 'scripts\test-windows-signatures.ps1'
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('NoticeSender-SignatureAudit-' + [guid]::NewGuid().ToString('N'))
$fixture = Join-Path $fixtureRoot 'publish'
$pin = '1111111111111111111111111111111111111111'
$otherPin = '2222222222222222222222222222222222222222'
$applicationFiles = @('NoticeSender.exe', 'NoticeSender.dll', 'NoticeSender.Core.dll', 'NoticeSender.Automation.dll')
$signatureState = [pscustomobject]@{ Mode = 'real'; Pin = $pin; OtherPin = $otherPin }
$failures = [Collections.Generic.List[string]]::new()

# Only the OS signature boundary is simulated for positive/publisher-policy cases.
# No certificates or trusted roots are created, and fixtures are never executed.
$signatureReader = {
    param([string]$LiteralPath)
    if ($signatureState.Mode -eq 'real') {
        return Microsoft.PowerShell.Security\Get-AuthenticodeSignature -LiteralPath $LiteralPath
    }
    $thumbprint = $signatureState.Pin
    if ($signatureState.Mode -eq 'wrong-publisher' -and [IO.Path]::GetFileName($LiteralPath) -eq 'NoticeSender.Core.dll') { $thumbprint = $signatureState.OtherPin }
    $status = 'Valid'
    if ($signatureState.Mode -eq 'unsigned-vendor' -and [IO.Path]::GetFileName($LiteralPath) -eq 'vendor.dll') { $status = 'NotSigned' }
    [pscustomobject]@{ Status = $status; SignatureType = 'Authenticode'; SignerCertificate = [pscustomobject]@{ Thumbprint=$thumbprint; Subject='CN=Audit test publisher' }; TimeStamperCertificate = $null }
}.GetNewClosure()
Set-Item Function:\Get-AuthenticodeSignature -Value $signatureReader

function Assert-True($Condition, [string]$Message) { if (!$Condition) { throw $Message } }
function Expect-Rejection([scriptblock]$Action, [string]$Pattern) {
    try { & $Action | Out-Null } catch {
        Assert-True ($_.Exception.Message -match $Pattern) "Unexpected rejection: $($_.Exception.Message)"
        return
    }
    throw "Expected rejection matching: $Pattern"
}
function Run-Case([string]$Name, [scriptblock]$Action) {
    try { & $Action; Write-Output "PASS $Name" } catch { $failures.Add("${Name}: $($_.Exception.Message)"); Write-Output "FAIL ${Name}: $($_.Exception.Message)" }
}

try {
    New-Item -ItemType Directory -Path $fixture | Out-Null
    foreach ($name in $applicationFiles) { [IO.File]::WriteAllText((Join-Path $fixture $name), 'synthetic signature-audit fixture; never execute') }
    $report = Join-Path $fixtureRoot 'audit.json'

    Run-Case 'inventory records every application DLL and real untrusted fixture states' {
        & $audit -PublishDirectory $fixture -ReportPath $report | Out-Null
        $result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
        Assert-True ($result.Files.Count -eq 4) 'Expected four application files'
        Assert-True (@($result.Files | Where-Object { $_.Status -eq 'Valid' -or $_.SignerThumbprint }).Count -eq 0) 'Synthetic fixtures must not have trusted signatures'
        Assert-True (@($result.Files | Where-Object { $_.Sha256 -notmatch '^[0-9A-F]{64}$' }).Count -eq 0) 'Missing SHA-256'
        Assert-True (!$result.SignaturesAccepted -and !$result.StrictVerificationRequested) 'Unsigned inventory must not imply verification success'
    }
    Run-Case 'strict gate refuses unsigned files and leaves a failure report' {
        Expect-Rejection { & $audit -PublishDirectory $fixture -ReportPath $report -RequireSigned -ExpectedApplicationSignerThumbprint $pin } 'signature verification failed'
        $result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
        Assert-True (!$result.SignaturesAccepted -and $result.Problems.Count -ge 4) 'Missing signature failures'
    }
    Run-Case 'strict gate requires an explicit valid publisher pin' {
        Expect-Rejection { & $audit -PublishDirectory $fixture -ReportPath $report -RequireSigned } 'thumbprint'
        Expect-Rejection { & $audit -PublishDirectory $fixture -ReportPath $report -RequireSigned -ExpectedApplicationSignerThumbprint 'invalid' } 'thumbprint'
    }
    Run-Case 'valid signatures with the expected application publisher pass' {
        $signatureState.Mode = 'valid'
        & $audit -PublishDirectory $fixture -ReportPath $report -RequireSigned -ExpectedApplicationSignerThumbprint $pin | Out-Null
        $result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
        Assert-True ($result.SignaturesAccepted -and $result.StrictVerificationRequested) 'Expected signature gate success'
    }
    Run-Case 'one valid DLL from an unexpected publisher fails' {
        $signatureState.Mode = 'wrong-publisher'
        Expect-Rejection { & $audit -PublishDirectory $fixture -ReportPath $report -RequireSigned -ExpectedApplicationSignerThumbprint $pin } 'signature verification failed'
        $result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
        Assert-True (($result.Problems -join ' ') -match 'NoticeSender.Core.dll.*publisher') 'Expected the wrong publisher DLL to be identified'
    }
    Run-Case 'an unsigned vendor dependency fails even with signed application files' {
        $signatureState.Mode = 'unsigned-vendor'
        $vendor = Join-Path $fixture 'vendor.dll'
        [IO.File]::WriteAllText($vendor, 'synthetic vendor fixture')
        try {
            Expect-Rejection { & $audit -PublishDirectory $fixture -ReportPath $report -RequireSigned -ExpectedApplicationSignerThumbprint $pin } 'signature verification failed'
            $result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
            Assert-True (($result.Problems -join ' ') -match 'vendor.dll.*NotSigned') 'Expected vendor signature failure'
        } finally { Remove-Item -LiteralPath $vendor }
    }
    Run-Case 'missing and unexpected application binaries fail inventory validation' {
        $signatureState.Mode = 'valid'
        $core = Join-Path $fixture 'NoticeSender.Core.dll'
        Remove-Item -LiteralPath $core
        try { Expect-Rejection { & $audit -PublishDirectory $fixture -ReportPath $report } 'application file set' }
        finally { [IO.File]::WriteAllText($core, 'synthetic signature-audit fixture; never execute') }
        $unexpected = Join-Path $fixture 'NoticeSender.Extra.dll'
        [IO.File]::WriteAllText($unexpected, 'synthetic unexpected fixture')
        try { Expect-Rejection { & $audit -PublishDirectory $fixture -ReportPath $report } 'application file set' }
        finally { Remove-Item -LiteralPath $unexpected }
    }
    Run-Case 'report paths inside publish cannot overwrite runtime configuration' {
        $runtimeConfig = Join-Path $fixture 'NoticeSender.runtimeconfig.json'
        $original = '{"runtimeOptions":{"tfm":"net10.0"}}'
        [IO.File]::WriteAllText($runtimeConfig, $original)
        try {
            Expect-Rejection { & $audit -PublishDirectory $fixture -ReportPath $runtimeConfig } 'outside.*publish'
            Assert-True ([IO.File]::ReadAllText($runtimeConfig) -ceq $original) 'Runtime configuration was overwritten'
        } finally { Remove-Item -LiteralPath $runtimeConfig }
    }
} finally {
    # Validate the exact temporary target before recursive cleanup.
    $resolvedFixture = [IO.Path]::GetFullPath($fixtureRoot)
    $tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (!$resolvedFixture.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolvedFixture) -notlike 'NoticeSender-SignatureAudit-*') { throw 'Unexpected cleanup target' }
    if (Test-Path -LiteralPath $resolvedFixture) { Remove-Item -LiteralPath $resolvedFixture -Recurse -Force }
}
if ($failures.Count) { throw "$($failures.Count) signature audit test(s) failed: $($failures -join '; ')" }
Write-Output 'Signature audit tests: 8/8 passed (policy-positive cases simulate OS signature results).'
