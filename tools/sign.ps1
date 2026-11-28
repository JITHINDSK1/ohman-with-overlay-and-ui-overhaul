# SPDX-License-Identifier: GPL-3.0-or-later
# Generates a trusted Code Signing certificate for JITHINDSK1 and signs OMI binaries.
param(
    [string[]]$Files = @("OMI.exe", "preview\OMI.exe", "OMI-Setup.exe")
)

$ErrorActionPreference = "Stop"
$Subject = "CN=JITHINDSK1"

# 1. Locate or create the certificate
$cert = Get-ChildItem -Path "Cert:\CurrentUser\My" | Where-Object { $_.Subject -like "*$Subject*" } | Select-Object -First 1

if (-not $cert) {
    Write-Host "[+] Creating new Code Signing certificate for $Subject..." -ForegroundColor Cyan
    $cert = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject $Subject `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -NotAfter (Get-Date).AddYears(10) `
        -KeyUsage DigitalSignature `
        -FriendlyName "JITHINDSK1 Code Signing Certificate"
} else {
    Write-Host "[i] Using existing Code Signing certificate for $Subject" -ForegroundColor Green
}

# 2. Export public .cer
$cerPath = Join-Path $PSScriptRoot "..\JITHINDSK1.cer"
Export-Certificate -Cert $cert -FilePath $cerPath -Force | Out-Null
Write-Host "[+] Exported public certificate to $cerPath" -ForegroundColor Green

# 3. Trust in user store
try {
    certutil -user -addstore -f "TrustedPublisher" $cerPath | Out-Null
} catch {}

# 4. Sign target binaries
$targetFiles = @()
foreach ($f in $Files) {
    if ($f -match ",") {
        $targetFiles += ($f -split ",")
    } else {
        $targetFiles += $f
    }
}

foreach ($rel in $targetFiles) {
    $rel = $rel.Trim()
    if (-not $rel) { continue }
    $full = Join-Path $PSScriptRoot "..\$rel"
    if (Test-Path $full) {
        Write-Host "[+] Signing $rel with Authenticode (SHA256)..." -ForegroundColor Cyan
        $sig = Set-AuthenticodeSignature -FilePath $full -Certificate $cert -HashAlgorithm SHA256
        $color = if ($sig.Status -eq "Valid") { "Green" } else { "Yellow" }
        Write-Host "    Signature status: $($sig.Status) ($($sig.StatusMessage))" -ForegroundColor $color
    }
}
