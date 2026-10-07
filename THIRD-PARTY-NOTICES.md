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

## Hinweise

- **WeCantSpell.Hunspell**: Wird unverändert als separate DLL dynamisch eingebunden (LGPL-konform). Quellcode: siehe NuGet-Projektseite.
- **PdfPig**: Apache-2.0 – Copyright-Hinweise und NOTICE der Originalautoren bleiben erhalten.

## Nicht enthalten

- **Hunspell-Wörterbücher** (`Dictionaries/`, de_DE / en_US): GPL/LGPL/MPL bzw. eigene Lizenzen – werden nicht im Repository mitgeliefert. Download über `Download-Dictionaries.ps1`.
- **Ghostscript**: AGPL-3.0 / kommerziell (Artifex) – wird nicht mitgeliefert, nur optional als externes Programm aufgerufen.
- **Microsoft Edge WebView2 Runtime**: Bestandteil von Windows.
