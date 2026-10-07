# Dateiumbenenner

Version 3.12 – WPF-Tool (.NET 8) zum automatischen Umbenennen, Zusammenführen und Komprimieren von PDF-Dokumenten (Rechnungen, Belege).

## Funktionen
- Erkennung von Dokumenttyp, Rechnungs-/Belegnummer und Datum
- Präfix-Umbenennung
- PDFs zusammenführen
- PDF-Vorschau (WebView2)
- OCR-Korrektur (Hunspell), PDF-Komprimierung

## Systemanforderungen
- Windows 10 (1809+) / 11, x64
- .NET 8 Desktop Runtime
- Microsoft Edge WebView2 Runtime
- Optional: Ghostscript, Hunspell-Wörterbücher (`Download-Dictionaries.ps1`)

## Build
```
dotnet build Dateiumbenenner.slnx -c Release
```

## Integrität / Signatur
Releases enthalten `SHA256SUMS.txt` (SHA-256 aller Dateien), die Signatur `SHA256SUMS.txt.p7s` und das öffentliche, selbst ausgestellte Zertifikat `Dateiumbenenner-CodeSigning.cer`. EXE/DLL des Projekts sind Authenticode-signiert.

Prüfen:
```powershell
Get-FileHash .\Dateiumbenenner.exe -Algorithm SHA256
Get-AuthenticodeSignature .\Dateiumbenenner.exe
```
Hinweis: Das Zertifikat ist selbst ausgestellt; Windows zeigt es daher als "nicht vertrauenswürdig" an, solange es nicht manuell importiert wird.

## Lizenz
MIT – siehe [LICENSE](LICENSE). Drittanbieter: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Haftungsausschluss
Die Software wird ohne Gewährleistung bereitgestellt. Nutzung auf eigene Gefahr; vor dem Umbenennen Sicherungen anlegen.

## Kontakt
GitHub: [everything-everything](https://github.com/everything-everything) · 85025743+everything-everything@users.noreply.github.com
