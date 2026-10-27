using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Dateiumbenenner.Engine;
using WpfBinding = System.Windows.Data.Binding;
using WpfButton = System.Windows.Controls.Button;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfDataGrid = System.Windows.Controls.DataGrid;
using GroupBox = System.Windows.Controls.GroupBox;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfListView = System.Windows.Controls.ListView;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfRadioButton = System.Windows.Controls.RadioButton;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace Dateiumbenenner
{
    /// <summary>Beleg mit seiner Textdatei für die TXT-Nummernkorrektur.</summary>
    public sealed record NumberFixDoc(string Name, string TxtPath);

    /// <summary>
    /// TXT-Nummernkorrektur: wiederkehrende OCR-Fehler in Nummern (z. B. 4700012345F831 statt 4700D12345F831) per Regel
    /// in allen TXT-Dateien der Liste finden, prüfen und korrigieren. Originale werden gesichert (rückgängig möglich).
    /// </summary>
    public sealed class NumberFixWindow : Window
    {
        public sealed class RuleRow : INotifyPropertyChanged
        {
            private bool _active = true;
            private string _length = "", _wrong = "", _right = "", _note = "";
            private FixPosition _position = FixPosition.Anfang;
            public bool Active { get => _active; set => Set(ref _active, value); }
            public string LengthText { get => _length; set => Set(ref _length, value); }
            public string Wrong { get => _wrong; set => Set(ref _wrong, value); }
            public string Right { get => _right; set => Set(ref _right, value); }
            public FixPosition Position { get => _position; set => Set(ref _position, value); }
            public string Note { get => _note; set => Set(ref _note, value); }

            public NumberFixRule ToRule() => new()
            {
                Active = Active, Length = int.TryParse(LengthText.Trim(), out var n) && n > 0 ? n : null,
                Wrong = Wrong.Trim(), Right = Right.Trim(), Position = Position, Note = string.IsNullOrWhiteSpace(Note) ? null : Note.Trim()
            };

            public static RuleRow From(NumberFixRule r) => new()
            {
                Active = r.Active, LengthText = r.Length?.ToString() ?? "", Wrong = r.Wrong, Right = r.Right, Position = r.Position, Note = r.Note ?? ""
            };

            public event PropertyChangedEventHandler? PropertyChanged;
            private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
            {
                if (EqualityComparer<T>.Default.Equals(field, value)) return;
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }
        }

        public sealed class HitRow : INotifyPropertyChanged
        {
            private bool _apply = true;
            public required NumberFixHit Hit { get; init; }
            public required string Document { get; init; }
            public bool Apply { get => _apply; set { if (_apply == value) return; _apply = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Apply))); } }
            public int Line => Hit.Line;
            public string Old => Hit.Old;
            public string New => Hit.New;
            public string LineText => Hit.LineText;
            public string RuleText => Describe(Hit.Rule);
            public event PropertyChangedEventHandler? PropertyChanged;
        }

        private static string Describe(NumberFixRule r) =>
            $"{(r.Length is int n ? $"Länge {n}, " : "")}{r.Wrong} → {r.Right} ({r.Position})";

        private readonly IReadOnlyList<NumberFixDoc> _all;
        private readonly IReadOnlyList<NumberFixDoc> _selected;
        private readonly ObservableCollection<RuleRow> _rules = new();
        private readonly ObservableCollection<HitRow> _hits = new();
        private readonly WpfDataGrid _ruleGrid;
        private readonly WpfListView _hitList;
        private readonly TextBlock _status;
        private readonly WpfRadioButton _rbSelected;
        private readonly WpfButton _btnUndo, _btnApply;
        private readonly DispatcherTimer _refreshTimer;
        private int _scanRun;

        /// <summary>Geänderte (oder wiederhergestellte) TXT-Dateien dieser Sitzung.</summary>
        public HashSet<string> ChangedFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

        public NumberFixWindow(IReadOnlyList<NumberFixDoc> all, IReadOnlyList<NumberFixDoc>? selected = null, string? example = null)
        {
            _all = all;
            _selected = selected ?? Array.Empty<NumberFixDoc>();
            Title = "TXT-Nummernkorrektur";
            Width = 1100; Height = 760; MinWidth = 800; MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            foreach (var r in NumberFixStore.Rules) _rules.Add(RuleRow.From(r));

            var root = new DockPanel { Margin = new Thickness(10) };
            Content = root;

            var intro = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8),
                Text = "Korrigiert wiederkehrende OCR-Fehler in Nummern direkt in den TXT-Dateien der Liste, z. B. Länge 14, „47000“ am Anfang → „4700D“ " +
                       "(4700012345F831 → 4700D12345F831). Als Nummer gilt jede zusammenhängende Folge aus Buchstaben und Ziffern. " +
                       "Im falschen Muster: $ = Ziffer, ? = Buchstabe, * = beliebiges Zeichen; in der Ersetzung behalten $, ? und * das vorhandene Zeichen. " +
                       "Länge leer = beliebig. Vor dem Schreiben werden die Originale gesichert."
            };
            DockPanel.SetDock(intro, Dock.Top);
            root.Children.Add(intro);

            // Aus Beispiel ableiten
            var exBox = new GroupBox { Header = "Regel aus Beispiel ableiten", Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 0, 0, 6) };
            var exPanel = new StackPanel { Orientation = WpfOrientation.Horizontal };
            var txtWrong = new WpfTextBox { Width = 200, Text = example ?? "", FontFamily = new System.Windows.Media.FontFamily("Consolas"), VerticalContentAlignment = VerticalAlignment.Center };
            var txtRight = new WpfTextBox { Width = 200, Text = example ?? "", FontFamily = new System.Windows.Media.FontFamily("Consolas"), VerticalContentAlignment = VerticalAlignment.Center };
            var btnDerive = new WpfButton { Content = "Regel ableiten ⬇", Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(8, 0, 0, 0) };
            exPanel.Children.Add(new TextBlock { Text = "Falsch erkannt:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) });
            exPanel.Children.Add(txtWrong);
            exPanel.Children.Add(new TextBlock { Text = "Richtig:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 5, 0) });
            exPanel.Children.Add(txtRight);
            exPanel.Children.Add(btnDerive);
            exPanel.Children.Add(new TextBlock { Text = "z. B. 4700012345F831 → 4700D12345F831", Foreground = System.Windows.Media.Brushes.Gray, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) });
            exBox.Content = exPanel;
            DockPanel.SetDock(exBox, Dock.Top);
            root.Children.Add(exBox);

            // Regeln
            var ruleBox = new GroupBox { Header = "Regeln (werden im Profil gespeichert)", Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 0, 0, 6) };
            var rulePanel = new DockPanel();
            var ruleButtons = new StackPanel { Orientation = WpfOrientation.Vertical, Margin = new Thickness(6, 0, 0, 0) };
            var btnAdd = new WpfButton { Content = "＋ Neue Regel", Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(0, 0, 0, 4) };
            var btnRemove = new WpfButton { Content = "Entfernen", Padding = new Thickness(8, 1, 8, 1) };
            ruleButtons.Children.Add(btnAdd);
            ruleButtons.Children.Add(btnRemove);
            DockPanel.SetDock(ruleButtons, Dock.Right);
            rulePanel.Children.Add(ruleButtons);
            _ruleGrid = new WpfDataGrid
            {
                ItemsSource = _rules, AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, Height = 140,
                HeadersVisibility = DataGridHeadersVisibility.Column, SelectionMode = DataGridSelectionMode.Extended
            };
            WpfBinding B(string path) => new(path) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged, Mode = BindingMode.TwoWay };
            _ruleGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "Aktiv", Binding = B(nameof(RuleRow.Active)), Width = 45 });
            _ruleGrid.Columns.Add(new DataGridTextColumn { Header = "Länge", Binding = B(nameof(RuleRow.LengthText)), Width = 55 });
            _ruleGrid.Columns.Add(new DataGridTextColumn { Header = "Falsches Muster", Binding = B(nameof(RuleRow.Wrong)), Width = 150 });
            _ruleGrid.Columns.Add(new DataGridTextColumn { Header = "Ersetzen durch", Binding = B(nameof(RuleRow.Right)), Width = 150 });
            _ruleGrid.Columns.Add(new DataGridComboBoxColumn { Header = "Position", SelectedItemBinding = B(nameof(RuleRow.Position)), ItemsSource = Enum.GetValues(typeof(FixPosition)), Width = 90 });
            _ruleGrid.Columns.Add(new DataGridTextColumn { Header = "Notiz", Binding = B(nameof(RuleRow.Note)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            rulePanel.Children.Add(_ruleGrid);
            ruleBox.Content = rulePanel;
            DockPanel.SetDock(ruleBox, Dock.Top);
            root.Children.Add(ruleBox);

            // Bereich
            var scope = new StackPanel { Orientation = WpfOrientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            int allCount = _all.Select(d => d.TxtPath).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            int selCount = _selected.Select(d => d.TxtPath).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            var rbAll = new WpfRadioButton { Content = $"Alle Belege der Liste ({allCount} TXT)", IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
            _rbSelected = new WpfRadioButton { Content = $"Nur markierte ({selCount} TXT)", IsEnabled = selCount > 0, Margin = new Thickness(15, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var btnScan = new WpfButton { Content = "Treffer suchen", Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(20, 0, 0, 0) };
            var btnAllOn = new WpfButton { Content = "Alle Treffer an", Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(20, 0, 0, 0) };
            var btnAllOff = new WpfButton { Content = "Keine", Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(5, 0, 0, 0) };
            scope.Children.Add(rbAll);
            scope.Children.Add(_rbSelected);
            scope.Children.Add(btnScan);
            scope.Children.Add(btnAllOn);
            scope.Children.Add(btnAllOff);
            DockPanel.SetDock(scope, Dock.Top);
            root.Children.Add(scope);

            // Schaltflächen unten
            var bottom = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            _status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            var buttons = new StackPanel { Orientation = WpfOrientation.Horizontal, HorizontalAlignment = WpfHorizontalAlignment.Right };
            _btnUndo = new WpfButton { Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 8, 0) };
            _btnApply = new WpfButton { Content = "In TXT-Dateien schreiben", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 8, 0), FontWeight = FontWeights.SemiBold };
            var btnClose = new WpfButton { Content = "Schließen", Width = 90, IsCancel = true };
            buttons.Children.Add(_btnUndo);
            buttons.Children.Add(_btnApply);
            buttons.Children.Add(btnClose);
            DockPanel.SetDock(buttons, Dock.Right);
            bottom.Children.Add(buttons);
            bottom.Children.Add(_status);
            DockPanel.SetDock(bottom, Dock.Bottom);
            root.Children.Add(bottom);

            // Trefferliste
            _hitList = new WpfListView { ItemsSource = _hits, SelectionMode = System.Windows.Controls.SelectionMode.Extended };
            var gv = new GridView();
            var check = new FrameworkElementFactory(typeof(WpfCheckBox));
            check.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new WpfBinding(nameof(HitRow.Apply)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            gv.Columns.Add(new GridViewColumn { Header = "✓", Width = 34, CellTemplate = new DataTemplate { VisualTree = check } });
            gv.Columns.Add(new GridViewColumn { Header = "Beleg", Width = 220, DisplayMemberBinding = new WpfBinding(nameof(HitRow.Document)) });
            gv.Columns.Add(new GridViewColumn { Header = "Zeile", Width = 50, DisplayMemberBinding = new WpfBinding(nameof(HitRow.Line)) });
            gv.Columns.Add(new GridViewColumn { Header = "Gefunden", Width = 150, DisplayMemberBinding = new WpfBinding(nameof(HitRow.Old)) });
            gv.Columns.Add(new GridViewColumn { Header = "Wird zu", Width = 150, DisplayMemberBinding = new WpfBinding(nameof(HitRow.New)) });
            gv.Columns.Add(new GridViewColumn { Header = "Regel", Width = 190, DisplayMemberBinding = new WpfBinding(nameof(HitRow.RuleText)) });
            gv.Columns.Add(new GridViewColumn { Header = "Textzeile", Width = 380, DisplayMemberBinding = new WpfBinding(nameof(HitRow.LineText)) });
            _hitList.View = gv;
            _hitList.FontFamily = new System.Windows.Media.FontFamily("Consolas");
            _hitList.KeyDown += (_, e) =>
            {
                if (e.Key != System.Windows.Input.Key.Space) return;
                var sel = _hitList.SelectedItems.OfType<HitRow>().ToList();
                if (sel.Count == 0) return;
                bool on = !sel.All(h => h.Apply);
                foreach (var h in sel) h.Apply = on;
                UpdateStatus();
                e.Handled = true;
            };
            var hitBox = new GroupBox { Header = "Treffer (Leertaste = an/aus für markierte Zeilen)", Padding = new Thickness(4), Content = _hitList };
            root.Children.Add(hitBox);

            // Ereignisse
            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _refreshTimer.Tick += async (_, _) => { _refreshTimer.Stop(); await ScanAsync(); };
            void Schedule() { _refreshTimer.Stop(); _refreshTimer.Start(); }
            void Hook(RuleRow r) => r.PropertyChanged += (_, _) => Schedule();
            foreach (var r in _rules) Hook(r);
            _rules.CollectionChanged += (_, e) => { if (e.NewItems != null) foreach (RuleRow r in e.NewItems) Hook(r); Schedule(); };

            btnDerive.Click += (_, _) =>
            {
                var rule = NumberFixStore.Derive(txtWrong.Text, txtRight.Text);
                if (rule == null)
                {
                    System.Windows.MessageBox.Show(this, "Bitte die falsch erkannte und die richtige Nummer eingeben (sie müssen sich unterscheiden).", Title);
                    return;
                }
                var row = RuleRow.From(rule);
                _rules.Add(row);
                _ruleGrid.SelectedItem = row;
                _ruleGrid.ScrollIntoView(row);
            };
            btnAdd.Click += (_, _) =>
            {
                var row = new RuleRow();
                _rules.Add(row);
                _ruleGrid.SelectedItem = row;
                _ruleGrid.ScrollIntoView(row);
            };
            btnRemove.Click += (_, _) =>
            {
                foreach (var r in _ruleGrid.SelectedItems.OfType<RuleRow>().ToList()) _rules.Remove(r);
            };
            rbAll.Checked += async (_, _) => await ScanAsync();
            _rbSelected.Checked += async (_, _) => await ScanAsync();
            btnScan.Click += async (_, _) => await ScanAsync();
            btnAllOn.Click += (_, _) => { foreach (var h in _hits) h.Apply = true; UpdateStatus(); };
            btnAllOff.Click += (_, _) => { foreach (var h in _hits) h.Apply = false; UpdateStatus(); };
            _btnApply.Click += async (_, _) => await ApplyAsync();
            _btnUndo.Click += async (_, _) => await UndoAsync();
            Closing += (_, _) => SaveRules();
            Loaded += async (_, _) =>
            {
                UpdateUndo();
                if (!string.IsNullOrEmpty(example)) { txtRight.Focus(); txtRight.SelectAll(); }
                await ScanAsync();
            };
        }

        private void SaveRules() => NumberFixStore.Save(_rules.Select(r => r.ToRule()));

        private IReadOnlyList<NumberFixDoc> Scope => _rbSelected.IsChecked == true ? _selected : _all;

        private void UpdateUndo()
        {
            var info = NumberFixStore.LastChangeInfo();
            _btnUndo.Content = info == null ? "Rückgängig" : $"Rückgängig ({info})";
            _btnUndo.IsEnabled = info != null;
            _btnUndo.ToolTip = "Stellt die zuletzt korrigierten TXT-Dateien aus der Sicherung wieder her.";
        }

        private void UpdateStatus(string? extra = null)
        {
            int on = _hits.Count(h => h.Apply);
            int files = _hits.Where(h => h.Apply).Select(h => h.Hit.File).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            _status.Text = $"{_hits.Count} Treffer, davon {on} ausgewählt in {files} Datei(en)." + (extra != null ? "  " + extra : "");
            _btnApply.IsEnabled = on > 0;
        }

        private async Task ScanAsync()
        {
            int run = ++_scanRun;
            var rules = _rules.Select(r => r.ToRule()).ToList();
            var docs = Scope.GroupBy(d => d.TxtPath, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
            _status.Text = $"Durchsuche {docs.Count} TXT-Datei(en)…";
            var found = await Task.Run(() =>
            {
                var list = new List<HitRow>();
                var errors = 0;
                if (!rules.Any(r => r.Active && r.IsValid)) return (list, errors);
                foreach (var d in docs)
                {
                    if (run != _scanRun) break;
                    try
                    {
                        if (!File.Exists(d.TxtPath)) continue;
                        var (text, _) = NumberFixStore.ReadText(d.TxtPath);
                        foreach (var h in NumberFixStore.FindHits(d.TxtPath, text, rules))
                            list.Add(new HitRow { Hit = h, Document = d.Name });
                    }
                    catch { errors++; }
                }
                return (list, errors);
            });
            if (run != _scanRun) return;
            _hits.Clear();
            foreach (var h in found.list) _hits.Add(h);
            UpdateStatus(found.errors > 0 ? $"{found.errors} Datei(en) nicht lesbar." : !rules.Any(r => r.Active && r.IsValid) ? "Keine gültige aktive Regel." : null);
        }

        private async Task ApplyAsync()
        {
            var chosen = _hits.Where(h => h.Apply).Select(h => h.Hit).ToList();
            if (chosen.Count == 0) return;
            int files = chosen.Select(h => h.File).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            if (System.Windows.MessageBox.Show(this, $"{chosen.Count} Korrektur(en) in {files} TXT-Datei(en) schreiben?\n\nDie Originale werden vorher gesichert und können mit „Rückgängig“ wiederhergestellt werden.",
                    Title, MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            SaveRules();
            var result = await Task.Run(() => NumberFixStore.Apply(chosen));
            foreach (var f in result.ChangedFiles) ChangedFiles.Add(f);
            NumberFixStore.RaiseTextFilesChanged(result.ChangedFiles);
            UpdateUndo();
            await ScanAsync();
            UpdateStatus($"Geschrieben: {result.Replacements} Korrektur(en) in {result.ChangedFiles.Count} Datei(en)."
                + (result.Errors.Count > 0 ? $" Fehler: {result.Errors.Count} ({result.Errors[0]})" : ""));
        }

        private async Task UndoAsync()
        {
            var info = NumberFixStore.LastChangeInfo();
            if (info == null) return;
            if (System.Windows.MessageBox.Show(this, $"Letzte Korrektur ({info}) rückgängig machen und die gesicherten Originale zurückschreiben?",
                    Title, MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            var result = await Task.Run(NumberFixStore.UndoLast);
            foreach (var f in result.ChangedFiles) ChangedFiles.Add(f);
            NumberFixStore.RaiseTextFilesChanged(result.ChangedFiles);
            UpdateUndo();
            await ScanAsync();
            UpdateStatus($"Wiederhergestellt: {result.ChangedFiles.Count} Datei(en)." + (result.Errors.Count > 0 ? $" Fehler: {result.Errors[0]}" : ""));
        }
    }
}
