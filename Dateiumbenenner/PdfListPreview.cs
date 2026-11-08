using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using CheckBox = System.Windows.Controls.CheckBox;
using TabControl = System.Windows.Controls.TabControl;
using TextBox = System.Windows.Controls.TextBox;
using UserControl = System.Windows.Controls.UserControl;

namespace Dateiumbenenner
{
    /// <summary>
    /// Vorschau der in einer Dateiliste gewählten PDF (Registerkarte "Dokument" wie bei Umbenennen):
    /// PDF-Anzeige oder Text mit Suchleiste, links/rechts andockbar oder in eigenem Fenster.
    /// </summary>
    internal sealed class PdfListPreview
    {
        private readonly CheckBox _chkPdf;
        private readonly Grid _host;
        private readonly TextBox _text;
        private readonly TextBlock _hint;
        private readonly Func<string?> _selectedPath;
        private readonly Func<MainWindow?> _mainWindow;
        private readonly TabDockManager _dock;
        private readonly TextSearchBar _search;
        private readonly Dictionary<string, string> _texts = new(StringComparer.OrdinalIgnoreCase);
        private Microsoft.Web.WebView2.Wpf.WebView2? _web;
        private int _version;

        public PdfListPreview(UserControl owner, TabControl left, TabControl right, ColumnDefinition colLeft, ColumnDefinition colRight,
            UIElement splitLeft, UIElement splitRight, FrameworkElement list, TabItem docTab, CheckBox chkPdf, ContentControl searchHost,
            Grid previewHost, TextBox text, TextBlock hint, string layoutFileName, string title, Func<string?> selectedPath, Func<MainWindow?> mainWindow)
        {
            _chkPdf = chkPdf; _host = previewHost; _text = text; _hint = hint;
            _selectedPath = selectedPath; _mainWindow = mainWindow;
            _search = new TextSearchBar(async () =>
            {
                if (_selectedPath() == null) return null;
                if (_text.Visibility != Visibility.Visible) { _chkPdf.IsChecked = false; await ShowAsync(); }
                return _text;
            });
            searchHost.Content = _search;
            chkPdf.Click += (_, _) => Show();
            var layoutFile = Path.Combine(ProfileManager.RootDirectory, layoutFileName);
            _dock = new TabDockManager(owner, left, right, colLeft, colRight, splitLeft, splitRight, list, new[] { docTab }, "", title)
            {
                Layout = MainWindow.LoadDockLayout(layoutFile),
                TabMoving = _ => Release(),
                TabsMoved = Show,
                FloatWindowCreated = w => _search.AttachShortcut(w),
            };
            _dock.Save = () => MainWindow.SaveDockLayout(_dock.Layout, layoutFile);
            // WebView2 nicht im unsichtbaren Zustand umhängen lassen: beim Verlassen freigeben, beim Anzeigen neu aufbauen
            previewHost.Loaded += (_, _) => Show();
            previewHost.Unloaded += (_, _) => owner.Dispatcher.BeginInvoke(() => { if (!_host.IsLoaded) Release(); },
                System.Windows.Threading.DispatcherPriority.Background);
        }

        /// <summary>PDF-Anzeige freigeben (z. B. bevor die Datei gelöscht oder überschrieben wird).</summary>
        public void Release()
        {
            if (_web == null) return;
            _host.Children.Remove(_web);
            try { _web.Dispose(); } catch { }
            _web = null;
        }

        /// <summary>Gemerkten Text einer Datei verwerfen (z. B. nach dem Ändern).</summary>
        public void Forget(string path) => _texts.Remove(path);

        public async void Show()
        {
            try { await ShowAsync(); }
            catch { /* Vorschau ist nur Hilfe */ }
        }

        private async Task ShowAsync()
        {
            if (!_host.IsLoaded) return;
            int version = ++_version;
            var path = _selectedPath();
            if (path == null || !File.Exists(path))
            {
                if (_web != null) _web.Visibility = Visibility.Collapsed;
                _text.Visibility = Visibility.Collapsed;
                _hint.Text = path != null ? "Datei nicht gefunden." : "Datei in der Liste wählen, um sie hier anzuzeigen.";
                _hint.Visibility = Visibility.Visible;
                _search.DocumentChanged("", null, false);
                return;
            }
            _hint.Visibility = Visibility.Collapsed;
            if (_chkPdf.IsChecked == true && path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if (_web == null)
                    {
                        _web = new Microsoft.Web.WebView2.Wpf.WebView2();
                        _host.Children.Add(_web);
                    }
                    var web = _web;
                    web.Visibility = Visibility.Visible;
                    _text.Visibility = Visibility.Collapsed;
                    _search.DocumentChanged(_texts.TryGetValue(path, out var known) ? known : "", null, false);
                    await web.EnsureCoreWebView2Async();
                    if (version != _version || web != _web) return;
                    _search.SetWeb(web);
                    var uri = new Uri(path);
                    if (web.Source != uri) web.Source = uri;
                    return;
                }
                catch { /* WebView2 nicht verfügbar -> Text */ }
            }
            if (_web != null) _web.Visibility = Visibility.Collapsed;
            _text.Visibility = Visibility.Visible;
            var text = await LoadTextAsync(path);
            if (version != _version) return;
            if (string.IsNullOrWhiteSpace(text)) text = "(kein Text gefunden – ggf. gescanntes PDF ohne OCR)";
            if (_text.Text != text) { _text.Text = text; _text.ScrollToHome(); }
            _search.DocumentChanged(text, _text, false);
        }

        /// <summary>Text der Datei: bereits in der Hauptliste geladener (OCR-)Text, sonst aus dem PDF gelesen.</summary>
        public async Task<string> LoadTextAsync(string path)
        {
            if (_texts.TryGetValue(path, out var cached)) return cached;
            var text = _mainWindow()?.GetLoadedText(path);
            if (string.IsNullOrEmpty(text) && path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
            {
                text = await Task.Run(() =>
                {
                    try
                    {
                        if (PdfTextCache.TryGet(path, out var c, out _)) return c;
                        using var doc = UglyToad.PdfPig.PdfDocument.Open(path);
                        var sb = new System.Text.StringBuilder();
                        foreach (var page in doc.GetPages()) sb.AppendLine(Dateiumbenenner.Engine.PdfLayoutReader.PageText(page));
                        return sb.ToString();
                    }
                    catch { return ""; }
                });
            }
            text ??= "";
            _texts[path] = text;
            return text;
        }
    }
}
