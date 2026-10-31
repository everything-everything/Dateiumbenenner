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
            Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "5.1.2600";

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
            AddPluginTopics();
            LstContents.ItemsSource = _topics.Where(t => t.Id != "index").Select(t => new TopicRef(t.Id, t.Title)).ToList();
            _index = BuildIndex();
            LstIndex.ItemsSource = _index;
            Show(page switch { Page.Licenses => "lizenzen", Page.About => "ueber", _ => "inhalt" }, addToHistory: false);
        }

        private sealed record TopicRef(string Id, string Title);
        private sealed record IndexEntry(string Keyword, string Id);
        private List<IndexEntry> _index = new();
        private bool _syncing;

        /// <summary>Stichwörter aus dem Index-Thema, allen Thementiteln und Zwischenüberschriften.</summary>
        private List<IndexEntry> BuildIndex()
        {
            var list = new List<IndexEntry>();
            var idx = _topics.FirstOrDefault(t => t.Id == "index");
            if (idx.Id != null)
                foreach (Match m in LinkRegex.Matches(idx.Body))
                    if (Resolve(m.Groups["target"].Value) is string target) list.Add(new(m.Groups["text"].Value, target));
            foreach (var t in _topics.Where(t => t.Id is not "index" and not "inhalt"))
            {
                list.Add(new(t.Title, t.Id));
                foreach (var line in t.Body.Replace("\r", "").Split('\n'))
                {
                    if (line.StartsWith("# "))
                        list.Add(new($"{LinkRegex.Replace(line[2..].Trim(), "${text}")} ({t.Title})", t.Id));
                    // Plugins liefern Indexbegriffe als Zeile „Stichwörter: a, b, c“
                    else if (line.StartsWith(KeywordLine))
                        foreach (var k in line[KeywordLine.Length..].Split(',').Select(k => k.Trim()).Where(k => k.Length > 0))
                            list.Add(new(k, t.Id));
                }
            }
            return list.GroupBy(e => e.Keyword, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
                       .OrderBy(e => e.Keyword, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        private void TxtIndexFilter_TextChanged(object s, TextChangedEventArgs e)
        {
            var f = TxtIndexFilter.Text.Trim();
            var items = f.Length == 0 ? _index : _index.Where(i => i.Keyword.Contains(f, StringComparison.CurrentCultureIgnoreCase))
                .OrderBy(i => i.Keyword.StartsWith(f, StringComparison.CurrentCultureIgnoreCase) ? 0 : 1).ToList();
            LstIndex.ItemsSource = items;
            if (items.Count > 0) LstIndex.SelectedIndex = 0;
        }

        private void TxtIndexFilter_PreviewKeyDown(object s, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter) { BtnIndexShow_Click(s, e); e.Handled = true; }
            else if (e.Key == System.Windows.Input.Key.Down && LstIndex.Items.Count > 0) { LstIndex.SelectedIndex = Math.Min(LstIndex.SelectedIndex + 1, LstIndex.Items.Count - 1); LstIndex.ScrollIntoView(LstIndex.SelectedItem); e.Handled = true; }
            else if (e.Key == System.Windows.Input.Key.Up && LstIndex.SelectedIndex > 0) { LstIndex.SelectedIndex--; LstIndex.ScrollIntoView(LstIndex.SelectedItem); e.Handled = true; }
        }

        private void BtnIndexShow_Click(object s, RoutedEventArgs e)
        {
            if ((LstIndex.SelectedItem ?? (LstIndex.Items.Count > 0 ? LstIndex.Items[0] : null)) is IndexEntry ie) Show(ie.Id);
        }

        private void LstContents_SelectionChanged(object s, SelectionChangedEventArgs e)
        {
            if (!_syncing && LstContents.SelectedItem is TopicRef t) Show(t.Id);
        }

        private void TxtSearch_PreviewKeyDown(object s, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter) { BtnSearch_Click(s, e); e.Handled = true; }
        }

        private void BtnSearch_Click(object s, RoutedEventArgs e)
        {
            var words = TxtSearch.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) { LstSearch.ItemsSource = null; TxtSearchInfo.Text = string.Empty; return; }
            var hits = _topics.Where(t => t.Id != "index")
                .Select(t => (t, text: t.Title + " " + LinkRegex.Replace(t.Body, "${text}")))
                .Where(x => words.All(w => x.text.Contains(w, StringComparison.CurrentCultureIgnoreCase)))
                .OrderByDescending(x => words.Sum(w => x.t.Title.Contains(w, StringComparison.CurrentCultureIgnoreCase) ? 10 : 0)
                                     + words.Sum(w => Regex.Matches(x.text, Regex.Escape(w), RegexOptions.IgnoreCase).Count))
                .Select(x => new TopicRef(x.t.Id, x.t.Title)).ToList();
            LstSearch.ItemsSource = hits;
            TxtSearchInfo.Text = $"{hits.Count} Thema/Themen gefunden";
        }

        private void LstSearch_SelectionChanged(object s, SelectionChangedEventArgs e)
        {
            if (LstSearch.SelectedItem is TopicRef t) Show(t.Id);
        }

        #region Navigation

        private const string KeywordLine = "Stichwörter:";

        private static bool IsExternal(string target) =>
            target.StartsWith("http", StringComparison.OrdinalIgnoreCase) || target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase);

        /// <summary>Themen-ID auflösen; Kurz-IDs wie „inixml“ finden auch Plugin-Themen. null = Thema nicht vorhanden (Plugin nicht geladen).</summary>
        private string? Resolve(string id) =>
            _topics.Any(t => t.Id == id) ? id : _topics.FirstOrDefault(t => t.Id.StartsWith("plugin:") && t.Id.EndsWith(":" + id)).Id;

        /// <summary>Zeile verweist nur auf nicht vorhandene Themen (Plugin nicht installiert) – wird ausgeblendet.</summary>
        private bool IsDeadLinkLine(string line)
        {
            var targets = LinkRegex.Matches(line).Select(m => m.Groups["target"].Value).Where(t => !IsExternal(t)).ToList();
            return targets.Count > 0 && targets.All(t => Resolve(t) == null);
        }

        private void Show(string id, bool addToHistory = true)
        {
            id = Resolve(id) ?? id;
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
            _syncing = true;
            LstContents.SelectedItem = (LstContents.ItemsSource as IEnumerable<TopicRef>)?.FirstOrDefault(t => t.Id == id);
            if (LstContents.SelectedItem != null) LstContents.ScrollIntoView(LstContents.SelectedItem);
            _syncing = false;
        }

        private void BtnContents_Click(object s, RoutedEventArgs e) => Show("inhalt");
        private void BtnIndex_Click(object s, RoutedEventArgs e) { NavTabs.SelectedItem = TabIndexPage; TxtIndexFilter.Focus(); }
        private void BtnBack_Click(object s, RoutedEventArgs e) { if (_history.Count > 0) Show(_history.Pop(), addToHistory: false); }
        private void BtnPrev_Click(object s, RoutedEventArgs e) { int i = _topics.FindIndex(t => t.Id == _current); if (i > 0) Show(_topics[i - 1].Id); }
        private void BtnNext_Click(object s, RoutedEventArgs e) { int i = _topics.FindIndex(t => t.Id == _current); if (i < _topics.Count - 1) Show(_topics[i + 1].Id); }
        private void BtnClose_Click(object s, RoutedEventArgs e) => Close();

        private void MenuCopy_Click(object s, RoutedEventArgs e)
        {
            var doc = Viewer.Document;
            if (doc != null) System.Windows.Clipboard.SetText(TxtTopicTitle.Text + Environment.NewLine + new TextRange(doc.ContentStart, doc.ContentEnd).Text);
        }

        private void MenuAboutHelp_Click(object s, RoutedEventArgs e)
        {
            var tabs = new System.Windows.Controls.TabControl { Margin = new Thickness(6) };

            FlowDocumentScrollViewer Doc(string id)
            {
                var t = _topics.FirstOrDefault(x => x.Id == id);
                return new FlowDocumentScrollViewer { Document = Render(t.Body ?? string.Empty), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, IsToolBarVisible = false };
            }

            System.Windows.Controls.ListView Grid2(string h1, string h2, IEnumerable<KeyValuePair<string, string>> rows, double w1 = 260, double w2 = 420)
            {
                var gv = new GridView();
                gv.Columns.Add(new GridViewColumn { Header = h1, Width = w1, DisplayMemberBinding = new System.Windows.Data.Binding("Key") });
                gv.Columns.Add(new GridViewColumn { Header = h2, Width = w2, DisplayMemberBinding = new System.Windows.Data.Binding("Value") });
                var lv = new System.Windows.Controls.ListView { View = gv, ItemsSource = rows.ToList() };
                ListViewCopyHelper.Attach(lv);
                return lv;
            }

            var sys = SystemInfo();
            var sysPanel = new DockPanel();
            var copy = new System.Windows.Controls.Button { Content = "In Zwischenablage kopieren", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
            copy.Click += (_, _) => { try { System.Windows.Clipboard.SetText(string.Join(Environment.NewLine, sys.Select(k => $"{k.Key}: {k.Value}"))); } catch { } };
            DockPanel.SetDock(copy, Dock.Bottom);
            sysPanel.Children.Add(copy);
            sysPanel.Children.Add(Grid2("Eigenschaft", "Wert", sys, 200, 480));

            var pkgs = NuGetPackages();
            tabs.Items.Add(new TabItem { Header = "Info", Content = Doc("ueber") });
            tabs.Items.Add(new TabItem { Header = "Lizenzen", Content = Doc("lizenzen") });
            tabs.Items.Add(new TabItem { Header = $"NuGet-Pakete ({pkgs.Count})", Content = Grid2("Paket", "Version", pkgs, 380, 200) });
            tabs.Items.Add(new TabItem { Header = "Plugins", Content = Grid2("Plugin", "Version / Status", LoadedPlugins.Select(p => new KeyValuePair<string, string>(p.Plugin.Name, $"{p.Plugin.Version}{(p.Enabled ? "" : " (deaktiviert)")} – {p.Plugin.Description}"))) });
            tabs.Items.Add(new TabItem { Header = "Systeminformationen", Content = sysPanel });
            var (cpuPanel, cpuTimer) = CpuPanel();
            tabs.Items.Add(new TabItem { Header = "CPU-Auslastung", Content = cpuPanel });

            var close = new System.Windows.Controls.Button { Content = "OK", IsDefault = true, IsCancel = true, MinWidth = 75, Margin = new Thickness(6), HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
            var root = new DockPanel();
            DockPanel.SetDock(close, Dock.Bottom);
            root.Children.Add(close);
            root.Children.Add(tabs);
            var w = new Window { Title = $"Info – Dateiumbenenner {AppVersion}", Owner = this, Width = 760, Height = 540, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = root, Background = Background, FontFamily = FontFamily, FontSize = 12 };
            close.Click += (_, _) => w.Close();
            w.Closed += (_, _) => cpuTimer.Stop();
            cpuTimer.Start();
            w.ShowDialog();
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

        /// <summary>Live-Diagramm der CPU-Auslastung (System gesamt und Dateiumbenenner), 1 Sekunde Takt, 60 Sekunden Verlauf.</summary>
        private static (UIElement Panel, System.Windows.Threading.DispatcherTimer Timer) CpuPanel()
        {
            const int points = 60;
            var sysVals = new List<double>(); var procVals = new List<double>();
            var info = new TextBlock { Margin = new Thickness(0, 0, 0, 6), FontSize = 13 };
            var canvas = new Canvas { Background = Brushes.Black, ClipToBounds = true, MinHeight = 200 };
            var sysLine = new System.Windows.Shapes.Polyline { Stroke = Brushes.LimeGreen, StrokeThickness = 2 };
            var procLine = new System.Windows.Shapes.Polyline { Stroke = Brushes.Orange, StrokeThickness = 2 };
            var legend = new TextBlock { Margin = new Thickness(0, 6, 0, 0) };
            legend.Inlines.Add(new Run("■ System gesamt   ") { Foreground = Brushes.Green });
            legend.Inlines.Add(new Run("■ Dateiumbenenner   ") { Foreground = Brushes.DarkOrange });
            legend.Inlines.Add(new Run("(letzte 60 Sekunden, Raster 25 %)") { Foreground = Brushes.Gray });

            void Redraw()
            {
                canvas.Children.Clear();
                double w = canvas.ActualWidth, h = canvas.ActualHeight;
                if (w <= 0 || h <= 0) return;
                for (int p = 1; p < 4; p++)
                    canvas.Children.Add(new System.Windows.Shapes.Line { X1 = 0, X2 = w, Y1 = h * p / 4, Y2 = h * p / 4, Stroke = new SolidColorBrush(Color.FromRgb(0, 70, 0)), StrokeThickness = 1 });
                PointCollection Pts(List<double> v)
                {
                    var pc = new PointCollection();
                    for (int i = 0; i < v.Count; i++) pc.Add(new System.Windows.Point(w - (v.Count - 1 - i) * w / (points - 1), h - v[i] / 100 * h));
                    return pc;
                }
                sysLine.Points = Pts(sysVals); procLine.Points = Pts(procVals);
                canvas.Children.Add(sysLine); canvas.Children.Add(procLine);
            }
            canvas.SizeChanged += (_, _) => Redraw();

            GetSystemTimes(out var lastIdle, out var lastKernel, out var lastUser);
            var proc = Process.GetCurrentProcess();
            var lastProc = proc.TotalProcessorTime; var lastWall = DateTime.UtcNow;
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (_, _) =>
            {
                double sys = 0, own = 0;
                if (GetSystemTimes(out var idle, out var kernel, out var user))
                {
                    long total = (kernel - lastKernel) + (user - lastUser);
                    if (total > 0) sys = Math.Clamp((total - (idle - lastIdle)) * 100.0 / total, 0, 100);
                    lastIdle = idle; lastKernel = kernel; lastUser = user;
                }
                proc.Refresh();
                var now = DateTime.UtcNow; var cpu = proc.TotalProcessorTime;
                var wall = (now - lastWall).TotalMilliseconds * Environment.ProcessorCount;
                if (wall > 0) own = Math.Clamp((cpu - lastProc).TotalMilliseconds * 100 / wall, 0, 100);
                lastProc = cpu; lastWall = now;
                sysVals.Add(sys); procVals.Add(own);
                if (sysVals.Count > points) { sysVals.RemoveAt(0); procVals.RemoveAt(0); }
                info.Text = $"System: {sys:0} %   Dateiumbenenner: {own:0.0} %   Durchschnitt System: {sysVals.Average():0} %   Prozessoren: {Environment.ProcessorCount}   Arbeitsspeicher Programm: {Environment.WorkingSet / 1024 / 1024:N0} MB";
                Redraw();
            };
            info.Text = "Messung läuft …";

            var panel = new DockPanel { Margin = new Thickness(8) };
            DockPanel.SetDock(info, Dock.Top); DockPanel.SetDock(legend, Dock.Bottom);
            panel.Children.Add(info); panel.Children.Add(legend);
            panel.Children.Add(new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Child = canvas });
            return (panel, timer);
        }

        private static List<KeyValuePair<string, string>> NuGetPackages()
        {
            var result = new List<KeyValuePair<string, string>>();
            try
            {
                var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
                var deps = System.IO.Path.Combine(AppContext.BaseDirectory, asm.GetName().Name + ".deps.json");
                if (!System.IO.File.Exists(deps)) return result;
                using var json = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(deps));
                if (!json.RootElement.TryGetProperty("libraries", out var libs)) return result;
                foreach (var lib in libs.EnumerateObject())
                {
                    if (!lib.Value.TryGetProperty("type", out var type) || type.GetString() != "package") continue;
                    var parts = lib.Name.Split('/');
                    result.Add(new(parts[0], parts.Length > 1 ? parts[1] : string.Empty));
                }
            }
            catch { }
            return result.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static List<KeyValuePair<string, string>> SystemInfo()
        {
            static KeyValuePair<string, string> P(string k, string? v) => new(k, v ?? string.Empty);
            var gc = GC.GetGCMemoryInfo();
            var list = new List<KeyValuePair<string, string>>
            {
                P("Programm", $"Dateiumbenenner {Assembly.GetExecutingAssembly().GetName().Version}"),
                P("Programmpfad", Environment.ProcessPath),
                P("Profilordner", ProfileManager.ActiveDirectory),
                P("Betriebssystem", System.Runtime.InteropServices.RuntimeInformation.OSDescription),
                P("OS-Version", Environment.OSVersion.VersionString),
                P("Architektur (OS / Prozess)", $"{System.Runtime.InteropServices.RuntimeInformation.OSArchitecture} / {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}"),
                P(".NET-Laufzeit", System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription),
                P("Computername", Environment.MachineName),
                P("Benutzer", $"{Environment.UserDomainName}\\{Environment.UserName}"),
                P("Prozessoren (logisch)", Environment.ProcessorCount.ToString()),
                P("Arbeitsspeicher verfügbar", $"{gc.TotalAvailableMemoryBytes / 1024 / 1024:N0} MB"),
                P("Arbeitsspeicher Programm", $"{Environment.WorkingSet / 1024 / 1024:N0} MB"),
                P("Bildschirm", $"{SystemParameters.PrimaryScreenWidth:0} × {SystemParameters.PrimaryScreenHeight:0}"),
                P("Sprache / Kultur", $"{System.Globalization.CultureInfo.CurrentUICulture.Name} / {System.Globalization.CultureInfo.CurrentCulture.Name}"),
                P("Zeitzone", TimeZoneInfo.Local.DisplayName),
                P("Systemlaufzeit", TimeSpan.FromMilliseconds(Environment.TickCount64).ToString(@"d\.hh\:mm\:ss")),
                P("Geladene Plugins", LoadedPlugins.Count.ToString())
            };
            return list;
        }

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
                if (IsDeadLinkLine(line)) continue;

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
                var target = m.Groups["target"].Value;
                if (IsExternal(target) || Resolve(target) != null) inlines.Add(CreateLink(target, m.Groups["text"].Value));
                else inlines.Add(new Run(m.Groups["text"].Value));
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
                if (IsExternal(target))
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
* [[txtnummern|TXT-Nummern korrigieren]]
* [[plz|PLZ-Verzeichnis]]
* [[firmenverzeichnis|Firmenverzeichnis (eigene DB, GLEIF)]]
* [[gegenpruefung|Hintergrund-Gegenprüfung Datum/Betrag]]
* [[nummernregeln|Nummernmuster und Belegarten]]
* [[profile|Profile]]
* [[listen|Listen: Kopieren, Markieren, Filtern]]
* [[firmauebertragen|Firma übertragen]]

# Technische Informationen
* [[technik|Aufbau und verwendete NuGet-Pakete]]
* [[signatur|Digitale Signatur und Prüfsummen]]
* [[plugins|Plugins (Erweiterungen)]]

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
* Windows-Sprachpakete Deutsch/Englisch – werden von der [[ocr|OCR-Korrektur]] zur Bestätigung richtiger Wörter genutzt, falls vorhanden (kein Download)
* PLZ-Liste: mitgeliefert, kein Download nötig, siehe [[plz|PLZ-Verzeichnis]]

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

# Markierte verschieben / kopieren
* Markieren Sie die Dateien in der Liste und klicken Sie auf ""Markierte verschieben / kopieren"".
* Wählen Sie Verschieben oder Kopieren und den Zielordner. Gleichnamige .txt/.ini/.xml werden immer mitgenommen, vorhandene Dateien nie überschrieben (Zusatz _2, _3 …).
* Optional ""Zugehörige XML-Belegdefinitionen mitnehmen"": Aus dem XML-Quellordner (Standard: Unterordner XML des Belegordners) werden die BE_….xml gesucht, deren Erkennungsmerkmale alle im Text eines markierten Belegs stehen (ohne Merkmale: gleiche Firma). Die Liste zeigt Firma, Kategorie und die Anzahl passender Belege (Tooltip: welche); Haken mit Maus oder Leertaste setzen.
* Die XML-Dateien landen im XML-Zielordner (Standard: Unterordner XML des Zielordners) und können kopiert oder verschoben werden. Passt eine Definition noch zu weiteren, nicht markierten Belegen der Liste, wird sie nur kopiert. Existiert sie im Ziel schon mit anderem Inhalt, wird sie nicht überschrieben und gemeldet.

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

            ("ocr", "OCR-Korrektur", @"Die OCR-Korrektur verbessert Texte aus der Texterkennung (OCR). Sie wird über ""OCR Korrektur"" bzw. das Menü ein- und ausgeschaltet und braucht keinen Download.

# Was korrigiert wird
* Getrennte Wörter wie ""Rechnu ng"" werden zusammengefügt, wenn das ganze Wort bekannt ist.
* Einzelne Wörter mit typischen OCR-Verwechslungen, z. B. ""Liefersche1n"" → ""Lieferschein"", ""Rechnunq"" → ""Rechnung"", ""GrnbH"" → ""GmbH"", ""Empfanger"" → ""Empfänger"" (rn/m, cl/d, 1/l/i, 0/o, 5/s, 8/b, q/g, a/ä, o/ö, u/ü …).
* Abstände nach Doppelpunkten und Feldbezeichnungen, mehrfache Leerzeichen und Leerzeilen.
* Eigene Regeln aus OcrCorrections.txt.

# Woher die Wörter kommen
* Mitgelieferte Fachwortliste (Belegarten, Feldbezeichnungen, Zahlungs-, Steuer- und Logistikbegriffe, Monate, Rechtsformen) – eigene Zusammenstellung unter MIT-Lizenz.
* Eigene Wörter aus OcrCorrections.txt (@dictionary=…) und bestätigte Firmennamen.
* Gelernte Wörter aus den eigenen Belegen: Das Programm zählt, in wie vielen Belegen ein Wort vorkommt. Die Liste bleibt lokal (Datei ocr_woerter.json im Profilordner, bei Passwortschutz verschlüsselt) und wird mit den gelernten Daten gelöscht. Häufige Wörter – etwa Namen – werden dadurch nicht verändert.
* Windows-Rechtschreibprüfung (installierte Sprachpakete Deutsch/Englisch): Kennt Windows ein Wort, bleibt es unverändert. Fehlt sie, arbeitet die Korrektur ohne sie weiter.

# Vorsichtig statt aggressiv
Ein Wort wird nur ersetzt, wenn der Unterschied durch OCR-Verwechslungen erklärbar ist (bei kurzen Wörtern höchstens eine, sonst höchstens zwei), genau ein passendes Wort in Frage kommt und das Original weder bekannt noch häufig ist. Zahlen, Beträge, IBAN sowie E-Mail- und Web-Adressen werden nicht verändert.

# Eigene Korrekturen
Eigene Ersetzungsregeln und Wörter können nach dem Muster der Datei OcrCorrections.example.txt in OcrCorrections.txt (Programmordner) eingetragen werden. Mit @wortkorrektur=aus wird nur die Wortkorrektur abgeschaltet.

Die Postleitzahlen-Liste ist ebenfalls mitgeliefert, siehe [[plz|PLZ-Verzeichnis]].

Siehe auch: [[lizenzen|Lizenzen und Drittanbieter]]"),

            ("txtnummern", "TXT-Nummern korrigieren", @"Korrigiert wiederkehrende OCR-Fehler in Nummern direkt in den TXT-Dateien – z. B. wenn die Texterkennung in 14-stelligen Belegnummern immer an der gleichen Stelle D als 0 liest (4700012345F831 statt 4700D12345F831).

# Aufruf
* Menü Extras → ""TXT-Nummern korrigieren..."" oder Schaltfläche ""TXT-Nummern korrigieren"" über der Textvorschau.
* Im Plugin ""INI Erzeugen"": Schaltfläche ""TXT-Nummern korrigieren…"" in der Werkzeugleiste.
* In der Textvorschau markierter Text bzw. die Belegnummer wird als Beispiel übernommen.

# Regeln
* Am einfachsten: Falsch erkannte und richtige Nummer eingeben und ""Regel ableiten"" – aus 4700012345F831 → 4700D12345F831 wird Länge 14, Muster ""47000"" am Anfang → ""4700D"".
* Länge: Anzahl Zeichen der ganzen Nummer (Buchstaben und Ziffern ohne Trennzeichen); leer = beliebig. Nummern anderer Länge bleiben unberührt.
* Position: Anfang, Ende oder Beliebig (jede Stelle in der Nummer).
* Platzhalter im falschen Muster: $ = Ziffer, ? = Buchstabe, * = beliebiges Zeichen. In der Ersetzung behält $, ? bzw. * das vorhandene Zeichen – z. B. ""$$$$0"" → ""$$$$D"" korrigiert jede 14-stellige Nummer mit 0 an fünfter Stelle.
* Groß-/Kleinschreibung spielt beim Vergleich keine Rolle. Die Regeln werden im Profil gespeichert (txt_number_fixes.json).

# Prüfen und schreiben
* Bereich: alle Belege der Liste mit TXT-Datei oder nur die markierten.
* Die Trefferliste zeigt Beleg, Zeile, gefundene und neue Nummer, Regel und Textzeile. Häkchen bzw. Leertaste schließt einzelne Treffer aus.
* ""In TXT-Dateien schreiben"" ändert die Dateien (Zeichenkodierung bleibt erhalten). Die Originale werden vorher im Profilordner unter TxtBackup gesichert; ""Rückgängig"" stellt die zuletzt geänderten Dateien wieder her.
* Die betroffenen Belege werden danach automatisch neu eingelesen und ausgewertet – im Hauptfenster und im Plugin. Von Hand gesetzte Werte bleiben erhalten.

# Hinweis
Korrigiert werden nur Textdateien: im A-Modus die gleichnamige .txt bzw. .txt-Dateien in der Liste. Text, der direkt aus einer PDF gelesen wird, kann so nicht geändert werden.

Siehe auch: [[ocr|OCR-Korrektur]]"),

            ("plz", "PLZ-Verzeichnis", @"Das PLZ-Verzeichnis dient zur Prüfung von Postleitzahl und Ort in der Adresserkennung.

# Mitgelieferte Liste
Die Postleitzahlen für Deutschland von GeoNames (CC BY 4.0) sind unverändert in der signierten Programmdatei enthalten. Ein Download ist nicht nötig, und die Liste kann nicht unbemerkt ausgetauscht werden.

# Welche Liste ist aktiv?
Reihenfolge:
* Daten\DE.txt – eine heruntergeladene Aktualisierung, sofern sie die Prüfung besteht. Sie ersetzt die mitgelieferte Liste.
* sonst die mitgelieferte Liste.
* Eigene Ergänzungen aus Daten\plz.csv (je Zeile ""PLZ;Ort"") kommen immer hinzu.

Die aktive Quelle zeigen der Menüpunkt ""PLZ-Verzeichnis (DE) prüfen und aktualisieren..."" und die Diagnose. Eine DE.txt aus einer Version vor 5.1.2619 erscheint als ""früher heruntergeladen, Herkunft nicht vermerkt"".

# Aktualisieren (optional)
* Geladen wird nur von https://download.geonames.org/export/zip/DE.zip (nur HTTPS, keine Umleitung, Größenlimit).
* Die Daten werden geprüft (Steuerzeichen, überlange Einträge, ungewöhnliche Ortsnamen, Anzahl der PLZ).
* Eine Vorschau zeigt Quelle, Datum, Größe, SHA-256 sowie neue, entfallene und geänderte PLZ. Übernommen wird erst nach Bestätigung; bei auffällig vielen Änderungen erscheint eine Warnung.

# Zurück zur mitgelieferten Liste
Menüpunkt ""PLZ-Verzeichnis: mitgelieferte Liste verwenden..."" – entfernt Daten\DE.txt; plz.csv bleibt erhalten.

# Hinweis
In den GeoNames-Daten stehen bei Großkunden-PLZ teils Firmennamen statt Ortsnamen. Das ist im Original so und wird bei der Prüfung toleriert.

Siehe auch: [[lizenzen|Lizenzen und Drittanbieter]]"),

            ("firmenverzeichnis", "Firmenverzeichnis (eigene DB, GLEIF)", @"Das Firmenverzeichnis stützt die Firmenerkennung: Bekannte Firmen bekommen Pluspunkte, besonders wenn ihre PLZ im Beleg steht. Es ersetzt die Erkennung nicht – Firmen, die in keiner Liste stehen (z. B. kleine Handwerker oder Händler vor Ort), werden nie abgewertet.

# Aufruf
Menü Extras → ""Firmenverzeichnis (eigene DB, GLEIF)..."". Der Stand wird rechts in der Statusleiste angezeigt.

# Quellen
* Eigene Datenbank: Firmen mit PLZ und Ort aus der Adress-Datenbank eines Plugins (z. B. INI Erzeugen). Wird nach dem Start im Hintergrund eingelesen und als Zwischenstand im Profil gespeichert; ist die Datenbank nicht erreichbar, bleibt der letzte Stand aktiv.
* Gelernt: Firmen, die Sie bestätigt bzw. übertragen haben, sofern ihre PLZ im Beleg vorkommt.
* GLEIF (optional): das freie Verzeichnis der Global Legal Entity Identifier Foundation (CC0) – rund 240.000 aktive deutsche Unternehmen mit eingetragenem Namen, PLZ und Ort. Überwiegend größere Firmen und Gesellschaften; Einzelunternehmer fehlen meist.

# Pluspunkte
* Eigene Datenbank oder gelernt: +1, mit passender PLZ im Text +3.
* GLEIF: +0,5, mit passender PLZ im Text +2.
* Verglichen wird ohne Rechtsform, Groß-/Kleinschreibung und Satzzeichen (""ZAPF GmbH"" = ""Zapf GmbH & Co. KG"", ""Siemens AG"" = ""Siemens Aktiengesellschaft"").
* Die Punkte wirken bei neu eingelesenen bzw. neu ausgewerteten Belegen.

# GLEIF herunterladen
* Schaltfläche ""GLEIF herunterladen / aktualisieren"" – lädt die aktuelle Gesamtdatei (ca. 480 MB) im Hintergrund; Fortschritt in Fenster und Statusleiste, Abbrechen jederzeit möglich.
* Geladen wird nur per HTTPS von goldencopy.gleif.org, ohne Umleitung; Größe und SHA-256 werden geprüft, die Daten auf Plausibilität kontrolliert.
* Gespeichert werden nur aktive deutsche Firmen (Name, PLZ, Ort) als kompakte Datei Daten\gleif_de.dat (ca. 5 MB) für alle Profile; die heruntergeladene Datei wird danach gelöscht.
* ""GLEIF entfernen"" löscht die Datei wieder.

# Testfeld
Firmenname und optional PLZ eingeben: angezeigt werden Vergleichsschlüssel, Pluspunkte und gefundene Einträge mit Quelle.

Siehe auch: [[firmauebertragen|Firma übertragen]], [[lizenzen|Lizenzen und Drittanbieter]]"),

            ("gegenpruefung", "Hintergrund-Gegenprüfung Datum/Betrag", @"Testfunktion: Datum und Betrag jedes Belegs werden im Hintergrund mit einer zweiten, unabhängigen Erkennung (Microsoft Recognizers-Text) gegengeprüft.

# Aufruf
Menü Extras → ""Hintergrund-Gegenprüfung Datum/Betrag (Test)"" ein- bzw. ausschalten. Die Einstellung bleibt erhalten.

# Anzeige
* Spalte ""Prüf."" in der Dokumentliste: ✓ bestätigt, ⚠ Abweichung, – nichts Vergleichbares gefunden. Der Tooltip zeigt die Einzelheiten.
* Fortschritt und Ergebnis rechts in der Statusleiste (""Gegenprüfung: x / n"", ""Gegenprüfung fertig …"").
* Findet die Gegenprüfung ein anderes Datum, wird es zusätzlich als Vorschlag in die Datumsauswahl aufgenommen.

# Wichtig
* Es wird nie etwas automatisch geändert oder überschrieben.
* Die Prüfung läuft mit niedriger Priorität; Ergebnisse werden zwischengespeichert (crosscheck_cache.json) und nur bei geändertem Text oder Datum neu berechnet.
* Grenzen: Schreibweisen wie 23Okt25, 25Oct23 oder 2025-MAY-10 kennt die Gegenprüfung nicht (– statt ⚠); erkannt werden nur Beträge in Euro.

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
> SymSpell                        6.7.3           Wortsuche für die OCR-Korrektur
> System.Drawing.Common           10.0.12         Grafikfunktionen
> System.Net.Http                 4.3.4           HTTP-Zugriffe
> System.Text.RegularExpressions  4.3.1           Mustererkennung
> Microsoft.Data.Sqlite           8.0.11          interne Dokumentdatenbank je Profil
> SQLitePCLRaw.bundle_e_sqlite3   2.1.6           SQLite-Anbindung (transitiv)
> Microsoft.Recognizers.Text.DateTime       1.8.13  Gegenprüfung Datum
> Microsoft.Recognizers.Text.NumberWithUnit 1.8.13  Gegenprüfung Betrag

# IniGenerator-Plugin (separat)
> MySqlConnector                  2.6.2           MySQL/MariaDB-Zugriff
> System.Security.Cryptography.ProtectedData 10.0.12  Passwortschutz (DPAPI)
> Microsoft.Extensions.Logging.Abstractions 8.0.2      transitiv
> Microsoft.Extensions.DependencyInjection.Abstractions 8.0.2  transitiv

# Ohne Zusatzpaket (in Windows bzw. .NET enthalten)
* Windows.Data.Pdf – Rendern von PDF-Seiten beim Rasterisieren
* Windows-Rechtschreibprüfung (Spell Checking API) – Bestätigung richtiger Wörter bei der OCR-Korrektur
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
Dateiumbenenner steht unter der MIT-Lizenz (freie Software). Copyright (c) 2026 {GitHubUser}. Den vollständigen Text enthält die Datei LICENSE.

# Mitgelieferte Komponenten
> Microsoft.Web.WebView2   Microsoft BSD-artige Lizenz
> UglyToad.PdfPig          Apache License 2.0
> SymSpell                 MIT (Wolf Garbe)
> System.Drawing.Common    MIT (.NET Foundation)
> System.Net.Http          MIT (.NET Foundation)
> System.Text.RegularExpr. MIT (.NET Foundation)
> Microsoft.Data.Sqlite    MIT (.NET Foundation)
> SQLitePCLRaw             Apache License 2.0 (Eric Sink)
> SQLite (e_sqlite3)       Public Domain
> Microsoft.Recognizers.Text MIT (Microsoft Corporation)
> Microsoft.Extensions.* 2.2 Apache License 2.0 (transitiv)

# IniGenerator-Plugin
> MySqlConnector           MIT
> ProtectedData            MIT (.NET Foundation)
> Microsoft.Extensions.Logging.Abstractions             MIT (.NET Foundation)
> Microsoft.Extensions.DependencyInjection.Abstractions MIT (.NET Foundation)

# Mitgelieferte Daten
> GeoNames-Postleitzahlen DE   CC BY 4.0, unverändert eingebettet
> OCR-Fachwortliste            MIT, eigene Zusammenstellung
> GLEIF-Firmenverzeichnis      CC0 1.0, optionaler Download

Postleitzahlen-Daten von GeoNames (www.geonames.org), lizenziert unter Creative Commons Attribution 4.0 (creativecommons.org/licenses/by/4.0). Siehe [[plz|PLZ-Verzeichnis]].

# Microsoft.Web.WebView2 – Lizenztext
Copyright (C) Microsoft Corporation. All rights reserved.

Redistribution and use in source and binary forms, with or without modification, are permitted provided that the following conditions are met:
* Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer.
* Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following disclaimer in the documentation and/or other materials provided with the distribution.
* The name of Microsoft Corporation, or the names of its contributors may not be used to endorse or promote products derived from this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS ""AS IS"" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

# Nicht enthalten (separat zu beziehen)
* Ghostscript – AGPL 3.0 oder kommerziell (Artifex); wird nur als externes Programm aufgerufen
* Microsoft Edge WebView2 Runtime – Bestandteil von Windows

Vollständige Angaben: Datei THIRD-PARTY-NOTICES.md im [[{RepoUrl}|Projekt auf GitHub]]."),

            ("haftung", "Haftungsausschluss", @"Die Software wird ""wie besehen"" ohne jegliche ausdrückliche oder stillschweigende Gewährleistung bereitgestellt, einschließlich, aber nicht beschränkt auf die Gewährleistung der Marktgängigkeit, der Eignung für einen bestimmten Zweck und der Nichtverletzung von Rechten Dritter.

Der Autor haftet nicht für Schäden, Datenverluste oder sonstige Ansprüche, die aus der Nutzung oder der Unmöglichkeit der Nutzung der Software entstehen, soweit dies gesetzlich zulässig ist. Unberührt bleibt die Haftung bei Vorsatz und grober Fahrlässigkeit sowie nach dem Produkthaftungsgesetz.

Erkannte Dokumentdaten und vorgeschlagene Dateinamen sind vor der Umbenennung vom Benutzer zu prüfen. Legen Sie vor dem Umbenennen, Zusammenführen oder Komprimieren Sicherungskopien an.

Für Inhalte verlinkter externer Seiten sind ausschließlich deren Betreiber verantwortlich."),

            ("ueber", "Über Dateiumbenenner / Impressum", $@"Dateiumbenenner
Version {AppVersion}

Copyright (c) 2026 {GitHubUser}
Veröffentlicht unter der MIT-Lizenz.

# Kontakt
* GitHub: [[{GitHubUrl}|{GitHubUrl}]]
* Projekt: [[{RepoUrl}|{RepoUrl}]]
* E-Mail: [[mailto:{ContactEmail}|{ContactEmail}]]

Dies ist ein privates, nicht kommerzielles Open-Source-Projekt.

Siehe auch: [[haftung|Haftungsausschluss]], [[lizenzen|Lizenzen und Drittanbieter]]"),

            ("nummernregeln", "Nummernmuster und Belegarten", @"Menü bzw. Schaltfläche ""Nummernregeln"": legt fest, wie Rechnungs- und Belegnummern je Firma und Belegart erkannt werden.

# Firmenübersicht
Links stehen alle Firmen mit Regeln oder geladenen Belegen. Das Filterfeld schränkt die Liste ein; ein Klick zeigt nur die Regeln dieser Firma – in den Reitern Nummernmuster, Datum und Betrag gleichermaßen. Firma und Belegart in der Leiste oben gelten für neue Regeln und den Test.

# Reiter
Nummernmuster, Datum, Betrag und ganz rechts Belegarten. Jeder Reiter hat oben einen Hinweis, unten eine Testzeile und die Schaltflächen.

# Maske
* $ = Ziffer
* ? = Buchstabe
* # = Buchstabe oder Ziffer
* * = beliebige Zeichen
* andere Zeichen (z. B. / - .) müssen genau so vorkommen

Beispiel: Die Nummer R2306/12345 ergibt die Maske
> ?$$$$/$$$$$

# Beispielnummer
In der Spalte ""Beispielnummer"" kann eine erkannte Nummer ausgewählt werden; die Maske wird daraus automatisch abgeleitet.

# Priorität
Passende Masken werden zuerst im Text gesucht und vor der allgemeinen Nummernerkennung bevorzugt. Regeln für Firma und Belegart gehen allgemeinen Regeln vor.

# Begriff vor/nach Nummer
* Begriff (vor Nummer): Text direkt vor der Nummer (gleiche Zeile oder Zeile darüber), z. B. ""Rechnung Nr.""
* Begriff (nach Nummer): Text direkt hinter der Nummer
* beide eingetragen: die Nummer steht zwischen den Begriffen
* Seite egal: ein einzelner Begriff darf vor oder hinter der Nummer stehen

Nummern an dieser Stelle werden bevorzugt; gleich aufgebaute Nummern an anderer Stelle bleiben möglich.
* Automatisch: Ist ein Beleg geöffnet, wird bei Masken-Regeln der Firma + Belegart ohne Begriff der Begriff an der Fundstelle der Nummer eingetragen (z. B. ""Rechnung-Nr."" links daneben → Begriff vor Nummer; rechts daneben → nach Nummer) – ebenso beim Wählen einer Beispielnummer und beim Speichern eines Musters im INI-Generator. Mit ""Speichern"" übernehmen; ein eingetragener Begriff wird nie überschrieben.

# Weitere Spalten
* Art: ""Maske"" (genaues Muster, Normalfall) oder ""Merkmale"" (nur ungefähre Länge und erlaubte Zeichen)
* Länge min/max, Ziffern, Buchstaben, Leerzeichen, Sonderzeichen: gelten nur für Art ""Merkmale"" – bei ""Maske"" legt die Maske das fest (Zellen ausgegraut). Die Spalten lassen sich unten ein- und ausblenden.
* Beginnt mit (Präfix): Nummern, die nicht so anfangen, werden schwächer bewertet; bei einer Maske meist überflüssig
* Gelernt: automatisch angelegt, als eine Nummer von Hand gewählt wurde (niedrige Priorität); ""Gelernte Regeln löschen"" entfernt sie

# Datum
Im Reiter ""Datum"" wird – im Stil der Nummernmuster – je Firma und Belegart festgelegt, welches Datum gilt:
* Begriff: Bezeichnung beim Datum, z. B. Rechnungsdatum, Belegdatum, Lieferdatum
* Lage: Datum rechts neben dem Begriff, darunter (Spaltenüberschrift) oder links davor; beliebig = alle
* Datumsformat: TT.MM.JJJJ, TT. Monat JJJJ, JJJJ-MM-TT, MM/TT/JJJJ oder eigene Maske (TT?MM?JJJJ, ? = Trennzeichen); beliebig = alle Schreibweisen
* Ohne Begriff wertet die Regel nur Daten in diesem Format auf.
* ""Aus Beleg vorschlagen"" liest Begriff, Lage und Schreibweise aus dem geöffneten Beleg; die Testzeile zeigt, welches Datum die Regeln finden.

# Belegarten
Im Reiter ""Belegarten"" (ganz rechts) werden Bezeichnungen und Synonyme gepflegt (z. B. Sammelrechnung, Barverkauf, Gutschrift, Storno). Sie gelten für alle Firmen.

Siehe auch: [[inixml|INI- und XML-Export]]"),

            ("profile", "Profile", @"Über das Menü ""Profile"" werden Einstellungen, Nummernregeln und Belegarten getrennt gespeichert, z. B. pro Mandant.

* Profil wechseln: Menü Profile, gewünschtes Profil wählen.
* Neues Profil: Name eingeben; die aktuellen Einstellungen werden übernommen.
* Profilordner öffnen: zeigt die gespeicherten Dateien im Explorer."),

            ("listen", "Listen: Kopieren, Markieren, Filtern", @"Alle Dateilisten (Hauptliste, INI Erzeugen, Firma übertragen) bieten ein Kontextmenü (rechte Maustaste):

* Kopieren (Zeilen) – Strg+C, tabulatorgetrennt mit Spaltenüberschriften (z. B. für Excel)
* Dateinamen kopieren
* Dateipfade kopieren
* Alles markieren – Strg+A

# Filter
Mehrere Suchwörter durch Leerzeichen trennen; es werden nur Einträge angezeigt, die alle Wörter enthalten."),

            ("firmauebertragen", "Firma übertragen", @"Schaltfläche ""Namen übertragen"": überträgt den Firmennamen und die Adresse des aktuellen Belegs auf weitere Belege.

* Oben: Gruppen gleich erkannter Firmen; ähnliche Einträge sind vorausgewählt. Leertaste schaltet die Auswahl um.
* Spalte ""Treffer"": Grund der Vorauswahl.
* Unten links: Dateien der markierten Gruppe, Firmenname direkt bearbeitbar.
* Unten rechts: Vorschau (PDF oder Textmodus, Vollbildvorschau).
* Filter: mehrere Wörter möglich.
* Im Plugin [[inixml|INI Erzeugen]] funktioniert der Dialog gleich und überträgt zusätzlich die Adressnummer.

# Belegart übertragen
* Gruppen nach Firma, Nummernaufbau und bisheriger Belegart. Vorausgewählt sind Gruppen der gleichen Firma mit gleichem Nummernaufbau/-kreis oder ähnlichem Inhalt – auch Gruppen, die die Belegart schon haben (Treffer ""bereits …""), damit sie bei einer Gruppenzuweisung mitwechseln.
* Die Statuszeile zeigt, wie viele markierte Belege die gewählte Belegart schon haben."),

            ("index", "Index", @"> A
* [[technik|Aufbau des Programms]]
> D
* [[ueber|Datenschutz / Kontakt]]
* [[kompression|DPI]]
> F
* [[firmenverzeichnis|Firmenverzeichnis]]
> G
* [[gegenpruefung|Gegenprüfung Datum/Betrag]]
* [[plz|GeoNames]]
* [[firmenverzeichnis|GLEIF]]
* [[kompression|Ghostscript]]
* [[haftung|Gewährleistung]]
> H
* [[haftung|Haftungsausschluss]]
* [[signatur|Hash (SHA-256)]]
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
* [[plz|PLZ-Verzeichnis]]
* [[plz|Postleitzahlen]]
* [[praefix|Präfix]]
* [[signatur|Prüfsummen]]
> R
* [[kompression|Rasterisieren]]
* [[gegenpruefung|Recognizers-Text]]
* [[umbenennen|Rechnungsnummer]]
* [[nummernregeln|Maske]]
* [[nummernregeln|Belegarten]]
* [[nummernregeln|Gutschrift]]
* [[nummernregeln|Storno]]
* [[nummernregeln|Sammelrechnung]]
* [[nummernregeln|Barverkauf]]
* [[profile|Profil]]
* [[listen|Kopieren]]
* [[listen|Dateinamen kopieren]]
* [[listen|Filter]]
> S
* [[signatur|Signatur]]
* [[system|Systemanforderungen]]
> T
* [[txtnummern|TXT-Nummern korrigieren]]
* [[txtnummern|Texterkennungsfehler in Nummern]]
> U
* [[umbenennen|Umbenennen]]
> V
* [[technik|Version]]
> W
* [[system|WebView2]]
* [[ocr|Wortkorrektur]]
* [[ocr|Wörterbuch]]
> Z
* [[signatur|Zertifikat]]
* [[zusammenfuehren|Zusammenführen]]")
        };

        #endregion

        #region Plugin-Hilfe

        /// <summary>Vom Hauptfenster gesetzte Liste der geladenen Plugins.</summary>
        public static IReadOnlyList<Plugins.LoadedPlugin> LoadedPlugins { get; set; } = Array.Empty<Plugins.LoadedPlugin>();

        private void AddPluginTopics()
        {
            var sb = new System.Text.StringBuilder("Plugins erweitern den Dateiumbenenner um zusätzliche Funktionen. Sie werden im Menü \"Plugins\" importiert und aktiviert und liegen im Programmordner unter \"Plugins\". Jedes Plugin bringt seine eigenen Abhängigkeiten mit.\n\n# Geladene Plugins\n");
            var extra = new List<(string Id, string Title, string Body)>();
            if (LoadedPlugins.Count == 0) sb.Append("Derzeit ist kein Plugin geladen.\n");
            foreach (var lp in LoadedPlugins)
            {
                var p = lp.Plugin;
                var first = (string?)null;
                if (p is Plugins.IPluginHelpProvider hp)
                {
                    try
                    {
                        foreach (var t in hp.GetHelpTopics())
                        {
                            var id = $"plugin:{p.Id}:{t.Id}";
                            first ??= id;
                            extra.Add((id, t.Title, t.Body));
                        }
                    }
                    catch { }
                }
                var name = first != null ? $"[[{first}|{p.Name}]]" : p.Name;
                sb.Append($"* {name}, Version {p.Version}{(lp.Enabled ? "" : " (deaktiviert)")} – {p.Description}\n");
            }
            // Plugin-interne Verweise auf eigene Themen-IDs umschreiben
            for (int i = 0; i < extra.Count; i++)
            {
                var body = extra[i].Body;
                foreach (var e in extra)
                {
                    var shortId = e.Id[(e.Id.LastIndexOf(':') + 1)..];
                    var prefix = e.Id[..(e.Id.LastIndexOf(':') + 1)];
                    if (extra[i].Id.StartsWith(prefix)) body = body.Replace("[[" + shortId + "|", "[[" + e.Id + "|");
                }
                extra[i] = (extra[i].Id, extra[i].Title, body);
            }
            int idx = _topics.FindIndex(t => t.Id == "lizenzen");
            if (idx < 0) idx = _topics.Count;
            _topics.Insert(idx, ("plugins", "Plugins (Erweiterungen)", sb.ToString().TrimEnd()));
            _topics.InsertRange(idx + 1, extra);

            // Inhaltsverzeichnis: je geladenem Plugin ein Abschnitt mit seinen Themen
            var toc = new System.Text.StringBuilder();
            foreach (var lp in LoadedPlugins)
            {
                var own = extra.Where(e => e.Id.StartsWith($"plugin:{lp.Plugin.Id}:")).ToList();
                if (own.Count == 0) continue;
                toc.Append($"# {lp.Plugin.Name}\n");
                foreach (var e in own) toc.Append($"* [[{e.Id}|{e.Title}]]\n");
                toc.Append('\n');
            }
            int home = _topics.FindIndex(t => t.Id == "inhalt");
            if (home >= 0 && toc.Length > 0)
            {
                var body = _topics[home].Body.Replace("\r", "");
                int at = body.IndexOf("# Technische Informationen", StringComparison.Ordinal);
                body = at < 0 ? body + "\n\n" + toc : body.Insert(at, toc.ToString());
                _topics[home] = (_topics[home].Id, _topics[home].Title, body);
            }
        }

        #endregion
    }
}
