using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using WpfBinding = System.Windows.Data.Binding;
using WpfButton = System.Windows.Controls.Button;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfListView = System.Windows.Controls.ListView;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfTextBox = System.Windows.Controls.TextBox;

// Gemeinsamer „Eigenschaften übertragen“-Dialog (Hauptprogramm und INI-Generator binden diese Datei jeweils selbst ein).
namespace Dateiumbenenner
{
    /// <summary>Beleg im Übertragen-Dialog – unabhängig davon, ob er aus dem Hauptprogramm oder dem INI-Generator stammt.</summary>
    internal sealed class TransferDoc
    {
        public object Item { get; init; } = null!;
        public string FilePath { get; init; } = "";
        public string FileName => System.IO.Path.GetFileName(FilePath);
        public string? Company { get; init; }
        public string? Detected { get; init; }
        public string? DocType { get; init; }
        public string? Number { get; init; }
        public string? Date { get; init; }
        public string? FullText { get; init; }
        public IReadOnlyList<string> Previous { get; init; } = Array.Empty<string>();
        /// <summary>Aktuelle Werte je Eigenschaft (Schlüssel wie <see cref="TransferProperty.Key"/>) – zeigt, was sich ändern würde.</summary>
        public Dictionary<string, string?> Values { get; init; } = new();
    }

    /// <summary>Übertragbare Eigenschaft bzw. Option des Dialogs.</summary>
    internal sealed class TransferProperty
    {
        public string Key { get; init; } = "";
        public string Label { get; init; } = "";
        /// <summary>Wert der Quelle (Anzeige und Vergleich mit <see cref="TransferDoc.Values"/>); null bei Optionen.</summary>
        public string? Value { get; init; }
        /// <summary>Am Quellbeleg von Hand geändert (gegenüber der Erkennung).</summary>
        public bool Changed { get; init; }
        public bool Selected { get; set; }
        public string? ToolTip { get; init; }
        /// <summary>Option (z. B. „Regeln neu anwenden“) statt übertragbarer Wert.</summary>
        public bool IsOption { get; init; }
    }

    /// <summary>„Eigenschaften übertragen“: Eigenschaften wählen (von Hand geänderte sind vorausgewählt) und Zielbelege gruppiert nach
    /// Aufbau auswählen – Vorauswahl: gleiche Firma (auch die vor der Korrektur erkannte) und gleicher Nummernaufbau/-kreis oder ähnlicher Inhalt.</summary>
    internal sealed class PropertyTransferDialog : Window
    {
        public sealed class Row : INotifyPropertyChanged
        {
            private bool _selected;
            private string _changes = "";
            public TransferDoc Doc { get; init; } = null!;
            public bool Selected { get => _selected; set { if (_selected == value) return; _selected = value; Changed(nameof(Selected)); SelectionChanged?.Invoke(); } }
            public string FileName => Doc.FileName;
            public string? Company => Doc.Company;
            public string? Detected => Doc.Detected;
            public string? DocType => Doc.DocType;
            public string? Number => Doc.Number;
            public string Similarity { get; init; } = "";
            internal double Sim { get; init; }
            /// <summary>Eigenschaften, die sich bei diesem Beleg ändern würden.</summary>
            public string Changes { get => _changes; set { if (_changes == value) return; _changes = value; Changed(nameof(Changes)); } }
            internal Action? SelectionChanged;
            public event PropertyChangedEventHandler? PropertyChanged;
            private void Changed(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        }

