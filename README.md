# Dateiumbenenner

Version 5.1.2661 – WPF-Tool (.NET 8) zum automatischen Umbenennen, Zusammenführen und Komprimieren von PDF-Dokumenten (Rechnungen, Belege).

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

Mit `-GleifDaten` entsteht zusätzlich `release\GleifDaten-<Version>.zip` mit dem
GLEIF-Firmenverzeichnis (aktive deutsche Firmen, CC0 1.0) aus der lokalen `gleif_de.dat`
(oder `-GleifDatenOrdner <Ordner>`). Das Paket wird nicht hochgeladen und im Programm
nur manuell installiert (Extras → „GLEIF-Paket installieren…“), nie automatisch.

Für das Plugin ausschließlich das vom Release-Build erzeugte ZIP aus
`Plugins\IniGeneratorPlugin\dist` verwenden, nicht den vollständigen `bin`- oder
Publish-Ordner packen: Dieser enthält auch große Abhängigkeiten des Hauptprogramms
und native Bibliotheken für andere Plattformen.
Das Plugin-ZIP enthält den Ordner `Dateiumbenenner.IniGenerator.Plugin` mit den
Plugin-Dateien sowie `Server\dateiumbenenner_api.php` und `Server\partner_create.php` für die separate Installation
auf dem Webserver. Der Plugin-Ordner wird unter `Plugins` neben der EXE installiert;
die PHP-Dateien gehören auf den Webserver, nicht in das Hauptprogramm-Verzeichnis.

## Erkennung der Belegdaten
- **Belegart:** Ein alleinstehender Titel zählt am meisten. Nummernbezeichnungen
  (`Rechnungsnummer`, `Rechnungs-Nr.`, `Lieferscheinnummer`, `Lieferschein-Nr.`,
  `Lieferschein Nummer`, ebenso Gutschrift und Angebot) sind ein eigener Hinweis.
  Titel und passende Nummernbezeichnung zusammen (z. B. `Lieferschein` +
  `Lieferscheinnummer`) sind ein starker Hinweis. `Belegnummer` ist neutral.
  Bezüge auf andere Belege (`Ihr Angebot vom …`, `zu Lieferschein-Nr. …`,
  `gemäß unserem Angebot`) werden praktisch nicht gewertet.
- **Betrag:** Eindeutige Endbeträge (`Gesamtbetrag`, `Rechnungsbetrag`,
  `Endbetrag`, `Zahlbetrag`, `zu zahlen` …) haben Vorrang vor allgemeinen Begriffen
  (`Summe`, `Gesamt`, `Brutto`, `Total`).
  - Netto-, Zwischensummen- und Steuerzeilen gelten nicht als Endbetrag.
  - Der Betrag darf bis zu drei Zeilen unter dem Begriff stehen.
  - Neben dem Begriff wird auch `1 234,56` erkannt.
  - Bei mehreren Belegen in einer Datei werden Beträge außerhalb des Belegs der
    gewählten Firma abgewertet.
- **Fahrgestellnummern (FIN/VIN):** 17-stellige FINs erscheinen als
  Nummernvorschlag, mit Bezeichnung `Fahrgestell-Nr.`/`FIN`/`VIN` vorrangig. Sie
  stehen hinter Rechnungs- und Belegnummern, außer Regeln oder eine gelernte
  Fundstelle bevorzugen sie.
- **Firmennamen:** Gruß- und Dankesformeln (`Mit freundlichen Grüßen`,
  `Vielen Dank`, `Sehr geehrte …`, `MfG` usw.) werden nie als Firma vorgeschlagen,
  gelernt oder als Absender übernommen – unabhängig von der Ausschlussliste.
  Steht eine Firma mit Rechtsform direkt dahinter, wird nur diese übernommen.
