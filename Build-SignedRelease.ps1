# Erstellt ein signiertes Release: Publish, Authenticode-Signatur (selbst ausgestelltes Zertifikat),
# SHA-256-Hashliste aller Dateien inkl. CMS-Signatur, ZIP-Archiv.
param(
    [string]$Version = "1.0.0",
    [string]$Subject = 'CN=everything-everything, E="85025743+everything-everything@users.noreply.github.com"'
)
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$out = Join-Path $root "release\Dateiumbenenner-$Version"
$cerPath = Join-Path $root "release\Dateiumbenenner-CodeSigning.cer"

# 1. Zertifikat suchen oder selbst ausstellen
$cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert | Where-Object { $_.Subject -eq $Subject -and $_.NotAfter -gt (Get-Date) } | Select-Object -First 1
if (-not $cert) {
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $Subject -CertStoreLocation Cert:\CurrentUser\My -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -NotAfter (Get-Date).AddYears(5)
}

# 2. Veröffentlichen
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
dotnet publish (Join-Path $root "Dateiumbenenner\Dateiumbenenner.csproj") -c Release -r win-x64 --self-contained false -o $out
if ($LASTEXITCODE -ne 0) { throw "dotnet publish fehlgeschlagen" }
Copy-Item (Join-Path $root "LICENSE"), (Join-Path $root "THIRD-PARTY-NOTICES.md"), (Join-Path $root "README.md") $out

# 3. Eigene Binärdateien und Skripte Authenticode-signieren (Fremd-DLLs bleiben unverändert)
$toSign = @(Get-ChildItem $out -Include "Dateiumbenenner.exe", "Dateiumbenenner.dll" -Recurse) + @(Get-ChildItem $root -Filter *.ps1)
foreach ($f in $toSign) {
    Set-AuthenticodeSignature -FilePath $f.FullName -Certificate $cert -HashAlgorithm SHA256 | Out-Null
}

# 4. Öffentliches Zertifikat exportieren
Export-Certificate -Cert $cert -FilePath $cerPath -Type CERT | Out-Null
Copy-Item $cerPath $out

# 5. SHA-256 aller Dateien
$sums = Join-Path $out "SHA256SUMS.txt"
Get-ChildItem $out -File -Recurse | Where-Object { $_.Name -notlike "SHA256SUMS*" } | Sort-Object FullName | ForEach-Object {
    "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower(), $_.FullName.Substring($out.Length + 1).Replace('\', '/')
} | Set-Content $sums -Encoding UTF8

# 6. Hashliste mit CMS/PKCS#7 (detached) signieren
Add-Type -AssemblyName System.Security
$content = New-Object System.Security.Cryptography.Pkcs.ContentInfo (, [IO.File]::ReadAllBytes($sums))
$cms = New-Object System.Security.Cryptography.Pkcs.SignedCms $content, $true
$signer = New-Object System.Security.Cryptography.Pkcs.CmsSigner $cert
$signer.DigestAlgorithm = New-Object System.Security.Cryptography.Oid "2.16.840.1.101.3.4.2.1"
$cms.ComputeSignature($signer)
[IO.File]::WriteAllBytes("$sums.p7s", $cms.Encode())

