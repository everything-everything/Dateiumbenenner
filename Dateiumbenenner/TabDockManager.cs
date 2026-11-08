using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DataObject = System.Windows.DataObject;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using QueryContinueDragEventHandler = System.Windows.QueryContinueDragEventHandler;
using Size = System.Windows.Size;
using TabControl = System.Windows.Controls.TabControl;

namespace Dateiumbenenner
{
    /// <summary>
    /// Registerkarten wie im Browser: per Ziehen oder Rechtsklick links/rechts neben einem Mittelbereich andocken
    /// oder in ein eigenes Fenster lösen. Die Anordnung (inkl. Fensterposition) wird in <see cref="Layout"/> gemerkt.
    /// </summary>
    internal sealed class TabDockManager
    {
        private const string DockFormat = "Dateiumbenenner.DockTab";
        private readonly FrameworkElement _view;
        private readonly ColumnDefinition _colLeft, _colRight;
        private readonly UIElement _splitLeft, _splitRight;
        private readonly FrameworkElement _center;
        private readonly TabItem[] _tabs;
        private readonly string _keyPrefix, _title;
        private readonly Dictionary<TabItem, char> _home = new(), _initialHome = new();
        private readonly Dictionary<TabControl, GridLength> _defaultWidth = new();
        private readonly List<Window> _floatWindows = new();
        private readonly HashSet<TabControl> _floatHosts = new();
        private Window? _owner;
        private bool _restoring, _ownerClosed, _restored, _dropped;
        private TabItem? _dragTab;
        private Point _dragStart;
        private System.Windows.Threading.DispatcherTimer? _saveTimer;

