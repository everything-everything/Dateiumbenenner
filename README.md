# Dateiumbenenner

Version 3.12 – WPF-Tool (.NET 8) zum automatischen Umbenennen, Zusammenführen und Komprimieren von PDF-Dokumenten (Rechnungen, Belege).

## Funktionen
- Erkennung von Dokumenttyp, Rechnungs-/Belegnummer und Datum
- Präfix-Umbenennung
- PDFs zusammenführen
- PDF-Vorschau (WebView2)
- OCR-Korrektur (SymSpell, mitgelieferte Fachwortliste, gelernte Wörter, Windows-Rechtschreibprüfung – ohne Download), PDF-Komprimierung
- PLZ-Prüfung mit mitgelieferter Postleitzahlen-Liste (GeoNames, CC BY 4.0), optional geprüfte Aktualisierung

NuGet-Pakete und Lizenzen
Paket	Version	Lizenz
Microsoft.Data.Sqlite	8.0.11	MIT
SQLitePCLRaw.bundle_e_sqlite3 (transitiv)	2.1.6	Apache-2.0
Microsoft.Web.WebView2	1.0.4258.31	Microsoft WebView2 SDK License (BSD-artig)
UglyToad.PdfPig	1.7.0-custom-5	Apache-2.0
SymSpell	6.7.3	MIT
System.Drawing.Common	10.0.12	MIT
System.Net.Http	4.3.4	MIT
System.Text.RegularExpressions	4.3.1	MIT

Dateiumbenenner selbst steht unter MIT-Lizenz. Details: THIRD-PARTY-NOTICES.md.



## Systemanforderungen
- Windows 10 (1809+) / 11, x64
- .NET 8 Desktop Runtime
- Microsoft Edge WebView2 Runtime
- Optional: Ghostscript

## Build
```
dotnet build Dateiumbenenner.slnx -c Release
```

### Release-Pakete
`Build-SignedRelease.ps1 -Version <Version>` erstellt nur das Hauptprogramm.
Wenn das private INI-Generator-Projekt lokal vorhanden ist, erstellt
`Build-SignedRelease.ps1 -Version <Version> -IncludeIniGeneratorPlugin`
zusätzlich ein **separates** Plugin-ZIP mit eigener SHA-256-Datei unter `release`.
Das Plugin gehört nicht in das Hauptprogramm-ZIP und wird nicht automatisch hochgeladen.

Für das Plugin ausschließlich das vom Release-Build erzeugte ZIP aus
`Plugins\IniGeneratorPlugin\dist` verwenden, nicht den vollständigen `bin`- oder
Publish-Ordner packen: Dieser enthält auch große Abhängigkeiten des Hauptprogramms
und native Bibliotheken für andere Plattformen.
Das Plugin-ZIP enthält den Ordner `Dateiumbenenner.IniGenerator.Plugin` mit den
Plugin-Dateien sowie `Server\dateiumbenenner_api.php` für die separate Installation
auf dem Webserver. Der Plugin-Ordner wird unter `Plugins` neben der EXE installiert;
die PHP-Datei gehört auf den Webserver, nicht in das Hauptprogramm-Verzeichnis.

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
