using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using ListView = System.Windows.Controls.ListView;
using ListViewItem = System.Windows.Controls.ListViewItem;
using TextBox = System.Windows.Controls.TextBox;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using Binding = System.Windows.Data.Binding;
using Clipboard = System.Windows.Clipboard;
using Keyboard = System.Windows.Input.Keyboard;
using ModifierKeys = System.Windows.Input.ModifierKeys;
using Separator = System.Windows.Controls.Separator;

namespace Dateiumbenenner
{
    /// <summary>Word-/Explorer-typisch: Strg+C, Strg+A und Kontextmenü (Kopieren) für ListViews mit GridView.</summary>
    public static class ListViewCopyHelper
    {
        public static void Attach(ListView lv)
        {
            lv.PreviewKeyDown += (s, e) =>
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
                if (e.OriginalSource is TextBox) return; // Editierfelder behalten ihr Kopieren
                if (e.Key == Key.C) { CopyRows(lv); e.Handled = true; }
                else if (e.Key == Key.A) { lv.SelectAll(); e.Handled = true; }
            };

            var menu = new ContextMenu();
            menu.Items.Add(Item("Kopieren (Zeilen)", "Strg+C", () => CopyRows(lv)));
            menu.Items.Add(Item("Dateinamen kopieren", null, () => CopyProperty(lv, "FileName")));
            menu.Items.Add(Item("Dateipfade kopieren", null, () => CopyProperty(lv, "FilePath")));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Alles markieren", "Strg+A", lv.SelectAll));
            lv.ContextMenu = menu;

            // Rechtsklick auf eine nicht markierte Zeile markiert diese (wie im Explorer)
            lv.PreviewMouseRightButtonDown += (s, e) =>
            {
                var item = FindParent<ListViewItem>(e.OriginalSource as DependencyObject);
                if (item != null && !item.IsSelected) { lv.SelectedItems.Clear(); item.IsSelected = true; }
            };
        }

        private static MenuItem Item(string header, string? gesture, Action action)
        {
            var mi = new MenuItem { Header = header, InputGestureText = gesture ?? string.Empty };
            mi.Click += (_, _) => action();
            return mi;
        }

        private static List<object> Selected(ListView lv) =>
            lv.Items.Cast<object>().Where(i => lv.SelectedItems.Contains(i)).ToList();

        private static void CopyRows(ListView lv)
        {
            var rows = Selected(lv);
            if (rows.Count == 0) return;
            var cols = (lv.View as GridView)?.Columns.ToList() ?? new List<GridViewColumn>();
            var sb = new StringBuilder();
            if (cols.Count > 0)
            {
                sb.AppendLine(string.Join("\t", cols.Select(c => HeaderText(c.Header))));
                foreach (var r in rows)
                    sb.AppendLine(string.Join("\t", cols.Select(c => Value(r, (c.DisplayMemberBinding as Binding)?.Path?.Path))));
            }
            else foreach (var r in rows) sb.AppendLine(r?.ToString());
            SetClipboard(sb.ToString());
        }

        private static void CopyProperty(ListView lv, string property) =>
            SetClipboard(string.Join(Environment.NewLine, Selected(lv).Select(r => Value(r, property))));

        private static string HeaderText(object? header) => header switch
        {
            GridViewColumnHeader h => h.Content?.ToString() ?? string.Empty,
            null => string.Empty,
            _ => header.ToString() ?? string.Empty
        };

        private static string Value(object? row, string? path)
        {
            if (row == null || string.IsNullOrEmpty(path)) return string.Empty;
            var v = row.GetType().GetProperty(path)?.GetValue(row);
            return (v?.ToString() ?? string.Empty).Replace('\t', ' ').Replace("\r", " ").Replace("\n", " ");
        }

        private static void SetClipboard(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            try { System.Windows.Clipboard.SetText(text); } catch { }
        }

        private static T? FindParent<T>(DependencyObject? d) where T : DependencyObject
        {
            while (d != null && d is not T) d = d is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            return d as T;
        }
    }
}
