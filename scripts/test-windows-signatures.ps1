[CmdletBinding()]
param(
    [string]$PublishDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'dist\NoticeSender-Windows'),
    [string]$ReportPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'outputs\windows-signatures.json'),
    [switch]$RequireSigned,
    [string]$ExpectedApplicationSignerThumbprint = ''
)
$ErrorActionPreference = 'Stop'
if ($RequireSigned -and $ExpectedApplicationSignerThumbprint -notmatch '^[0-9A-Fa-f]{40}$') {
    throw 'Strict verification requires an explicit 40-hex application signer thumbprint from the approved provider.'
}
$publishRoot = (Get-Item -LiteralPath $PublishDirectory).FullName.TrimEnd('\')
if (!(Test-Path -LiteralPath $publishRoot -PathType Container)) { throw 'Publish directory does not exist.' }
$reportFile = [IO.Path]::GetFullPath($ReportPath)
if ([IO.Path]::GetExtension($reportFile) -ne '.json') { throw 'ReportPath must name a JSON file, never a product binary.' }
if ($reportFile.StartsWith($publishRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'ReportPath must be outside the publish directory to preserve every product file.'
}
$required = @('NoticeSender.exe', 'NoticeSender.dll', 'NoticeSender.Core.dll', 'NoticeSender.Automation.dll')
$problems = [Collections.Generic.List[string]]::new()
$shapeProblems = [Collections.Generic.List[string]]::new()
$entries = @(Get-ChildItem -LiteralPath $publishRoot -Recurse -Force)
if ((Get-Item -LiteralPath $publishRoot).Attributes -band [IO.FileAttributes]::ReparsePoint -or @($entries | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) {
    throw 'Publish directory must not contain reparse points.'
}
$files = @($entries | Where-Object { !$_.PSIsContainer -and $_.Extension -in '.exe', '.dll' } | Sort-Object FullName)
$relativeNames = @($files | ForEach-Object { $_.FullName.Substring($publishRoot.Length + 1) })
foreach ($name in $required) {
    if ($relativeNames -notcontains $name) { $shapeProblems.Add("Missing application file: $name") }
}
foreach ($name in $relativeNames) {
    if ([IO.Path]::GetFileName($name) -like 'NoticeSender*' -and $required -notcontains $name) { $shapeProblems.Add("Unexpected application file: $name") }
}
$inventory = @(foreach ($file in $files) {
    $relative = $file.FullName.Substring($publishRoot.Length + 1)
    $isApplication = $required -contains $relative
    $beforeHash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    $signature = Get-AuthenticodeSignature -LiteralPath $file.FullName
    $afterHash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    if ($beforeHash -ne $afterHash) { $problems.Add("${relative}: file changed during signature inspection") }
    $signer = $signature.SignerCertificate
    if ([string]$signature.Status -ne 'Valid') { $problems.Add("${relative}: $($signature.Status)") }
    if ($RequireSigned -and $isApplication) {
        if (!$signer -or $signer.Thumbprint -ne $ExpectedApplicationSignerThumbprint) { $problems.Add("${relative}: unexpected application publisher") }
        if ([string]$signature.SignatureType -ne 'Authenticode') { $problems.Add("${relative}: application needs an embedded Authenticode signature") }
    }
    [pscustomobject]@{
        Path = $relative
        IsApplication = $isApplication
        Length = $file.Length
        Sha256 = $afterHash
        Status = [string]$signature.Status
        SignatureType = [string]$signature.SignatureType
        SignerSubject = if ($signer) { $signer.Subject } else { $null }
        SignerThumbprint = if ($signer) { $signer.Thumbprint } else { $null }
        TimestampSigner = if ($signature.TimeStamperCertificate) { $signature.TimeStamperCertificate.Subject } else { $null }
    }
})
foreach ($problem in $shapeProblems) { $problems.Add($problem) }
$report = [ordered]@{
    SchemaVersion = 1
    InspectedAtUtc = [DateTime]::UtcNow.ToString('o')
    PublishDirectory = $publishRoot
    StrictVerificationRequested = [bool]$RequireSigned
    ExpectedApplicationSignerThumbprint = if ($RequireSigned) { $ExpectedApplicationSignerThumbprint.ToUpperInvariant() } else { $null }
    SignaturesAccepted = [bool]($RequireSigned -and $problems.Count -eq 0)
    Scope = 'Read-only Authenticode/hash inspection. Does not launch code, prove SAC acceptance, or test KakaoTalk delivery.'
    Problems = @($problems.ToArray())
    Files = $inventory
}
[IO.Directory]::CreateDirectory((Split-Path $reportFile -Parent)) | Out-Null
[IO.File]::WriteAllText($reportFile, ($report | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
Write-Output "Inspected $($inventory.Count) PE files. Report: $reportFile"
if ($shapeProblems.Count) { throw "Invalid application file set: $($shapeProblems -join '; ')" }
if ($RequireSigned -and $problems.Count) { throw "Release signature verification failed: $($problems -join '; ')" }
if (!$RequireSigned) { Write-Output 'Inventory only; no execution approval is implied.' }
