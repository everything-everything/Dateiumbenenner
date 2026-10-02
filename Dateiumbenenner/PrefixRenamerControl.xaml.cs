using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using MessageBoxWpf = System.Windows.MessageBox;
using UserControlWpf = System.Windows.Controls.UserControl;

namespace Dateiumbenenner
{
    public class PrefixRenameItem : INotifyPropertyChanged
    {
        private string _filePath = string.Empty;
        public string FilePath { get => _filePath; set { if (_filePath != value) { _filePath = value; OnPropertyChanged(nameof(FilePath)); OnPropertyChanged(nameof(FileName)); } } }
        public string FileName => Path.GetFileName(FilePath);
        
        private string _currentPrefix = string.Empty;
        public string CurrentPrefix { get => _currentPrefix; set { if (_currentPrefix != value) { _currentPrefix = value; OnPropertyChanged(nameof(CurrentPrefix)); } } }
        
        private string _newPrefix = string.Empty;
        public string NewPrefix { get => _newPrefix; set { if (_newPrefix != value) { _newPrefix = value; OnPropertyChanged(nameof(NewPrefix)); OnPropertyChanged(nameof(NewFileName)); } } }
        
        private string _extractedCompanyName = string.Empty;
        public string ExtractedCompanyName { get => _extractedCompanyName; set { if (_extractedCompanyName != value) { _extractedCompanyName = value; OnPropertyChanged(nameof(ExtractedCompanyName)); } } }
        
        // NEU: Liste aller erkannten Firmennamen
        public List<string> CompanyNameCandidates { get; set; } = new();
        
        // NEU: Ausgewählter Firmenname
        private string? _selectedCompanyName;
        public string? SelectedCompanyName { get => _selectedCompanyName; set { if (_selectedCompanyName != value) { _selectedCompanyName = value; OnPropertyChanged(nameof(SelectedCompanyName)); } } }
        
        public string NewFileName => string.IsNullOrEmpty(NewPrefix) ? FileName : $"{NewPrefix}{CurrentSuffix}";
        