# 7. ZIP + Hash
$zip = Join-Path $root "release\Dateiumbenenner-$Version-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path "$out\*" -DestinationPath $zip
"{0}  {1}" -f (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower(), (Split-Path $zip -Leaf) | Set-Content "$zip.sha256" -Encoding UTF8

Write-Host "Fertig: $zip"
Write-Host "Zertifikat-Fingerabdruck: $($cert.Thumbprint)"

# SIG # Begin signature block
# MIIH/AYJKoZIhvcNAQcCoIIH7TCCB+kCAQExDzANBglghkgBZQMEAgEFADB5Bgor
# BgEEAYI3AgEEoGswaTA0BgorBgEEAYI3AgEeMCYCAwEAAAQQH8w7YFlLCE63JNLG
# KX7zUQIBAAIBAAIBAAIBAAIBADAxMA0GCWCGSAFlAwQCAQUABCCtR1A/0S0nVrvm
# fJKRaFSxJauVTGWDhORVhf2RIQVXJKCCBKQwggSgMIIDCKADAgECAhBDYujFJsGA
# mEtWv63vIZ0LMA0GCSqGSIb3DQEBCwUAMGgxRjBEBgkqhkiG9w0BCQEWNzg1MDI1
# NzQzK2V2ZXJ5dGhpbmctZXZlcnl0aGluZ0B1c2Vycy5ub3JlcGx5LmdpdGh1Yi5j
# b20xHjAcBgNVBAMMFWV2ZXJ5dGhpbmctZXZlcnl0aGluZzAeFw0yNjEwMDIyMjMz
# MzNaFw0zMTEwMDIyMjQzMzNaMGgxRjBEBgkqhkiG9w0BCQEWNzg1MDI1NzQzK2V2
# ZXJ5dGhpbmctZXZlcnl0aGluZ0B1c2Vycy5ub3JlcGx5LmdpdGh1Yi5jb20xHjAc
# BgNVBAMMFWV2ZXJ5dGhpbmctZXZlcnl0aGluZzCCAaIwDQYJKoZIhvcNAQEBBQAD
# ggGPADCCAYoCggGBAK36Uvdvq+B6NW9i44XtpwUkpqnAWQFIycuEbGkUKAnwAn2G
# T3VT1bDoqGV+C2ei2JPe0n3Id5H5oi4FZtBassOv7hxTQg0yNarkNe9TX5iW1Fo0
# qY5GXzKwAQbXcez1oWkzd8+j/iPhq/0zp2o64p3fN8MPn8MD69klMrNCKoGHtlxf
# 7UG8uNCrinC+JE7fgGE5hLr2c5k2hhpXhuh4sZvzZh/25BQZBH/agMNhQogmyx33
# z5keR5cnKB68YvodSkq6rLyhXxPALA+aDu3HHNvx81WrsA6XB66V+o/8uzeU6zCm
# ieUyNsIrDkEX7n7QpJl30py7iikbyOnCLI5kVBSIE7hAzCzkwYgvZw3Na3TO7EAR
# 6amT/zTcirGyUQBM1JiggIjkZTtSBtsZ15G4KCKcytFLNB4gr0U4yXEd7crUefzV
# WEbQIed9pmDz/wy8bkgcsB1ETNFSwdU5EZZGVKF0ZMSoeknIC2aNTFTvDY371tUJ
# BQjDmzy2AdS7sZtx9QIDAQABo0YwRDAOBgNVHQ8BAf8EBAMCB4AwEwYDVR0lBAww
# CgYIKwYBBQUHAwMwHQYDVR0OBBYEFC6O00nlqjWt3PZwl/P7R6t5AkVHMA0GCSqG
# SIb3DQEBCwUAA4IBgQCInqql5//NBNup0FrxEmwcdtnR8uzgTXSzlvSilx9avPlh
# BT0pQZxzbAV9ORg5Zph45/uy7cYMqmjziFRaaT7ksUrilrbP2EepN7Q3Efy2qyl6
# vQINgoIN2UcUudVqnp9cmw1p/1HPx9l+lLEzPD7dOeTK+qaAAvit19hs/r99BIP/
# GeazBPTBdm262t7Z1YrGYLwMaAkmlekT2Pb4S1hOkrt/M/cQuu3xhP6gQC0zaBcI
# 5P10V46DcH+WJa7SwctDrCSxkO4PGgvNlaPCaJG4C5rC10WuLkApmXFepVEendVs
# cKtrLx99jb240hpQR3P0p5hBCA54CmPTWIiSmB9FBcN3q2rKjIGEK4mgRuuQJ+AS
# 7wS0d9l23NG2GYMF+VeMd3stA1oKBeJ1lWfe3l0AGCSjdfmy1lfdsrtZsbhBD8AW
# MF+MqzEhjzE696ZaRulEt1zFEOuTSdzBKGfd2vlsev8fJ/IuQJGYFXSD/yhIluE8
# LHAyYX4TDv5J6PpmapIxggKuMIICqgIBATB8MGgxRjBEBgkqhkiG9w0BCQEWNzg1
# MDI1NzQzK2V2ZXJ5dGhpbmctZXZlcnl0aGluZ0B1c2Vycy5ub3JlcGx5LmdpdGh1
# Yi5jb20xHjAcBgNVBAMMFWV2ZXJ5dGhpbmctZXZlcnl0aGluZwIQQ2LoxSbBgJhL
# Vr+t7yGdCzANBglghkgBZQMEAgEFAKCBhDAYBgorBgEEAYI3AgEMMQowCKACgACh
# AoAAMBkGCSqGSIb3DQEJAzEMBgorBgEEAYI3AgEEMBwGCisGAQQBgjcCAQsxDjAM
# BgorBgEEAYI3AgEVMC8GCSqGSIb3DQEJBDEiBCCmADb4izOTMH9LRnR60Gk/zyaO
# lhJ7f1/G8mD3hIzfSTANBgkqhkiG9w0BAQEFAASCAYBPsV1urAEMa6aW1k7X6bkK
# Pn1iuqEW9Yd9WTmhHa6Ms2t4DQMHFhFwjsqOB4h/EdOE35HO9+4ie96eiRrXy8wI
# E3gY49PjhAR9FeGf7f4KEJ87lVobT8ReGKozcA2YwO80J7ptYdKcoKm2Mt2NEwWc
# Tqh2dlMr0xhMyF7bxhKKby3CNsNaga6ucASXhAqhRtZpR+JuSIufRcFyOB2wr5wG
# z6r0tzzDeZM+fqZfpAm0Ve7JoEs3S4UD59dYhyfgerpgNz92NphkC7CeUVZHacmX
# rFIfY+ySOfPQuyZIs3ffVBY1zHUR1q17pTuWYDEy8+/SAXBZ4/MDbDzb3jb755rH
# SefYPsE1k+Vcix0sC9SCPVCzVuBDo2QKLsVql7HGG4QQU48XORudtzMe8phwqNge
# Fz46w4p1OnIyGu5hoTDI63wzgdp20sCjzpTw3Hm0i1F10MOEBu1b4MKHkSUBveh4
# 5sk1Mp+CS+dKgEvaHRugzVV5cFbYKmB9YoZuqGCkyG0=
# SIG # End signature block
