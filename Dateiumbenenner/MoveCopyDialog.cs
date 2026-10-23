using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Dateiumbenenner.Engine;
using Binding = System.Windows.Data.Binding;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using ListView = System.Windows.Controls.ListView;
using MessageBoxWpf = System.Windows.MessageBox;
using Orientation = System.Windows.Controls.Orientation;
using RadioButton = System.Windows.Controls.RadioButton;
using TextBox = System.Windows.Controls.TextBox;

namespace Dateiumbenenner
{
    /// <summary>„Markierte verschieben / kopieren“: Ziel wählen und optional die zugehörigen XML-Belegdefinitionen
    /// (alle Erkennungsmerkmale stehen im Beleg) in einen eigenen Ordner mitnehmen.</summary>
    public class MoveCopyDialog : Window
    {
        public class XmlRow : INotifyPropertyChanged
        {
            private bool _selected = true;
            public XmlDefinition Definition { get; init; } = null!;
            public bool Selected { get => _selected; set { _selected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected))); Changed?.Invoke(); } }
            public string FileName => Definition.FileName;
            public string Company => Definition.Company;
            public string Category => Definition.Category;
            public int Matches { get; init; }
            /// <summary>Weitere (nicht markierte) Belege der Liste, zu denen die Definition ebenfalls passt.</summary>
            public int Others { get; init; }
            public string Features => Definition.Features.Count > 0 ? string.Join(" | ", Definition.Features) : "(keine – über Firmenname)";
            public string Examples { get; init; } = "";
            internal Action? Changed;
            public event PropertyChangedEventHandler? PropertyChanged;
        }

        private readonly IReadOnlyList<DocumentMetadata> _selected;
        private readonly IReadOnlyList<DocumentMetadata> _all;
        private readonly string? _currentFolder;
        private readonly RadioButton _rbMove, _rbCopy, _rbXmlCopy, _rbXmlMove;
        private readonly TextBox _target, _xmlSource, _xmlTarget;
        private readonly CheckBox _chkXml;
        private readonly TextBlock _xmlInfo;
        private readonly ObservableCollection<XmlRow> _rows = new();
        private readonly FrameworkElement _xmlPanel;
        private Dictionary<DocumentMetadata, string>? _texts;
        private bool _xmlTargetEdited;
        private int _scanVersion;

        public bool Move => _rbMove.IsChecked == true;
        public string Target => _target.Text.Trim();
        public bool WithXml => _chkXml.IsChecked == true;
        public string XmlTarget => _xmlTarget.Text.Trim();
        public bool XmlMove => _rbXmlMove.IsChecked == true;
        /// <summary>Gewählte XML-Definitionen; Shared = wird noch von weiteren Belegen der Liste genutzt (dann nur kopieren).</summary>
        public List<(string Path, bool Shared)> XmlFiles => _rows.Where(r => r.Selected).Select(r => (r.Definition.Path, r.Others > 0)).ToList();

        public MoveCopyDialog(IReadOnlyList<DocumentMetadata> selected, IReadOnlyList<DocumentMetadata> all, string? currentFolder,
            string? lastTarget, bool move, bool withXml, bool xmlMove)
        {
            _selected = selected; _all = all; _currentFolder = currentFolder;
            Title = "Markierte verschieben / kopieren";
            Width = 1050; Height = 640;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var root = new DockPanel { Margin = new Thickness(10) };
            var head = new StackPanel();
            DockPanel.SetDock(head, Dock.Top);
            head.Children.Add(new TextBlock
            {
                Text = $"{selected.Count:N0} markierte Datei(en). Gleichnamige .txt/.ini/.xml werden immer mitgenommen.",
                FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6)
            });
            var action = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            _rbMove = new RadioButton { Content = "Verschieben", GroupName = "act", IsChecked = move, Margin = new Thickness(0, 0, 16, 0) };
            _rbCopy = new RadioButton { Content = "Kopieren", GroupName = "act", IsChecked = !move };
            action.Children.Add(_rbMove); action.Children.Add(_rbCopy);
            head.Children.Add(action);
            _target = new TextBox { Text = lastTarget ?? "" };
            head.Children.Add(FolderRow("Zielordner:", _target, "Zielordner für die Belege", new Guid("7C4D2E91-0A3B-4F6E-8D15-2B9E6C1F4A73")));

            _chkXml = new CheckBox
            {
                Content = "Zugehörige XML-Belegdefinitionen (BE_….xml) mitnehmen", IsChecked = withXml, Margin = new Thickness(0, 10, 0, 4),
                ToolTip = "Eine Definition gehört zu einem Beleg, wenn alle ihre Erkennungsmerkmale im Belegtext stehen (ohne Merkmale: gleiche Firma)."
            };
            head.Children.Add(_chkXml);
            root.Children.Add(head);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            DockPanel.SetDock(buttons, Dock.Bottom);
            var ok = new Button { Content = "Ausführen", Padding = new Thickness(12, 2, 12, 2), Margin = new Thickness(0, 0, 6, 0), IsDefault = true };
            ok.Click += (_, _) => { if (Validate()) DialogResult = true; };
            var cancel = new Button { Content = "Abbrechen", Padding = new Thickness(12, 2, 12, 2), IsCancel = true };
            buttons.Children.Add(ok); buttons.Children.Add(cancel);
            root.Children.Add(buttons);

            var xml = new DockPanel();
            _xmlPanel = xml;
            var xmlHead = new StackPanel();
            DockPanel.SetDock(xmlHead, Dock.Top);
            _xmlSource = new TextBox { Text = string.IsNullOrEmpty(currentFolder) ? "" : XmlDefinitions.DefaultSourceFolder(currentFolder) };
            xmlHead.Children.Add(FolderRow("XML-Quelle:", _xmlSource, "Ordner mit den XML-Belegdefinitionen", new Guid("5E2A9C47-1B6D-4E83-A0F2-8C3D7B1E9A64")));
            _xmlTarget = new TextBox();
            xmlHead.Children.Add(FolderRow("XML-Ziel:", _xmlTarget, "Zielordner für die XML-Belegdefinitionen", new Guid("A1F37B2C-6D48-4C95-B0E7-3F9D2A6C8E15")));
            var xmlAction = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(90, 2, 0, 6) };
            _rbXmlCopy = new RadioButton { Content = "XML kopieren", GroupName = "xml", IsChecked = !xmlMove, Margin = new Thickness(0, 0, 16, 0) };
            _rbXmlMove = new RadioButton
            {
                Content = "XML verschieben", GroupName = "xml", IsChecked = xmlMove,
                ToolTip = "Definitionen, die noch zu weiteren (nicht markierten) Belegen der Liste passen, werden trotzdem nur kopiert."
            };
            xmlAction.Children.Add(_rbXmlCopy); xmlAction.Children.Add(_rbXmlMove);
            xmlHead.Children.Add(xmlAction);
            xml.Children.Add(xmlHead);

            _xmlInfo = new TextBlock { Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
            DockPanel.SetDock(_xmlInfo, Dock.Bottom);
            xml.Children.Add(_xmlInfo);

            var lv = new ListView { ItemsSource = _rows };
            var gv = new GridView();
            gv.Columns.Add(new GridViewColumn { Header = "", Width = 30, CellTemplate = CheckTemplate(nameof(XmlRow.Selected)) });
            gv.Columns.Add(new GridViewColumn { Header = "XML-Datei", Width = 300, DisplayMemberBinding = new Binding(nameof(XmlRow.FileName)) });
            gv.Columns.Add(new GridViewColumn { Header = "Firma", Width = 170, DisplayMemberBinding = new Binding(nameof(XmlRow.Company)) });
            gv.Columns.Add(new GridViewColumn { Header = "Kategorie", Width = 110, DisplayMemberBinding = new Binding(nameof(XmlRow.Category)) });
            gv.Columns.Add(new GridViewColumn { Header = "Markierte Belege", Width = 105, DisplayMemberBinding = new Binding(nameof(XmlRow.Matches)) });
            gv.Columns.Add(new GridViewColumn { Header = "Weitere in Liste", Width = 100, DisplayMemberBinding = new Binding(nameof(XmlRow.Others)) });
            gv.Columns.Add(new GridViewColumn { Header = "Erkennungsmerkmale", Width = 260, DisplayMemberBinding = new Binding(nameof(XmlRow.Features)) });
            lv.View = gv;
            var itemStyle = new Style(typeof(System.Windows.Controls.ListViewItem));
            itemStyle.Setters.Add(new Setter(ToolTipProperty, new Binding(nameof(XmlRow.Examples))));
            lv.ItemContainerStyle = itemStyle;
            ListViewCopyHelper.Attach(lv);
            lv.KeyDown += (_, e) =>
            {
                if (e.Key != System.Windows.Input.Key.Space) return;
                foreach (XmlRow r in lv.SelectedItems) r.Selected = !r.Selected;
                e.Handled = true;
            };
            xml.Children.Add(lv);
            root.Children.Add(xml);
            Content = root;

            _target.TextChanged += (_, _) => { if (!_xmlTargetEdited) SetXmlTargetDefault(); };
            _xmlTarget.TextChanged += (_, _) => { if (_xmlTarget.IsKeyboardFocusWithin) _xmlTargetEdited = true; };
            _xmlSource.LostFocus += (_, _) => _ = ScanAsync();
            _chkXml.Click += (_, _) => { UpdateXmlPanel(); if (WithXml && _rows.Count == 0) _ = ScanAsync(); };
            _rbMove.Checked += (_, _) => UpdateInfo();
            _rbCopy.Checked += (_, _) => UpdateInfo();
            _rbXmlMove.Checked += (_, _) => UpdateInfo();
            _rbXmlCopy.Checked += (_, _) => UpdateInfo();
            SetXmlTargetDefault();
            UpdateXmlPanel();
            Loaded += (_, _) => { if (WithXml) _ = ScanAsync(); };
        }

        private FrameworkElement FolderRow(string label, TextBox box, string description, Guid clientGuid)
        {
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            var lbl = new TextBlock { Text = label, Width = 90, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(lbl, Dock.Left);
            row.Children.Add(lbl);
            var pick = new Button { Content = "…", Width = 30, Margin = new Thickness(6, 0, 0, 0), ToolTip = "Ordner wählen" };
            DockPanel.SetDock(pick, Dock.Right);
            pick.Click += (_, _) =>
            {
                var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = description, UseDescriptionForTitle = true, ClientGuid = clientGuid };
                if (Directory.Exists(box.Text.Trim())) dlg.InitialDirectory = box.Text.Trim();
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                if (box == _xmlTarget) _xmlTargetEdited = true;
                box.Text = dlg.SelectedPath;
                if (box == _xmlSource) _ = ScanAsync();
            };
            row.Children.Add(pick);
            row.Children.Add(box);
            return row;
        }

        private void SetXmlTargetDefault() => _xmlTarget.Text = Target.Length == 0 ? "" : Path.Combine(Target, "XML");

        private void UpdateXmlPanel()
        {
            _xmlPanel.IsEnabled = WithXml;
            _xmlPanel.Opacity = WithXml ? 1 : 0.5;
            UpdateInfo();
        }

        private async Task ScanAsync()
        {
            if (!WithXml) return;
            int version = ++_scanVersion;
            var folder = _xmlSource.Text.Trim();
            _rows.Clear();
            if (!Directory.Exists(folder)) { _xmlInfo.Text = "XML-Quellordner nicht gefunden."; return; }
            _xmlInfo.Text = "Suche zugehörige XML-Definitionen …";
            var selected = _selected; var all = _all;
            var known = _texts;
            var (defs, texts, rows) = await Task.Run(() =>
            {
                var d = XmlDefinitions.Load(folder);
                var t = known ?? BuildTexts(all.Concat(selected).Distinct().ToList());
                var sel = new HashSet<DocumentMetadata>(selected);
                var others = all.Where(m => !sel.Contains(m)).ToList();
                var result = new List<XmlRow>();
                foreach (var def in d)
                {
                    var hits = selected.Where(m => XmlDefinitions.Matches(def, t.GetValueOrDefault(m, ""), m.CompanyName)).ToList();
                    if (hits.Count == 0) continue;
                    int more = others.Count(m => XmlDefinitions.Matches(def, t.GetValueOrDefault(m, ""), m.CompanyName));
                    var examples = string.Join("\n", hits.Take(15).Select(m => m.FileName)) + (hits.Count > 15 ? $"\n… und {hits.Count - 15} weitere" : "");
                    result.Add(new XmlRow { Definition = def, Matches = hits.Count, Others = more, Examples = "Passt zu:\n" + examples });
                }
                return (d, t, result);
            });
            if (version != _scanVersion) return;
            _texts = texts;
            foreach (var r in rows) { r.Changed = UpdateInfo; _rows.Add(r); }
            _found = defs.Count;
            UpdateInfo();
        }

        private int _found = -1;

        /// <summary>Belegtext je Beleg (normalisiert); fehlt er, die gleichnamige TXT lesen.</summary>
        private static Dictionary<DocumentMetadata, string> BuildTexts(List<DocumentMetadata> items)
        {
            var arr = new string[items.Count];
            Parallel.For(0, items.Count, i =>
            {
                var m = items[i];
                var text = m.FullText;
                if (string.IsNullOrEmpty(text))
                {
                    try
                    {
                        var txt = !string.IsNullOrEmpty(m.TextSourcePath) ? m.TextSourcePath : Path.ChangeExtension(m.FilePath, ".txt");
                        if (File.Exists(txt)) text = File.ReadAllText(txt);
                    }
                    catch { }
                }
                arr[i] = XmlDefinitions.Normalize(text);
            });
            var d = new Dictionary<DocumentMetadata, string>();
            for (int i = 0; i < items.Count; i++) d[items[i]] = arr[i];
            return d;
        }

        private void UpdateInfo()
        {
            if (_xmlInfo == null) return;
            if (!WithXml) { _xmlInfo.Text = "XML-Belegdefinitionen werden nicht mitgenommen."; return; }
            if (_found < 0) return;
            int sel = _rows.Count(r => r.Selected);
            var text = _rows.Count == 0
                ? $"Keine der {_found} XML-Definitionen im Quellordner passt zu den markierten Belegen."
                : $"{_rows.Count} von {_found} XML-Definitionen passen zu den markierten Belegen, {sel} ausgewählt (Tooltip: passende Belege).";
            int shared = _rows.Count(r => r.Selected && r.Others > 0);
            if (XmlMove && shared > 0)
                text += $" {shared} davon passen noch zu weiteren Belegen der Liste und werden nur kopiert.";
            _xmlInfo.Text = text;
        }

        private bool Validate()
        {
            if (Target.Length == 0) { MessageBoxWpf.Show("Bitte einen Zielordner wählen.", Title); return false; }
            if (!string.IsNullOrEmpty(_currentFolder) && SameFolder(Target, _currentFolder))
            { MessageBoxWpf.Show("Der Zielordner ist der aktuelle Ordner.", Title); return false; }
            if (WithXml && _rows.Any(r => r.Selected))
            {
                if (XmlTarget.Length == 0) { MessageBoxWpf.Show("Bitte einen Zielordner für die XML-Definitionen wählen.", Title); return false; }
                if (SameFolder(XmlTarget, _xmlSource.Text.Trim()))
                { MessageBoxWpf.Show("XML-Quelle und XML-Ziel sind derselbe Ordner.", Title); return false; }
            }
            try { Directory.CreateDirectory(Target); }
            catch (Exception ex) { MessageBoxWpf.Show("Zielordner kann nicht angelegt werden: " + ex.Message, Title); return false; }
            return true;
        }

        private static bool SameFolder(string a, string b)
        {
            try { return string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        private static DataTemplate CheckTemplate(string path)
        {
            var chk = new FrameworkElementFactory(typeof(CheckBox));
            chk.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,
                new Binding(path) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            return new DataTemplate { VisualTree = chk };
        }
    }
}
