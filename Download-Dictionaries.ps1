# Hunspell Dictionary Downloader
# Dieses Skript lädt automatisch deutsche und englische Wörterbücher herunter

$ErrorActionPreference = "Stop"

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "  Hunspell Dictionary Downloader" -ForegroundColor Cyan
Write-Host "  für OCR-Rechtschreibkorrektur" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host ""

# Erstelle Dictionaries-Ordner falls nicht vorhanden
$dictDir = "Dictionaries"
if (-not (Test-Path $dictDir)) {
    New-Item -ItemType Directory -Path $dictDir | Out-Null
    Write-Host "? Dictionaries-Ordner erstellt" -ForegroundColor Green
}

# Funktion zum Download
function Download-Dictionary {
    param(
        [string]$Language,
        [string]$AffUrl,
        [string]$DicUrl,
        [string]$DisplayName
    )
    
    Write-Host ""
    Write-Host "Lade $DisplayName Wörterbuch herunter..." -ForegroundColor Yellow
    
    try {
        $affPath = Join-Path $dictDir "$Language.aff"
        $dicPath = Join-Path $dictDir "$Language.dic"
        
        # Download .aff Datei
        Write-Host "  ? $Language.aff ... " -NoNewline
        Invoke-WebRequest -Uri $AffUrl -OutFile $affPath -UseBasicParsing
        Write-Host "?" -ForegroundColor Green
        
        # Download .dic Datei
        Write-Host "  ? $Language.dic ... " -NoNewline
        Invoke-WebRequest -Uri $DicUrl -OutFile $dicPath -UseBasicParsing
        Write-Host "?" -ForegroundColor Green
        
        Write-Host "? $DisplayName erfolgreich heruntergeladen" -ForegroundColor Green
        return $true
    }
    catch {
        Write-Host "?" -ForegroundColor Red
        Write-Host "  Fehler: $_" -ForegroundColor Red
        return $false
    }
}

# Download Deutsch (Duden-kompatibel) - AKTUALISIERT: GitHub Mirror
$deSuccess = Download-Dictionary `
    -Language "de_DE" `
    -AffUrl "https://raw.githubusercontent.com/LibreOffice/dictionaries/master/de/de_DE_frami.aff" `
    -DicUrl "https://raw.githubusercontent.com/LibreOffice/dictionaries/master/de/de_DE_frami.dic" `
    -DisplayName "Deutsch (de_DE)"

# Download Englisch (US) - AKTUALISIERT: GitHub Mirror
$enSuccess = Download-Dictionary `
    -Language "en_US" `
    -AffUrl "https://raw.githubusercontent.com/LibreOffice/dictionaries/master/en/en_US.aff" `
    -DicUrl "https://raw.githubusercontent.com/LibreOffice/dictionaries/master/en/en_US.dic" `
    -DisplayName "Englisch (en_US)"

# Zusammenfassung
Write-Host ""
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "  Download abgeschlossen" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan

if ($deSuccess -and $enSuccess) {
    Write-Host "? Alle Wörterbücher erfolgreich heruntergeladen!" -ForegroundColor Green
    Write-Host ""
    Write-Host "Nächste Schritte:" -ForegroundColor Yellow
    Write-Host "1. Öffnen Sie die Datei 'OcrCorrections.txt'" -ForegroundColor White
    Write-Host "2. Fügen Sie folgende Zeile hinzu:" -ForegroundColor White
    Write-Host "   @spellcheck=de_DE,en_US" -ForegroundColor Cyan
    Write-Host "3. Starten Sie die Anwendung neu" -ForegroundColor White
}
else {
    Write-Host "? Einige Downloads sind fehlgeschlagen" -ForegroundColor Yellow
    Write-Host "Bitte prüfen Sie Ihre Internetverbindung und versuchen Sie es erneut" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Drücken Sie eine beliebige Taste zum Beenden..." -ForegroundColor Gray
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")

# SIG # Begin signature block
# MIIH/AYJKoZIhvcNAQcCoIIH7TCCB+kCAQExDzANBglghkgBZQMEAgEFADB5Bgor
# BgEEAYI3AgEEoGswaTA0BgorBgEEAYI3AgEeMCYCAwEAAAQQH8w7YFlLCE63JNLG
# KX7zUQIBAAIBAAIBAAIBAAIBADAxMA0GCWCGSAFlAwQCAQUABCCh2y1rKZa7eBtD
# NmU96kyzo3YHc9Dt1aP7NPnwkFac2aCCBKQwggSgMIIDCKADAgECAhBDYujFJsGA
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
# BgorBgEEAYI3AgEVMC8GCSqGSIb3DQEJBDEiBCBO3ar0xzhtmg7PssJVTCDEfQmz
# H3OegnAncHSj8IMtaDANBgkqhkiG9w0BAQEFAASCAYBFjshmPejyDG7WzkRB4ntA
# ItoKAw2cnXIoFnm18OekfpzhRgOrZjXRMtpuBO4KHJKh0g0fVG0xIxTM0uqBIJoD
# GnsBVIdUUOK8QMFkjflhmoKOjem2KroYelo2tw8NvBrmurPFTrVWZTbwLr4pj7Gn
# sK/Cd2sGVqoM2grsNkNyPO0w5leNE6UvmJQ9MnHQ4kZNi45nxVO0gr/YCs79Otm5
# BW+0Ton60bCODgBSwaE1GLi/E7KOkS81+VDzMqthBZkzlQwSa8ADqcD2GTxzqW81
# UkbjI/WfVo4H8Nw+aavsFpGtfsZXJN1G3Dduh3IV/m2Wu+V7jv3OvFNvrVp3xtew
# cyYji6bK267ep9KnoSS8ZH/c2Quas/DmL8mLHj4Xh0TQ+kK7QluedovjVlqbzwVg
# uyGiUTim0DXJOWYrmzKKh/pQg9Grmx/ivI45qSdLfoYe0qFRXwfNJ8+F4qq917V8
# FNTmYoWY12/P+wPQdBFi2im71zMUHmqIz2x8BR6kHkM=
# SIG # End signature block
