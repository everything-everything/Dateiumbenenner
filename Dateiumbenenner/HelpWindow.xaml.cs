using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using WpfFontFamily = System.Windows.Media.FontFamily;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using PrintDialog = System.Windows.Controls.PrintDialog;
using List = System.Windows.Documents.List;

namespace Dateiumbenenner
{
    /// <summary>Hilfefenster im Stil der Windows-XP-Hilfe (Luna-Design).</summary>
    public partial class HelpWindow : Window
    {
        public enum Page { Help, Licenses, About }

        public const string GitHubUser = "everything-everything";
        public const string GitHubUrl = "https://github.com/everything-everything";
        public const string RepoUrl = "https://github.com/everything-everything/Dateiumbenenner";
        public const string ContactEmail = "85025743+everything-everything@users.noreply.github.com";

        public static string AppVersion =>
            Assembly.GetExecutingAssembly().GetName().Version?.ToString(2) ?? "3.11";

        private static readonly Brush LinkBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x66, 0xCC));
        private static readonly Regex LinkRegex = new(@"\[\[(?<target>[^|\]]+)\|(?<text>[^\]]+)\]\]", RegexOptions.Compiled);

        private readonly List<(string Id, string Title, string Body)> _topics;
        private readonly Stack<string> _history = new();
        private string _current = "inhalt";

        public HelpWindow(Page page)
        {
            InitializeComponent();
            Title = $"Dateiumbenenner {AppVersion} – Hilfe";
            _topics = BuildTopics();
            Show(page switch { Page.Licenses => "lizenzen", Page.About => "ueber", _ => "inhalt" }, addToHistory: false);
        }

        #region Navigation

        private void Show(string id, bool addToHistory = true)
        {
            var topic = _topics.FirstOrDefault(t => t.Id == id);
            if (topic.Id == null) return;
            if (addToHistory && _current != id) _history.Push(_current);
            _current = id;
            TxtTopicTitle.Text = topic.Title;
            Viewer.Document = Render(topic.Body);
            int idx = _topics.FindIndex(t => t.Id == id);
            BtnPrev.IsEnabled = idx > 0;
            BtnNext.IsEnabled = idx < _topics.Count - 1;
            BtnBack.IsEnabled = _history.Count > 0;
        }

        private void BtnContents_Click(object s, RoutedEventArgs e) => Show("inhalt");
        private void BtnIndex_Click(object s, RoutedEventArgs e) => Show("index");
        private void BtnBack_Click(object s, RoutedEventArgs e) { if (_history.Count > 0) Show(_history.Pop(), addToHistory: false); }
        private void BtnPrev_Click(object s, RoutedEventArgs e) { int i = _topics.FindIndex(t => t.Id == _current); if (i > 0) Show(_topics[i - 1].Id); }
        private void BtnNext_Click(object s, RoutedEventArgs e) { int i = _topics.FindIndex(t => t.Id == _current); if (i < _topics.Count - 1) Show(_topics[i + 1].Id); }
        private void BtnClose_Click(object s, RoutedEventArgs e) => Close();

        private void MenuCopy_Click(object s, RoutedEventArgs e)
        {
            var doc = Viewer.Document;
            if (doc != null) System.Windows.Clipboard.SetText(TxtTopicTitle.Text + Environment.NewLine + new TextRange(doc.ContentStart, doc.ContentEnd).Text);
        }

        private void MenuAboutHelp_Click(object s, RoutedEventArgs e) => Show("ueber");

        private void BtnPrint_Click(object s, RoutedEventArgs e)
        {
            var dlg = new PrintDialog();
            if (dlg.ShowDialog() != true) return;
            var topic = _topics.First(t => t.Id == _current);
            var doc = Render(topic.Body);
            doc.Blocks.InsertBefore(doc.Blocks.FirstBlock, new Paragraph(new Bold(new Run(topic.Title))) { FontSize = 16 });
            doc.PageWidth = dlg.PrintableAreaWidth;
            doc.PageHeight = dlg.PrintableAreaHeight;
            doc.PagePadding = new Thickness(60);
            doc.ColumnWidth = double.PositiveInfinity;
            dlg.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, $"Dateiumbenenner-Hilfe – {topic.Title}");
        }

        #endregion

        #region Darstellung

        // Einfache Auszeichnung: "# " Zwischenüberschrift, "* " Aufzählung, "> " Festbreitenschrift,
        // Leerzeile = neuer Absatz, [[themenId|Text]] bzw. [[https://...|Text]] = Verweis.
        private FlowDocument Render(string body)
        {
            var doc = new FlowDocument
            {
                FontFamily = new WpfFontFamily("Tahoma"),
                FontSize = 12,
                PagePadding = new Thickness(12, 8, 12, 12),
                Background = Brushes.White
            };
            List? list = null;
            Paragraph? para = null;

            foreach (var raw in body.Replace("\r", "").Split('\n'))
            {
                var line = raw.TrimEnd();
                if (line.Length == 0) { para = null; list = null; continue; }

                if (line.StartsWith("# "))
                {
                    list = null; para = null;
                    var h = new Paragraph { FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x33, 0x99)), Margin = new Thickness(0, 10, 0, 2) };
                    AddInlines(h.Inlines, line[2..]);
                    doc.Blocks.Add(h);
                }
                else if (line.StartsWith("* "))
                {
                    para = null;
                    if (list == null) { list = new List { MarkerStyle = TextMarkerStyle.Disc, Margin = new Thickness(0, 2, 0, 4), Padding = new Thickness(20, 0, 0, 0) }; doc.Blocks.Add(list); }
                    var p = new Paragraph { Margin = new Thickness(0, 1, 0, 1) };
                    AddInlines(p.Inlines, line[2..]);
                    list.ListItems.Add(new ListItem(p));
                }
                else if (line.StartsWith("> "))
                {
                    list = null; para = null;
                    var p = new Paragraph(new Run(line[2..])) { FontFamily = new WpfFontFamily("Courier New"), FontSize = 12, Margin = new Thickness(20, 0, 0, 0) };
                    doc.Blocks.Add(p);
                }
                else
                {
                    list = null;
                    if (para == null) { para = new Paragraph { Margin = new Thickness(0, 4, 0, 4) }; doc.Blocks.Add(para); }
                    else para.Inlines.Add(new Run(" "));
                    AddInlines(para.Inlines, line);
                }
            }
            return doc;
        }

        private void AddInlines(InlineCollection inlines, string text)
        {
            int pos = 0;
            foreach (Match m in LinkRegex.Matches(text))
            {
                if (m.Index > pos) inlines.Add(new Run(text[pos..m.Index]));
                inlines.Add(CreateLink(m.Groups["target"].Value, m.Groups["text"].Value));
                pos = m.Index + m.Length;
            }
            if (pos < text.Length) inlines.Add(new Run(text[pos..]));
        }

        // Verweise wie in der XP-Hilfe: blau mit gepunkteter Unterstreichung
        private Hyperlink CreateLink(string target, string text)
        {
            var link = new Hyperlink(new Run(text)) { Foreground = LinkBrush, Cursor = System.Windows.Input.Cursors.Hand };
            var underline = new TextDecoration(TextDecorationLocation.Underline,
                new Pen(LinkBrush, 1) { DashStyle = DashStyles.Dot }, 1, TextDecorationUnit.FontRecommended, TextDecorationUnit.FontRecommended);
            link.TextDecorations = new TextDecorationCollection { underline };
            link.Click += (_, _) =>
            {
                if (target.StartsWith("http", StringComparison.OrdinalIgnoreCase) || target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                    Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
                else
                    Show(target);
            };
            return link;
        }

        #endregion

        #region Themen

        private static List<(string Id, string Title, string Body)> BuildTopics() => new()
        {
            ("inhalt", "Inhalt", $@"Willkommen bei der Hilfe zum Dateiumbenenner, Version {AppVersion}.

Klicken Sie auf ein unterstrichenes Thema, um es anzuzeigen. Mit den Schaltflächen << und >> blättern Sie durch alle Themen, mit Zurück gelangen Sie zum zuletzt angezeigten Thema.

# Einführung
* [[uebersicht|Was macht der Dateiumbenenner?]]
* [[system|Systemanforderungen]]

# Bedienung
* [[umbenennen|Dokumente erkennen und umbenennen]]
* [[praefix|Präfix-Umbenennung]]
* [[zusammenfuehren|PDF-Dateien zusammenführen]]
* [[kompression|Kompressionseinstellungen]]
* [[ocr|OCR-Korrektur und Wörterbücher]]

# Technische Informationen
* [[technik|Aufbau und verwendete NuGet-Pakete]]
* [[signatur|Digitale Signatur und Prüfsummen]]

# Rechtliches
* [[lizenzen|Lizenzen und Drittanbieter]]
* [[haftung|Haftungsausschluss]]
* [[ueber|Über Dateiumbenenner / Impressum]]

# Suche
* [[index|Index (Stichwortverzeichnis)]]"),

            ("uebersicht", "Was macht der Dateiumbenenner?", @"Der Dateiumbenenner liest PDF-Dokumente wie Rechnungen und Belege aus einem Ordner ein, erkennt darin Dokumenttyp, Rechnungs- bzw. Belegnummer und Datum und schlägt daraus einen einheitlichen Dateinamen vor.

# Funktionen im Überblick
* Automatische Erkennung von Dokumenttyp, Nummern und Datum ([[umbenennen|mehr]])
* Präfix-Umbenennung vieler Dateien in einem Schritt ([[praefix|mehr]])
* Zusammenführen mehrerer PDFs zu einer Datei ([[zusammenfuehren|mehr]])
* Verkleinern von PDF-Dateien ([[kompression|mehr]])
* PDF-Vorschau direkt im Programm
* Optionale Korrektur typischer Texterkennungsfehler ([[ocr|mehr]])

Siehe auch: [[system|Systemanforderungen]]"),

            ("system", "Systemanforderungen", @"# Betriebssystem
* Windows 10 ab Version 1809 oder Windows 11, 64 Bit (x64)

# Laufzeitumgebungen
* .NET 8 Desktop Runtime
* Microsoft Edge WebView2 Runtime (für die PDF-Vorschau; in Windows 11 bereits enthalten)

# Optional
* Ghostscript – nur für den Kompressionsmodus ""Ghostscript (extern)"", siehe [[kompression|Kompressionseinstellungen]]
* Hunspell-Wörterbücher im Ordner ""Dictionaries"" – nur für die [[ocr|OCR-Korrektur]]

# Hardware
* ca. 200 MB freier Festplattenspeicher
* 4 GB Arbeitsspeicher empfohlen"),

            ("umbenennen", "Dokumente erkennen und umbenennen", @"Registerkarte ""Umbenennen"":

* Wählen Sie im Menü Datei den Befehl ""Ordner öffnen..."" oder klicken Sie auf ""Ordner wählen"".
* Markieren Sie eine Datei in der Liste. Rechts erscheinen Vorschau und erkannte Werte.
* Prüfen Sie Dokumenttyp, Nummer und Datum und wählen Sie bei Bedarf einen anderen Vorschlag aus.
* Klicken Sie auf ""Ausgewählte umbenennen"" oder ""Alle umbenennen"".

# Weitere Optionen
* Filterfeld über der Liste: zeigt nur passende Dateien an.
* Natürliche Dateinamensortierung: sortiert 2 vor 10 und behandelt _ wie -.
* OCR Korrektur: siehe [[ocr|OCR-Korrektur und Wörterbücher]].
* MB/KB umschalten: ändert die Anzeige der Dateigröße.
* Zur Liste Zusammenfügen hinzufügen: übernimmt Dateien in die Registerkarte [[zusammenfuehren|PDF Zusammenführen]].

Wichtig: Umbenennungen erfolgen direkt im Dateisystem. Legen Sie vorher eine Sicherung an."),

            ("praefix", "Präfix-Umbenennung", @"Registerkarte ""Präfix-Umbenennung"":

* Dateien aus dem Hauptfenster übernehmen bzw. Ordner wählen.
* Neues Präfix (z. B. Firmenname) eingeben oder aus der Erkennung übernehmen.
* Zieldateien mit dem Kontrollkästchen markieren.
* Vorschau prüfen und Umbenennung ausführen.

Bei Namenskonflikten wird automatisch eine fortlaufende Nummer angehängt, z. B. ""Name (2).pdf"".

Siehe auch: [[umbenennen|Dokumente erkennen und umbenennen]]"),

            ("zusammenfuehren", "PDF-Dateien zusammenführen", @"Registerkarte ""PDF Zusammenführen"":

* Dateien hinzufügen (auch per Ziehen und Ablegen oder aus der Hauptliste).
* Reihenfolge festlegen.
* Ausgabedatei mit ""Durchsuchen..."" wählen.
* Optional: ""PDF nach dem Zusammenführen öffnen"" und ""Originaldateien nach dem Zusammenführen löschen"" (Papierkorb).
* ""Ausgewählte zusammenführen/komprimieren"" oder ""Alle zusammenführen/komprimieren"" klicken.

Die Ausgabedatei kann dabei verkleinert werden, siehe [[kompression|Kompressionseinstellungen]]."),

            ("kompression", "Kompressionseinstellungen", @"Aktivieren Sie ""Kompression aktivieren"" und wählen Sie einen Modus:

# Ghostscript (extern)
Beste Verkleinerung bei erhaltenem Text. Benötigt eine lokale Ghostscript-Installation. Fehlt Ghostscript, wird automatisch ""Nur neu schreiben"" verwendet.

# Nur neu schreiben (verlustfrei)
Schreibt die PDF neu und entfernt Überflüssiges. Keine Qualitätsverluste, meist aber nur geringe Verkleinerung.

# Rasterisieren
Jede Seite wird mit dem in Windows eingebauten PDF-Renderer (Windows.Data.Pdf) als Bild gerendert, mit der eingestellten JPEG-Qualität gespeichert und zu einer neuen PDF zusammengesetzt. Sehr wirksam bei Scans. Achtung: Der Text ist danach nicht mehr markier- oder durchsuchbar; bei reinen Text-PDFs kann die Datei größer werden.

# Regler
* Bild-DPI: Auflösung der Bilder. Bei ""Original-DPI beibehalten"" verwendet das Rasterisieren 200 dpi.
* JPEG-Qualität: 1 bis 100 Prozent; höher = bessere Qualität, größere Datei.
* Metadaten entfernen, Schriften optimieren: zusätzliche Verkleinerung.

# Empfehlungen
> Text-PDFs        150 dpi, 75 %
> Bild-/Scan-PDFs  200 dpi, 80 %
> Druckqualität    300 dpi, 85 %
> Hochauflösend    450-600 dpi, 90 %"),

            ("ocr", "OCR-Korrektur und Wörterbücher", @"Die OCR-Korrektur verbessert Texte, die durch Texterkennung (OCR) entstanden sind, z. B. verwechselte Zeichen wie 0/O oder 1/l. Sie arbeitet mit der Rechtschreibprüfung Hunspell.

# Wörterbücher installieren
Die Wörterbücher (de_DE, en_US) werden aus Lizenzgründen nicht mitgeliefert. Führen Sie Download-Dictionaries.ps1 oder Download-Dictionaries.bat aus; die Dateien werden im Ordner ""Dictionaries"" abgelegt.

# Eigene Korrekturen
Eigene Ersetzungsregeln können nach dem Muster der Datei OcrCorrections.example.txt angelegt werden.

Siehe auch: [[lizenzen|Lizenzen und Drittanbieter]]"),

            ("technik", "Aufbau und verwendete NuGet-Pakete", $@"# Plattform
* Programmiersprache: C#
* Framework: .NET 8, Zielplattform net8.0-windows10.0.19041.0
* Oberfläche: WPF, für Ordnerauswahl zusätzlich Windows Forms
* Version: {AppVersion}
* Mindest-Windows-Version: 10.0.17763 (1809), x64
* Erweiterbar über Plugins (Ordner ""Plugins"")

# NuGet-Pakete (Name, Version, Zweck)
> Microsoft.Web.WebView2          1.0.4258.31     PDF-Vorschau
> UglyToad.PdfPig                 1.7.0-custom-5  Text lesen, zusammenführen, PDF erzeugen
> WeCantSpell.Hunspell            7.0.1           Rechtschreibprüfung (OCR-Korrektur)
> System.Drawing.Common           10.0.12         Grafikfunktionen
> System.Net.Http                 4.3.4           HTTP-Zugriffe
> System.Text.RegularExpressions  4.3.1           Mustererkennung

# Ohne Zusatzpaket (in Windows bzw. .NET enthalten)
* Windows.Data.Pdf – Rendern von PDF-Seiten beim Rasterisieren
* WPF JpegBitmapEncoder – JPEG-Kompression

Lizenzen der Pakete: siehe [[lizenzen|Lizenzen und Drittanbieter]]"),

            ("signatur", "Digitale Signatur und Prüfsummen", @"Die veröffentlichten Programmdateien sind mit einem selbst ausgestellten Zertifikat (CN=everything-everything) per Authenticode signiert. Fremde Bibliotheken bleiben unverändert.

Zu jedem Release gehören:
* SHA256SUMS.txt – SHA-256-Prüfsummen aller Dateien
* SHA256SUMS.txt.p7s – digitale Signatur (CMS/PKCS#7) dieser Liste
* Dateiumbenenner-CodeSigning.cer – öffentliches Zertifikat
* .zip.sha256 – Prüfsumme des Archivs

# Prüfen in PowerShell
> Get-FileHash .\Dateiumbenenner.exe -Algorithm SHA256
> Get-AuthenticodeSignature .\Dateiumbenenner.exe

Da das Zertifikat selbst ausgestellt ist, meldet Windows es als nicht vertrauenswürdig, solange es nicht manuell importiert wurde. Die Prüfsummen belegen trotzdem, dass die Dateien unverändert sind."),

            ("lizenzen", "Lizenzen und Drittanbieter", $@"# Dieses Programm
Dateiumbenenner steht unter der MIT-Lizenz (freie Software). Copyright (c) 2025 {GitHubUser}. Den vollständigen Text enthält die Datei LICENSE.

# Mitgelieferte Komponenten
> Microsoft.Web.WebView2   Microsoft BSD-artige Lizenz
> UglyToad.PdfPig          Apache License 2.0
> WeCantSpell.Hunspell     MPL 1.1/GPL 2/LGPL 2.1, genutzt unter LGPL
> System.Drawing.Common    MIT (.NET Foundation)
> System.Net.Http          MIT (.NET Foundation)
> System.Text.RegularExpr. MIT (.NET Foundation)

WeCantSpell.Hunspell wird unverändert als eigenständige DLL eingebunden (LGPL-konform).

# Nicht enthalten (separat zu beziehen)
* Hunspell-Wörterbücher – GPL/LGPL/MPL; Download per Skript, siehe [[ocr|OCR-Korrektur]]
* Ghostscript – AGPL 3.0 oder kommerziell (Artifex); wird nur als externes Programm aufgerufen
* Microsoft Edge WebView2 Runtime – Bestandteil von Windows

Vollständige Angaben: Datei THIRD-PARTY-NOTICES.md im [[{RepoUrl}|Projekt auf GitHub]]."),

            ("haftung", "Haftungsausschluss", @"Die Software wird ""wie besehen"" ohne jegliche ausdrückliche oder stillschweigende Gewährleistung bereitgestellt, einschließlich, aber nicht beschränkt auf die Gewährleistung der Marktgängigkeit, der Eignung für einen bestimmten Zweck und der Nichtverletzung von Rechten Dritter.

Der Autor haftet nicht für Schäden, Datenverluste oder sonstige Ansprüche, die aus der Nutzung oder der Unmöglichkeit der Nutzung der Software entstehen, soweit dies gesetzlich zulässig ist. Unberührt bleibt die Haftung bei Vorsatz und grober Fahrlässigkeit sowie nach dem Produkthaftungsgesetz.

Erkannte Dokumentdaten und vorgeschlagene Dateinamen sind vor der Umbenennung vom Benutzer zu prüfen. Legen Sie vor dem Umbenennen, Zusammenführen oder Komprimieren Sicherungskopien an.

Für Inhalte verlinkter externer Seiten sind ausschließlich deren Betreiber verantwortlich."),

            ("ueber", "Über Dateiumbenenner / Impressum", $@"Dateiumbenenner
Version {AppVersion}

Copyright (c) 2025 {GitHubUser}
Veröffentlicht unter der MIT-Lizenz.

# Kontakt
* GitHub: [[{GitHubUrl}|{GitHubUrl}]]
* Projekt: [[{RepoUrl}|{RepoUrl}]]
* E-Mail: [[mailto:{ContactEmail}|{ContactEmail}]]

Dies ist ein privates, nicht kommerzielles Open-Source-Projekt.

Siehe auch: [[haftung|Haftungsausschluss]], [[lizenzen|Lizenzen und Drittanbieter]]"),

            ("index", "Index", @"> A
* [[technik|Aufbau des Programms]]
> D
* [[ueber|Datenschutz / Kontakt]]
* [[ocr|Dictionaries (Wörterbücher)]]
* [[kompression|DPI]]
> G
* [[kompression|Ghostscript]]
* [[haftung|Gewährleistung]]
> H
* [[haftung|Haftungsausschluss]]
* [[signatur|Hash (SHA-256)]]
* [[ocr|Hunspell]]
> I
* [[ueber|Impressum]]
> J
* [[kompression|JPEG-Qualität]]
> K
* [[kompression|Kompression]]
* [[ueber|Kontakt]]
> L
* [[lizenzen|Lizenzen]]
> M
* [[lizenzen|MIT-Lizenz]]
> N
* [[technik|NuGet-Pakete]]
> O
* [[ocr|OCR-Korrektur]]
* [[umbenennen|Ordner öffnen]]
> P
* [[praefix|Präfix]]
* [[signatur|Prüfsummen]]
> R
* [[kompression|Rasterisieren]]
* [[umbenennen|Rechnungsnummer]]
> S
* [[signatur|Signatur]]
* [[system|Systemanforderungen]]
> U
* [[umbenennen|Umbenennen]]
> V
* [[technik|Version]]
> W
* [[system|WebView2]]
> Z
* [[signatur|Zertifikat]]
* [[zusammenfuehren|Zusammenführen]]")
        };

        #endregion
    }
}
