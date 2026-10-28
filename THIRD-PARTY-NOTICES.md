# Third-Party Notices

Dateiumbenenner (MIT) verwendet folgende Komponenten. Diese werden per NuGet bezogen und unterliegen ihren eigenen Lizenzen.

| Paket | Version | Lizenz | Link |
|---|---|---|---|
| Microsoft.Web.WebView2 | 1.0.4258.31 | Microsoft WebView2 SDK License (BSD-artig) | https://www.nuget.org/packages/Microsoft.Web.WebView2/ |
| UglyToad.PdfPig | 1.7.0-custom-5 | Apache-2.0 | https://github.com/UglyToad/PdfPig |
| SymSpell | 6.7.3 | MIT | https://github.com/wolfgarbe/SymSpell |
| System.Drawing.Common | 10.0.12 | MIT | https://github.com/dotnet/runtime |
| System.Net.Http | 4.3.4 | MIT | https://github.com/dotnet/runtime |
| System.Text.RegularExpressions | 4.3.1 | MIT | https://github.com/dotnet/runtime |
| Microsoft.Data.Sqlite | 8.0.11 | MIT | https://github.com/dotnet/efcore |
| SQLitePCLRaw (bundle_e_sqlite3, transitiv) | 2.1.6 | Apache-2.0 | https://github.com/ericsink/SQLitePCL.raw |
| SQLite (e_sqlite3, nativ) | 3.x | Public Domain | https://sqlite.org/copyright.html |
| Microsoft.Recognizers.Text.DateTime / .NumberWithUnit (mit Microsoft.Recognizers.Text, .Number, .Definitions, .DataTypes.TimexExpression, transitiv) | 1.8.13 | MIT | https://github.com/microsoft/Recognizers-Text |
| Microsoft.Extensions.Caching.Memory / .Caching.Abstractions / .Options / .Primitives / .DependencyInjection.Abstractions (transitiv über Recognizers-Text) | 2.2.0 | Apache-2.0 | https://github.com/dotnet/extensions |

### IniGenerator-Plugin (separates Repository)

| Paket | Version | Lizenz | Link |
|---|---|---|---|
| MySqlConnector | 2.6.2 | MIT | https://github.com/mysql-net/MySqlConnector |
| System.Security.Cryptography.ProtectedData | 10.0.12 | MIT | https://github.com/dotnet/runtime |
| Microsoft.Extensions.Logging.Abstractions (transitiv über MySqlConnector) | 8.0.2 | MIT | https://github.com/dotnet/runtime |
| Microsoft.Extensions.DependencyInjection.Abstractions (transitiv über MySqlConnector) | 8.0.2 | MIT | https://github.com/dotnet/runtime |

### Daten

| Daten | Stand | Lizenz | Quelle |
|---|---|---|---|
| GeoNames Postleitzahlen Deutschland (`Dateiumbenenner/Data/GeoNames-DE.zip`, unverändert in die Programmdatei eingebettet) | 24.10.2026 | CC BY 4.0 | https://www.geonames.org/ (https://download.geonames.org/export/zip/DE.zip) |
| OCR-Fachwortliste (`Dateiumbenenner/Data/OcrFachwoerter.txt`, in die Programmdatei eingebettet) | – | MIT (eigene Zusammenstellung, Teil von Dateiumbenenner) | – |
| GLEIF LEI-Daten (Golden Copy, nicht mitgeliefert – nur optionaler Download im Programm, daraus Name/PLZ/Ort aktiver deutscher Firmen) | jeweils aktuell | CC0 1.0 | https://www.gleif.org/ (https://goldencopy.gleif.org/) |

- **GeoNames**: Postleitzahlen-Daten von GeoNames (www.geonames.org), lizenziert unter Creative Commons Attribution 4.0 (https://creativecommons.org/licenses/by/4.0/). Die Daten werden unverändert mitgeliefert (SHA-256 der ZIP: `295DC0017D2DA8C633C748A24DF989A50F039DCD96187B3F0C6EBB30DA10D15C`); Aktualisierungen im Programm laden ausschließlich von der oben genannten Adresse.

## Hinweise

- **Microsoft.Web.WebView2**: Copyright (C) Microsoft Corporation. All rights reserved. Lizenztext siehe unten.
- **SymSpell**: Copyright (C) 2025 Wolf Garbe, MIT. Lizenztext siehe unten. Die englischen Beispiel-Wörterbücher des Pakets werden nicht mitgeliefert.
- **PdfPig**: Apache-2.0 – Copyright-Hinweise und NOTICE der Originalautoren bleiben erhalten.

## Nicht enthalten

- **Windows-Rechtschreibprüfung** (Spell Checking API, Sprachpakete): Bestandteil von Windows, wird von der OCR-Korrektur nur genutzt.
- **Ghostscript**: AGPL-3.0 / kommerziell (Artifex) – wird nicht mitgeliefert, nur optional als externes Programm aufgerufen.
- **Microsoft Edge WebView2 Runtime**: Bestandteil von Windows.

## Lizenztext Microsoft.Web.WebView2

```text
Copyright (C) Microsoft Corporation. All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are
met:

   * Redistributions of source code must retain the above copyright
notice, this list of conditions and the following disclaimer.
   * Redistributions in binary form must reproduce the above
copyright notice, this list of conditions and the following disclaimer
in the documentation and/or other materials provided with the
distribution.
   * The name of Microsoft Corporation, or the names of its contributors 
may not be used to endorse or promote products derived from this
software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
"AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

## Lizenztext SymSpell

```
MIT License

Copyright (C) 2025 Wolf Garbe

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

