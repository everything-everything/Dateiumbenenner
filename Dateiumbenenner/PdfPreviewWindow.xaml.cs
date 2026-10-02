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
    }
}
