using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Dateiumbenenner.Engine;
using GroupBox = System.Windows.Controls.GroupBox;
using WpfButton = System.Windows.Controls.Button;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfProgressBar = System.Windows.Controls.ProgressBar;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfMessageBox = System.Windows.MessageBox;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;

namespace Dateiumbenenner
{
    /// <summary>Firmenverzeichnis: Quellen anzeigen, eigene Datenbank neu einlesen, GLEIF laden/entfernen, Namen testen.</summary>
    public sealed class CompanyDirectoryWindow : Window
    {
        private readonly MainWindow _main;
        private readonly TextBlock _db = new() { TextWrapping = TextWrapping.Wrap };
        private readonly TextBlock _learned = new() { TextWrapping = TextWrapping.Wrap };
        private readonly TextBlock _gleif = new() { TextWrapping = TextWrapping.Wrap };
        private readonly TextBlock _gleifStatus = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray };
        private readonly WpfProgressBar _progress = new() { Height = 14, Minimum = 0, Maximum = 100, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 0) };
        private readonly WpfButton _btnDb = new() { Content = "Eigene Datenbank neu einlesen", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 6, 0) };
        private readonly WpfButton _btnGleif = new() { Content = "GLEIF herunterladen / aktualisieren (~480 MB)…", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 6, 0) };
        private readonly WpfButton _btnGleifRemove = new() { Content = "GLEIF entfernen", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 6, 0) };
        private readonly WpfButton _btnCancel = new() { Content = "Abbrechen", Padding = new Thickness(8, 2, 8, 2) };
        private readonly WpfTextBox _testName = new() { Width = 260, Margin = new Thickness(0, 0, 6, 0) };
        private readonly WpfTextBox _testZip = new() { Width = 70, Margin = new Thickness(0, 0, 6, 0), ToolTip = "Optional: PLZ, die im Beleg steht" };
        private readonly TextBlock _testResult = new() { TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas"), Margin = new Thickness(0, 6, 0, 0) };

        public CompanyDirectoryWindow(MainWindow main)
        {
            _main = main;
            Title = "Firmenverzeichnis";
            Width = 720; SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.CanResize;

            var root = new StackPanel { Margin = new Thickness(12) };
            root.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10),
                Text = "Bekannte Firmen stützen die Firmenerkennung, ersetzen sie aber nicht: Steht ein Kandidat im Verzeichnis, gibt es Pluspunkte – "
                     + "deutlich mehr, wenn eine seiner PLZ im Beleg steht. Unbekannte Firmen (z. B. kleine Handwerker vor Ort) werden nie abgewertet."
            });

            var src = new StackPanel();
            src.Children.Add(Row("Eigene Datenbank:", _db));
            src.Children.Add(Row("Gelernt:", _learned));
            src.Children.Add(Row("GLEIF (CC0):", _gleif));
            var buttons = new StackPanel { Orientation = WpfOrientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            buttons.Children.Add(_btnDb); buttons.Children.Add(_btnGleif); buttons.Children.Add(_btnGleifRemove); buttons.Children.Add(_btnCancel);
            src.Children.Add(buttons);
            src.Children.Add(_progress);
            src.Children.Add(_gleifStatus);
            root.Children.Add(new GroupBox { Header = "Quellen", Content = src, Padding = new Thickness(6), Margin = new Thickness(0, 0, 0, 10) });

            var test = new StackPanel();
            var tr = new StackPanel { Orientation = WpfOrientation.Horizontal };
            tr.Children.Add(new TextBlock { Text = "Firma:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            tr.Children.Add(_testName);
            tr.Children.Add(new TextBlock { Text = "PLZ im Beleg:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            tr.Children.Add(_testZip);
            test.Children.Add(tr);
            test.Children.Add(_testResult);
            root.Children.Add(new GroupBox { Header = "Testen", Content = test, Padding = new Thickness(6), Margin = new Thickness(0, 0, 0, 10) });

            var close = new WpfButton { Content = "Schließen", Padding = new Thickness(14, 2, 14, 2), HorizontalAlignment = WpfHorizontalAlignment.Right, IsCancel = true };
            close.Click += (_, _) => Close();
            root.Children.Add(close);
            Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

            _btnDb.Click += async (_, _) =>
            {
                _btnDb.IsEnabled = false;
                try { _gleifStatus.Text = await _main.RefreshCompanyDirectoryAsync(true); }
                finally { _btnDb.IsEnabled = true; Refresh(); }
            };
            _btnGleif.Click += (_, _) => StartGleif();
            _btnGleifRemove.Click += (_, _) =>
            {
                if (WpfMessageBox.Show(this, "GLEIF-Firmenverzeichnis entfernen?", Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                try { CompanyDirectory.RemoveGleif(); } catch (Exception ex) { WpfMessageBox.Show(this, ex.Message, Title); }
                Refresh();
            };
            _btnCancel.Click += (_, _) => CompanyDirectory.CancelGleif();
            _testName.TextChanged += (_, _) => UpdateTest();
            _testZip.TextChanged += (_, _) => UpdateTest();

            CompanyDirectory.GleifProgressChanged += OnProgress;
            CompanyDirectory.Changed += OnChanged;
            Closed += (_, _) => { CompanyDirectory.GleifProgressChanged -= OnProgress; CompanyDirectory.Changed -= OnChanged; };
            Loaded += async (_, _) => { Refresh(); await CompanyDirectory.EnsureLoadedAsync(); Refresh(); };
        }

        private static UIElement Row(string label, TextBlock value)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var l = new TextBlock { Text = label, FontWeight = FontWeights.SemiBold };
            g.Children.Add(l);
            Grid.SetColumn(value, 1);
            g.Children.Add(value);
            return g;
        }

        private void OnChanged(object? s, EventArgs e) => Dispatcher.BeginInvoke(new Action(Refresh), DispatcherPriority.Background);
        private void OnProgress(object? s, EventArgs e) => Dispatcher.BeginInvoke(new Action(Refresh), DispatcherPriority.Background);

        private void Refresh()
        {
            bool loaded = CompanyDirectory.IsLoaded;
            _db.Text = !loaded ? "wird geladen…"
                : CompanyDirectory.DbCount == 0 ? "keine Firmen (INI-Plugin mit Datenbank und Adresstabelle samt Namensspalte nötig)"
                : $"{CompanyDirectory.DbCount:N0} Firmen{(CompanyDirectory.DbLoaded is DateTime d ? $", eingelesen am {d:dd.MM.yyyy HH:mm}" : "")} – Adresstabelle, gemerkte Adressen und Alias-Namen des INI-Plugins";
            _learned.Text = !loaded ? "wird geladen…" : $"{CompanyDirectory.LearnedCount:N0} Firmen mit PLZ (aus bestätigten Belegen)";
            var info = CompanyDirectory.GleifSource;
            _gleif.Text = !loaded ? "wird geladen…"
                : CompanyDirectory.GleifCount == 0 ? "nicht geladen (optional). Freies Verzeichnis aller Firmen mit LEI-Nummer; enthält eingetragene Namen mit Sitz-PLZ, aber meist keine kleinen Betriebe."
                : $"{info?.Companies ?? CompanyDirectory.GleifCount:N0} aktive deutsche Firmen, Stand {info?.Publish:dd.MM.yyyy}, geladen am {info?.Retrieved:dd.MM.yyyy HH:mm}"
                  + (info != null ? $"\nQuelle: {info.Url}\nSHA-256 des Downloads: {info.Sha256[..Math.Min(16, info.Sha256.Length)]}…, {info.Size / 1048576.0:N0} MB, {info.Records:N0} Einträge weltweit geprüft" : "");
            bool running = CompanyDirectory.GleifJob is { IsCompleted: false };
            _btnGleif.IsEnabled = !running;
            _btnGleifRemove.IsEnabled = !running && CompanyDirectory.GleifCount > 0;
            _btnCancel.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
            _progress.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
            _progress.Value = CompanyDirectory.GleifProgress;
            if (running || !string.IsNullOrEmpty(CompanyDirectory.GleifStatus)) _gleifStatus.Text = CompanyDirectory.GleifStatus;
            UpdateTest();
        }

        private void StartGleif()
        {
            var text = "Das GLEIF-Verzeichnis (Global Legal Entity Identifier Foundation, Lizenz CC0) wird als Gesamtdatei geladen:\n\n"
                + "• ca. 480 MB von goldencopy.gleif.org (nur HTTPS, keine Umleitungen, Größe wird geprüft)\n"
                + "• daraus werden nur aktive deutsche Firmen (Name, PLZ, Ort) gespeichert – einige MB\n"
                + "• der Download wird danach gelöscht\n"
                + "• Dauer je nach Leitung einige Minuten; läuft im Hintergrund weiter, auch wenn Sie dieses Fenster schließen. "
                + "Der Fortschritt steht unten rechts in der Statusleiste.\n\n"
                + "Es werden keine Daten aus Ihren Belegen übertragen.\n\nJetzt laden?";
            if (WpfMessageBox.Show(this, text, Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            CompanyDirectory.StartGleifImport();
            Refresh();
        }

        private void UpdateTest()
        {
            var name = _testName.Text.Trim();
            if (name.Length == 0) { _testResult.Text = "Firmennamen eingeben, um zu sehen, welche Quellen ihn kennen und wie viele Pluspunkte er bekäme."; return; }
            var zips = new System.Collections.Generic.HashSet<string>();
            if (_testZip.Text.Trim() is { Length: 5 } z) zips.Add(z);
            var hits = CompanyDirectory.Lookup(name);
            var key = CompanyDirectory.Key(name);
            if (hits.Count == 0) { _testResult.Text = $"Vergleichsschlüssel „{key}“: nicht im Verzeichnis – keine Pluspunkte (keine Abwertung)."; return; }
            var lines = hits.Take(12).Select(h => $"{h.Source,-10} {h.Name}  {h.Zip} {h.City}");
            _testResult.Text = $"Vergleichsschlüssel „{key}“ – Pluspunkte: +{CompanyDirectory.Bonus(name, zips):0.#}\n"
                + string.Join("\n", lines) + (hits.Count > 12 ? $"\n… und {hits.Count - 12} weitere" : "");
        }
    }
}
