# Third-Party Notices

Dateiumbenenner (MIT) verwendet folgende Komponenten. Diese werden per NuGet bezogen und unterliegen ihren eigenen Lizenzen.

| Paket | Version | Lizenz | Link |
|---|---|---|---|
| Microsoft.Web.WebView2 | 1.0.3650-prerelease | Microsoft WebView2 SDK License (BSD-artig) | https://www.nuget.org/packages/Microsoft.Web.WebView2/ |
| UglyToad.PdfPig | 1.7.0-custom-5 | Apache-2.0 | https://github.com/UglyToad/PdfPig |
| PdfSharpCore | 1.3.46 | MIT | https://github.com/ststeiger/PdfSharpCore |
| SixLabors.ImageSharp | 4.1.2 |
| NHunspell | 1.2.5554.16953 | LGPL / GPL / MPL (Tri-Lizenz, hier LGPL) | https://www.nuget.org/packages/NHunspell/ |
| System.Drawing.Common | 10.0.0 | MIT | https://github.com/dotnet/runtime |
| System.Net.Http | 4.3.4 | MIT | https://github.com/dotnet/runtime |
| System.Text.RegularExpressions | 4.3.1 | MIT | https://github.com/dotnet/runtime |

## Hinweise

- **SixLabors.ImageSharp**: Kostenlos nutzbar, da dieses Projekt Open Source unter einer OSI-Lizenz (MIT) ist. Wer den Code in einem kommerziellen, geschlossenen Produkt mit mehr als 1 Mio. USD Jahresumsatz verwendet, benötigt eine kommerzielle Lizenz von Six Labors.
- **NHunspell**: Wird unverändert als separate DLL dynamisch eingebunden (LGPL-konform). Quellcode: siehe NuGet-Projektseite.
- **PdfPig**: Apache-2.0 – Copyright-Hinweise und NOTICE der Originalautoren bleiben erhalten.

## Nicht enthalten

- **Hunspell-Wörterbücher** (`Dictionaries/`, de_DE / en_US): GPL/LGPL/MPL bzw. eigene Lizenzen – werden nicht im Repository mitgeliefert. Download über `Download-Dictionaries.ps1`.
- **Ghostscript**: AGPL-3.0 / kommerziell (Artifex) – wird nicht mitgeliefert, nur optional als externes Programm aufgerufen.
- **Microsoft Edge WebView2 Runtime**: Bestandteil von Windows.
