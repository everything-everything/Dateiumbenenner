using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using ListView = System.Windows.Controls.ListView;
using Binding = System.Windows.Data.Binding;

namespace Dateiumbenenner
{
    /// <summary>
    /// Sortiert jede ListView mit GridView per Klick auf den Spaltenkopf (auch in Plugin-Tabs).
    /// Erneuter Klick kehrt die Richtung um.
    /// </summary>
    public static class ListViewSorter
    {
        private static bool _registered;

        public static void Register()
        {
            if (_registered) return;
            _registered = true;
            EventManager.RegisterClassHandler(typeof(GridViewColumnHeader), ButtonBase.ClickEvent, new RoutedEventHandler(OnHeaderClick));
        }

        private static void OnHeaderClick(object sender, RoutedEventArgs e)
        {
            if (sender is not GridViewColumnHeader header || header.Column == null || header.Role == GridViewColumnHeaderRole.Padding) return;
            if (header.Tag is string) return;                     // Liste hat eigene Sortierung (z. B. Umbenennen, PDF Zusammenführen)
            var listView = FindParent<ListView>(header);
            if (listView?.ItemsSource == null && listView?.Items == null) return;

            var property = GetSortProperty(header.Column);
            if (string.IsNullOrEmpty(property)) return;

            var view = CollectionViewSource.GetDefaultView(listView!.ItemsSource ?? listView.Items);
            if (view == null || !view.CanSort) return;

            var direction = ListSortDirection.Ascending;
            if (view.SortDescriptions.Count > 0 && view.SortDescriptions[0].PropertyName == property
                && view.SortDescriptions[0].Direction == ListSortDirection.Ascending)
                direction = ListSortDirection.Descending;

            using (view.DeferRefresh())
            {
                view.SortDescriptions.Clear();
                view.SortDescriptions.Add(new SortDescription(property, direction));
            }

            if (listView.View is GridView gv)
                foreach (var c in gv.Columns)
                    if (c.Header is string h) c.Header = h.TrimEnd(' ', '▲', '▼');
            if (header.Column.Header is string text)
                header.Column.Header = text.TrimEnd(' ', '▲', '▼') + (direction == ListSortDirection.Ascending ? " ▲" : " ▼");
            e.Handled = true;
        }

        /// <summary>Eigenschaftsname aus DisplayMemberBinding oder aus der ersten Bindung im Zellen-Template.</summary>
        private static string? GetSortProperty(GridViewColumn column)
        {
            if (column.DisplayMemberBinding is Binding b && !string.IsNullOrEmpty(b.Path?.Path)) return b.Path.Path;
            if (column.CellTemplate?.LoadContent() is DependencyObject root) return FindBindingPath(root);
            return null;
        }

        private static string? FindBindingPath(DependencyObject element)
        {
            var enumerator = element.GetLocalValueEnumerator();
            while (enumerator.MoveNext())
            {
                var path = BindingOperations.GetBinding(element, enumerator.Current.Property)?.Path?.Path;
                if (!string.IsNullOrEmpty(path) && enumerator.Current.Property.Name is "Text" or "SelectedItem" or "IsChecked" or "Content")
                    return path;
            }
            foreach (var child in LogicalTreeHelper.GetChildren(element))
                if (child is DependencyObject d && FindBindingPath(d) is string p) return p;
            return null;
        }

        private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            var current = VisualTreeHelper.GetParent(child);
            while (current != null && current is not T) current = VisualTreeHelper.GetParent(current);
            return current as T;
        }
    }
}
