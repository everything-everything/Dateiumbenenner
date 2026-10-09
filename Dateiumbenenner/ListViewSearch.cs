using System;
using System.Collections;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ListView = System.Windows.Controls.ListView;
using TextBox = System.Windows.Controls.TextBox;
using Panel = System.Windows.Controls.Panel;
using DockPanel = System.Windows.Controls.DockPanel;
using Orientation = System.Windows.Controls.Orientation;
using Binding = System.Windows.Data.Binding;
using Brushes = System.Windows.Media.Brushes;

namespace Dateiumbenenner
{
    /// <summary>
    /// Blendet über jeder Dateiliste (ListView mit GridView, auch in Plugin-Tabs) ein Suchfeld ein.
    /// Gesucht wird in allen Textspalten (Teilbegriff, Groß-/Kleinschreibung egal; mehrere Wörter = alle müssen vorkommen).
    /// </summary>
    public static class ListViewSearch
    {
        private static bool _registered;

        public static void Register()
        {
            if (_registered) return;
            _registered = true;
            EventManager.RegisterClassHandler(typeof(ListView), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnLoaded));
        }

        private static readonly DependencyProperty AttachedProperty =
            DependencyProperty.RegisterAttached("SearchAttached", typeof(bool), typeof(ListViewSearch));

        private static void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not ListView lv || lv.View is not GridView || (bool)lv.GetValue(AttachedProperty)) return;
            if (lv.Name is "LvFiles" or "LvIniFiles") return;      // haben bereits ein eigenes Filterfeld
            if (LogicalTreeHelper.GetParent(lv) is not Panel parent) return;
            lv.SetValue(AttachedProperty, true);

            var box = new TextBox { Margin = new Thickness(lv.Margin.Left, lv.Margin.Top, lv.Margin.Right, 4), ToolTip = "In der Liste suchen (alle Spalten)" };
            var hint = new TextBlock { Text = "🔍 Suchen …", Foreground = Brushes.Gray, IsHitTestVisible = false, Margin = new Thickness(lv.Margin.Left + 5, lv.Margin.Top + 2, 0, 0) };
            var boxHost = new Grid();
            boxHost.Children.Add(box); boxHost.Children.Add(hint);
            DockPanel.SetDock(boxHost, Dock.Top);

            // ListView durch DockPanel (Suchfeld + Liste) ersetzen, Layout-Eigenschaften übernehmen
            int index = parent.Children.IndexOf(lv);
            parent.Children.RemoveAt(index);
            var host = new DockPanel { LastChildFill = true };
            foreach (var dp in new[] { Grid.RowProperty, Grid.ColumnProperty, Grid.RowSpanProperty, Grid.ColumnSpanProperty, DockPanel.DockProperty })
            {
                var v = lv.ReadLocalValue(dp);
                if (v != DependencyProperty.UnsetValue) { host.SetValue(dp, v); lv.ClearValue(dp); }
            }
            lv.Margin = new Thickness(lv.Margin.Left, 0, lv.Margin.Right, lv.Margin.Bottom);
            host.Children.Add(boxHost);
            host.Children.Add(lv);
            parent.Children.Insert(index, host);

            box.TextChanged += (_, _) =>
            {
                hint.Visibility = string.IsNullOrEmpty(box.Text) ? Visibility.Visible : Visibility.Collapsed;
                ApplyFilter(lv, box.Text);
            };
            // Neue ItemsSource (z. B. nach "Neu einlesen") erneut filtern
            DependencyPropertyDescriptor.FromProperty(ItemsControl.ItemsSourceProperty, typeof(ListView))
                .AddValueChanged(lv, (_, _) => ApplyFilter(lv, box.Text));
        }

        private static void ApplyFilter(ListView lv, string text)
        {
            var view = CollectionViewSource.GetDefaultView(lv.ItemsSource ?? (IEnumerable)lv.Items);
            if (view == null || !view.CanFilter) return;
            var words = (text ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) { view.Filter = null; return; }
            var paths = Columns(lv);
            view.Filter = item =>
            {
                if (item == null) return false;
                var values = Values(item, paths);
                return words.All(w => values.Any(v => v.Contains(w, StringComparison.OrdinalIgnoreCase)));
            };
        }

        /// <summary>Eigenschaftsnamen der angezeigten Spalten (DisplayMemberBinding bzw. Bindung im Zellen-Template).</summary>
        private static string[] Columns(ListView lv)
        {
            if (lv.View is not GridView gv) return Array.Empty<string>();
            return gv.Columns.Select(c => c.DisplayMemberBinding is Binding b ? b.Path?.Path
                                       : c.CellTemplate?.LoadContent() is DependencyObject d ? FirstBinding(d) : null)
                             .Where(p => !string.IsNullOrEmpty(p)).Distinct().ToArray()!;
        }

        private static string? FirstBinding(DependencyObject element)
        {
            var en = element.GetLocalValueEnumerator();
            while (en.MoveNext())
                if (en.Current.Property.Name is "Text" or "Content" or "SelectedItem"
                    && BindingOperations.GetBinding(element, en.Current.Property)?.Path?.Path is string p && p.Length > 0) return p;
            foreach (var child in LogicalTreeHelper.GetChildren(element))
                if (child is DependencyObject d && FirstBinding(d) is string r) return r;
            return null;
        }

        private static string[] Values(object item, string[] paths)
        {
            var type = item.GetType();
            var props = paths.Length > 0
                ? paths.Select(p => type.GetProperty(p)).Where(p => p != null)
                : type.GetProperties().Where(p => p.PropertyType == typeof(string));
            return props.Select(p => { try { return Convert.ToString(p!.GetValue(item)) ?? string.Empty; } catch { return string.Empty; } })
                        .ToArray();
        }
    }
}
