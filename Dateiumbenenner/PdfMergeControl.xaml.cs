using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.ComponentModel;
using WpfMessageBox = System.Windows.MessageBox;
using WpfMessageBoxButton = System.Windows.MessageBoxButton;
using WpfMessageBoxImage = System.Windows.MessageBoxImage;
using WpfDragEventArgs = System.Windows.DragEventArgs;
using WpfDragDropEffects = System.Windows.DragDropEffects;
using WpfDataFormats = System.Windows.DataFormats;
using Microsoft.VisualBasic.FileIO;

namespace Dateiumbenenner
{
    // NEU: Kompressionseinstellungen-Klasse
    public enum PdfCompressionMode { Ghostscript, Rewrite, Rasterize }
    public class PdfCompressionSettings
    {
        public int? ImageDpi { get; set; } // null = Original
        public int JpegQuality { get; set; } = 75;
        public bool RemoveMetadata { get; set; } = true;
        public bool OptimizeFonts { get; set; } = true;
        public bool Enabled { get; set; } = false;
        public PdfCompressionMode Mode { get; set; } = PdfCompressionMode.Ghostscript;
    }

    public partial class PdfMergeControl : System.Windows.Controls.UserControl
    {
        private ObservableCollection<PdfMergeItem> _mergeItems = new();
        private MainWindow? _mainWindow;

        public PdfMergeControl()
        {
            InitializeComponent();
            LvMergeItems.ItemsSource = _mergeItems;
            LvMergeItems.AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler(MergeHeader_Click));
        }

        public void SetMainWindow(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
        }

        private async void BtnUseMainList_Click(object sender, RoutedEventArgs e)
        {
            if (_mainWindow == null)
            {
                WpfMessageBox.Show("Hauptfenster nicht verfügbar.", "Fehler", WpfMessageBoxButton.OK, WpfMessageBoxImage.Error);
                return;
            }

            var allFiles = _mainWindow.GetAllFiles();
            if (allFiles == null || allFiles.Count == 0)
            {
                WpfMessageBox.Show("Die Hauptliste enthält keine PDF-Dateien.", "Keine Dateien", WpfMessageBoxButton.OK, WpfMessageBoxImage.Information);
                return;
            }

            // Frage Benutzer, ob vorhandene Liste ersetzt oder ergänzt werden soll
            if (_mergeItems.Count > 0)
            {
                var result = WpfMessageBox.Show(
                    "Möchten Sie die vorhandene Liste ersetzen?\n\nJa = Ersetzen\nNein = Hinzufügen\nAbbrechen = Abbrechen",
                    "Liste vorhanden",
                    MessageBoxButton.YesNoCancel,
                    WpfMessageBoxImage.Question);

                if (result == MessageBoxResult.Cancel)
                    return;

                if (result == MessageBoxResult.Yes)
                {
                    _mergeItems.Clear();
                }
            }

            foreach (var file in allFiles)
            {
                await AddFileToList(file);
            }

            TxtStatus.Text = $"{allFiles.Count} Datei(en) aus Hauptliste hinzugefügt.";
        }

        private async void BtnLoadFromMain_Click(object sender, RoutedEventArgs e)
        {
            if (_mainWindow == null)
            {
                WpfMessageBox.Show("Hauptfenster nicht verfügbar.", "Fehler", WpfMessageBoxButton.OK, WpfMessageBoxImage.Error);
                return;
            }

            var selectedFiles = _mainWindow.GetSelectedFiles();
            if (selectedFiles == null || selectedFiles.Count == 0)
            {
                WpfMessageBox.Show("Bitte wählen Sie in der Hauptliste mindestens eine PDF-Datei aus.", "Keine Auswahl", WpfMessageBoxButton.OK, WpfMessageBoxImage.Information);
                return;
            }

            foreach (var file in selectedFiles)
            {
                await AddFileToList(file);
            }

            TxtStatus.Text = $"{selectedFiles.Count} Datei(en) hinzugefügt.";
        }

