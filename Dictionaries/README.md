# Hunspell-Wörterbücher für OCR-Rechtschreibkorrektur

## ?? Wichtig: Sichere Download-Quellen verwenden!

Aufgrund von SSL-Zertifikatsproblemen bei `cgit.freedesktop.org` empfehlen wir die GitHub-Mirror-URLs.

## Download-Quellen

### ?? GitHub Mirror (EMPFOHLEN - Sicher & Zuverlässig)

Die offiziellen LibreOffice-Wörterbücher sind auf GitHub gespiegelt:

**Repository:** https://github.com/LibreOffice/dictionaries

### Deutsch (Duden-kompatibel)
- **GitHub:** https://github.com/LibreOffice/dictionaries/tree/master/de
- **Dateien:** `de_DE_frami.aff` und `de_DE_frami.dic`

### Englisch
- **GitHub:** https://github.com/LibreOffice/dictionaries/tree/master/en
- **Dateien:** `en_US.aff` und `en_US.dic` (Amerikanisch) oder `en_GB.aff` und `en_GB.dic` (Britisch)

### Weitere Sprachen
- **Alle Sprachen:** https://github.com/LibreOffice/dictionaries/tree/master

## Installation

### Option 1: Automatischer Download (EMPFOHLEN)

Führen Sie einfach `Download-Dictionaries.bat` aus:
1. Doppelklick auf `Download-Dictionaries.bat`
2. Warten Sie bis der Download abgeschlossen ist
3. Fertig!

### Option 2: Manueller Download via PowerShell

```powershell
# Ordner erstellen
New-Item -ItemType Directory -Path "Dictionaries" -Force

# Deutsch herunterladen
Invoke-WebRequest -Uri "https://raw.githubusercontent.com/LibreOffice/dictionaries/master/de/de_DE_frami.aff" -OutFile "Dictionaries\de_DE.aff"
Invoke-WebRequest -Uri "https://raw.githubusercontent.com/LibreOffice/dictionaries/master/de/de_DE_frami.dic" -OutFile "Dictionaries\de_DE.dic"

# Englisch herunterladen
Invoke-WebRequest -Uri "https://raw.githubusercontent.com/LibreOffice/dictionaries/master/en/en_US.aff" -OutFile "Dictionaries\en_US.aff"
Invoke-WebRequest -Uri "https://raw.githubusercontent.com/LibreOffice/dictionaries/master/en/en_US.dic" -OutFile "Dictionaries\en_US.dic"
```

### Option 3: Manueller Download über Browser

1. Gehen Sie zu: https://github.com/LibreOffice/dictionaries
2. Navigieren Sie zum gewünschten Sprachordner (z.B. `de` für Deutsch)
3. Klicken Sie auf die `.aff` und `.dic` Dateien
4. Klicken Sie auf "Raw" oder "Download"
5. Speichern Sie beide Dateien im `Dictionaries`-Ordner
6. Benennen Sie um:
   - `de_DE_frami.aff` ? `de_DE.aff`
   - `de_DE_frami.dic` ? `de_DE.dic`

### Option 4: Von LibreOffice Extensions Portal

1. Besuchen Sie: https://extensions.libreoffice.org
2. Suchen Sie nach:
   - "German Dictionary" für Deutsch
   - "English Dictionary" für Englisch
3. Download der Extension (.oxt Datei)
4. Entpacken Sie die .oxt Datei (es ist eine ZIP-Datei)
5. Kopieren Sie die `.aff` und `.dic` Dateien in den `Dictionaries`-Ordner

## Dateistruktur

Nach der Installation sollte Ihre Verzeichnisstruktur so aussehen:

```
Dateiumbenenner.exe
OcrCorrections.txt
Dictionaries/
  ??? de_DE.aff      (Deutsche Affix-Regeln)
  ??? de_DE.dic      (Deutsche Wortliste, ~6 MB)
  ??? en_US.aff      (Englische Affix-Regeln)
  ??? en_US.dic      (Englische Wortliste, ~2 MB)
```

## Aktivierung

Öffnen Sie `OcrCorrections.txt` und fügen Sie hinzu:

```
@spellcheck=de_DE,en_US
```

## Verfügbare Sprachen

Alle verfügbaren Sprachen finden Sie hier:
https://github.com/LibreOffice/dictionaries/tree/master

Beispiele:
- `de_DE` - Deutsch (Deutschland)
- `de_AT` - Deutsch (Österreich)
- `de_CH` - Deutsch (Schweiz)
- `en_US` - Englisch (USA)
- `en_GB` - Englisch (UK)
- `fr_FR` - Französisch
- `es_ES` - Spanisch
- `it_IT` - Italienisch
- `nl_NL` - Niederländisch
- `pt_BR` - Portugiesisch (Brasilien)

## Hinweise

- Die Wörterbuch-Dateien sind **nicht im Repository enthalten** (Lizenzgründe)
- Jedes Wörterbuch besteht aus **zwei Dateien**: `.aff` (Affix-Regeln) und `.dic` (Wortliste)
- Größere Wörterbücher (wie Deutsch mit ~150.000 Wörtern) können die Verarbeitung leicht verlangsamen
- Sie können mehrere Sprachen gleichzeitig aktivieren

## Lizenzinformationen

Die Hunspell-Wörterbücher stehen unter freien Lizenzen:
- **Deutsch:** GPL v2, LGPL oder CC-BY-SA
- **Englisch:** LGPL, BSD
- **Quelle:** LibreOffice Dictionaries Project (https://github.com/LibreOffice/dictionaries)

Alle Wörterbücher sind Open Source und frei verwendbar.

## Problemlösung

### "Fehler beim Herunterladen"
- Prüfen Sie Ihre Internetverbindung
- Falls GitHub blockiert ist, verwenden Sie Option 4 (LibreOffice Extensions)
- Firewall/Proxy könnte den Download blockieren

### "Rechtschreibprüfung funktioniert nicht"
1. Prüfen Sie ob beide Dateien (.aff UND .dic) vorhanden sind
2. Prüfen Sie die Dateinamen (müssen exakt `de_DE.aff` etc. heißen)
3. Prüfen Sie ob `@spellcheck=de_DE,en_US` in OcrCorrections.txt steht
4. Starten Sie die Anwendung neu

### "SSL/TLS Fehler"
- Nutzen Sie die GitHub-URLs (siehe oben)
- Oder laden Sie manuell über den Browser herunter

## Unterstützung

Bei Problemen:
1. Prüfen Sie diese Anleitung
2. Verwenden Sie den automatischen Download (`Download-Dictionaries.bat`)
3. Versuchen Sie den manuellen Download über GitHub
