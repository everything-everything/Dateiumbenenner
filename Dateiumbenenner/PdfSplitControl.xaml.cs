using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualBasic.FileIO;
using WpfMessageBox = System.Windows.MessageBox;
using WpfMessageBoxButton = System.Windows.MessageBoxButton;
using WpfMessageBoxImage = System.Windows.MessageBoxImage;
using WpfDragEventArgs = System.Windows.DragEventArgs;
using WpfDragDropEffects = System.Windows.DragDropEffects;
using WpfDataFormats = System.Windows.DataFormats;

namespace Dateiumbenenner
{
    /// <summary>Registerkarte "PDF teilen": PDFs in einzelne Seiten, feste Seitenzahl, Seitenbereiche oder an Leerseiten aufteilen.</summary>
    public partial class PdfSplitControl : System.Windows.Controls.UserControl
    {
        private readonly ObservableCollection<PdfSplitItem> _items = new();
        private readonly PdfListPreview _preview;
        private MainWindow? _mainWindow;
        private readonly bool _ready;

        public PdfSplitControl()
        {
            InitializeComponent();
            LvSplitItems.ItemsSource = _items;
            // Vorschau wie bei Umbenennen: Registerkarte links/rechts andockbar oder in eigenem Fenster
            _preview = new PdfListPreview(this, SplitDockLeft, SplitDockRight, ColSplitDockLeft, ColSplitDockRight, SplitSplitDockLeft, SplitSplitDockRight, LvSplitItems,
                TabSplitDoc, ChkSplitPdf, SplitSearchHost, SplitPreviewHost, TxtSplitPreview, TxtSplitPreviewHint, "registerkarten_pdfteilen.json", "PDF teilen",
                () => (LvSplitItems.SelectedItem as PdfSplitItem)?.FilePath, () => _mainWindow);
            _ready = true;
            UpdatePlans();
        }

        public void SetMainWindow(MainWindow mainWindow) => _mainWindow = mainWindow;

        public Task AddExternalFile(string filePath) => AddFileToList(filePath);

        private async void BtnUseMainList_Click(object sender, RoutedEventArgs e)
        {
            if (_mainWindow == null) return;
            var files = _mainWindow.GetAllFiles();
            if (files.Count == 0)
            {
                WpfMessageBox.Show("Die Hauptliste enthält keine PDF-Dateien.", "Keine Dateien", WpfMessageBoxButton.OK, WpfMessageBoxImage.Information);
                return;
            }
            if (_items.Count > 0)
            {
                var result = WpfMessageBox.Show("Möchten Sie die vorhandene Liste ersetzen?\n\nJa = Ersetzen\nNein = Hinzufügen\nAbbrechen = Abbrechen",
                    "Liste vorhanden", WpfMessageBoxButton.YesNoCancel, WpfMessageBoxImage.Question);
                if (result == MessageBoxResult.Cancel) return;
                if (result == MessageBoxResult.Yes) _items.Clear();
            }
            await AddFiles(files);
        }

        private async void BtnLoadFromMain_Click(object sender, RoutedEventArgs e)
        {
            if (_mainWindow == null) return;
            var files = _mainWindow.GetSelectedFiles();
            if (files.Count == 0)
            {
                WpfMessageBox.Show("Bitte wählen Sie in der Hauptliste mindestens eine PDF-Datei aus.", "Keine Auswahl", WpfMessageBoxButton.OK, WpfMessageBoxImage.Information);
                return;
            }
            await AddFiles(files);
        }