        private async void BtnAddFiles_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "PDF-Dateien (*.pdf)|*.pdf",
                Multiselect = true,
                Title = "PDF-Dateien auswählen"
            };

            if (dlg.ShowDialog() == true)
            {
                foreach (var file in dlg.FileNames)
                {
                    await AddFileToList(file);
                }

                TxtStatus.Text = $"{dlg.FileNames.Length} Datei(en) hinzugefügt.";
            }
        }

        private async System.Threading.Tasks.Task AddFileToList(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return;

            // Prüfe, ob die Datei bereits in der Liste ist
            if (_mergeItems.Any(x => x.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase)))
                return;

            var pageCount = await PdfMergeManager.GetPageCountAsync(filePath);

            var item = new PdfMergeItem
            {
                FilePath = filePath,
                PageCount = pageCount,
                Order = _mergeItems.Count + 1,
                FileSizeBytes = new FileInfo(filePath).Length,
                Dpi = 72
            };

            _mergeItems.Add(item);
            UpdateOrder();
        }

        public async System.Threading.Tasks.Task AddExternalFile(string filePath)
        {
            await AddFileToList(filePath);
        }

        // Stellt sicher, dass die ListView ohne aktive Sortierung direkt auf _mergeItems zeigt,
        // damit die Positionsbuttons die Reihenfolge steuern können
        private void EnsureManualOrderMode()
        {
            if (!ReferenceEquals(LvMergeItems.ItemsSource, _mergeItems))
            {
                LvMergeItems.ItemsSource = _mergeItems;
            }
            var view = System.Windows.Data.CollectionViewSource.GetDefaultView(LvMergeItems.ItemsSource);
            if (view != null)
            {
                view.SortDescriptions.Clear();
                if (view is System.Windows.Data.ListCollectionView lcv)
                {
                    lcv.CustomSort = null;
                }
                view.Refresh();
            }
        }

        private void BtnMoveUp_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = LvMergeItems.SelectedItems.Cast<PdfMergeItem>().ToList();
            if (selectedItems.Count == 0) return;

            EnsureManualOrderMode();

            foreach (var item in selectedItems.OrderBy(x => x.Order))
            {
                var index = _mergeItems.IndexOf(item);
                if (index > 0)
                {
                    _mergeItems.Move(index, index - 1);
                }
            }

            UpdateOrder();

            // Auswahl beibehalten
            LvMergeItems.SelectedItems.Clear();
            foreach (var it in selectedItems)
            {
                LvMergeItems.SelectedItems.Add(it);
            }
            if (selectedItems.Count > 0)
            {
                LvMergeItems.ScrollIntoView(selectedItems.First());
            }
        }

        private void BtnMoveDown_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = LvMergeItems.SelectedItems.Cast<PdfMergeItem>().ToList();
            if (selectedItems.Count == 0) return;

            EnsureManualOrderMode();

            foreach (var item in selectedItems.OrderByDescending(x => x.Order))
            {
                var index = _mergeItems.IndexOf(item);
                if (index < _mergeItems.Count - 1)
                {
                    _mergeItems.Move(index, index + 1);
                }
            }

            UpdateOrder();

            // Auswahl beibehalten
            LvMergeItems.SelectedItems.Clear();
            foreach (var it in selectedItems)
            {
                LvMergeItems.SelectedItems.Add(it);
            }
            if (selectedItems.Count > 0)
            {
                LvMergeItems.ScrollIntoView(selectedItems.Last());
            }
        }

        private void BtnRemoveSelected_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = LvMergeItems.SelectedItems.Cast<PdfMergeItem>().ToList();
            if (selectedItems.Count == 0)
            {
                WpfMessageBox.Show("Bitte wählen Sie mindestens eine Datei aus.", "Keine Auswahl", WpfMessageBoxButton.OK, WpfMessageBoxImage.Information);
                return;
            }

            foreach (var item in selectedItems)
            {
                _mergeItems.Remove(item);
            }

            UpdateOrder();
            TxtStatus.Text = $"{selectedItems.Count} Datei(en) entfernt.";
        }

        private void BtnDeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = LvMergeItems.SelectedItems.Cast<PdfMergeItem>().ToList();
            if (selectedItems.Count == 0)
            {
                WpfMessageBox.Show("Bitte wählen Sie mindestens eine Datei aus.", "Keine Auswahl", WpfMessageBoxButton.OK, WpfMessageBoxImage.Information);
                return;
            }

            // Prüfe ob Netzlaufwerke dabei sind
            bool hasNetworkFiles = selectedItems.Any(item => IsNetworkPath(item.FilePath));
            
            // Sicherheitsabfrage mit Info über Löschmethode
            string message;
            if (hasNetworkFiles)
            {
                message = $"Möchten Sie wirklich {selectedItems.Count} Datei(en) löschen?\n\n" +
                         "?? Netzlaufwerk-Dateien werden in einen Backup-Ordner verschoben.\n" +
                         "?? Lokale Dateien werden in den Papierkorb verschoben.\n\n" +
                         "Die Dateien können wiederhergestellt werden.";
            }
            else
            {
                message = $"Möchten Sie wirklich {selectedItems.Count} Datei(en) in den Papierkorb verschieben?\n\n" +
                         "Die Dateien können aus dem Papierkorb wiederhergestellt werden.";
            }
            
            var result = WpfMessageBox.Show(message, "Dateien löschen?", MessageBoxButton.YesNo, WpfMessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            int deletedCount = 0;
            int failedCount = 0;
            var failedFiles = new System.Collections.Generic.List<string>();

            foreach (var item in selectedItems)
            {
                try
                {
                    if (File.Exists(item.FilePath))
                    {
                        // Entscheide zwischen Papierkorb und Backup-Ordner
                        if (IsNetworkPath(item.FilePath))
                        {
                            MoveToBackupFolder(item.FilePath);
                        }
                        else
                        {
                            // Lokales Laufwerk: Papierkorb
                            FileSystem.DeleteFile(item.FilePath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                        }
                        deletedCount++;
                        
                        // Entferne aus der Liste
                        _mergeItems.Remove(item);
                    }
                    else
                    {
                        // Datei existiert nicht mehr, entferne trotzdem aus Liste
                        _mergeItems.Remove(item);
                        failedFiles.Add($"{Path.GetFileName(item.FilePath)}: Datei nicht gefunden");
                        failedCount++;
                    }
                }
                catch (Exception ex)
                {
                    failedCount++;
                    failedFiles.Add($"{Path.GetFileName(item.FilePath)}: {ex.Message}");
                }
            }

            UpdateOrder();

            // Zeige Ergebnis an
            if (failedCount > 0)
            {
                WpfMessageBox.Show(
                    $"{deletedCount} Datei(en) erfolgreich gelöscht.\n{failedCount} Datei(en) konnten nicht gelöscht werden:\n\n" +
                    string.Join("\n", failedFiles.Take(5)) + 
                    (failedFiles.Count > 5 ? $"\n... und {failedFiles.Count - 5} weitere" : ""),
                    "Teilweise gelöscht",
                    WpfMessageBoxButton.OK,
                    WpfMessageBoxImage.Warning);
                
                TxtStatus.Text = $"{deletedCount} gelöscht, {failedCount} fehlgeschlagen.";
            }
            else
            {
                WpfMessageBox.Show(
                    $"{deletedCount} Datei(en) erfolgreich gelöscht.",
                    "Erfolgreich gelöscht",
                    WpfMessageBoxButton.OK,
                    WpfMessageBoxImage.Information);
                
                TxtStatus.Text = $"{deletedCount} Datei(en) gelöscht.";
            }
        }

        // Prüft ob ein Pfad auf einem Netzlaufwerk liegt
        private bool IsNetworkPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            try
            {
                // UNC-Pfad (\\server\share)
                if (path.StartsWith(@"\\"))
                    return true;

                // Prüfe ob es ein gemapptes Netzlaufwerk ist
                var root = Path.GetPathRoot(path);
                if (string.IsNullOrEmpty(root))
                    return false;

                var driveInfo = new DriveInfo(root);
                return driveInfo.DriveType == DriveType.Network;
            }
            catch
            {
                // Im Zweifelsfall als lokal behandeln
                return false;
            }
        }

        // Verschiebt eine Datei in einen Backup-Ordner
        private void MoveToBackupFolder(string filePath)
        {
            var directory = Path.GetDirectoryName(filePath);
            if (string.IsNullOrEmpty(directory))
                throw new InvalidOperationException("Verzeichnis konnte nicht ermittelt werden.");

            // Erstelle Backup-Ordner im selben Verzeichnis
            var backupFolder = Path.Combine(directory, ".deleted");
            Directory.CreateDirectory(backupFolder);

            // Erstelle Unterordner mit Datum
            var dateFolder = Path.Combine(backupFolder, DateTime.Now.ToString("yyyy-MM-dd"));
            Directory.CreateDirectory(dateFolder);

            // Zieldateiname
            var fileName = Path.GetFileName(filePath);
            var targetPath = Path.Combine(dateFolder, fileName);

            // Falls Datei bereits existiert, füge Zeitstempel hinzu
            if (File.Exists(targetPath))
            {
                var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                var extension = Path.GetExtension(fileName);
                var timestamp = DateTime.Now.ToString("HHmmss");
                fileName = $"{nameWithoutExt}_{timestamp}{extension}";
                targetPath = Path.Combine(dateFolder, fileName);
            }

            // Verschiebe die Datei
            File.Move(filePath, targetPath);
        }

        private void BtnClearAll_Click(object sender, RoutedEventArgs e)
        {
            if (_mergeItems.Count == 0) return;

            var result = WpfMessageBox.Show(
                "Möchten Sie wirklich alle Dateien aus der Liste entfernen?",
                "Alle entfernen",
                WpfMessageBoxButton.YesNo,
                WpfMessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _mergeItems.Clear();
                TxtStatus.Text = "Liste geleert.";
            }
        }

        private void BtnBrowseOutput_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "PDF-Dateien (*.pdf)|*.pdf",
                Title = "Ausgabedatei auswählen",
                FileName = "Zusammengefuehrt.pdf"
            };

            if (dlg.ShowDialog() == true)
            {
                TxtOutputPath.Text = dlg.FileName;
            }
        }

        private async void BtnMergeSelected_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = LvMergeItems.SelectedItems.Cast<PdfMergeItem>().ToList();
            if (selectedItems.Count == 0)
            {
                WpfMessageBox.Show("Bitte wählen Sie mindestens eine Datei aus.", "Keine Auswahl", WpfMessageBoxButton.OK, WpfMessageBoxImage.Information);
                return;
            }

            await MergePdfs(selectedItems);
        }

        private async void BtnMergeAll_Click(object sender, RoutedEventArgs e)
        {
            if (_mergeItems.Count == 0)
            {
                WpfMessageBox.Show("Die Liste ist leer.", "Keine Dateien", WpfMessageBoxButton.OK, WpfMessageBoxImage.Information);
                return;
            }

            await MergePdfs(_mergeItems.ToList());
        }

        private async System.Threading.Tasks.Task MergePdfs(System.Collections.Generic.List<PdfMergeItem> items)
        {
            if (string.IsNullOrWhiteSpace(TxtOutputPath.Text))
            {
                WpfMessageBox.Show("Bitte wählen Sie eine Ausgabedatei aus.", "Keine Ausgabedatei", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
                BtnBrowseOutput_Click(this, new RoutedEventArgs());
                return;
            }

            try
            {
                TxtStatus.Text = "Zusammenführen...";
                BtnMergeAll.IsEnabled = false;
                BtnMergeSelected.IsEnabled = false;

                // NEU: Hole Kompressionseinstellungen
                var compressionSettings = GetCompressionSettings();
                
                var outputPath = await PdfMergeManager.MergePdfsAsync(items, TxtOutputPath.Text, compressionSettings);

                TxtStatus.Text = $"Erfolgreich zusammengeführt: {Path.GetFileName(outputPath)}";
                
                // NEU: Lösche Originaldateien, falls gewünscht
                if (ChkDeleteAfterMerge.IsChecked == true)
                {
                    // Prüfe ob Netzlaufwerke dabei sind
                    bool hasNetworkFiles = items.Any(item => IsNetworkPath(item.FilePath));
                    
                    string deleteMessage;
                    if (hasNetworkFiles)
                    {
                        deleteMessage = $"Möchten Sie {items.Count} Originaldatei(en) löschen?\n\n" +
                                       "?? Netzlaufwerk-Dateien werden in einen Backup-Ordner verschoben.\n" +
                                       "?? Lokale Dateien werden in den Papierkorb verschoben.\n\n" +
                                       "Die Dateien können wiederhergestellt werden.";
                    }
                    else
                    {
                        deleteMessage = $"Möchten Sie {items.Count} Originaldatei(en) in den Papierkorb verschieben?\n\n" +
                                       "Die Dateien können aus dem Papierkorb wiederhergestellt werden.";
                    }
                    
                    var deleteResult = WpfMessageBox.Show(
                        deleteMessage,
                        "Dateien löschen?",
                        MessageBoxButton.YesNo,
                        WpfMessageBoxImage.Question);

                    if (deleteResult == MessageBoxResult.Yes)
                    {
                        int deletedCount = 0;
                        int failedCount = 0;
                        var failedFiles = new System.Collections.Generic.List<string>();

                        foreach (var item in items)
                        {
                            try
                            {
                                if (File.Exists(item.FilePath))
                                {
                                    // Entscheide zwischen Papierkorb und Backup-Ordner
                                    if (IsNetworkPath(item.FilePath))
                                    {
                                        MoveToBackupFolder(item.FilePath);
                                    }
                                    else
                                    {
                                        // Lokales Laufwerk: Papierkorb
                                        FileSystem.DeleteFile(item.FilePath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                                    }
                                    deletedCount++;
                                    
                                    // Entferne aus der Liste
                                    _mergeItems.Remove(item);
                                }
                            }
                            catch (Exception ex)
                            {
                                failedCount++;
                                failedFiles.Add($"{Path.GetFileName(item.FilePath)}: {ex.Message}");
                            }
                        }

                        UpdateOrder();

                        if (failedCount > 0)
                        {
                            WpfMessageBox.Show(
                                $"{deletedCount} Datei(en) gelöscht.\n{failedCount} Datei(en) konnten nicht gelöscht werden:\n\n" +
                                string.Join("\n", failedFiles.Take(5)) + 
                                (failedFiles.Count > 5 ? $"\n... und {failedFiles.Count - 5} weitere" : ""),
                                "Teilweise gelöscht",
                                WpfMessageBoxButton.OK,
                                WpfMessageBoxImage.Warning);
                        }
                        else
                        {
                            TxtStatus.Text = $"Zusammengeführt und {deletedCount} Datei(en) gelöscht.";
                        }
                    }
                }
                
                WpfMessageBox.Show(
                    $"PDF erfolgreich erstellt:\n{outputPath}\n\nAnzahl Dokumente: {items.Count}",
                    "Erfolg",
                    WpfMessageBoxButton.OK,
                    WpfMessageBoxImage.Information);

                if (ChkOpenAfterMerge.IsChecked == true && File.Exists(outputPath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = outputPath,
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                TxtStatus.Text = "Fehler beim Zusammenführen.";
                WpfMessageBox.Show($"Fehler beim Zusammenführen:\n{ex.Message}", "Fehler", WpfMessageBoxButton.OK, WpfMessageBoxImage.Error);
            }
            finally
            {
                BtnMergeAll.IsEnabled = true;
                BtnMergeSelected.IsEnabled = true;
            }
        }

        private void UpdateOrder()
        {
            for (int i = 0; i < _mergeItems.Count; i++)
            {
                _mergeItems[i].Order = i + 1;
            }
            LvMergeItems.Items.Refresh();
        }

        private async void LvMergeItems_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Benachrichtige das MainWindow über die Auswahl-Änderung
            if (_mainWindow != null && LvMergeItems.SelectedItem is PdfMergeItem selectedItem)
            {
                // Lade den Text aus dem PDF, falls noch nicht vorhanden
                string? fullText = null;
                
                try
                {
                    if (selectedItem.FilePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                    {
                        await System.Threading.Tasks.Task.Run(() =>
                        {
                            using var doc = UglyToad.PdfPig.PdfDocument.Open(selectedItem.FilePath);
                            var sb = new System.Text.StringBuilder();
                            foreach (var page in doc.GetPages())
                            {
                                var text = page.Text;
                                if (!string.IsNullOrWhiteSpace(text))
                                {
                                    sb.AppendLine(text);
                                }
                            }
                            fullText = sb.ToString();
                        });
                    }
                }
                catch
                {
                    // Fehler beim Laden ignorieren
                    fullText = null;
                }

                // Aktualisiere das Vorschaufenster im MainWindow
                await _mainWindow.UpdatePreviewWindowAsync(selectedItem.FilePath, fullText);
            }
        }

        private async void LvMergeItems_Drop(object sender, WpfDragEventArgs e)
        {
            if (e.Data.GetDataPresent(WpfDataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(WpfDataFormats.FileDrop);
                foreach (var file in files.Where(f => f.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)))
                {
                    await AddFileToList(file);
                }
            }
        }

        private void LvMergeItems_DragOver(object sender, WpfDragEventArgs e)
        {
            if (e.Data.GetDataPresent(WpfDataFormats.FileDrop))
            {
                e.Effects = WpfDragDropEffects.Copy;
            }
            else
            {
                e.Effects = WpfDragDropEffects.None;
            }
            e.Handled = true;
        }

        public void RefreshSizeDisplays()
        {
            foreach (var m in _mergeItems)
            {
                m.RefreshSizeDisplay();
            }
            LvMergeItems.Items.Refresh();
        }

        private GridViewColumnHeader? _lastMergeHeader;
        private ListSortDirection _lastMergeDirection = ListSortDirection.Ascending;

        private static readonly NaturalFileNameComparer _naturalComparer = new();

        private void MergeHeader_Click(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is GridViewColumnHeader header && header.Tag is string sortProp && !string.IsNullOrEmpty(sortProp))
            {
                var direction = ListSortDirection.Ascending;
                if (_lastMergeHeader == header && _lastMergeDirection == ListSortDirection.Ascending)
                {
                    direction = ListSortDirection.Descending;
                }
                if (sortProp == "FileName" && _mainWindow != null)
                {
                    var chk = _mainWindow.FindName("ChkNaturalSort") as System.Windows.Controls.CheckBox;
                    if (chk != null && chk.IsChecked == true)
                    {
                        // Wende eine Naturalsortierung an, indem die Collection in diese Reihenfolge gebracht wird
                        var ordered = direction == ListSortDirection.Ascending
                            ? _mergeItems.OrderBy(m => m.FileName, _naturalComparer).ToList()
                            : _mergeItems.OrderByDescending(m => m.FileName, _naturalComparer).ToList();

                        // Stelle sicher, dass ItemsSource bei _mergeItems bleibt
                        EnsureManualOrderMode();

                        // Reordne die ObservableCollection entsprechend
                        for (int i = 0; i < ordered.Count; i++)
                        {
                            var currentIndex = _mergeItems.IndexOf(ordered[i]);
                            if (currentIndex != i && currentIndex >= 0)
                            {
                                _mergeItems.Move(currentIndex, i);
                            }
                        }
                        UpdateOrder();
                    }
                    else
                    {
                        SortMergeList(sortProp, direction);
                    }
                }
                else
                {
                    SortMergeList(sortProp, direction);
                }
                _lastMergeHeader = header;
                _lastMergeDirection = direction;
            }
        }

        private void SortMergeList(string propertyName, ListSortDirection dir)
        {
            var view = System.Windows.Data.CollectionViewSource.GetDefaultView(LvMergeItems.ItemsSource);
            if (view == null) return;
            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new System.ComponentModel.SortDescription(propertyName, dir));
            view.Refresh();
        }

        // NEU: Event Handler für Slider JPEG
        private void SliderJpegQuality_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtJpegQualityValue != null)
            {
                TxtJpegQualityValue.Text = $"{(int)SliderJpegQuality.Value}%";
            }
        }

        // NEU: Event Handler für Kompression Ein/Aus
        private void ChkEnableCompression_Changed(object sender, RoutedEventArgs e)
        {
            if (ChkEnableCompression?.IsChecked == true)
            {
                TxtStatus.Text = "Kompression aktiviert.";
            }
            else
            {
                TxtStatus.Text = "Kompression deaktiviert.";
            }
        }

        // NEU: DPI Slider geändert
        private void SliderImageDpi_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (ChkImageDpiOriginal?.IsChecked == true) return; // Original behalten
            int dpi = (int)Math.Round(SliderImageDpi.Value);
            if (TxtImageDpiDisplay != null) TxtImageDpiDisplay.Text = dpi + " dpi";
            if (TxtImageDpiValue != null && !TxtImageDpiValue.IsKeyboardFocused) TxtImageDpiValue.Text = dpi.ToString();
        }

        // NEU: Direkte DPI Eingabe verlassen
        private void TxtImageDpiValue_LostFocus(object sender, RoutedEventArgs e)
        {
            if (ChkImageDpiOriginal?.IsChecked == true) return;
            if (TxtImageDpiValue == null || SliderImageDpi == null) return;
            if (!int.TryParse(TxtImageDpiValue.Text, out int dpi))
            {
                dpi = (int)SliderImageDpi.Value; // invalid -> lasse bisherigen Wert
            }
            dpi = Math.Clamp(dpi, 72, 600);
            SliderImageDpi.Value = dpi;
            TxtImageDpiDisplay.Text = dpi + " dpi";
            TxtImageDpiValue.Text = dpi.ToString();
        }

        // NEU: Original DPI Checkbox
        private void ChkImageDpiOriginal_Changed()
        {
            if (ChkImageDpiOriginal == null) return;
            bool original = ChkImageDpiOriginal.IsChecked == true;
            if (original)
            {
                if (TxtImageDpiDisplay != null) TxtImageDpiDisplay.Text = "Original";
            }
            else
            {
                // sync mit Slider
                int dpi = (int)Math.Round(SliderImageDpi.Value);
                if (TxtImageDpiDisplay != null) TxtImageDpiDisplay.Text = dpi + " dpi";
                if (TxtImageDpiValue != null) TxtImageDpiValue.Text = dpi.ToString();
            }
        }
        private void ChkImageDpiOriginal_Changed(object sender, RoutedEventArgs e)
        {
            ChkImageDpiOriginal_Changed();
        }

        // NEU: Methode zum Auslesen der Kompressionseinstellungen
        private PdfCompressionSettings GetCompressionSettings()
        {
            var settings = new PdfCompressionSettings();
            settings.Enabled = ChkEnableCompression?.IsChecked ?? false;
            if (!settings.Enabled)
                return settings;

            // DPI Logik: Original wenn Checkbox aktiv sonst Sliderwert
            if (ChkImageDpiOriginal?.IsChecked == true)
            {
                settings.ImageDpi = null; // Original
            }
            else if (SliderImageDpi != null)
            {
                settings.ImageDpi = (int)Math.Round(SliderImageDpi.Value);
            }

            settings.JpegQuality = (int)SliderJpegQuality.Value;
            settings.RemoveMetadata = ChkRemoveMetadata?.IsChecked ?? false;
            settings.OptimizeFonts = ChkOptimizeFonts?.IsChecked ?? false;

            // Modus auslesen
            if (CmbCompressionMode?.SelectedItem is ComboBoxItem modeItem && modeItem.Tag is string tag)
            {
                settings.Mode = tag switch
                {
                    "Rewrite" => PdfCompressionMode.Rewrite,
                    "Rasterize" => PdfCompressionMode.Rasterize,
                    _ => PdfCompressionMode.Ghostscript
                };
            }

            return settings;
        }
    }
}