- **INI-Generator-Plugin:** Die bestehende Firmen-Vorauswahl wird automatisch
  nach Inhalt/Formulartext und Straßen nachgefiltert. Reine Beträge und Mengen
  erscheinen weder als Firmen- noch als Inhaltsvorschläge. Eine bearbeitbare
  sichere Inhaltsliste gilt im aktiven Profil; Textnachweise, echte Absender
  und bewusste Feldzuordnungen bleiben geschützt. Leistungsangaben wie
  `Teilleistungsrechnung Keller`, `Inbetriebnahme Heizung`, `Außenputz` und
  der OCR-Fehler `Zusotrereinbarung` bleiben als Inhaltsvorschläge erhalten.
  Abrechnungstexte wie `Vertragswert`, `Offener Posten`, `Bisher gezahlt` und
  `Stand 15.05.2024` werden als Formulartext verschoben. Kurze Fragmente
  (`Bisher`, `Ergibt`, `Stand`) brauchen den passenden
  Kontext im Originaltext. Artikel- und Leistungsbeschreibungen aus Tabellen und
  Kassenbons werden ebenfalls als Inhalt angeboten und vor allgemeinen
  Belegüberschriften bevorzugt. Bei Pressfittings wird `AG` nur mit benachbarter
  Artikelnummer und Stückzahl als Außengewinde statt als Rechtsform gewertet;
  ausdrückliche Absender bleiben geschützt. Grußformeln, Seitenangaben und
  konkrete Nummern-/Betragszeilen werden nicht automatisch als Inhalt ausgewählt. Vorschläge,
  die nur den wörtlichen Anfang einer bereits erkannten Artikel- oder
  Formularangabe enthalten (etwa `EPS D`), bleiben als Inhaltsvorschläge erhalten; für die
  automatische Auswahl wird die vollständige belegte Angabe verwendet.
  Die Textfundstellensuche vermeidet den wiederholten Aufbau großer Suchautomaten
  je OCR-Begriff. Ohne zusätzliche Firmenregeln wird ein Beleg nicht doppelt klassifiziert.
  Bestätigte Formularbegriffe für dieselbe Firma und Belegart haben Vorrang.
  Ohne solche Vorgaben wird eine Artikel-/Leistungsangabe bevorzugt; fehlt sie,
  wird ein tatsächlich vorhandenes, stabiles Formularlabel wie
  `Skontierfahiger Rechnungsbetrag` oder `Zahlungsbedingungen` ausgewählt und
  ins XML übernommen. Konkrete Beträge und wechselnde Datums-/Seitenwerte werden
  dabei nicht als Ersatzinhalt verwendet. Das Inhaltsfeld markiert die Auswahl
  auch in seiner Auswahlliste. Eindeutige Bank-/Registerfragmente bleiben keine
  Firmenvorschläge; bei gleichem Dateinamensbezug werden vollständige Firmennamen
  mit Rechtsform vor abgebrochenen OCR-Varianten bevorzugt.
  Bewusste manuelle Zuordnungen bleiben erhalten. Das Plugin wird separat ausgeliefert.
- **Inhalt übertragen (INI-Generator):** Es werden nur Zeile und Spalte der
  Quellfundstelle verwendet. Im Ziel wird der passende eigene Inhaltsvorschlag
  ausgewählt; der Inhaltstext der Quelle wird weder kopiert noch zur Zielliste
  hinzugefügt. Kleine Verschiebungen (eine Zeile, bis zu acht Spalten) werden
  toleriert. Fehlende oder mehrdeutige Treffer werden gezählt und übersprungen;
  bisherige Zielwerte bleiben erhalten. Firmen- und Belegartübertragung kopieren
  dabei weiterhin keinen Inhaltstext.
  Automatisch leere INI-Inhalte verhindern keine spätere Vorauswahl mehr.
  Neu gespeicherte bewusste Leerungen bleiben erhalten; ältere leere Speicherstände
  ohne Kennzeichnung werden erneut automatisch geprüft.
- **Vorauswahl bei Firma/Belegart übertragen:** Der ausgewählte, im jeweiligen
  Belegtext nachgewiesene Inhalt wird zusätzlich bewertet. Gute Übereinstimmung
  stützt die Vorauswahl; reine Summen-/Betragslabels gelten nicht als Zusatznachweis.
  Fehlender, allgemeiner oder unterschiedlicher Artikel-/Leistungsinhalt blockiert
  die bisherigen Kriterien nicht. Bei Firmen braucht es weiterhin einen belegten
  Namen bzw. Firmenvorschlag – gleicher Inhalt allein reicht nicht. Unterschiedliche
  Belegarten schließen dieselbe Firma nicht aus. Bei der Belegartübertragung wird
  eine eindeutig andere Formularüberschrift nicht automatisch angehakt, auch bei
  gleichem Nummernaufbau. Mehrdeutige Überschriften und bloße Belegverweise gelten
  nicht als Widerspruch. Die Prüfung erfolgt pro Beleg statt für die gesamte Gruppe.
  Inhalt und Begründung werden in beiden Dialogen angezeigt. Manuelle Häkchen und
  ausdrücklich markierte Belege beim Firmenübertragen bleiben maßgeblich; die
  positionsbasierte Inhaltsübertragung bleibt unverändert.