        private async void BtnAddFiles_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "PDF-Dateien (*.pdf)|*.pdf", Multiselect = true, Title = "PDF-Dateien zum Teilen auswählen" };
            if (dlg.ShowDialog() == true) await AddFiles(dlg.FileNames);
        }

        private async Task AddFiles(IEnumerable<string> files)
        {
            int before = _items.Count;
            foreach (var f in files) await AddFileToList(f);
            TxtStatus.Text = $"{_items.Count - before} Datei(en) hinzugefügt.";
        }

        private async Task AddFileToList(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath) || !filePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) return;
            if (_items.Any(x => x.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase))) return;
            var (pages, blank) = await PdfSplitManager.AnalyzeAsync(filePath);
            if (_items.Any(x => x.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase))) return;
            var item = new PdfSplitItem { FilePath = filePath, PageCount = pages, BlankPages = blank, FileSizeBytes = new FileInfo(filePath).Length };
            item.RangesChanged += i => UpdatePlan(i, GetSettings());
            UpdatePlan(item, GetSettings());
            _items.Add(item);
            if (LvSplitItems.SelectedItem == null) LvSplitItems.SelectedItem = item;
        }

        private void BtnRemoveSelected_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in LvSplitItems.SelectedItems.Cast<PdfSplitItem>().ToList()) _items.Remove(item);
        }

        private void BtnClearAll_Click(object sender, RoutedEventArgs e)
        {
            if (_items.Count == 0) return;
            if (WpfMessageBox.Show("Alle Dateien aus der Liste entfernen?", "Liste leeren", WpfMessageBoxButton.YesNo, WpfMessageBoxImage.Question) == MessageBoxResult.Yes)
                _items.Clear();
        }

        private void LvSplitItems_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _preview.Show();
            UpdateNameExample();
        }

        private async void LvSplitItems_Drop(object sender, WpfDragEventArgs e)
        {
            if (e.Data.GetDataPresent(WpfDataFormats.FileDrop) && e.Data.GetData(WpfDataFormats.FileDrop) is string[] files)
                await AddFiles(files.Where(f => f.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)));
        }

        private void LvSplitItems_DragOver(object sender, WpfDragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(WpfDataFormats.FileDrop) ? WpfDragDropEffects.Copy : WpfDragDropEffects.None;
            e.Handled = true;
        }

        private void BtnBrowseOutput_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Zielordner für die geteilten PDF-Dateien",
                UseDescriptionForTitle = true,
                ClientGuid = new Guid("7C2D7E51-4B8A-4E0B-9D63-2F5A8C1E3B47"),
            };
            if (Directory.Exists(TxtOutputFolder.Text)) dlg.SelectedPath = TxtOutputFolder.Text;
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK) TxtOutputFolder.Text = dlg.SelectedPath;
        }

        private void SplitOption_Changed(object sender, RoutedEventArgs e)
        {
            if (!_ready) return;
            // Eingabe in ein Feld wählt die passende Aufteilung
            if (ReferenceEquals(sender, TxtEveryN) && TxtEveryN.IsKeyboardFocusWithin) RbEveryN.IsChecked = true;
            if (ReferenceEquals(sender, TxtRanges) && TxtRanges.IsKeyboardFocusWithin) RbRanges.IsChecked = true;
            UpdatePlans();
        }

        private PdfSplitSettings GetSettings() => new()
        {
            Mode = RbEveryN.IsChecked == true ? PdfSplitMode.EveryN
                : RbRanges.IsChecked == true ? PdfSplitMode.Ranges
                : RbBlank.IsChecked == true ? PdfSplitMode.BlankPages
                : PdfSplitMode.SinglePages,
            EveryN = int.TryParse(TxtEveryN.Text, out var n) && n > 0 ? n : 1,
            Ranges = TxtRanges.Text ?? "",
            SkipBlankPages = ChkSkipBlank.IsChecked == true,
            NamePattern = string.IsNullOrWhiteSpace(CmbNamePattern.Text) ? PdfSplitManager.DefaultPattern : CmbNamePattern.Text.Trim(),
            OutputFolder = TxtOutputFolder.Text?.Trim() ?? "",
        };

        private void UpdatePlans()
        {
            if (!_ready) return;
            var s = GetSettings();
            foreach (var item in _items) UpdatePlan(item, s);
            UpdateNameExample();
        }

        private static void UpdatePlan(PdfSplitItem item, PdfSplitSettings s) => item.PartsDisplay = PdfSplitManager.Describe(item, s);

        private void UpdateNameExample()
        {
            if (!_ready) return;
            var s = GetSettings();
            var item = LvSplitItems.SelectedItem as PdfSplitItem ?? _items.FirstOrDefault();
            string example;
            try
            {
                if (item != null && PdfSplitManager.Plan(item, s) is { Count: > 0 } parts)
                    example = PdfSplitManager.PartFileName(s.NamePattern, item.FilePath, 1, parts.Count, parts[0]);
                else
                    example = PdfSplitManager.PartFileName(s.NamePattern, "Beleg.pdf", 1, 3, new[] { 1, 2 });
            }
            catch (FormatException) { example = PdfSplitManager.PartFileName(s.NamePattern, item?.FilePath ?? "Beleg.pdf", 1, 3, new[] { 1 }); }
            TxtNameExample.Text = "z. B. " + example;
        }

        private async void BtnSplitSelected_Click(object sender, RoutedEventArgs e)
        {
            var selected = LvSplitItems.SelectedItems.Cast<PdfSplitItem>().ToList();
            if (selected.Count == 0)
            {
                WpfMessageBox.Show("Bitte wählen Sie mindestens eine Datei aus.", "Keine Auswahl", WpfMessageBoxButton.OK, WpfMessageBoxImage.Information);
                return;
            }
            await SplitFiles(selected);
        }

        private async void BtnSplitAll_Click(object sender, RoutedEventArgs e)
        {
            if (_items.Count == 0)
            {
                WpfMessageBox.Show("Die Liste ist leer.", "Keine Dateien", WpfMessageBoxButton.OK, WpfMessageBoxImage.Information);
                return;
            }
            await SplitFiles(_items.ToList());
        }

        private async Task SplitFiles(List<PdfSplitItem> items)
        {
            var s = GetSettings();
            if (s.OutputFolder.Length > 0 && !Path.IsPathRooted(s.OutputFolder))
            {
                WpfMessageBox.Show("Bitte einen vollständigen Zielordner angeben (oder leer lassen für den Ordner der Originaldatei).", "Zielordner", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
                return;
            }
            // Ungültige Seitenbereiche vorher melden statt mitten im Lauf abzubrechen
            var invalid = new List<string>();
            foreach (var item in items)
            {
                try { if (PdfSplitManager.Plan(item, s).Count == 0) invalid.Add($"{item.FileName}: keine Seiten"); }
                catch (FormatException ex) { invalid.Add($"{item.FileName}: {ex.Message}"); }
            }
            if (invalid.Count > 0)
            {
                WpfMessageBox.Show("Bitte zuerst korrigieren:\n\n" + string.Join("\n", invalid.Take(10)) + (invalid.Count > 10 ? $"\n… und {invalid.Count - 10} weitere" : ""),
                    "PDF teilen", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
                return;
            }

            var created = new List<string>();
            var done = new List<PdfSplitItem>();
            var errors = new List<string>();
            BtnSplitAll.IsEnabled = BtnSplitSelected.IsEnabled = false;
            try
            {
                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    TxtStatus.Text = $"Teile {i + 1}/{items.Count}: {item.FileName} …";
                    try
                    {
                        created.AddRange(await PdfSplitManager.SplitAsync(item, s));
                        done.Add(item);
                    }
                    catch (Exception ex) { errors.Add($"{item.FileName}: {ex.Message}"); }
                }
            }
            finally { BtnSplitAll.IsEnabled = BtnSplitSelected.IsEnabled = true; }

            TxtStatus.Text = $"{done.Count} Datei(en) in {created.Count} Teile zerlegt.";
            var deleted = done.Count > 0 && ChkDeleteAfterSplit.IsChecked == true ? DeleteOriginals(done) : new List<string>();

            if (ChkPartsToMain.IsChecked == true && _mainWindow != null && (created.Count > 0 || deleted.Count > 0))
                await _mainWindow.RefreshIfFolderShownAsync(created.Concat(deleted).Select(Path.GetDirectoryName).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase));

            var msg = $"{done.Count} Datei(en) geteilt, {created.Count} neue PDF-Datei(en) erstellt.";
            if (created.Count > 0) msg += $"\n\nZielordner: {Path.GetDirectoryName(created[0])}";
            if (deleted.Count > 0) msg += $"\n{deleted.Count} Originaldatei(en) gelöscht.";
            if (errors.Count > 0) msg += "\n\nFehler:\n" + string.Join("\n", errors.Take(5)) + (errors.Count > 5 ? $"\n… und {errors.Count - 5} weitere" : "");
            WpfMessageBox.Show(msg, "PDF teilen", WpfMessageBoxButton.OK, errors.Count > 0 ? WpfMessageBoxImage.Warning : WpfMessageBoxImage.Information);

            if (ChkOpenFolderAfterSplit.IsChecked == true && created.Count > 0 && File.Exists(created[0]))
            {
                try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{created[0]}\"") { UseShellExecute = true }); }
                catch { /* Explorer nicht verfügbar */ }
            }
        }

        /// <summary>Originale nach Rückfrage löschen (lokal: Papierkorb, Netzlaufwerk: Backup-Ordner .deleted).</summary>
        private List<string> DeleteOriginals(List<PdfSplitItem> items)
        {
            var deleted = new List<string>();
            bool network = items.Any(i => PdfMergeControl.IsNetworkPath(i.FilePath));
            var question = network
                ? $"Möchten Sie {items.Count} Originaldatei(en) löschen?\n\nDateien auf Netzlaufwerken werden in einen Backup-Ordner (.deleted) verschoben, lokale Dateien in den Papierkorb."
                : $"Möchten Sie {items.Count} Originaldatei(en) in den Papierkorb verschieben?";
            if (WpfMessageBox.Show(question, "Originaldateien löschen?", WpfMessageBoxButton.YesNo, WpfMessageBoxImage.Question) != MessageBoxResult.Yes) return deleted;
            _preview.Release();
            var failed = new List<string>();
            foreach (var item in items)
            {
                try
                {
                    if (!File.Exists(item.FilePath)) continue;
                    if (PdfMergeControl.IsNetworkPath(item.FilePath)) PdfMergeControl.MoveToBackupFolder(item.FilePath);
                    else FileSystem.DeleteFile(item.FilePath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                    deleted.Add(item.FilePath);
                    _items.Remove(item);
                }
                catch (Exception ex) { failed.Add($"{item.FileName}: {ex.Message}"); }
            }
            if (failed.Count > 0)
                WpfMessageBox.Show("Nicht gelöscht:\n\n" + string.Join("\n", failed.Take(5)) + (failed.Count > 5 ? $"\n… und {failed.Count - 5} weitere" : ""),
                    "Teilweise gelöscht", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
            _preview.Show();
            return deleted;
        }
    }
}
