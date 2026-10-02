using System.Text;
using System.Windows;
using System.Windows.Controls;
using SWC = System.Windows.Controls;
using System.Text.RegularExpressions;
using System.IO;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using System;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Globalization;
using System.ComponentModel;
using MessageBoxWpf = System.Windows.MessageBox;
using CheckBoxWpf = System.Windows.Controls.CheckBox;
using System.Windows.Documents;
using System.Windows.Media;

namespace Dateiumbenenner
{
    public class InsertPosition { public int Position { get; set; } public string ValueType { get; set; } = "Rechnungsnummer"; public bool Enabled { get; set; } = true; }
    public class DocumentMetadata : INotifyPropertyChanged
    {
        private string _filePath = string.Empty;
        public string FilePath { get => _filePath; set { if (_filePath != value) { _filePath = value; OnPropertyChanged(nameof(FilePath)); OnPropertyChanged(nameof(FileName)); } } }
        public string FileName => Path.GetFileName(FilePath);
        private string? _invoiceNumber; public string? InvoiceNumber { get => _invoiceNumber; set { if (_invoiceNumber != value) { _invoiceNumber = value; OnPropertyChanged(nameof(InvoiceNumber)); } } }
        private string? _documentNumber; public string? DocumentNumber { get => _documentNumber; set { if (_documentNumber != value) { _documentNumber = value; OnPropertyChanged(nameof(DocumentNumber)); } } }
        private string? _date; public string? Date { get => _date; set { if (_date != value) { _date = value; OnPropertyChanged(nameof(Date)); } } }
        private int _pageCount; public int PageCount { get => _pageCount; set { if (_pageCount != value) { _pageCount = value; OnPropertyChanged(nameof(PageCount)); } } }
        public string? FullText { get; set; }
        public string? OriginalFullText { get; set; }
        public bool Loaded { get; set; }
        public string? SelectedNumber { get; set; }
        public string? SelectedDate { get; set; }
        private string? _documentType; public string? DocumentType { get => _documentType; set { if (_documentType != value) { _documentType = value; OnPropertyChanged(nameof(DocumentType)); } } }
        public List<string> NumberCandidates { get; set; } = new();
        public List<string> DateCandidates { get; set; } = new();
        private string? _proposedNewName; public string? ProposedNewName { get => _proposedNewName; set { if (_proposedNewName != value) { _proposedNewName = value; OnPropertyChanged(nameof(ProposedNewName)); } } }
        public long FileSizeBytes { get; set; }
        public static bool UseMegaBytes { get; set; } = true;
        public string FileSizeDisplay => FileSizeBytes <= 0 ? "" : (UseMegaBytes ? (FileSizeBytes / (1024d * 1024d)).ToString("F2") + " MB" : (FileSizeBytes / 1024d).ToString("F2") + " KB");
        private double? _dpi; public double? Dpi { get => _dpi; set { if (_dpi != value) { _dpi = value; OnPropertyChanged(nameof(Dpi)); OnPropertyChanged(nameof(DpiDisplay)); } } }
        public string DpiDisplay => Dpi.HasValue ? Dpi.Value.ToString("F0") : "";
        public string? SavedDelimiter { get; set; }
        public int? SavedSegmentIndex { get; set; }
        public int? SavedInsertValueIndex { get; set; }
        public string? ManualFileName { get; set; }
        public bool DelimiterAutoDetected { get; set; }
        public bool UseDelimiterPosition { get; set; } = true;
        public bool RemoveDuplicates { get; set; }
        public bool ReplaceSegments { get; set; }
        public bool RemoveAttachment { get; set; } // NEU: letztes WAxxxx[_ocred]-Segment entfernen
        public List<InsertPosition> InsertPositions { get; set; } = new(){ new InsertPosition{ Position=1, ValueType="Nummer (Auswahl)", Enabled=true }, new InsertPosition{ Position=2, ValueType="Datum", Enabled=true }, new InsertPosition{ Position=3, ValueType="Dokumenttyp", Enabled=true } };
        public bool NumberManuallySet { get; set; }
        public bool DateManuallySet { get; set; }
        public bool DocTypeManuallySet { get; set; }
        public event PropertyChangedEventHandler? PropertyChanged; private void OnPropertyChanged(string n)=>PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(n));
        public void RefreshSizeDisplay()=>OnPropertyChanged(nameof(FileSizeDisplay));
    }
    public class NaturalFileNameComparer : IComparer<string>
    {
        public int Compare(string? a, string? b)
        { if (a == b) return 0; if (a == null) return -1; if (b == null) return 1; a = a.Replace('_','-'); b = b.Replace('_','-'); var ta = Tokenize(a); var tb = Tokenize(b); for (int i=0;i<Math.Min(ta.Count,tb.Count);i++){ var ca=ta[i]; var cb=tb[i]; int r = ca.isNum && cb.isNum ? ca.num.CompareTo(cb.num) : string.Compare(ca.text,cb.text,StringComparison.CurrentCultureIgnoreCase); if (r!=0) return r; } return ta.Count.CompareTo(tb.Count); }
        private List<(bool isNum,int num,string text)> Tokenize(string s){ var list=new List<(bool,int,string)>(); var sb=new StringBuilder(); bool num=s.Length>0 && char.IsDigit(s[0]); foreach(var ch in s){ bool isD=char.IsDigit(ch); if(isD!=num){ Flush(); num=isD; } sb.Append(ch);} Flush(); return list; void Flush(){ if(sb.Length==0) return; var str=sb.ToString(); if(num && int.TryParse(str,out var n)) list.Add((true,n,str)); else list.Add((false,0,str)); sb.Clear(); } }
    }

    public partial class MainWindow : Window
    {
        private bool _isBulkLoading;
        private ObservableCollection<DocumentMetadata> _items = new();
        private DocumentMetadata? _currentMeta;
        private int _globalMinDigits = 1;
        private PdfPreviewWindow? _openPreviewWindow;
        private bool _isSettingPreviewName;
        private bool _useNaturalSort;
        private bool _isPopulatingSelectors;
        private System.Windows.Threading.DispatcherTimer? _updateTimer;
        private static readonly NaturalFileNameComparer _naturalComparer = new();
        private bool _enableOcrCorrection;
        private List<TextRange> _searchResults = new();
        private int _currentSearchIndex = -1;
        private string _lastSearchText = string.Empty;
        private int _searchVersion = 0;
        private System.Windows.Threading.DispatcherTimer? _searchDebounceTimer;
        private int _docTypeNumberSteps = 1;
        private bool _docTypeNumberSearchForward = true;
        private readonly Dictionary<string, string[]> _docTypeNumberKeywords = new(StringComparer.OrdinalIgnoreCase)
        {
            {"Rechnung", new[]{"Rechnung","Rechnungsnummer","Rechnungsnr","Rechnung Nr","Sammelrechnung","Nummer","Nr"} },
            {"Sammelrechnung", new[]{"Rechnung","Rechnungsnummer","Rechnungsnr","Rechnung Nr","Nummer","Nr"} },
            {"Gutschrift", new[]{"Gutschrift","Gutschriftsnummer","Gutschriftsnr","Gutschrift Nr","Nummer","Nr","Stornorechnung"} },
            {"Stornorechnung", new[]{"Gutschrift","Gutschriftsnummer","Gutschriftsnr","Gutschrift Nr","Nummer","Nr","Stornorechnung"} },
            {"Barverkauf", new[]{"Barverkauf","Rechnung","Rechnungsnummer","Rechnungsnr","Rechnung Nr","Nummer","Nr"} },
            {"Beleg", new[]{"Beleg","Belegnummer","Belegnr","Nummer","Nr"} },
            {"Angebot", new[]{"Angebot","Anbebot","Angebotsnummer","Angebotsnr","Nummer","Nr"} },
            {"Lieferschein", new[]{"Lieferschein","Lieferscheinnummer","Liefernr","Liefernummer","Nummer","Nr"} },
            {"Bestellung", new[]{"Bestellung","Bestellnummer","Best.","Nummer","Nr"} },
            {"Mahnung", new[]{"Mahnung","Mahnungsnummer","Nummer","Nr"} }
        };
        private static readonly Regex _environmentNumberRegex = new(@"\b([A-Z]{0,4}\d[A-Z0-9\-/]{3,}|\d{4,})\b", RegexOptions.IgnoreCase|RegexOptions.Compiled);
        private bool IsInvoiceType(string? t) => !string.IsNullOrEmpty(t) && (t.Equals("Rechnung",StringComparison.OrdinalIgnoreCase)||t.Equals("Sammelrechnung",StringComparison.OrdinalIgnoreCase)||t.Equals("Stornorechnung",StringComparison.OrdinalIgnoreCase)||t.Equals("Gutschrift",StringComparison.OrdinalIgnoreCase)||t.Equals("Barverkauf",StringComparison.OrdinalIgnoreCase));
        private bool IsDocType(string? t) => !string.IsNullOrEmpty(t) && (t.Equals("Beleg",StringComparison.OrdinalIgnoreCase)||t.Equals("Angebot",StringComparison.OrdinalIgnoreCase)||t.Equals("Lieferschein",StringComparison.OrdinalIgnoreCase)||t.Equals("Bestellung",StringComparison.OrdinalIgnoreCase)||t.Equals("Mahnung",StringComparison.OrdinalIgnoreCase));
        private int CountDigits(string s){ if(string.IsNullOrEmpty(s)) return 0; int c=0; foreach(var ch in s) if(char.IsDigit(ch)) c++; return c; }

        // Validierung von Nummern - filtert offensichtlich ung�ltige Nummern heraus
        private bool IsValidNumber(string number)
        {
            if (string.IsNullOrWhiteSpace(number)) return false;

            // Entferne Trennzeichen f�r die Validierung
            var digitsOnly = new string(number.Where(char.IsDigit).ToArray());
            if (string.IsNullOrEmpty(digitsOnly)) return false;

            // Mindestens 4 Ziffern
            if (digitsOnly.Length < 4) return false;

            // Pr�fe ob alle Ziffern gleich sind (z.B. 0000000000, 111111, 222222)
            if (digitsOnly.All(c => c == digitsOnly[0])) return false;

            // Z�hle die Anzahl unterschiedlicher Ziffern
            var uniqueDigits = digitsOnly.Distinct().Count();

            // VERSCH�RFT: Bei langen Nummern (>10 Ziffern) mindestens 5 verschiedene Ziffern
            if (digitsOnly.Length > 10 && uniqueDigits < 5)
            {
                return false; // z.B. 2028282881111 hat nur 4 verschiedene Ziffern (2,0,8,1)
            }

            // Bei Nummern >8 Ziffern mindestens 4 verschiedene Ziffern
            if (digitsOnly.Length > 8 && uniqueDigits < 4)
            {
                return false;
            }

            // VERSCH�RFT: Pr�fe auf verd�chtige Muster: mehr als 60% gleiche Ziffern (statt 70%)
            var mostCommonDigit = digitsOnly.GroupBy(c => c).OrderByDescending(g => g.Count()).First();
            if (mostCommonDigit.Count() > digitsOnly.Length * 0.6) return false;

            // Pr�fe auf zu viele aufeinanderfolgende Nullen (mehr als 4)
            if (Regex.IsMatch(number, @"0{5,}")) return false;

            // KORRIGIERT: Pr�fe auf 2-stellige Wiederholungsmuster an ALLEN Positionen (nicht nur gerade)
            if (digitsOnly.Length >= 8)
            {
                // Z�hle alle 2-Zeichen-Kombinationen an ALLEN Positionen
                var twoCharPatterns = new Dictionary<string, int>();
                for (int i = 0; i <= digitsOnly.Length - 2; i++) // WICHTIG: i++ statt i+=2!
                {
                    var pattern = digitsOnly.Substring(i, 2);
                    if (twoCharPatterns.ContainsKey(pattern))
                        twoCharPatterns[pattern]++;
                    else
                        twoCharPatterns[pattern] = 1;
                }

                // Wenn ein 2-Zeichen-Muster mehr als 3x vorkommt ? ung�ltig
                // z.B. "06134215259" ? "21" kommt 2x vor, "52" kommt 2x vor ? OK
                // z.B. "2028282881111" ? "28" kommt 3x vor (Position 1,3,5) ? ung�ltig
                if (twoCharPatterns.Any(kvp => kvp.Value >= 4))
                    return false;
            }

            // NEUE REGEL: Pr�fe auf aufeinanderfolgende identische 2er-Paare (z.B. "1111")
            if (digitsOnly.Length >= 4)
            {
                for (int i = 0; i <= digitsOnly.Length - 4; i++)
                {
                    if (digitsOnly[i] == digitsOnly[i + 1] && 
                        digitsOnly[i] == digitsOnly[i + 2] && 
                        digitsOnly[i] == digitsOnly[i + 3])
                    {
                        // 4 gleiche Ziffern hintereinander ? ung�ltig (au�er am Ende bei sehr langen Nummern)
                        if (i < digitsOnly.Length - 5 || digitsOnly.Length < 10)
                            return false;
                    }
                }
            }
            
            return true;
        }
        
        private void AutoAssignNumbers(DocumentMetadata meta)
        { 
            if(meta==null) return; 
            // Verhindere �berschreiben einer manuell gesetzten Nummer
            if(!meta.NumberManuallySet && (string.IsNullOrEmpty(meta.SelectedNumber) || !IsValidNumber(meta.SelectedNumber)))
            { 
                var mapped=GetAppropriateNumber(meta,meta.DocumentType); 
                if(!string.IsNullOrEmpty(mapped) && IsValidNumber(mapped))
                    meta.SelectedNumber=mapped; 
            }
            if(!string.IsNullOrEmpty(meta.DocumentType)&&!string.IsNullOrEmpty(meta.SelectedNumber))
            { 
                if(IsInvoiceType(meta.DocumentType))
                { 
                    meta.InvoiceNumber=meta.SelectedNumber; 
                    meta.DocumentNumber=null; 
                } 
                else if(IsDocType(meta.DocumentType))
                { 
                    meta.DocumentNumber=meta.SelectedNumber; 
                    meta.InvoiceNumber=null; 
                } 
            } 
            if(!string.IsNullOrEmpty(meta.SelectedDate)) 
                meta.Date=meta.SelectedDate; 
        }

        private readonly Regex _docTypeRegex = new(@"\b(Rechnung|Sammelrechnung|Stornorechnung|Beleg|Angebot|Lieferschein|Gutschrift|Bestellung|Mahnung|Barverkauf)\b", RegexOptions.IgnoreCase|RegexOptions.Compiled);
        private static readonly Regex KeyInvoice = new(@"\b(Rechnungsnummer|Rg\.?\s*Nr\.?|Re-?Nr\.?|Gu\.?\s*Nr\.?|Gutschriftsnummer|Gutschrift\s*Nr\.?|Stornonummer|Storno\s*Nr\.?|Rechnung\s*Nr\.?)\b", RegexOptions.IgnoreCase|RegexOptions.Compiled);
        private static readonly Regex KeyDocNum = new(@"\b(Belegnummer|Beleg\s*Nr\.?|Angebotsnummer|Angebot\s*Nr\.?|Auftragsnummer|Auftrag\s*Nr\.?|Lieferschein\s*Nr\.?)\b", RegexOptions.IgnoreCase|RegexOptions.Compiled);
        private static readonly Regex KeyDate = new(@"\b(Rechnungsdatum|Datum)\b", RegexOptions.IgnoreCase|RegexOptions.Compiled);
        private static readonly Regex TokenNumber = new(@"\b(?!\d{5}(?:\D|$))([A-Z]{0,4}\d[A-Z0-9\-/]{3,}|\d{6,})\b", RegexOptions.IgnoreCase|RegexOptions.Compiled);
        private static readonly Regex TokenNumberAfterKey = new(@"^([A-Z]{0,4}\d{4,}(?:/\d{1,4})?)", RegexOptions.IgnoreCase|RegexOptions.Compiled);
        private readonly Regex _numberLooseRegex = new(@"\b([A-Z]{0,3}[0-9]{4,}[A-Z0-9\-/]*)\b", RegexOptions.IgnoreCase|RegexOptions.Compiled);
        private readonly Regex _dateNumeric1 = new(@"\b(\d{1,2}[\.\-/]\d{1,2}[\.\-/]\d{2,4})\b", RegexOptions.IgnoreCase|RegexOptions.Compiled);
        private readonly Regex _dateNumeric2 = new(@"\b(\d{4}[\.\-/]\d{1,2}[\.\-/]\d{1,2})\b", RegexOptions.IgnoreCase|RegexOptions.Compiled);
        private readonly Regex _dateSpelledDayMonthYear = new(@"\b(\d{1,2}\.?\s+(Januar|Februar|M�rz|Maerz|April|Mai|Juni|Juli|August|September|Oktober|November|Dezember|Jan|Feb|M�r|Mrz|Mar|Apr|Mai|Jun|Jul|Aug|Sep|Sept|Okt|Nov|Dez)\s+\d{2,4})\b", RegexOptions.IgnoreCase|RegexOptions.Compiled);
        private readonly Regex _dateSpelledMonthYear = new(@"\b((Januar|Februar|M�rz|Maerz|April|Mai|Juni|Juli|August|September|Oktober|November|Dezember|Jan|Feb|M�r|Mrz|Mar|Apr|Mai|Jun|Jul|Aug|Sep|Sept|Okt|Nov|Dez)\s+\d{2,4})\b", RegexOptions.IgnoreCase|RegexOptions.Compiled);
        private static readonly char[] InvalidFileNameChars = new[]{'\\','/',':','*','?','"','<','>','|'};
        private static readonly CultureInfo DeCulture = CultureInfo.GetCultureInfo("de-DE");
        private static readonly string[] DateFormats = new[]{"d.M.yyyy","dd.MM.yyyy","d.M.yy","dd.MM.yy","dd-MM-yyyy","d-M-yyyy","dd/MM/yyyy","d/M/yyyy","yyyy-MM-dd","yyyy/MM/dd","yyyy.MM.dd","d. MMMM yyyy","dd. MMMM yyyy","d. MMM yyyy","dd. MMM yyyy","d MMMM yyyy","dd MMMM yyyy","d MMM yyyy","dd MMM yyyy","MMMM yyyy","MMM yyyy","MMMM yy","MMM yy","yyyy MMMM","yyyy MMM"};

        public MainWindow(){ InitializeComponent(); if(LvFiles!=null) LvFiles.ItemsSource=_items; if(LvFiles!=null) LvFiles.AddHandler(GridViewColumnHeader.ClickEvent,new RoutedEventHandler(GridHeader_Click)); if(CmbDocType!=null){ CmbDocType.ItemsSource=new List<string>{"Rechnung","Sammelrechnung","Stornorechnung","Gutschrift","Barverkauf","Beleg","Angebot","Lieferschein","Bestellung","Mahnung"}; CmbDocType.IsEditable=true; } this.Loaded += (s,e)=>{ var pdfMergeTab=FindName("PdfMergeTab") as PdfMergeControl; pdfMergeTab?.SetMainWindow(this); var prefixTab=FindName("PrefixRenamerTab") as PrefixRenamerControl; prefixTab?.SetMainWindow(this); var txtSteps=FindName("TxtDocTypeNumberSteps") as System.Windows.Controls.TextBox; if(txtSteps!=null && string.IsNullOrEmpty(txtSteps.Text)) txtSteps.Text=_docTypeNumberSteps.ToString(); }; }

        private void ChkOcrCorrection_Changed(object sender,RoutedEventArgs e){ _enableOcrCorrection=(sender as CheckBoxWpf)?.IsChecked==true; TxtStatus.Text=_enableOcrCorrection?"OCR-Korrektur wird angewendet...":"OCR-Korrektur wird entfernt..."; foreach(var meta in _items.Where(m=>m.Loaded).ToList()){ ReapplyOcrAndReextract(meta); } LvFiles.Items.Refresh(); if(_currentMeta!=null && _currentMeta.Loaded){ PopulateSelectors(_currentMeta); ShowDetails(_currentMeta); RefreshNewNamePreview(_currentMeta); _=UpdatePdfPreviewAsync(_currentMeta); } UpdateStandardStatus(); }

        private void MenuExit_Click(object sender, RoutedEventArgs e) => Close();
        private void MenuHelp_Click(object sender, RoutedEventArgs e) => new HelpWindow(HelpWindow.Page.Help) { Owner = this }.ShowDialog();
        private void MenuLicenses_Click(object sender, RoutedEventArgs e) => new HelpWindow(HelpWindow.Page.Licenses) { Owner = this }.ShowDialog();
        private void MenuAbout_Click(object sender, RoutedEventArgs e) => new HelpWindow(HelpWindow.Page.About) { Owner = this }.ShowDialog();

        private async void BtnSelectFolder_Click(object sender,RoutedEventArgs e){ var dlg=new System.Windows.Forms.FolderBrowserDialog(); if(dlg.ShowDialog()==System.Windows.Forms.DialogResult.OK){ TxtFolder.Text=dlg.SelectedPath; await LoadFolderAsync(dlg.SelectedPath); } }
        private async Task LoadFolderAsync(string folder){ _items.Clear(); TxtStatus.Text="Dateien werden eingelesen..."; if(LoadingProgress!=null){ LoadingProgress.Visibility=Visibility.Visible; LoadingProgress.Value=0; } var exts=new[]{".pdf",".txt",".csv"}; var files=Directory.EnumerateFiles(folder).Where(f=>exts.Contains(Path.GetExtension(f).ToLowerInvariant())).ToList(); files=_useNaturalSort? files.OrderBy(f=>Path.GetFileName(f),_naturalComparer).ToList(): files.OrderBy(f=>f).ToList(); int total=files.Count; int processed=0; _isBulkLoading=true; foreach(var f in files){ var fi=new FileInfo(f); var meta=new DocumentMetadata{ FilePath=f, FileSizeBytes=fi.Length, Dpi=Path.GetExtension(f).Equals(".pdf",StringComparison.OrdinalIgnoreCase)?72:null, SavedDelimiter=null, SavedSegmentIndex=2, SavedInsertValueIndex=0, DelimiterAutoDetected=true, UseDelimiterPosition=true }; _items.Add(meta); await LoadMetadataAsync(meta); AutoAssignNumbers(meta); processed++; if(LoadingProgress!=null && total>0) LoadingProgress.Value=processed*100.0/total; if(processed%5==0 && !_isBulkLoading) LvFiles.Items.Refresh(); } _isBulkLoading=false; LvFiles.Items.Refresh(); if(LoadingProgress!=null) LoadingProgress.Visibility=Visibility.Collapsed; UpdateStandardStatus(); }

        private async void LvFiles_SelectionChanged(object sender,SelectionChangedEventArgs e){ 
            // Speichere die Einstellungen des vorherigen Eintrags BEVOR _currentMeta ge�ndert wird
            if(e.RemovedItems.Count>0 && e.RemovedItems[0] is DocumentMetadata prevMeta && _currentMeta == prevMeta) 
                SaveCurrentSettings(prevMeta); 
            
            if(LvFiles.SelectedItem is DocumentMetadata meta){ 
                if(!meta.Loaded) 
                    await LoadMetadataAsync(meta);
                
                // Setze _isPopulatingSelectors SOFORT auf true, um Event-Handler zu blockieren
                _isPopulatingSelectors = true;
                
                // Setze _currentMeta erst NACH dem Speichern und VOR dem Wiederherstellen
                _currentMeta=meta; 
                
                try {
                    // Stelle die gespeicherten Einstellungen wieder her und zeige sie in der UI an
                    RestoreSettings(meta); 
                    PopulateSelectors(meta); 
                    
                    // AutoAssignNumbers nur aufrufen, wenn noch keine Werte gesetzt sind
                    AutoAssignNumbers(meta); 
                    
                    ShowDetails(meta); 
                    await Dispatcher.InvokeAsync(()=>{}, System.Windows.Threading.DispatcherPriority.Loaded); 
                    RefreshNewNamePreview(meta); 
                    await UpdatePdfPreviewAsync(meta); 
                    
                    if(_openPreviewWindow!=null && !string.IsNullOrEmpty(meta.FilePath)) 
                        await _openPreviewWindow.LoadPdfAsync(meta.FilePath, meta.FullText); 
                    
                    LvFiles.Items.Refresh();
                } finally {
                    // Stelle sicher, dass _isPopulatingSelectors am Ende zur�ckgesetzt wird
                    _isPopulatingSelectors = false;
                }
            } else {
                _currentMeta=null; 
            }
            
            UpdateStandardStatus();
        }

        private async Task LoadMetadataAsync(DocumentMetadata meta){ try{ var ext=Path.GetExtension(meta.FilePath).ToLowerInvariant(); if(ext==".pdf"){ await Task.Run(()=>{ using var doc=PdfDocument.Open(meta.FilePath); meta.PageCount=doc.NumberOfPages; var sb=new StringBuilder(); foreach(var page in doc.GetPages()){ var s=page.Text; if(string.IsNullOrWhiteSpace(s)){ var sbLetters=new StringBuilder(); foreach(var letter in page.Letters) sbLetters.Append(letter.Value); s=sbLetters.ToString(); } if(!string.IsNullOrWhiteSpace(s)) sb.AppendLine(s); } meta.FullText=sb.ToString(); }); } else { meta.FullText=await File.ReadAllTextAsync(meta.FilePath); meta.PageCount=1; } meta.OriginalFullText = meta.FullText; // Original sichern
            if(_enableOcrCorrection && !string.IsNullOrEmpty(meta.FullText))
                meta.FullText = ApplyOcrHeuristics(meta.FullText);
            meta.NumberCandidates.Clear(); meta.DateCandidates.Clear(); if(!meta.NumberManuallySet) meta.SelectedNumber=null; if(!meta.DateManuallySet) meta.SelectedDate=null; if(!meta.DocTypeManuallySet) meta.DocumentType=null; if(!string.IsNullOrEmpty(meta.FullText)) ExtractCandidates(meta); if(string.IsNullOrEmpty(meta.SavedDelimiter)||meta.DelimiterAutoDetected){ meta.SavedDelimiter=DetectDelimiterFromFilename(meta.FileName); meta.DelimiterAutoDetected=true; } meta.Loaded=true; } catch(Exception ex){ meta.FullText=$"Fehler beim Lesen: {ex.Message}"; } }

        private string DetectDelimiterFromFilename(string filename)
        {
            var baseName = Path.GetFileNameWithoutExtension(filename);
            if (string.IsNullOrEmpty(baseName))
                return "_";

            var delimiters = new Dictionary<string, int>
            {
                ["_"] = baseName.Count(c => c == '_'),
                ["-"] = baseName.Count(c => c == '-'),
                ["."] = baseName.Count(c => c == '.'),
                [" "] = baseName.Count(c => c == ' '),
                ["#"] = baseName.Count(c => c == '#')
            };

            var mostCommon = delimiters.Where(k => k.Value > 0).OrderByDescending(k => k.Value).FirstOrDefault();
            return mostCommon.Value > 0 ? mostCommon.Key : "_";
        }

        private void ExtractCandidates(DocumentMetadata meta)
        { 
            var types=_docTypeRegex.Matches(meta.FullText!).Cast<Match>().Select(m=>m.Groups[1].Value.Trim()).Distinct().ToList(); 
            if(!meta.DocTypeManuallySet) meta.DocumentType=types.FirstOrDefault(); 
            var lines=meta.FullText!.Split(new[]{"\r\n","\n","\r"}, StringSplitOptions.RemoveEmptyEntries); 
            var invoiceNumbers=new List<(string num,int pos)>(); 
            var documentNumbers=new List<(string num,int pos)>(); 
            var datesWithPos=new List<(string date,int pos)>(); 
            for(int i=0;i<lines.Length;i++)
            { 
                ScanForValuesWithPosition(lines,i,KeyInvoice,invoiceNumbers); 
                ScanForValuesWithPosition(lines,i,KeyDocNum,documentNumbers); 
                ScanForDatesWithPosition(lines,i,KeyDate,meta.FullText!, datesWithPos); 
            } 
            invoiceNumbers=invoiceNumbers.OrderBy(x=>x.pos).ToList(); 
            documentNumbers=documentNumbers.OrderBy(x=>x.pos).ToList(); 
            datesWithPos=datesWithPos.OrderBy(x=>x.pos).ToList(); 
            meta.DateCandidates.Clear(); 
            foreach(var (date,_) in datesWithPos) 
                if(!meta.DateCandidates.Contains(date)) 
                    meta.DateCandidates.Add(date); 
            
            if(!meta.NumberManuallySet)
            { 
                // Filtere ung�ltige Nummern heraus
                var validInvoiceNumbers = invoiceNumbers.Where(n => IsValidNumber(n.num)).ToList();
                var validDocumentNumbers = documentNumbers.Where(n => IsValidNumber(n.num)).ToList();
                
                meta.InvoiceNumber = validInvoiceNumbers.FirstOrDefault().num; 
                meta.DocumentNumber = validDocumentNumbers.FirstOrDefault().num; 
            } 
            
            if(!meta.DateManuallySet) 
                meta.Date=meta.DateCandidates.FirstOrDefault(); 
            
            meta.NumberCandidates.Clear(); 
            
            // F�ge nur g�ltige Nummern hinzu
            foreach(var (n,_) in invoiceNumbers) 
                if(!string.IsNullOrEmpty(n) && IsValidNumber(n) && !meta.NumberCandidates.Contains(n)) 
                    meta.NumberCandidates.Add(n); 
            
            foreach(var (n,_) in documentNumbers) 
                if(!string.IsNullOrEmpty(n) && IsValidNumber(n) && !meta.NumberCandidates.Contains(n)) 
                    meta.NumberCandidates.Add(n); 
            
            foreach(Match m in _numberLooseRegex.Matches(meta.FullText!))
            { 
                var val=m.Groups[1].Value.Trim(); 
                if(!string.IsNullOrEmpty(val) && IsValidNumber(val) && !meta.NumberCandidates.Contains(val)) 
                    meta.NumberCandidates.Add(val); 
            } 
            
            if(string.IsNullOrEmpty(meta.Date) && string.IsNullOrEmpty(meta.SelectedDate) && !meta.DateManuallySet)
            { 
                var dateFromName=ExtractDateFromFileName(meta.FileName); 
                if(!string.IsNullOrEmpty(dateFromName))
                { 
                    meta.DateCandidates.Insert(0,dateFromName); 
                    meta.SelectedDate=dateFromName; 
                    meta.Date=dateFromName; 
                } 
            } 
        }

        private void ScanForValuesWithPosition(string[] lines,int i,Regex keyRegex,List<(string,int)> results){ var match=keyRegex.Match(lines[i]); if(match.Success){ var rest=lines[i].Substring(match.Index+match.Length); var im=TokenNumber.Match(rest); string? extracted=null; if(im.Success){ var value=im.Groups[1].Value.Trim(); var cut=Regex.Match(value,@"^[A-Z]{0,4}\d{4,}(?:/\d{1,4})?"); extracted=cut.Success?cut.Value:value; } else { var alt=TokenNumberAfterKey.Match(rest); if(alt.Success) extracted=alt.Groups[1].Value.Trim(); } if(!string.IsNullOrEmpty(extracted)){ int pos=i*100; if(!results.Any(x=>x.Item1==extracted)) results.Add((extracted,pos)); if(extracted.Contains('/')){ var before=extracted.Split('/')[0]; if(before.Length>=6 && !results.Any(x=>x.Item1==before)) results.Add((before,pos+1)); } } for(int off=1;off<=2 && (i+off)<lines.Length;off++){ var matches=TokenNumber.Matches(lines[i+off]); foreach(Match m in matches){ var val=m.Groups[1].Value.Trim(); var cut=Regex.Match(val,@"^[A-Z]{0,4}\d{4,}(?:/\d{1,4})?"); var finalVal=cut.Success?cut.Value:val; int pos=i*100+off*10; if(!results.Any(x=>x.Item1==finalVal)) results.Add((finalVal,pos)); if(finalVal.Contains('/')){ var before=finalVal.Split('/')[0]; if(before.Length>=6 && !results.Any(x=>x.Item1==before)) results.Add((before,pos+1)); } } var alt2=TokenNumberAfterKey.Match(lines[i+off]); if(alt2.Success){ var val=alt2.Groups[1].Value.Trim(); int pos=i*100+off*10+5; if(!results.Any(x=>x.Item1==val)) results.Add((val,pos)); if(val.Contains('/')){ var before=val.Split('/')[0]; if(before.Length>=6 && !results.Any(x=>x.Item1==before)) results.Add((before,pos+1)); } } } } }

        private void ScanForDatesWithPosition(string[] lines,int i,Regex keyRegex,string fullText,List<(string,int)> results){ var line=lines[i]; int lineStart=0; for(int j=0;j<i;j++) lineStart+=lines[j].Length+Environment.NewLine.Length; void addMatches(Regex rx){ foreach(Match m in rx.Matches(line)){ var ds=NormalizeDate(m.Groups[1].Value.Trim()); var pos=lineStart+m.Index; if(!results.Any(x=>x.Item1==ds)) results.Add((ds,pos)); } } addMatches(_dateNumeric1); addMatches(_dateNumeric2); addMatches(_dateSpelledDayMonthYear); addMatches(_dateSpelledMonthYear); if(keyRegex.IsMatch(line)){ for(int off=1;off<=2 && (i+off)<lines.Length;off++){ var next=lines[i+off]; int nextStart=lineStart; for(int j=i;j<i+off;j++) nextStart+=lines[j].Length+Environment.NewLine.Length; foreach(var rx in new[]{_dateNumeric1,_dateNumeric2,_dateSpelledDayMonthYear,_dateSpelledMonthYear}){ foreach(Match m in rx.Matches(next)){ var ds=NormalizeDate(m.Groups[1].Value.Trim()); var pos=nextStart+m.Index; if(!results.Any(x=>x.Item1==ds)) results.Add((ds,pos)); } } } } }
        private void AddUniqueWithPosition(List<(string,int)> list,String value,int pos){ if(!string.IsNullOrWhiteSpace(value) && !list.Any(x=>x.Item1==value)) list.Add((value,pos)); }

        private async Task UpdatePdfPreviewAsync(DocumentMetadata meta){ PdfWebView.Visibility=Visibility.Collapsed; ImgPdfPreview.Visibility=Visibility.Collapsed; PdfTextScroll.Visibility=Visibility.Collapsed; if(meta==null||string.IsNullOrEmpty(meta.FilePath)) return; var ext=Path.GetExtension(meta.FilePath).ToLowerInvariant(); if(ext!=".pdf"){ TxtPdfFallback.Text=meta.FullText??"(kein Text verf�gbar)"; PdfTextScroll.Visibility=Visibility.Visible; return; } var mode=(CmbPdfPreviewMode.SelectedItem as ComboBoxItem)?.Content?.ToString()??"Auto"; try{ switch(mode){ case "WebView2": await ShowPdfInWebView2(meta.FilePath); break; case "Text": ShowPdfAsText(meta); break; default: if(await TryShowPdfInWebView2(meta.FilePath)) break; ShowPdfAsText(meta); break; } } catch(Exception ex){ TxtPdfFallback.Text=$"Fehler bei der PDF-Vorschau:\n{ex.Message}\n\nText-Vorschau:\n{meta.FullText}"; PdfTextScroll.Visibility=Visibility.Visible; } }
        private async Task<bool> TryShowPdfInWebView2(string filePath){ try{ await ShowPdfInWebView2(filePath); return true;} catch{ return false; } }
        private async Task ShowPdfInWebView2(string filePath){ await PdfWebView.EnsureCoreWebView2Async(); PdfWebView.Source=new Uri(filePath); PdfWebView.Visibility=Visibility.Visible; }
        private void ShowPdfAsText(DocumentMetadata meta){ TxtPdfFallback.Text=meta.FullText??"(kein Text verf�gbar)"; PdfTextScroll.Visibility=Visibility.Visible; }

        private void PopulateSelectors(DocumentMetadata meta){ _isPopulatingSelectors=true; try{ if(meta.DocTypeManuallySet && !string.IsNullOrEmpty(meta.DocumentType)) CmbDocType.Text=meta.DocumentType; else if(!string.IsNullOrEmpty(meta.DocumentType)){ var match=(CmbDocType.ItemsSource as List<string>)?.FirstOrDefault(i=>i.Equals(meta.DocumentType,StringComparison.OrdinalIgnoreCase)); if(match!=null) CmbDocType.SelectedItem=match; else CmbDocType.Text=meta.DocumentType; } else { CmbDocType.SelectedIndex=-1; CmbDocType.Text=string.Empty; }
            CmbNumberCandidates.ItemsSource=null; CmbNumberCandidates.Text=""; CmbNumberCandidates.SelectedIndex=-1; var validNumbers=meta.NumberCandidates.Where(n=>IsValidNumber(n)).ToList(); if(_globalMinDigits>0){ var enough=validNumbers.Where(n=>CountDigits(n)>=_globalMinDigits).OrderBy(n=>CountDigits(n)).ToList(); var few=validNumbers.Where(n=>CountDigits(n)<_globalMinDigits).OrderBy(n=>CountDigits(n)).ToList(); validNumbers=enough.Concat(few).ToList(); } else validNumbers=validNumbers.OrderBy(n=>CountDigits(n)).ToList(); if(meta.NumberManuallySet && !string.IsNullOrEmpty(meta.SelectedNumber) && !validNumbers.Contains(meta.SelectedNumber)) validNumbers.Insert(0,meta.SelectedNumber); else if(!meta.NumberManuallySet && !string.IsNullOrEmpty(meta.SelectedNumber) && IsValidNumber(meta.SelectedNumber) && !validNumbers.Contains(meta.SelectedNumber)) validNumbers.Insert(0,meta.SelectedNumber); CmbNumberCandidates.ItemsSource=validNumbers; if(meta.NumberManuallySet && !string.IsNullOrEmpty(meta.SelectedNumber)){ CmbNumberCandidates.Text=meta.SelectedNumber; CmbNumberCandidates.SelectedItem=meta.SelectedNumber; } else if(!string.IsNullOrEmpty(meta.SelectedNumber) && validNumbers.Contains(meta.SelectedNumber) && IsValidNumber(meta.SelectedNumber)){ CmbNumberCandidates.SelectedItem=meta.SelectedNumber; CmbNumberCandidates.Text=meta.SelectedNumber; } else { var appropriate=GetAppropriateNumber(meta,meta.DocumentType); if(!string.IsNullOrEmpty(appropriate) && !validNumbers.Contains(appropriate)){ validNumbers.Insert(0,appropriate); CmbNumberCandidates.ItemsSource=null; CmbNumberCandidates.ItemsSource=validNumbers; } if(!string.IsNullOrEmpty(appropriate)){ CmbNumberCandidates.SelectedItem=appropriate; CmbNumberCandidates.Text=appropriate; if(!meta.NumberManuallySet) meta.SelectedNumber=appropriate; } else if(validNumbers.Any()){ var first=validNumbers.First(); CmbNumberCandidates.SelectedItem=first; CmbNumberCandidates.Text=first; if(!meta.NumberManuallySet) meta.SelectedNumber=first; } else { if(!meta.NumberManuallySet) meta.SelectedNumber=null; } }
            if(!meta.NumberManuallySet) UpdateMetadataFromSelections(meta);
            CmbDateCandidates.ItemsSource=null; CmbDateCandidates.Text=""; CmbDateCandidates.SelectedIndex=-1; CmbDateCandidates.ItemsSource=meta.DateCandidates; if(meta.DateManuallySet && !string.IsNullOrEmpty(meta.SelectedDate) && meta.DateCandidates.Contains(meta.SelectedDate)){ CmbDateCandidates.SelectedItem=meta.SelectedDate; CmbDateCandidates.Text=meta.SelectedDate; } else if(!string.IsNullOrEmpty(meta.SelectedDate) && meta.DateCandidates.Contains(meta.SelectedDate)){ CmbDateCandidates.SelectedItem=meta.SelectedDate; CmbDateCandidates.Text=meta.SelectedDate; } else if(meta.DateCandidates.Any()){ CmbDateCandidates.SelectedIndex=0; if(!meta.DateManuallySet) meta.SelectedDate=meta.DateCandidates.First(); } else { if(!meta.DateManuallySet) meta.SelectedDate=null; } } finally { _isPopulatingSelectors=false; } }

        private void UpdateMetadataFromSelections(DocumentMetadata meta){ if(meta==null||_isPopulatingSelectors) return; if(CmbDocType!=null && !string.IsNullOrWhiteSpace(CmbDocType.Text)) meta.DocumentType=CmbDocType.Text.Trim(); if(CmbNumberCandidates!=null && !string.IsNullOrWhiteSpace(CmbNumberCandidates.Text)) meta.SelectedNumber=CmbNumberCandidates.Text.Trim(); if(CmbDateCandidates!=null && !string.IsNullOrWhiteSpace(CmbDateCandidates.Text)){ var dt=CmbDateCandidates.Text.Trim(); meta.SelectedDate=dt; meta.Date=dt; } }

        private void SaveCurrentSettings(DocumentMetadata meta){ if(meta==null||!meta.Loaded) return; if(_currentMeta!=meta || _isPopulatingSelectors) return; var docType=CmbDocType.Text; if(!string.IsNullOrEmpty(docType)){ meta.DocumentType=docType; meta.DocTypeManuallySet=true; } var number=CmbNumberCandidates.Text; if(!string.IsNullOrWhiteSpace(number)){ meta.SelectedNumber=number; meta.NumberManuallySet=true; } var date=CmbDateCandidates.Text; if(!string.IsNullOrWhiteSpace(date)){ meta.SelectedDate=date; meta.Date=date; meta.DateManuallySet=true; } meta.SavedDelimiter=TxtDelimiter.Text; meta.RemoveDuplicates=ChkRemoveDuplicates.IsChecked==true; meta.ReplaceSegments=ChkReplaceSegments.IsChecked==true; meta.RemoveAttachment=ChkRemoveAttachment.IsChecked==true; if(int.TryParse(TxtSegmentIndex.Text,out int segIdx)) meta.SavedSegmentIndex=segIdx; meta.UseDelimiterPosition=ChkUseDelimiterPosition.IsChecked==true; meta.InsertPositions.Clear(); if(CmbNumberPosition!=null && CmbNumberPosition.SelectedIndex<3) meta.InsertPositions.Add(new InsertPosition{Position=CmbNumberPosition.SelectedIndex+1,ValueType="Nummer (Auswahl)",Enabled=true}); if(CmbDatePosition!=null && CmbDatePosition.SelectedIndex<3) meta.InsertPositions.Add(new InsertPosition{Position=CmbDatePosition.SelectedIndex+1,ValueType="Datum",Enabled=true}); if(CmbDocTypePosition!=null && CmbDocTypePosition.SelectedIndex<3) meta.InsertPositions.Add(new InsertPosition{Position=CmbDocTypePosition.SelectedIndex+1,ValueType="Dokumenttyp",Enabled=true}); if(TxtEditableNewName!=null && TxtEditableNewName.IsFocused) meta.ManualFileName=TxtEditableNewName.Text.Trim(); RefreshNewNamePreview(meta); }

        private void RefreshNewNamePreview(DocumentMetadata meta){ if(meta==null) return; ChainedUpdates(()=>{ var raw=GenerateNewFileName(meta); var unique=EnsureUniquePreviewName(meta,raw); meta.ProposedNewName=unique; if(TxtEditableNewName!=null){ _isSettingPreviewName=true; try{ var sel=TxtEditableNewName.SelectionStart; TxtEditableNewName.Text=unique; TxtEditableNewName.SelectionStart=Math.Min(sel,unique.Length); } finally { _isSettingPreviewName=false; } } }); }
        private void ChainedUpdates(Action act){ _updateTimer?.Stop(); _updateTimer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(100)}; _updateTimer.Tick += (s,e)=>{ _updateTimer.Stop(); act(); }; _updateTimer.Start(); }

        private string GenerateNewFileName(DocumentMetadata meta){ if(!string.IsNullOrEmpty(meta.ManualFileName) && TxtEditableNewName!=null && TxtEditableNewName.IsFocused){ var sanitized=meta.ManualFileName; foreach(var c in InvalidFileNameChars) sanitized=sanitized.Replace(c,'-'); if(meta.RemoveAttachment) sanitized=RemoveTrailingAttachment(sanitized, meta.SavedDelimiter??"_"); return sanitized; } var delimiter=string.IsNullOrEmpty(meta.SavedDelimiter)?"_":meta.SavedDelimiter; var useDelPos=meta.UseDelimiterPosition; var replace=meta.ReplaceSegments; var segmentIndex=meta.SavedSegmentIndex??2; var original=Path.GetFileNameWithoutExtension(meta.FileName); if(meta.RemoveAttachment) original=RemoveTrailingAttachment(original, delimiter); var enabled=meta.InsertPositions.Where(p=>p.Enabled).OrderBy(p=>p.Position).ToList(); if(enabled.Count==0) return original; string result; if(replace){ var segments=original.Split(new[]{delimiter},StringSplitOptions.None).ToList(); var values=new List<string>(); foreach(var ins in enabled){ var v=GetValueForType(meta,ins.ValueType); if(!string.IsNullOrEmpty(v)) values.Add(v); } if(values.Count==0) result=original; else if(useDelPos){ int delimCount=0; int startPos=0; for(int i=0;i<original.Length;i++){ if(original[i].ToString()==delimiter){ delimCount++; if(delimCount==segmentIndex){ startPos=i+1; break; } } } var prefix=original.Substring(0,startPos); var suffix=startPos<original.Length?original.Substring(startPos):""; var suffixSegs=suffix.Split(new[]{delimiter},StringSplitOptions.None).ToList(); for(int i=0;i<values.Count && i<suffixSegs.Count;i++) suffixSegs[i]=values[i]; if(values.Count>suffixSegs.Count) for(int i=suffixSegs.Count;i<values.Count;i++) suffixSegs.Add(values[i]); result=prefix+string.Join(delimiter,suffixSegs.Where(s=>!string.IsNullOrEmpty(s))); } else { int startIdx=segmentIndex<0?0:segmentIndex; for(int i=0;i<values.Count;i++){ int target=startIdx+i; if(target<segments.Count) segments[target]=values[i]; else segments.Add(values[i]); } result=string.Join(delimiter,segments.Where(s=>!string.IsNullOrEmpty(s))); } }
        else if(useDelPos){ var values=new List<string>(); foreach(var ins in enabled){ var v=GetValueForType(meta,ins.ValueType); if(!string.IsNullOrEmpty(v)) values.Add(v); } if(values.Count==0) result=original; else { int delimCount=0; int insertPos=-1; for(int i=0;i<original.Length;i++){ if(original[i].ToString()==delimiter){ delimCount++; if(delimCount==segmentIndex){ insertPos=i+1; break; } } } if(insertPos==-1) result=original+delimiter+string.Join(delimiter,values); else { var prefix=original.Substring(0,insertPos); var suffix=original.Substring(insertPos); var inserted=string.Join(delimiter,values); result=prefix+inserted+(string.IsNullOrEmpty(suffix)?"":delimiter+suffix); } } }
        else { var segments=original.Split(new[]{delimiter},StringSplitOptions.None).ToList(); foreach(var ins in enabled.OrderByDescending(p=>p.Position)){ var v=GetValueForType(meta,ins.ValueType); if(!string.IsNullOrEmpty(v)){ int pos=ins.Position; if(pos<0) pos=0; if(pos>segments.Count) pos=segments.Count; segments.Insert(pos,v); } } result=string.Join(delimiter,segments.Where(s=>!string.IsNullOrEmpty(s))); }
        if(meta.RemoveDuplicates){ var segs=result.Split(new[]{delimiter},StringSplitOptions.RemoveEmptyEntries); var unique=new List<string>(); foreach(var s in segs) if(!unique.Contains(s,StringComparer.OrdinalIgnoreCase)) unique.Add(s); result=string.Join(delimiter,unique); } if(meta.RemoveAttachment) result=RemoveTrailingAttachment(result, delimiter); foreach(var c in InvalidFileNameChars) result=result.Replace(c,'-'); return result; }
        private string RemoveTrailingAttachment(string baseName,string delimiter){ if(string.IsNullOrEmpty(baseName)) return baseName; var segs=baseName.Split(new[]{delimiter},StringSplitOptions.None).ToList(); if(segs.Count==0) return baseName; var last=segs[^1]; if(last.Contains("_ocred",StringComparison.OrdinalIgnoreCase) || Regex.IsMatch(last,@"^(WA\d{2,}(_ocr(ed)?)?)$",RegexOptions.IgnoreCase)){ segs.RemoveAt(segs.Count-1); return string.Join(delimiter,segs); } return baseName; }
        private string? GetValueForType(DocumentMetadata meta,string type)=> type switch { "Rechnungsnummer"=>meta.InvoiceNumber, "Belegnummer"=>meta.DocumentNumber, "Nummer (Auswahl)"=>meta.SelectedNumber, "Datum"=>meta.SelectedDate??meta.Date, "Seiten"=>meta.PageCount.ToString(), "Dokumenttyp"=>meta.DocumentType, _=>null };

        private string? GetFirstNumberAfterDocType(string fullText,string docType,int targetDigits){ if(string.IsNullOrWhiteSpace(fullText)||string.IsNullOrWhiteSpace(docType)) return null; var match=Regex.Match(fullText,"\\b"+Regex.Escape(docType)+"\\b",RegexOptions.IgnoreCase); if(!match.Success) return null; int start=match.Index+match.Length; if(start>=fullText.Length) return null; var slice=fullText.Substring(start); List<string> candidates=new(); foreach(Match m in _environmentNumberRegex.Matches(slice)){ var c=m.Groups[1].Value.Trim(); if(IsValidNumber(c)) candidates.Add(c); } if(!candidates.Any()) return null; if(targetDigits>=4){ var exact=candidates.FirstOrDefault(c=>CountDigits(c)==targetDigits); if(!string.IsNullOrEmpty(exact)) return exact; var ge=candidates.FirstOrDefault(c=>CountDigits(c)>=targetDigits); if(!string.IsNullOrEmpty(ge)) return ge; } return candidates.First(); }

        private string? GetAppropriateNumber(DocumentMetadata meta,string? docType){ var validCandidates=meta.NumberCandidates.Where(IsValidNumber).ToList(); if(!meta.NumberManuallySet && !string.IsNullOrEmpty(docType) && !string.IsNullOrEmpty(meta.FullText)){ var after=GetFirstNumberAfterDocType(meta.FullText!,docType!,_globalMinDigits); if(!string.IsNullOrEmpty(after) && IsValidNumber(after)){ if(!meta.NumberCandidates.Contains(after)) meta.NumberCandidates.Insert(0,after); return after; } } if(!string.IsNullOrEmpty(docType) && !string.IsNullOrEmpty(meta.FullText) && !meta.NumberManuallySet){ int minDigitsForEnv=Math.Max(1,_globalMinDigits); var env=FindNumberNearDocType(meta.FullText!,docType!,minDigitsForEnv); if(!string.IsNullOrEmpty(env) && IsValidNumber(env)){ if(!meta.NumberCandidates.Contains(env)) meta.NumberCandidates.Insert(0,env); validCandidates=meta.NumberCandidates.Where(IsValidNumber).ToList(); if(CountDigits(env)>=_globalMinDigits || _globalMinDigits<=1) return env; } } if(!validCandidates.Any()) return null; if(_globalMinDigits>0){ var exact=validCandidates.FirstOrDefault(n=>CountDigits(n)==_globalMinDigits); if(exact!=null) return exact; var enough=validCandidates.Where(n=>CountDigits(n)>=_globalMinDigits).OrderBy(n=>CountDigits(n)).ToList(); if(enough.Any()) return enough.First(); for(int d=_globalMinDigits-1; d>=4; d--){ var fb=validCandidates.FirstOrDefault(n=>CountDigits(n)==d); if(fb!=null) return fb; } } return validCandidates.FirstOrDefault(); }

        private GridViewColumnHeader? _lastHeaderClicked; private ListSortDirection _lastDirection=ListSortDirection.Ascending;
        private void GridHeader_Click(object sender,RoutedEventArgs e){ if(e.OriginalSource is GridViewColumnHeader header && header.Tag is string sortProp && !string.IsNullOrEmpty(sortProp)){ var dir=ListSortDirection.Ascending; if(header==_lastHeaderClicked && _lastDirection==ListSortDirection.Ascending) dir=ListSortDirection.Descending; if(sortProp=="FileName" && _useNaturalSort){ var ordered=dir==ListSortDirection.Ascending? _items.OrderBy(m=>m.FileName,_naturalComparer).ToList(): _items.OrderByDescending(m=>m.FileName,_naturalComparer).ToList(); LvFiles.ItemsSource=ordered; } else { var view=System.Windows.Data.CollectionViewSource.GetDefaultView(LvFiles.ItemsSource); if(view!=null){ view.SortDescriptions.Clear(); view.SortDescriptions.Add(new SortDescription(sortProp,dir)); view.Refresh(); } } _lastHeaderClicked=header; _lastDirection=dir; } }
        private void UpdateStandardStatus(){ if(TxtStatus==null) return; var total=_items.Count; var sel=LvFiles.SelectedItems.Count; var current=_currentMeta?.FileName??""; if(total==0) TxtStatus.Text="Keine Dokumente geladen."; else if(sel==0) TxtStatus.Text=$"{total} Dokument(e) geladen."; else if(sel==1 && !string.IsNullOrEmpty(current)) TxtStatus.Text=$"{total} Dokument(e) | 1 ausgew�hlt: {current}"; else TxtStatus.Text=$"{total} Dokument(e) | {sel} ausgew�hlt"; }
        private string EnsureUniquePreviewName(DocumentMetadata meta,string candidate){ if(meta==null||string.IsNullOrWhiteSpace(candidate)) return candidate; var planned=new HashSet<string>(StringComparer.OrdinalIgnoreCase); foreach(var item in _items){ if(item==meta||!item.Loaded) continue; var baseName=!string.IsNullOrEmpty(item.ManualFileName)? item.ManualFileName : GenerateNewFileName(item); if(!string.IsNullOrWhiteSpace(baseName)) planned.Add(baseName); } var dir=Path.GetDirectoryName(meta.FilePath)??string.Empty; var ext=Path.GetExtension(meta.FilePath)??string.Empty; if(!planned.Contains(candidate) && !File.Exists(Path.Combine(dir,candidate+ext))) return candidate; int n=1; while(true){ var attempt=$"{candidate} ({n})"; if(!planned.Contains(attempt) && !File.Exists(Path.Combine(dir,attempt+ext))) return attempt; n++; } }
        private void RestoreSettings(DocumentMetadata meta){ _isPopulatingSelectors=true; try{ TxtDelimiter.Text=meta.SavedDelimiter??"_"; if(meta.SavedSegmentIndex.HasValue) TxtSegmentIndex.Text=meta.SavedSegmentIndex.Value.ToString(); ChkUseDelimiterPosition.IsChecked=meta.UseDelimiterPosition; ChkRemoveDuplicates.IsChecked=meta.RemoveDuplicates; ChkReplaceSegments.IsChecked=meta.ReplaceSegments; ChkRemoveAttachment.IsChecked=meta.RemoveAttachment; TxtMinDigits.Text=_globalMinDigits.ToString(); if(TxtDocTypeNumberSteps!=null && string.IsNullOrEmpty(TxtDocTypeNumberSteps.Text)) TxtDocTypeNumberSteps.Text=_docTypeNumberSteps.ToString(); if(meta.InsertPositions==null||meta.InsertPositions.Count==0){ meta.InsertPositions=new List<InsertPosition>{ new InsertPosition{Position=1,ValueType="Nummer (Auswahl)",Enabled=true}, new InsertPosition{Position=2,ValueType="Datum",Enabled=true}, new InsertPosition{Position=3,ValueType="Dokumenttyp",Enabled=true} }; } CmbNumberPosition.SelectedIndex=3; CmbDatePosition.SelectedIndex=3; CmbDocTypePosition.SelectedIndex=3; foreach(var pos in meta.InsertPositions.Where(p=>p.Enabled)){ if(pos.ValueType=="Nummer (Auswahl)") CmbNumberPosition.SelectedIndex=pos.Position-1; else if(pos.ValueType=="Datum") CmbDatePosition.SelectedIndex=pos.Position-1; else if(pos.ValueType=="Dokumenttyp") CmbDocTypePosition.SelectedIndex=pos.Position-1; } if(!string.IsNullOrEmpty(meta.ManualFileName)) TxtEditableNewName.Text=meta.ManualFileName; } finally { _isPopulatingSelectors=false; } }
        private void ShowDetails(DocumentMetadata meta){ TxtDetailInvoice.Text=meta.InvoiceNumber??"(keine)"; TxtDetailDocument.Text=meta.DocumentNumber??"(keine)"; TxtDetailDate.Text=meta.Date??"(kein)"; TxtDetailPages.Text=meta.PageCount.ToString(); var raw=string.IsNullOrEmpty(meta.FullText)?"(kein Text verf�gbar)":meta.FullText; if(_enableOcrCorrection && !string.IsNullOrEmpty(meta.FullText)) raw=ApplyOcrHeuristics(raw); var display=raw.Length>4000? raw.Substring(0,4000)+"..." : raw; TxtPreviewRich.Document.Blocks.Clear(); TxtPreviewRich.Document.Blocks.Add(new Paragraph(new Run(display))); ClearSearchHighlights(); }
        private string ApplyOcrHeuristics(string input)=> string.IsNullOrWhiteSpace(input)? input : OcrTextCorrector.Fix(input);
        private string? ExtractDateFromFileName(string fileName){ var match=Regex.Match(fileName,@"(\d{1,4})[-_.](\d{1,2})[-_.](\d{1,4})"); if(match.Success){ var year=match.Groups[1].Value; var month=match.Groups[2].Value; var day=match.Groups[3].Value; year=year.Length==2?"20"+year:year; month=month.PadLeft(2,'0'); day=day.PadLeft(2,'0'); return $"{day}.{month}.{year}"; } return null; }
        private string NormalizeDate(string dateStr){ if(string.IsNullOrWhiteSpace(dateStr)) return dateStr; dateStr=Regex.Replace(dateStr.Trim(),@"\s+"," "); if(DateTime.TryParseExact(dateStr,DateFormats,DeCulture,DateTimeStyles.AllowWhiteSpaces,out var dt)) return dt.ToString("dd.MM.yyyy",DeCulture); if(DateTime.TryParse(dateStr,DeCulture,DateTimeStyles.AllowWhiteSpaces,out dt)) return dt.ToString("dd.MM.yyyy",DeCulture); return dateStr; }
        private void ReapplyOcrAndReextract(DocumentMetadata meta){ if(meta==null||!meta.Loaded) return; meta.OriginalFullText??=meta.FullText; var source=meta.OriginalFullText??meta.FullText; if(string.IsNullOrEmpty(source)) return; meta.FullText=_enableOcrCorrection? ApplyOcrHeuristics(source): source; meta.NumberCandidates.Clear(); meta.DateCandidates.Clear(); if(!meta.NumberManuallySet){ meta.SelectedNumber=null; meta.InvoiceNumber=null; meta.DocumentNumber=null; } if(!meta.DateManuallySet){ meta.SelectedDate=null; meta.Date=null; } meta.DocumentType=null; var flag=meta.DocTypeManuallySet; meta.DocTypeManuallySet=false; if(!string.IsNullOrEmpty(meta.FullText)) ExtractCandidates(meta); meta.DocTypeManuallySet=flag; AutoAssignNumbers(meta); PopulateSelectors(meta); ShowDetails(meta); RefreshNewNamePreview(meta); LvFiles.Items.Refresh(); }
        private string? FindNumberNearDocType(string fullText,string docType,int minDigits){ if(!_docTypeNumberKeywords.TryGetValue(docType,out var keywords)||keywords.Length==0) return null; var pattern="\\b("+string.Join("|",keywords.Select(Regex.Escape))+")\\b"; var keyMatches=Regex.Matches(fullText,pattern,RegexOptions.IgnoreCase); if(keyMatches.Count==0) return null; foreach(Match km in keyMatches){ if(_docTypeNumberSearchForward){ int startIndex=km.Index+km.Length; if(startIndex>=fullText.Length) continue; var sliceLen=Math.Min(1000,fullText.Length-startIndex); var slice=fullText.Substring(startIndex,sliceLen); var tokenMatches=Regex.Matches(slice,@"\S+"); for(int t=0;t<Math.Min(_docTypeNumberSteps,tokenMatches.Count);t++){ var tok=tokenMatches[t].Value.Trim(); if(IsPotentialEnvironmentNumber(tok,minDigits)) return tok; var tmIndex=tokenMatches[t].Index; var m=_environmentNumberRegex.Match(slice,tmIndex); if(m.Success && IsPotentialEnvironmentNumber(m.Groups[1].Value,minDigits)) return m.Groups[1].Value; } var mAll=_environmentNumberRegex.Match(slice); if(mAll.Success && IsPotentialEnvironmentNumber(mAll.Groups[1].Value,minDigits)) return mAll.Groups[1].Value; } else { int endIndex=km.Index; if(endIndex<=0) continue; var sliceStart=Math.Max(0,endIndex-1000); var slice=fullText.Substring(sliceStart,endIndex-sliceStart); var tokenMatches=Regex.Matches(slice,@"\S+"); for(int t=1;t<=Math.Min(_docTypeNumberSteps,tokenMatches.Count);t++){ var idx=tokenMatches.Count-t; var tok=tokenMatches[idx].Value.Trim(); if(IsPotentialEnvironmentNumber(tok,minDigits)) return tok; var tmIndex=tokenMatches[idx].Index; var m=_environmentNumberRegex.Match(slice,tmIndex); if(m.Success && IsPotentialEnvironmentNumber(m.Groups[1].Value,minDigits)) return m.Groups[1].Value; } var all=_environmentNumberRegex.Matches(slice); if(all.Count>0){ var last=all.Cast<Match>().Last(); if(IsPotentialEnvironmentNumber(last.Groups[1].Value,minDigits)) return last.Groups[1].Value; } } } return null; }
        private void ReevaluateNumbersAfterDocTypeEnvChange(){ foreach(var item in _items.Where(m=>m.Loaded)){ if(!item.NumberManuallySet){ var appropriate=GetAppropriateNumber(item,item.DocumentType); if(!string.IsNullOrEmpty(appropriate)){ item.SelectedNumber=appropriate; AutoAssignNumbers(item); } } } if(_currentMeta is {Loaded:true} cur){ PopulateSelectors(cur); AutoAssignNumbers(cur); ShowDetails(cur); RefreshNewNamePreview(cur); } LvFiles.Items.Refresh(); UpdateStandardStatus(); }
        private bool IsPotentialEnvironmentNumber(string text,int minDigits){ if(string.IsNullOrWhiteSpace(text)) return false; if(!_environmentNumberRegex.IsMatch(text)) return false; var digits=new string(text.Where(char.IsDigit).ToArray()); if(digits.Length<minDigits) return false; return IsValidNumber(text); }

        // Suchfunktionalit�t
        private void StartSearchAsync(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                ClearSearchHighlights();
                return;
            }

            _searchVersion++;
            int version = _searchVersion;

            var docRange = new TextRange(TxtPreviewRich.Document.ContentStart, TxtPreviewRich.Document.ContentEnd);
            var all = docRange.Text;
            if (string.IsNullOrEmpty(all))
            {
                ClearSearchHighlights();
                return;
            }

            Task.Run(() =>
            {
                var hits = new List<(int start, int length)>();
                int idx = 0;
                while (true)
                {
                    idx = all.IndexOf(text, idx, StringComparison.OrdinalIgnoreCase);
                    if (idx < 0) break;
                    hits.Add((idx, text.Length));
                    idx += text.Length;
                }
                return hits;
            }).ContinueWith(t =>
            {
                if (version != _searchVersion) return;
                if (t.Status != TaskStatus.RanToCompletion)
                {
                    ClearSearchHighlights();
                    return;
                }
                ApplySearchResults(text, t.Result, version);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }
        private void ApplySearchResults(string searchText, List<(int start, int length)> spans, int version)
        {
            ClearSearchHighlights();
            _searchResults.Clear();
            _lastSearchText = searchText;

            if (spans.Count == 0)
            {
                UpdateSearchInfo();
                return;
            }

            var docStart = TxtPreviewRich.Document.ContentStart;

            foreach (var (start, len) in spans)
            {
                var tpStart = GetTextPointerAtOffset(docStart, start);
                var tpEnd = GetTextPointerAtOffset(docStart, start + len);

                if (tpStart == null || tpEnd == null) continue;

                var rng = new TextRange(tpStart, tpEnd);
                _searchResults.Add(rng);
                rng.ApplyPropertyValue(TextElement.BackgroundProperty, new SolidColorBrush(Colors.Yellow));
                rng.ApplyPropertyValue(TextElement.ForegroundProperty, new SolidColorBrush(Colors.Black));
            }

            _currentSearchIndex = 0;
            HighlightCurrentSearchResult();
            UpdateSearchInfo();
        }
        private void HighlightCurrentSearchResult()
        {
            if (_searchResults.Count == 0 || _currentSearchIndex < 0 || _currentSearchIndex >= _searchResults.Count)
            {
                UpdateSearchInfo();
                return;
            }

            foreach (var r in _searchResults)
            {
                r.ApplyPropertyValue(TextElement.BackgroundProperty, new SolidColorBrush(Colors.Yellow));
                r.ApplyPropertyValue(TextElement.ForegroundProperty, new SolidColorBrush(Colors.Black));
                r.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Normal);
            }

            var cur = _searchResults[_currentSearchIndex];
            cur.ApplyPropertyValue(TextElement.BackgroundProperty, new SolidColorBrush(Colors.Orange));
            cur.ApplyPropertyValue(TextElement.ForegroundProperty, new SolidColorBrush(Colors.White));
            cur.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Bold);

            try
            {
                var rect = cur.Start.GetCharacterRect(LogicalDirection.Forward);
                TxtPreviewRich.ScrollToVerticalOffset(Math.Max(rect.Top - 40, 0));
            }
            catch { }

            UpdateSearchInfo();
        }
        private void ClearSearchHighlights()
        {
            foreach (var r in _searchResults)
            {
                r.ApplyPropertyValue(TextElement.BackgroundProperty, null);
                r.ApplyPropertyValue(TextElement.ForegroundProperty, null);
                r.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Normal);
            }
            _searchResults.Clear();
            _currentSearchIndex = -1;
            UpdateSearchInfo();
        }
        private void UpdateSearchInfo()
        {
            if (TxtSearchInfo == null) return;

            if (_searchResults.Count == 0)
            {
                TxtSearchInfo.Text= string.IsNullOrEmpty(_lastSearchText)?"":"Keine Treffer";
            }
            else
            {
                TxtSearchInfo.Text=$"{_currentSearchIndex+1} von {_searchResults.Count}";
            }
        }
        private void PerformSearch(string text){ StartSearchAsync(text); }
        private TextPointer? GetTextPointerAtOffset(TextPointer start,int offset){ if(offset<=0) return start; int remaining=offset; var navigator=start; while(navigator!=null && remaining>0 && navigator.CompareTo(TxtPreviewRich.Document.ContentEnd)<0){ var ctx=navigator.GetPointerContext(LogicalDirection.Forward); if(ctx==TextPointerContext.Text){ int runLen=navigator.GetTextRunLength(LogicalDirection.Forward); if(remaining<=runLen) return navigator.GetPositionAtOffset(remaining); remaining-=runLen; navigator=navigator.GetPositionAtOffset(runLen); } else { navigator=navigator.GetNextContextPosition(LogicalDirection.Forward); } } return navigator; }

        private async Task RenameFileAsync(DocumentMetadata meta,HashSet<string>? already=null){ try{ string newName= meta==_currentMeta && !string.IsNullOrEmpty(TxtEditableNewName.Text.Trim())? TxtEditableNewName.Text.Trim() : GenerateNewFileName(meta); if(string.IsNullOrEmpty(newName)){ MessageBoxWpf.Show($"Neuer Dateiname f�r '{meta.FileName}' darf nicht leer sein."); return; } var dir=Path.GetDirectoryName(meta.FilePath)!; var ext=Path.GetExtension(meta.FilePath); var planned=new HashSet<string>(StringComparer.OrdinalIgnoreCase); if(already!=null) foreach(var r in already) planned.Add(r); foreach(var it in _items){ if(it==meta||!it.Loaded) continue; var plan=GenerateNewFileName(it); if(!string.IsNullOrEmpty(plan)) planned.Add(plan); } var newPath=Path.Combine(dir,newName+ext); if(File.Exists(newPath)||planned.Contains(newName)){ int c=1; var baseName=newName; do{ newName=$"{baseName} ({c})"; newPath=Path.Combine(dir,newName+ext); c++; } while(File.Exists(newPath)||planned.Contains(newName)); } File.Move(meta.FilePath,newPath); meta.FilePath=newPath; already?.Add(newName); TxtStatus.Text=$"Datei umbenannt: {newName}{ext}"; } catch(Exception ex){ MessageBoxWpf.Show($"Fehler beim Umbenennen: {ex.Message}"); } }
        private async void BtnRenameSelected_Click(object sender,RoutedEventArgs e){ var sel=LvFiles.SelectedItems.Cast<DocumentMetadata>().ToList(); if(sel.Count==0){ MessageBoxWpf.Show("Keine Auswahl."); return; } if(sel.Count>1 && MessageBoxWpf.Show($"{sel.Count} Dateien umbenennen?","Best�tigung",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes) return; if(_currentMeta!=null && sel.Contains(_currentMeta)) SaveCurrentSettings(_currentMeta); var renamed=new HashSet<string>(StringComparer.OrdinalIgnoreCase); foreach(var m in sel){ try{ if(!m.Loaded) await LoadMetadataAsync(m); await RenameFileAsync(m,renamed); } catch(Exception ex){ MessageBoxWpf.Show(ex.Message); } } UpdateStandardStatus(); }
        private async void BtnRenameAll_Click(object sender,RoutedEventArgs e){ if(_items.Count==0){ MessageBoxWpf.Show("Keine Dateien."); return; } if(MessageBoxWpf.Show($"Alle {_items.Count} Dateien umbenennen?","Best�tigung",MessageBoxButton.YesNo)!=MessageBoxResult.Yes) return; if(_currentMeta!=null) SaveCurrentSettings(_currentMeta); var renamed=new HashSet<string>(StringComparer.OrdinalIgnoreCase); foreach(var m in _items){ try{ if(!m.Loaded) await LoadMetadataAsync(m); await RenameFileAsync(m,renamed); } catch{} } UpdateStandardStatus(); }
        private void TxtMinDigits_TextChanged(object s,TextChangedEventArgs e){ if(_isPopulatingSelectors) return; if(int.TryParse(TxtMinDigits.Text,out var v) && v>=0){ _globalMinDigits=v; foreach(var item in _items.Where(m=>m.Loaded && !m.NumberManuallySet)){ var appropriate=GetAppropriateNumber(item,item.DocumentType); if(!string.IsNullOrEmpty(appropriate) && IsValidNumber(appropriate)){ item.SelectedNumber=appropriate; AutoAssignNumbers(item); } } if(_currentMeta is { Loaded:true } cur){ PopulateSelectors(cur); AutoAssignNumbers(cur); ShowDetails(cur); RefreshNewNamePreview(cur); } LvFiles.Items.Refresh(); UpdateStandardStatus(); } }
        public IEnumerable<DocumentMetadata> GetLoadedMetadata()=> _items.Where(m=>m.Loaded).ToList();
        public async Task RefreshCurrentFolder(){ var folder=TxtFolder.Text; if(Directory.Exists(folder)) await LoadFolderAsync(folder); }
        public List<string> GetSelectedFiles()=> LvFiles.SelectedItems.Cast<DocumentMetadata>().Where(m=>!string.IsNullOrEmpty(m.FilePath)&&Path.GetExtension(m.FilePath).Equals(".pdf",StringComparison.OrdinalIgnoreCase)).Select(m=>m.FilePath).ToList();
        public List<string> GetAllFiles()=> (LvFiles.ItemsSource as IEnumerable<DocumentMetadata> ?? _items).Where(m=>!string.IsNullOrEmpty(m.FilePath)&&Path.GetExtension(m.FilePath).Equals(".pdf",StringComparison.OrdinalIgnoreCase)).Select(m=>m.FilePath).ToList();
        public void PreviewWindowClosed(){ _openPreviewWindow=null; }
        public async Task UpdatePreviewWindowAsync(string filePath,string? fullText){ if(_openPreviewWindow!=null && !string.IsNullOrEmpty(filePath)) await _openPreviewWindow.LoadPdfAsync(filePath,fullText); }
        private TextPointer? GetTextPointerAtOffset(TextPointer start,int offset,bool dummy){ return GetTextPointerAtOffset(start,offset); }

        private void ToggleSizeUnit_Click(object sender,RoutedEventArgs e){ DocumentMetadata.UseMegaBytes=!DocumentMetadata.UseMegaBytes; foreach(var it in _items) it.RefreshSizeDisplay(); UpdateStandardStatus(); }
        private async void BtnAddToMerge_Click(object sender,RoutedEventArgs e){ var pdfMergeTab=PdfMergeTab; if(pdfMergeTab==null){ MessageBoxWpf.Show("Merge-Tab fehlt."); return; } var list=GetSelectedFiles(); if(list.Count==0){ MessageBoxWpf.Show("Keine PDF gew�hlt."); return; } foreach(var f in list) await pdfMergeTab.AddExternalFile(f); TxtStatus.Text=$"{list.Count} hinzugef�gt"; }
        private void ChkNaturalSort_Changed(object sender,RoutedEventArgs e){ _useNaturalSort=ChkNaturalSort.IsChecked==true; if(Directory.Exists(TxtFolder.Text)) _=LoadFolderAsync(TxtFolder.Text); }
        private void TxtFilter_TextChanged(object sender,TextChangedEventArgs e){ var ft=TxtFilter.Text; if(string.IsNullOrWhiteSpace(ft)) LvFiles.ItemsSource=_items; else LvFiles.ItemsSource=_items.Where(i=> i.FileName.Contains(ft,StringComparison.OrdinalIgnoreCase) || (i.InvoiceNumber?.Contains(ft,StringComparison.OrdinalIgnoreCase)??false) || (i.DocumentNumber?.Contains(ft,StringComparison.OrdinalIgnoreCase)??false)).ToList(); }
        private void CmbDocType_SelectionChanged(object s,SelectionChangedEventArgs e){ if(_isPopulatingSelectors) return; if(_currentMeta is { Loaded:true } meta){ meta.DocTypeManuallySet=true; UpdateMetadataFromSelections(meta); AutoAssignNumbers(meta); ShowDetails(meta); RefreshNewNamePreview(meta); } }
        private void EditableCombo_DropDownClosed(object sender,EventArgs e){ if(_currentMeta is { Loaded:true } cm){ SaveCurrentSettings(cm); AutoAssignNumbers(cm); ShowDetails(cm); RefreshNewNamePreview(cm); } }
        private void EditableCombo_KeyDown(object sender,System.Windows.Input.KeyEventArgs e){ if(e.Key==System.Windows.Input.Key.Enter){ if(sender is SWC.ComboBox combo){ combo.IsDropDownOpen=false; if(_currentMeta is { Loaded:true } cm){ SaveCurrentSettings(cm); AutoAssignNumbers(cm); ShowDetails(cm); RefreshNewNamePreview(cm); } } } }
        private void InsertPosition_Changed(object sender,RoutedEventArgs e){ if(_isPopulatingSelectors) return; if(_currentMeta is { Loaded:true } meta){ SaveCurrentSettings(meta); UpdateMetadataFromSelections(meta); RefreshNewNamePreview(meta); } }
        private void CmbNumberCandidates_SelectionChanged(object s,SelectionChangedEventArgs e){ if(_isPopulatingSelectors) return; if(_currentMeta is { Loaded:true } meta){ meta.NumberManuallySet=true; UpdateMetadataFromSelections(meta); AutoAssignNumbers(meta); ShowDetails(meta); RefreshNewNamePreview(meta); LvFiles.Items.Refresh(); } }
        private void CmbNumberCandidates_LostFocus(object s,RoutedEventArgs e)=>CmbNumberCandidates_SelectionChanged(s,null!);
        private void CmbNumberCandidates_TextChanged(object s,TextChangedEventArgs e)=>CmbNumberCandidates_SelectionChanged(s,null!);
        private void EditableCombo_PreviewMouseLeftButtonDown(object s,System.Windows.Input.MouseButtonEventArgs e){ }
        private void CmbDateCandidates_SelectionChanged(object s,SelectionChangedEventArgs e){ if(_isPopulatingSelectors) return; if(_currentMeta is { Loaded:true } meta){ meta.DateManuallySet=true; UpdateMetadataFromSelections(meta); AutoAssignNumbers(meta); ShowDetails(meta); RefreshNewNamePreview(meta); LvFiles.Items.Refresh(); } }
        private void CmbDateCandidates_LostFocus(object s,RoutedEventArgs e)=>CmbDateCandidates_SelectionChanged(s,null!);
        private void CmbDateCandidates_TextChanged(object s,TextChangedEventArgs e)=>CmbDateCandidates_SelectionChanged(s,null!);
        private void DelimiterRelated_Changed(object s,TextChangedEventArgs e){ if(_currentMeta is { Loaded:true } m){ SaveCurrentSettings(m); RefreshNewNamePreview(m); } }
        private void TxtSegmentIndex_TextChanged(object s,TextChangedEventArgs e){ if(_currentMeta is { Loaded:true } m){ SaveCurrentSettings(m); RefreshNewNamePreview(m); } }
        private void ChkUseDelimiterPosition_Changed(object s,RoutedEventArgs e){ if(_currentMeta is { Loaded:true } m){ SaveCurrentSettings(m); RefreshNewNamePreview(m); } }
        private void ChkRemoveDuplicates_Changed(object s,RoutedEventArgs e){ if(_currentMeta is { Loaded:true } m){ SaveCurrentSettings(m); RefreshNewNamePreview(m); } }
        private void ChkReplaceSegments_Changed(object s,RoutedEventArgs e){ if(_currentMeta is { Loaded:true } m){ SaveCurrentSettings(m); RefreshNewNamePreview(m); } }
        private void ChkRemoveAttachment_Changed(object s,RoutedEventArgs e){ if(_currentMeta is { Loaded:true } m){ SaveCurrentSettings(m); RefreshNewNamePreview(m); } }
        private void TxtEditableNewName_TextChanged(object s,TextChangedEventArgs e){ if(_isSettingPreviewName) return; if(_currentMeta is { Loaded:true } meta && TxtEditableNewName.IsFocused){ meta.ManualFileName=TxtEditableNewName.Text.Trim(); RefreshNewNamePreview(meta); } }
        private void CmbPdfPreviewMode_SelectionChanged(object s,SelectionChangedEventArgs e){ if(_currentMeta is { Loaded:true } meta) _=UpdatePdfPreviewAsync(meta); }
        private async void BtnOpenPdfInWindow_Click(object sender,RoutedEventArgs e){ if(_currentMeta==null||string.IsNullOrEmpty(_currentMeta.FilePath)){ MessageBoxWpf.Show("Bitte zuerst eine Datei w�hlen."); return; } if(_openPreviewWindow==null){ _openPreviewWindow=new PdfPreviewWindow{ Owner=this }; _openPreviewWindow.SetMainWindow(this); _openPreviewWindow.Show(); } await _openPreviewWindow.LoadPdfAsync(_currentMeta.FilePath,_currentMeta.FullText); }
        private void CmbDocTypeNumberDirection_SelectionChanged(object sender,SelectionChangedEventArgs e){ _docTypeNumberSearchForward=CmbDocTypeNumberDirection.SelectedIndex==0; ReevaluateNumbersAfterDocTypeEnvChange(); }
        private void TxtDocTypeNumberSteps_TextChanged(object sender,TextChangedEventArgs e){ if(int.TryParse(TxtDocTypeNumberSteps.Text,out var steps) && steps>0 && steps<500){ _docTypeNumberSteps=steps; ReevaluateNumbersAfterDocTypeEnvChange(); } }
        private void TxtSearch_TextChanged(object sender,TextChangedEventArgs e){ var search=TxtSearch.Text; if(_searchDebounceTimer==null){ _searchDebounceTimer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(250)}; _searchDebounceTimer.Tick += (s,ev)=>{ _searchDebounceTimer.Stop(); StartSearchAsync(TxtSearch.Text); }; } _searchDebounceTimer.Stop(); _searchDebounceTimer.Start(); }
        private void BtnSearchPrevious_Click(object sender,RoutedEventArgs e){ if(_searchResults.Count==0) return; _currentSearchIndex--; if(_currentSearchIndex<0) _currentSearchIndex=_searchResults.Count-1; HighlightCurrentSearchResult(); }
        private void BtnSearchNext_Click(object sender,RoutedEventArgs e){ if(_searchResults.Count==0) return; _currentSearchIndex++; if(_currentSearchIndex>=_searchResults.Count) _currentSearchIndex=0; HighlightCurrentSearchResult(); }
        private void BtnHighlightNumber_Click(object sender,RoutedEventArgs e){ if(_currentMeta==null) return; var num=CmbNumberCandidates.Text; if(string.IsNullOrWhiteSpace(num)){ MessageBoxWpf.Show("Keine Nummer."); return; } TxtSearch.Text=num; PerformSearch(num); }
        private void BtnHighlightDate_Click(object sender,RoutedEventArgs e){ if(_currentMeta==null) return; var dt=CmbDateCandidates.Text; if(string.IsNullOrWhiteSpace(dt)){ MessageBoxWpf.Show("Kein Datum."); return; } TxtSearch.Text=dt; PerformSearch(dt); }
        private void BtnClearHighlight_Click(object sender,RoutedEventArgs e){ TxtSearch.Text=""; ClearSearchHighlights(); }
    }
}