        public sealed class Group : INotifyPropertyChanged
        {
            public string Company { get; init; } = "";
            public string DocType { get; init; } = "";
            public string Shape { get; init; } = "";
            public string Match { get; init; } = "";
            internal bool Suggested { get; init; }
            public List<Row> Rows { get; } = new();
            public int Count => Rows.Count;
            public bool? Selected
            {
                get => Rows.All(r => r.Selected) ? true : Rows.Any(r => r.Selected) ? null : false;
                set { foreach (var r in Rows) r.Selected = value == true; }
            }
            public string Changes
            {
                get
                {
                    var c = Rows.Select(r => r.Changes).Where(x => x.Length > 0 && x != NoChange).SelectMany(x => x.Split(", "))
                        .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    return c.Count == 0 ? NoChange : string.Join(", ", c);
                }
            }
            public event PropertyChangedEventHandler? PropertyChanged;
            internal void Notify()
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Changes)));
            }
        }

        private const string NoChange = "–";
        private const double ContentThreshold = 0.45;
        private static readonly string[] Modes = { "Firma + Nummernaufbau + Belegart", "Firma + Belegart", "Erkannter Firmenname + Belegart", "Keine Gruppen – alle Belege einzeln" };
        private static readonly string[] ModeHeaders = { "Firma", "Firma", "Firma erkannt", "Beleg" };
        private static int _mode;

        private readonly List<TransferProperty> _props;
        private readonly ObservableCollection<Group> _groups;
        private readonly ICollectionView _view;
        private readonly WpfTextBox _filter;
        private readonly TextBlock _info;
        private readonly WpfButton _ok;

        /// <summary>Ergebnis: gewählte Zielbelege (<see cref="TransferDoc.Item"/>) in der Reihenfolge der Eingabe.</summary>
        public List<object> SelectedItems { get; } = new();

        /// <summary>Eigenschaft bzw. Option gewählt?</summary>
        public bool Has(string key) => _props.Any(p => p.Selected && p.Key == key);

        /// <param name="source">Quellbeleg.</param>
        /// <param name="sourceCompanies">Firmennamen, unter denen gleichartige Belege zu finden sind (aktuell, erkannt, früher).</param>
        /// <param name="preselected">Vorab in der Liste markierte Belege – werden statt der Vorschläge angehakt.</param>
        public PropertyTransferDialog(TransferDoc source, IEnumerable<string?> sourceCompanies, IEnumerable<TransferDoc> targets,
            IEnumerable<TransferProperty> properties, Func<string?, string?, bool> sameCompany, IEnumerable<object>? preselected = null)
        {
            Title = "Eigenschaften übertragen";
            Width = 1300; Height = 860;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            _props = properties.ToList();

            var companies = sourceCompanies.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            bool CompanyFits(TransferDoc d) => new[] { d.Company, d.Detected }.Concat(d.Previous)
                .Any(c => !string.IsNullOrWhiteSpace(c) && companies.Any(s => sameCompany(c, s)));
            var srcWords = Words(source.FullText);
            var srcShape = Shape(source.Number);
            var srcRange = Range(source.Number);

            static string Disp(string? s) => string.IsNullOrWhiteSpace(s) ? "(leer)" : s.Trim();
            static string Join(IEnumerable<string?> v, string empty)
            {
                var s = string.Join(" | ", v.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase));
                return s.Length == 0 ? empty : s;
            }
            string KeyOf(Row r, int mode) => mode switch
            {
                1 => $"{Disp(r.Company)}\u0001{Disp(r.DocType)}",
                2 => $"{Disp(r.Detected)}\u0001{Disp(r.DocType)}",
                3 => r.Doc.FilePath,
                _ => $"{Disp(r.Company)}\u0001{Shape(r.Number)}\u0001{Disp(r.DocType)}"
            };
            Group MakeGroup(List<Row> rows, int mode)
            {
                bool company = rows.Any(r => CompanyFits(r.Doc));
                var shapes = rows.Select(r => Shape(r.Number)).Distinct().ToList();
                var reasons = new List<string>();
                if (company) reasons.Add("Firma");
                if (srcShape.Length > 0 && shapes.Contains(srcShape)) reasons.Add("Nummernaufbau");
                if (srcRange != null && rows.Any(r => Range(r.Number) == srcRange)) reasons.Add("Nummernkreis " + srcRange + "…");
                double best = rows.Max(r => r.Sim);
                if (best >= ContentThreshold) reasons.Add($"Inhalt {best:P0}");
                if (!string.IsNullOrWhiteSpace(source.DocType) && rows.Any(r => string.Equals(r.DocType?.Trim(), source.DocType.Trim(), StringComparison.OrdinalIgnoreCase)))
                    reasons.Add("gleiche Belegart");
                bool structure = reasons.Any(r => r != "Firma" && r != "gleiche Belegart");
                var g = new Group
                {
                    Company = mode switch
                    {
                        2 => Join(rows.Select(r => r.Detected), "(leer)"),
                        3 => $"{Disp(rows[0].Company)}  ({rows[0].FileName})",
                        _ => Join(rows.Select(r => r.Company), "(leer)")
                    },
                    Shape = Join(shapes, "(keine Nummer)"),
                    DocType = Join(rows.Select(r => r.DocType), "(leer)"),
                    Match = string.Join(", ", reasons),
                    Suggested = company && structure,
                };
                g.Rows.AddRange(rows);
                foreach (var r in g.Rows) r.SelectionChanged = () => { g.Notify(); UpdateInfo(); };
                return g;
            }
            List<Group> Build(IEnumerable<Row> rows, int mode)
            {
                var groups = rows.GroupBy(r => KeyOf(r, mode), StringComparer.OrdinalIgnoreCase).Select(g => MakeGroup(g.ToList(), mode));
                // „Keine Gruppen“ bzw. „Gruppen wie Dateiliste“: Reihenfolge der Dateiliste (GroupBy behält die Reihenfolge des ersten Belegs)
                return mode == Modes.Length - 1 || TransferSearch.GroupsInListOrder ? groups.ToList() : groups
                    .OrderByDescending(g => g.Selected != false)
                    .ThenByDescending(g => g.Suggested)
                    .ThenByDescending(g => g.Count).ThenBy(g => g.Company)
                    .ToList();
            }

            var allRows = targets.Select(d => { var sim = Jaccard(srcWords, Words(d.FullText)); return new Row { Doc = d, Sim = sim, Similarity = $"{sim:P0}" }; }).ToList();
            var pre = preselected?.ToHashSet() ?? new HashSet<object>();
            var marked = allRows.Where(r => pre.Contains(r.Doc.Item)).ToList();
            if (marked.Count > 0)
                foreach (var r in marked) r.Selected = true;
            else
                foreach (var g in Build(allRows, 0).Where(g => g.Suggested))
                    foreach (var r in g.Rows) r.Selected = true;
            _groups = new ObservableCollection<Group>(Build(allRows, _mode));

            var root = new DockPanel { Margin = new Thickness(8) };

            // Kopf: Quelle, Eigenschaften, Optionen, Filter
            var head = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
            DockPanel.SetDock(head, Dock.Top);
            head.Children.Add(new TextBlock
            {
                Text = $"Quelle: {source.FileName} – {Disp(source.Company)} – {Disp(source.DocType)}"
                    + (srcShape.Length > 0 ? $"  (Nummer {source.Number}, Aufbau {srcShape})" : ""),
                FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4)
            });
            var propBox = new System.Windows.Controls.GroupBox { Header = "Zu übertragende Eigenschaften (✎ = am Quellbeleg von Hand geändert, vorausgewählt)", Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 0, 0, 4) };
            var propPanel = new StackPanel();
            var wrap = new WrapPanel();
            var checks = new List<(TransferProperty p, WpfCheckBox c)>();
            WpfCheckBox MakeCheck(TransferProperty p)
            {
                var text = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 380 };
                if (p.Changed) text.Inlines.Add(new System.Windows.Documents.Run("✎ ") { Foreground = System.Windows.Media.Brushes.DarkOrange });
                text.Inlines.Add(new System.Windows.Documents.Run(p.Label) { FontWeight = p.Changed ? FontWeights.SemiBold : FontWeights.Normal });
                if (!p.IsOption) text.Inlines.Add(new System.Windows.Documents.Run(": " + Disp(p.Value)) { Foreground = System.Windows.Media.Brushes.DimGray });
                var c = new WpfCheckBox
                {
                    Content = text, IsChecked = p.Selected, Margin = new Thickness(0, 2, 18, 2), VerticalContentAlignment = VerticalAlignment.Center,
                    ToolTip = (p.IsOption ? "" : $"{p.Label}: {Disp(p.Value)}" + (p.Changed ? " (von Hand geändert)" : "")) + (p.ToolTip is { Length: > 0 } t ? (p.IsOption ? "" : "\n") + t : "")
                };
                c.Click += (_, _) => { p.Selected = c.IsChecked == true; RefreshChanges(); };
                checks.Add((p, c));
                return c;
            }
            foreach (var p in _props.Where(p => !p.IsOption)) wrap.Children.Add(MakeCheck(p));
            propPanel.Children.Add(wrap);
            var quick = new StackPanel { Orientation = WpfOrientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            WpfButton Quick(string text, Func<TransferProperty, bool> on)
            {
                var b = new WpfButton { Content = text, Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(0, 0, 4, 0) };
                b.Click += (_, _) =>
                {
                    foreach (var (p, c) in checks.Where(x => !x.p.IsOption)) { p.Selected = on(p); c.IsChecked = p.Selected; }
                    RefreshChanges();
                };
                quick.Children.Add(b);
                return b;
            }
            Quick("Nur geänderte", p => p.Changed).IsEnabled = _props.Any(p => p.Changed && !p.IsOption);
            Quick("Alle", _ => true);
            Quick("Keine", _ => false);
            var opts = _props.Where(p => p.IsOption).ToList();
            if (opts.Count > 0)
            {
                quick.Children.Add(new Border { Width = 1, Margin = new Thickness(8, 0, 12, 0), Background = System.Windows.Media.Brushes.LightGray });
                foreach (var p in opts) quick.Children.Add(MakeCheck(p));
            }
            propPanel.Children.Add(quick);
            propBox.Content = propPanel;
            head.Children.Add(propBox);

            head.Children.Add(new TextBlock
            {
                Text = (marked.Count > 0
                        ? $"Angehakt: die {marked.Count} in der Liste markierten Belege."
                        : "Vorausgewählt: gleiche Firma (auch die vor der Korrektur erkannte) und gleicher Nummernaufbau/-kreis oder ähnlicher Inhalt.")
                    + " Ganze Gruppen oder einzelne Belege an-/abhaken (Leertaste), „Wird geändert“ zeigt die Wirkung.",
                TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.DimGray, Margin = new Thickness(0, 0, 0, 4)
            });
            var fp = new DockPanel();
            fp.Children.Add(new TextBlock { Text = "Filter: ", VerticalAlignment = VerticalAlignment.Center, Width = 50 });
            var right = new StackPanel { Orientation = WpfOrientation.Horizontal };
            DockPanel.SetDock(right, Dock.Right);
            right.Children.Add(TransferSearch.CreateTextOption(() => _view?.Refresh()));
            right.Children.Add(new TextBlock { Text = "Gruppieren nach: ", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) });
            var modeBox = new WpfComboBox { Width = 250, ItemsSource = Modes, SelectedIndex = _mode };
            right.Children.Add(modeBox);
            Action regroup = () => { };
            right.Children.Add(TransferSearch.CreateOrderOption(() => regroup()));
            fp.Children.Add(right);
            _filter = new WpfTextBox { ToolTip = "Sucht in Firmenname (aktuell, erkannt, früher erkannt), Dateiname, Belegart, Nummer, Datum – und optional im Belegtext" };
            fp.Children.Add(_filter);
            head.Children.Add(fp);
            root.Children.Add(head);

            // Fuß: Schaltflächen
            var buttons = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
            DockPanel.SetDock(buttons, Dock.Bottom);
            _info = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Foreground = System.Windows.Media.Brushes.DimGray };
            var bp = new StackPanel { Orientation = WpfOrientation.Horizontal, HorizontalAlignment = WpfHorizontalAlignment.Right };
            DockPanel.SetDock(bp, Dock.Right);
            WpfButton Btn(string text, RoutedEventHandler h)
            {
                var b = new WpfButton { Content = text, Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(6, 0, 0, 0) };
                b.Click += h; bp.Children.Add(b); return b;
            }
            Btn("Sichtbare anhaken", (_, _) => { foreach (Group g in _view!) g.Selected = true; });
            Btn("Sichtbare abwählen", (_, _) => { foreach (Group g in _view!) g.Selected = false; });
            Btn("Nur mit Änderung", (_, _) => { foreach (var r in _groups.SelectMany(g => g.Rows)) r.Selected = r.Selected && r.Changes != NoChange; });
            _ok = Btn("Übertragen", (_, _) =>
            {
                var chosen = _groups.SelectMany(g => g.Rows).Where(r => r.Selected).Select(r => r.Doc.Item).ToHashSet();
                SelectedItems.AddRange(allRows.Select(r => r.Doc.Item).Where(chosen.Contains));
                DialogResult = true;
            });
            _ok.IsDefault = true;
            Btn("Abbrechen", (_, _) => DialogResult = false).IsCancel = true;
            buttons.Children.Add(bp);
            buttons.Children.Add(_info);
            root.Children.Add(buttons);

            // Mitte: Gruppen oben, Belege + Vorschau unten
            var lv = new WpfListView();
            var gv = new GridView();
            gv.Columns.Add(new GridViewColumn { Header = "", Width = 30, CellTemplate = ListKeyboard.CheckTemplate(nameof(Group.Selected)) });
            gv.Columns.Add(new GridViewColumn { Header = "Anzahl", Width = 55, DisplayMemberBinding = new WpfBinding(nameof(Group.Count)) });
            var companyCol = new GridViewColumn { Header = ModeHeaders[_mode], Width = 260, DisplayMemberBinding = new WpfBinding(nameof(Group.Company)) };
            gv.Columns.Add(companyCol);
            gv.Columns.Add(new GridViewColumn { Header = "Nummernaufbau", Width = 130, DisplayMemberBinding = new WpfBinding(nameof(Group.Shape)) });
            gv.Columns.Add(new GridViewColumn { Header = "Belegart", Width = 140, DisplayMemberBinding = new WpfBinding(nameof(Group.DocType)) });
            gv.Columns.Add(new GridViewColumn { Header = "Wird geändert", Width = 250, DisplayMemberBinding = new WpfBinding(nameof(Group.Changes)) });
            gv.Columns.Add(new GridViewColumn { Header = "Übereinstimmung (Grund der Vorauswahl)", Width = 320, DisplayMemberBinding = new WpfBinding(nameof(Group.Match)) });
            lv.View = gv;
            lv.ItemsSource = _groups;
            ListViewCopyHelper.Attach(lv);
            ListKeyboard.AttachSpaceToggle<Group>(lv, g => g.Selected == true, (g, on) => g.Selected = on);

            var body = new Grid();
            body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.3, GridUnitType.Star) });
            Grid.SetRow(lv, 0);
            body.Children.Add(lv);
            var split = new GridSplitter { Height = 5, HorizontalAlignment = WpfHorizontalAlignment.Stretch };
            Grid.SetRow(split, 1);
            body.Children.Add(split);

            var bottom = new Grid();
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.7, GridUnitType.Star) });
            Grid.SetRow(bottom, 2);
            body.Children.Add(bottom);

            var files = new WpfListView();
            var fgv = new GridView();
            fgv.Columns.Add(new GridViewColumn { Header = "", Width = 30, CellTemplate = ListKeyboard.CheckTemplate(nameof(Row.Selected)) });
            fgv.Columns.Add(new GridViewColumn { Header = "Dateiname", Width = 220, DisplayMemberBinding = new WpfBinding(nameof(Row.FileName)) });
            fgv.Columns.Add(new GridViewColumn { Header = "Wird geändert", Width = 170, DisplayMemberBinding = new WpfBinding(nameof(Row.Changes)) });
            fgv.Columns.Add(new GridViewColumn { Header = "Nummer", Width = 100, DisplayMemberBinding = new WpfBinding(nameof(Row.Number)) });
            fgv.Columns.Add(new GridViewColumn { Header = "Belegart", Width = 90, DisplayMemberBinding = new WpfBinding(nameof(Row.DocType)) });
            fgv.Columns.Add(new GridViewColumn { Header = "Firma", Width = 140, DisplayMemberBinding = new WpfBinding(nameof(Row.Company)) });
            fgv.Columns.Add(new GridViewColumn { Header = "Firma erkannt", Width = 130, DisplayMemberBinding = new WpfBinding(nameof(Row.Detected)) });
            fgv.Columns.Add(new GridViewColumn { Header = "Inhalt ähnl.", Width = 70, DisplayMemberBinding = new WpfBinding(nameof(Row.Similarity)) });
            files.View = fgv;
            ListViewCopyHelper.Attach(files);
            ListKeyboard.AttachSpaceToggle<Row>(files, r => r.Selected, (r, on) => r.Selected = on);
            bottom.Children.Add(files);
            GroupTools.Attach(lv, files, _groups, g => g.Rows, (g, rows, single) =>
            {
                var ng = new Group { Company = g.Company, Shape = g.Shape, DocType = g.DocType, Match = single ? $"{g.Match} – {rows[0].FileName}".TrimStart(' ', '–') : "herausgelöst" + (g.Match.Length > 0 ? ": " + g.Match : "") };
                ng.Rows.AddRange(rows);
                foreach (var r in rows) r.SelectionChanged = () => { ng.Notify(); UpdateInfo(); };
                return ng;
            }, g => g.Notify(), UpdateInfo);
            ListKeyboard.LinkLists(lv, files);
            var anchorOf = ListKeyboard.KeepSelection(lv, files, g => ((Group)g).Rows);
            var vsplit = new GridSplitter { Width = 5, HorizontalAlignment = WpfHorizontalAlignment.Stretch };
            Grid.SetColumn(vsplit, 1);
            bottom.Children.Add(vsplit);
            var previewHost = new DockPanel();
            Grid.SetColumn(previewHost, 2);
            bottom.Children.Add(previewHost);
            var chkText = new WpfCheckBox { Content = "Textmodus", Margin = new Thickness(0, 0, 0, 4) };
            DockPanel.SetDock(chkText, Dock.Top);
            previewHost.Children.Add(chkText);
            var preview = new Grid();
            previewHost.Children.Add(preview);

            Microsoft.Web.WebView2.Wpf.WebView2? web = null;
            var text = new WpfTextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new System.Windows.Media.FontFamily("Consolas"), IsInactiveSelectionHighlightEnabled = true };
            TextSearchBar? search = null;
            async System.Threading.Tasks.Task ShowPreview()
            {
                if (files.SelectedItem is not Row { Doc: var d }) { preview.Children.Clear(); search?.DocumentChanged("", null, false); return; }
                if (chkText.IsChecked != true && System.IO.Path.GetExtension(d.FilePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(d.FilePath))
                {
                    try
                    {
                        web ??= new Microsoft.Web.WebView2.Wpf.WebView2();
                        if (!preview.Children.Contains(web)) { preview.Children.Clear(); preview.Children.Add(web); }
                        search?.DocumentChanged(d.FullText, null, false);
                        await web.EnsureCoreWebView2Async();
                        search?.SetWeb(web);
                        web.Source = new Uri(d.FilePath);
                        return;
                    }
                    catch { /* WebView2 nicht verfügbar → Text */ }
                }
                if (!preview.Children.Contains(text)) { preview.Children.Clear(); preview.Children.Add(text); }
                text.Text = d.FullText ?? "";
                search?.DocumentChanged(text.Text, text, true);
            }
            search = new TextSearchBar(async () =>
            {
                if (files.SelectedItem is not Row) return null;
                if (!preview.Children.Contains(text)) { chkText.IsChecked = true; await ShowPreview(); }
                return text;
            });
            DockPanel.SetDock(search, Dock.Top);
            previewHost.Children.Insert(1, search);
            search.AttachShortcut(this);
            search.FollowFilter(_filter);
            lv.SelectionChanged += (_, _) =>
            {
                files.ItemsSource = (lv.SelectedItem as Group)?.Rows;
                if (files.Items.Count > 0) files.SelectedIndex = 0;
            };
            files.SelectionChanged += async (_, _) => await ShowPreview();
            chkText.Click += async (_, _) => await ShowPreview();

            root.Children.Add(body);
            Content = root;

            _view = CollectionViewSource.GetDefaultView(_groups);
            _view.Filter = o =>
            {
                var words = _filter.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (words.Length == 0) return true;
                if (o is not Group g) return false;
                var key = string.Join("\u0001", new[] { g.Company, g.DocType, g.Shape, g.Match }
                    .Concat(g.Rows.SelectMany(r => new[] { r.FileName, r.Company, r.Detected, r.Number, r.Doc.Date }.Concat(r.Doc.Previous))));
                return TransferSearch.Matches(words, key, null) || TransferSearch.InText && g.Rows.Any(r => TransferSearch.Matches(words, "", r.Doc.FullText));
            };
            var filterTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            filterTimer.Tick += (_, _) => { filterTimer.Stop(); _view.Refresh(); };
            _filter.TextChanged += (_, _) => { filterTimer.Stop(); filterTimer.Start(); };

            modeBox.SelectionChanged += (_, _) =>
            {
                if (modeBox.SelectedIndex < 0 || modeBox.SelectedIndex == _mode) return;
                _mode = modeBox.SelectedIndex;
                regroup();
            };
            regroup = () =>
            {
                var current = anchorOf() as Row ?? files.SelectedItem as Row ?? (lv.SelectedItem as Group)?.Rows.FirstOrDefault();
                var present = _groups.SelectMany(g => g.Rows).ToHashSet();
                var rows = allRows.Where(present.Contains).ToList();
                lv.SelectedItem = null;
                _groups.Clear();
                foreach (var g in Build(rows, _mode)) _groups.Add(g);
                companyCol.Header = ModeHeaders[_mode];
                var sel = current == null ? null : _groups.FirstOrDefault(g => g.Rows.Contains(current));
                if (sel != null && _view.Contains(sel))
                {
                    lv.SelectedItem = sel;
                    lv.ScrollIntoView(sel);
                    files.SelectedItem = current;
                }
                UpdateInfo();
            };
            RefreshChanges();
            Loaded += (_, _) =>
            {
                lv.SelectedItem = _groups.FirstOrDefault(g => g.Selected != false) ?? _groups.FirstOrDefault();
                ListKeyboard.FocusSelected(lv);
            };
        }

        private static bool SameValue(string? a, string? b) => string.Equals(a?.Trim() ?? "", b?.Trim() ?? "", StringComparison.OrdinalIgnoreCase);

        /// <summary>„Wird geändert“ je Beleg neu berechnen (nach Änderung der gewählten Eigenschaften).</summary>
        private void RefreshChanges()
        {
            var props = _props.Where(p => p.Selected && !p.IsOption).ToList();
            foreach (var r in _groups.SelectMany(g => g.Rows))
            {
                var c = props.Where(p => !r.Doc.Values.TryGetValue(p.Key, out var v) || !SameValue(v, p.Value)).Select(p => p.Label).ToList();
                r.Changes = c.Count == 0 ? NoChange : string.Join(", ", c);
            }
            foreach (var g in _groups) g.Notify();
            UpdateInfo();
        }

        private void UpdateInfo()
        {
            if (_info == null || _ok == null) return;
            var rows = _groups.SelectMany(g => g.Rows).Where(r => r.Selected).ToList();
            int changing = rows.Count(r => r.Changes != NoChange);
            bool any = _props.Any(p => p.Selected);
            _info.Text = $"{rows.Count} Beleg(e) angehakt, bei {changing} ändert sich ein Wert"
                + (_props.Any(p => p.Selected && p.IsOption) ? " (Optionen wirken auf alle angehakten)" : "")
                + (any ? "" : " – keine Eigenschaft gewählt");
            _ok.IsEnabled = rows.Count > 0 && any;
        }

        /// <summary>Nummernaufbau: Ziffer → 9, Buchstabe → A, Trennzeichen bleiben.</summary>
        internal static string Shape(string? number)
            => string.IsNullOrWhiteSpace(number) ? "" : Dateiumbenenner.Engine.NumberPatternStore.Shape(number.Trim());

        /// <summary>Nummernkreis: Nummer ohne die letzten 3 Stellen (z. B. LS-10234 → LS-10); null bei zu kurzen Nummern.</summary>
        internal static string? Range(string? number)
        {
            var n = number?.Replace(" ", "").Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(n) || n.Count(char.IsDigit) < 5) return null;
            return n[..^3];
        }

        private static readonly Regex WordRx = new(@"\p{L}{4,}", RegexOptions.Compiled);

        internal static HashSet<string> Words(string? text)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(text)) return set;
            foreach (Match m in WordRx.Matches(text)) set.Add(m.Value);
            return set;
        }

        internal static double Jaccard(HashSet<string> a, HashSet<string> b)
        {
            if (a.Count == 0 || b.Count == 0) return 0;
            int common = a.Count(b.Contains);
            return (double)common / (a.Count + b.Count - common);
        }
    }
}