        public TabControl Left { get; }
        public TabControl Right { get; }
        /// <summary>Gemerkte Anordnung je Registerkarte: "L", "R" oder "F&lt;Heimatseite&gt;|links,oben,breite,höhe|fenster".</summary>
        public IDictionary<string, string> Layout { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Action? Save { get; set; }
        public double MinSideWidth { get; set; } = 250;
        /// <summary>Vor dem Umhängen einer Registerkarte (z. B. WebView2 freigeben).</summary>
        public Action<TabItem>? TabMoving { get; set; }
        /// <summary>Nach dem Umhängen (nicht beim Wiederherstellen) – Inhalte auffrischen.</summary>
        public Action? TabsMoved { get; set; }
        public Action<TabControl>? HostCreated { get; set; }
        public Action<Window>? FloatWindowCreated { get; set; }

        public TabDockManager(FrameworkElement view, TabControl left, TabControl right, ColumnDefinition colLeft, ColumnDefinition colRight,
            UIElement splitLeft, UIElement splitRight, FrameworkElement center, IEnumerable<TabItem> tabs, string keyPrefix, string title)
        {
            _view = view; Left = left; Right = right; _colLeft = colLeft; _colRight = colRight;
            _splitLeft = splitLeft; _splitRight = splitRight; _center = center; _tabs = tabs.ToArray();
            _keyPrefix = keyPrefix; _title = title;
            _defaultWidth[left] = colLeft.Width.Value >= 1 ? colLeft.Width : new GridLength(2, GridUnitType.Star);
            _defaultWidth[right] = colRight.Width.Value >= 1 ? colRight.Width : new GridLength(2, GridUnitType.Star);
            foreach (var tab in _tabs)
            {
                _home[tab] = _initialHome[tab] = tab.Parent == left ? 'L' : 'R';
                tab.ToolTip ??= "Ziehen oder Rechtsklick: links/rechts andocken oder in eigenem Fenster öffnen";
                tab.PreviewMouseLeftButtonDown += Tab_PreviewMouseLeftButtonDown;
                tab.PreviewMouseMove += Tab_PreviewMouseMove;
                tab.PreviewMouseLeftButtonUp += (_, _) => _dragTab = null;
                tab.ContextMenu = CreateMenu(tab);
            }
            HookHost(left);
            HookHost(right);
            center.AllowDrop = true;
            center.PreviewDragOver += Host_PreviewDragOver;
            center.PreviewDrop += Center_PreviewDrop;
            UpdateLayout();
            view.Loaded += View_Loaded;
        }

        public bool IsHost(TabControl tc) => tc == Left || tc == Right || _floatHosts.Contains(tc);

        private string Key(TabItem t) => _keyPrefix + (t.Header as string ?? t.Name);

        private string Spec(TabItem tab) => Layout.TryGetValue(Key(tab), out var s) && !string.IsNullOrEmpty(s) ? s : _home[tab].ToString();

        private TabControl HomeHost(TabItem tab) => _home.TryGetValue(tab, out var side) && side == 'L' ? Left : Right;

        private void View_Loaded(object sender, RoutedEventArgs e)
        {
            var owner = Window.GetWindow(_view);
            if (owner != null && owner != _owner)
            {
                _owner = owner;
                // eigene Fenster werden mit dem Besitzer geschlossen: Anordnung vorher merken, danach nicht zurückdocken
                owner.Closing += (_, _) => SaveLayout();
                owner.Closed += (_, _) => _ownerClosed = true;
            }
            if (_restored || owner == null) return;
            _restored = true;
            _restoring = true;
            try
            {
                foreach (var tab in _tabs)
                {
                    var spec = Spec(tab);
                    if (spec.Length > 1 && spec[0] == 'F') _home[tab] = spec[1] == 'L' ? 'L' : 'R';
                    if (spec == "L") MoveTab(tab, Left);
                    else if (spec == "R") MoveTab(tab, Right);
                }
                foreach (var g in _tabs.Select(t => (Tab: t, Spec: Spec(t))).Where(x => x.Spec.StartsWith("F", StringComparison.Ordinal)).GroupBy(x => x.Spec))
                {
                    var win = CreateFloatWindow(ParseBounds(g.Key));
                    var host = (TabControl)win.Content;
                    foreach (var x in g) MoveTab(x.Tab, host);
                    host.SelectedIndex = 0;
                    win.Show();
                }
                foreach (var tc in new[] { Left, Right }) if (tc.Items.Count > 0 && tc.SelectedIndex < 0) tc.SelectedIndex = 0;
            }
            finally { _restoring = false; }
            UpdateLayout();
            TabsMoved?.Invoke();
        }

        private void HookHost(TabControl tc)
        {
            tc.AllowDrop = true;
            tc.PreviewDragOver += Host_PreviewDragOver;
            tc.PreviewDrop += Host_PreviewDrop;
        }

        private ContextMenu CreateMenu(TabItem tab)
        {
            var menu = new ContextMenu();
            var left = new MenuItem { Header = "Links andocken" };
            var right = new MenuItem { Header = "Rechts andocken" };
            var own = new MenuItem { Header = "In eigenem Fenster öffnen" };
            var reset = new MenuItem { Header = "Standardanordnung wiederherstellen" };
            left.Click += (_, _) => MoveTab(tab, Left);
            right.Click += (_, _) => MoveTab(tab, Right);
            own.Click += (_, _) => FloatTab(tab, MouseScreenDip());
            reset.Click += (_, _) => ResetLayout();
            menu.Items.Add(left);
            menu.Items.Add(right);
            menu.Items.Add(own);
            menu.Items.Add(new Separator());
            menu.Items.Add(reset);
            menu.Opened += (_, _) =>
            {
                var host = tab.Parent as TabControl;
                left.IsEnabled = host != Left;
                right.IsEnabled = host != Right;
                own.IsEnabled = host == Left || host == Right || host?.Items.Count > 1;
            };
            return menu;
        }

        private void ResetLayout()
        {
            foreach (var tab in _tabs) MoveTab(tab, _initialHome[tab] == 'L' ? Left : Right);
        }

        /// <summary>Registerkarte in einen anderen Bereich (links, rechts, eigenes Fenster) verschieben; index &lt; 0 = ans Ende.</summary>
        public void MoveTab(TabItem tab, TabControl target, int index = -1)
        {
            var src = tab.Parent as TabControl;
            if (src == target && index < 0) { tab.IsSelected = true; return; }
            TabMoving?.Invoke(tab);
            if (src != null)
            {
                int old = src.Items.IndexOf(tab);
                src.Items.Remove(tab);
                if (src == target && index > old) index--;
            }
            if (index < 0 || index > target.Items.Count) index = target.Items.Count;
            target.Items.Insert(index, tab);
            target.SelectedItem = tab;
            if (target == Left) _home[tab] = 'L';
            else if (target == Right) _home[tab] = 'R';
            UpdateLayout();
            if (src != null && src != target && _floatHosts.Contains(src) && Window.GetWindow(src) is Window w && _floatWindows.Contains(w))
            {
                if (src.Items.Count == 0) w.Close();
                else UpdateFloatTitle(w);
            }
            if (Window.GetWindow(target) is Window tw && _floatWindows.Contains(tw)) UpdateFloatTitle(tw);
            if (_restoring) return;
            TabsMoved?.Invoke();
            SaveLayout();
        }

        private void UpdateLayout()
        {
            void Side(TabControl tc, ColumnDefinition col, UIElement splitter)
            {
                bool show = tc.Items.Count > 0;
                tc.Visibility = splitter.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                col.MinWidth = show ? MinSideWidth : 0;
                if (!show) col.Width = new GridLength(0);
                else if (col.Width.Value < 1) col.Width = _defaultWidth[tc];
            }
            Side(Left, _colLeft, _splitLeft);
            Side(Right, _colRight, _splitRight);
        }

        private Window CreateFloatWindow(Rect? bounds)
        {
            var tc = new TabControl { Margin = new Thickness(4) };
            _floatHosts.Add(tc);
            HookHost(tc);
            HostCreated?.Invoke(tc);
            var owner = _owner ?? Window.GetWindow(_view);
            var w = new Window
            {
                Title = _title,
                Content = tc,
                Owner = owner,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Width = 560,
                Height = 720,
                ShowActivated = true,
            };
            var r = bounds ?? Rect.Empty;
            if (!r.IsEmpty && r.Width >= 200 && r.Height >= 150 && OnScreen(r))
            { w.Left = r.Left; w.Top = r.Top; w.Width = r.Width; w.Height = r.Height; }
            else if (owner != null)
            { w.Left = owner.Left + Math.Max(0, owner.ActualWidth - w.Width - 40); w.Top = owner.Top + 80; }
            w.Closing += (_, _) => _floatWindows.Remove(w);
            w.Closed += (_, _) => _view.Dispatcher.BeginInvoke(new Action(() =>
            {
                _floatHosts.Remove(tc);
                // vom Benutzer geschlossen (nicht mit dem Besitzer): Registerkarten zurück an ihre letzte Andockseite
                if (_ownerClosed || _owner == null || !_owner.IsVisible) return;
                foreach (var tab in tc.Items.Cast<TabItem>().ToList()) MoveTab(tab, HomeHost(tab));
                SaveLayout();
            }));
            w.LocationChanged += (_, _) => SaveLayoutLater();
            w.SizeChanged += (_, _) => SaveLayoutLater();
            _floatWindows.Add(w);
            FloatWindowCreated?.Invoke(w);
            return w;
        }

        private static bool OnScreen(Rect r)
        {
            var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            var vis = Rect.Intersect(screen, r);
            return !vis.IsEmpty && vis.Width >= 100 && vis.Height >= 50;
        }

        private void UpdateFloatTitle(Window w)
        {
            if (w.Content is TabControl tc)
                w.Title = _title + " – " + string.Join(", ", tc.Items.OfType<TabItem>().Select(t => t.Header as string ?? t.Name));
        }

        /// <summary>Registerkarte in ein neues eigenes Fenster lösen (Position = Bildschirmpunkt in DIP, Fenster etwa unter dem Mauszeiger).</summary>
        public void FloatTab(TabItem tab, Point? at)
        {
            var src = tab.Parent as TabControl;
            var srcWin = src == null ? null : Window.GetWindow(src);
            if (srcWin != null && _floatWindows.Contains(srcWin) && src!.Items.Count == 1)
            {
                // einzige Registerkarte eines eigenen Fensters: nur das Fenster verschieben
                if (at is Point p0) { srcWin.Left = p0.X - 80; srcWin.Top = p0.Y - 12; }
                srcWin.Activate();
                return;
            }
            Rect? bounds = null;
            if (at is Point p)
            {
                var size = src != null && src.ActualWidth > 200 ? new Size(Math.Max(420, src.ActualWidth), Math.Max(400, src.ActualHeight + 40)) : new Size(560, 720);
                bounds = new Rect(p.X - 80, p.Y - 12, size.Width, size.Height);
            }
            var win = CreateFloatWindow(bounds);
            MoveTab(tab, (TabControl)win.Content);
            win.Show();
            win.Activate();
            SaveLayout();
        }

        private void SaveLayout()
        {
            if (_restoring || _ownerClosed || !_restored) return;
            foreach (var tab in _tabs)
            {
                var host = tab.Parent as TabControl;
                string spec;
                if (host == Left) spec = "L";
                else if (host == Right || host == null) spec = "R";
                else
                {
                    var w = Window.GetWindow(host);
                    var b = w == null ? Rect.Empty : w.WindowState == WindowState.Normal ? new Rect(w.Left, w.Top, w.Width, w.Height) : w.RestoreBounds;
                    var home = _home.TryGetValue(tab, out var side) ? side : 'R';
                    spec = b.IsEmpty ? "F" + home
                        : "F" + home + "|" + string.Join(",", new[] { b.Left, b.Top, b.Width, b.Height }.Select(v => Math.Round(v).ToString(CultureInfo.InvariantCulture)))
                          + "|" + (w == null ? 0 : _floatWindows.IndexOf(w));
                }
                Layout[Key(tab)] = spec;
            }
            try { Save?.Invoke(); } catch { }
        }

        private void SaveLayoutLater()
        {
            if (_restoring || _ownerClosed) return;
            if (_saveTimer == null)
            {
                _saveTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
                _saveTimer.Tick += (_, _) => { _saveTimer!.Stop(); SaveLayout(); };
            }
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        private static Rect? ParseBounds(string spec)
        {
            var parts = spec.Split('|');
            if (parts.Length < 2) return null;
            var v = parts[1].Split(',').Select(x => double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : double.NaN).ToArray();
            return v.Length == 4 && v.All(d => !double.IsNaN(d)) ? new Rect(v[0], v[1], v[2], v[3]) : null;
        }

        #region Ziehen wie im Browser

        private void Tab_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Mausereignisse aus dem Inhalt laufen über die Registerkarte mit – nur der Kopf startet das Ziehen
            _dragTab = sender is TabItem tab && IsOnHeader(tab, e.OriginalSource) ? tab : null;
            _dragStart = e.GetPosition(null);
        }

        /// <summary>Liegt das Element im Registerkopf? Der Inhalt hängt optisch am TabControl, nicht an der Registerkarte.</summary>
        private static bool IsOnHeader(TabItem tab, object? source)
        {
            for (var d = source as DependencyObject; d != null;
                 d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
                if (d == tab) return true;
            return false;
        }

        private void Tab_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) { _dragTab = null; return; }
            if (_dragTab == null || _dragTab != sender) return;
            var p = e.GetPosition(null);
            if (Math.Abs(p.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance * 2
                && Math.Abs(p.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance * 2) return;
            var tab = _dragTab;
            _dragTab = null;
            bool cancelled = false;
            QueryContinueDragEventHandler query = (_, a) => { if (a.EscapePressed) cancelled = true; };
            tab.QueryContinueDrag += query;
            _dropped = false;
            try { DragDrop.DoDragDrop(tab, new DataObject(DockFormat, tab), DragDropEffects.Move); }
            catch { return; }
            finally { tab.QueryContinueDrag -= query; }
            if (cancelled || _dropped) return;
            // außerhalb aller eigenen Fenster losgelassen → eigenes Fenster an der Mausposition
            var screen = MouseScreenPixel();
            var windows = new List<Window>(_floatWindows);
            if (_owner != null) windows.Add(_owner);
            if (windows.Any(w => IsOver(w, screen))) return;
            FloatTab(tab, MouseScreenDip());
        }

        private static Point MouseScreenPixel()
        {
            var m = System.Windows.Forms.Control.MousePosition;
            return new Point(m.X, m.Y);
        }

        private Point? MouseScreenDip()
        {
            var px = MouseScreenPixel();
            var src = PresentationSource.FromVisual(_owner ?? (Visual)_view);
            return src?.CompositionTarget != null ? src.CompositionTarget.TransformFromDevice.Transform(px) : px;
        }

        private static bool IsOver(Window w, Point screenPixel)
        {
            if (!w.IsVisible || w.WindowState == WindowState.Minimized) return false;
            try
            {
                var p = w.PointFromScreen(screenPixel);
                return p.X >= 0 && p.Y >= 0 && p.X <= w.ActualWidth && p.Y <= w.ActualHeight;
            }
            catch { return false; }
        }

        private TabItem? DraggedTab(DragEventArgs e) =>
            e.Data.GetDataPresent(DockFormat) && e.Data.GetData(DockFormat) is TabItem t && _tabs.Contains(t) ? t : null;

        private void Host_PreviewDragOver(object sender, DragEventArgs e)
        {
            if (DraggedTab(e) == null) return;
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        private void Host_PreviewDrop(object sender, DragEventArgs e)
        {
            if (DraggedTab(e) is not TabItem tab || sender is not TabControl tc) return;
            e.Handled = true;
            _dropped = true;
            // auf eine Registerkarte gezogen: an deren Stelle einfügen, sonst ans Ende
            var over = FindAncestor<TabItem>(e.OriginalSource as DependencyObject);
            if (over == tab) return;
            int index = over != null && over.Parent == tc ? tc.Items.IndexOf(over) : -1;
            MoveTab(tab, tc, index);
        }

        /// <summary>Auf den Mittelbereich gezogen: linke Hälfte = links andocken, rechte Hälfte = rechts andocken.</summary>
        private void Center_PreviewDrop(object sender, DragEventArgs e)
        {
            if (DraggedTab(e) is not TabItem tab) return;
            e.Handled = true;
            _dropped = true;
            MoveTab(tab, e.GetPosition(_center).X < _center.ActualWidth / 2 ? Left : Right);
        }

        private static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
        {
            while (d != null && d is not T)
                d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            return d as T;
        }

        #endregion
    }
}
