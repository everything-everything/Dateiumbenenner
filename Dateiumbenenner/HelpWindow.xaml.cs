using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;

namespace Dateiumbenenner
{
    public partial class HelpWindow : Window
    {
        public enum Page { Help, Licenses, About }

        public const string GitHubUser = "everything-everything";
        public const string GitHubUrl = "https://github.com/everything-everything";
        public const string ContactEmail = "85025743+everything-everything@users.noreply.github.com";

        public static string AppVersion =>
            Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

        public HelpWindow(Page page)
        {
            InitializeComponent();
            Title = $"Dateiumbenenner {AppVersion} – Hilfe";
            TxtHelp.Text = HelpText;
            TxtLicenses.Text = LicenseText;
            TxtAbout.Text = AboutText;
            Tabs.SelectedIndex = (int)page;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }

        private static string HelpText => $@"DATEIUMBENENNER – PROGRAMMHILFE
Version {AppVersion}

1. WAS DAS PROGRAMM MACHT
-------------------------
Der Dateiumbenenner liest PDF-Dokumente (z. B. Rechnungen, Belege) aus einem
Ordner ein, erkennt darin Dokumenttyp, Rechnungs-/Belegnummer und Datum und
schlägt daraus einen einheitlichen Dateinamen vor. Zusätzlich:
 - Präfix-Umbenennung mehrerer Dateien in einem Schritt
 - Zusammenführen mehrerer PDFs zu einer Datei
 - PDF-Vorschau (WebView2)
 - optionale OCR-Textkorrektur per Rechtschreibwörterbuch (Hunspell)
 - optionale PDF-Komprimierung

2. SYSTEMANFORDERUNGEN
----------------------
 - Windows 10 (ab 1809) oder Windows 11, x64
 - .NET 8 Desktop Runtime (sofern nicht als self-contained veröffentlicht)
 - Microsoft Edge WebView2 Runtime (für die PDF-Vorschau, in Windows 11 enthalten)
 - Optional: Ghostscript (für die Ghostscript-Komprimierung)
 - Optional: Hunspell-Wörterbücher im Ordner 'Dictionaries'
   (per Download-Dictionaries.ps1 / .bat herunterladbar)
 - ca. 200 MB freier Speicher, 4 GB RAM empfohlen

3. BEDIENUNG
------------
Registerkarte 'Umbenennen':
 1. Menü Datei > Ordner öffnen (oder Schaltfläche 'Ordner wählen').
 2. Datei in der Liste auswählen – rechts erscheinen Vorschau und erkannte Werte.
 3. Dokumenttyp, Nummer und Datum prüfen bzw. aus den Vorschlägen wählen.
 4. 'Ausgewählte umbenennen' oder 'Alle umbenennen' klicken.
 - Filterfeld über der Liste: schränkt die Liste ein.
 - 'Natürliche Dateinamensortierung': sortiert 2 vor 10, behandelt _ wie -.
 - 'OCR Korrektur': korrigiert typische Texterkennungsfehler.
 - 'Zur Liste Zusammenfügen hinzufügen': übernimmt Dateien in den Merge-Tab.

Registerkarte 'Präfix-Umbenennung':
 Ordner wählen, neues Präfix eingeben, Vorschau prüfen, umbenennen.

Registerkarte 'PDF Zusammenführen':
 Dateien hinzufügen, Reihenfolge festlegen, Zieldatei wählen, zusammenführen.

Hinweis: Umbenennungen erfolgen direkt im Dateisystem. Bitte vorher Sicherungen anlegen.

4. TECHNISCHE INFORMATIONEN
---------------------------
 - Sprache / Framework: C# 12, .NET 8 (net8.0-windows), WPF + Windows Forms
 - Eingebundene NuGet-Pakete:
     Microsoft.Web.WebView2   1.0.3650-prerelease  PDF-Vorschau
     UglyToad.PdfPig          1.7.0-custom-5       PDF-Textextraktion
     PdfSharpCore             1.3.46               PDF-Bearbeitung/-Zusammenführung
     SixLabors.ImageSharp     3.1.3                Bildverarbeitung/Komprimierung
     NHunspell                1.2.5554.16953       Rechtschreibprüfung (OCR-Korrektur)
     System.Drawing.Common    10.0.0               Grafikfunktionen
     System.Net.Http          4.3.4                HTTP
     System.Text.RegularExpressions 4.3.1         Reguläre Ausdrücke
 - Lizenzen siehe Menü ? > Lizenzen und Drittanbieter.

5. INTEGRITÄT / SIGNATUR
------------------------
Die veröffentlichten Programmdateien sind mit einem selbst ausgestellten
Zertifikat (CN=everything-everything) signiert. Zu jedem Release gibt es eine
Datei SHA256SUMS.txt mit den SHA-256-Hashwerten aller Dateien sowie deren
Signatur SHA256SUMS.txt.p7s und das öffentliche Zertifikat (.cer).
Prüfen:  Get-FileHash <Datei> -Algorithm SHA256
";

