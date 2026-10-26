using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using WpfButton = System.Windows.Controls.Button;
using WpfButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfContextMenu = System.Windows.Controls.ContextMenu;
using WpfListBox = System.Windows.Controls.ListBox;
using WpfListView = System.Windows.Controls.ListView;
using WpfListViewItem = System.Windows.Controls.ListViewItem;
using WpfMenuItem = System.Windows.Controls.MenuItem;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfTextBoxBase = System.Windows.Controls.Primitives.TextBoxBase;

// Gemeinsame Hilfen der „Übertragen“-Dialoge (Hauptprogramm und INI-Generator binden diese Datei jeweils selbst ein).
namespace Dateiumbenenner
{
    /// <summary>Gemeinsame Filter-Einstellung der Übertragen-Dialoge (sitzungsweit gemerkt).</summary>
    internal static class TransferSearch
    {
        /// <summary>Filter durchsucht zusätzlich den Belegtext.</summary>
        public static bool InText { get; set; } = true;

        public static WpfCheckBox CreateTextOption(Action changed)
        {
            var c = new WpfCheckBox
            {
                Content = "auch im Belegtext suchen", IsChecked = InText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0),
                ToolTip = "Aus: nur Firmennamen (aktuell, erkannt und früher erkannt), Dateiname, Belegart, Nummer usw. durchsuchen –\n"
                    + "z. B. findet „0,78 KG“ dann nur Belege, die einmal so benannt waren, statt aller Belege mit einem Gewicht im Text."
            };
            c.Click += (_, _) => { InText = c.IsChecked == true; changed(); };
            return c;
        }

