# Third-Party Notices

Dateiumbenenner (MIT) verwendet folgende Komponenten. Diese werden per NuGet bezogen und unterliegen ihren eigenen Lizenzen.

| Paket | Version | Lizenz | Link |
|---|---|---|---|
| Microsoft.Web.WebView2 | 1.0.4258.31 | Microsoft WebView2 SDK License (BSD-artig) | https://www.nuget.org/packages/Microsoft.Web.WebView2/ |
| UglyToad.PdfPig | 1.7.0-custom-5 | Apache-2.0 | https://github.com/UglyToad/PdfPig |
| WeCantSpell.Hunspell | 7.0.1 | MPL-1.1 / GPL-2.0 / LGPL-2.1 (hier LGPL) | https://github.com/aarondandy/WeCantSpell.Hunspell |
| System.Drawing.Common | 10.0.12 | MIT | https://github.com/dotnet/runtime |
| System.Net.Http | 4.3.4 | MIT | https://github.com/dotnet/runtime |
| System.Text.RegularExpressions | 4.3.1 | MIT | https://github.com/dotnet/runtime |
| Microsoft.Data.Sqlite | 8.0.11 | MIT | https://github.com/dotnet/efcore |
| SQLitePCLRaw (bundle_e_sqlite3, transitiv) | 2.1.6 | Apache-2.0 | https://github.com/ericsink/SQLitePCL.raw |
| SQLite (e_sqlite3, nativ) | 3.x | Public Domain | https://sqlite.org/copyright.html |

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

- **GeoNames**: Postleitzahlen-Daten von GeoNames (www.geonames.org), lizenziert unter Creative Commons Attribution 4.0 (https://creativecommons.org/licenses/by/4.0/). Die Daten werden unverändert mitgeliefert (SHA-256 der ZIP: `295DC0017D2DA8C633C748A24DF989A50F039DCD96187B3F0C6EBB30DA10D15C`); Aktualisierungen im Programm laden ausschließlich von der oben genannten Adresse.

## Hinweise

- **Microsoft.Web.WebView2**: Copyright (C) Microsoft Corporation. All rights reserved. Lizenztext siehe unten.
- **WeCantSpell.Hunspell**: Wird unverändert als separate DLL dynamisch eingebunden (LGPL-konform). Quellcode: siehe NuGet-Projektseite.
- **PdfPig**: Apache-2.0 – Copyright-Hinweise und NOTICE der Originalautoren bleiben erhalten.

## Nicht enthalten

- **Hunspell-Wörterbücher** (`Dictionaries/`, de_DE / en_US): GPL/LGPL/MPL bzw. eigene Lizenzen – werden nicht im Repository mitgeliefert. Download über `Download-Dictionaries.ps1`.
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