        private static string LicenseText => @"LIZENZ DIESES PROGRAMMS
=======================
MIT License – Copyright (c) 2025 everything-everything
Siehe Datei LICENSE.

DRITTANBIETER-KOMPONENTEN
=========================
Microsoft.Web.WebView2 ........ Microsoft BSD-artige Lizenz (WebView2 SDK)
UglyToad.PdfPig ............... Apache License 2.0
PdfSharpCore .................. MIT License
SixLabors.ImageSharp .......... Six Labors Split License 1.0
                                (kostenlos für Open-Source-Projekte unter
                                OSI-Lizenz sowie bei weniger als 1 Mio. USD
                                Jahresumsatz; sonst kommerzielle Lizenz nötig)
NHunspell ..................... LGPL / GPL / MPL (Tri-Lizenz), hier unter LGPL
                                genutzt, unverändert als separate DLL
System.Drawing.Common,
System.Net.Http,
System.Text.RegularExpressions  MIT License (.NET Foundation)

NICHT ENTHALTEN (separat zu beziehen)
=====================================
Hunspell-Wörterbücher (de_DE, en_US) – GPL/LGPL/MPL bzw. eigene Lizenzen.
  Werden nicht mitgeliefert; Download per Download-Dictionaries.ps1.
Ghostscript – AGPL 3.0 bzw. kommerziell (Artifex).
  Wird nicht mitgeliefert; nur optional als externes Programm aufgerufen.
Microsoft Edge WebView2 Runtime – Bestandteil von Windows / Microsoft-Lizenz.

Vollständige Lizenztexte: Datei THIRD-PARTY-NOTICES.md im Repository.
";

        private static string AboutText => $@"Dateiumbenenner
Version {AppVersion}

Copyright (c) 2025 {GitHubUser}
Veröffentlicht unter der MIT-Lizenz (freie Software).

KONTAKT
GitHub: {GitHubUrl}
E-Mail: {ContactEmail}

Dies ist ein privates, nicht-kommerzielles Open-Source-Projekt.

HAFTUNGSAUSSCHLUSS
Die Software wird ""wie besehen"" ohne jegliche ausdrückliche oder
stillschweigende Gewährleistung bereitgestellt, einschließlich, aber nicht
beschränkt auf die Gewährleistung der Marktgängigkeit, der Eignung für einen
bestimmten Zweck und der Nichtverletzung von Rechten Dritter. Der Autor haftet
nicht für Schäden, Datenverluste oder sonstige Ansprüche, die aus der Nutzung
oder Unmöglichkeit der Nutzung der Software entstehen, soweit gesetzlich
zulässig (unberührt bleibt die Haftung bei Vorsatz und grober Fahrlässigkeit
sowie nach dem Produkthaftungsgesetz). Erkannte Dokumentdaten und vorgeschlagene
Dateinamen sind vor der Umbenennung vom Benutzer zu prüfen.
Für Inhalte verlinkter externer Seiten sind ausschließlich deren Betreiber
verantwortlich.
";
    }
}