        /// <summary>Alle Suchwörter im Schlüssel bzw. – wenn eingeschaltet – im Belegtext.</summary>
        public static bool Matches(string[] words, string key, string? fullText)
            => words.All(w => key.Contains(w, StringComparison.OrdinalIgnoreCase))
                || InText && fullText != null && words.All(w => fullText.Contains(w, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Hilfen für die Übertragen-Dialoge: Ankreuzen per Pfeiltasten + Leertaste.</summary>
    internal static class ListKeyboard
    {
        /// <summary>Checkbox-Spalte, die den Fokus nicht übernimmt – Pfeiltasten und Leertaste bleiben in der Liste.</summary>
        public static DataTemplate CheckTemplate(string path)
        {
            var chk = new FrameworkElementFactory(typeof(WpfCheckBox));
            chk.SetBinding(ToggleButton.IsCheckedProperty, new System.Windows.Data.Binding(path) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            chk.SetValue(UIElement.FocusableProperty, false);
            chk.AddHandler(WpfButtonBase.ClickEvent, new RoutedEventHandler(CheckClicked));
            return new DataTemplate { VisualTree = chk };
        }

        /// <summary>Klick auf ein Kästchen wählt auch die Zeile, damit danach mit den Pfeiltasten weitergearbeitet werden kann.</summary>
        private static void CheckClicked(object sender, RoutedEventArgs e)
        {
            if (sender is not DependencyObject d) return;
            var item = FindAncestor<WpfListViewItem>(d);
            if (item == null) return;
            if (!item.IsSelected && ItemsControl.ItemsControlFromItemContainer(item) is WpfListBox list)
                list.SelectedItem = list.ItemContainerGenerator.ItemFromContainer(item);
            item.Focus();
        }

        /// <summary>Leertaste kreuzt alle gewählten Zeilen an bzw. ab (neuer Zustand = Gegenteil der ersten Zeile).</summary>
        public static void AttachSpaceToggle<T>(WpfListView list, Func<T, bool> get, Action<T, bool> set, Action? changed = null)
        {
            list.PreviewKeyDown += (_, e) =>
            {
                if (e.Key != Key.Space || Keyboard.Modifiers != ModifierKeys.None || e.OriginalSource is WpfTextBoxBase or WpfComboBox) return;
                var rows = list.SelectedItems.OfType<T>().ToList();
                if (rows.Count == 0) return;
                var on = !get(rows[0]);
                foreach (var r in rows) set(r, on);
                changed?.Invoke();
                e.Handled = true;
            };
        }

        /// <summary>Pfeil rechts/links wechselt zwischen zwei Listen (z. B. Gruppen ↔ Belege).</summary>
        public static void LinkLists(WpfListView left, WpfListView right)
        {
            left.PreviewKeyDown += (_, e) =>
            {
                if (e.Key != Key.Right || Keyboard.Modifiers != ModifierKeys.None || e.OriginalSource is WpfTextBoxBase || right.Items.Count == 0) return;
                if (right.SelectedIndex < 0) right.SelectedIndex = 0;
                FocusSelected(right);
                e.Handled = true;
            };
            right.PreviewKeyDown += (_, e) =>
            {
                if (e.Key != Key.Left || Keyboard.Modifiers != ModifierKeys.None || e.OriginalSource is WpfTextBoxBase) return;
                FocusSelected(left);
                e.Handled = true;
            };
        }

        /// <summary>Fokus auf die gewählte Zeile (sonst die erste), damit die Pfeiltasten sofort wirken.</summary>
        public static void FocusSelected(WpfListView list) => FocusSelected(list, true);

        private static void FocusSelected(WpfListView list, bool retry)
        {
            WpfListViewItem? c = null;
            if (list.Items.Count > 0)
            {
                if (list.SelectedIndex < 0) list.SelectedIndex = 0;
                list.ScrollIntoView(list.SelectedItem);
                list.UpdateLayout();
                c = list.ItemContainerGenerator.ContainerFromItem(list.SelectedItem) as WpfListViewItem;
            }
            if (c != null) { c.Focus(); return; }
            list.Focus();
            // Zeilen werden teils erst nach dem Laden gefüllt bzw. erzeugt – einmal später erneut versuchen
            if (retry)
                list.Dispatcher.BeginInvoke(() => { if (list.IsKeyboardFocusWithin && Keyboard.FocusedElement == list) FocusSelected(list, false); },
                    System.Windows.Threading.DispatcherPriority.ContextIdle);
        }

        internal static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
        {
            while (d != null && d is not T) d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            return d as T;
        }
    }

    /// <summary>Gruppen in den Übertragen-Dialogen bearbeiten: markierte Belege aus einer Gruppe lösen (eigene Gruppe)
    /// oder eine Gruppe auflösen (je Beleg eine Gruppe). Einzelne Belege werden über ihr Häkchen abgewählt.</summary>
    internal static class GroupTools
    {
        /// <summary>Ergänzt Kontextmenüs und eine Leiste über der Belegliste. Liefert den Bereich, der statt
        /// <paramref name="rowList"/> eingesetzt wird.</summary>
        /// <param name="rowsOf">Veränderbare Belegliste einer Gruppe.</param>
        /// <param name="create">Neue Gruppe nach Vorlage mit den angegebenen Belegen; <c>true</c> = beim Auflösen (ein Beleg je Gruppe).</param>
        /// <param name="refresh">Anzeige einer Gruppe aktualisieren (Anzahl, Häkchen …).</param>
        public static DockPanel Attach<TGroup, TRow>(WpfListView groupList, WpfListView rowList, ObservableCollection<TGroup> groups,
            Func<TGroup, List<TRow>> rowsOf, Func<TGroup, List<TRow>, bool, TGroup> create, Action<TGroup> refresh, Action? changed = null)
            where TGroup : class where TRow : class
        {
            var info = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), Foreground = System.Windows.Media.Brushes.DimGray, TextTrimming = TextTrimming.CharacterEllipsis };

            void PullOut()
            {
                if (groupList.SelectedItem is not TGroup g) { info.Text = "Erst oben eine Gruppe wählen."; return; }
                var all = rowsOf(g);
                var rows = rowList.SelectedItems.OfType<TRow>().Where(all.Contains).ToList();
                if (rows.Count == 0) { info.Text = "Belege in dieser Liste markieren (Strg/Umschalt + Klick)."; return; }
                if (rows.Count >= all.Count) { info.Text = "Alle Belege markiert – das wäre wieder dieselbe Gruppe."; return; }
                foreach (var r in rows) all.Remove(r);
                var ng = create(g, rows, false);
                refresh(g);
                groups.Insert(groups.IndexOf(g) + 1, ng);
                groupList.SelectedItem = ng;
                groupList.ScrollIntoView(ng);
                info.Text = $"{rows.Count} Beleg(e) als eigene Gruppe herausgelöst.";
                changed?.Invoke();
            }

            void Dissolve()
            {
                var sel = groupList.SelectedItems.OfType<TGroup>().Where(g => rowsOf(g).Count > 1).ToList();
                if (sel.Count == 0) { info.Text = "Gruppe(n) mit mehr als einem Beleg oben markieren."; return; }
                TGroup? first = null;
                int n = 0;
                foreach (var g in sel)
                {
                    var idx = groups.IndexOf(g);
                    if (idx < 0) continue;
                    var rows = rowsOf(g).ToList();
                    groups.RemoveAt(idx);
                    for (int i = 0; i < rows.Count; i++)
                    {
                        var ng = create(g, new List<TRow> { rows[i] }, true);
                        groups.Insert(idx + i, ng);
                        first ??= ng;
                    }
                    n += rows.Count;
                }
                if (first != null) { groupList.SelectedItem = first; groupList.ScrollIntoView(first); }
                info.Text = $"{sel.Count} Gruppe(n) in {n} Einzelbelege aufgelöst.";
                changed?.Invoke();
            }

            WpfMenuItem Item(string header, Action a) { var m = new WpfMenuItem { Header = header }; m.Click += (_, _) => a(); return m; }
            void AddMenu(WpfListView list, WpfMenuItem item)
            {
                list.ContextMenu ??= new WpfContextMenu();
                if (list.ContextMenu.Items.Count > 0 && list.ContextMenu.Items[^1] is not Separator && !list.ContextMenu.Items.OfType<object>().Any(o => o is WpfMenuItem { Tag: "group" }))
                    list.ContextMenu.Items.Add(new Separator());
                item.Tag = "group";
                list.ContextMenu.Items.Add(item);
            }
            AddMenu(rowList, Item("Markierte Belege aus der Gruppe lösen (eigene Gruppe)", PullOut));
            AddMenu(groupList, Item("Gruppe auflösen (je Beleg eine eigene Gruppe)", Dissolve));
            AddMenu(rowList, Item("Gruppe auflösen (je Beleg eine eigene Gruppe)", Dissolve));
            rowList.ToolTip ??= "Häkchen = Beleg wird übertragen. Markierte Belege können aus der Gruppe gelöst werden (Rechtsklick).";

            var bar = new StackPanel { Orientation = WpfOrientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            WpfButton Btn(string text, string tip, Action a)
            {
                var b = new WpfButton { Content = text, ToolTip = tip, Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(0, 0, 4, 0) };
                b.Click += (_, _) => a();
                bar.Children.Add(b);
                return b;
            }
            Btn("Aus Gruppe lösen", "Die in dieser Liste markierten Belege (Strg/Umschalt + Klick) bilden eine eigene Gruppe – z. B. wenn sie nicht zur Gruppe passen", PullOut);
            Btn("Gruppe auflösen", "Die oben markierte(n) Gruppe(n) in Einzelbelege aufteilen, die sich einzeln an- und abwählen lassen", Dissolve);
            bar.Children.Add(info);

            var panel = new DockPanel();
            DockPanel.SetDock(bar, Dock.Top);
            panel.Children.Add(bar);
            if (rowList.Parent is System.Windows.Controls.Panel parent)
            {
                var idx = parent.Children.IndexOf(rowList);
                parent.Children.RemoveAt(idx);
                Grid.SetRow(panel, Grid.GetRow(rowList));
                Grid.SetColumn(panel, Grid.GetColumn(rowList));
                parent.Children.Insert(idx, panel);
            }
            panel.Children.Add(rowList);
            return panel;
        }
    }

    /// <summary>Hebt alle Fundstellen eines Suchbegriffs in einer Text-Anzeige farbig hervor (nur der sichtbare Bereich wird gezeichnet).</summary>
    internal sealed class HighlightAdorner : Adorner
    {
        private static readonly SolidColorBrush Fill = Freeze(new SolidColorBrush(System.Windows.Media.Color.FromArgb(120, 255, 214, 0)));
        private static readonly System.Windows.Media.Pen Border = Freeze(new System.Windows.Media.Pen(new SolidColorBrush(System.Windows.Media.Color.FromArgb(200, 230, 150, 0)), 1));
        private readonly WpfTextBox _box;
        private List<(int Start, int Length)> _ranges = new();
        /// <summary>Text, zu dem die Fundstellen gehören – nach einem Belegwechsel passen sie nicht mehr.</summary>
        private string _rangesText = "";
        private bool _retry;

        public HighlightAdorner(WpfTextBox box) : base(box)
        {
            _box = box;
            IsHitTestVisible = false;
            box.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, _) => InvalidateVisual()));
            box.SizeChanged += (_, _) => InvalidateVisual();
            box.TextChanged += (_, _) => InvalidateVisual();
        }

