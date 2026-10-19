using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

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
            _marks.Clear();
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
                ScheduleDecorations();
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
                ScheduleDecorations();
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
            ScheduleDecorations();
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

        /// <summary>Fundstelle im Text markieren (schaltet auf die Text-Ansicht um, damit die Stelle sichtbar ist).</summary>
        public void ShowTextAt(int index, int length) => ShowTextAt(index, length, -1, 0);

        /// <summary>
        /// Fundstelle farbig hinterlegen wie im XML-Editor: Zeile hellblau, Begriff orange, Wert türkis.
        /// </summary>
        public void ShowTextAt(int index, int length, int labelIndex, int labelLength)
            => ShowTextMarks(new[] { (index, length, labelIndex, labelLength) }, 0);

        /// <summary>Beleg direkt in der Text-Ansicht oeffnen (ohne PDF-Ansicht vorher zu laden) und Fundstellen markieren.</summary>
        public void ShowDocumentText(string filePath, string? fullText, IReadOnlyList<(int Index, int Length, int LabelIndex, int LabelLength)> marks, int focus = 0)
        {
            _currentFilePath = filePath;
            _fullText = fullText;
            TxtFileName.Text = Path.GetFileName(filePath);
            ShowTextMarks(marks, focus);
        }

        /// <summary>Mehrere Fundstellen markieren; zur Fundstelle <paramref name="focus"/> scrollen.</summary>
        public void ShowTextMarks(IReadOnlyList<(int Index, int Length, int LabelIndex, int LabelLength)> marks, int focus = 0)
        {
            CmbPdfPreviewMode.SelectedIndex = 2;
            ShowPdfAsText();
            PdfWebView.Visibility = Visibility.Collapsed;
            ImgPdfPreview.Visibility = Visibility.Collapsed;
            var box = TxtPdfFallback;
            int max = box.Text?.Length ?? 0;
            _marks.Clear();
            (int Index, int Length, int LabelIndex, int LabelLength)? target = null;
            for (int i = 0; i < marks.Count; i++)
            {
                var m = marks[i];
                if (m.Index < 0 || m.Index + m.Length > max) continue;
                if (m.LabelIndex < 0 || m.LabelIndex + m.LabelLength > max) m = (m.Index, m.Length, -1, 0);
                _marks.Add(m);
                if (i == focus || target == null) target = m;
            }
            box.UpdateLayout();
            RenderDecorations();
            if (target is not { } t) return;
            box.Select(t.Index, 0);
            var rect = box.GetRectFromCharacterIndex(t.LabelIndex >= 0 ? Math.Min(t.Index, t.LabelIndex) : t.Index);
            if (!rect.IsEmpty)
            {
                var top = box.TranslatePoint(rect.TopLeft, PdfTextScroll).Y + PdfTextScroll.VerticalOffset;
                PdfTextScroll.ScrollToVerticalOffset(Math.Max(0, top - PdfTextScroll.ViewportHeight / 3));
            }
        }

        private static readonly Brush LineBrush = new SolidColorBrush(Color.FromRgb(0xDC, 0xEA, 0xFB));
        private static readonly Brush TermBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xC8, 0x80));
        private static readonly Brush HitBrush = Brushes.Aquamarine;
        private readonly List<(int Index, int Length, int LabelIndex, int LabelLength)> _marks = new();
        private bool _decorationsPending;

        private void TxtPdfFallback_SizeChanged(object sender, SizeChangedEventArgs e) => ScheduleDecorations();

        private void ScheduleDecorations()
        {
            if (_decorationsPending) return;
            _decorationsPending = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(RenderDecorations));
        }

        /// <summary>Zeilennummern (je Textzeile, auch bei Umbruch) und Markierungen der Fundstelle neu zeichnen.</summary>
        private void RenderDecorations()
        {
            _decorationsPending = false;
            LineGutter.Children.Clear();
            MarkLayer.Children.Clear();
            if (PdfTextScroll.Visibility != Visibility.Visible) return;
            var box = TxtPdfFallback;
            var text = box.Text ?? string.Empty;
            var starts = new List<int> { 0 };
            for (int i = 0; i < text.Length; i++) if (text[i] == '\n') starts.Add(i + 1);

            var typeface = new Typeface(box.FontFamily, box.FontStyle, box.FontWeight, box.FontStretch);
            double width = new FormattedText(new string('0', starts.Count.ToString().Length), CultureInfo.CurrentUICulture,
                System.Windows.FlowDirection.LeftToRight, typeface, box.FontSize, Brushes.Gray,
                VisualTreeHelper.GetDpi(this).PixelsPerDip).Width + 10;
            LineGutter.Width = width;
            for (int l = 0; l < starts.Count; l++)
            {
                var r = box.GetRectFromCharacterIndex(Math.Min(starts[l], text.Length));
                if (r.IsEmpty) continue;
                var tb = new TextBlock
                {
                    Text = (l + 1).ToString(), FontFamily = box.FontFamily, FontSize = box.FontSize,
                    Foreground = Brushes.Gray, Width = width - 5, TextAlignment = TextAlignment.Right
                };
                Canvas.SetTop(tb, r.Top);
                LineGutter.Children.Add(tb);
            }

            int LineOf(int idx) { int p = starts.BinarySearch(idx); return p < 0 ? ~p - 1 : p; }
            var valid = _marks.Where(m => m.Index + m.Length <= text.Length && m.LabelIndex + m.LabelLength <= text.Length).ToList();
            var lines = new SortedSet<int>();
            foreach (var m in valid)
            {
                lines.Add(LineOf(m.Index));
                if (m.LabelIndex >= 0) lines.Add(LineOf(m.LabelIndex));
            }
            foreach (var l in lines)
            {
                int s = starts[l], e = l + 1 < starts.Count ? starts[l + 1] - 1 : text.Length;
                while (e > s && (text[e - 1] == '\r' || text[e - 1] == '\n')) e--;
                var top = box.GetRectFromCharacterIndex(s);
                var bottom = box.GetRectFromCharacterIndex(Math.Max(s, e - 1));
                if (top.IsEmpty || bottom.IsEmpty) continue;
                AddMark(0, top.Top, box.ActualWidth, bottom.Bottom - top.Top, LineBrush);
            }
            foreach (var m in valid)
            {
                if (m.LabelIndex >= 0) MarkRange(m.LabelIndex, m.LabelLength, TermBrush);
                MarkRange(m.Index, m.Length, HitBrush);
            }
        }

        /// <summary>Bereich hinterlegen; bei Zeilenumbruch je Bildschirmzeile ein Rechteck.</summary>
        private void MarkRange(int start, int length, Brush brush)
        {
            var box = TxtPdfFallback;
            Rect? run = null;
            for (int i = start; i < start + length; i++)
            {
                var a = box.GetRectFromCharacterIndex(i);
                var b = box.GetRectFromCharacterIndex(i, true);
                if (a.IsEmpty || b.IsEmpty) continue;
                var r = new Rect(new Point(a.Left, a.Top), new Point(Math.Max(a.Left, b.Right), a.Bottom));
                if (run is { } cur && Math.Abs(cur.Top - r.Top) < 1) run = Rect.Union(cur, r);
                else { if (run is { } done) AddMark(done.Left, done.Top, done.Width, done.Height, brush); run = r; }
            }
            if (run is { } last) AddMark(last.Left, last.Top, last.Width, last.Height, brush);
        }

        private void AddMark(double x, double y, double w, double h, Brush brush)
        {
            if (w <= 0 || h <= 0) return;
            var rect = new System.Windows.Shapes.Rectangle { Width = w, Height = h, Fill = brush, RadiusX = 2, RadiusY = 2 };
            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, y);
            MarkLayer.Children.Add(rect);
        }

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
