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
    /// <summary>„Namen übertragen“ im Hauptprogramm: Dateien gruppiert nach aktuellem Firmennamen, ankreuzbar.</summary>
    public class CompanyTransferDialog : Window
    {
        public class Group : INotifyPropertyChanged
        {
            private bool _selected;
            public bool Selected { get => _selected; set { _selected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected))); } }
            public string Name { get; init; } = "";
            public string Match { get; init; } = "";
            public string Candidates { get; init; } = "";
            public string Files { get; init; } = "";
            public int Count => Items.Count;
            public List<DocumentMetadata> Items { get; } = new();
            public event PropertyChangedEventHandler? PropertyChanged;
        }

        private readonly ObservableCollection<Group> _groups;
        private readonly ICollectionView _view;
        private readonly System.Windows.Controls.TextBox _filter;

        public IEnumerable<DocumentMetadata> SelectedItems => _groups.Where(g => g.Selected).SelectMany(g => g.Items);

        public CompanyTransferDialog(IEnumerable<DocumentMetadata> items, string target, Func<string, bool> isSimilar)
        {
            Title = "Firmennamen übertragen";
            Width = 1200; Height = 560;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            _groups = new ObservableCollection<Group>(items
                .GroupBy(i => (i.CompanyName ?? "").Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    var matches = new List<string>();
                    foreach (var i in g)
                    {
                        // Regel: die gewählte Firma muss in den erkannten Firmennamen oder im Belegtext stehen
                        foreach (var k in i.CompanyCandidates.Where(k => !string.IsNullOrWhiteSpace(k) && isSimilar(k) && CompanyMatcher.AppearsInText(k, i.FullText)))
                            matches.Add("Liste: " + k.Trim());
                        if (CompanyMatcher.AppearsInText(target, i.FullText)) matches.Add("Text: " + target);
                    }
                    var grp = new Group
                    {
                        Name = g.Key.Length == 0 ? "(leer)" : g.Key,
                        Match = string.Join(" | ", matches.Distinct(StringComparer.OrdinalIgnoreCase)),
                        Candidates = string.Join(" | ", g.SelectMany(i => i.CompanyCandidates)
                            .Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)),
                        Files = string.Join(" | ", g.Select(i => i.FileName)),
                    };
                    grp.Items.AddRange(g);
                    grp.Selected = g.All(i => CompanyMatcher.AppearsInText(target, i.FullText));
                    return grp;
                })
                .OrderByDescending(g => g.Selected).ThenByDescending(g => g.Count).ThenBy(g => g.Name));

            var root = new DockPanel { Margin = new Thickness(8) };
            var head = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
            DockPanel.SetDock(head, Dock.Top);
            head.Children.Add(new TextBlock
            {
                Text = $"Übertragen: '{target}'\nÄhnliche Einträge sind vorausgewählt. Der Firmenname wird auf die markierten Dateien übernommen.",
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
            var chk = new FrameworkElementFactory(typeof(System.Windows.Controls.CheckBox));
            chk.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new System.Windows.Data.Binding(nameof(Group.Selected)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            gv.Columns.Add(new GridViewColumn { Header = "", Width = 30, CellTemplate = new DataTemplate { VisualTree = chk } });
            gv.Columns.Add(new GridViewColumn { Header = "Anzahl", Width = 55, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(Group.Count)) });
            gv.Columns.Add(new GridViewColumn { Header = "Aktuell gewählt", Width = 220, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(Group.Name)) });
            gv.Columns.Add(new GridViewColumn { Header = "Treffer (Grund der Vorauswahl)", Width = 240, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(Group.Match)) });
            gv.Columns.Add(new GridViewColumn { Header = "Erkannte Firmennamen (Liste)", Width = 340, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(Group.Candidates)) });
            gv.Columns.Add(new GridViewColumn { Header = "Dateien", Width = 260, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(Group.Files)) });
            lv.View = gv;
            lv.ItemsSource = _groups;
            lv.KeyDown += (_, e) =>
            {
                if (e.Key != System.Windows.Input.Key.Space) return;
                foreach (Group g in lv.SelectedItems) g.Selected = !g.Selected;
                e.Handled = true;
            };
            root.Children.Add(lv);
            Content = root;

            _view = CollectionViewSource.GetDefaultView(_groups);
            _view.Filter = o => string.IsNullOrWhiteSpace(_filter.Text) || o is Group g
                && (g.Name.Contains(_filter.Text.Trim(), StringComparison.OrdinalIgnoreCase)
                    || g.Candidates.Contains(_filter.Text.Trim(), StringComparison.OrdinalIgnoreCase)
                    || g.Files.Contains(_filter.Text.Trim(), StringComparison.OrdinalIgnoreCase));
            _filter.TextChanged += (_, _) => _view.Refresh();
        }
    }
}
