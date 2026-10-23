using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Button = System.Windows.Controls.Button;
using Binding = System.Windows.Data.Binding;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using ListView = System.Windows.Controls.ListView;
using Orientation = System.Windows.Controls.Orientation;
using TextBox = System.Windows.Controls.TextBox;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace Dateiumbenenner
{
    /// <summary>„Belegart übertragen“: Belege gruppiert nach Aufbau (Firma, Nummernaufbau, bisherige Belegart),
    /// ankreuzbar je Gruppe oder Beleg; Gruppenzuweisung verschiedener Belegarten in einem Durchgang.</summary>
    public class DocTypeTransferDialog : Window
    {
        public class Row : INotifyPropertyChanged
        {
            private bool _selected;
            private string _pending = "";
            public DocumentMetadata Item { get; init; } = null!;
            public bool Selected { get => _selected; set { if (_selected == value) return; _selected = value; Changed(nameof(Selected)); SelectionChanged?.Invoke(); } }
            /// <summary>Bereits zugewiesene neue Belegart (Gruppenzuweisung), leer = unverändert.</summary>
            public string Pending { get => _pending; set { _pending = value; Changed(nameof(Pending)); } }
            public string FileName => Item.FileName;
            public string? Current => Item.DocumentType;
            public string? Number => Item.SelectedNumber;
            public string Similarity { get; init; } = "";
            internal Action? SelectionChanged;
            public event PropertyChangedEventHandler? PropertyChanged;
            private void Changed(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        }

        public class Group : INotifyPropertyChanged
        {
            public string Company { get; init; } = "";
            public string Current { get; init; } = "";
            public string Shape { get; init; } = "";
            public string Match { get; init; } = "";
            public List<Row> Rows { get; } = new();
            public int Count => Rows.Count;
            public bool? Selected
            {
                get => Rows.All(r => r.Selected) ? true : Rows.Any(r => r.Selected) ? null : false;
                set { foreach (var r in Rows) r.Selected = value == true; }
            }
            public string Pending => string.Join(" | ", Rows.Select(r => r.Pending).Where(p => p.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase));
            public event PropertyChangedEventHandler? PropertyChanged;
            internal void Notify()
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Pending)));
            }
        }

        private const double ContentThreshold = 0.45;
        private readonly ObservableCollection<Group> _groups;
        private readonly ICollectionView _view;
        private readonly ComboBox _target;
        private readonly TextBox _filter;
        private readonly TextBlock _info;

        /// <summary>Ergebnis: Beleg → neue Belegart (zugewiesene Gruppen plus beim Schließen markierte).</summary>
        public Dictionary<DocumentMetadata, string> Assignments { get; } = new();

        public DocTypeTransferDialog(DocumentMetadata source, IEnumerable<DocumentMetadata> items, IEnumerable<string> docTypes, Func<string?, string?, bool> sameCompany)
        {
            var targetType = source.DocumentType?.Trim() ?? "";
            Title = "Belegart übertragen";
            Width = 1300; Height = 800;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var srcWords = Words(source.FullText);
            var srcShape = Shape(source.SelectedNumber);
            var srcRange = Range(source.SelectedNumber);

            _groups = new ObservableCollection<Group>(items
                .GroupBy(i => (Company: i.CompanyName?.Trim() ?? "", Shape: Shape(i.SelectedNumber), Type: i.DocumentType?.Trim() ?? ""),
                    new KeyComparer())
                .Select(g =>
                {
                    bool company = sameCompany(g.Key.Company, source.CompanyName);
                    var rows = new List<Row>();
                    double best = 0;
                    foreach (var i in g)
                    {
                        var sim = Jaccard(srcWords, Words(i.FullText));
                        best = Math.Max(best, sim);
                        rows.Add(new Row { Item = i, Similarity = $"{sim:P0}" });
                    }
                    var reasons = new List<string>();
                    if (company) reasons.Add("Firma");
                    if (srcShape.Length > 0 && g.Key.Shape == srcShape) reasons.Add("Nummernaufbau");
                    if (srcRange != null && g.Any(i => Range(i.SelectedNumber) == srcRange)) reasons.Add("Nummernkreis " + srcRange + "…");
                    if (best >= ContentThreshold) reasons.Add($"Inhalt {best:P0}");
                    bool structure = reasons.Any(r => r != "Firma");
                    if (company && structure && targetType.Length > 0 && string.Equals(g.Key.Type, targetType, StringComparison.OrdinalIgnoreCase))
                        reasons.Add("bereits " + targetType);
                    var grp = new Group
                    {
                        Company = g.Key.Company.Length == 0 ? "(leer)" : g.Key.Company,
                        Shape = g.Key.Shape.Length == 0 ? "(keine Nummer)" : g.Key.Shape,
                        Current = g.Key.Type.Length == 0 ? "(leer)" : g.Key.Type,
                        Match = string.Join(", ", reasons),
                    };
                    grp.Rows.AddRange(rows);
                    // Firma muss passen, dazu gleicher Nummernaufbau/-kreis oder ähnlicher Inhalt – auch wenn die Gruppe
                    // schon die Belegart der Quelle hat (z. B. für eine Gruppenzuweisung auf eine andere Belegart)
                    if (company && structure) grp.Selected = true;
                    foreach (var r in grp.Rows) { var gg = grp; r.SelectionChanged = () => { gg.Notify(); UpdateInfo(); }; }
                    return grp;
                })
                .OrderByDescending(g => g.Selected != false)
                .ThenByDescending(g => sameCompany(g.Company, source.CompanyName))
                .ThenByDescending(g => g.Count).ThenBy(g => g.Company));

            var root = new DockPanel { Margin = new Thickness(8) };

            var head = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
            DockPanel.SetDock(head, Dock.Top);
            head.Children.Add(new TextBlock
            {
                Text = $"Quelle: {source.FileName} – {source.CompanyName} – {(string.IsNullOrWhiteSpace(source.DocumentType) ? "(leer)" : source.DocumentType)}"
                    + (srcShape.Length > 0 ? $"  (Nummer {source.SelectedNumber}, Aufbau {srcShape})" : "")
                    + "\nGruppiert nach Firma, Nummernaufbau und bisheriger Belegart. Vorausgewählt: gleiche Firma und gleicher Nummernaufbau/-kreis oder ähnlicher Inhalt (auch Gruppen, die die Belegart schon haben – „bereits …“)."
                    + "\nGruppenzuweisung: Belegart wählen, Gruppen/Belege markieren, „Markierten zuweisen“ – beliebig oft mit anderen Belegarten wiederholen.",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4)
            });
            var tp = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            tp.Children.Add(new TextBlock { Text = "Belegart: ", VerticalAlignment = VerticalAlignment.Center, Width = 60 });
            _target = new ComboBox { IsEditable = true, Width = 220, Text = targetType, HorizontalAlignment = HorizontalAlignment.Left };
            foreach (var t in docTypes.Append(targetType).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase)) _target.Items.Add(t);
            _target.Text = targetType;
            _target.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => UpdateInfo()));
            tp.Children.Add(_target);
            var btnAssign = new Button { Content = "Markierten zuweisen", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(6, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            tp.Children.Add(btnAssign);
            _info = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), Foreground = System.Windows.Media.Brushes.Gray };
            tp.Children.Add(_info);
            head.Children.Add(tp);
            var fp = new DockPanel();
            fp.Children.Add(new TextBlock { Text = "Filter: ", VerticalAlignment = VerticalAlignment.Center, Width = 60 });
            _filter = new TextBox();
            fp.Children.Add(_filter);
            head.Children.Add(fp);
            root.Children.Add(head);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0) };
            DockPanel.SetDock(buttons, Dock.Bottom);
            Button Btn(string text, RoutedEventHandler h)
            {
                var b = new Button { Content = text, Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 6, 0) };
                b.Click += h; buttons.Children.Add(b); return b;
            }
            Btn("Sichtbare markieren", (_, _) => { foreach (Group g in _view!) g.Selected = true; });
            Btn("Sichtbare abwählen", (_, _) => { foreach (Group g in _view!) g.Selected = false; });
            Btn("Zuweisungen verwerfen", (_, _) => { foreach (var r in _groups.SelectMany(g => g.Rows)) r.Pending = ""; foreach (var g in _groups) g.Notify(); UpdateInfo(); });
            Btn("Übertragen", (_, _) =>
            {
                AssignChecked();
                foreach (var r in _groups.SelectMany(g => g.Rows).Where(r => r.Pending.Length > 0))
                    Assignments[r.Item] = r.Pending;
                DialogResult = true;
            }).IsDefault = true;
            Btn("Abbrechen", (_, _) => DialogResult = false).IsCancel = true;
            root.Children.Add(buttons);

            btnAssign.Click += (_, _) => AssignChecked();

            var lv = new ListView();
            var gv = new GridView();
            gv.Columns.Add(new GridViewColumn { Header = "", Width = 30, CellTemplate = CheckTemplate(nameof(Group.Selected)) });
            gv.Columns.Add(new GridViewColumn { Header = "Anzahl", Width = 55, DisplayMemberBinding = new Binding(nameof(Group.Count)) });
            gv.Columns.Add(new GridViewColumn { Header = "Firma", Width = 280, DisplayMemberBinding = new Binding(nameof(Group.Company)) });
            gv.Columns.Add(new GridViewColumn { Header = "Nummernaufbau", Width = 140, DisplayMemberBinding = new Binding(nameof(Group.Shape)) });
            gv.Columns.Add(new GridViewColumn { Header = "Bisherige Belegart", Width = 150, DisplayMemberBinding = new Binding(nameof(Group.Current)) });
            gv.Columns.Add(new GridViewColumn { Header = "Zugewiesen", Width = 150, DisplayMemberBinding = new Binding(nameof(Group.Pending)) });
            gv.Columns.Add(new GridViewColumn { Header = "Übereinstimmung (Grund der Vorauswahl)", Width = 360, DisplayMemberBinding = new Binding(nameof(Group.Match)) });
            lv.View = gv;
            lv.ItemsSource = _groups;
            Dateiumbenenner.ListViewCopyHelper.Attach(lv);
            lv.KeyDown += (_, e) =>
            {
                if (e.Key != System.Windows.Input.Key.Space) return;
                foreach (Group g in lv.SelectedItems) g.Selected = g.Selected != true;
                e.Handled = true;
            };

            var body = new Grid();
            body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.3, GridUnitType.Star) });
            Grid.SetRow(lv, 0);
            body.Children.Add(lv);
            var split = new GridSplitter { Height = 5, HorizontalAlignment = HorizontalAlignment.Stretch };
            Grid.SetRow(split, 1);
            body.Children.Add(split);

            var bottom = new Grid();
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            Grid.SetRow(bottom, 2);
            body.Children.Add(bottom);

            var files = new ListView();
            var fgv = new GridView();
            fgv.Columns.Add(new GridViewColumn { Header = "", Width = 30, CellTemplate = CheckTemplate(nameof(Row.Selected)) });
            fgv.Columns.Add(new GridViewColumn { Header = "Dateiname", Width = 240, DisplayMemberBinding = new Binding(nameof(Row.FileName)) });
            fgv.Columns.Add(new GridViewColumn { Header = "Nummer", Width = 110, DisplayMemberBinding = new Binding(nameof(Row.Number)) });
            fgv.Columns.Add(new GridViewColumn { Header = "Inhalt ähnl.", Width = 70, DisplayMemberBinding = new Binding(nameof(Row.Similarity)) });
            fgv.Columns.Add(new GridViewColumn { Header = "Zugewiesen", Width = 110, DisplayMemberBinding = new Binding(nameof(Row.Pending)) });
            files.View = fgv;
            Dateiumbenenner.ListViewCopyHelper.Attach(files);
            files.KeyDown += (_, e) =>
            {
                if (e.Key != System.Windows.Input.Key.Space) return;
                foreach (Row r in files.SelectedItems) r.Selected = !r.Selected;
                e.Handled = true;
            };
            bottom.Children.Add(files);
            var vsplit = new GridSplitter { Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch };
            Grid.SetColumn(vsplit, 1);
            bottom.Children.Add(vsplit);
            var previewHost = new DockPanel();
            Grid.SetColumn(previewHost, 2);
            bottom.Children.Add(previewHost);
            var chkText = new CheckBox { Content = "Textmodus", Margin = new Thickness(0, 0, 0, 4) };
            DockPanel.SetDock(chkText, Dock.Top);
            previewHost.Children.Add(chkText);
            var preview = new Grid();
            previewHost.Children.Add(preview);

            Microsoft.Web.WebView2.Wpf.WebView2? web = null;
            var text = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new System.Windows.Media.FontFamily("Consolas") };
            async System.Threading.Tasks.Task ShowPreview()
            {
                if (files.SelectedItem is not Row { Item: var it }) { preview.Children.Clear(); return; }
                if (chkText.IsChecked != true && System.IO.Path.GetExtension(it.FilePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(it.FilePath))
                {
                    try
                    {
                        web ??= new Microsoft.Web.WebView2.Wpf.WebView2();
                        if (!preview.Children.Contains(web)) { preview.Children.Clear(); preview.Children.Add(web); }
                        await web.EnsureCoreWebView2Async();
                        web.Source = new Uri(it.FilePath);
                        return;
                    }
                    catch { /* WebView2 nicht verfügbar → Text */ }
                }
                if (!preview.Children.Contains(text)) { preview.Children.Clear(); preview.Children.Add(text); }
                text.Text = it.FullText ?? "";
            }
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
                var key = string.Join("\u0001", new[] { g.Company, g.Current, g.Shape, g.Match, g.Pending }
                    .Concat(g.Rows.SelectMany(r => new[] { r.Item.FileName, r.Item.SelectedNumber, r.Item.SelectedDate })));
                return words.All(w => key.Contains(w, StringComparison.OrdinalIgnoreCase))
                    || g.Rows.Any(r => words.All(w => (r.Item.FullText ?? "").Contains(w, StringComparison.OrdinalIgnoreCase)));
            };
            var filterTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            filterTimer.Tick += (_, _) => { filterTimer.Stop(); _view.Refresh(); };
            _filter.TextChanged += (_, _) => { filterTimer.Stop(); filterTimer.Start(); };
            UpdateInfo();
        }

        /// <summary>Markierte Belege erhalten die gewählte Belegart (vorgemerkt) und werden abgewählt.</summary>
        private void AssignChecked()
        {
            var type = _target.Text?.Trim() ?? "";
            if (type.Length == 0) return;
            foreach (var r in _groups.SelectMany(g => g.Rows).Where(r => r.Selected).ToList())
            {
                r.Pending = string.Equals(r.Item.DocumentType?.Trim(), type, StringComparison.OrdinalIgnoreCase) ? "" : type;
                r.Selected = false;
            }
            foreach (var g in _groups) g.Notify();
            UpdateInfo();
        }

        private void UpdateInfo()
        {
            if (_info == null) return;
            var rows = _groups.SelectMany(g => g.Rows).ToList();
            var type = _target?.Text?.Trim() ?? "";
            int already = type.Length == 0 ? 0 : rows.Count(r => r.Selected && string.Equals(r.Item.DocumentType?.Trim(), type, StringComparison.OrdinalIgnoreCase));
            _info.Text = $"{rows.Count(r => r.Selected)} markiert" + (already > 0 ? $" (davon {already} schon „{type}“)" : "")
                + $", {rows.Count(r => r.Pending.Length > 0)} zugewiesen";
        }

        private static DataTemplate CheckTemplate(string path)
        {
            var chk = new FrameworkElementFactory(typeof(CheckBox));
            chk.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,
                new Binding(path) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            return new DataTemplate { VisualTree = chk };
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

        private sealed class KeyComparer : IEqualityComparer<(string Company, string Shape, string Type)>
        {
            public bool Equals((string Company, string Shape, string Type) x, (string Company, string Shape, string Type) y)
                => string.Equals(x.Company, y.Company, StringComparison.OrdinalIgnoreCase) && x.Shape == y.Shape
                    && string.Equals(x.Type, y.Type, StringComparison.OrdinalIgnoreCase);
            public int GetHashCode((string Company, string Shape, string Type) k)
                => HashCode.Combine(k.Company.ToUpperInvariant(), k.Shape, k.Type.ToUpperInvariant());
        }
    }
}