        private string _currentSuffix = string.Empty;
        public string CurrentSuffix { get => _currentSuffix; set { if (_currentSuffix != value) { _currentSuffix = value; OnPropertyChanged(nameof(CurrentSuffix)); OnPropertyChanged(nameof(NewFileName)); } } }
        
        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); } } }
        
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public partial class PrefixRenamerControl : UserControlWpf
    {
        private ObservableCollection<PrefixRenameItem> _items = new();
        private MainWindow? _mainWindow;
        private string _currentDelimiter = "_";
        private int _segmentIndexToSplit = 0;
        private int _segmentsToRemove = 0;
        private PrefixRenameItem? _currentPreviewItem; // NEU: Aktuelles Vorschau-Item
        private bool _isUpdatingPreview; // NEU: Flag um Rekursion zu vermeiden

        // Regex für Firmenname in Adresszeile - NOCHMALS VERBESSERT
        private static readonly Regex CompanyRegex1 = new(@"^([A-ZÄÖÜ][a-zäöüß]*(?:[\s\-&]+[A-ZÄÖÜ][a-zäöüß]*)*(?:\s+(?:GmbH|AG|KG|UG|e\.V\.|mbH|Co\.|OHG|PartG|GbR))?)", RegexOptions.Compiled);
        private static readonly Regex CompanyRegex2 = new(@"([A-ZÄÖÜ][A-ZÄÖÜ\s&-]{2,}(?:GmbH|AG|KG|UG|e\.V\.|mbH|Co\.|OHG|PartG|GbR|Werk|WERK)?)", RegexOptions.Compiled);
        private static readonly Regex CompanyRegex3 = new(@"([A-ZÄÖÜ][a-zäöüß]+(?:\s+[A-ZÄÖÜ][a-zäöüß]+)*\s+(?:GmbH|AG|KG|UG|e\.V\.|mbH|Co\.|OHG|PartG|GbR))", RegexOptions.Compiled);
        // NEU: Spezielle Patterns für komplexe Firmennamen
        private static readonly Regex CompanyRegex4 = new(@"([A-ZÄÖÜ][\w\-]+(?:\s+|\-)[\w\-]+(?:\s+|\-)?(?:GmbH\s*&\s*Co\.\s*KG|GmbH\s*&\s*Co\.?|Werk))", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex CompanyRegex5 = new(@"([A-ZÄÖÜ][A-Z\-]+(?:\s+[A-Z][A-Z\-]+)*(?:\s+Werk)?)", RegexOptions.Compiled);
        private static readonly Regex CompanyRegex6 = new(@"([A-ZÄÖÜ][\w\-]+(?:[\s\-][A-ZÄÖÜ][\w\-]+)+)", RegexOptions.Compiled);

        public PrefixRenamerControl()
        {
            InitializeComponent();
            LvPrefixFiles.ItemsSource = _items;
        }

        public void SetMainWindow(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
        }

        public void LoadFilesFromMain(IEnumerable<DocumentMetadata> metadataList)
        {
            _items.Clear();
            
            foreach (var meta in metadataList)
            {
                var item = new PrefixRenameItem
                {
                    FilePath = meta.FilePath
                };
                
                // Delimiter erkennen
                var delimiter = meta.SavedDelimiter ?? DetectDelimiter(meta.FileName);
                
                // Prefix und Suffix basierend auf Segment-Index trennen
                SplitFileNameBySegment(item, delimiter, _segmentIndexToSplit);
                
                // VERBESSERT: Alle Firmennamen-Kandidaten extrahieren
                item.CompanyNameCandidates = ExtractAllCompanyNames(meta.FullText);
                
                // Ersten Kandidaten als Standard setzen
                if (item.CompanyNameCandidates.Any())
                {
                    item.ExtractedCompanyName = item.CompanyNameCandidates.First();
                    item.SelectedCompanyName = item.CompanyNameCandidates.First();
                }
                else
                {
                    item.ExtractedCompanyName = string.Empty;
                    item.SelectedCompanyName = null;
                }
                
                _items.Add(item);
            }
            
            UpdateStatus($"{_items.Count} Dateien geladen.");
        }

        private void SplitFileNameBySegment(PrefixRenameItem item, string delimiter, int segmentIndex)
        {
            var fileName = Path.GetFileNameWithoutExtension(item.FileName);
            var ext = Path.GetExtension(item.FileName);
            
            if (string.IsNullOrEmpty(delimiter) || segmentIndex <= 0)
            {
                item.CurrentPrefix = "";
                item.CurrentSuffix = fileName + ext;
                return;
            }
            
            // Finde die Position des N-ten Trennzeichens
            int count = 0;
            int position = -1;
            
            for (int i = 0; i < fileName.Length; i++)
            {
                if (fileName[i].ToString() == delimiter)
                {
                    count++;
                    if (count == segmentIndex)
                    {
                        position = i;
                        break;
                    }
                }
            }
            
            if (position > 0)
            {
                item.CurrentPrefix = fileName.Substring(0, position);
                item.CurrentSuffix = fileName.Substring(position) + ext;
            }
            else
            {
                item.CurrentPrefix = "";
                item.CurrentSuffix = fileName + ext;
            }
        }

        private string ExtractCompanyNameFromText(string? fullText)
        {
            if (string.IsNullOrWhiteSpace(fullText))
                return string.Empty;
            
            var candidates = new List<string>();
            var lines = fullText.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);
            
            // Durchsuche die ersten 20 Zeilen
            foreach (var line in lines.Take(20))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.Length < 3)
                    continue;
                
                // Versuche verschiedene Regex-Muster (ERWEITERT)
                foreach (var regex in new[] { CompanyRegex1, CompanyRegex2, CompanyRegex3, CompanyRegex4, CompanyRegex5, CompanyRegex6 })
                {
                    var match = regex.Match(trimmed);
                    if (match.Success && match.Groups[1].Value.Length >= 3)
                    {
                        var name = match.Groups[1].Value.Trim();
                        // Filter: zu kurze oder nur Zahlen
                        if (name.Length >= 3 && !Regex.IsMatch(name, @"^\d+$"))
                        {
                            if (!candidates.Contains(name))
                                candidates.Add(name);
                        }
                    }
                }
            }
            
            // Gebe den ersten gefundenen Kandidaten zurück
            return candidates.FirstOrDefault() ?? string.Empty;
        }

        private List<string> ExtractAllCompanyNames(string? fullText)
        {
            var candidates = new List<string>();
            
            if (string.IsNullOrWhiteSpace(fullText))
                return candidates;
            
            var lines = fullText.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);
            
            // Durchsuche die ersten 20 Zeilen
            foreach (var line in lines.Take(20))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.Length < 3)
                    continue;
                
                // Versuche verschiedene Regex-Muster (ERWEITERT)
                foreach (var regex in new[] { CompanyRegex1, CompanyRegex2, CompanyRegex3, CompanyRegex4, CompanyRegex5, CompanyRegex6 })
                {
                    var matches = regex.Matches(trimmed);
                    foreach (Match match in matches)
                    {
                        if (match.Success && match.Groups[1].Value.Length >= 3)
                        {
                            var name = match.Groups[1].Value.Trim();
                            // Filter: zu kurze, nur Zahlen oder allgemeine Begriffe
                            if (name.Length >= 3 && 
                                !Regex.IsMatch(name, @"^\d+$") &&
                                !name.Equals("Rechnung", StringComparison.OrdinalIgnoreCase) &&
                                !name.Equals("Datum", StringComparison.OrdinalIgnoreCase))
                            {
                                if (!candidates.Contains(name))
                                    candidates.Add(name);
                            }
                        }
                    }
                }
            }
            
            return candidates;
        }

        private string DetectDelimiter(string fileName)
        {
            var baseName = Path.GetFileNameWithoutExtension(fileName);
            if (string.IsNullOrEmpty(baseName)) return "_";
            
            var delimiters = new Dictionary<string, int>
            {
                {"_", baseName.Count(c => c == '_')},
                {"-", baseName.Count(c => c == '-')},
                {".", baseName.Count(c => c == '.')},
                {" ", baseName.Count(c => c == ' ')}
            };
            
            var mostCommon = delimiters.Where(k => k.Value > 0).OrderByDescending(k => k.Value).FirstOrDefault();
            return mostCommon.Value > 0 ? mostCommon.Key : "_";
        }

        private void BtnLoadFromMain_Click(object sender, RoutedEventArgs e)
        {
            if (_mainWindow == null)
            {
                MessageBoxWpf.Show("MainWindow nicht gesetzt.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            
            var metadataList = _mainWindow.GetLoadedMetadata();
            if (metadataList == null || !metadataList.Any())
            {
                MessageBoxWpf.Show("Keine Dateien im Hauptfenster geladen.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            LoadFilesFromMain(metadataList);
        }

        private void TxtDelimiterPrefix_TextChanged(object sender, TextChangedEventArgs e)
        {
            _currentDelimiter = TxtDelimiterPrefix.Text;
            RefreshAllSplits();
        }

        private void TxtSegmentIndexPrefix_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (int.TryParse(TxtSegmentIndexPrefix.Text, out int value) && value >= 0)
            {
                _segmentIndexToSplit = value;
                RefreshAllSplits();
            }
        }

        private void TxtRemoveSegments_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (int.TryParse(TxtRemoveSegments.Text, out int value) && value >= 0)
            {
                _segmentsToRemove = value;
            }
        }

        private void RefreshAllSplits()
        {
            foreach (var item in _items)
            {
                SplitFileNameBySegment(item, _currentDelimiter, _segmentIndexToSplit);
            }
        }

        private void BtnUseCompanyName_Click(object sender, RoutedEventArgs e)
        {
            var selected = LvPrefixFiles.SelectedItem as PrefixRenameItem;
            if (selected == null)
            {
                MessageBoxWpf.Show("Bitte wählen Sie eine Datei aus.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            // Verwende SelectedCompanyName statt ExtractedCompanyName
            var companyName = selected.SelectedCompanyName ?? selected.ExtractedCompanyName;
            
            if (string.IsNullOrWhiteSpace(companyName))
            {
                MessageBoxWpf.Show("Kein Firmenname erkannt oder ausgewählt.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            // Firmenname als Prefix verwenden (Leerzeichen durch Delimiter ersetzen)
            selected.NewPrefix = companyName.Replace(" ", _currentDelimiter);
        }

        // NEU: Überträgt gewählten Firmennamen auf alle Dateien (auch wenn nicht in Liste)
        private void BtnApplyCompanyNameToAll_Click(object sender, RoutedEventArgs e)
        {
            var selected = LvPrefixFiles.SelectedItem as PrefixRenameItem;
            if (selected == null)
            {
                MessageBoxWpf.Show("Bitte wählen Sie eine Datei aus.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            var companyName = selected.SelectedCompanyName ?? selected.ExtractedCompanyName;
            
            if (string.IsNullOrWhiteSpace(companyName))
            {
                MessageBoxWpf.Show("Kein Firmenname erkannt oder ausgewählt.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            if (MessageBoxWpf.Show($"Möchten Sie den Firmennamen '{companyName}' auf alle {_items.Count} Dateien übertragen?",
                "Bestätigung", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            
            foreach (var item in _items)
            {
                // NEU: Füge den Namen zur Kandidatenliste hinzu, wenn er noch nicht vorhanden ist
                if (!item.CompanyNameCandidates.Contains(companyName))
                {
                    item.CompanyNameCandidates.Add(companyName);
                }
                item.SelectedCompanyName = companyName;
            }
            
            UpdateStatus($"Firmenname '{companyName}' auf alle Dateien übertragen.");
        }

        // NEU: Überträgt gewählten Firmennamen auf markierte Dateien (auch wenn nicht in Liste)
        private void BtnApplyCompanyNameToSelected_Click(object sender, RoutedEventArgs e)
        {
            var selected = LvPrefixFiles.SelectedItem as PrefixRenameItem;
            if (selected == null)
            {
                MessageBoxWpf.Show("Bitte wählen Sie eine Datei aus.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            var companyName = selected.SelectedCompanyName ?? selected.ExtractedCompanyName;
            
            if (string.IsNullOrWhiteSpace(companyName))
            {
                MessageBoxWpf.Show("Kein Firmenname erkannt oder ausgewählt.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            var selectedItems = _items.Where(i => i.IsSelected).ToList();
            if (selectedItems.Count == 0)
            {
                MessageBoxWpf.Show("Bitte markieren Sie die Zieldateien mit der Checkbox.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            foreach (var item in selectedItems)
            {
                // NEU: Füge den Namen zur Kandidatenliste hinzu, wenn er noch nicht vorhanden ist
                if (!item.CompanyNameCandidates.Contains(companyName))
                {
                    item.CompanyNameCandidates.Add(companyName);
                }
                item.SelectedCompanyName = companyName;
            }
            
            UpdateStatus($"Firmenname '{companyName}' auf {selectedItems.Count} Datei(en) übertragen.");
        }

        // NEU: Vorschau-Event-Handler
        private void LvPrefixFiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LvPrefixFiles.SelectedItem is PrefixRenameItem item)
            {
                UpdatePreview(item);
            }
        }

        private void UpdatePreview(PrefixRenameItem item)
        {
            if (_isUpdatingPreview) return;
            
            _isUpdatingPreview = true;
            _currentPreviewItem = item;
            
            try
            {
                TxtPreviewFileName.Text = item.FileName;
                TxtPreviewCurrentPrefix.Text = string.IsNullOrEmpty(item.CurrentPrefix) ? "(kein Präfix)" : item.CurrentPrefix;
                TxtPreviewSuffix.Text = item.CurrentSuffix;
                
                // Firmennamen ComboBox
                CmbPreviewCompanyName.ItemsSource = item.CompanyNameCandidates;
                CmbPreviewCompanyName.Text = item.SelectedCompanyName ?? string.Empty;
                
                // Neuer Präfix
                TxtPreviewNewPrefix.Text = item.NewPrefix ?? string.Empty;
                
                // Vorschau Dateiname
                TxtPreviewNewFileName.Text = item.NewFileName;
                
                // Textvorschau - lade Dokumentmetadata
                var meta = _mainWindow?.GetLoadedMetadata()?.FirstOrDefault(m => m.FilePath == item.FilePath);
                if (meta != null && !string.IsNullOrEmpty(meta.FullText))
                {
                    var preview = meta.FullText.Length > 500 
                        ? meta.FullText.Substring(0, 500) + "..." 
                        : meta.FullText;
                    TxtPreviewContent.Text = preview;
                }
                else
                {
                    TxtPreviewContent.Text = "(Keine Textvorschau verfügbar)";
                }
            }
            finally
            {
                _isUpdatingPreview = false;
            }
        }

        private void CmbPreviewCompanyName_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingPreview || _currentPreviewItem == null) return;
            
            var companyName = CmbPreviewCompanyName.Text;
            if (!string.IsNullOrWhiteSpace(companyName))
            {
                _currentPreviewItem.SelectedCompanyName = companyName;
                UpdatePreview(_currentPreviewItem);
            }
        }

        private void CmbPreviewCompanyName_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingPreview || _currentPreviewItem == null) return;
            
            var companyName = CmbPreviewCompanyName.Text;
            if (!string.IsNullOrWhiteSpace(companyName))
            {
                // Füge zur Kandidatenliste hinzu, wenn nicht vorhanden
                if (!_currentPreviewItem.CompanyNameCandidates.Contains(companyName))
                {
                    _currentPreviewItem.CompanyNameCandidates.Add(companyName);
                }
                _currentPreviewItem.SelectedCompanyName = companyName;
                UpdatePreview(_currentPreviewItem);
            }
        }

        private void TxtPreviewNewPrefix_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingPreview || _currentPreviewItem == null) return;
            
            var newPrefix = TxtPreviewNewPrefix.Text;
            _currentPreviewItem.NewPrefix = newPrefix;
            TxtPreviewNewFileName.Text = _currentPreviewItem.NewFileName;
        }

        private void BtnApplyToSelected_Click(object sender, RoutedEventArgs e)
        {
            var current = LvPrefixFiles.SelectedItem as PrefixRenameItem;
            if (current == null || string.IsNullOrWhiteSpace(current.NewPrefix))
            {
                MessageBoxWpf.Show("Bitte wählen Sie eine Datei mit einem neuen Präfix aus.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            var selectedItems = _items.Where(i => i.IsSelected).ToList();
            if (selectedItems.Count == 0)
            {
                MessageBoxWpf.Show("Bitte markieren Sie die Zieldateien mit der Checkbox.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            foreach (var item in selectedItems)
            {
                item.NewPrefix = current.NewPrefix;
            }
            
            UpdateStatus($"Präfix auf {selectedItems.Count} Datei(en) übertragen.");
        }

        private void BtnApplyToAll_Click(object sender, RoutedEventArgs e)
        {
            var current = LvPrefixFiles.SelectedItem as PrefixRenameItem;
            if (current == null || string.IsNullOrWhiteSpace(current.NewPrefix))
            {
                MessageBoxWpf.Show("Bitte wählen Sie eine Datei mit einem neuen Präfix aus.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            if (MessageBoxWpf.Show($"Möchten Sie den Präfix '{current.NewPrefix}' auf alle {_items.Count} Dateien anwenden?",
                "Bestätigung", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            
            foreach (var item in _items)
            {
                item.NewPrefix = current.NewPrefix;
            }
            
            UpdateStatus($"Präfix auf alle Dateien übertragen.");
        }

        private void BtnRemoveSegmentsFromSelected_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = _items.Where(i => i.IsSelected).ToList();
            if (selectedItems.Count == 0)
            {
                MessageBoxWpf.Show("Bitte markieren Sie die Dateien mit der Checkbox.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            if (_segmentsToRemove <= 0)
            {
                MessageBoxWpf.Show("Bitte geben Sie an, wie viele Segmente entfernt werden sollen.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            foreach (var item in selectedItems)
            {
                var suffix = item.CurrentSuffix;
                var ext = Path.GetExtension(suffix);
                var suffixWithoutExt = Path.GetFileNameWithoutExtension(suffix);
                
                var segments = suffixWithoutExt.Split(new[] { _currentDelimiter }, StringSplitOptions.None);
                
                if (segments.Length > _segmentsToRemove)
                {
                    var remainingSegments = segments.Skip(_segmentsToRemove);
                    item.CurrentSuffix = string.Join(_currentDelimiter, remainingSegments) + ext;
                }
            }
            
            UpdateStatus($"{_segmentsToRemove} Segment(e) von {selectedItems.Count} Datei(en) entfernt.");
        }

        private async void BtnRenameFiles_Click(object sender, RoutedEventArgs e)
        {
            var itemsToRename = _items.Where(i => !string.IsNullOrWhiteSpace(i.NewPrefix)).ToList();
            
            if (itemsToRename.Count == 0)
            {
                MessageBoxWpf.Show("Keine Dateien mit neuem Präfix vorhanden.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            if (MessageBoxWpf.Show($"Möchten Sie wirklich {itemsToRename.Count} Datei(en) umbenennen?",
                "Bestätigung", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            
            int success = 0;
            int errors = 0;
            
            foreach (var item in itemsToRename)
            {
                try
                {
                    var dir = Path.GetDirectoryName(item.FilePath);
                    var newFileName = item.NewFileName;
                    var newPath = Path.Combine(dir!, newFileName);
                    
                    // Prüfe auf Duplikate
                    if (File.Exists(newPath) && !newPath.Equals(item.FilePath, StringComparison.OrdinalIgnoreCase))
                    {
                        int counter = 1;
                        var baseName = Path.GetFileNameWithoutExtension(newFileName);
                        var ext = Path.GetExtension(newFileName);
                        
                        do
                        {
                            newFileName = $"{baseName} ({counter}){ext}";
                            newPath = Path.Combine(dir!, newFileName);
                            counter++;
                        } while (File.Exists(newPath));
                    }
                    
                    if (!newPath.Equals(item.FilePath, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Move(item.FilePath, newPath);
                        item.FilePath = newPath;
                        item.CurrentPrefix = item.NewPrefix;
                        item.NewPrefix = string.Empty;
                        success++;
                    }
                }
                catch (Exception ex)
                {
                    errors++;
                    MessageBoxWpf.Show($"Fehler beim Umbenennen von '{item.FileName}':\n{ex.Message}",
                        "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            
            UpdateStatus($"{success} Datei(en) erfolgreich umbenannt, {errors} Fehler.");
            
            // Aktualisiere MainWindow falls vorhanden
            if (_mainWindow != null && success > 0)
            {
                await _mainWindow.RefreshCurrentFolder();
            }
        }

        private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _items)
                item.IsSelected = true;
        }

        private void BtnDeselectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _items)
                item.IsSelected = false;
        }

        private void UpdateStatus(string message)
        {
            if (TxtPrefixStatus != null)
                TxtPrefixStatus.Text = message;
        }

        private void TxtNewPrefix_TextChanged(object sender, TextChangedEventArgs e)
        {
            // NewPrefix wird durch Binding automatisch aktualisiert
        }
    }
}
