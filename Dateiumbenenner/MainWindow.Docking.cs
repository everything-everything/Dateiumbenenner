using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Web.WebView2.Wpf;
using TabControl = System.Windows.Controls.TabControl;

namespace Dateiumbenenner
{
    /// <summary>Seiten-Registerkarten (Dokument, Werte) wie im INI-Generator: links/rechts andockbar oder in eigenem Fenster.</summary>
    public partial class MainWindow
    {
        private static string DockLayoutFile => Path.Combine(ProfileManager.RootDirectory, "registerkarten.json");
        private TabDockManager? _mainDock;
        private bool _mainDockMoving;
        private WebView2? _pdfWebView;

        /// <summary>WebView2 wird bei Bedarf erzeugt, damit die Registerkarte Dokument zwischen Fenstern verschoben werden kann.</summary>
        private WebView2 PdfWebView
        {
            get
            {
                if (_pdfWebView == null)
                {
                    _pdfWebView = new WebView2 { Visibility = Visibility.Collapsed };
                    PdfViewHost.Children.Insert(0, _pdfWebView);
                }
                return _pdfWebView;
            }
        }

        private void ReleasePdfWebView()
        {
            if (_pdfWebView == null) return;
            PdfViewHost.Children.Remove(_pdfWebView);
            try { _pdfWebView.Dispose(); } catch { }
            _pdfWebView = null;
        }

        private void InitMainDocking()
        {
            _mainDock = new TabDockManager(this, MainDockLeft, MainDockRight, ColMainDockLeft, ColMainDockRight, SplitMainDockLeft, SplitMainDockRight, PnlMainList,
                new[] { TabMainDoc, TabMainValues }, "", "Dateiumbenenner")
            {
                Layout = LoadDockLayout(),
                TabMoving = t => { _mainDockMoving = true; if (t == TabMainDoc) ReleasePdfWebView(); },
                TabsMoved = () => { _mainDockMoving = false; RefreshMainDocPreview(); },
                HostCreated = HookMainDockHost,
                FloatWindowCreated = w => w.PreviewKeyDown += async (_, e) =>
                {
                    if (e.Key == Key.F5 && !e.Handled) { e.Handled = true; await RefreshFolderAsync(); }
                },
            };
            _mainDock.Save = () => SaveDockLayout(_mainDock.Layout);
            HookMainDockHost(MainDockLeft);
            HookMainDockHost(MainDockRight);
        }

        private void HookMainDockHost(TabControl tc) => tc.SelectionChanged += (_, e) =>
        {
            // nach dem Wechsel zurück auf Dokument die PDF-Ansicht neu aufbauen
            if (!_mainDockMoving && e.OriginalSource == tc && e.AddedItems.Contains(TabMainDoc)) RefreshMainDocPreview();
        };

        private void RefreshMainDocPreview()
        {
            if (_currentMeta is { Loaded: true } meta) _ = UpdatePdfPreviewAsync(meta);
        }

        internal static IDictionary<string, string> LoadDockLayout(string? file = null)
        {
            file ??= DockLayoutFile;
            try
            {
                if (File.Exists(file)
                    && JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file)) is { } d)
                    return new Dictionary<string, string>(d, StringComparer.OrdinalIgnoreCase);
            }
            catch { }
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        internal static void SaveDockLayout(IDictionary<string, string> layout, string? file = null)
        {
            file ??= DockLayoutFile;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, JsonSerializer.Serialize(layout));
            }
            catch { }
        }
    }
}
