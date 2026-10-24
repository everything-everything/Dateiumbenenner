using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Dateiumbenenner
{
    /// <summary>„Namen übertragen“ im Hauptprogramm: Dateien gruppiert nach aktuellem Firmennamen, je Gruppe oder Datei ankreuzbar.
    /// Unpassende Dateien lassen sich abwählen, aus der Gruppe lösen oder die Gruppe ganz auflösen.</summary>
    public class CompanyTransferDialog : Window
    {
        public class Group : INotifyPropertyChanged
        {
            public string Name { get; init; } = "";
            public List<Row> Rows { get; } = new();
            public int Count => Rows.Count;
            public bool? Selected
            {
                get => Rows.Count > 0 && Rows.All(r => r.Selected) ? true : Rows.Any(r => r.Selected) ? null : false;
                set { foreach (var r in Rows) r.Selected = value == true; }
            }
            public string Match => string.Join(" | ", Rows.SelectMany(r => r.Match.Split(" | ", StringSplitOptions.RemoveEmptyEntries)).Distinct(StringComparer.OrdinalIgnoreCase));
            public string Candidates => string.Join(" | ", Rows.SelectMany(r => r.Item.CompanyCandidates)
                .Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim()).Distinct(StringComparer.OrdinalIgnoreCase));
            public string Files => string.Join(" | ", Rows.Select(r => r.FileName));
            public IEnumerable<DocumentMetadata> Items => Rows.Select(r => r.Item);
            public event PropertyChangedEventHandler? PropertyChanged;
            internal void Notify()
            {
                foreach (var n in new[] { nameof(Selected), nameof(Count), nameof(Match), nameof(Candidates), nameof(Files) })
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
            }
        }

        /// <summary>Datei in der unteren Liste; der Firmenname ist hier bearbeitbar und wird erst mit „Übertragen“ gespeichert.</summary>
        public class Row : INotifyPropertyChanged
        {
            private bool _selected;
            public DocumentMetadata Item { get; init; } = null!;
            public bool Selected { get => _selected; set { if (_selected == value) return; _selected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected))); SelectionChanged?.Invoke(); } }
            public string FileName => Item.FileName;
            public string? Number => Item.SelectedNumber;
            public string? DocumentType => Item.DocumentType;
            public string Company { get; set; } = "";
            /// <summary>Grund der Vorauswahl für diese Datei.</summary>
            public string Match { get; init; } = "";
            public bool Changed => !string.Equals(Company.Trim(), (Item.CompanyName ?? "").Trim(), StringComparison.Ordinal);
            internal Action? SelectionChanged;
            public event PropertyChangedEventHandler? PropertyChanged;
        }

        private readonly ObservableCollection<Group> _groups;
        private readonly ICollectionView _view;
        private readonly System.Windows.Controls.TextBox _filter;
        private readonly TextBlock _info = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };

        public IEnumerable<DocumentMetadata> SelectedItems => _groups.SelectMany(g => g.Rows).Where(r => r.Selected).Select(r => r.Item);
        /// <summary>In der Dateiliste von Hand geänderte Firmennamen (nicht angehakter Dateien).</summary>
        public IEnumerable<(DocumentMetadata Item, string Name)> EditedNames => _groups.SelectMany(g => g.Rows)
            .Where(r => !r.Selected && r.Changed && !string.IsNullOrWhiteSpace(r.Company)).Select(r => (r.Item, r.Company.Trim()));

        public CompanyTransferDialog(IEnumerable<DocumentMetadata> items, string target, Func<string, bool> isSimilar)
        {
            Title = "Firmennamen übertragen";
            Width = 1200; Height = 800;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            Row MakeRow(DocumentMetadata i)
            {
                var matches = new List<string>();
                // Regel: die gewählte Firma muss in den erkannten Firmennamen oder im Belegtext stehen –
                // und nicht nur eine ähnlich heißende andere Firma
                foreach (var k in i.CompanyCandidates.Where(k => !string.IsNullOrWhiteSpace(k) && isSimilar(k) && CompanyMatcher.AppearsInTextExclusive(k, i.FullText)))
                    matches.Add("Liste: " + k.Trim());
                bool inText = CompanyMatcher.AppearsInTextExclusive(target, i.FullText);
                if (inText) matches.Add("Text: " + target);
                return new Row { Item = i, Company = i.CompanyName ?? "", Match = string.Join(" | ", matches.Distinct(StringComparer.OrdinalIgnoreCase)), Selected = inText };
            }

            _groups = new ObservableCollection<Group>(items
                .GroupBy(i => (i.CompanyName ?? "").Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => NewGroup(g.Key.Length == 0 ? "(leer)" : g.Key, g.Select(MakeRow)))
                .OrderByDescending(g => g.Selected == true).ThenByDescending(g => g.Selected == null).ThenByDescending(g => g.Count).ThenBy(g => g.Name));

            var root = new DockPanel { Margin = new Thickness(8) };
            var head = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
            DockPanel.SetDock(head, Dock.Top);
            head.Children.Add(new TextBlock
            {
                Text = $"Übertragen: '{target}'\nDateien, in deren Text die Firma steht, sind vorausgewählt. Der Firmenname wird auf die angehakten Dateien übernommen."
                    + "\nEinzelne Dateien unten abhaken, mit „Aus Gruppe lösen“ herausziehen oder die Gruppe auflösen, wenn sie so nicht passt.",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4)
            });
            var fp = new DockPanel();
            fp.Children.Add(new TextBlock { Text = "Filter: ", VerticalAlignment = VerticalAlignment.Center });
            _filter = new System.Windows.Controls.TextBox();
            fp.Children.Add(_filter);
            head.Children.Add(fp);
            root.Children.Add(head);

            var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0) };
            DockPanel.SetDock(buttons, Dock.Bottom);
            buttons.Children.Add(_info);
            System.Windows.Controls.Button Btn(string text, RoutedEventHandler h)
            {
                var b = new System.Windows.Controls.Button { Content = text, Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 6, 0) };
                b.Click += h; buttons.Children.Add(b); return b;
            }
            Btn("Sichtbare markieren", (_, _) => { foreach (Group g in _view!) g.Selected = true; });
            Btn("Sichtbare abwählen", (_, _) => { foreach (Group g in _view!) g.Selected = false; });
            Btn("Übertragen", (_, _) => DialogResult = true).IsDefault = true;
            Btn("Abbrechen", (_, _) => DialogResult = false).IsCancel = true;
            root.Children.Add(buttons);

            var lv = new System.Windows.Controls.ListView();
            var gv = new GridView();
            gv.Columns.Add(new GridViewColumn { Header = "", Width = 30, CellTemplate = ListKeyboard.CheckTemplate(nameof(Group.Selected)) });
            gv.Columns.Add(new GridViewColumn { Header = "Anzahl", Width = 55, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(Group.Count)) });
            gv.Columns.Add(new GridViewColumn { Header = "Aktuell gewählt", Width = 220, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(Group.Name)) });
            gv.Columns.Add(new GridViewColumn { Header = "Treffer (Grund der Vorauswahl)", Width = 240, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(Group.Match)) });
            gv.Columns.Add(new GridViewColumn { Header = "Erkannte Firmennamen (Liste)", Width = 340, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(Group.Candidates)) });
            gv.Columns.Add(new GridViewColumn { Header = "Dateien", Width = 260, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(Group.Files)) });
            lv.View = gv;
            lv.ItemsSource = _groups;
            ListViewCopyHelper.Attach(lv);
            ListKeyboard.AttachSpaceToggle<Group>(lv, g => g.Selected == true, (g, on) => g.Selected = on);

            // Unten: Dateien der markierten Zeile + Vorschau des gewählten Dokuments
            var body = new Grid();
            body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.5, GridUnitType.Star) });
            Grid.SetRow(lv, 0);
            body.Children.Add(lv);
            var split = new GridSplitter { Height = 5, HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch };
            Grid.SetRow(split, 1);
            body.Children.Add(split);

            var bottom = new Grid();
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            Grid.SetRow(bottom, 2);
            body.Children.Add(bottom);

            var files = new System.Windows.Controls.ListView();
            var fgv = new GridView();
            fgv.Columns.Add(new GridViewColumn { Header = "", Width = 30, CellTemplate = ListKeyboard.CheckTemplate(nameof(Row.Selected)) });
            fgv.Columns.Add(new GridViewColumn { Header = "Dateiname", Width = 240, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(Row.FileName)) });
            var edit = new FrameworkElementFactory(typeof(System.Windows.Controls.TextBox));
            edit.SetBinding(System.Windows.Controls.TextBox.TextProperty, new System.Windows.Data.Binding(nameof(Row.Company)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            edit.SetValue(MinWidthProperty, 160.0);
            fgv.Columns.Add(new GridViewColumn { Header = "Aktuell (bearbeitbar)", Width = 200, CellTemplate = new DataTemplate { VisualTree = edit } });
            fgv.Columns.Add(new GridViewColumn { Header = "Belegart", Width = 110, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(Row.DocumentType)) });
            fgv.Columns.Add(new GridViewColumn { Header = "Nummer", Width = 110, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(Row.Number)) });
            fgv.Columns.Add(new GridViewColumn { Header = "Treffer", Width = 200, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(Row.Match)) });
            files.View = fgv;
            files.ToolTip = "Häkchen = Datei erhält den übertragenen Namen. Von Hand geänderte Namen nicht angehakter Dateien werden mit „Übertragen“ gespeichert.";
            ListViewCopyHelper.Attach(files);
            ListKeyboard.AttachSpaceToggle<Row>(files, r => r.Selected, (r, on) => r.Selected = on);
            bottom.Children.Add(files);
            GroupTools.Attach(lv, files, _groups, g => g.Rows,
                (g, rows, single) => NewGroup(single ? $"{g.Name}  ({rows[0].FileName})" : $"{g.Name}  (herausgelöst)", rows),
                g => g.Notify(), UpdateInfo);
            var vsplit = new GridSplitter { Width = 5, HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch };
            Grid.SetColumn(vsplit, 1);
            bottom.Children.Add(vsplit);
            var previewHost = new DockPanel();
            Grid.SetColumn(previewHost, 2);
            bottom.Children.Add(previewHost);
            var tools = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            DockPanel.SetDock(tools, Dock.Top);
            var chkText = new System.Windows.Controls.CheckBox { Content = "Textmodus", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            var btnFull = new System.Windows.Controls.Button { Content = "Vollbildvorschau", Padding = new Thickness(8, 1, 8, 1) };
            tools.Children.Add(chkText);
            tools.Children.Add(btnFull);
            previewHost.Children.Add(tools);
            var preview = new Grid();
            previewHost.Children.Add(preview);

            Microsoft.Web.WebView2.Wpf.WebView2? web = null;
            var text = new System.Windows.Controls.TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new System.Windows.Media.FontFamily("Consolas"), IsInactiveSelectionHighlightEnabled = true };
            TextSearchBar? search = null;
            bool IsPdf(DocumentMetadata it) => chkText.IsChecked != true
                && System.IO.Path.GetExtension(it.FilePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(it.FilePath);

            lv.SelectionChanged += (_, _) =>
            {
                files.ItemsSource = (lv.SelectedItem as Group)?.Rows;
                if (files.Items.Count > 0) files.SelectedIndex = 0;
            };
            async System.Threading.Tasks.Task ShowPreview()
            {
                if (files.SelectedItem is not Row { Item: var it }) { preview.Children.Clear(); search?.DocumentChanged("", null, false); return; }
                if (IsPdf(it))
                {
                    try
                    {
                        web ??= new Microsoft.Web.WebView2.Wpf.WebView2();
                        if (!preview.Children.Contains(web)) { preview.Children.Clear(); preview.Children.Add(web); }
                        search?.DocumentChanged(it.FullText, null, false);
                        await web.EnsureCoreWebView2Async();
                        search?.SetWeb(web);
                        web.Source = new Uri(it.FilePath);
                        return;
                    }
                    catch { /* WebView2 nicht verfügbar → Text */ }
                }
                if (!preview.Children.Contains(text)) { preview.Children.Clear(); preview.Children.Add(text); }
                text.Text = it.FullText ?? "";
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
            ListKeyboard.LinkLists(lv, files);
            Loaded += (_, _) => ListKeyboard.FocusSelected(lv);
            files.SelectionChanged += async (_, _) => await ShowPreview();
            chkText.Click += async (_, _) => await ShowPreview();
            btnFull.Click += async (_, _) =>
            {
                if (files.SelectedItem is not Row { Item: var it }) return;
                var w = new Window { Title = it.FileName, Owner = this, WindowState = WindowState.Maximized };
                if (IsPdf(it))
                {
                    try
                    {
                        var fw = new Microsoft.Web.WebView2.Wpf.WebView2();
                        w.Content = fw;
                        w.Show();
                        await fw.EnsureCoreWebView2Async();
                        fw.Source = new Uri(it.FilePath);
                        return;
                    }
                    catch { }
                }
                w.Content = new System.Windows.Controls.TextBox { Text = it.FullText ?? "", IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new System.Windows.Media.FontFamily("Consolas") };
                if (!w.IsVisible) w.Show();
            };

            root.Children.Add(body);
            Content = root;

            _view = CollectionViewSource.GetDefaultView(_groups);
            _view.Filter = o =>
            {
                var words = _filter.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (words.Length == 0) return true;
                if (o is not Group g) return false;
                var key = string.Join("\u0001", new[] { g.Name, g.Match, g.Candidates, g.Files }
                    .Concat(g.Items.SelectMany(i => new[] { i.CompanyName, i.DocumentType, i.SelectedNumber, i.SelectedDate })));
                return words.All(w => key.Contains(w, StringComparison.OrdinalIgnoreCase))
                    || g.Items.Any(i => i.FullText != null && words.All(w => i.FullText.Contains(w, StringComparison.OrdinalIgnoreCase)));
            };
            var filterTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            filterTimer.Tick += (_, _) => { filterTimer.Stop(); _view.Refresh(); };
            _filter.TextChanged += (_, _) => { filterTimer.Stop(); filterTimer.Start(); };
            UpdateInfo();
        }

        private Group NewGroup(string name, IEnumerable<Row> rows)
        {
            var g = new Group { Name = name };
            g.Rows.AddRange(rows);
            foreach (var r in g.Rows) r.SelectionChanged = () => { g.Notify(); UpdateInfo(); };
            return g;
        }

        private void UpdateInfo()
        {
            if (_groups == null) return;
            _info.Text = $"{_groups.Sum(g => g.Rows.Count(r => r.Selected))} von {_groups.Sum(g => g.Count)} Dateien angehakt";
        }
    }
}
