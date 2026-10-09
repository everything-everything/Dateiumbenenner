using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Dateiumbenenner
{
    public partial class PdfPreviewWindow : Window
    {
        private string? _currentFilePath;
        private string? _fullText;
        private MainWindow? _mainWindow;

        public PdfPreviewWindow()
        {
            InitializeComponent();
            
            // Event-Handler für das Schließen des Fensters
            this.Closed += (s, e) =>
            {
                if (_mainWindow != null)
                {
                    _mainWindow.PreviewWindowClosed();
                }
            };
        }

        public void SetMainWindow(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
        }

        public async Task LoadPdfAsync(string filePath, string? fullText = null)
        {
            _currentFilePath = filePath;
            _fullText = fullText;
            
            TxtFileName.Text = Path.GetFileName(filePath);
            
            await UpdatePdfPreviewAsync();
        }

        private async Task UpdatePdfPreviewAsync()
        {
            if (string.IsNullOrEmpty(_currentFilePath))
                return;

            // Verstecke alle Vorschau-Elemente zunächst
            PdfWebView.Visibility = Visibility.Collapsed;
            ImgPdfPreview.Visibility = Visibility.Collapsed;
            PdfTextScroll.Visibility = Visibility.Collapsed;

            var ext = Path.GetExtension(_currentFilePath).ToLowerInvariant();
            if (ext != ".pdf")
            {
                // Kein PDF -> zeige Text-Vorschau
                TxtPdfFallback.Text = _fullText ?? "(kein Text verfügbar)";
                PdfTextScroll.Visibility = Visibility.Visible;
                return;
            }

            // Bestimme den Vorschau-Modus
            var selectedMode = (CmbPdfPreviewMode.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Auto";

            try
            {
                switch (selectedMode)
                {
                    case "WebView2":
                        await ShowPdfInWebView2(_currentFilePath);
                        break;

                    case "Text":
                        ShowPdfAsText();
                        break;

                    case "Auto":
                    default:
                        // Auto: Versuche WebView2, dann Text
                        if (await TryShowPdfInWebView2(_currentFilePath))
                            break;
                        ShowPdfAsText();
                        break;
                }
            }
            catch (Exception ex)
            {
                TxtPdfFallback.Text = $"Fehler bei der PDF-Vorschau:\n{ex.Message}\n\nText-Vorschau:\n{_fullText}";
                PdfTextScroll.Visibility = Visibility.Visible;
            }
        }

        private async Task<bool> TryShowPdfInWebView2(string filePath)
        {
            try
            {
                await ShowPdfInWebView2(filePath);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private async Task ShowPdfInWebView2(string filePath)
        {
            try
            {
                // Stelle sicher, dass WebView2 initialisiert ist
                await PdfWebView.EnsureCoreWebView2Async();
                
                // Navigiere zur PDF-Datei
                PdfWebView.Source = new Uri(filePath);
                PdfWebView.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"WebView2 nicht verfügbar: {ex.Message}", ex);
            }
        }

        private void ShowPdfAsText()
        {
            TxtPdfFallback.Text = _fullText ?? "(kein Text verfügbar)";
            PdfTextScroll.Visibility = Visibility.Visible;
        }

        private async void CmbPdfPreviewMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            await UpdatePdfPreviewAsync();
        }

        private void TxtSearch_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != System.Windows.Input.Key.Enter) return;
            Search((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) == 0);
            e.Handled = true;
        }

        private void BtnSearchNext_Click(object sender, RoutedEventArgs e) => Search(true);
        private void BtnSearchPrev_Click(object sender, RoutedEventArgs e) => Search(false);

        /// <summary>Sucht in der Text-Ansicht; schaltet bei Bedarf von der PDF-Ansicht auf Text um.</summary>
        private void Search(bool forward)
        {
            var term = TxtSearch.Text;
            if (string.IsNullOrEmpty(term)) { TxtSearchInfo.Text = string.Empty; return; }
            if (PdfTextScroll.Visibility != Visibility.Visible)
            {
                CmbPdfPreviewMode.SelectedIndex = 2;
                ShowPdfAsText();
                PdfWebView.Visibility = Visibility.Collapsed;
            }
            var box = TxtPdfFallback;
            var text = box.Text ?? string.Empty;
            int pos;
            if (forward)
            {
                int start = box.SelectionLength > 0 ? box.SelectionStart + 1 : box.CaretIndex;
                pos = start < text.Length ? text.IndexOf(term, start, StringComparison.CurrentCultureIgnoreCase) : -1;
                if (pos < 0) pos = text.IndexOf(term, StringComparison.CurrentCultureIgnoreCase);
            }
            else
            {
                int start = box.SelectionStart - 1;
                pos = start >= 0 ? text.LastIndexOf(term, start, StringComparison.CurrentCultureIgnoreCase) : -1;
                if (pos < 0) pos = text.LastIndexOf(term, StringComparison.CurrentCultureIgnoreCase);
            }
            if (pos < 0) { TxtSearchInfo.Text = "Nicht gefunden"; return; }
            int count = 0, idx = 0, current = 0;
            while ((idx = text.IndexOf(term, idx, StringComparison.CurrentCultureIgnoreCase)) >= 0)
            {
                count++;
                if (idx == pos) current = count;
                idx += term.Length;
            }
            TxtSearchInfo.Text = $"{current} / {count}";
            box.Select(pos, term.Length);
            box.UpdateLayout();
            var rect = box.GetRectFromCharacterIndex(pos);
            if (!rect.IsEmpty)
            {
                var top = box.TranslatePoint(rect.TopLeft, PdfTextScroll).Y + PdfTextScroll.VerticalOffset;
                PdfTextScroll.ScrollToVerticalOffset(Math.Max(0, top - PdfTextScroll.ViewportHeight / 3));
            }
        }
    }
}