- **Titelleiste:** Zeigt Profil, Arbeitsordner des sichtbaren Registers und
  Programmpfad. Die Pfadanzeige folgt Ordner- und Registerwechseln.
- **XML-Editor:** Zusätzlich zur Wortmarkierung wird die vollständige ausgewählte
  Textzeile hervorgehoben, auch bei Zeilenumbruch in der Vorschau.
  Die Fundzeilen der übrigen Formular-Erkennungen bleiben dezent gelborange
  markiert; die aktive Regel wird kräftiger blau umrahmt. Ohne aktive Auswahl
  bleiben alle gefundenen Formularzeilen sichtbar markiert.
  Steht die ausgewählte Firma (auch hinter `Empfänger:`) in einer XML-Rohzeile,
  wird eine fehlende Formular-Erkennung aus ihrer tatsächlichen Schreibweise
  ergänzt. Das gilt auch bei vorhandenen Vorlagen und beim Öffnen älterer XMLs.
  Auch der ausgewählte Inhalt wird mit seiner tatsächlichen Schreibweise aus
  einer XML-Rohzeile als fehlende Formular-Erkennung ergänzt, beim Erzeugen,
  Öffnen, Aktualisieren und Anwenden der Belegvorgaben. Ohne Textnachweis wird
  kein Begriff erfunden; eine nicht mögliche Ergänzung wird ausdrücklich gemeldet.
  Bereits gültige Erkennungen und feste Werte bleiben erhalten; vorhandene XMLs
  werden dabei nicht umgeordnet oder automatisch gespeichert. Die Ergänzung
  erscheint in der Vorschau und wird erst mit **Speichern** dauerhaft übernommen.
  Fehlt eine freie Regelzeile mit verträglicher Suchreihenfolge, wird dies gemeldet.
- **Doppelte Belegerkennungen aktualisieren:** Im Duplikatvergleich die vorhandene
  XML unten auswählen und den gleichnamigen Button drücken. Eine kurze Vorschau
  zeigt bisherige und aktuelle Formular-Erkennungen sowie die Kreditor-/Debitornummer.
  **Aktualisieren / überschreiben** ersetzt die komplette Ziel-XML, nicht nur ihre
  Formular-Erkennungen. Die aktuelle Editor-Fassung einschließlich ungespeicherter
  Änderungen kann verwendet werden; eine eigene gespeicherte XML ist dafür nicht nötig.
  Dateiname und Kennung des Ziels bleiben erhalten, die Quelldatei bleibt unverändert.
  Vor dem atomaren Austausch wird eine eigene `.bak`-Sicherung angelegt.
  Zwischenzeitlich geänderte Ziel-Dateien werden nicht überschrieben. Orange Treffer
  mit nur gleicher Firma, aber anderer Belegart sind von der Aktualisierung ausgeschlossen.
- **Neue Belegerkennungen kopieren:** **Markierte neue kopieren** übernimmt auch die
  aktuelle, ungespeicherte Editor-Fassung. Ohne eigene XML wird sie aus dem vorhandenen
  Belegtext erzeugt; vorheriges Speichern ist nicht erforderlich. **Neue markieren**
  berücksichtigt diese Belege ebenfalls. Eine Rechnung darf zusätzlich zu einem
  vorhandenen Lieferschein derselben Firma kopiert werden. Gleiche Firma und Belegart,
  gleiche Kennung und bereits vorhandene Dateinamen werden nicht erneut kopiert oder
  überschrieben – auch wenn der Treffer erst während des Kopiervorgangs hinzukommt.
- **XML-Editor-Vorgaben** gelten nur für genau die Firma und Belegart, für die sie
  gespeichert wurden. Ältere, firmenweite Vorgaben werden nicht mehr angewendet.
  Wird eine Vorgabezeile im Beleg nicht über Rohzeile, Erkennungsmerkmal oder
  Begriff gefunden, füllt sie nur leere Felder.

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