        private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }

        public void SetRanges(List<(int Start, int Length)> ranges)
        {
            _ranges = ranges;
            _rangesText = _box.Text ?? "";
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            var text = _box.Text ?? "";
            if (_ranges.Count == 0 || text.Length == 0 || !ReferenceEquals(text, _rangesText) && text != _rangesText) return;
            dc.PushClip(new RectangleGeometry(new Rect(_box.RenderSize)));
            try
            {
                Draw(dc, text.Length);
                _retry = false;
            }
            catch (Exception ex) when (ex is ArgumentOutOfRangeException or InvalidOperationException)
            {
                // Zeilenaufteilung der Anzeige noch nicht neu berechnet (Text gerade gewechselt) – nach dem Layout erneut zeichnen
                if (!_retry)
                {
                    _retry = true;
                    Dispatcher.BeginInvoke(InvalidateVisual, System.Windows.Threading.DispatcherPriority.Background);
                }
            }
            finally { dc.Pop(); }
        }

        private void Draw(DrawingContext dc, int textLength)
        {
            int lines = _box.LineCount;
            if (lines <= 0) return;
            int first = _box.GetFirstVisibleLineIndex(), last = _box.GetLastVisibleLineIndex();
            if (first < 0 || first >= lines) return;
            if (last < 0 || last >= lines) last = lines - 1;
            int from = _box.GetCharacterIndexFromLineIndex(first);
            int to = Math.Min(textLength, _box.GetCharacterIndexFromLineIndex(last) + _box.GetLineLength(last));
            foreach (var (start, length) in _ranges)
            {
                int end = Math.Min(start + length, textLength);
                if (end <= from || start >= to) continue;
                // Treffer über einen Zeilenumbruch hinweg: je Zeile ein Rechteck
                int pos = Math.Max(start, from);
                while (pos < end)
                {
                    int line = _box.GetLineIndexFromCharacterIndex(pos);
                    if (line < 0 || line >= lines) break;
                    int lineEnd = _box.GetCharacterIndexFromLineIndex(line) + _box.GetLineLength(line);
                    int segEnd = Math.Max(pos + 1, Math.Min(end, lineEnd));
                    var a = _box.GetRectFromCharacterIndex(pos);
                    var b = _box.GetRectFromCharacterIndex(segEnd - 1, true);
                    if (!a.IsEmpty && !b.IsEmpty && b.Right > a.Left)
                        dc.DrawRectangle(Fill, Border, new Rect(new System.Windows.Point(a.Left, a.Top), new System.Windows.Point(b.Right, Math.Max(a.Bottom, b.Bottom))));
                    pos = segEnd;
                }
            }
        }
    }

    /// <summary>Suchleiste für den Belegtext der Vorschau: alle Fundstellen werden gelb hervorgehoben, die aktuelle markiert.
    /// Enter = nächster Treffer, Umschalt+Enter = vorheriger, Strg+F = ins Suchfeld. Folgt dem Filter des Dialogs,
    /// solange kein eigener Suchbegriff eingegeben wurde. In der PDF-Ansicht wird die Suche der Anzeige verwendet.</summary>
    internal sealed class TextSearchBar : DockPanel
    {
        private readonly WpfTextBox _box;
        private readonly TextBlock _info;
        private readonly Func<Task<WpfTextBox?>> _textView;
        private string _fullText = "";
        private WpfTextBox? _view;
        private HighlightAdorner? _adorner;
        private Microsoft.Web.WebView2.Wpf.WebView2? _web;
        private bool _webFind;
        private string _lastFilter = "";

        /// <param name="textView">Schaltet die Vorschau auf den Belegtext um und liefert die Text-Anzeige (null = kein Beleg gewählt).</param>
        public TextSearchBar(Func<Task<WpfTextBox?>> textView)
        {
            _textView = textView;
            Margin = new Thickness(0, 0, 0, 4);
            LastChildFill = true;
            var label = new TextBlock { Text = "Suchen im Text: ", VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(label, Dock.Left);
            Children.Add(label);
            WpfButton Btn(string content, string tip, bool forward)
            {
                var b = new WpfButton { Content = content, ToolTip = tip, Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(4, 0, 0, 0), Focusable = false };
                b.Click += async (_, _) => await Find(forward);
                DockPanel.SetDock(b, Dock.Right);
                return b;
            }
            _info = new TextBlock { MinWidth = 90, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), Foreground = System.Windows.Media.Brushes.DimGray };
            DockPanel.SetDock(_info, Dock.Right);
            Children.Add(Btn("▶", "Nächster Treffer (Enter)", true));
            Children.Add(Btn("◀", "Vorheriger Treffer (Umschalt+Enter)", false));
            Children.Add(_info);
            _box = new WpfTextBox { ToolTip = "Text im gewählten Beleg suchen – alle Fundstellen werden gelb markiert. Enter = weiter, Umschalt+Enter = zurück, Strg+F = hierher.\nMehrere Wörter: steht die Wortfolge nicht im Text, wird jedes Wort einzeln markiert." };
            _box.PreviewKeyDown += async (_, e) =>
            {
                if (e.Key != Key.Enter) return;
                e.Handled = true;
                await Find((Keyboard.Modifiers & ModifierKeys.Shift) == 0);
            };
            _box.TextChanged += async (_, _) => { Update(true); await FindInWeb(); };
            Children.Add(_box);
        }

        public string Term => _box.Text ?? "";

        /// <summary>Strg+F im Fenster setzt den Fokus ins Suchfeld.</summary>
        public void AttachShortcut(Window window)
        {
            window.PreviewKeyDown += (_, e) =>
            {
                if (e.Key != Key.F || Keyboard.Modifiers != ModifierKeys.Control) return;
                _box.Focus();
                _box.SelectAll();
                e.Handled = true;
            };
        }

        /// <summary>Filter des Dialogs übernehmen, solange im Suchfeld nichts Eigenes steht – die gefilterten Wörter werden im Text markiert.</summary>
        public void FollowFilter(WpfTextBox filter)
        {
            filter.TextChanged += (_, _) =>
            {
                var f = filter.Text?.Trim() ?? "";
                if (Term.Trim() == _lastFilter) _box.Text = f;
                _lastFilter = f;
            };
        }

        /// <summary>PDF-Anzeige: nach dem Laden die Suche der Anzeige mit allen Fundstellen starten.</summary>
        public void SetWeb(Microsoft.Web.WebView2.Wpf.WebView2 web)
        {
            if (ReferenceEquals(_web, web)) return;
            _web = web;
            web.NavigationCompleted += async (_, _) => await FindInWeb();
        }

        /// <summary>Neuer Beleg in der Vorschau: Treffer zählen, hervorheben und optional den ersten markieren.</summary>
        /// <param name="view">Text-Anzeige, falls sichtbar (null bei PDF-Ansicht).</param>
        public void DocumentChanged(string? fullText, WpfTextBox? view, bool selectFirst)
        {
            _fullText = fullText ?? "";
            _view = view;
            if (view != null) _webFind = false;
            Update(selectFirst);
        }

        private void Update(bool selectFirst)
        {
            var term = Term;
            var matches = Matches(_fullText, term);
            Highlight(matches);
            if (term.Trim().Length == 0) { _info.Text = ""; return; }
            _info.Text = matches.Count == 0 ? "Nicht gefunden" : $"{matches.Count} Treffer";
            if (!selectFirst || matches.Count == 0 || _view == null || _view.Text != _fullText) return;
            Show(_view, matches[0]);
            _info.Text = $"1 / {matches.Count}";
        }

        private void Highlight(List<(int Start, int Length)> matches)
        {
            if (_view == null || _view.Text != _fullText) return;
            var view = _view;
            var layer = AdornerLayer.GetAdornerLayer(view);
            if (layer == null)
            {
                // Anzeige noch nicht im Fenster – nach dem Laden erneut
                view.Dispatcher.BeginInvoke(() => { if (_view == view && AdornerLayer.GetAdornerLayer(view) != null) Highlight(matches); },
                    System.Windows.Threading.DispatcherPriority.Loaded);
                return;
            }
            if (_adorner == null || !ReferenceEquals(_adorner.AdornedElement, view))
            {
                _adorner = new HighlightAdorner(view);
                var a = _adorner;
                view.Unloaded += (_, _) => AdornerLayer.GetAdornerLayer(view)?.Remove(a);
            }
            if (layer.GetAdorners(view) is not { } existing || !existing.Contains(_adorner)) layer.Add(_adorner);
            _adorner.SetRanges(matches);
        }

        private async Task FindInWeb()
        {
            if (_view != null || _web?.CoreWebView2 == null || !_web.IsVisible) return;
            try
            {
                var core = _web.CoreWebView2;
                var term = Term.Trim();
                if (term.Length == 0) { core.Find.Stop(); _webFind = false; return; }
                var opt = core.Environment.CreateFindOptions();
                opt.FindTerm = term;
                opt.IsCaseSensitive = false;
                opt.ShouldHighlightAllMatches = true;
                opt.SuppressDefaultFindDialog = false;
                await core.Find.StartAsync(opt);
                _webFind = true;
            }
            catch { _webFind = false; /* Suche der Anzeige nicht verfügbar */ }
        }

        public async Task Find(bool forward)
        {
            var term = Term;
            if (term.Trim().Length == 0) { _info.Text = ""; return; }
            // PDF-Anzeige mit eigener Suche: dort weiterblättern, sonst auf den Belegtext umschalten
            if (_view == null && _webFind && _web?.CoreWebView2 is { } core)
            {
                try
                {
                    if (core.Find.MatchCount > 0)
                    {
                        if (forward) core.Find.FindNext(); else core.Find.FindPrevious();
                        _info.Text = $"{core.Find.ActiveMatchIndex + 1} / {core.Find.MatchCount}";
                        return;
                    }
                }
                catch { }
            }
            var view = await _textView();
            if (view == null) { _info.Text = "Kein Beleg gewählt"; return; }
            _view = view;
            _fullText = view.Text ?? "";
            var matches = Matches(_fullText, term);
            Highlight(matches);
            if (matches.Count == 0) { _info.Text = "Nicht gefunden"; return; }
            int sel = view.SelectionStart;
            int i;
            if (forward)
            {
                i = matches.FindIndex(m => view.SelectionLength > 0 ? m.Start > sel : m.Start >= sel);
                if (i < 0) i = 0;
            }
            else
            {
                i = matches.FindLastIndex(m => m.Start < sel);
                if (i < 0) i = matches.Count - 1;
            }
            _info.Text = $"{i + 1} / {matches.Count}";
            Show(view, matches[i]);
        }

        /// <summary>Fundstellen: die ganze Eingabe; steht sie so nicht im Text, jedes Wort einzeln (wie der Filter, der alle Wörter einzeln sucht).</summary>
        internal static List<(int Start, int Length)> Matches(string text, string term)
        {
            var result = new List<(int Start, int Length)>();
            term = term.Trim();
            if (term.Length == 0 || text.Length == 0) return result;
            AddAll(text, term, result);
            if (result.Count > 0) return result;
            var words = term.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();
            if (words.Count < 2) return result;
            foreach (var w in words) AddAll(text, w, result);
            result.Sort((a, b) => a.Start.CompareTo(b.Start));
            return result;
        }

        private static void AddAll(string text, string term, List<(int Start, int Length)> result)
        {
            int idx = 0;
            while (idx < text.Length && (idx = text.IndexOf(term, idx, StringComparison.CurrentCultureIgnoreCase)) >= 0)
            {
                result.Add((idx, term.Length));
                idx += Math.Max(1, term.Length);
            }
        }

        /// <summary>Nächste Fundstelle ab der aktuellen Markierung, am Ende wieder von vorn.</summary>
        internal static int Next(string text, string term, int selStart, int selLength, bool forward)
        {
            var matches = Matches(text, term);
            if (matches.Count == 0) return -1;
            int i = forward ? matches.FindIndex(m => selLength > 0 ? m.Start > selStart : m.Start >= selStart) : matches.FindLastIndex(m => m.Start < selStart);
            if (i < 0) i = forward ? 0 : matches.Count - 1;
            return matches[i].Start;
        }

        internal static int Count(string text, string term) => Matches(text, term).Count;

        private static void Show(WpfTextBox view, (int Start, int Length) m)
        {
            view.IsInactiveSelectionHighlightEnabled = true;
            view.Select(m.Start, m.Length);
            view.Dispatcher.InvokeAsync(() =>
            {
                var line = view.GetLineIndexFromCharacterIndex(m.Start);
                if (line >= 0) view.ScrollToLine(Math.Max(0, line - 3));
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }
}
