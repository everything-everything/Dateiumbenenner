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
        /// <summary>A-Modus: Text wird aus dieser Datei statt aus FilePath gelesen.</summary>
        public string? TextSourcePath { get; set; }
        public string? OriginalFullText { get; set; }
        private string? _companyName; public string? CompanyName { get => _companyName; set { if (_companyName != value) { _companyName = value; OnPropertyChanged(nameof(CompanyName)); } } }
        public ObservableCollection<string> CompanyCandidates { get; } = new();
        /// <summary>Von der Firmenerkennung zuerst vorgeschlagener Name (ohne Zuweisungen von Hand).</summary>
        public string? DetectedCompany { get; set; }
        /// <summary>Früher zugewiesene Firmennamen (neueste zuerst), z. B. vor einer Korrektur oder Übertragung.</summary>
        public List<string> PreviousCompanies { get; set; } = new();
        /// <summary>Absenderadresse (nur bei Bedarf berechnet).</summary>
        public string? Street { get; set; }
        public string? Zip { get; set; }
        public string? City { get; set; }
        public bool AddressLoaded { get; set; }
        /// <summary>Adresse per „Firma übertragen“ bestätigt (auch leer) – wird nicht automatisch neu ermittelt.</summary>
        public bool AddressManuallySet { get; set; }
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
        /// <summary>Dateinamen-Aufbau wurde festgelegt (Vorlage/Benutzer) und wird in der Datenbank gespeichert.</summary>
        public bool LayoutSet { get; set; }
        public string? TemplateDeclined { get; set; }
        public List<InsertPosition> InsertPositions { get; set; } = new(){ new InsertPosition{ Position=1, ValueType="Nummer (Auswahl)", Enabled=true }, new InsertPosition{ Position=2, ValueType="Datum", Enabled=true }, new InsertPosition{ Position=3, ValueType="Dokumenttyp", Enabled=true } };
        public bool NumberManuallySet { get; set; }
        public bool DateManuallySet { get; set; }
        public bool DocTypeManuallySet { get; set; }
        /// <summary>Hintergrund-Gegenprüfung: "✓" bestätigt, "⚠" Abweichung, "–" nichts vergleichbar, leer = (noch) nicht geprüft.</summary>
        private string? _crossCheckFlag; public string? CrossCheckFlag { get => _crossCheckFlag; set { if (_crossCheckFlag != value) { _crossCheckFlag = value; OnPropertyChanged(nameof(CrossCheckFlag)); } } }
        private string? _crossCheckInfo; public string? CrossCheckInfo { get => _crossCheckInfo; set { if (_crossCheckInfo != value) { _crossCheckInfo = value; OnPropertyChanged(nameof(CrossCheckInfo)); } } }
        /// <summary>Stand der letzten Gegenprüfung (Textreferenz und Datum), um Änderungen zu erkennen.</summary>
        internal string? CrossCheckText; internal string? CrossCheckDate;
        public event PropertyChangedEventHandler? PropertyChanged; private void OnPropertyChanged(string n)=>PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(n));
        public void RefreshSizeDisplay()=>OnPropertyChanged(nameof(FileSizeDisplay));
    }
    public class NaturalFileNameComparer : IComparer<string>
    {
        public int Compare(string? a, string? b)
        { if (a == b) return 0; if (a == null) return -1; if (b == null) return 1; a = a.Replace('_','-'); b = b.Replace('_','-'); var ta = Tokenize(a); var tb = Tokenize(b); for (int i=0;i<Math.Min(ta.Count,tb.Count);i++){ var ca=ta[i]; var cb=tb[i]; int r = ca.isNum && cb.isNum ? ca.num.CompareTo(cb.num) : string.Compare(ca.text,cb.text,StringComparison.CurrentCultureIgnoreCase); if (r!=0) return r; } return ta.Count.CompareTo(tb.Count); }
        private List<(bool isNum,int num,string text)> Tokenize(string s){ var list=new List<(bool,int,string)>(); var sb=new StringBuilder(); bool num=s.Length>0 && char.IsDigit(s[0]); foreach(var ch in s){ bool isD=char.IsDigit(ch); if(isD!=num){ Flush(); num=isD; } sb.Append(ch);} Flush(); return list; void Flush(){ if(sb.Length==0) return; var str=sb.ToString(); if(num && int.TryParse(str,out var n)) list.Add((true,n,str)); else list.Add((false,0,str)); sb.Clear(); } }
    }

    public partial class MainWindow : Window, Dateiumbenenner.Plugins.IPluginHost
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
        private string? _lastPreviewPath;
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
            // Ausschlussregeln der Firma + Belegart: solche Nummern nie wählen
            var excluded=Dateiumbenenner.Engine.NumberRuleStore.Excluded(meta.CompanyName, meta.DocumentType, meta.NumberCandidates.Append(meta.SelectedNumber), meta.FullText);
            if(excluded.Count>0)
            {
                meta.NumberCandidates.RemoveAll(excluded.Contains);
                if(!meta.NumberManuallySet && meta.SelectedNumber!=null && excluded.Contains(meta.SelectedNumber.Trim())) meta.SelectedNumber=null;
            }
            // Verhindere �berschreiben einer manuell gesetzten Nummer
            if(!meta.NumberManuallySet && (string.IsNullOrEmpty(meta.SelectedNumber) || !IsValidNumber(meta.SelectedNumber)))
            { 
                var mapped=GetAppropriateNumber(meta,meta.DocumentType); 
                if(excluded.Count>0)
                {
                    meta.NumberCandidates.RemoveAll(excluded.Contains);
                    if(mapped!=null && excluded.Contains(mapped.Trim())) mapped=meta.NumberCandidates.FirstOrDefault(IsValidNumber);
                }
                if(!string.IsNullOrEmpty(mapped) && IsValidNumber(mapped))
                    meta.SelectedNumber=mapped; 
            }
            // Nummernfeld je Firma + Belegart (Belegarten & Nummernmuster bzw. Schnellaktion neben der Nummer)
            var target=Dateiumbenenner.Engine.NumberTargetStore.Get(meta.CompanyName, meta.DocumentType);
            if(target==Dateiumbenenner.Engine.NumberTarget.Keine)
            {
                meta.InvoiceNumber=null;
                meta.DocumentNumber=null;
            }
            else if(!string.IsNullOrEmpty(meta.DocumentType)&&!string.IsNullOrEmpty(meta.SelectedNumber))
            { 
                if(target==Dateiumbenenner.Engine.NumberTarget.Rechnungsnummer || target==Dateiumbenenner.Engine.NumberTarget.Automatisch && IsInvoiceType(meta.DocumentType))
                { 
                    meta.InvoiceNumber=meta.SelectedNumber; 
                    meta.DocumentNumber=null; 
                } 
                else if(target==Dateiumbenenner.Engine.NumberTarget.Belegnummer || IsDocType(meta.DocumentType))
                { 
                    meta.DocumentNumber=meta.SelectedNumber; 
                    meta.InvoiceNumber=null; 
                } 
            } 
            if(!string.IsNullOrEmpty(meta.SelectedDate)) 
                meta.Date=meta.SelectedDate; 
        }

        private readonly Regex _docTypeRegex = new(@"\b(Rechnung|Sammelrechnung|Stornorechnung|Beleg|Angebot|Lieferschein|Gutschrift|Bestellung|Mahnung|Barverkauf)\b", RegexOptions.IgnoreCase|RegexOptions.Compiled);
        private static readonly Regex KeyInvoice = new(@"\b(Rechnungs?\s*-?\s*(?:nummer|Nr\.?|No\.?)|Rg\.?\s*-?\s*Nr\.?|Re\.?\s*-?\s*Nr\.?|Gu\.?\s*-?\s*Nr\.?|Gutschrifts?\s*-?\s*(?:nummer|Nr\.?)|Storno\s*-?\s*(?:nummer|Nr\.?)|Invoice\s*(?:No\.?|Number))", RegexOptions.IgnoreCase|RegexOptions.Compiled);
        private static readonly Regex KeyDocNum = new(@"\b(Beleg\s*-?\s*(?:nummer|Nr\.?)|Angebots?\s*-?\s*(?:nummer|Nr\.?)|Auftrags?\s*-?\s*(?:nummer|Nr\.?)|Lieferschein\s*-?\s*(?:nummer|Nr\.?)|Bestell\s*-?\s*(?:nummer|Nr\.?))", RegexOptions.IgnoreCase|RegexOptions.Compiled);
        // "123456 / 2" -> "123456/2" (Leerzeichen um Schrägstrich entfernen, damit Zusatz erhalten bleibt)
        private static readonly Regex SlashSuffixRegex = new(@"(\d{4,})\s+/\s*(\d{1,4})\b|(\d{4,})\s*/\s+(\d{1,4})\b", RegexOptions.Compiled);
        private static string NormalizeSlash(string s) => string.IsNullOrEmpty(s) ? s : SlashSuffixRegex.Replace(s, m => m.Groups[1].Success ? m.Groups[1].Value + "/" + m.Groups[2].Value : m.Groups[3].Value + "/" + m.Groups[4].Value);
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

        public MainWindow()
        {
            ListViewSorter.Register();
            ListViewSearch.Register();
            InitializeComponent();
            Title += $" – Profil: {ProfileManager.ActiveProfile}";
            Dateiumbenenner.Engine.NumberFixStore.TextFilesChanged += OnTextFilesChanged;
            Closed += (_, _) => Dateiumbenenner.Engine.NumberFixStore.TextFilesChanged -= OnTextFilesChanged;
            if (LvFiles != null)
            {
                LvFiles.ItemsSource = _items;
                ListViewCopyHelper.Attach(LvFiles);
                LvFiles.AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler(GridHeader_Click));
            }
            if (CmbDocType != null)
            {
                CmbDocType.ItemsSource = Dateiumbenenner.Engine.DocTypeStore.Names;
                CmbDocType.IsEditable = true;
            }
            this.Loaded += (s, e) =>
            {
                MenuAMode.IsChecked = AMode;
                MenuClearOnExit.IsChecked = Dateiumbenenner.Engine.DataReset.ClearOnExit;
                _enableOcrCorrection = File.Exists(OcrFile);
                MenuOcrCorrection.IsChecked = _enableOcrCorrection;
                ChkOcrCorrection.IsChecked = _enableOcrCorrection;
                TxtLineWidth.Text = Dateiumbenenner.Engine.TextReflow.Width.ToString();
                var pdfMergeTab = FindName("PdfMergeTab") as PdfMergeControl;
                pdfMergeTab?.SetMainWindow(this);
                var prefixTab = FindName("PrefixRenamerTab") as PrefixRenamerControl;
                prefixTab?.SetMainWindow(this);
                prefixTab?.AttachExternalEditors(CmbMainCompany, TxtMainNewPrefix, TxtMainPrefixNewName);
                prefixTab?.AttachSplitInfo(TxtMainSplitInfo);
                if (TxtMainNewPrefix != null) TxtMainNewPrefix.TextChanged += (_, _) => { if (_currentMeta != null) RefreshNewNamePreview(_currentMeta); };
                if (CmbMainCompany != null) HookMainCompanyEditing();
                InitBackgroundServices();
                InitPlugins();
                var txtSteps = FindName("TxtDocTypeNumberSteps") as System.Windows.Controls.TextBox;
                if (txtSteps != null && string.IsNullOrEmpty(txtSteps.Text)) txtSteps.Text = _docTypeNumberSteps.ToString();
                _ = AskRestoreSessionAsync();
            };
        }

        private void ChkOcrCorrection_Changed(object sender,RoutedEventArgs e){ OcrCorrection=(sender as CheckBoxWpf)?.IsChecked==true; }

        public event EventHandler? LineWidthChanged;

        private void TxtLineWidth_KeyDown(object sender, System.Windows.Input.KeyEventArgs e) { if (e.Key == System.Windows.Input.Key.Enter) TxtLineWidth_LostFocus(sender, e); }

        private async void TxtLineWidth_LostFocus(object sender, RoutedEventArgs e)
        {
            bool changed = false;
            if (sender == TxtLineWidth && int.TryParse(TxtLineWidth.Text.Trim(), out var w) && w != Dateiumbenenner.Engine.TextReflow.Width)
            { Dateiumbenenner.Engine.TextReflow.Width = w; changed = true; }
            if (sender == TxtLineWidthFile && _currentMeta != null)
            {
                var t = TxtLineWidthFile.Text.Trim();
                int? fw = int.TryParse(t, out var v) ? v : null;
                if (fw != Dateiumbenenner.Engine.TextReflow.GetFileWidth(_currentMeta.FilePath))
                {
                    Dateiumbenenner.Engine.TextReflow.SetFileWidth(_currentMeta.FilePath, fw);
                    await LoadMetadataAsync(_currentMeta);
                    PopulateSelectors(_currentMeta); ShowDetails(_currentMeta); RefreshNewNamePreview(_currentMeta);
                    LvFiles.Items.Refresh();
                    LineWidthChanged?.Invoke(this, EventArgs.Empty);
                }
            }
            if (changed)
            {
                TxtStatus.Text = "Zeilenbreite geändert – Belege werden neu eingelesen...";
                foreach (var meta in _items.Where(m => m.Loaded).ToList()) await LoadMetadataAsync(meta);
                LvFiles.Items.Refresh();
                if (_currentMeta != null && _currentMeta.Loaded) { PopulateSelectors(_currentMeta); ShowDetails(_currentMeta); RefreshNewNamePreview(_currentMeta); }
                UpdateStandardStatus();
                LineWidthChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private static readonly string OcrFile = Path.Combine(Dateiumbenenner.Plugins.PluginManager.BaseDirectory, "ocr.flag");
        public event EventHandler? OcrCorrectionChanged;
        public bool OcrCorrection
        {
            get => _enableOcrCorrection;
            set
            {
                if (_enableOcrCorrection == value) return;
                _enableOcrCorrection = value;
                try { Directory.CreateDirectory(Path.GetDirectoryName(OcrFile)!); if (value) File.WriteAllText(OcrFile, "1"); else File.Delete(OcrFile); } catch { }
                if (ChkOcrCorrection != null && ChkOcrCorrection.IsChecked != value) ChkOcrCorrection.IsChecked = value;
                if (MenuOcrCorrection != null) MenuOcrCorrection.IsChecked = value;
                TxtStatus.Text = value ? "OCR-Korrektur wird angewendet..." : "OCR-Korrektur wird entfernt...";
                _ = ReapplyOcrAllAsync(value);
            }
        }

        private int _ocrRun;
        /// <summary>OCR-Korrektur schrittweise auf alle Belege anwenden – UI bleibt bedienbar und zeigt Fortschritt.</summary>
        private async Task ReapplyOcrAllAsync(bool value)
        {
            int run = ++_ocrRun;
            var list = _items.Where(m => m.Loaded).ToList();
            System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.AppStarting;
            try
            {
                TxtStatus.Text = value ? "OCR-Korrektur wird vorbereitet..." : "OCR-Korrektur wird entfernt...";
                foreach (var m in list) m.OriginalFullText ??= m.FullText;
                var sources = list.Select(m => m.OriginalFullText ?? m.FullText ?? "").ToArray();
                // rechenintensive Textkorrektur parallel im Hintergrund
                var texts = await Task.Run(() =>
                {
                    // erst den Wortschatz aller Belege lernen, damit die Wortkorrektur ihn schon beim ersten Beleg kennt
                    if (value) OcrTextCorrector.Learn(sources);
                    var r = new string[sources.Length];
                    Parallel.For(0, sources.Length, i => r[i] = value && !string.IsNullOrWhiteSpace(sources[i]) ? OcrTextCorrector.Fix(sources[i]) : sources[i]);
                    return r;
                });
                if (run != _ocrRun) return;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < list.Count; i++)
                {
                    if (run != _ocrRun) return;
                    ReapplyOcrAndReextract(list[i], texts[i], false);
                    if (sw.ElapsedMilliseconds > 50 || i == list.Count - 1)
                    {
                        TxtStatus.Text = $"{(value ? "OCR-Korrektur wird angewendet" : "OCR-Korrektur wird entfernt")}: {i + 1:N0} / {list.Count:N0}";
                        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
                        sw.Restart();
                    }
                }
                LvFiles.Items.Refresh();
                if (_currentMeta != null && _currentMeta.Loaded) { PopulateSelectors(_currentMeta); ShowDetails(_currentMeta); RefreshNewNamePreview(_currentMeta); _ = UpdatePdfPreviewAsync(_currentMeta); }
                UpdateStandardStatus();
                OcrCorrectionChanged?.Invoke(this, EventArgs.Empty);
            }
            finally { if (run == _ocrRun) System.Windows.Input.Mouse.OverrideCursor = null; }
        }
        private void MenuOcrCorrection_Click(object sender, RoutedEventArgs e) => OcrCorrection = MenuOcrCorrection.IsChecked;

        private static readonly string AModeFile = Path.Combine(Dateiumbenenner.Plugins.PluginManager.BaseDirectory, "amode.flag");
        private bool? _aMode;
        public event EventHandler? AModeChanged;
        public bool AMode
        {
            get => _aMode ??= File.Exists(AModeFile);
            set
            {
                if (AMode == value) return;
                _aMode = value;
                try { Directory.CreateDirectory(Path.GetDirectoryName(AModeFile)!); if (value) File.WriteAllText(AModeFile, "1"); else File.Delete(AModeFile); } catch { }
                if (MenuAMode != null) MenuAMode.IsChecked = value;
                AModeChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        private async void MenuAMode_Click(object sender, RoutedEventArgs e)
        {
            AMode = MenuAMode.IsChecked;
            if (!string.IsNullOrEmpty(TxtFolder.Text) && Directory.Exists(TxtFolder.Text)) await LoadFolderAsync(TxtFolder.Text);
        }

        private void MenuProfiles_Opened(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource != MenuProfiles) return;
            MenuProfiles.Items.Clear();
            var choose = new MenuItem { Header = "Profil _w\u00e4hlen" };
            foreach (var p in ProfileManager.GetProfiles())
            {
                var mi = new MenuItem { Header = p, IsCheckable = true, IsChecked = p.Equals(ProfileManager.ActiveProfile, StringComparison.OrdinalIgnoreCase) };
                mi.Click += (_, _) => SwitchProfile(p);
                choose.Items.Add(mi);
            }
            MenuProfiles.Items.Add(choose);
            var create = new MenuItem { Header = "_Neues Profil festlegen..." };
            create.Click += (_, _) => CreateProfile();
            MenuProfiles.Items.Add(create);
            MenuProfiles.Items.Add(new Separator());
            var folder = new MenuItem { Header = "Profil-_Ordner öffnen" };
            folder.Click += (_, _) => { try { System.Diagnostics.Process.Start("explorer.exe", ProfileManager.ActiveDirectory); } catch { } };
            MenuProfiles.Items.Add(folder);
            var clean = new MenuItem { Header = "Datenbank _bereinigen (fehlende Dateien)" };
            clean.Click += (_, _) => TxtStatus.Text = $"{DocumentStore.Cleanup()} veraltete Einträge entfernt.";
            MenuProfiles.Items.Add(clean);
        }

        private void CreateProfile()
        {
            int n = 1;
            while (ProfileManager.Exists(n.ToString())) n++;
            var name = Microsoft.VisualBasic.Interaction.InputBox("Name des neuen Profils:", "Neues Profil", n.ToString())?.Trim();
            if (string.IsNullOrEmpty(name)) return;
            var copy = MessageBoxWpf.Show($"Einstellungen des aktuellen Profils „{ProfileManager.ActiveProfile}“ übernehmen?\n(Nein = leeres Profil)",
                "Neues Profil", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (copy == MessageBoxResult.Cancel) return;
            try { ProfileManager.Create(name, copy == MessageBoxResult.Yes ? ProfileManager.ActiveProfile : null); }
            catch (Exception ex) { MessageBoxWpf.Show(ex.Message, "Neues Profil"); return; }
            SwitchProfile(name);
        }

        private void SwitchProfile(string name)
        {
            if (name.Equals(ProfileManager.ActiveProfile, StringComparison.OrdinalIgnoreCase)) return;
            if (MessageBoxWpf.Show($"Zu Profil „{name}“ wechseln? Das Programm wird dazu neu gestartet.", "Profil wechseln",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            ProfileManager.SetActive(name);
            var exe = Environment.ProcessPath;
            if (exe != null) System.Diagnostics.Process.Start(exe);
            System.Windows.Application.Current.Shutdown();
        }

        private void MenuExit_Click(object sender, RoutedEventArgs e) => Close();

        private void MenuClearOnExit_Click(object sender, RoutedEventArgs e) => Dateiumbenenner.Engine.DataReset.ClearOnExit = MenuClearOnExit.IsChecked;

        private void MenuClearNow_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBoxWpf.Show("Dokument-Datenbank und gelernte Daten (Firmen, Absenderprofile, Nummernformate, Dateinamen-Vorlagen) des aktiven Profils jetzt löschen?\n\nEinstellungen bleiben erhalten. Das Programm wird danach neu gestartet.",
                    "Daten leeren", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
            _clearNow = true;
            var exe = Environment.ProcessPath;
            Close();
            if (exe != null) System.Diagnostics.Process.Start(exe);
        }

        private bool _clearNow;

        private void MenuPassword_Opened(object sender, RoutedEventArgs e)
        {
            bool on = Dateiumbenenner.Engine.SecureStorage.IsEnabled;
            MenuPwSet.IsEnabled = !on;
            MenuPwChange.IsEnabled = on;
            MenuPwRemove.IsEnabled = on;
        }

        private void RestartAfterSecurityChange(string message)
        {
            MessageBoxWpf.Show(message + "\n\nDas Programm wird neu gestartet.", "Passwortschutz", MessageBoxButton.OK, MessageBoxImage.Information);
            var exe = Environment.ProcessPath;
            if (exe != null) System.Diagnostics.Process.Start(exe);
            System.Windows.Application.Current.Shutdown();
        }

        private void MenuPwSet_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBoxWpf.Show("Alle Profile werden jetzt verschlüsselt. Bitte vorher eine Sicherung des Ordners „Daten“ anlegen.\n\nFortfahren?",
                    "Passwortschutz", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
            var dlg = new PasswordDialog(PasswordDialog.Mode.Set) { Owner = this };
            if (dlg.ShowDialog() != true) return;
            System.Windows.Clipboard.SetText(dlg.RecoveryCode ?? "");
            RestartAfterSecurityChange($"Passwortschutz ist eingerichtet.\n\nWiederherstellungsschlüssel (bitte notieren und sicher aufbewahren – er wird nur jetzt angezeigt und wurde in die Zwischenablage kopiert):\n\n{dlg.RecoveryCode}\n\nOhne Passwort und ohne diesen Schlüssel sind die Daten nicht wiederherstellbar.");
        }

        private void MenuPwChange_Click(object sender, RoutedEventArgs e)
        {
            if (new PasswordDialog(PasswordDialog.Mode.Change) { Owner = this }.ShowDialog() == true)
                MessageBoxWpf.Show("Passwort geändert.", "Passwortschutz");
        }

        private void MenuPwRemove_Click(object sender, RoutedEventArgs e)
        {
            if (new PasswordDialog(PasswordDialog.Mode.Remove) { Owner = this }.ShowDialog() == true)
                RestartAfterSecurityChange("Passwortschutz entfernt, alle Daten sind wieder unverschlüsselt.");
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            if (_clearNow || Dateiumbenenner.Engine.DataReset.ClearOnExit) Dateiumbenenner.Engine.DataReset.Clear();
        }
        private void MenuHelp_Click(object sender, RoutedEventArgs e) => new HelpWindow(HelpWindow.Page.Help) { Owner = this }.ShowDialog();
        private void MenuLicenses_Click(object sender, RoutedEventArgs e) => new HelpWindow(HelpWindow.Page.Licenses) { Owner = this }.ShowDialog();
        private void MenuAbout_Click(object sender, RoutedEventArgs e) => new HelpWindow(HelpWindow.Page.About) { Owner = this }.ShowDialog();

        private async void BtnSelectFolder_Click(object sender,RoutedEventArgs e){ var dlg=new System.Windows.Forms.FolderBrowserDialog{ ClientGuid=new Guid("3B8E6E2A-5C1D-4F7A-9E21-6A1F2C9D0B02") }; if(dlg.ShowDialog()==System.Windows.Forms.DialogResult.OK){ TxtFolder.Text=dlg.SelectedPath; await LoadFolderAsync(dlg.SelectedPath); } }
        private int _loadVersion;
        private async void BtnRefreshFolder_Click(object sender,RoutedEventArgs e)=>await RefreshFolderAsync();

        private async void BtnMoveCopy_Click(object sender, RoutedEventArgs e)
        {
            var sel = LvFiles.SelectedItems.OfType<DocumentMetadata>().ToList();
            if (sel.Count == 0) { MessageBoxWpf.Show("Bitte zuerst Dateien in der Liste markieren.", "Markierte verschieben / kopieren"); return; }
            var dlg = new MoveCopyDialog(sel, _items.ToList(), TxtFolder.Text, LoadSessionValue("MoveCopy.Target"),
                LoadSessionValue("MoveCopy.Move") != "0", LoadSessionValue("MoveCopy.Xml") == "1", LoadSessionValue("MoveCopy.XmlMove") == "1") { Owner = this };
            if (dlg.ShowDialog() != true) return;
            bool move = dlg.Move;
            var target = dlg.Target;
            SaveSessionValue("MoveCopy.Target", target);
            SaveSessionValue("MoveCopy.Move", move ? "1" : "0");
            SaveSessionValue("MoveCopy.Xml", dlg.WithXml ? "1" : "0");
            SaveSessionValue("MoveCopy.XmlMove", dlg.XmlMove ? "1" : "0");
            var xmlFiles = dlg.WithXml ? dlg.XmlFiles : new List<(string Path, bool Shared)>();
            var xmlTarget = dlg.XmlTarget; bool xmlMove = dlg.XmlMove;
            var paths = sel.Select(m => m.FilePath).ToList();
            int total = paths.Count;
            var progress = new Progress<int>(n => TxtStatus.Text = n < total
                ? $"{(move ? "Verschiebe" : "Kopiere")} {n:N0} / {total:N0}..."
                : move ? "Datenbank wird aktualisiert..." : $"{n:N0} Datei(en) kopiert...");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var (done, errors, xml) = await Task.Run(() =>
            {
                var res = Dateiumbenenner.Engine.FileTransfer.Run(paths, target, move, progress);
                (int Moved, int Copied, int Same, List<string> Messages) x = xmlFiles.Count > 0 ? Dateiumbenenner.Engine.FileTransfer.TransferXml(xmlFiles, xmlTarget, xmlMove) : (0, 0, 0, new List<string>());
                return (res.Done, res.Errors, x);
            });
            if (move) RemoveItems(new HashSet<string>(done, StringComparer.OrdinalIgnoreCase));
            var status = $"{done.Count:N0} Datei(en) {(move ? "verschoben" : "kopiert")} nach {target} ({sw.Elapsed.TotalSeconds:0.0} s).";
            if (xmlFiles.Count > 0)
                status += $" XML: {xml.Moved} verschoben, {xml.Copied} kopiert" + (xml.Same > 0 ? $", {xml.Same} bereits vorhanden" : "") + $" nach {xmlTarget}.";
            if (errors.Count > 0) status += $" {errors.Count} Fehler.";
            TxtStatus.Text = status;
            var messages = errors.Concat(xml.Messages).ToList();
            if (messages.Count > 0)
                MessageBoxWpf.Show(string.Join("\n", messages.Take(25)) + (messages.Count > 25 ? $"\n... und {messages.Count - 25} weitere" : ""), (errors.Count > 0 ? "Fehler beim " : "Hinweise zum ") + (move ? "Verschieben" : "Kopieren"), MessageBoxButton.OK, errors.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }

        /// <summary>Einträge gesammelt aus der Liste entfernen; die Liste ist dabei abgekoppelt, damit nicht jede einzelne
        /// Entfernung Auswahl-/Vorschau-Ereignisse auslöst.</summary>
        private void RemoveItems(ISet<string> paths)
        {
            if (paths.Count == 0) return;
            var bound = LvFiles.ItemsSource; LvFiles.ItemsSource = null;
            var keep = _items.Where(m => !paths.Contains(m.FilePath)).ToList();
            if (keep.Count < _items.Count)
            {
                if (keep.Count < _items.Count / 2) { _items.Clear(); foreach (var m in keep) _items.Add(m); }
                else foreach (var m in _items.Where(m => paths.Contains(m.FilePath)).ToList()) _items.Remove(m);
            }
            if (bound is List<DocumentMetadata> shown) shown.RemoveAll(m => paths.Contains(m.FilePath));
            LvFiles.ItemsSource = bound ?? _items;
        }
        protected override async void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e){ base.OnPreviewKeyDown(e); if(e.Key==System.Windows.Input.Key.F5 && !e.Handled){ e.Handled=true; await RefreshFolderAsync(); } }

        /// <summary>Ordner neu laden; ausgewählte Datei bleibt (falls noch vorhanden) ausgewählt.</summary>
        private async Task RefreshFolderAsync(){
            var folder=TxtFolder.Text; if(string.IsNullOrEmpty(folder)||!Directory.Exists(folder)){ TxtStatus.Text="Kein Ordner gewählt."; return; }
            var sel=_currentMeta?.FilePath;
            await LoadFolderAsync(folder);
            if(sel!=null){ var m=_items.FirstOrDefault(i=>string.Equals(i.FilePath,sel,StringComparison.OrdinalIgnoreCase)); if(m!=null){ LvFiles.SelectedItem=m; LvFiles.ScrollIntoView(m); } }
        }

        private FileSystemWatcher? _watcher; private System.Windows.Threading.DispatcherTimer? _watchTimer;
        /// <summary>Vom Watcher gemeldete neu geschriebene Dateien (z. B. ersetzte PDF/TXT mit besserer OCR).</summary>
        private readonly HashSet<string> _changedFiles=new(StringComparer.OrdinalIgnoreCase);
        private void ChkWatchFolder_Changed(object sender,RoutedEventArgs e)=>SetupWatcher();
        private void SetupWatcher(){
            _watcher?.Dispose(); _watcher=null;
            var folder=TxtFolder?.Text;
            if(ChkWatchFolder?.IsChecked!=true || string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
            try{
                _watcher=new FileSystemWatcher(folder){ IncludeSubdirectories=false, InternalBufferSize=64*1024, NotifyFilter=NotifyFilters.FileName|NotifyFilters.LastWrite|NotifyFilters.Size };
                FileSystemEventHandler h=(s,ev)=>OnFolderChanged(); FileSystemEventHandler w=(s,ev)=>{ lock(_changedFiles) _changedFiles.Add(ev.FullPath); OnFolderChanged(); }; _watcher.Created+=w; _watcher.Deleted+=h; _watcher.Changed+=w; _watcher.Renamed+=(s,ev)=>OnFolderChanged(); _watcher.Error+=(s,ev)=>OnFolderChanged();
                _watcher.EnableRaisingEvents=true;
            }catch(Exception ex){ TxtStatus.Text="Ordnerüberwachung nicht möglich: "+ex.Message; }
        }
        private void OnFolderChanged()=>Dispatcher.BeginInvoke(()=>{
            if(_watchTimer==null){ _watchTimer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromSeconds(1.5)}; _watchTimer.Tick+=async(s,e)=>{ _watchTimer.Stop(); if(_isBulkLoading){ _watchTimer.Start(); return; } await SyncFolderAsync(); }; }
            _watchTimer.Stop(); _watchTimer.Start();
        });
        /// <summary>Nur Differenzen abgleichen: fehlende Dateien entfernen, bei neuen Dateien neu laden (DB liefert bekannte Belege sofort).</summary>
        private async Task SyncFolderAsync(){
            var folder=TxtFolder.Text; if(string.IsNullOrEmpty(folder)||!Directory.Exists(folder)) return;
            var exts=AMode? new[]{".pdf"} : new[]{".pdf",".txt",".csv"};
            var known=_items.Select(i=>i.FilePath).ToList();
            string[] changed; lock(_changedFiles){ changed=_changedFiles.ToArray(); _changedFiles.Clear(); }
            var (gone,added)=await Task.Run(()=>{ var now=new HashSet<string>(Directory.EnumerateFiles(folder).Where(f=>exts.Contains(Path.GetExtension(f).ToLowerInvariant())),StringComparer.OrdinalIgnoreCase); var k=new HashSet<string>(known,StringComparer.OrdinalIgnoreCase); return (known.Where(p=>!now.Contains(p)).ToHashSet(StringComparer.OrdinalIgnoreCase), now.Count(p=>!k.Contains(p))); });
            if(added>0){ TxtStatus.Text=$"{added:N0} neue Datei(en) erkannt – Liste wird aktualisiert..."; await RefreshFolderAsync(); return; }
            // Ersetzte Belege bzw. (A-Modus) ersetzte TXT: neu laden, unveränderte Belege kommen direkt aus der Datenbank
            var knownSet=new HashSet<string>(known,StringComparer.OrdinalIgnoreCase);
            int modified=changed.Count(p=>File.Exists(p) && (knownSet.Contains(p) || AMode && Path.GetExtension(p).Equals(".txt",StringComparison.OrdinalIgnoreCase) && knownSet.Contains(Path.ChangeExtension(p,".pdf"))));
            if(modified>0){ TxtStatus.Text=$"{modified:N0} geänderte Datei(en) erkannt – Liste wird aktualisiert..."; await RefreshFolderAsync(); return; }
            if(gone.Count==0) return;
            RemoveItems(gone);
            TxtStatus.Text=$"{gone.Count:N0} Datei(en) aus dem Ordner entfernt/verschoben – Liste angepasst ({_items.Count:N0} Dateien).";
        }

        private async Task LoadFolderAsync(string folder)
        {
            int loadVersion=++_loadVersion;
            SaveSessionValue(SessionMainFolderKey, folder);
            _items.Clear();
            if(LoadingProgress!=null){ LoadingProgress.Visibility=Visibility.Visible; LoadingProgress.IsIndeterminate=true; }
            bool aMode=AMode;
            var exts=aMode? new[]{".pdf"} : new[]{".pdf",".txt",".csv"};
            var naturalSort=_useNaturalSort;
            var metas=await Task.Run(()=>{
                var fis=new DirectoryInfo(folder).EnumerateFiles().Where(f=>exts.Contains(f.Extension.ToLowerInvariant())).ToList();
                fis=naturalSort? fis.OrderBy(f=>f.Name,_naturalComparer).ToList(): fis.OrderBy(f=>f.FullName).ToList();
                HashSet<string>? txts=null;
                if(aMode) txts=new HashSet<string>(Directory.EnumerateFiles(folder,"*.txt"),StringComparer.OrdinalIgnoreCase);
                return fis.Select(fi=>{ var f=fi.FullName; var meta=new DocumentMetadata{ FilePath=f, FileSizeBytes=fi.Length, Dpi=fi.Extension.Equals(".pdf",StringComparison.OrdinalIgnoreCase)?72:null, SavedDelimiter=null, SavedSegmentIndex=2, SavedInsertValueIndex=0, DelimiterAutoDetected=true, UseDelimiterPosition=true }; if(txts!=null){ var txt=Path.ChangeExtension(f,".txt"); if(txts.Contains(txt)) meta.TextSourcePath=txt; } return meta; }).ToList();
            });
            if(loadVersion!=_loadVersion) return;
            int total=metas.Count, processed=0;
            _isBulkLoading=true;
            int fromDbLive=0;
            var sw=System.Diagnostics.Stopwatch.StartNew();
            var progress=new Progress<int>(n=>{
                if(LoadingProgress!=null && total>0) LoadingProgress.Value=n*100.0/total;
                var eta=n>0? TimeSpan.FromSeconds(sw.Elapsed.TotalSeconds*(total-n)/n) : TimeSpan.Zero;
                TxtStatus.Text=$"Beleg {n:N0} / {total:N0} ({(total>0?n*100.0/total:0):0.0} %) – aus Datenbank: {Volatile.Read(ref fromDbLive):N0} – Rest ca. {eta:hh\\:mm\\:ss}"; });
            // Parallel: PDF-Text lesen + Regex-Auswertung auf mehreren Kernen (Objekte sind noch nicht in der UI-Liste)
            // Alle Kerne bis auf einen (wie im Plugin): ein Kern bleibt für die Oberfläche frei
            var options=new ParallelOptions{ MaxDegreeOfParallelism=Math.Max(2,Environment.ProcessorCount-1) };
            var companiesByMeta=new List<string>?[metas.Count];
            var storedCompany=new string?[metas.Count];
            var toSave=new bool[metas.Count];
            var confirmedArr=new bool[metas.Count];
            int fromDb=0;
            if(LoadingProgress!=null){ LoadingProgress.IsIndeterminate=false; LoadingProgress.Value=0; }
            TxtStatus.Text=$"{total:N0} Dateien gefunden – Datenbank wird gelesen...";
            // Gespeicherte Stände des Ordners gebündelt vorab lesen (eine Abfrage statt einer je Datei)
            using var prefetch=await Task.Run(()=>DocumentStore.Prefetch(metas.Select(m=>m.FilePath), true));
            if(loadVersion!=_loadVersion) return;
            TxtStatus.Text=$"{total:N0} Dateien gefunden – Auswertung startet...";
            var indexed=metas.Select((m,i)=>(m,i)).ToList();
            await Parallel.ForEachAsync(indexed, options, async (x,_)=>{
                var key=StoreSettingsKey(x.m.FilePath);
                var rec=DocumentStore.TryGet(x.m.FilePath, key, out var upToDate);
                confirmedArr[x.i]=rec?.CompanyConfirmed==true;
                if(rec!=null && upToDate)
                {
                    ApplyStoreRecord(x.m, rec);
                    companiesByMeta[x.i]=TextCandidates(rec.CompanyCandidates, rec.FullText);
                    // Gespeicherter Stand ohne Firmenvorschläge (z. B. aus fehlerhaftem Lauf): Firmen neu ermitteln und nachspeichern
                    if(companiesByMeta[x.i]!.Count==0 && string.IsNullOrWhiteSpace(rec.CompanyName) && !string.IsNullOrEmpty(rec.FullText))
                    {
                        try{ companiesByMeta[x.i]=ExtractCompanyNames(rec.FullText, x.m.FilePath); toSave[x.i]=true; } catch{ }
                    }
                    // Neue Regeln (z. B. Firmenname aus Webadresse) auch für bereits gespeicherte Belege ergänzen
                    try{ var dn=new List<string>(); AddDomainNames(dn, rec.FullText); foreach(var c in dn) if(!companiesByMeta[x.i]!.Contains(c, StringComparer.OrdinalIgnoreCase)) companiesByMeta[x.i]!.Add(c); } catch{ }
                    storedCompany[x.i]=rec.CompanyName;
                    Interlocked.Increment(ref fromDb); Interlocked.Increment(ref fromDbLive);
                }
                else
                {
                    // Datei neu/geändert: neu auswerten, manuell gesetzte Werte aus der Datenbank übernehmen
                    if(rec!=null) ApplyManualValues(x.m, rec);
                    await LoadMetadataAsync(x.m);
                    if(rec!=null) confirmedArr[x.i]=KeepFittingManualValues(x.m, rec);
                    // Firmenerkennung ebenfalls parallel (vorher seriell auf dem UI-Thread -> Hänger)
                    try{ companiesByMeta[x.i]=ExtractCompanyNames(x.m.FullText, x.m.FilePath); } catch{ }
                    if(confirmedArr[x.i]) storedCompany[x.i]=rec!.CompanyName;
                    toSave[x.i]=true;
                }
                int done=Interlocked.Increment(ref processed); if((done&15)==0 || done==total) ((IProgress<int>)progress).Report(done); });
            // Wurde inzwischen neu geladen (z. B. Sortierung/A-Modus umgeschaltet), dieses Ergebnis verwerfen
            if(loadVersion!=_loadVersion) return;
            // UI-Thread: Nummern zuordnen und Liste in Originalreihenfolge in einem Durchgang füllen
            var boundSource=LvFiles.ItemsSource; LvFiles.ItemsSource=null;
            for(int mi=0; mi<metas.Count; mi++){ var meta=metas[mi]; AutoAssignNumbers(meta); try{ meta.CompanyCandidates.Clear(); foreach(var c in companiesByMeta[mi] ?? new List<string>()) meta.CompanyCandidates.Add(c); meta.DetectedCompany=companiesByMeta[mi]?.FirstOrDefault(); if(!string.IsNullOrWhiteSpace(storedCompany[mi]) && !meta.CompanyCandidates.Contains(storedCompany[mi]!)) meta.CompanyCandidates.Insert(0, storedCompany[mi]!); meta.CompanyName=string.IsNullOrWhiteSpace(storedCompany[mi]) ? meta.CompanyCandidates.FirstOrDefault() : storedCompany[mi]; } catch{ } meta.PropertyChanged-=Meta_CompanyChanged; meta.PropertyChanged+=Meta_CompanyChanged; _lastCompany[meta]=meta.CompanyName; _items.Add(meta); }
            LvFiles.ItemsSource=boundSource??_items;
            var newMetas=metas.Select((m,i)=>(m,i)).Where(x=>toSave[x.i]).Select(x=>(x.m, key:StoreSettingsKey(x.m.FilePath), confirmed:confirmedArr[x.i])).ToList();
            foreach(var x in metas.Select((m,i)=>(m,i)).Where(x=>confirmedArr[x.i])) _confirmedCompany.Add(x.m);
            if(newMetas.Count>0) _ = Task.Run(()=>{ foreach(var x in newMetas) SaveToStore(x.m, x.key, x.confirmed); });
            // Präfix-Bereich
            try{ PrefixRenamerTab?.LoadFilesFromMain(metas); } catch{ }
            _isBulkLoading=false;
            if(LoadingProgress!=null) LoadingProgress.Visibility=Visibility.Collapsed;
            UpdateStandardStatus();
            if(total>0) TxtStatus.Text+=$"  |  Profil {ProfileManager.ActiveProfile}: {fromDb} aus Datenbank, {total-fromDb} neu ausgewertet";
            OfferTemplates(metas);
        }

        private string StoreSettingsKey(string path) =>
            $"{AMode}|{(_enableOcrCorrection ? "True|" + OcrTextCorrector.AlgorithmTag : "False")}|{Dateiumbenenner.Engine.TextReflow.Width}|{Dateiumbenenner.Engine.TextReflow.GetFileWidth(path)}{TextSourceKey(path)}";

        /// <summary>A-Modus: Der Text stammt aus der gleichnamigen TXT – deren Inhalt gehört zum Schlüssel, damit eine ersetzte TXT neu ausgewertet wird.</summary>
        private string TextSourceKey(string path)
        {
            if(!AMode) return "";
            try{ var txt=Path.ChangeExtension(path,".txt"); return File.Exists(txt) ? "|txt:"+DocumentStore.Stamp(txt, out _, out _) : ""; }
            catch{ return ""; }
        }

        private void ApplyManualValues(DocumentMetadata m, DocumentStore.Record r)
        {
            if(r.NumberManuallySet){ m.SelectedNumber=r.SelectedNumber; m.NumberManuallySet=true; }
            if(r.DateManuallySet){ m.SelectedDate=r.SelectedDate; m.Date=r.Date; m.DateManuallySet=true; }
            if(r.DocTypeManuallySet){ m.DocumentType=r.DocumentType; m.DocTypeManuallySet=true; }
            m.PreviousCompanies=r.PreviousCompanies.ToList();
            ApplyStoredLayout(m, r);
        }

        /// <summary>
        /// Nach dem Neuauswerten einer geänderten/ersetzten Datei: übernommene Handwerte des alten Stands nur behalten, wenn sie zum neuen Inhalt passen
        /// (gleichnamige neue Datei darf keine Werte des Vorgängers erben). Liefert, ob die bestätigte Firma weiter gilt.
        /// </summary>
        private bool KeepFittingManualValues(DocumentMetadata m, DocumentStore.Record r)
        {
            var text = m.FullText ?? "";
            bool sameDoc = SameDocumentText(r.FullText, text);
            bool reextract = false;
            if (m.NumberManuallySet && !FitsContent(m.SelectedNumber, m.NumberCandidates, text)) { m.NumberManuallySet = false; m.SelectedNumber = null; reextract = true; }
            if (m.DateManuallySet && !FitsContent(m.SelectedDate, m.DateCandidates, text)) { m.DateManuallySet = false; m.SelectedDate = null; m.Date = null; reextract = true; }
            if (m.DocTypeManuallySet && !sameDoc) { m.DocTypeManuallySet = false; m.DocumentType = null; reextract = true; }
            // Frühere Firmennamen gehören zum ersetzten Inhalt
            if (!sameDoc) m.PreviousCompanies.Clear();
            if (reextract && !string.IsNullOrEmpty(m.FullText)) { m.NumberCandidates.Clear(); m.DateCandidates.Clear(); ExtractCandidates(m); }
            return r.CompanyConfirmed && !string.IsNullOrWhiteSpace(r.CompanyName) && (sameDoc || CompanyMatcher.AppearsInText(r.CompanyName!, text));
        }

        private static bool FitsContent(string? value, List<string> candidates, string text) =>
            !string.IsNullOrWhiteSpace(value) && (candidates.Contains(value) || text.Contains(value, StringComparison.OrdinalIgnoreCase));

        private static readonly Regex DocWordRegex = new(@"[\p{L}\d]{3,}", RegexOptions.Compiled);

        /// <summary>Gleicher Beleg (z. B. neu gespeichert/OCR wiederholt): nahezu gleicher Wortbestand.</summary>
        private static bool SameDocumentText(string? oldText, string? newText)
        {
            if (string.IsNullOrWhiteSpace(oldText) || string.IsNullOrWhiteSpace(newText)) return false;
            if (string.Equals(oldText, newText, StringComparison.Ordinal)) return true;
            static HashSet<string> Words(string t) => DocWordRegex.Matches(t).Select(x => x.Value.ToLowerInvariant()).ToHashSet();
            var a = Words(oldText); var b = Words(newText);
            if (a.Count == 0 || b.Count == 0) return false;
            return a.Count(b.Contains) >= 0.85 * Math.Max(a.Count, b.Count);
        }

        private void ApplyStoredLayout(DocumentMetadata m, DocumentStore.Record r)
        {
            m.TemplateDeclined=r.TemplateDeclined;
            if(r.Layout==null) return;
            ApplyLayout(m, r.Layout);
            if(m.DelimiterAutoDetected) m.SavedDelimiter=DetectDelimiterFromFilename(m.FileName);
        }

        private static void ApplyLayout(DocumentMetadata m, Dateiumbenenner.Engine.DocTemplate t)
        {
            m.SavedDelimiter = t.Delimiter;
            m.DelimiterAutoDetected = string.IsNullOrEmpty(t.Delimiter);
            m.SavedSegmentIndex = t.SegmentIndex;
            m.UseDelimiterPosition = t.UseDelimiterPosition;
            m.RemoveDuplicates = t.RemoveDuplicates;
            m.ReplaceSegments = t.ReplaceSegments;
            m.RemoveAttachment = t.RemoveAttachment;
            if (t.InsertPositions.Count > 0)
                m.InsertPositions = t.InsertPositions.Select(p => new InsertPosition { Position = p.Position, ValueType = p.ValueType, Enabled = p.Enabled }).ToList();
            m.LayoutSet = true;
        }

        private static Dateiumbenenner.Engine.DocTemplate CaptureLayout(DocumentMetadata m) => new()
        {
            Delimiter = m.DelimiterAutoDetected ? null : m.SavedDelimiter,
            SegmentIndex = m.SavedSegmentIndex,
            UseDelimiterPosition = m.UseDelimiterPosition,
            RemoveDuplicates = m.RemoveDuplicates,
            ReplaceSegments = m.ReplaceSegments,
            RemoveAttachment = m.RemoveAttachment,
            InsertPositions = m.InsertPositions.Select(p => new Dateiumbenenner.Engine.TemplatePosition { Position = p.Position, ValueType = p.ValueType, Enabled = p.Enabled }).ToList()
        };

        private void ApplyStoreRecord(DocumentMetadata m, DocumentStore.Record r)
        {
            m.FullText=r.FullText; m.PageCount=r.PageCount;
            m.DocumentType=r.DocumentType; m.InvoiceNumber=r.InvoiceNumber; m.DocumentNumber=r.DocumentNumber; m.Date=r.Date;
            m.SelectedNumber=r.SelectedNumber; m.SelectedDate=r.SelectedDate;
            m.NumberCandidates=new List<string>(r.NumberCandidates); m.DateCandidates=new List<string>(r.DateCandidates);
            m.NumberManuallySet=r.NumberManuallySet; m.DateManuallySet=r.DateManuallySet; m.DocTypeManuallySet=r.DocTypeManuallySet;
            if(!string.IsNullOrWhiteSpace(r.Street)||!string.IsNullOrWhiteSpace(r.City)||r.AddressManuallySet){ m.Street=r.Street; m.Zip=r.Zip; m.City=r.City; m.AddressLoaded=true; }
            m.AddressManuallySet=r.AddressManuallySet;
            m.PreviousCompanies=r.PreviousCompanies.ToList();
            if(string.IsNullOrEmpty(m.SavedDelimiter)||m.DelimiterAutoDetected){ m.SavedDelimiter=DetectDelimiterFromFilename(m.FileName); m.DelimiterAutoDetected=true; }
            ApplyStoredLayout(m, r);
            m.Loaded=true;
        }

        /// <summary>Nur erkannte Namen, die im Beleg stehen (bzw. eigene Firma) – übertragene/zugewiesene Namen gehören nicht in die Liste.</summary>
        private static List<string> TextCandidates(IEnumerable<string> list, string? fullText)
        {
            var own = Dateiumbenenner.Engine.OwnCompany.Current;
            return list.Where(c => !string.IsNullOrWhiteSpace(c) && (string.IsNullOrEmpty(fullText) || CompanyMatcher.AppearsInText(c, fullText) || own.Matches(c))).ToList();
        }

        /// <summary>Aktuellen Stand einer Datei in die Profil-Datenbank schreiben (thread-sicher).</summary>
        private void SaveToStore(DocumentMetadata m, string key, bool confirmed)
        {
            if(!m.Loaded || string.IsNullOrEmpty(m.FilePath)) return;
            var rec=new DocumentStore.Record{
                FullText=m.FullText, PageCount=m.PageCount, CompanyName=m.CompanyName, CompanyCandidates=TextCandidates(m.CompanyCandidates, m.FullText),
                DocumentType=m.DocumentType, InvoiceNumber=m.InvoiceNumber, DocumentNumber=m.DocumentNumber, Date=m.Date,
                SelectedNumber=m.SelectedNumber, SelectedDate=m.SelectedDate, NumberCandidates=m.NumberCandidates.ToList(), DateCandidates=m.DateCandidates.ToList(),
                Street=m.Street, Zip=m.Zip, City=m.City, AddressManuallySet=m.AddressManuallySet,
                NumberManuallySet=m.NumberManuallySet, DateManuallySet=m.DateManuallySet, DocTypeManuallySet=m.DocTypeManuallySet,
                CompanyConfirmed=confirmed, PreviousCompanies=m.PreviousCompanies.ToList(),
                Layout=m.LayoutSet ? CaptureLayout(m) : null, TemplateDeclined=m.TemplateDeclined };
            DocumentStore.Save(m.FilePath, key, rec);
        }

        private readonly HashSet<DocumentMetadata> _confirmedCompany = new();

        private void ScheduleStoreSave(DocumentMetadata m)
        {
            if(_isBulkLoading || !m.Loaded) return;
            var key=StoreSettingsKey(m.FilePath); var confirmed=_confirmedCompany.Contains(m);
            _ = Task.Run(()=>SaveToStore(m, key, confirmed));
        }

        private async void LvFiles_SelectionChanged(object sender,SelectionChangedEventArgs e){ 
            // Auswahl in den Präfix-Bereich übernehmen
            try{ PrefixRenamerTab?.SyncSelection(LvFiles.SelectedItems.OfType<DocumentMetadata>().Select(m=>m.FilePath)); } catch{ }
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
                    // Vorschau nur bei echtem Dateiwechsel neu laden (verhindert WebView2-Flackern)
                    if(!string.Equals(_lastPreviewPath, meta.FilePath, StringComparison.OrdinalIgnoreCase))
                    {
                        _lastPreviewPath = meta.FilePath;
                        await UpdatePdfPreviewAsync(meta); 
                        if(_openPreviewWindow!=null && !string.IsNullOrEmpty(meta.FilePath)) 
                            await _openPreviewWindow.LoadPdfAsync(meta.FilePath, meta.FullText); 
                    }
                    // KEIN LvFiles.Items.Refresh() hier: würde die Zeilen (inkl. offener Firmen-ComboBox) neu aufbauen
                } finally {
                    // Stelle sicher, dass _isPopulatingSelectors am Ende zur�ckgesetzt wird
                    _isPopulatingSelectors = false;
                }
            } else {
                _currentMeta=null; 
            }
            
            UpdateStandardStatus();
        }

        private async Task LoadMetadataAsync(DocumentMetadata meta)
        {
            try
            {
                var ext=Path.GetExtension(meta.FilePath).ToLowerInvariant();
                if(!string.IsNullOrEmpty(meta.TextSourcePath) && File.Exists(meta.TextSourcePath))
                {
                    meta.FullText=await File.ReadAllTextAsync(meta.TextSourcePath);
                    meta.PageCount=1; /* A-Modus: PDF wird nicht geöffnet (schneller) */
                }
                else if(ext==".pdf")
                {
                    meta.FullText=await Task.Run(()=>
                    {
                        if (PdfTextCache.TryGet(meta.FilePath, out var cachedText, out var cachedPages))
                        {
                            meta.PageCount=cachedPages;
                            return cachedText;
                        }
                        using var doc=UglyToad.PdfPig.PdfDocument.Open(meta.FilePath);
                        meta.PageCount=doc.NumberOfPages;
                        var sb=new System.Text.StringBuilder();
                        foreach(var page in doc.GetPages()) sb.AppendLine(Dateiumbenenner.Engine.PdfLayoutReader.PageText(page));
                        var text=sb.ToString();
                        PdfTextCache.Store(meta.FilePath, text, meta.PageCount);
                        return text;
                    });
                }
                else
                {
                    meta.FullText=await File.ReadAllTextAsync(meta.FilePath);
                }
            if(!string.IsNullOrEmpty(meta.FullText))
                meta.FullText = Dateiumbenenner.Engine.TextReflow.Apply(Dateiumbenenner.Engine.TextLineJoiner.Join(meta.FullText), meta.FilePath);
            if(_enableOcrCorrection && !string.IsNullOrEmpty(meta.FullText))
                meta.FullText = ApplyOcrHeuristics(meta.FullText);
            meta.NumberCandidates.Clear(); meta.DateCandidates.Clear(); if(!meta.NumberManuallySet) meta.SelectedNumber=null; if(!meta.DateManuallySet) meta.SelectedDate=null; if(!meta.DocTypeManuallySet) meta.DocumentType=null; if(!string.IsNullOrEmpty(meta.FullText)) ExtractCandidates(meta); if(string.IsNullOrEmpty(meta.SavedDelimiter)||meta.DelimiterAutoDetected){ meta.SavedDelimiter=DetectDelimiterFromFilename(meta.FileName); meta.DelimiterAutoDetected=true; } meta.Loaded=true; } catch(Exception ex){ meta.FullText=$"Fehler beim Lesen: {ex.Message}"; } }

        public IReadOnlyList<DocumentMetadata> Documents => _items.ToList();
        public string? CurrentFolder => string.IsNullOrWhiteSpace(TxtFolder?.Text) ? null : TxtFolder.Text;
        public string? LoadPluginData(string pluginId, string filePath) => DocumentStore.LoadPluginData(pluginId, filePath);
        public void SavePluginData(string pluginId, string filePath, string data) => DocumentStore.SavePluginData(pluginId, filePath, data);
        public string ActiveProfile => ProfileManager.ActiveProfile;

        public async Task<DocumentMetadata> AnalyzeFileAsync(string filePath, string? textSourcePath = null)
        {
            var meta = new DocumentMetadata { FilePath = filePath, TextSourcePath = textSourcePath };
            var rec = DocumentStore.TryGet(filePath, StoreSettingsKey(filePath), out var upToDate);
            if (rec != null && upToDate)
            {
                ApplyStoreRecord(meta, rec);
                foreach (var c in TextCandidates(rec.CompanyCandidates, rec.FullText)) meta.CompanyCandidates.Add(c);
                meta.DetectedCompany = meta.CompanyCandidates.FirstOrDefault();
                meta.CompanyName = rec.CompanyName;
                AutoAssignNumbers(meta);
                return meta;
            }
            if (rec != null) ApplyManualValues(meta, rec);
            await LoadMetadataAsync(meta);
            bool confirmed = rec != null && KeepFittingManualValues(meta, rec);
            AutoAssignNumbers(meta);
            meta.Loaded = true;
            var key = StoreSettingsKey(filePath);
            _ = Task.Run(() => SaveToStore(meta, key, confirmed));
            return meta;
        }

        public IReadOnlyList<string> DocumentTypes => (CmbDocType?.ItemsSource as List<string>) ?? new List<string>();

        public List<string> ExtractCompanyNames(string? fullText, string? pdfPath = null)
        {
            var list = CompanyMatcher.Improve(PrefixRenamerTab?.ExtractAllCompanyNames(fullText) ?? new List<string>(), fullText, out var replaced);
            list = CleanCandidates(list);
            list = PreferLegalFormNames(list, fullText);
            AddIndustryNames(list, fullText);
            list = CleanCandidates(list);
            list.RemoveAll(Dateiumbenenner.Engine.CompanyExclusions.IsExcluded);
            list.RemoveAll(IsNoiseCandidate);
            // Eigene Firma ist Empfänger/Aussteller – nie als fremder Absender vorschlagen
            var own = Dateiumbenenner.Engine.OwnCompany.Current;
            list.RemoveAll(own.Matches);
            // Nur eigene oder keine Adresse erkannt → eigener/interner Beleg
            if (list.Count == 0 && own.HasName) return new List<string> { own.Name };
            // Firmenname in der Nähe
            list = Engine.RankCompaniesByAddress(list, fullText, pdfPath);
            // Mehrere Faktoren kombinieren: Rechtsform, Branche, Position, USt-ID/HRB nahe, Adressnähe, steht im Text
            list = ScoreCompanies(list, fullText, pdfPath);
            // Gelerntes Absender-Profil (USt-ID, IBAN, Telefon ...) hat Vorrang
            // pdfPath: gleichnamige Layout-TXT/PDF wird für die Adressbewertung genutzt
            var profile = Engine.IdentifySender(fullText, pdfPath);
            if (profile != null && !Dateiumbenenner.Engine.CompanyExclusions.IsExcluded(profile.Company) && !Dateiumbenenner.Engine.CompanyExclusions.IsLetterPhrase(profile.Company) && !own.Matches(profile.Company))
            {
                list.RemoveAll(c => string.Equals(c, profile.Company, StringComparison.OrdinalIgnoreCase));
                // Profil-Name steht nicht im Beleg (z. B. falsch gelerntes Merkmal) oder nur ähnlich, während eine andere ähnliche Firma wörtlich dasteht: nur als Alternative anbieten
                if (list.Count > 0 && !CompanyMatcher.AppearsInTextExclusive(profile.Company, fullText)) list.Add(profile.Company);
                // Kurzform wie "DFH" nicht vor vollständigen Namen mit Rechtsform ("DFH Haus GmbH") setzen
                else if (list.Count > 0 && HasLegalForm(list[0]) && !HasLegalForm(profile.Company) && !list[0].StartsWith(profile.Company, StringComparison.OrdinalIgnoreCase)) list.Insert(1, profile.Company);
                else list.Insert(0, profile.Company);
            }
            var secured = SecureCandidates(list, fullText);
            // Von einer nur ähnlichen bekannten Firma verdrängte Namen (kann eine andere Firma sein) hinten zur Auswahl anbieten
            foreach (var r in replaced)
                if (!secured.Any(c => c.Equals(r, StringComparison.OrdinalIgnoreCase)) && !Dateiumbenenner.Engine.CompanyExclusions.IsExcluded(r)
                    && !IsNoiseCandidate(r) && !own.Matches(r) && CompanyMatcher.AppearsLiterally(r, fullText)) secured.Add(r);
            // Firmenname aus Webadresse/E-Mail immer anbieten (wird sonst evtl. von Filtern entfernt)
            var dn = new List<string>();
            AddDomainNames(dn, fullText);
            foreach (var d in dn)
                if (!secured.Any(c => c.Equals(d, StringComparison.OrdinalIgnoreCase))) secured.Add(d);
            // Menge/Gewicht ("0,78 KG" – "KG" ist hier keine Rechtsform) ist als Firma sehr unwahrscheinlich: nur ganz hinten anbieten
            var quantities = secured.Where(CompanyMatcher.IsQuantity).ToList();
            if (quantities.Count > 0 && quantities.Count < secured.Count) { secured.RemoveAll(CompanyMatcher.IsQuantity); secured.AddRange(quantities); }
            return secured;
        }

        /// <summary>Absicherung: nur Namen, die im Beleg stehen; Kurzformen ("ZAPF-G") hinter den vollständigen Namen mit Rechtsform ("ZAPF GmbH").</summary>
        private static List<string> SecureCandidates(List<string> list, string? fullText)
        {
            if (string.IsNullOrEmpty(fullText)) return list;
            var own = Dateiumbenenner.Engine.OwnCompany.Current;
            var result = list.Where(c => CompanyMatcher.AppearsInText(c, fullText) || own.Matches(c))
                             .Where(c => !IsBicCode(c, fullText) && !IsNoiseCandidate(c))
                             .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            // Teilstücke ("Gmb", "Mayer Gmb", "Mayer GmbH", "ZAPF-G") hinter den längeren vollständigen Namen ("WERNER K. MAYER GMBH")
            for (int i = 0; i < result.Count; i++)
            {
                var nc = CompanyMatcher.Normalize(result[i]);
                if (nc.Length < 2) continue;
                int full = result.FindIndex(i + 1, l => HasLegalForm(l) && l.Length > result[i].Length
                    && CompanyMatcher.Normalize(l) is var nl && nl != nc && nl.Contains(nc, StringComparison.Ordinal));
                if (full < 0) continue;
                var part = result[i];
                result.RemoveAt(i);
                result.Insert(full, part);
                i--;
            }
            // Einzelne normale Wörter ("Jahre", "Herr") nicht vor einen erkannten Namen mit Rechtsform ("DFH Haus GmbH")
            if (result.Any(HasLegalForm))
            {
                var plain = result.Where(IsPlainWord).ToList();
                result.RemoveAll(IsPlainWord);
                result.AddRange(plain);
            }
            // Straßenähnliche Namen
            var streets = result.Where(IsStreetLike).ToList();
            if (streets.Count < result.Count) { result.RemoveAll(IsStreetLike); result.AddRange(streets); }
            return result;
        }

        // Ein bis drei Wörter in normaler Schreibweise ohne Rechtsform ("Jahre", "Jana Rückert"), keine Marke wie "DEUTDESS" oder "AUTO-EINMAL-EINS"
        private static bool IsPlainWord(string c)
        {
            var words = c.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return words.Length is > 0 and <= 3 && !HasLegalForm(c.Trim())
                && words.All(t => t.Length > 1 && char.IsUpper(t[0]) && t.Skip(1).All(char.IsLower));
        }

        private static bool IsStreetLike(string c) => !HasLegalForm(c.Trim())
            && Regex.IsMatch(c.Trim(), @"(stra(ß|ss)e|str\.?)(\s*\d{1,4}\s?[a-zA-Z]?)?$", RegexOptions.IgnoreCase);

        private static readonly Regex BicRegex = new(@"^[A-Z]{4}DE[A-Z0-9]{2}([A-Z0-9]{3})?$", RegexOptions.Compiled);

        /// <summary>BIC
        private static bool IsBicCode(string c, string fullText)
        {
            var t = c.Trim();
            if (!BicRegex.IsMatch(t) && !Regex.IsMatch(t, @"^[A-Z]{6}[A-Z0-9]{2}([A-Z0-9]{3})?$")) return false;
            return BicRegex.IsMatch(t) || Regex.IsMatch(fullText, @"\b(BIC|SWIFT)\b[^\n]{0,40}" + Regex.Escape(t), RegexOptions.IgnoreCase);
        }

        // Name (1-5 Wörter) direkt vor einer Rechtsform; Beginn am Zeilenanfang oder nach ':', '|', ';', ',' bzw. Tab/Mehrfach-Leerzeichen
        private static readonly System.Text.RegularExpressions.Regex LegalFormNameRegex = new(
            @"(?<![\p{L}\d&.'\-])((?:(?:[\p{L}\d][\p{L}\d&.'\-]*|&|\+)\s+){0,4}?[\p{L}\d][\p{L}\d&.'\-]*\s+(?:(?i:(?:GmbH|mbH|AG|SE|UG(?:\s*\(haftungsbeschr(?:ä|ae)nkt\))?)\s*(?:&|\+|u\.|und)\s*Co\.?\s*(?:KGaA|KG|OHG))|(?i:GmbH)|AG|KGaA|KG|UG|OHG|GbR|SE|e\.\s?V\.))(?![\p{L}\d])",
            System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.Multiline);

        private static readonly Regex IndustryRegex = new(
            @"\b(Versicherung(en)?|Versicherungs\w*|Sparkasse|Volksbank|Raiffeisenbank|Bank|Stadtwerke|Stadtverwaltung|Gemeindeverwaltung|Landratsamt|Finanzamt|KfW|Krankenkasse|AOK|BKK|IKK|Verlag|Werke|Schotterwerke?|Gruppe|Telekom|Energie)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex DocTypeWordRegex = new(
            @"^(Rechnung|Stornorechnung|Gutschrift|Mahnung|Zahlungserinnerung|Angebot|Lieferschein|Auftragsbestätigung|Auftrag|Bestellung|Beleg|Quittung|Kostenvoranschlag|Invoice|Kundennummer|Seite|Page|Blatt|Gebucht|Eingegangen)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RegisterRegex = new(@"\b(USt[\s.-]*Id|UID|DE\s?\d{9}|HRB|HRA|Amtsgericht|Steuer[\s-]*Nr|Steuernummer|Handelsregister|IBAN|BIC|Registergericht)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Zeilen mit Branchenwort (z. B. "SV SparkassenVersicherung") als Kandidat aufnehmen, auch ohne Rechtsform.</summary>
        private static void AddIndustryNames(List<string> list, string? fullText)
        {
            if (string.IsNullOrEmpty(fullText)) return;
            var lines = fullText.Split('\n');
            int n = lines.Length;
            for (int i = 0; i < n; i++)
            {
                if (i > 25 && i < n - 25) continue;          // nur Briefkopf und Fußzeile
                var part = Regex.Split(lines[i].Trim(), @"\s{2,}|\t|\|")[0].Trim(' ', ',', ';', ':');
                if (part.Length < 3 || part.Length > 50 || !IndustryRegex.IsMatch(part)) continue;
                if (part.Count(char.IsDigit) > 2 || DocTypeWordRegex.IsMatch(part) || part.Contains('@')) continue;
                if (Regex.IsMatch(part, @"\b(IBAN|BIC|Konto|BLZ)\b", RegexOptions.IgnoreCase)) continue;
                if (!list.Any(c => c.Equals(part, StringComparison.OrdinalIgnoreCase))) list.Add(part);
            }
        }

        private static readonly Regex NoiseWordRegex = new(
            @"^(EUR|Euro|USD|CHF|MwSt|USt|Netto|Brutto|Summe|Gesamt|Betrag|Datum|Seite|Page|Blatt|Hiermit|Bitte|Ihre?|Unsere?|Sehr|Geehrte\w*|Anlage|Kunde|Kunden\w*|Konto|IBAN|BIC|Bank|Gebucht|Gebuch|Eingetragen|Eingegangen|Eingang|Bezahlt|Erledigt|Geprüft|Kopie|Original|Urlaub|Dieser Betrag|Total|Telefon|Fax|Tel)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex TableHeaderRegex = new(
            @"\b(Bezeichnung|Auftragsmenge|Menge|Einzelpreis|Gesamtpreis|Preis|Pos\.?|Position|Artikel(nr|nummer)?|Einheit|Rabatt|Anzahl|Beschreibung|Leistung|skontierfähig|Zwischensumme)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex AddressWordRegex = new(
            @"(stra(ß|ss)e|str\.|platz|weg|allee|gasse|ring|damm|ufer|markt|steige?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        // Präfixe vor dem eigentlichen Namen: "HRA-Nr. 706214 …", "Handelsregister Ulm KANE 700214 …", "AGB Auftrag …"
        private static readonly Regex RegisterPrefixRegex = new(
            @"^.*?\b(HR[AB]|Handelsregister|Amtsgericht|Registergericht|Steuer\S*|USt\S*|BIC|IBAN|Gesellschafter\w*)\b.*?(\d{3,}|:)[\s:,.]+(?=\p{Lu})",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex LeadingJunkRegex = new(
            @"^((AGB|Auftrag|Rechnung|Lieferschein|Bezeichnung|Artikel|und|Pers\.?|haft\.?|persönlich|Komplementär\w*|Gesellschafter\w*:?|Inhaber:?|Bankverbindung:?|Sitz:?)\s+)+",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Zeichen, die Firmennamen umschließen können ("\ Haus GmbH \", "|Haus GmbH|", ": Haus GmbH:").</summary>
        private static readonly char[] EnclosingChars = { '\\', '/', '|', ':', ';', ',', '"', '\'', '*', '#', '[', ']', '<', '>', '„', '“', '”', '»', '«', ' ', '\t' };

        /// <summary>Entfernt Register-/Fußzeilen-Präfixe vor Namen mit Rechtsform und ersetzt Varianten durch die bereinigte Form.</summary>
        private static List<string> CleanCandidates(List<string> list)
        {
            var result = new List<string>();
            foreach (var raw in list)
            {
                var c = raw.Trim().Trim(EnclosingChars).Trim();
                if (c.Length == 0) continue;
                var m = RegisterPrefixRegex.Match(c);
                if (m.Success && HasLegalForm(c[m.Length..])) c = c[m.Length..].Trim();
                var j = LeadingJunkRegex.Match(c);
                if (j.Success && j.Length < c.Length && c[j.Length..].Trim().Length >= 3) c = c[j.Length..].Trim();
                // "Mit freundlichen Grüßen Muster GmbH" -> "Muster GmbH" (ohne Rechtsform bleibt die Formel und wird als Rauschen verworfen)
                int g = Dateiumbenenner.Engine.CompanyExclusions.LetterPhraseLength(c);
                if (g > 0 && c[g..].Trim(EnclosingChars).Trim() is { Length: >= 3 } rest && HasLegalForm(rest)) c = rest;
                if (!result.Contains(c, StringComparer.OrdinalIgnoreCase)) result.Add(c);
            }
            return result;
        }

        /// <summary>Allgemeine Wörter, Währungen und Bruchstücke ("EUR", "S G", "DD D-") sind keine Firma.</summary>
        private static bool IsNoiseCandidate(string c)
        {
            var t = c.Trim();
            if (t.Length < 3 || NoiseWordRegex.IsMatch(t) || DocTypeWordRegex.IsMatch(t)) return true;
            // Gruß-/Dankesformel ("Mit freundlichen Grüßen", "Vielen Dank")
            if (Dateiumbenenner.Engine.CompanyExclusions.IsLetterPhrase(t) && !HasLegalForm(t)) return true;
            // Tabellenkopf ("Bezeichnung Auftragsmenge Einzelpreis Gesamtpreis")
            if (!HasLegalForm(t) && TableHeaderRegex.Matches(t).Count >= 2) return true;
            // PLZ + Ort ("D-55469 Simmern", "55469 Simmern") ist eine Adresse, keine Firma
            if (Regex.IsMatch(t, @"^([A-Z]{1,2}-)?\d{4,5}\s+\p{L}")) return true;
            var words = t.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return words.All(w => w.Trim('-', '.').Length <= 2);
        }

        private const int LinesPerPage = 45;

        private static readonly Regex DomainRegex = new(@"(?:\bwww\s*\.\s*|@)([\p{L}\d][\p{L}\d-]{2,})\s*\.\s*(?:de|com|at|ch|eu|net|org|info|biz)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly HashSet<string> GenericDomains = new(StringComparer.OrdinalIgnoreCase) { "gmail", "gmx", "web", "t-online", "outlook", "hotmail", "yahoo", "icloud", "online", "freenet", "posteo", "mail", "email" };

        /// <summary>Firmenwort aus Webadresse/E-Mail (www.debeka.de, info@debeka.de → debeka); eigene Firma ausgenommen.</summary>
        private static IEnumerable<string> DomainWords(string? fullText)
        {
            if (string.IsNullOrEmpty(fullText)) yield break;
            var own = Dateiumbenenner.Engine.OwnCompany.Current;
            foreach (var w in DomainRegex.Matches(fullText).Select(m => m.Groups[1].Value).Distinct(StringComparer.OrdinalIgnoreCase))
                if (!GenericDomains.Contains(w) && !own.Matches(w)) yield return w;
        }

        /// <summary>
        /// Webadresse als Firmenhinweis: steht das Domainwort auch im Fließtext ("VGB (Debeka Wohngebäudeversicherung"),
        /// wird dieser Name samt folgender großgeschriebener Wörter übernommen, sonst das Domainwort selbst ("Debeka").
        /// </summary>
        private static void AddDomainNames(List<string> list, string? fullText)
        {
            if (string.IsNullOrEmpty(fullText)) return;
            foreach (var d in DomainWords(fullText))
            {
                var rx = new Regex(@"(?<![\p{L}\d.@/-])" + Regex.Escape(d) + @"(?![\p{L}\d]*\.(?:de|com|at|ch|eu|net|org|info|biz)\b)(?:[ \t]+\p{Lu}[\p{L}\-]+){0,3}", RegexOptions.IgnoreCase);
                var found = rx.Matches(fullText).Select(m => m.Value.Trim()).Where(v => v.Length >= d.Length && char.IsUpper(v[0])).ToList();
                var plain = d.All(char.IsLower) ? char.ToUpper(d[0]) + d[1..] : d;
                var tld = DomainRegex.Matches(fullText).Where(m => m.Groups[1].Value.Equals(d, StringComparison.OrdinalIgnoreCase))
                    .Select(m => Regex.Match(m.Value, @"\.\s*(\w+)$").Groups[1].Value.ToLowerInvariant()).FirstOrDefault() ?? "de";
                // Varianten: mit Zusatz ("Debeka Wohngebäudeversicherung"), ohne ("Debeka") und Webadresse ("debeka.de")
                var variants = found.Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(v => v.Length).ToList();
                variants.Add(plain);
                variants.Add(d.ToLowerInvariant() + "." + tld);
                foreach (var v in variants)
                    if (!list.Contains(v, StringComparer.OrdinalIgnoreCase)) list.Add(v);
            }
        }

        /// <summary>Kombiniert mehrere Faktoren zu einer Punktzahl; Reihenfolge aus dem Adress-Ranking fließt mit ein.</summary>
        private static List<string> ScoreCompanies(List<string> list, string? fullText, string? pdfPath = null)
        {
            if (string.IsNullOrEmpty(fullText) || list.Count < 2) return list;
            var lines = fullText.Split('\n');
            int n = Math.Max(1, lines.Length);
            var layout = Dateiumbenenner.Engine.PdfLayoutReader.ReadPageLayout(pdfPath);
            list.RemoveAll(c => DocTypeWordRegex.IsMatch(c.Trim()));   // Belegtyp ist keine Firma
            var zipsInText = Dateiumbenenner.Engine.CompanyDirectory.ZipsInText(fullText);
            double Score(string c, int order)
            {
                double s = -order * 0.3;                                 // Adress-Ranking
                if (HasLegalForm(c)) s += 4;
                if (IndustryRegex.IsMatch(c)) s += 2;
                var ct = c.Trim();
                // Firmenverzeichnis (eigene DB, gelernt, GLEIF): bekannte Firma, ggf. mit ihrer PLZ im Text – nur Pluspunkte
                s += Dateiumbenenner.Engine.CompanyDirectory.Bonus(ct, zipsInText);
                if (!HasLegalForm(ct))
                {
                    if (TableHeaderRegex.IsMatch(ct)) s -= 3;                     // Tabellenkopf/Positionstext
                    if (AddressWordRegex.IsMatch(ct)) s -= 3;                     // "Marktplatz", "Industriestraße …"
                    if (Dateiumbenenner.Engine.DocumentEngine.Plz.IsPlaceName(ct)) s -= 3;   // reiner Ortsname
                    if (ct.Length <= 5 && !ct.Contains(' ') && ct.All(ch => !char.IsLetter(ch) || char.IsUpper(ch))) s -= 2;   // "UNG", "KPFNY"
                }
                // Mehrfach genannt (Briefkopf + Fußzeile) spricht für den Aussteller
                int hits = Regex.Matches(fullText, Regex.Escape(ct), RegexOptions.IgnoreCase).Count;
                s += Math.Min(3, hits - 1) * 0.7;
                if (c.Trim().Split(' ').Any(w => w.Length == 1 && char.IsLetter(w[0]))) s -= 2;   // Fragmente wie "i DIE GARAGE S"
                // Impressum-Block ("Sitz der Gesellschaft", "Registergericht", "Geschäftsführung") direkt beim Namen = Aussteller
                if (Regex.IsMatch(fullText, Regex.Escape(c) + @"[\s\S]{0,120}?\b(Sitz\s+der\s+Gesellschaft|Registergericht|HRB|HRA|Geschäftsführ\w*)", RegexOptions.IgnoreCase)) s += 4;
                // Name direkt hinter Register-/Bankangabe ("HRA 706214 Hans Widmann Verwaltungs GmbH")
                if (Regex.IsMatch(fullText, @"\b(HR[AB]|Handelsregister|Amtsgericht|BIC|IBAN|Steuer\S*|USt\S*)\b[^\n]{0,60}?" + Regex.Escape(ct), RegexOptions.IgnoreCase)) s += 2;
                int idx = Array.FindIndex(lines, l => l.Contains(c, StringComparison.OrdinalIgnoreCase));
                if (idx < 0) return s - 10;                              // steht nicht im Dokument
                // Position auf der Seite (3x3-Raster): Briefkopf oben, Fußzeile unten; Querformat: Absender eher oben links
                var zone = layout?.FindZone(c);
                if (zone is { } z)
                {
                    if (z.Row == 0) s += z.Col == 1 && layout!.Landscape ? 1 : 1.5;
                    else if (z.Row == 2) s += 1;
                    else s -= 0.5;                                       // Mitte: eher Positionstext
                    if (layout!.Landscape && z.Row == 0 && z.Col == 0) s += 0.5;
                }
                else
                {
                    // Kein Layout (TXT/Scan): Text läuft von oben nach unten; A4 hat ca. 45 Zeilen.
                    // Seitenumbruch (\f) beachten, sonst Zeile innerhalb einer 45-Zeilen-Seite
                    int pageStart = Array.FindLastIndex(lines, Math.Max(0, idx), l => l.Contains('\f'));
                    int lineInPage = pageStart >= 0 ? idx - pageStart : idx % LinesPerPage;
                    int pageLines = Math.Min(LinesPerPage, n);
                    double rel = (double)lineInPage / Math.Max(1, pageLines);
                    if (rel < 1.0 / 3) s += 1.5;                     // oberes Drittel (ca. Zeile 1-15)
                    else if (rel >= 2.0 / 3) s += 1;                 // unteres Drittel (ca. Zeile 30-45)
                    else s -= 0.5;
                }
                for (int k = Math.Max(0, idx - 5); k <= Math.Min(n - 1, idx + 5); k++)
                    if (RegisterRegex.IsMatch(lines[k])) { s += 1.5; break; }
                return s;
            }
            return list.Select((c, i) => (c, s: Score(c, i))).OrderByDescending(x => x.s).Select(x => x.c).ToList();
        }

        private static readonly System.Text.RegularExpressions.Regex LegalFormEndRegex = new(
            @"\s(?i:GmbH(\s*&\s*Co\.?\s*KG)?|mbH)|\s(AG|KG|UG|OHG|GbR|SE|e\.\s?V\.|e\.\s?K\.)\.?$", System.Text.RegularExpressions.RegexOptions.Compiled);

        private static bool HasLegalForm(string c) => LegalFormEndRegex.IsMatch(c.Trim());

        private static readonly HashSet<string> NameArticles = new(StringComparer.Ordinal)
        { "der", "die", "das", "den", "dem", "des", "zum", "zur", "am", "im", "und", "von", "vom", "&" };

        private static readonly HashSet<string> LeadingFillWords = new(StringComparer.OrdinalIgnoreCase)
        { "i", "an", "von", "die", "der", "das", "den", "dem", "und", "firma", "fa", "fa.", "bei", "für", "empfänger", "absender", "lieferant" };

        /// <summary>Firmennamen mit Rechtsform (z. B. "ZAPF GmbH", "ean50 GmbH", "X GmbH &amp; Co. KG") aus dem Text nach vorne;
        /// Kurzformen davon ("ZAPF", "DFH") und bekannte Firmen, die nicht im Text stehen, nach hinten.</summary>
        private static List<string> PreferLegalFormNames(List<string> list, string? fullText)
        {
            if (string.IsNullOrWhiteSpace(fullText)) return list;
            var legal = new List<string>();
            foreach (System.Text.RegularExpressions.Match m in LegalFormNameRegex.Matches(fullText))
            {
                var matched = m.Groups[1].Value;
                int nl = matched.LastIndexOfAny(new[] { '\r', '\n', '\f' });
                int lineOffset = nl + 1;
                // Name endet nie über einen Zeilenumbruch hinweg ("Freundliche Grüße\nBux der Einrichter GmbH")
                var words = matched.Substring(lineOffset).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();
                int absStart = m.Groups[1].Index + lineOffset;
                int ls = absStart;
                while (ls > 0 && fullText[ls - 1] is ' ' or '\t') ls--;   // Einrückung (" Bux der Einrichter GmbH") zählt als Zeilenanfang
                bool atLineStart = ls == 0 || fullText[ls - 1] is '\n' or '\r' or '\f';
                // Artikel mitten im Namen ("Bux der Einrichter GmbH") nur, wenn der Name allein am Zeilenanfang steht
                bool InnerArticle(int j) => atLineStart && j == 1 && NameArticles.Contains(words[j])
                    && words.Take(j).All(x => char.IsUpper(x[0]) && !LeadingFillWords.Contains(x)) && !DocTypeWordRegex.IsMatch(words[0]);
                // Satzanfang abschneiden: "Jahren die Zapf GmbH" -> "Zapf GmbH" (vorne klein geschriebene Wörter und Füllwörter weg)
                if (words.Count < 2) continue;
                int cut = -1;
                for (int j = words.Count - 2; j >= 0; j--)
                {
                    var w = words[j];
                    if (InnerArticle(j)) continue;
                    if ((char.IsLower(w[0]) && !w.Any(char.IsDigit)) || LeadingFillWords.Contains(w.TrimEnd(':')) || w.EndsWith(':') || w.EndsWith(')')) { cut = j; break; }
                }
                // Kleingeschriebener Name direkt vor der Rechtsform (\"ean50 GmbH\") bleibt erhalten
                if (cut == words.Count - 2 && !LeadingFillWords.Contains(words[cut].TrimEnd(':')) && !words[cut].EndsWith(':') && !words[cut].EndsWith(')')) cut--;
                if (cut >= 0) words.RemoveRange(0, cut + 1);
                if (words.Count < 2) continue;
                while (words.Count > 2 && LeadingFillWords.Contains(words[0].TrimEnd(':'))) words.RemoveAt(0);
                var name = string.Join(" ", words);
                if (name.Length < 5) continue;
                // Ortsname nur verwerfen, wenn eine PLZ davor steht ("95448 Bayreuth GmbH"); "ZAPF" ist auch ein Ort, aber hier Firmenname
                int start = m.Groups[1].Index + lineOffset + matched.Substring(lineOffset).IndexOf(words[0], StringComparison.Ordinal);
                if (Dateiumbenenner.Engine.DocumentEngine.Plz.IsPlaceName(words[0])
                    && System.Text.RegularExpressions.Regex.IsMatch(fullText.Substring(Math.Max(0, start - 8), Math.Min(8, start)), @"\d{5}\s*$")) continue;
                if (System.Text.RegularExpressions.Regex.IsMatch(name, @"\b(Bank|Sparkasse|Volksbank|Amtsgericht|Registergericht|HRB|IBAN|BIC)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) continue;
                if (!legal.Any(l => l.Equals(name, StringComparison.OrdinalIgnoreCase))) legal.Add(name);
            }
            if (legal.Count == 0) return list;

            bool IsShortFormOfLegal(string c) => legal.Any(l => !l.Equals(c, StringComparison.OrdinalIgnoreCase)
                && CompanyMatcher.Normalize(l).StartsWith(CompanyMatcher.Normalize(c), StringComparison.Ordinal) && CompanyMatcher.Normalize(c).Length >= 2);

            var inText = new List<string>();
            var notInText = new List<string>();
            foreach (var c in list)
            {
                if (legal.Any(l => l.Equals(c, StringComparison.OrdinalIgnoreCase)) || IsShortFormOfLegal(c)) continue;
                (CompanyMatcher.AppearsInText(c, fullText) ? inText : notInText).Add(c);
            }
            var shortForms = list.Where(c => !legal.Any(l => l.Equals(c, StringComparison.OrdinalIgnoreCase)) && IsShortFormOfLegal(c));
            return legal.Concat(inText).Concat(shortForms).Concat(notInText).ToList();
        }

        public Dateiumbenenner.Engine.IDocumentEngine Engine { get; } = new Dateiumbenenner.Engine.DocumentEngine();

        Window Dateiumbenenner.Plugins.IPluginHost.MainWindow => this;

        private readonly Dateiumbenenner.Plugins.PluginManager _pluginManager = new();
        private readonly Dictionary<string, TabItem> _pluginTabs = new();

        private const string SessionMainFolderKey = "Main.Folder";
        private static string SessionFile => Path.Combine(ProfileManager.ActiveDirectory, "session.json");
        private static readonly object SessionLock = new();

        private static Dictionary<string, string> ReadSession()
        {
            try { if (File.Exists(SessionFile)) return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(SessionFile)) ?? new(); } catch { }
            return new();
        }

        public string? LoadSessionValue(string key) { lock (SessionLock) return ReadSession().TryGetValue(key, out var v) ? v : null; }

        public void SaveSessionValue(string key, string? value)
        {
            lock (SessionLock)
            {
                try
                {
                    var d = ReadSession();
                    if (value == null) d.Remove(key); else d[key] = value;
                    Directory.CreateDirectory(Path.GetDirectoryName(SessionFile)!);
                    File.WriteAllText(SessionFile, System.Text.Json.JsonSerializer.Serialize(d));
                }
                catch { }
            }
        }

        private async Task AskRestoreSessionAsync()
        {
            var mainFolder = LoadSessionValue(SessionMainFolderKey);
            bool hasMain = !string.IsNullOrEmpty(mainFolder) && Directory.Exists(mainFolder);
            var iniPlugins = _pluginManager.Plugins.Where(p => p.Enabled && _pluginTabs.ContainsKey(p.Plugin.Id))
                .Select(p => p.Plugin).OfType<Dateiumbenenner.Plugins.IPluginSessionRestore>().Where(p => p.HasSavedSession).ToList();
            bool hasIni = iniPlugins.Count > 0;
            if (!hasMain && !hasIni) return;

            var dlg = new Window { Title = "Zustand wiederherstellen", Owner = this, SizeToContent = SizeToContent.WidthAndHeight, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize };
            var panel = new StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new TextBlock { Text = "Es ist ein gespeicherter Zustand vorhanden. Was soll wiederhergestellt werden?", Margin = new Thickness(0, 0, 0, 10) });
            System.Windows.Controls.RadioButton Rb(string text, bool enabled) => new() { Content = text, IsEnabled = enabled, Margin = new Thickness(0, 2, 0, 2) };
            var rbBoth = Rb("Beides", hasMain && hasIni);
            var rbMain = Rb("Umbenennen" + (hasMain ? $"  ({mainFolder})" : ""), hasMain);
            var rbIni = Rb("INI Generator", hasIni);
            var rbNew = Rb("Nichts wiederherstellen (neu laden)", true);
            (hasMain && hasIni ? rbBoth : hasMain ? rbMain : rbIni).IsChecked = true;
            foreach (var rb in new[] { rbBoth, rbMain, rbIni, rbNew }) panel.Children.Add(rb);
            var ok = new System.Windows.Controls.Button { Content = "OK", Width = 90, IsDefault = true, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            ok.Click += (_, _) => dlg.DialogResult = true;
            panel.Children.Add(ok);
            dlg.Content = panel;
            if (dlg.ShowDialog() != true || rbNew.IsChecked == true) return;
            bool restoreMain = hasMain && (rbBoth.IsChecked == true || rbMain.IsChecked == true);
            bool restoreIni = hasIni && (rbBoth.IsChecked == true || rbIni.IsChecked == true);
            if (restoreMain) { TxtFolder.Text = mainFolder!; await LoadFolderAsync(mainFolder!); }
            if (restoreIni) foreach (var p in iniPlugins) { try { await p.RestoreSessionAsync(); } catch { } }
        }

        private void InitPlugins()
        {
            _pluginManager.LoadAll();
            foreach (var p in _pluginManager.Plugins)
            {
                try { p.Plugin.Initialize(this); if (p.Enabled) ShowPluginTab(p); }
                catch (Exception ex) { _pluginManager.Errors.Add($"{p.Plugin.Name}: {ex.Message}"); p.Enabled = false; }
            }
            RebuildPluginMenu();
            HelpWindow.LoadedPlugins = _pluginManager.Plugins;
            if (_pluginManager.Errors.Count > 0)
                MessageBoxWpf.Show(string.Join(Environment.NewLine, _pluginManager.Errors), "Plugins", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void RebuildPluginMenu()
        {
            var sepIndex = MenuPlugins.Items.IndexOf(PluginMenuSeparator);
            while (MenuPlugins.Items.Count > sepIndex + 1) MenuPlugins.Items.RemoveAt(sepIndex + 1);
            if (_pluginManager.Plugins.Count == 0)
            {
                MenuPlugins.Items.Add(new MenuItem { Header = "(keine Plugins installiert)", IsEnabled = false });
                return;
            }
            foreach (var p in _pluginManager.Plugins)
            {
                var mi = new MenuItem { Header = $"{p.Plugin.Name} {p.Plugin.Version}", ToolTip = p.Plugin.Description, IsCheckable = true, IsChecked = p.Enabled };
                mi.Click += (s, e) =>
                {
                    p.Enabled = mi.IsChecked;
                    if (p.Enabled) ShowPluginTab(p); else HidePluginTab(p);
                    _pluginManager.SaveSettings();
                };
                MenuPlugins.Items.Add(mi);
            }
        }

        private void ShowPluginTab(Dateiumbenenner.Plugins.LoadedPlugin p)
        {
            if (_pluginTabs.ContainsKey(p.Plugin.Id)) return;
            try
            {
                var tab = new TabItem { Header = p.Plugin.Name, Content = p.Plugin.CreateTabContent() };
                _pluginTabs[p.Plugin.Id] = tab;
                // "PDF Zusammenführen" bleibt immer der letzte Tab
                var mergeTab = MainTabs.Items.OfType<TabItem>().FirstOrDefault(t => (t.Header as string) == "PDF Zusammenführen");
                if (mergeTab != null) MainTabs.Items.Insert(MainTabs.Items.IndexOf(mergeTab), tab);
                else MainTabs.Items.Add(tab);
            }
            catch (Exception ex)
            {
                p.Enabled = false;
                MessageBoxWpf.Show($"Plugin '{p.Plugin.Name}' konnte nicht aktiviert werden: {ex.Message}", "Plugins", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void HidePluginTab(Dateiumbenenner.Plugins.LoadedPlugin p)
        {
            if (_pluginTabs.Remove(p.Plugin.Id, out var tab)) MainTabs.Items.Remove(tab);
        }

        private void MenuPluginImport_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Plugin importieren", Filter = "Dateiumbenenner-Plugin (*.Plugin.dll)|*.Plugin.dll", ClientGuid = new Guid("3B8E6E2A-5C1D-4F7A-9E21-6A1F2C9D0B03") };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                var target = _pluginManager.Import(dlg.FileName, out var needsRestart);
                if (needsRestart)
                {
                    if (MessageBoxWpf.Show("Das Plugin ist bereits geladen. Die neue Version wird beim nächsten Start übernommen.\n\nJetzt neu starten?", "Plugins", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                        RestartApplication();
                    return;
                }
                var added = _pluginManager.LoadFrom(target);
                if (added.Count == 0)
                {
                    var err = _pluginManager.Errors.Count > 0 ? string.Join(Environment.NewLine, _pluginManager.Errors) : "Keine Plugin-Klasse gefunden.";
                    MessageBoxWpf.Show($"Plugin konnte nicht geladen werden:\n{err}", "Plugins", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                foreach (var p in added)
                {
                    p.Plugin.Initialize(this);
                    p.Enabled = true;
                    ShowPluginTab(p);
                }
                _pluginManager.SaveSettings();
                RebuildPluginMenu();
                MessageBoxWpf.Show($"Plugin geladen und aktiviert: {string.Join(", ", added.Select(p => p.Plugin.Name))}", "Plugins", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBoxWpf.Show($"Import fehlgeschlagen: {ex.Message}", "Plugins", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RestartApplication()
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true });
            System.Windows.Application.Current.Shutdown();
        }

        private void MenuPluginFolder_Click(object sender, RoutedEventArgs e)
        {
            Directory.CreateDirectory(Dateiumbenenner.Plugins.PluginManager.PluginDirectory);
            System.Diagnostics.Process.Start("explorer.exe", Dateiumbenenner.Plugins.PluginManager.PluginDirectory);
        }

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
            var block=new Dateiumbenenner.Engine.LineBlock(lines);
            var invoiceHit=block.Mark(KeyInvoice);
            var docNumHit=block.Mark(KeyDocNum);
            var dateHit=new bool[lines.Length];
            foreach(var rx in new[]{_dateNumeric1,_dateNumeric2,_dateSpelledDayMonthYear,_dateSpelledMonthYear}) block.Mark(rx,dateHit);
            foreach(var rx in Dateiumbenenner.Engine.DateRuleStore.MonthNameRegexes) block.Mark(rx,dateHit);
            for(int i=0;i<lines.Length;i++)
            { 
                if(invoiceHit[i]) ScanForValuesWithPosition(lines,i,KeyInvoice,invoiceNumbers); 
                if(docNumHit[i]) ScanForValuesWithPosition(lines,i,KeyDocNum,documentNumbers); 
                ScanForDatesWithPosition(lines,i,KeyDate,meta.FullText!, datesWithPos, dateHit); 
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
            ApplyEngineFields(meta);
        }

        /// <summary>Gemeinsame Engine (wie INI-Plugin) hat Vorrang; alte Kandidaten bleiben als Ergänzung.</summary>
        private void LearnNumberPattern(DocumentMetadata meta)
        {
            var value = meta.SelectedNumber;
            if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(meta.CompanyName) || string.IsNullOrWhiteSpace(meta.DocumentType)) return;
            var src = Engine.ExtractNumber(meta.FullText, meta.FilePath).Candidates
                .FirstOrDefault(c => c.Value.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase))?.Source;
            Dateiumbenenner.Engine.NumberPatternStore.Learn(meta.CompanyName, meta.DocumentType, src, value);
            Dateiumbenenner.Engine.NumberRuleStore.Learn(meta.CompanyName, meta.DocumentType, value);
            Dateiumbenenner.Engine.ValueLocationStore.Learn(Dateiumbenenner.Engine.ValueKind.Nummer, meta.CompanyName, meta.DocumentType, meta.FullText, value);
            ReapplyNumbers(meta);
        }

        /// <summary>Weitere Belege derselben Firma + Belegart nach Muster und gelernter Fundstelle neu bewerten (manuell gesetzte bleiben).</summary>
        private void ReapplyNumbers(DocumentMetadata src)
        {
            var dt = Dateiumbenenner.Engine.DocTypeStore.Canonical(src.DocumentType);
            int changed = 0, total = 0;
            foreach (var m in _items.Where(i => i != src && i.Loaded && !i.NumberManuallySet && !string.IsNullOrEmpty(i.FullText)
                         && string.Equals(i.CompanyName?.Trim(), src.CompanyName?.Trim(), StringComparison.OrdinalIgnoreCase)
                         && string.Equals(Dateiumbenenner.Engine.DocTypeStore.Canonical(i.DocumentType), dt, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                total++;
                var before = m.SelectedNumber;
                ApplyNumberField(m, Engine.ExtractNumber(m.FullText, m.FilePath), m.DocumentType);
                if (string.Equals(before, m.SelectedNumber, StringComparison.Ordinal)) continue;
                changed++;
                AutoAssignNumbers(m);
                RefreshNewNamePreview(m);
                ScheduleStoreSave(m);
            }
            if (total > 0) TxtStatus.Text = $"Nummer-Fundstelle für {src.CompanyName} / {src.DocumentType} gelernt: {changed} von {total} weiteren Beleg(en) angepasst.";
        }

        /// <summary>Nummern-Kandidaten nach gelerntem Format, Regeln und Fundstelle ordnen; Nummer setzen, wenn nicht manuell gewählt.</summary>
        private void ApplyNumberField(DocumentMetadata meta, Dateiumbenenner.Engine.Field number, string? docType)
        {
            var pattern = Dateiumbenenner.Engine.NumberPatternStore.Get(meta.CompanyName, docType);
            if (pattern != null)
                foreach (var c in number.Candidates.ToList())
                {
                    double bonus = (Dateiumbenenner.Engine.NumberPatternStore.Shape(c.Value) == pattern.Shape ? 4 : 0)
                                 + (pattern.Label.Length > 0 && c.Source.Equals(pattern.Label, StringComparison.OrdinalIgnoreCase) ? 2 : 0);
                    if (bonus > 0) number.Add(c.Value, c.Score + bonus, c.Source);
                }
            Dateiumbenenner.Engine.NumberRuleStore.Apply(number, meta.CompanyName, docType, meta.FullText);
            foreach (var c in number.Candidates.Where(c => c.Score < 0 && c.Source == Dateiumbenenner.Engine.NumberRuleStore.ExcludedSource))
            {
                meta.NumberCandidates.RemoveAll(n => n.Equals(c.Value, StringComparison.OrdinalIgnoreCase));
                if (!meta.NumberManuallySet && string.Equals(meta.SelectedNumber?.Trim(), c.Value, StringComparison.OrdinalIgnoreCase)) { meta.SelectedNumber = null; meta.InvoiceNumber = null; }
            }
            for (int i = number.Values.Count - 1; i >= 0; i--)
            { meta.NumberCandidates.Remove(number.Values[i]); meta.NumberCandidates.Insert(0, number.Values[i]); }
            if (!meta.NumberManuallySet && number.Value != null) { meta.InvoiceNumber = number.Value; meta.SelectedNumber = number.Value; }
        }

        private bool NeedsNumberRules(DocumentMetadata m) => !m.NumberManuallySet
            && (Dateiumbenenner.Engine.NumberPatternStore.Get(m.CompanyName, m.DocumentType) != null
                || Dateiumbenenner.Engine.NumberRuleStore.RulesFor(m.CompanyName, m.DocumentType).Count > 0
                || Dateiumbenenner.Engine.NumberRuleStore.HasExcludes(m.CompanyName, m.DocumentType)
                || Dateiumbenenner.Engine.ValueLocationStore.Has(Dateiumbenenner.Engine.ValueKind.Nummer, m.CompanyName, m.DocumentType));

        private static bool NeedsDateRules(DocumentMetadata m) => !m.DateManuallySet
            && Dateiumbenenner.Engine.DateRuleStore.RulesFor(m.CompanyName, m.DocumentType).Count > 0;

        /// <summary>Gelernte Eigenschaften (Nummernmuster/-regeln/-fundstelle, Datumsregeln) der Kombination Firma + Belegart
        /// anwenden, z. B. nachdem Firma oder Belegart zugewiesen wurde. Von Hand gesetzte Werte bleiben unver?ndert.
        /// Optional bereits im Hintergrund ermittelte Felder verwenden. R?ckgabe: Nummer oder Datum hat sich ge?ndert.</summary>
        private bool ApplyLearnedRules(DocumentMetadata m, Dateiumbenenner.Engine.Field? number = null, Dateiumbenenner.Engine.Field? date = null)
        {
            if (!m.Loaded || string.IsNullOrEmpty(m.FullText)) return false;
            var oldNumber = m.SelectedNumber; var oldDate = m.SelectedDate;
            if (NeedsNumberRules(m))
                ApplyNumberField(m, number ?? Engine.ExtractNumber(m.FullText, m.FilePath), m.DocumentType);
            ReapplyDateRules(m, date);
            AutoAssignNumbers(m);
            return !string.Equals(oldNumber, m.SelectedNumber, StringComparison.Ordinal) || !string.Equals(oldDate, m.SelectedDate, StringComparison.Ordinal);
        }

        /// <summary>Gelernte Regeln auf viele Belege anwenden, ohne die Oberfl?che zu blockieren (Textdateien werden im Hintergrund gelesen).</summary>
        private async Task<int> ApplyLearnedRulesAsync(IReadOnlyList<DocumentMetadata> list)
        {
            var loaded = new HashSet<DocumentMetadata>(_items);
            var tick = System.Diagnostics.Stopwatch.StartNew();
            int changed = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var m = list[i];
                if (!loaded.Contains(m) || !m.Loaded || string.IsNullOrEmpty(m.FullText)) continue;
                bool needN = NeedsNumberRules(m), needD = NeedsDateRules(m);
                Dateiumbenenner.Engine.Field? number = null, date = null;
                if (needN || needD)
                {
                    var text = m.FullText; var path = m.FilePath;
                    (number, date) = await Task.Run(() => (needN ? Engine.ExtractNumber(text, path) : null, needD ? Engine.ExtractDate(text, path) : null));
                }
                if (ApplyLearnedRules(m, number, date)) { changed++; ScheduleStoreSave(m); }
                if (tick.ElapsedMilliseconds >= 40)
                {
                    tick.Restart();
                    TxtStatus.Text = $"Gelernte Eigenschaften werden ?bernommen: {i + 1:N0} / {list.Count:N0}...";
                    await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
                }
            }
            if (_currentMeta is { } cur && list.Contains(cur)) RefreshAfterRules(cur);
            return changed;
        }

        /// <summary>Aktuellen Beleg nach Regel?bernahme neu anzeigen (Nummer/Datum-Auswahl), sobald die laufende Eingabe abgeschlossen ist.</summary>
        private void RefreshAfterRules(DocumentMetadata meta) =>
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_currentMeta != meta) return;
                PopulateSelectors(meta);
                ShowDetails(meta);
                RefreshNewNamePreview(meta);
            }), System.Windows.Threading.DispatcherPriority.Background);

        /// <summary>Datumsregeln (Begriff/Schreibweise) der aktuellen Firma + Belegart erneut anwenden, z. B. nach einer Belegart-?bertragung.
        /// Ein von Hand gesetztes Datum bleibt unver?ndert.</summary>
        private void ReapplyDateRules(DocumentMetadata meta, Dateiumbenenner.Engine.Field? date = null)
        {
            if (string.IsNullOrEmpty(meta.FullText) || !NeedsDateRules(meta)) return;
            date ??= Engine.ExtractDate(meta.FullText, meta.FilePath);
            Dateiumbenenner.Engine.DateRuleStore.Apply(date, meta.CompanyName, meta.DocumentType, meta.FullText);
            for (int i = date.Values.Count - 1; i >= 0; i--)
            { meta.DateCandidates.Remove(date.Values[i]); meta.DateCandidates.Insert(0, date.Values[i]); }
            if (date.Value != null) { meta.Date = date.Value; meta.SelectedDate = date.Value; }
        }

        private void ApplyEngineFields(DocumentMetadata meta)
        {
            var pdf = meta.FilePath;
            if (!meta.AddressManuallySet) meta.AddressLoaded = false; // Adresse bei Bedarf neu berechnen (Text hat sich geändert)
            var number = Engine.ExtractNumber(meta.FullText, pdf);
            var date = Engine.ExtractDate(meta.FullText, pdf);
            var docType = Engine.ExtractDocumentType(meta.FullText, pdf);

            // Gelerntes Nummernformat, Regeln und Fundstelle (Firma + Belegtyp): passende Kandidaten nach vorne
            ApplyNumberField(meta, number, docType.Value ?? meta.DocumentType);
            // Datumsregeln (Begriff/Schreibweise je Firma + Belegart)
            Dateiumbenenner.Engine.DateRuleStore.Apply(date, meta.CompanyName, docType.Value ?? meta.DocumentType, meta.FullText);
            for (int i = date.Values.Count - 1; i >= 0; i--)
            { meta.DateCandidates.Remove(date.Values[i]); meta.DateCandidates.Insert(0, date.Values[i]); }

            if (!meta.DateManuallySet && date.Value != null) { meta.Date = date.Value; meta.SelectedDate = date.Value; }
            if (!meta.DocTypeManuallySet && docType.Value != null) meta.DocumentType = docType.Value;
        }

        private static void AddWithPrefix(List<(string,int)> results,string val,int pos)
        {
            if(!results.Any(x=>x.Item1==val)) results.Add((val,pos));
            if(val.Contains('/'))
            {
                var before=val.Split('/')[0];
                if(before.Length>=6 && !results.Any(x=>x.Item1==before)) results.Add((before,pos+1));
            }
        }

        private void ScanForValuesWithPosition(string[] lines,int i,Regex keyRegex,List<(string,int)> results)
        {
            var match=keyRegex.Match(lines[i]);
            if(!match.Success) return;
            var rest=NormalizeSlash(lines[i].Substring(match.Index+match.Length));
            string? extracted=null;
            var im=TokenNumber.Match(rest);
            if(im.Success)
            {
                var value=im.Groups[1].Value.Trim();
                var cut=_numberCutRegex.Match(value);
                extracted=cut.Success?cut.Value:value;
            }
            else
            {
                var alt=TokenNumberAfterKey.Match(rest);
                if(alt.Success) extracted=alt.Groups[1].Value.Trim();
            }
            if(!string.IsNullOrEmpty(extracted)) AddWithPrefix(results,extracted,i*100);

            for(int off=1;off<=2 && (i+off)<lines.Length;off++)
            {
                foreach(Match m in TokenNumber.Matches(NormalizeSlash(lines[i+off])))
                {
                    var val=m.Groups[1].Value.Trim();
                    var cut=_numberCutRegex.Match(val);
                    AddWithPrefix(results,cut.Success?cut.Value:val,i*100+off*10);
                }
                var alt2=TokenNumberAfterKey.Match(lines[i+off]);
                if(alt2.Success) AddWithPrefix(results,alt2.Groups[1].Value.Trim(),i*100+off*10+5);
            }
        }

        private static readonly Regex _numberCutRegex=new(@"^[A-Z]{0,4}\d{4,}(?:/\d{1,4})?",RegexOptions.Compiled);
        [ThreadStatic] private static string[]? _offsetLines;
        [ThreadStatic] private static int[]? _lineOffsets;
        private static int[] GetLineOffsets(string[] lines){ if(!ReferenceEquals(_offsetLines,lines)||_lineOffsets==null){ var offs=new int[lines.Length+1]; int nl=Environment.NewLine.Length; for(int j=0;j<lines.Length;j++) offs[j+1]=offs[j]+lines[j].Length+nl; _lineOffsets=offs; _offsetLines=lines; } return _lineOffsets; }
        private void ScanForDatesWithPosition(string[] lines,int i,Regex keyRegex,string fullText,List<(string,int)> results,bool[]? dateHit=null)
        {
            var offsets=GetLineOffsets(lines);
            var dateRegexes=new[]{_dateNumeric1,_dateNumeric2,_dateSpelledDayMonthYear,_dateSpelledMonthYear};
            void addMatches(int line)
            {
                if(dateHit!=null && !dateHit[line]) return;
                var text=lines[line]; int start=offsets[line];
                // Monatsnamen auch kompakt/OCR-tolerant (23Okt25, 25-Dez-23, 2025-MAY-10, 0kt)
                foreach(var h in Dateiumbenenner.Engine.DateRuleStore.FindDates(text,Dateiumbenenner.Engine.DateFormatKind.Monatsname))
                    if(!results.Any(x=>x.Item1==h.Date)) results.Add((h.Date,start+h.Index));
                foreach(var rx in dateRegexes)
                    foreach(Match m in rx.Matches(text))
                    {
                        var ds=NormalizeDate(m.Groups[1].Value.Trim());
                        if(!results.Any(x=>x.Item1==ds)) results.Add((ds,start+m.Index));
                    }
            }
            addMatches(i);
            if(keyRegex.IsMatch(lines[i]))
                for(int off=1;off<=2 && (i+off)<lines.Length;off++)
                    addMatches(i+off);
        }
        private bool _propagatingCompany;

        // Klick in eine Zelle
        private void LvFilesItem_PreviewGotKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
        {
            if (sender is not System.Windows.Controls.ListViewItem lvi || lvi.IsSelected) return;
            var mods = System.Windows.Input.Keyboard.Modifiers;
            if ((mods & (System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift)) != 0) lvi.IsSelected = true;
            else { LvFiles.SelectedItems.Clear(); lvi.IsSelected = true; }
        }

        private readonly Dictionary<DocumentMetadata, string?> _lastCompany = new();
        /// <summary>Gerade abgeschlossene Bearbeitung im Firmenfeld: Name beim Betreten (statt getippter Zwischenstände).</summary>
        private (DocumentMetadata Meta, string? Original)? _finishedEdit;

        /// <summary>Bisherigen Firmennamen in der Liste früherer Namen merken (neueste zuerst, höchstens 10).</summary>
        private static void RememberPreviousCompany(DocumentMetadata m, string? oldName)
        {
            var old = oldName?.Trim();
            if (string.IsNullOrWhiteSpace(old) || string.Equals(old, m.CompanyName?.Trim(), StringComparison.OrdinalIgnoreCase)) return;
            // Neue Liste statt Änderung: Speichern im Hintergrund liest die bisherige gefahrlos weiter
            m.PreviousCompanies = new[] { old }.Concat(m.PreviousCompanies.Where(p => !string.Equals(p, old, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(p, m.CompanyName?.Trim(), StringComparison.OrdinalIgnoreCase)))
                .Take(10).ToList();
        }

        private void Meta_CompanyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(DocumentMetadata.CompanyName) || sender is not DocumentMetadata src) return;
            _lastCompany.TryGetValue(src, out var oldName);
            _lastCompany[src] = src.CompanyName;
            // Alten Namen merken – nicht während des Tippens (Zwischenstände); beim Abschluss zählt der Name vor der Bearbeitung
            if (!_isBulkLoading && !ReferenceEquals(src, _companyEditMeta))
                RememberPreviousCompany(src, _finishedEdit is { } fe && ReferenceEquals(fe.Meta, src) ? fe.Original : oldName);
            PrefixRenamerTab?.SetCompanyName(src.FilePath, src.CompanyName);
            if (!_isBulkLoading) ResetAddress(src);
            if (!_isBulkLoading && !string.IsNullOrWhiteSpace(src.CompanyName)) { _confirmedCompany.Add(src); ScheduleStoreSave(src); }
            if (src == _currentMeta) RefreshNewNamePreview(src);
            // Benutzer bearbeitet das Firmenfeld noch: Lernen, Vorlagen und Rückfrage erst beim Verlassen
            if (ReferenceEquals(src, _companyEditMeta) && !_propagatingCompany)
            {
                if (_pendingCompanyPropagation?.Src != src) _pendingCompanyPropagation = (src, oldName);
                return;
            }
            if (!_isBulkLoading && !_propagatingCompany && src == _currentMeta) LearnCompany(src);
            if (_propagatingCompany || string.IsNullOrWhiteSpace(src.CompanyName)) return;
            var name = src.CompanyName!;
            Dispatcher.BeginInvoke(new Action(() => PropagateCompany(src, name, oldName)), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void LearnCompany(DocumentMetadata src)
        {
            CompanyMatcher.Learn(src.CompanyName);
            if (!string.IsNullOrWhiteSpace(src.CompanyName) && src.FullText?.Contains(src.CompanyName!, StringComparison.OrdinalIgnoreCase) == true)
                Engine.ConfirmSender(src.FullText, src.CompanyName!);
            try
            {
                EnsureAddress(src);
                if (!string.IsNullOrWhiteSpace(src.Zip) && src.FullText?.Contains(src.Zip!) == true)
                    Dateiumbenenner.Engine.CompanyDirectory.Learn(src.CompanyName, src.Zip, src.City);
            }
            catch { }
            // Gelernte Nummer/Datum der Kombination Firma + Belegart ?bernehmen (von Hand gesetzte Werte bleiben)
            if (ApplyLearnedRules(src)) { ScheduleStoreSave(src); RefreshAfterRules(src); }
            Dispatcher.BeginInvoke(new Action(() => OfferTemplates(new[] { src })), System.Windows.Threading.DispatcherPriority.Background);
        }

        // Bearbeitungssitzung im Firmenfeld: Beleg, Firma beim Betreten, letzter vom Benutzer eingegebener Text
        private DocumentMetadata? _companyEditMeta;
        private string? _companyEditOriginal;
        private string? _companyEditText;
        private bool _companyUserEdited;
        private (DocumentMetadata Src, string? OldName)? _pendingCompanyPropagation;
        private System.Windows.Threading.DispatcherTimer? _companyEditTimer;

        private static bool IsInside(DependencyObject? d, DependencyObject? container)
        {
            for (; d != null && container != null; d = (d is System.Windows.Media.Visual || d is System.Windows.Media.Media3D.Visual3D ? System.Windows.Media.VisualTreeHelper.GetParent(d) : null) ?? LogicalTreeHelper.GetParent(d))
                if (ReferenceEquals(d, container)) return true;
            return false;
        }

        private bool EnsureCompanySession()
        {
            if (_companyEditMeta != null) return true;
            if (_currentMeta == null) return false;
            _companyEditMeta = _currentMeta;
            _companyEditOriginal = _currentMeta.CompanyName;
            _companyEditText = null;
            _companyUserEdited = false;
            _pendingCompanyPropagation = null;
            return true;
        }

        /// <summary>Firmenfeld: nur echte Benutzereingaben übernehmen (nicht das Befüllen beim Belegwechsel).
        /// Getippter Text wird nach kurzer Pause übernommen; nach weiteren Belegen wird erst beim Verlassen gefragt
        /// (Fokuswechsel, Klick außerhalb des Felds oder Enter).</summary>
        private void HookMainCompanyEditing()
        {
            var cb = CmbMainCompany;
            _companyEditTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
            _companyEditTimer.Tick += (_, _) => { _companyEditTimer.Stop(); ApplyTypedCompany(); };
            void Capture() => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_companyEditMeta == null || !cb.IsKeyboardFocusWithin) return;
                _companyEditText = cb.Text;
                _companyEditTimer.Stop(); _companyEditTimer.Start();
            }), System.Windows.Threading.DispatcherPriority.Input);
            void Edited() { if (!EnsureCompanySession()) return; _companyUserEdited = true; Capture(); }

            cb.AddHandler(UIElement.GotKeyboardFocusEvent, new System.Windows.Input.KeyboardFocusChangedEventHandler((_, _) =>
            {
                if (_companyEditMeta != null && !ReferenceEquals(_companyEditMeta, _currentMeta)) FinishCompanyEdit(null, null, true);
                EnsureCompanySession();
            }), true);
            cb.AddHandler(UIElement.PreviewTextInputEvent, new System.Windows.Input.TextCompositionEventHandler((_, _) => Edited()), true);
            cb.AddHandler(System.Windows.DataObject.PastingEvent, new System.Windows.DataObjectPastingEventHandler((_, _) => Edited()), true);
            cb.AddHandler(UIElement.PreviewKeyDownEvent, new System.Windows.Input.KeyEventHandler((_, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter && !cb.IsDropDownOpen)
                {
                    FinishCompanyEdit(cb.Text, _currentMeta, true); // Enter = Eingabe abschließen
                    return;
                }
                var mods = System.Windows.Input.Keyboard.Modifiers;
                bool ctrl = (mods & System.Windows.Input.ModifierKeys.Control) != 0, shift = (mods & System.Windows.Input.ModifierKeys.Shift) != 0;
                if (e.Key is System.Windows.Input.Key.Back or System.Windows.Input.Key.Delete or System.Windows.Input.Key.Up or System.Windows.Input.Key.Down
                    || ctrl && e.Key is System.Windows.Input.Key.V or System.Windows.Input.Key.X
                    || shift && e.Key == System.Windows.Input.Key.Insert)
                    Edited();
            }), true);
            cb.SelectionChanged += (_, _) =>
            {
                // Nur Auswahl aus der aufgeklappten Liste – das Befüllen beim Belegwechsel ist keine Benutzerauswahl
                if (!cb.IsDropDownOpen || cb.SelectedItem is not string s || string.IsNullOrWhiteSpace(s) || !EnsureCompanySession()) return;
                var meta = _companyEditMeta!;
                _companyUserEdited = true;
                _companyEditText = s;
                if (s != meta.CompanyName) meta.CompanyName = s;
                RefreshNewNamePreview(meta);
            };
            cb.LostFocus += (_, _) =>
            {
                var text = cb.Text;
                var cur = _currentMeta;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (cb.IsDropDownOpen || cb.IsKeyboardFocusWithin) return; // noch im Feld (z. B. Liste aufgeklappt)
                    FinishCompanyEdit(text, cur, true);
                }), System.Windows.Threading.DispatcherPriority.Input);
            };
            // Klick außerhalb des Felds (auch auf Bereiche, die keinen Fokus annehmen) = Verlassen
            AddHandler(UIElement.PreviewMouseDownEvent, new System.Windows.Input.MouseButtonEventHandler((_, e) =>
            {
                if (_companyEditMeta == null || cb.IsDropDownOpen || IsInside(e.OriginalSource as DependencyObject, cb)) return;
                var text = cb.Text;
                var cur = _currentMeta;
                // „Übertragen…“ fragt selbst – keine zweite Rückfrage
                bool ask = !IsInside(e.OriginalSource as DependencyObject, BtnMainTransferName);
                Dispatcher.BeginInvoke(new Action(() => FinishCompanyEdit(text, cur, ask)), System.Windows.Threading.DispatcherPriority.Input);
            }), true);
        }

        /// <summary>Getippten Firmennamen nach kurzer Pause übernehmen (Dateiname aktualisiert sich), noch ohne Rückfrage.</summary>
        private void ApplyTypedCompany()
        {
            var meta = _companyEditMeta;
            var text = _companyEditText?.Trim();
            if (meta == null || !_companyUserEdited || string.IsNullOrWhiteSpace(text) || text == meta.CompanyName || !_items.Contains(meta)) return;
            if (!meta.CompanyCandidates.Contains(text)) meta.CompanyCandidates.Add(text);
            meta.CompanyName = text;
            RefreshNewNamePreview(meta);
            TxtStatus.Text = $"Firma „{text}“ übernommen.";
        }

        private void FinishCompanyEdit(string? textAtLeave, DocumentMetadata? currentAtLeave, bool ask)
        {
            _companyEditTimer?.Stop();
            var meta = _companyEditMeta;
            var original = _companyEditOriginal;
            var edited = _companyUserEdited;
            // Bei Belegwechsel zeigt das Feld schon den neuen Beleg – dann den zuletzt getippten Text verwenden
            var text = (ReferenceEquals(currentAtLeave, meta) ? textAtLeave : _companyEditText)?.Trim();
            var pending = _pendingCompanyPropagation;
            _companyEditMeta = null; _companyEditOriginal = null; _companyEditText = null; _companyUserEdited = false; _pendingCompanyPropagation = null;
            if (meta == null || !edited || !_items.Contains(meta)) return;

            if (!string.IsNullOrWhiteSpace(text) && text != meta.CompanyName)
            {
                if (!meta.CompanyCandidates.Contains(text)) meta.CompanyCandidates.Add(text);
                if (!ask) _propagatingCompany = true;
                _finishedEdit = (meta, original);
                try { meta.CompanyName = text; } // Sitzung ist beendet → Meta_CompanyChanged lernt und fragt jetzt regulär
                finally { _propagatingCompany = false; _finishedEdit = null; }
                if (!ask && meta == _currentMeta) LearnCompany(meta);
                RefreshNewNamePreview(meta);
                return;
            }
            // Während der Bearbeitung übernommene Firma: jetzt lernen und nachfragen, sofern sie sich gegenüber dem Betreten geändert hat
            if (pending is { } p && ReferenceEquals(p.Src, meta) && !string.IsNullOrWhiteSpace(meta.CompanyName)
                && !string.Equals(meta.CompanyName?.Trim(), original?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                RememberPreviousCompany(meta, original);
                ScheduleStoreSave(meta);
                if (meta == _currentMeta) LearnCompany(meta);
                if (!ask) return;
                var name = meta.CompanyName!;
                Dispatcher.BeginInvoke(new Action(() => PropagateCompany(meta, name, p.OldName)), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private async void PropagateCompany(DocumentMetadata src, string name, string? oldName = null)
        {
            if (src.CompanyName != name) return; // inzwischen erneut geändert
            // 1) Weitere markierte Einträge
            var selected = LvFiles.SelectedItems.OfType<DocumentMetadata>().Where(m => m != src && m.CompanyName != name).ToList();
            // 2) Einträge mit vergleichbarer Auswahlliste: die neue Firma muss enthalten sein
            //    und der dort aktuell gewählte Eintrag muss in der Liste der Quelle vorkommen
            //    (sonst handelt es sich um einen anderen Absender, z. B. eigene Firma/Empfänger).
            var sameCompany = _items.Where(m => m != src && m.CompanyName != name && !selected.Contains(m) && ((!string.IsNullOrWhiteSpace(oldName) && !string.IsNullOrWhiteSpace(m.CompanyName) && SameCompany(m.CompanyName!, oldName!)) || (
                                             true && m.CompanyCandidates.Any(c => SameCompany(c, name))
                                             && (string.IsNullOrWhiteSpace(m.CompanyName) || src.CompanyCandidates.Any(c => SameCompany(c, m.CompanyName!)))
                                             && CompanyMatcher.ListOverlap(src.CompanyCandidates, m.CompanyCandidates) >= CompanyMatcher.ListThreshold))).ToList();
            if (selected.Count == 0 && sameCompany.Count == 0) return;

            var targets = new List<DocumentMetadata>();
            if (selected.Count > 0 && src.FilePath != null && LvFiles.SelectedItems.Contains(src))
                targets.AddRange(selected); // Mehrfachauswahl: ohne Rückfrage übernehmen
            if (sameCompany.Count > 0 &&
                MessageBoxWpf.Show($"„{name}“ wurde auch in {sameCompany.Count} weiteren Datei(en) erkannt.\n\nFirmenname für ALLE diese Einträge übernehmen?",
                    "Firmenname übernehmen", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                targets.AddRange(sameCompany);
            if (targets.Count == 0) return;

            await EnsureAddressAsync(src);
            if (src.CompanyName != name) return;
            _propagatingCompany = true;
            try
            {
                foreach (var m in targets)
                {
                    if (!m.CompanyCandidates.Contains(name)) m.CompanyCandidates.Add(name);
                    m.CompanyName = name;
                    TransferAddress(src, m);
                    // Nur lernen, wenn die Firma im Dokument tatsächlich vorkommt
                    if (m.FullText?.Contains(name, StringComparison.OrdinalIgnoreCase) == true) Engine.ConfirmSender(m.FullText, name);
                }
            }
            finally { _propagatingCompany = false; }
            var learned = await ApplyLearnedRulesAsync(targets.Prepend(src).ToList());
            TxtStatus.Text = $"Firmenname „{name}“ auf {targets.Count + 1} Einträge übernommen."
                + (learned > 0 ? $" Gelernte Nummer/Datum bei {learned} Beleg(en) übernommen." : "");
        }

        /// <summary>„Namen übertragen“: Übersicht aller Dateien, markierte erhalten den Firmennamen der aktuellen Datei.</summary>
        /// <summary>Belege in der Reihenfolge der Dateiliste (Sortierung wie angezeigt), weggefilterte danach in Ladereihenfolge –
        /// damit die Übertragen-Dialoge die Belege so zeigen, wie man sie sich in der Dateiliste hinsortiert hat.</summary>
        private List<DocumentMetadata> InListOrder(IEnumerable<DocumentMetadata> items)
        {
            var pos = new Dictionary<DocumentMetadata, int>(ReferenceEqualityComparer.Instance);
            foreach (var o in LvFiles.Items) if (o is DocumentMetadata m) pos.TryAdd(m, pos.Count);
            return items.Select((m, i) => (m, i)).OrderBy(t => pos.TryGetValue(t.m, out var p) ? p : int.MaxValue).ThenBy(t => t.i).Select(t => t.m).ToList();
        }

        private async void BtnMainTransferName_Click(object sender, RoutedEventArgs e)
        {
            if (_currentMeta is not { } src) return;
            var name = (string.IsNullOrWhiteSpace(CmbMainCompany.Text) ? src.CompanyName : CmbMainCompany.Text)?.Trim();
            if (string.IsNullOrWhiteSpace(name)) { TxtStatus.Text = "Kein Firmenname angegeben."; return; }
            if (!string.Equals(src.CompanyName, name, StringComparison.Ordinal))
            {
                _propagatingCompany = true;
                try { src.CompanyName = name; }
                finally { _propagatingCompany = false; }
            }
            var others = InListOrder(_items.Where(m => m != src));
            if (others.Count == 0) return;
            var dlg = new CompanyTransferDialog(others, name, n => !string.IsNullOrWhiteSpace(n) && SameCompany(n, name)) { Owner = this };
            if (dlg.ShowDialog() != true) return;
            var targets = dlg.SelectedItems.ToList();
            var edited = dlg.EditedNames.ToList();
            if (edited.Count > 0)
            {
                // Im Dialog von Hand korrigierte Namen ohne weitere Rückfrage übernehmen
                _propagatingCompany = true;
                try
                {
                    foreach (var (m, n) in edited)
                    {
                        if (!m.CompanyCandidates.Contains(n)) m.CompanyCandidates.Add(n);
                        m.CompanyName = n;
                    }
                }
                finally { _propagatingCompany = false; }
            }
            if (targets.Count == 0)
            {
                if (edited.Count > 0)
                {
                    var n = await ApplyLearnedRulesAsync(edited.Select(x => x.Item1).Prepend(src).ToList());
                    TxtStatus.Text = $"Firmenname bei {edited.Count} Datei(en) geändert." + (n > 0 ? $" Gelernte Nummer/Datum bei {n} Beleg(en) übernommen." : "");
                }
                return;
            }
            await EnsureAddressAsync(src);
            _propagatingCompany = true;
            try
            {
                foreach (var m in targets)
                {
                    if (!m.CompanyCandidates.Contains(name)) m.CompanyCandidates.Add(name);
                    m.CompanyName = name;
                    TransferAddress(src, m);
                    if (m.FullText?.Contains(name, StringComparison.OrdinalIgnoreCase) == true) Engine.ConfirmSender(m.FullText, name);
                }
            }
            finally { _propagatingCompany = false; }
            var learnedCount = await ApplyLearnedRulesAsync(targets.Concat(edited.Select(x => x.Item1)).Prepend(src).Distinct().ToList());
            var adr = FormatAddress(src);
            TxtStatus.Text = $"Firmenname „{name}“" + (adr.Length > 0 ? $" mit Adresse ({adr})" : " mit leerer Adresse") + $" auf {targets.Count + 1} Datei(en) übertragen."
                + (edited.Count > 0 ? $" {edited.Count} weitere von Hand geändert." : "")
                + (learnedCount > 0 ? $" Gelernte Nummer/Datum bei {learnedCount} Beleg(en) übernommen." : "");
        }

        /// <summary>„Belegart übertragen“: Belege gruppiert nach Firma/Nummernaufbau/Belegart, markierte bzw. zugewiesene erhalten die Belegart.</summary>
        private async void BtnMainTransferDocType_Click(object sender, RoutedEventArgs e)
        {
            if (_currentMeta is not { Loaded: true } src || _docTypeApplying) return;
            var type = (string.IsNullOrWhiteSpace(CmbDocType.Text) ? src.DocumentType : CmbDocType.Text)?.Trim();
            if (!string.IsNullOrWhiteSpace(type) && !string.Equals(src.DocumentType, type, StringComparison.Ordinal))
            {
                src.DocumentType = type;
                src.DocTypeManuallySet = true;
            }
            var others = InListOrder(_items.Where(m => m != src && m.Loaded));
            if (others.Count == 0) { TxtStatus.Text = "Keine weiteren geladenen Belege."; return; }
            var dlg = new DocTypeTransferDialog(src, others, Dateiumbenenner.Engine.DocTypeStore.Names,
                (a, b) => !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) && SameCompany(a, b)) { Owner = this };
            if (dlg.ShowDialog() != true || dlg.Assignments.Count == 0) return;
            _docTypeApplying = true;
            int learned;
            try
            {
                var tick = System.Diagnostics.Stopwatch.StartNew();
                int i = 0;
                foreach (var (m, t) in dlg.Assignments)
                {
                    m.DocumentType = t;
                    m.DocTypeManuallySet = true;
                    AutoAssignNumbers(m);
                    ScheduleStoreSave(m);
                    i++;
                    if (tick.ElapsedMilliseconds >= 40)
                    {
                        tick.Restart();
                        TxtStatus.Text = $"Belegart wird übertragen: {i:N0} / {dlg.Assignments.Count:N0}...";
                        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
                    }
                }
                // Gelernte Nummer/Datum der neuen Kombination Firma + Belegart übernehmen
                learned = await ApplyLearnedRulesAsync(dlg.Assignments.Keys.Prepend(src).ToList());
            }
            finally { _docTypeApplying = false; }
            LvFiles.Items.Refresh();
            RefreshAfterRules(src);
            TxtStatus.Text = $"Belegart auf {dlg.Assignments.Count} Datei(en) übertragen: "
                + string.Join(", ", dlg.Assignments.GroupBy(a => a.Value, StringComparer.OrdinalIgnoreCase).Select(g => $"{g.Key} ({g.Count()})"))
                + (learned > 0 ? $". Gelernte Nummer/Datum bei {learned} Beleg(en) übernommen." : "");
        }

        private async void BtnMainFromIni_Click(object sender, RoutedEventArgs e)
        {
            var targets = LvFiles.SelectedItems.OfType<DocumentMetadata>().ToList();
            if (targets.Count <= 1) targets = _items.ToList();
            if (targets.Count == 0) return;
            TxtStatus.Text = "Werte aus INI Erzeugen werden gelesen...";
            var data = await Task.Run(() => targets.Select(m => (m, json: DocumentStore.LoadPluginData("IniGenerator.Values", m.FilePath))).Where(x => x.json != null).ToList());
            int n = 0;
            _propagatingCompany = true;
            try
            {
                foreach (var (m, json) in data)
                {
                    Dictionary<string, string?>? v;
                    try { v = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string?>>(json!); } catch { continue; }
                    if (v == null) continue;
                    if (v.TryGetValue("CompanyName", out var c) && !string.IsNullOrWhiteSpace(c) && c != m.CompanyName)
                    {
                        if (!m.CompanyCandidates.Contains(c)) m.CompanyCandidates.Insert(0, c);
                        m.CompanyName = c; ResetAddress(m); _confirmedCompany.Add(m);
                    }
                    if (v.TryGetValue("DocumentType", out var t) && !string.IsNullOrWhiteSpace(t)) { m.DocumentType = t; m.DocTypeManuallySet = true; }
                    if (v.TryGetValue("Number", out var nr) && !string.IsNullOrWhiteSpace(nr)) { if (!m.NumberCandidates.Contains(nr)) m.NumberCandidates.Insert(0, nr); m.SelectedNumber = nr; m.NumberManuallySet = true; }
                    if (v.TryGetValue("Date", out var d) && !string.IsNullOrWhiteSpace(d)) { if (!m.DateCandidates.Contains(d)) m.DateCandidates.Insert(0, d); m.SelectedDate = d; m.DateManuallySet = true; }
                    ScheduleStoreSave(m);
                    n++;
                }
            }
            finally { _propagatingCompany = false; }
            foreach (var (m, _) in data) RefreshNewNamePreview(m);
            if (_currentMeta != null) { var cur = _currentMeta; LvFiles.SelectedItem = null; LvFiles.SelectedItem = cur; }
            TxtStatus.Text = n == 0 ? "Keine Werte aus INI Erzeugen gefunden (zuerst dort INI erzeugen)." : $"Werte aus INI Erzeugen für {n} von {targets.Count} Datei(en) übernommen.";
        }

        private static bool SameCompany(string a, string b) =>
            string.Equals(a, b, StringComparison.OrdinalIgnoreCase) ||
            CompanyMatcher.Similarity(CompanyMatcher.Normalize(a), CompanyMatcher.Normalize(b)) >= CompanyMatcher.Threshold;

        /// <summary>Anteil der Auswahlmöglichkeiten, die in beiden Listen (gleich oder ähnlich) vorkommen.</summary>
        private static double CandidateOverlap(IList<string> a, IList<string> b)
        {
            if (a.Count == 0 || b.Count == 0) return 0;
            var small = a.Count <= b.Count ? a : b; var large = small == a ? b : a;
            return (double)small.Count(x => large.Any(y => SameCompany(x, y))) / small.Count;
        }

        /// <summary>Firma geändert: Adresse (Dokument/DB) für die neue Firma neu ermitteln.</summary>
        private void ResetAddress(DocumentMetadata meta)
        {
            meta.AddressLoaded = false;
            meta.AddressManuallySet = false;
            meta.Street = meta.Zip = meta.City = null;
            if (meta == _currentMeta) ShowDetails(meta);
        }

        /// <summary>Firma übertragen = Adresse bestätigt: Straße, PLZ und Ort der Quelle werden übernommen – auch leer
        /// (entfernt z. B. eine fälschlich erkannte Empfängeradresse) und danach nicht automatisch neu ermittelt.</summary>
        private void TransferAddress(DocumentMetadata src, DocumentMetadata target)
        {
            target.Street = src.Street;
            target.Zip = src.Zip;
            target.City = src.City;
            target.AddressLoaded = true;
            target.AddressManuallySet = true;
            _addressTasks.Remove(target);
            if (target == _currentMeta) ShowDetails(target);
            RefreshNewNamePreview(target);
            ScheduleStoreSave(target);
        }

        private static string FormatAddress(DocumentMetadata m)
            => string.Join(", ", new[] { m.Street, $"{m.Zip} {m.City}".Trim() }.Where(s => !string.IsNullOrWhiteSpace(s)));

        private static string AutoTemplatesOffPath => Path.Combine(Dateiumbenenner.Plugins.PluginManager.BaseDirectory, "templates_auto.off");
        private bool AutoTemplates
        {
            get => !File.Exists(AutoTemplatesOffPath);
            set { try { if (value) { if (File.Exists(AutoTemplatesOffPath)) File.Delete(AutoTemplatesOffPath); } else { Directory.CreateDirectory(Path.GetDirectoryName(AutoTemplatesOffPath)!); File.WriteAllText(AutoTemplatesOffPath, ""); } } catch { } }
        }

        private void MenuNumberRules_Click(object sender, RoutedEventArgs e)
        {
            var samples = _items.Where(i => i.Loaded).SelectMany(i => new[] { i.SelectedNumber }.Concat(i.NumberCandidates)
                .Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(n => ((string?)i.CompanyName, (string?)i.DocumentType, (string?)n))).ToList();
            var dlg = new NumberRulesWindow(_currentMeta?.CompanyName, _currentMeta?.DocumentType, samples) { Owner = this };
            string? amount = null;
            try { if (!string.IsNullOrEmpty(_currentMeta?.FullText)) amount = Engine.ExtractAmount(_currentMeta.FullText).Value; } catch { }
            dlg.SetDocument(_currentMeta?.FilePath, _currentMeta?.FullText, _currentMeta?.SelectedNumber, _currentMeta?.SelectedDate, amount);
            if (dlg.ShowDialog() != true) return;
            if (CmbDocType != null) CmbDocType.ItemsSource = Dateiumbenenner.Engine.DocTypeStore.Names;
            if (dlg.Recalculate) RecalculateNumbers();
        }

        private void MenuRecalcNumbers_Click(object sender, RoutedEventArgs e) => RecalculateNumbers();

        /// <summary>Schnellaktion neben der Nummer: falsche Nummer für Firma + Belegart ausschließen, andere bevorzugen, Nummernfeld festlegen –
        /// danach auf alle Belege dieser Firma + Belegart anwenden.</summary>
        private async void BtnNumberType_Click(object sender, RoutedEventArgs e)
        {
            var meta = _currentMeta;
            if (meta == null || string.IsNullOrWhiteSpace(meta.CompanyName) || string.IsNullOrWhiteSpace(meta.DocumentType))
            {
                MessageBoxWpf.Show(this, "Bitte zuerst Firma und Belegart des Belegs festlegen.", "Nummer für Firma + Belegart");
                return;
            }
            var number = CmbNumberCandidates?.Text?.Trim();
            if (string.IsNullOrEmpty(number)) number = meta.SelectedNumber;
            var dlg = new NumberTypeWindow(meta.CompanyName, meta.DocumentType, number, meta.FullText, meta.NumberCandidates) { Owner = this };
            if (dlg.ShowDialog() != true) return;
            await ReapplyNumbersAsync(meta.CompanyName, meta.DocumentType);
        }

        /// <summary>Nummern aller Belege einer Firma + Belegart nach den aktuellen Regeln neu ermitteln (Erkennung parallel im Hintergrund,
        /// Übernahme mit Fortschritt). Von Hand gesetzte Nummern bleiben; das Nummernfeld wird bei allen angepasst.</summary>
        private async Task ReapplyNumbersAsync(string? company, string? docType)
        {
            var dt = Dateiumbenenner.Engine.DocTypeStore.Canonical(docType);
            var list = _items.Where(i => i.Loaded && !string.IsNullOrEmpty(i.FullText)
                    && string.Equals(i.CompanyName?.Trim(), company?.Trim(), StringComparison.OrdinalIgnoreCase)
                    && string.Equals(Dateiumbenenner.Engine.DocTypeStore.Canonical(i.DocumentType), dt, StringComparison.OrdinalIgnoreCase)).ToList();
            if (list.Count == 0) return;
            var input = list.Select(m => (Auto: !m.NumberManuallySet, Text: m.FullText!, Path: m.FilePath)).ToArray();
            var fields = new Dateiumbenenner.Engine.Field?[input.Length];
            TxtStatus.Text = $"Nummern für {company} / {docType} werden neu ermittelt ({list.Count:N0} Belege)...";
            await Task.Run(() => Parallel.For(0, input.Length, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) }, k =>
            {
                if (input[k].Auto) try { fields[k] = Engine.ExtractNumber(input[k].Text, input[k].Path); } catch { }
            }));
            var tick = System.Diagnostics.Stopwatch.StartNew();
            int changed = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var m = list[i];
                string? sel = m.SelectedNumber, inv = m.InvoiceNumber, doc = m.DocumentNumber;
                if (fields[i] is { } f && !m.NumberManuallySet) ApplyNumberField(m, f, m.DocumentType);
                AutoAssignNumbers(m);
                if (sel != m.SelectedNumber || inv != m.InvoiceNumber || doc != m.DocumentNumber)
                {
                    changed++;
                    RefreshNewNamePreview(m);
                    ScheduleStoreSave(m);
                }
                if (tick.ElapsedMilliseconds >= 40)
                {
                    tick.Restart();
                    TxtStatus.Text = $"Nummern für {company} / {docType}: {i + 1:N0} / {list.Count:N0}...";
                    await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
                }
            }
            if (_currentMeta is { } cur && list.Contains(cur)) RefreshAfterRules(cur);
            LvFiles.Items.Refresh();
            TxtStatus.Text = $"Nummernregel für {company} / {docType} angewendet: {changed:N0} von {list.Count:N0} Beleg(en) geändert.";
        }

        /// <summary>TXT-Datei eines Belegs: A-Modus-Quelle, die Datei selbst oder eine gleichnamige .txt.</summary>
        private static string? TxtOf(DocumentMetadata m)
        {
            if (!string.IsNullOrEmpty(m.TextSourcePath)) return m.TextSourcePath;
            if (string.IsNullOrEmpty(m.FilePath)) return null;
            if (Path.GetExtension(m.FilePath).Equals(".txt", StringComparison.OrdinalIgnoreCase)) return m.FilePath;
            var txt = Path.ChangeExtension(m.FilePath, ".txt");
            return File.Exists(txt) ? txt : null;
        }

        private void MenuNumberFix_Click(object sender, RoutedEventArgs e)
        {
            static NumberFixDoc? Doc(DocumentMetadata m) => TxtOf(m) is { } t ? new NumberFixDoc(m.FileName, t) : null;
            var all = _items.Select(Doc).OfType<NumberFixDoc>().ToList();
            if (all.Count == 0)
            {
                MessageBoxWpf.Show(this, "In der Liste gibt es keine Belege mit TXT-Datei.\n\nDie Korrektur ändert die Textdateien (z. B. im A-Modus die gleichnamige .txt). Text, der direkt aus einer PDF gelesen wird, kann so nicht korrigiert werden.", "TXT-Nummernkorrektur");
                return;
            }
            var selected = LvFiles.SelectedItems.OfType<DocumentMetadata>().Select(Doc).OfType<NumberFixDoc>().ToList();
            string? example = null;
            try { example = TxtPreviewRich?.Selection?.Text?.Trim(); } catch { }
            if (string.IsNullOrEmpty(example) || example.Length > 40 || example.Contains('\n')) example = _currentMeta?.SelectedNumber;
            new NumberFixWindow(all, selected, example) { Owner = this }.ShowDialog();
        }

        private int _txtReloadRun;
        /// <summary>Nach einer TXT-Korrektur die betroffenen Belege neu einlesen und auswerten.</summary>
        private async void OnTextFilesChanged(IReadOnlyCollection<string> files)
        {
            if (!Dispatcher.CheckAccess()) { _ = Dispatcher.InvokeAsync(() => OnTextFilesChanged(files)); return; }
            int run = ++_txtReloadRun;
            var set = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
            var affected = _items.Where(m => m.Loaded && TxtOf(m) is { } t && set.Contains(t)
                && (!string.IsNullOrEmpty(m.TextSourcePath) || Path.GetExtension(m.FilePath).Equals(".txt", StringComparison.OrdinalIgnoreCase))).ToList();
            if (affected.Count == 0) return;
            for (int i = 0; i < affected.Count; i++)
            {
                if (run != _txtReloadRun) return;
                var m = affected[i];
                m.OriginalFullText = null;
                await LoadMetadataAsync(m);
                AutoAssignNumbers(m);
                ScheduleStoreSave(m);
                TxtStatus.Text = $"Korrigierte TXT-Dateien werden neu ausgewertet: {i + 1:N0} / {affected.Count:N0}";
            }
            LvFiles.Items.Refresh();
            if (_currentMeta != null && affected.Contains(_currentMeta))
            {
                _isPopulatingSelectors = true;
                try { PopulateSelectors(_currentMeta); ShowDetails(_currentMeta); RefreshNewNamePreview(_currentMeta); }
                finally { _isPopulatingSelectors = false; }
                _ = UpdatePdfPreviewAsync(_currentMeta);
            }
            UpdateStandardStatus();
        }

        /// <summary>Belegart und Belegnummer aller geladenen Dateien neu ermitteln (manuell gesetzte Werte bleiben).</summary>
        private void RecalculateNumbers()
        {
            int n = 0;
            foreach (var m in _items.Where(i => i.Loaded))
            {
                var before = m.SelectedNumber;
                ApplyEngineFields(m);
                if (!string.Equals(before, m.SelectedNumber, StringComparison.Ordinal)) n++;
            }
            LvFiles?.Items.Refresh();
            if (_currentMeta != null) { ShowDetails(_currentMeta); RefreshNewNamePreview(_currentMeta); }
            TxtStatus.Text = $"Belegnummern neu ermittelt: {n} ge\u00E4ndert.";
        }

        private void MenuSettings_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SettingsWindow(AutoTemplates, AutoApplyTemplates) { Owner = this };
            if (dlg.ShowDialog() != true) return;
            AutoTemplates = dlg.AskTemplates;
            AutoApplyTemplates = dlg.AutoApplyTemplates;
            if (MessageBoxWpf.Show("Einstellungen gespeichert.\n\nFirmenerkennung für alle geladenen Dateien mit den neuen Ausschlüssen neu ausführen?",
                    "Einstellungen", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            _propagatingCompany = true;
            try
            {
                foreach (var m in _items)
                {
                    var current = m.CompanyName;
                    m.CompanyCandidates.Clear();
                    foreach (var c in ExtractCompanyNames(m.FullText, m.FilePath)) m.CompanyCandidates.Add(c);
                    m.DetectedCompany = m.CompanyCandidates.FirstOrDefault();
                    if (string.IsNullOrWhiteSpace(current) || Dateiumbenenner.Engine.CompanyExclusions.IsExcluded(current))
                        m.CompanyName = m.CompanyCandidates.FirstOrDefault();
                    else if (!m.CompanyCandidates.Contains(current)) m.CompanyCandidates.Insert(0, current);
                }
            }
            finally { _propagatingCompany = false; }
            TxtStatus.Text = "Firmenerkennung mit neuen Ausschlüssen aktualisiert.";
        }

        private Dateiumbenenner.Engine.DocTemplate CaptureTemplate(DocumentMetadata m) => new()
        {
            Delimiter = m.SavedDelimiter,
            SegmentIndex = m.SavedSegmentIndex,
            UseDelimiterPosition = m.UseDelimiterPosition,
            RemoveDuplicates = m.RemoveDuplicates,
            ReplaceSegments = m.ReplaceSegments,
            RemoveAttachment = m.RemoveAttachment,
            InsertPositions = m.InsertPositions.Select(p => new Dateiumbenenner.Engine.TemplatePosition { Position = p.Position, ValueType = p.ValueType, Enabled = p.Enabled }).ToList(),
            PrefixDelimiter = TxtPrefixDelimiter?.Text,
            PrefixSegment = TxtPrefixSegment?.Text,
            PrefixRemove = TxtPrefixRemove?.Text
        };

        private void ApplyTemplate(DocumentMetadata m, Dateiumbenenner.Engine.DocTemplate t)
        {
            ApplyLayout(m, t);
            m.TemplateDeclined = null;
            if (m == _currentMeta)
            {
                if (t.PrefixDelimiter != null && TxtPrefixDelimiter != null) TxtPrefixDelimiter.Text = t.PrefixDelimiter;
                if (t.PrefixSegment != null && TxtPrefixSegment != null) TxtPrefixSegment.Text = t.PrefixSegment;
                if (t.PrefixRemove != null && TxtPrefixRemove != null) TxtPrefixRemove.Text = t.PrefixRemove;
                RestoreSettings(m);
                PopulateSelectors(m);
            }
            RefreshNewNamePreview(m);
            ScheduleStoreSave(m);
        }

        /// <summary>Gespeicherte Vorlagen (Firma + Dokumenttyp) nach Rückfrage anwenden.</summary>
        private void OfferTemplates(IEnumerable<DocumentMetadata> metas)
        {
            if (!AutoTemplates) return;
            // Nur Dokumente, deren Einstellungen noch von der Vorlage abweichen
            var hits = metas.Select(m => (m, t: Dateiumbenenner.Engine.DocTemplateStore.Get(m.CompanyName, m.DocumentType)))
                            .Where(x => x.t != null && CompanyMatcher.AppearsInText(x.m.CompanyName, x.m.FullText) && !TemplateMatches(x.m, x.t)
                                        && x.m.TemplateDeclined != DeclineKey(x.m, x.t))
                            .Select(x => (x.m, t: x.t!)).ToList();
            if (hits.Count == 0) return;
            var groups = hits.GroupBy(x => ((x.m.CompanyName ?? "").Trim() + "|" + (x.m.DocumentType ?? "").Trim()).ToLowerInvariant())
                             .Select(g => g.ToList()).ToList();
            var chosen = AutoApplyTemplates ? groups : AskTemplateGroups(groups);
            // Abgelehnte Vorlagen je Datei merken, damit nach einem Neustart nicht erneut gefragt wird
            foreach (var (m, t) in groups.Where(g => chosen == null || !chosen.Contains(g)).SelectMany(g => g))
            {
                m.TemplateDeclined = DeclineKey(m, t);
                ScheduleStoreSave(m);
            }
            if (chosen == null || chosen.Count == 0) return;
            foreach (var (m, t) in chosen.SelectMany(g => g)) ApplyTemplate(m, t);
            TxtStatus.Text = "Vorlage angewendet: " + string.Join(", ", chosen.Select(g => $"{g[0].m.CompanyName} – {g[0].m.DocumentType} ({g.Count})"))
                + (AutoApplyTemplates ? "  (automatisch, abschaltbar unter Einstellungen)" : "");
        }

        private static string DeclineKey(DocumentMetadata m, Dateiumbenenner.Engine.DocTemplate t)
            => $"{m.CompanyName?.Trim()}|{m.DocumentType?.Trim()}|{DescribeTemplate(t)}".ToLowerInvariant();

        private static string AutoApplyTemplatesPath => Path.Combine(Dateiumbenenner.Plugins.PluginManager.BaseDirectory, "templates_auto.apply");
        /// <summary>Vorlagen beim Laden ohne Rückfrage anwenden.</summary>
        private bool AutoApplyTemplates
        {
            get => File.Exists(AutoApplyTemplatesPath);
            set { try { if (!value) { if (File.Exists(AutoApplyTemplatesPath)) File.Delete(AutoApplyTemplatesPath); } else { Directory.CreateDirectory(Path.GetDirectoryName(AutoApplyTemplatesPath)!); File.WriteAllText(AutoApplyTemplatesPath, ""); } } catch { } }
        }

        private static List<(int Pos, string Type)> EffectivePositions(IEnumerable<(int Pos, string Type, bool Enabled)>? list)
        {
            var l = list?.Where(p => p.Enabled).Select(p => (p.Pos, p.Type)).ToList() ?? new();
            if (l.Count == 0) l = new() { (1, "Nummer (Auswahl)"), (2, "Datum"), (3, "Dokumenttyp") };
            return l.OrderBy(p => p.Pos).ThenBy(p => p.Type).ToList();
        }

        /// <summary>Entspricht das Dokument bereits der Vorlage? Dann muss nicht gefragt werden.</summary>
        private static bool TemplateMatches(DocumentMetadata m, Dateiumbenenner.Engine.DocTemplate t)
        {
            if (!string.IsNullOrEmpty(t.Delimiter) && m.SavedDelimiter != t.Delimiter) return false;
            if (t.SegmentIndex.HasValue && m.SavedSegmentIndex != t.SegmentIndex) return false;
            if (m.UseDelimiterPosition != t.UseDelimiterPosition || m.RemoveDuplicates != t.RemoveDuplicates
                || m.ReplaceSegments != t.ReplaceSegments || m.RemoveAttachment != t.RemoveAttachment) return false;
            return t.InsertPositions.Count == 0
                || EffectivePositions(m.InsertPositions?.Select(p => (p.Position, p.ValueType, p.Enabled)))
                   .SequenceEqual(EffectivePositions(t.InsertPositions.Select(p => (p.Position, p.ValueType, p.Enabled))));
        }

        private static string DescribeTemplate(Dateiumbenenner.Engine.DocTemplate t)
        {
            var parts = new List<string> { "Trennzeichen " + (string.IsNullOrEmpty(t.Delimiter) ? "automatisch" : $"„{t.Delimiter}“") };
            if (t.SegmentIndex.HasValue) parts.Add($"Segment {t.SegmentIndex}");
            var pos = t.InsertPositions.Where(p => p.Enabled).OrderBy(p => p.Position).Select(p => $"{p.Position}. {p.ValueType}").ToList();
            if (pos.Count > 0) parts.Add("Einfügen: " + string.Join(", ", pos));
            if (t.RemoveDuplicates) parts.Add("doppelte Segmente entfernen");
            if (t.ReplaceSegments) parts.Add("Segmente ersetzen");
            if (t.RemoveAttachment) parts.Add("Anhang entfernen");
            return string.Join(" · ", parts);
        }

        /// <summary>Auswahl, welche Vorlagen angewendet werden: je Firma + Dokumenttyp mit Anzahl, Inhalt und Dateiliste (Tooltip).</summary>
        private List<List<(DocumentMetadata m, Dateiumbenenner.Engine.DocTemplate t)>>? AskTemplateGroups(List<List<(DocumentMetadata m, Dateiumbenenner.Engine.DocTemplate t)>> groups)
        {
            var dlg = new Window { Title = "Gespeicherte Vorlagen übernehmen?", Owner = this, SizeToContent = SizeToContent.WidthAndHeight, MaxHeight = 600, MaxWidth = 900,
                                   WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize };
            var panel = new StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 640, Margin = new Thickness(0, 0, 0, 10),
                Text = "Für folgende Firmen und Dokumenttypen haben Sie früher eine Vorlage gespeichert (Button „Eigenschaften übertragen“). " +
                       "Eine Vorlage legt fest, wie der neue Dateiname aufgebaut wird (Trennzeichen, Segment, Einfügepositionen). " +
                       "Die Firma oder andere erkannte Werte werden dabei nicht geändert.\n\nHaken = Vorlage auf diese Dokumente anwenden:" });
            var boxes = new List<(CheckBoxWpf box, List<(DocumentMetadata m, Dateiumbenenner.Engine.DocTemplate t)> group)>();
            var list = new StackPanel();
            foreach (var g in groups)
            {
                var (m0, t0) = g[0];
                var files = g.Select(x => x.m.FileName).Take(25).ToList();
                var box = new CheckBoxWpf
                {
                    IsChecked = true, Margin = new Thickness(0, 3, 0, 3),
                    ToolTip = string.Join("\n", files) + (g.Count > files.Count ? $"\n… und {g.Count - files.Count} weitere" : ""),
                    Content = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 610,
                        Text = $"{m0.CompanyName} – {m0.DocumentType}:  {g.Count} Dokument(e)\n    {DescribeTemplate(t0)}" }
                };
                boxes.Add((box, g));
                list.Children.Add(box);
            }
            panel.Children.Add(new ScrollViewer { Content = list, MaxHeight = 330, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            var chkAuto = new CheckBoxWpf { Content = "Künftig ohne Rückfrage übernehmen (abschaltbar unter Einstellungen)", Margin = new Thickness(0, 12, 0, 0) };
            panel.Children.Add(chkAuto);
            var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var ok = new System.Windows.Controls.Button { Content = "Übernehmen", Width = 100, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var no = new System.Windows.Controls.Button { Content = "Nicht übernehmen", Width = 120, IsCancel = true };
            ok.Click += (_, _) => dlg.DialogResult = true;
            buttons.Children.Add(ok); buttons.Children.Add(no);
            panel.Children.Add(buttons);
            dlg.Content = panel;
            if (dlg.ShowDialog() != true) return null;
            if (chkAuto.IsChecked == true) AutoApplyTemplates = true;
            return boxes.Where(b => b.box.IsChecked == true).Select(b => b.group).ToList();
        }

        private static Dateiumbenenner.TransferDoc TransferDocOf(DocumentMetadata m, Func<DocumentMetadata, string> layout) => new()
        {
            Item = m, FilePath = m.FilePath, Company = m.CompanyName, Detected = m.DetectedCompany ?? m.CompanyCandidates.FirstOrDefault(),
            DocType = m.DocumentType, Number = m.SelectedNumber, Date = m.SelectedDate, FullText = m.FullText, Previous = m.PreviousCompanies,
            Values = new()
            {
                ["Layout"] = layout(m), ["Company"] = m.CompanyName, ["Address"] = FormatAddress(m), ["DocType"] = m.DocumentType
            }
        };

        /// <summary>„Eigenschaften übertragen“: Dateinamen-Aufbau (Vorlage für Firma + Dokumenttyp), Firma, Adresse und Belegart des aktuellen
        /// Dokuments – auch nachträglich – auf gleichartige Dokumente übertragen. Ziele werden im Dialog gruppiert gewählt (markierte sind angehakt).</summary>
        private async void BtnApplyTemplate_Click(object sender, RoutedEventArgs e)
        {
            if (_currentMeta is not { Loaded: true } src) { MessageBoxWpf.Show("Bitte zuerst ein Dokument auswählen.", "Eigenschaften übertragen"); return; }
            SaveCurrentSettings(src);
            var others = InListOrder(_items.Where(m => m != src));
            if (others.Count == 0) { TxtStatus.Text = "Keine weiteren Dokumente zum Übertragen."; return; }
            var t = CaptureTemplate(src);
            var stored = Dateiumbenenner.Engine.DocTemplateStore.Get(src.CompanyName, src.DocumentType);
            var layoutText = DescribeTemplate(t);
            string LayoutOf(DocumentMetadata m) => m == src || TemplateMatches(m, t) ? layoutText : "abweichend";
            bool companyChanged = !string.IsNullOrWhiteSpace(src.CompanyName) && !string.IsNullOrWhiteSpace(src.DetectedCompany)
                && !string.Equals(src.CompanyName.Trim(), src.DetectedCompany.Trim(), StringComparison.OrdinalIgnoreCase);
            var props = new List<Dateiumbenenner.TransferProperty>
            {
                new() { Key = "Layout", Label = "Dateinamen-Aufbau", Value = layoutText, Changed = stored == null || !TemplateMatches(src, stored), Selected = true,
                        ToolTip = "Trennzeichen, Segment und Einfügepositionen. Wird außerdem als Vorlage für Firma + Dokumenttyp gespeichert\n(neue Dokumente dieser Art erhalten sie automatisch bzw. nach Rückfrage)." },
                new() { Key = "Company", Label = "Firma", Value = src.CompanyName, Changed = companyChanged, Selected = companyChanged },
                new() { Key = "Address", Label = "Adresse", Value = FormatAddress(src), Changed = src.AddressManuallySet, Selected = src.AddressManuallySet || companyChanged,
                        ToolTip = "Straße, PLZ und Ort der Quelle – auch leer. Ohne Haken wird die Adresse bei geänderter Firma neu ermittelt." },
                new() { Key = "DocType", Label = "Belegart", Value = src.DocumentType, Changed = src.DocTypeManuallySet, Selected = src.DocTypeManuallySet },
                new() { Key = "Rules", Label = "Danach Nummern- und Datumsregeln neu anwenden", IsOption = true, Selected = true,
                        ToolTip = "Gelerntes Nummernformat, Nummern- und Datumsregeln nach Firma + Belegart des Ziels. Von Hand gesetzte Nummern und Daten bleiben." },
                new() { Key = "Learn", Label = "Firma lernen", IsOption = true, Selected = true,
                        ToolTip = "Nur Dokumente, in deren Text die Firma steht: Merkmale wie IBAN oder USt-ID werden der Firma zugeordnet." },
            };
            var sourceCompanies = new[] { src.CompanyName, src.DetectedCompany, src.CompanyCandidates.FirstOrDefault() }.Concat(src.PreviousCompanies);
            var marked = LvFiles.SelectedItems.OfType<DocumentMetadata>().Where(m => m != src).Cast<object>().ToList();
            var dlg = new Dateiumbenenner.PropertyTransferDialog(TransferDocOf(src, LayoutOf), sourceCompanies, others.Select(m => TransferDocOf(m, LayoutOf)), props,
                (a, b) => !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) && SameCompany(a, b), marked) { Owner = this };
            if (dlg.ShowDialog() != true) return;
            var targets = dlg.SelectedItems.OfType<DocumentMetadata>().ToList();
            if (targets.Count == 0) return;

            bool layout = dlg.Has("Layout"), doCompany = dlg.Has("Company") && !string.IsNullOrWhiteSpace(src.CompanyName), doAddress = dlg.Has("Address"),
                 doType = dlg.Has("DocType"), rules = dlg.Has("Rules"), learn = dlg.Has("Learn");
            var name = src.CompanyName?.Trim();
            bool saved = layout && !string.IsNullOrWhiteSpace(src.CompanyName) && !string.IsNullOrWhiteSpace(src.DocumentType);
            if (saved) Dateiumbenenner.Engine.DocTemplateStore.Set(src.CompanyName!, src.DocumentType!, t);
            if (doAddress) await EnsureAddressAsync(src);
            int learned = 0;
            _propagatingCompany = true;
            try
            {
                foreach (var m in targets)
                {
                    if (doCompany && !string.Equals(m.CompanyName?.Trim(), name, StringComparison.Ordinal))
                    {
                        if (!m.CompanyCandidates.Contains(name!)) m.CompanyCandidates.Add(name!);
                        m.CompanyName = name;
                        _confirmedCompany.Add(m);
                    }
                    if (doAddress) TransferAddress(src, m);
                    if (doType && !string.IsNullOrWhiteSpace(src.DocumentType)) { m.DocumentType = src.DocumentType; m.DocTypeManuallySet = true; }
                    if (rules && m.Loaded && !string.IsNullOrEmpty(m.FullText))
                    {
                        ApplyNumberField(m, Engine.ExtractNumber(m.FullText, m.FilePath), m.DocumentType);
                        ReapplyDateRules(m);
                        AutoAssignNumbers(m);
                    }
                    if (layout) ApplyTemplate(m, t);
                    if (doCompany && learn && m.FullText?.Contains(name!, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        Engine.ConfirmSender(m.FullText, name!);
                        learned++;
                    }
                    RefreshNewNamePreview(m);
                    ScheduleStoreSave(m);
                }
            }
            finally { _propagatingCompany = false; }
            LvFiles.Items.Refresh();
            ShowDetails(src);
            var what = new List<string>();
            if (layout) what.Add("Dateinamen-Aufbau");
            if (doCompany) what.Add("Firma");
            if (doAddress) what.Add("Adresse");
            if (doType) what.Add("Belegart");
            TxtStatus.Text = (what.Count > 0 ? string.Join(", ", what) : "Regeln") + $" auf {targets.Count} Dokument(e) übertragen"
                + (rules ? ", Regeln neu angewendet" : "") + (learned > 0 ? $", Firma aus {learned} Dokument(en) gelernt" : "")
                + (saved ? $". Vorlage „{src.CompanyName} – {src.DocumentType}“ gespeichert." : ".");
        }

        private void BtnPrefixAction_Click(object sender, RoutedEventArgs e)
        {
            if (PrefixRenamerTab == null || sender is not FrameworkElement fe || fe.Tag is not string action) return;
            PrefixRenamerTab.RunAction(action);
        }

        private void PrefixSettings_Changed(object sender, TextChangedEventArgs e)
        {
            if (PrefixRenamerTab == null || TxtPrefixDelimiter == null || TxtPrefixSegment == null || TxtPrefixRemove == null) return;
            PrefixRenamerTab.SetSplitSettings(TxtPrefixDelimiter.Text, TxtPrefixSegment.Text, TxtPrefixRemove.Text);
            if (_currentMeta != null) RefreshNewNamePreview(_currentMeta);
        }

        private void BtnCompanyPrefix_Click(object sender, RoutedEventArgs e)
        {
            if (PrefixRenamerTab == null) return;
            if (LvFiles.SelectedItems.Count == 0) { MessageBoxWpf.Show("Bitte zuerst Dateien in der Liste markieren.", "Präfix"); return; }
            PrefixRenamerTab.SyncSelection(LvFiles.SelectedItems.OfType<DocumentMetadata>().Select(m => m.FilePath));
            var n = PrefixRenamerTab.ApplyCompanyNameAsPrefixToChecked();
            TxtStatus.Text = n > 0 ? $"Firmenname als Präfix für {n} Datei(en) gesetzt – mit „Präfix-Umbenennung ausführen“ umbenennen." : "Für die markierten Dateien wurde kein Firmenname erkannt.";
        }

        private async void MenuPlzUpdate_Click(object sender, RoutedEventArgs e)
        {
            var plz = Dateiumbenenner.Engine.DocumentEngine.Plz;
            var before = plz.Count;
            var bundledDate = Dateiumbenenner.Engine.PlzDirectory.BundledDate;
            var info = $"Aktiv: {before:N0} PLZ – {plz.Source}" + (plz.Warning != null ? $"\n\nAchtung: {plz.Warning}" : "")
                + (!plz.IsBundled && bundledDate is { } bd
                    ? $"\n\nMitgeliefert ist außerdem die GeoNames-Liste (Stand {bd:dd.MM.yyyy}). Sie gilt, solange keine Daten\\DE.txt vorhanden ist – zurückstellen über „PLZ-Verzeichnis: mitgelieferte Liste verwenden…“."
                    : "");
            if (MessageBoxWpf.Show($"{info}\n\nAktualisierung prüfen?\nQuelle (fest, nur HTTPS, keine Umleitung):\n{Dateiumbenenner.Engine.PlzDirectory.SourceUrl}\n\n"
                + "Die Daten werden zuerst geprüft und mit der aktiven Liste verglichen. Übernommen wird erst nach deiner Bestätigung.",
                "PLZ-Verzeichnis", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            var old = Cursor;
            Cursor = System.Windows.Input.Cursors.Wait;
            if (TxtStatus != null) TxtStatus.Text = "PLZ-Verzeichnis wird geladen und geprüft...";
            Dateiumbenenner.Engine.PlzDirectory.PlzUpdate? update = null;
            try
            {
                update = await plz.DownloadGeoNamesAsync();
                Cursor = old;
                var u = update;
                var msg = $"Quelle: {u.Url}\n"
                    + (u.LastModified is { } lm ? $"Datenstand laut Server: {lm.LocalDateTime:dd.MM.yyyy HH:mm}\n" : "")
                    + $"Größe: {u.Size:N0} Byte\nSHA-256: {u.Sha256}\n\n"
                    + $"Prüfung bestanden: {u.Zips:N0} PLZ (aktiv: {u.Before:N0})\n"
                    + $"Neu: {u.Added:N0}   Entfallen: {u.Removed:N0}   Ort geändert: {u.Changed:N0}"
                    + (u.Unusual > 0 ? $"\nUngewöhnliche Ortsnamen (auch im Original vorhanden): {u.Unusual}" : "")
                    + (u.Suspicious ? "\n\nWARNUNG: Ungewöhnlich viele Abweichungen gegenüber der aktiven Liste. Nur übernehmen, wenn du der Quelle sicher vertraust." : "")
                    + "\n\nDiese Daten übernehmen?";
                if (MessageBoxWpf.Show(msg, "PLZ-Verzeichnis – Prüfergebnis", MessageBoxButton.YesNo,
                        u.Suspicious ? MessageBoxImage.Warning : MessageBoxImage.Question, u.Suspicious ? MessageBoxResult.No : MessageBoxResult.Yes) != MessageBoxResult.Yes)
                {
                    Dateiumbenenner.Engine.PlzDirectory.Discard(u);
                    return;
                }
                Dateiumbenenner.Engine.PlzDirectory.Activate(u);
                await ReloadPlzAndRefreshAsync();
                MessageBoxWpf.Show($"PLZ-Verzeichnis aktualisiert: {plz.Count:N0} PLZ (vorher {before:N0}).\n{plz.Source}", "PLZ-Verzeichnis", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                if (update != null) Dateiumbenenner.Engine.PlzDirectory.Discard(update);
                MessageBoxWpf.Show("Aktualisierung abgebrochen:\n" + ex.Message + "\n\nDie bisherige PLZ-Liste bleibt aktiv.", "PLZ-Verzeichnis", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                Cursor = old;
                UpdateStandardStatus();
            }
        }

        private async void MenuPlzRestore_Click(object sender, RoutedEventArgs e)
        {
            var plz = Dateiumbenenner.Engine.DocumentEngine.Plz;
            var date = Dateiumbenenner.Engine.PlzDirectory.BundledDate;
            if (MessageBoxWpf.Show($"Aktiv: {plz.Count:N0} PLZ – {plz.Source}\n\nAuf die mitgelieferte PLZ-Liste (GeoNames{(date is { } d ? $", Stand {d:dd.MM.yyyy}" : "")}) zurückstellen?\n"
                + "Eine heruntergeladene Aktualisierung (Daten\\DE.txt) wird entfernt; eigene Ergänzungen in plz.csv bleiben erhalten.",
                "PLZ-Verzeichnis", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            try
            {
                Dateiumbenenner.Engine.PlzDirectory.RestoreBundled();
                await ReloadPlzAndRefreshAsync();
                MessageBoxWpf.Show($"Aktiv: {plz.Count:N0} PLZ – {plz.Source}", "PLZ-Verzeichnis", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBoxWpf.Show("Zurückstellen fehlgeschlagen:\n" + ex.Message, "PLZ-Verzeichnis", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally { UpdateStandardStatus(); }
        }

        private async Task ReloadPlzAndRefreshAsync()
        {
            await Task.Run(Dateiumbenenner.Engine.DocumentEngine.ReloadPlz);
            foreach (var m in _items.Where(i => !i.AddressManuallySet)) m.AddressLoaded = false;
            if (_currentMeta != null) ShowDetails(_currentMeta);
        }

        private async void MenuOcrTest_Click(object sender, RoutedEventArgs e)
        {
            var folder = TxtFolder.Text;
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) { MessageBoxWpf.Show("Bitte zuerst einen Ordner wählen.", "OCR-/Parallel-Test"); return; }
            bool aMode = AMode;
            var exts = aMode ? new[] { ".pdf" } : new[] { ".pdf", ".txt", ".csv" };
            var files = Directory.EnumerateFiles(folder).Where(f => exts.Contains(Path.GetExtension(f).ToLowerInvariant())).ToList();

            List<DocumentMetadata> Create() => files.Select(f =>
            {
                var m = new DocumentMetadata { FilePath = f };
                if (aMode) { var txt = Path.ChangeExtension(f, ".txt"); if (File.Exists(txt)) m.TextSourcePath = txt; }
                return m;
            }).ToList();

            async Task<(List<DocumentMetadata> metas, long ms)> Run(int dop)
            {
                var metas = Create();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                await Parallel.ForEachAsync(metas, new ParallelOptions { MaxDegreeOfParallelism = dop }, async (m, _) => await LoadMetadataAsync(m));
                return (metas, sw.ElapsedMilliseconds);
            }

            try
            {
                IsEnabled = false;
                TxtStatus.Text = "Vergleichstest läuft...";
                await Run(1); // Aufwärmen (OCR-Konfiguration, Regex-JIT)
                var (seq, tSeq) = await Run(1);
                var (par, tPar) = await Run(Math.Max(2, Environment.ProcessorCount - 1));

                var diffs = new List<string>();
                for (int i = 0; i < seq.Count; i++)
                {
                    var a = seq[i]; var b = par[i];
                    if (a.FullText != b.FullText) diffs.Add($"{a.FileName}: Text unterschiedlich");
                    if (a.InvoiceNumber != b.InvoiceNumber) diffs.Add($"{a.FileName}: Rechnungsnr. {a.InvoiceNumber} ≠ {b.InvoiceNumber}");
                    if (a.DocumentNumber != b.DocumentNumber) diffs.Add($"{a.FileName}: Belegnr. {a.DocumentNumber} ≠ {b.DocumentNumber}");
                    if (a.Date != b.Date) diffs.Add($"{a.FileName}: Datum {a.Date} ≠ {b.Date}");
                    if (!a.NumberCandidates.SequenceEqual(b.NumberCandidates)) diffs.Add($"{a.FileName}: Nummernkandidaten unterschiedlich");
                    if (!a.DateCandidates.SequenceEqual(b.DateCandidates)) diffs.Add($"{a.FileName}: Datumskandidaten unterschiedlich");
                }
                var errors = par.Count(m => m.FullText?.StartsWith("Fehler beim Lesen") == true);
                TxtStatus.Text = "Vergleichstest beendet.";
                MessageBoxWpf.Show(
                    $"Modus: {(aMode ? "A-Modus" : "Normal")}, OCR-Korrektur: {(_enableOcrCorrection ? "an" : "aus")}\n" +
                    $"Dateien: {files.Count}, Lesefehler: {errors}\n" +
                    $"Sequenziell: {tSeq} ms\nParallel: {tPar} ms" + (tPar > 0 ? $" (Faktor {(double)tSeq / tPar:0.0})" : "") + "\n\n" +
                    (diffs.Count == 0 ? "✔ Keine Unterschiede – parallele Verarbeitung liefert identische Ergebnisse." : $"✘ {diffs.Count} Unterschiede:\n" + string.Join("\n", diffs.Take(30))),
                    "OCR-/Parallel-Test");
            }
            catch (Exception ex) { MessageBoxWpf.Show($"Test fehlgeschlagen: {ex.Message}", "OCR-/Parallel-Test"); }
            finally { IsEnabled = true; }
        }

        private void AddUniqueWithPosition(List<(string,int)> list,String value,int pos){ if(!string.IsNullOrWhiteSpace(value) && !list.Any(x=>x.Item1==value)) list.Add((value,pos)); }

        private async Task UpdatePdfPreviewAsync(DocumentMetadata meta){ PdfWebView.Visibility=Visibility.Collapsed; ImgPdfPreview.Visibility=Visibility.Collapsed; PdfTextScroll.Visibility=Visibility.Collapsed; if(meta==null||string.IsNullOrEmpty(meta.FilePath)) return; var ext=Path.GetExtension(meta.FilePath).ToLowerInvariant(); if(ext!=".pdf"){ TxtPdfFallback.Text=meta.FullText??"(kein Text verfügbar)"; PdfTextScroll.Visibility=Visibility.Visible; return; } var mode=(CmbPdfPreviewMode.SelectedItem as ComboBoxItem)?.Content?.ToString()??"Auto"; try{ switch(mode){ case "WebView2": await ShowPdfInWebView2(meta.FilePath); break; case "Text": ShowPdfAsText(meta); break; default: if(await TryShowPdfInWebView2(meta.FilePath)) break; ShowPdfAsText(meta); break; } } catch(Exception ex){ TxtPdfFallback.Text=$"Fehler bei der PDF-Vorschau:\n{ex.Message}\n\nText-Vorschau:\n{meta.FullText}"; PdfTextScroll.Visibility=Visibility.Visible; } }
        private async Task<bool> TryShowPdfInWebView2(string filePath){ try{ await ShowPdfInWebView2(filePath); return true;} catch{ return false; } }
        private async Task ShowPdfInWebView2(string filePath){ await PdfWebView.EnsureCoreWebView2Async(); PdfWebView.Source=new Uri(filePath); PdfWebView.Visibility=Visibility.Visible; }
        private void ShowPdfAsText(DocumentMetadata meta){ TxtPdfFallback.Text=BuildRecognizedHeader(meta)+(meta.FullText??"(kein Text verfügbar)"); PdfTextScroll.Visibility=Visibility.Visible; }

        /// <summary>Erkannte Felder der Engine als Kopf über dem OCR-Text (Adresse einzeilig).</summary>
        private string BuildRecognizedHeader(DocumentMetadata meta)
        {
            if (string.IsNullOrEmpty(meta.FullText)) return "";
            try
            {
                var a = Engine.ExtractAddress(meta.FullText, meta.CompanyName, meta.FilePath);
                var addr = string.Join(", ", new[] { a.Street.Value, $"{a.Zip.Value} {a.City.Value}".Trim() }.Where(s => !string.IsNullOrWhiteSpace(s)));
                var sb = new StringBuilder();
                sb.AppendLine("=== Erkannt ===");
                sb.AppendLine($"Firma:   {meta.CompanyName}");
                sb.AppendLine($"Adresse: {addr}");
                sb.AppendLine($"Typ:     {meta.DocumentType}");
                sb.AppendLine($"Nummer:  {meta.SelectedNumber ?? meta.InvoiceNumber}");
                sb.AppendLine($"Datum:   {meta.SelectedDate ?? meta.Date}");
                sb.AppendLine("===============");
                sb.AppendLine();
                return sb.ToString();
            }
            catch { return ""; }
        }

        private void PopulateSelectors(DocumentMetadata meta){ _isPopulatingSelectors=true; try{ if(!string.IsNullOrEmpty(meta.DocumentType)){ var match=(CmbDocType.ItemsSource as List<string>)?.FirstOrDefault(i=>i.Equals(meta.DocumentType.Trim(),StringComparison.OrdinalIgnoreCase)); if(match!=null) CmbDocType.SelectedItem=match; else CmbDocType.SelectedIndex=-1; if(match==null || meta.DocTypeManuallySet) CmbDocType.Text=meta.DocumentType; } else { CmbDocType.SelectedIndex=-1; CmbDocType.Text=string.Empty; } } finally { _isPopulatingSelectors=false; } PopulateNumberDate(meta); }

        /// <summary>Nummern- und Datumsauswahl des Belegs anzeigen (auch einzeln, z. B. nach ?bernahme gelernter Regeln).</summary>
        private void PopulateNumberDate(DocumentMetadata meta){ var was=_isPopulatingSelectors; _isPopulatingSelectors=true; try{
            CmbNumberCandidates.ItemsSource=null; CmbNumberCandidates.Text=""; CmbNumberCandidates.SelectedIndex=-1; var validNumbers=meta.NumberCandidates.Where(n=>IsValidNumber(n)).ToList(); if(_globalMinDigits>0){ var enough=validNumbers.Where(n=>CountDigits(n)>=_globalMinDigits).OrderBy(n=>CountDigits(n)).ToList(); var few=validNumbers.Where(n=>CountDigits(n)<_globalMinDigits).OrderBy(n=>CountDigits(n)).ToList(); validNumbers=enough.Concat(few).ToList(); } else validNumbers=validNumbers.OrderBy(n=>CountDigits(n)).ToList(); if(meta.NumberManuallySet && !string.IsNullOrEmpty(meta.SelectedNumber) && !validNumbers.Contains(meta.SelectedNumber)) validNumbers.Insert(0,meta.SelectedNumber); else if(!meta.NumberManuallySet && !string.IsNullOrEmpty(meta.SelectedNumber) && IsValidNumber(meta.SelectedNumber) && !validNumbers.Contains(meta.SelectedNumber)) validNumbers.Insert(0,meta.SelectedNumber); CmbNumberCandidates.ItemsSource=validNumbers; if(meta.NumberManuallySet && !string.IsNullOrEmpty(meta.SelectedNumber)){ CmbNumberCandidates.Text=meta.SelectedNumber; CmbNumberCandidates.SelectedItem=meta.SelectedNumber; } else if(!string.IsNullOrEmpty(meta.SelectedNumber) && validNumbers.Contains(meta.SelectedNumber) && IsValidNumber(meta.SelectedNumber)){ CmbNumberCandidates.SelectedItem=meta.SelectedNumber; CmbNumberCandidates.Text=meta.SelectedNumber; } else { var appropriate=GetAppropriateNumber(meta,meta.DocumentType); if(!string.IsNullOrEmpty(appropriate) && !validNumbers.Contains(appropriate)){ validNumbers.Insert(0,appropriate); CmbNumberCandidates.ItemsSource=null; CmbNumberCandidates.ItemsSource=validNumbers; } if(!string.IsNullOrEmpty(appropriate)){ CmbNumberCandidates.SelectedItem=appropriate; CmbNumberCandidates.Text=appropriate; if(!meta.NumberManuallySet) meta.SelectedNumber=appropriate; } else if(validNumbers.Any()){ var first=validNumbers.First(); CmbNumberCandidates.SelectedItem=first; CmbNumberCandidates.Text=first; if(!meta.NumberManuallySet) meta.SelectedNumber=first; } else { if(!meta.NumberManuallySet) meta.SelectedNumber=null; } }
            if(!meta.NumberManuallySet) UpdateMetadataFromSelections(meta);
            CmbDateCandidates.ItemsSource=null; CmbDateCandidates.Text=""; CmbDateCandidates.SelectedIndex=-1; CmbDateCandidates.ItemsSource=meta.DateCandidates; if(meta.DateManuallySet && !string.IsNullOrEmpty(meta.SelectedDate) && meta.DateCandidates.Contains(meta.SelectedDate)){ CmbDateCandidates.SelectedItem=meta.SelectedDate; CmbDateCandidates.Text=meta.SelectedDate; } else if(!string.IsNullOrEmpty(meta.SelectedDate) && meta.DateCandidates.Contains(meta.SelectedDate)){ CmbDateCandidates.SelectedItem=meta.SelectedDate; CmbDateCandidates.Text=meta.SelectedDate; } else if(meta.DateCandidates.Any()){ CmbDateCandidates.SelectedIndex=0; if(!meta.DateManuallySet) meta.SelectedDate=meta.DateCandidates.First(); } else { if(!meta.DateManuallySet) meta.SelectedDate=null; } } finally { _isPopulatingSelectors=was; } }

        private void UpdateMetadataFromSelections(DocumentMetadata meta){ if(meta==null||_isPopulatingSelectors) return; if(CmbDocType!=null && !string.IsNullOrWhiteSpace(CmbDocType.Text)) meta.DocumentType=CmbDocType.Text.Trim(); if(CmbNumberCandidates!=null && !string.IsNullOrWhiteSpace(CmbNumberCandidates.Text)) meta.SelectedNumber=CmbNumberCandidates.Text.Trim(); if(CmbDateCandidates!=null && !string.IsNullOrWhiteSpace(CmbDateCandidates.Text)){ var dt=CmbDateCandidates.Text.Trim(); meta.SelectedDate=dt; meta.Date=dt; } }

        private void SaveCurrentSettings(DocumentMetadata meta){ if(meta==null||!meta.Loaded) return; if(_currentMeta!=meta || _isPopulatingSelectors) return; var docType=CmbDocType.Text; if(!string.IsNullOrEmpty(docType)){ meta.DocumentType=docType; meta.DocTypeManuallySet=true; } var number=CmbNumberCandidates.Text; if(!string.IsNullOrWhiteSpace(number) && !string.Equals(number.Trim(), meta.SelectedNumber?.Trim(), StringComparison.Ordinal)){ meta.SelectedNumber=number; meta.NumberManuallySet=true; } var date=CmbDateCandidates.Text; if(!string.IsNullOrWhiteSpace(date) && !string.Equals(date.Trim(), meta.SelectedDate?.Trim(), StringComparison.Ordinal)){ meta.SelectedDate=date; meta.Date=date; meta.DateManuallySet=true; } meta.SavedDelimiter=TxtDelimiter.Text; meta.RemoveDuplicates=ChkRemoveDuplicates.IsChecked==true; meta.ReplaceSegments=ChkReplaceSegments.IsChecked==true; meta.RemoveAttachment=ChkRemoveAttachment.IsChecked==true; if(int.TryParse(TxtSegmentIndex.Text,out int segIdx)) meta.SavedSegmentIndex=segIdx; meta.UseDelimiterPosition=ChkUseDelimiterPosition.IsChecked==true; meta.InsertPositions.Clear(); if(CmbNumberPosition!=null && CmbNumberPosition.SelectedIndex<3) meta.InsertPositions.Add(new InsertPosition{Position=CmbNumberPosition.SelectedIndex+1,ValueType="Nummer (Auswahl)",Enabled=true}); if(CmbDatePosition!=null && CmbDatePosition.SelectedIndex<3) meta.InsertPositions.Add(new InsertPosition{Position=CmbDatePosition.SelectedIndex+1,ValueType="Datum",Enabled=true}); if(CmbDocTypePosition!=null && CmbDocTypePosition.SelectedIndex<3) meta.InsertPositions.Add(new InsertPosition{Position=CmbDocTypePosition.SelectedIndex+1,ValueType="Dokumenttyp",Enabled=true}); if(CmbAddressPosition!=null && CmbAddressPosition.SelectedIndex<3) meta.InsertPositions.Add(new InsertPosition{Position=CmbAddressPosition.SelectedIndex+1,ValueType=AddressTypePrefix+SelectedAddressPart,Enabled=true}); if(TxtEditableNewName!=null && TxtEditableNewName.IsFocused) meta.ManualFileName=TxtEditableNewName.Text.Trim(); meta.LayoutSet=true; RefreshNewNamePreview(meta); ScheduleStoreSave(meta); }

        private void RefreshNewNamePreview(DocumentMetadata meta){ if(meta==null) return; ChainedUpdates(()=>{ var raw=GenerateNewFileName(meta); var unique=EnsureUniquePreviewName(meta,raw); meta.ProposedNewName=unique; if(TxtEditableNewName!=null){ _isSettingPreviewName=true; try{ var sel=TxtEditableNewName.SelectionStart; TxtEditableNewName.Text=unique; TxtEditableNewName.SelectionStart=Math.Min(sel,unique.Length); } finally { _isSettingPreviewName=false; } } }); }
        private void ChainedUpdates(Action act){ _updateTimer?.Stop(); _updateTimer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(100)}; _updateTimer.Tick += (s,e)=>{ _updateTimer.Stop(); act(); }; _updateTimer.Start(); }

        private string GenerateNewFileName(DocumentMetadata meta){ if(!string.IsNullOrEmpty(meta.ManualFileName) && TxtEditableNewName!=null && TxtEditableNewName.IsFocused){ var sanitized=meta.ManualFileName; foreach(var c in InvalidFileNameChars) sanitized=sanitized.Replace(c,'-'); if(meta.RemoveAttachment) sanitized=RemoveTrailingAttachment(sanitized, meta.SavedDelimiter??"_"); return sanitized; } var delimiter=string.IsNullOrEmpty(meta.SavedDelimiter)?"_":meta.SavedDelimiter; var useDelPos=meta.UseDelimiterPosition; var replace=meta.ReplaceSegments; var segmentIndex=meta.SavedSegmentIndex??2; var original=Path.GetFileNameWithoutExtension(meta.FileName); if(meta.RemoveAttachment) original=RemoveTrailingAttachment(original, delimiter); var enabled=meta.InsertPositions.Where(p=>p.Enabled).OrderBy(p=>p.Position).ToList(); if(enabled.Count==0) return ApplyCompanyPrefix(meta,original,delimiter); string result; if(replace){ var segments=original.Split(new[]{delimiter},StringSplitOptions.None).ToList(); var values=new List<string>(); foreach(var ins in enabled){ var v=GetValueForType(meta,ins.ValueType); if(!string.IsNullOrEmpty(v)) values.Add(v); } if(values.Count==0) result=original; else if(useDelPos){ int delimCount=0; int startPos=0; for(int i=0;i<original.Length;i++){ if(original[i].ToString()==delimiter){ delimCount++; if(delimCount==segmentIndex){ startPos=i+1; break; } } } var prefix=original.Substring(0,startPos); var suffix=startPos<original.Length?original.Substring(startPos):""; var suffixSegs=suffix.Split(new[]{delimiter},StringSplitOptions.None).ToList(); for(int i=0;i<values.Count && i<suffixSegs.Count;i++) suffixSegs[i]=values[i]; if(values.Count>suffixSegs.Count) for(int i=suffixSegs.Count;i<values.Count;i++) suffixSegs.Add(values[i]); result=prefix+string.Join(delimiter,suffixSegs.Where(s=>!string.IsNullOrEmpty(s))); } else { int startIdx=segmentIndex<0?0:segmentIndex; for(int i=0;i<values.Count;i++){ int target=startIdx+i; if(target<segments.Count) segments[target]=values[i]; else segments.Add(values[i]); } result=string.Join(delimiter,segments.Where(s=>!string.IsNullOrEmpty(s))); } }
        else if(useDelPos){ var values=new List<string>(); foreach(var ins in enabled){ var v=GetValueForType(meta,ins.ValueType); if(!string.IsNullOrEmpty(v)) values.Add(v); } if(values.Count==0) result=original; else { int delimCount=0; int insertPos=-1; for(int i=0;i<original.Length;i++){ if(original[i].ToString()==delimiter){ delimCount++; if(delimCount==segmentIndex){ insertPos=i+1; break; } } } if(insertPos==-1) result=original+delimiter+string.Join(delimiter,values); else { var prefix=original.Substring(0,insertPos); var suffix=original.Substring(insertPos); var inserted=string.Join(delimiter,values); result=prefix+inserted+(string.IsNullOrEmpty(suffix)?"":delimiter+suffix); } } }
        else { var segments=original.Split(new[]{delimiter},StringSplitOptions.None).ToList(); foreach(var ins in enabled.OrderByDescending(p=>p.Position)){ var v=GetValueForType(meta,ins.ValueType); if(!string.IsNullOrEmpty(v)){ int pos=ins.Position; if(pos<0) pos=0; if(pos>segments.Count) pos=segments.Count; segments.Insert(pos,v); } } result=string.Join(delimiter,segments.Where(s=>!string.IsNullOrEmpty(s))); }
        if(meta.RemoveAttachment) result=RemoveTrailingAttachment(result, delimiter); result=ApplyCompanyPrefix(meta,result,delimiter); foreach(var c in InvalidFileNameChars) result=result.Replace(c,'-'); return result; }
        private string ApplyCompanyPrefix(DocumentMetadata meta,string name,string delimiter)
        {
            // 1) Manuell eingegebener neuer Präfix (nur für aktuell angezeigtes Dokument) -> vorne anhängen
            var manualPrefix=(meta==_currentMeta && TxtMainNewPrefix!=null) ? TxtMainNewPrefix.Text?.Trim(delimiter.ToCharArray()).Trim() : null;
            if(!string.IsNullOrEmpty(manualPrefix))
            {
                if(name.StartsWith(manualPrefix+delimiter,StringComparison.OrdinalIgnoreCase)) return name;
                return manualPrefix+delimiter+name.TrimStart(delimiter.ToCharArray());
            }

            // 2) Kein neuer Präfix -> Firmenname ersetzt die ersten Segmente (Standard: 1, sonst "Entf.")
            var company=meta.CompanyName?.Trim();
            if(string.IsNullOrEmpty(company)||company.Equals("Stop",StringComparison.OrdinalIgnoreCase)) return name;
            if(name.StartsWith(company+delimiter,StringComparison.OrdinalIgnoreCase)||name.Equals(company,StringComparison.OrdinalIgnoreCase)) return name;

            int replaceCount=0;
            if(TxtPrefixRemove!=null && int.TryParse(TxtPrefixRemove.Text,out var rc) && rc>0) replaceCount=rc;

            var segs=name.Split(new[]{delimiter},StringSplitOptions.None).ToList();
            if(replaceCount==0 || segs.Count<=1) return company+delimiter+name.TrimStart(delimiter.ToCharArray());
            segs.RemoveRange(0,Math.Min(replaceCount,segs.Count-1));
            segs.Insert(0,company);
            return string.Join(delimiter,segs.Where(s=>!string.IsNullOrEmpty(s)));
        }
        private string RemoveTrailingAttachment(string baseName,string delimiter)
        {
            if(string.IsNullOrEmpty(baseName)) return baseName;
            var segs=baseName.Split(new[]{delimiter},StringSplitOptions.None).ToList();
            if(segs.Count==0) return baseName;
            var last=segs[^1];
            if(last.Contains("_ocred",StringComparison.OrdinalIgnoreCase) || Regex.IsMatch(last,@"^(WA\d{2,}(_ocr(ed)?)?)$",RegexOptions.IgnoreCase))
            {
                segs.RemoveAt(segs.Count-1);
                return string.Join(delimiter,segs);
            }
            return baseName;
        }
        private string? GetValueForType(DocumentMetadata meta,string type)=>type switch
        {
            "Rechnungsnummer"=>meta.InvoiceNumber,
            "Belegnummer"=>meta.DocumentNumber,
            "Nummer (Auswahl)"=>meta.SelectedNumber,
            "Datum"=>meta.SelectedDate??meta.Date,
            "Seiten"=>meta.PageCount.ToString(),
            "Dokumenttyp"=>meta.DocumentType,
            _ when type.StartsWith(AddressTypePrefix, StringComparison.Ordinal)=>GetAddressValue(meta, type.Substring(AddressTypePrefix.Length)),
            _=>null
        };

        private const string AddressTypePrefix = "Adresse:";

        /// <summary>Adresse aus der Engine-Bewertung (nur Kandidaten mit Score >= 0, kein Profil/DB-Vorrang).</summary>
        private void EnsureAddress(DocumentMetadata meta)
        {
            if (meta.AddressLoaded || string.IsNullOrEmpty(meta.FullText)) return;
            try
            {
                var a = Engine.ExtractAddress(meta.FullText, meta.CompanyName, meta.FilePath);
                meta.Street = a.Street.Value;
                meta.Zip = a.Zip.Value;
                meta.City = a.City.Value;
            }
            catch { }
            meta.AddressLoaded = true;
            if (string.IsNullOrWhiteSpace(meta.Street) && string.IsNullOrWhiteSpace(meta.Zip) && string.IsNullOrWhiteSpace(meta.City))
                _addressTasks[meta] = FillAddressFromProvidersAsync(meta);
            else
                LogAddress(meta, $"Dokumentadresse verwendet, keine DB-Abfrage: {meta.Street}, {meta.Zip} {meta.City}");
        }

        private readonly Dictionary<DocumentMetadata, Task> _addressTasks = new();

        /// <summary>Adresse ermitteln und auf eine laufende DB-Abfrage warten.</summary>
        private async Task EnsureAddressAsync(DocumentMetadata meta)
        {
            EnsureAddress(meta);
            if (_addressTasks.TryGetValue(meta, out var t))
            {
                try { await t; } catch { }
                _addressTasks.Remove(meta);
            }
        }

        /// <summary>Diagnose: Statuszeile (aktuelles Dokument) + Daten/debug/db_address.log.</summary>
        private void LogAddress(DocumentMetadata meta, string msg)
        {
            var line = $"{DateTime.Now:HH:mm:ss} [{meta.FileName}] {msg}";
            System.Diagnostics.Debug.WriteLine("DB-Adresse: " + line);
            if (!Dateiumbenenner.Engine.DocumentEngine.DebugEnabled) return;
            try
            {
                var dir = Path.Combine(Dateiumbenenner.Plugins.PluginManager.BaseDirectory, "debug");
                Directory.CreateDirectory(dir);
                Dateiumbenenner.Engine.SecureStorage.AppendAllText(Path.Combine(dir, "db_address.log"), line + Environment.NewLine);
            }
            catch { }
            if (meta == _currentMeta && TxtStatus != null) TxtStatus.Text = "Adresse: " + msg;
        }

        private readonly List<Dateiumbenenner.Plugins.IAddressProvider> _addressProviders = new();

        public void RegisterAddressProvider(Dateiumbenenner.Plugins.IAddressProvider provider)
        {
            if (provider != null && !_addressProviders.Contains(provider)) _addressProviders.Add(provider);
            if (provider != null) RegisterCompanySource(provider);
        }

        /// <summary>DB-/Plugin-Adresse nur als Fallback: füllt ausschließlich leere Felder.</summary>
        private async Task FillAddressFromProvidersAsync(DocumentMetadata meta)
        {
            var company = meta.CompanyName?.Trim();
            if (_addressProviders.Count == 0) { LogAddress(meta, "Kein Adress-Provider angemeldet (INI-Plugin aktiviert und neu gebaut?)."); return; }
            if (string.IsNullOrEmpty(company)) { LogAddress(meta, "Keine Firma erkannt – keine DB-Suche."); return; }
            var found = false;
            foreach (var p in _addressProviders.ToList())
            {
                try
                {
                    var r = await p.LookupAddressAsync(company);
                    if (meta.AddressManuallySet) return;
                    if (r == null) { LogAddress(meta, $"{p.GetType().Name}: kein Treffer für „{company}“ (DB aus, Adressspalten leer oder Firma nicht gefunden)."); continue; }
                    if (string.IsNullOrWhiteSpace(meta.Street)) meta.Street = r.Street;
                    if (string.IsNullOrWhiteSpace(meta.Zip)) meta.Zip = r.Zip;
                    if (string.IsNullOrWhiteSpace(meta.City)) meta.City = r.City;
                    LogAddress(meta, $"DB-Treffer für „{company}“: {r.Street}, {r.Zip} {r.City}");
                    found = true;
                    break;
                }
                catch (Exception ex) { LogAddress(meta, $"{p.GetType().Name}: Fehler – {ex.Message}"); }
            }
            if (!found) return;
            if (meta == _currentMeta && TxtDetailAddress != null)
            {
                var adr = string.Join(", ", new[] { meta.Street, $"{meta.Zip} {meta.City}".Trim() }
                    .Where(s => !string.IsNullOrWhiteSpace(s)));
                TxtDetailAddress.Text = string.IsNullOrEmpty(adr) ? "(nicht erkannt)" : adr + " (DB)";
            }
            RefreshNewNamePreview(meta);
        }

        private string? GetAddressValue(DocumentMetadata meta, string part)
        {
            EnsureAddress(meta);
            string? Join(params string?[] p) { var s = string.Join(" ", p.Where(x => !string.IsNullOrWhiteSpace(x))).Trim(); return s.Length == 0 ? null : s; }
            return part switch
            {
                "Straße" => meta.Street,
                "PLZ" => meta.Zip,
                "Ort" => meta.City,
                "PLZ Ort" => Join(meta.Zip, meta.City),
                "Komplett" => Join(meta.Street, meta.Zip, meta.City),
                _ => null
            };
        }

        private string SelectedAddressPart => (CmbAddressPart?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "PLZ Ort";

        private string? GetFirstNumberAfterDocType(string fullText,string docType,int targetDigits){ if(string.IsNullOrWhiteSpace(fullText)||string.IsNullOrWhiteSpace(docType)) return null; var match=Regex.Match(fullText,"\\b"+Regex.Escape(docType)+"\\b",RegexOptions.IgnoreCase); if(!match.Success) return null; int start=match.Index+match.Length; if(start>=fullText.Length) return null; var slice=fullText.Substring(start); List<string> candidates=new(); foreach(Match m in _environmentNumberRegex.Matches(slice)){ var c=m.Groups[1].Value.Trim(); if(IsValidNumber(c)) candidates.Add(c); } if(!candidates.Any()) return null; if(targetDigits>=4){ var exact=candidates.FirstOrDefault(c=>CountDigits(c)==targetDigits); if(!string.IsNullOrEmpty(exact)) return exact; var ge=candidates.FirstOrDefault(c=>CountDigits(c)>=targetDigits); if(!string.IsNullOrEmpty(ge)) return ge; } return candidates.First(); }

        private string? GetAppropriateNumber(DocumentMetadata meta,string? docType){ var validCandidates=meta.NumberCandidates.Where(IsValidNumber).ToList(); if(!meta.NumberManuallySet && !string.IsNullOrEmpty(docType) && !string.IsNullOrEmpty(meta.FullText)){ var after=GetFirstNumberAfterDocType(meta.FullText!,docType!,_globalMinDigits); if(!string.IsNullOrEmpty(after) && IsValidNumber(after)){ if(!meta.NumberCandidates.Contains(after)) meta.NumberCandidates.Insert(0,after); return after; } } if(!string.IsNullOrEmpty(docType) && !string.IsNullOrEmpty(meta.FullText) && !meta.NumberManuallySet){ int minDigitsForEnv=Math.Max(1,_globalMinDigits); var env=FindNumberNearDocType(meta.FullText!,docType!,minDigitsForEnv); if(!string.IsNullOrEmpty(env) && IsValidNumber(env)){ if(!meta.NumberCandidates.Contains(env)) meta.NumberCandidates.Insert(0,env); validCandidates=meta.NumberCandidates.Where(IsValidNumber).ToList(); if(CountDigits(env)>=_globalMinDigits || _globalMinDigits<=1) return env; } } if(!validCandidates.Any()) return null; if(_globalMinDigits>0){ var exact=validCandidates.FirstOrDefault(n=>CountDigits(n)==_globalMinDigits); if(exact!=null) return exact; var enough=validCandidates.Where(n=>CountDigits(n)>=_globalMinDigits).OrderBy(n=>CountDigits(n)).ToList(); if(enough.Any()) return enough.First(); for(int d=_globalMinDigits-1; d>=4; d--){ var fb=validCandidates.FirstOrDefault(n=>CountDigits(n)==d); if(fb!=null) return fb; } } return validCandidates.FirstOrDefault(); }

        private GridViewColumnHeader? _lastHeaderClicked; private ListSortDirection _lastDirection=ListSortDirection.Ascending;
        private void GridHeader_Click(object sender,RoutedEventArgs e){ if(e.OriginalSource is GridViewColumnHeader header && header.Tag is string sortProp && !string.IsNullOrEmpty(sortProp)){ var dir=ListSortDirection.Ascending; if(header==_lastHeaderClicked && _lastDirection==ListSortDirection.Ascending) dir=ListSortDirection.Descending; if(sortProp=="FileName" && _useNaturalSort){ var ordered=dir==ListSortDirection.Ascending? _items.OrderBy(m=>m.FileName,_naturalComparer).ToList(): _items.OrderByDescending(m=>m.FileName,_naturalComparer).ToList(); LvFiles.ItemsSource=ordered; } else { var view=System.Windows.Data.CollectionViewSource.GetDefaultView(LvFiles.ItemsSource); if(view!=null){ view.SortDescriptions.Clear(); view.SortDescriptions.Add(new SortDescription(sortProp,dir)); view.Refresh(); } } _lastHeaderClicked=header; _lastDirection=dir; } }
        private void UpdateStandardStatus(){ if(TxtStatus==null) return; var total=_items.Count; var sel=LvFiles.SelectedItems.Count; var current=_currentMeta?.FileName??""; if(total==0) TxtStatus.Text="Keine Dokumente geladen."; else if(sel==0) TxtStatus.Text=$"{total} Dokument(e) geladen."; else if(sel==1 && !string.IsNullOrEmpty(current)) TxtStatus.Text=$"{total} Dokument(e) | 1 ausgew�hlt: {current}"; else TxtStatus.Text=$"{total} Dokument(e) | {sel} ausgew�hlt"; }
        private string EnsureUniquePreviewName(DocumentMetadata meta,string candidate){ if(meta==null||string.IsNullOrWhiteSpace(candidate)) return candidate; var planned=new HashSet<string>(StringComparer.OrdinalIgnoreCase); foreach(var item in _items){ if(item==meta||!item.Loaded) continue; var baseName=!string.IsNullOrEmpty(item.ManualFileName)? item.ManualFileName : GenerateNewFileName(item); if(!string.IsNullOrWhiteSpace(baseName)) planned.Add(baseName); } var dir=Path.GetDirectoryName(meta.FilePath)??string.Empty; var ext=Path.GetExtension(meta.FilePath)??string.Empty; if(!planned.Contains(candidate) && !File.Exists(Path.Combine(dir,candidate+ext))) return candidate; int n=1; while(true){ var attempt=$"{candidate} ({n})"; if(!planned.Contains(attempt) && !File.Exists(Path.Combine(dir,attempt+ext))) return attempt; n++; } }
        private void RestoreSettings(DocumentMetadata meta){ _isPopulatingSelectors=true; try{ TxtDelimiter.Text=meta.SavedDelimiter??"_"; if(meta.SavedSegmentIndex.HasValue) TxtSegmentIndex.Text=meta.SavedSegmentIndex.Value.ToString(); ChkUseDelimiterPosition.IsChecked=meta.UseDelimiterPosition; ChkRemoveDuplicates.IsChecked=meta.RemoveDuplicates; ChkReplaceSegments.IsChecked=meta.ReplaceSegments; ChkRemoveAttachment.IsChecked=meta.RemoveAttachment; TxtMinDigits.Text=_globalMinDigits.ToString(); if(TxtDocTypeNumberSteps!=null && string.IsNullOrEmpty(TxtDocTypeNumberSteps.Text)) TxtDocTypeNumberSteps.Text=_docTypeNumberSteps.ToString(); if(meta.InsertPositions==null||meta.InsertPositions.Count==0){ meta.InsertPositions=new List<InsertPosition>{ new InsertPosition{Position=1,ValueType="Nummer (Auswahl)",Enabled=true}, new InsertPosition{Position=2,ValueType="Datum",Enabled=true}, new InsertPosition{Position=3,ValueType="Dokumenttyp",Enabled=true} }; } CmbNumberPosition.SelectedIndex=3; CmbDatePosition.SelectedIndex=3; CmbDocTypePosition.SelectedIndex=3; if(CmbAddressPosition!=null) CmbAddressPosition.SelectedIndex=3; foreach(var pos in meta.InsertPositions.Where(p=>p.Enabled)){ if(pos.ValueType=="Nummer (Auswahl)") CmbNumberPosition.SelectedIndex=pos.Position-1; else if(pos.ValueType=="Datum") CmbDatePosition.SelectedIndex=pos.Position-1; else if(pos.ValueType=="Dokumenttyp") CmbDocTypePosition.SelectedIndex=pos.Position-1; else if(pos.ValueType.StartsWith(AddressTypePrefix) && CmbAddressPosition!=null && CmbAddressPart!=null){ CmbAddressPosition.SelectedIndex=pos.Position-1; var part=pos.ValueType.Substring(AddressTypePrefix.Length); foreach(ComboBoxItem it in CmbAddressPart.Items) if((it.Content as string)==part){ CmbAddressPart.SelectedItem=it; break; } } } if(!string.IsNullOrEmpty(meta.ManualFileName)) TxtEditableNewName.Text=meta.ManualFileName; } finally { _isPopulatingSelectors=false; } }
        private void ShowDetails(DocumentMetadata meta){ if(TxtLineWidthFile!=null) TxtLineWidthFile.Text=Dateiumbenenner.Engine.TextReflow.GetFileWidth(meta.FilePath)?.ToString()??""; TxtDetailInvoice.Text=meta.InvoiceNumber??"(keine)"; TxtDetailDocument.Text=meta.DocumentNumber??"(keine)"; TxtDetailDate.Text=meta.Date??"(kein)"; TxtDetailPages.Text=meta.PageCount.ToString(); EnsureAddress(meta); if(TxtDetailAddress!=null){ var adr=string.Join(", ", new[]{ meta.Street, $"{meta.Zip} {meta.City}".Trim() }.Where(s=>!string.IsNullOrWhiteSpace(s))); TxtDetailAddress.Text=string.IsNullOrEmpty(adr)?"(nicht erkannt)":adr; } var raw=string.IsNullOrEmpty(meta.FullText)?"(kein Text verfügbar)":meta.FullText; if(_enableOcrCorrection && !string.IsNullOrEmpty(meta.FullText)) raw=ApplyOcrHeuristics(raw); var display=raw.Length>4000? raw.Substring(0,4000)+"...": raw; TxtPreviewRich.Document.Blocks.Clear(); TxtPreviewRich.Document.Blocks.Add(new Paragraph(new Run(display))); ClearSearchHighlights(); }
        private string ApplyOcrHeuristics(string input)=> string.IsNullOrWhiteSpace(input)? input : OcrTextCorrector.Fix(input);
        private string? ExtractDateFromFileName(string fileName){ var match=Regex.Match(fileName,@"(\d{1,4})[-_.](\d{1,2})[-_.](\d{1,4})"); if(match.Success){ var year=match.Groups[1].Value; var month=match.Groups[2].Value; var day=match.Groups[3].Value; year=year.Length==2?"20"+year:year; month=month.PadLeft(2,'0'); day=day.PadLeft(2,'0'); return $"{day}.{month}.{year}"; } return null; }
        private string NormalizeDate(string dateStr){ if(string.IsNullOrWhiteSpace(dateStr)) return dateStr; dateStr=Regex.Replace(dateStr.Trim(),@"\s+"," "); if(DateTime.TryParseExact(dateStr,DateFormats,DeCulture,DateTimeStyles.AllowWhiteSpaces,out var dt)) return dt.ToString("dd.MM.yyyy",DeCulture); if(DateTime.TryParse(dateStr,DeCulture,DateTimeStyles.AllowWhiteSpaces,out dt)) return dt.ToString("dd.MM.yyyy",DeCulture); return dateStr; }
        private void ReapplyOcrAndReextract(DocumentMetadata meta) => ReapplyOcrAndReextract(meta, null, true);
        private void ReapplyOcrAndReextract(DocumentMetadata meta, string? prepared, bool refreshUi){ if(meta==null||!meta.Loaded) return; meta.OriginalFullText??=meta.FullText; var source=meta.OriginalFullText??meta.FullText; if(string.IsNullOrEmpty(source)) return; meta.FullText=prepared ?? (_enableOcrCorrection? ApplyOcrHeuristics(source): source); meta.NumberCandidates.Clear(); meta.DateCandidates.Clear(); if(!meta.NumberManuallySet){ meta.SelectedNumber=null; meta.InvoiceNumber=null; meta.DocumentNumber=null; } if(!meta.DateManuallySet){ meta.SelectedDate=null; meta.Date=null; } var manualType=meta.DocTypeManuallySet ? meta.DocumentType : null; meta.DocumentType=null; var flag=meta.DocTypeManuallySet; meta.DocTypeManuallySet=false; if(!string.IsNullOrEmpty(meta.FullText)) ExtractCandidates(meta); meta.DocTypeManuallySet=flag; if(flag && !string.IsNullOrWhiteSpace(manualType)) meta.DocumentType=manualType; AutoAssignNumbers(meta); if(!refreshUi) return; PopulateSelectors(meta); ShowDetails(meta); RefreshNewNamePreview(meta); LvFiles.Items.Refresh(); }
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

        private async Task RenameFileAsync(DocumentMetadata meta,HashSet<string>? already=null){ try{ string newName= meta==_currentMeta && !string.IsNullOrEmpty(TxtEditableNewName.Text.Trim())? TxtEditableNewName.Text.Trim() : GenerateNewFileName(meta); if(string.IsNullOrEmpty(newName)){ MessageBoxWpf.Show($"Neuer Dateiname für '{meta.FileName}' darf nicht leer sein."); return; } var dir=Path.GetDirectoryName(meta.FilePath)!; var ext=Path.GetExtension(meta.FilePath); var planned=new HashSet<string>(StringComparer.OrdinalIgnoreCase); if(already!=null) foreach(var r in already) planned.Add(r); foreach(var it in _items){ if(it==meta||!it.Loaded) continue; var plan=GenerateNewFileName(it); if(!string.IsNullOrEmpty(plan)) planned.Add(plan); } var newPath=Path.Combine(dir,newName+ext); if(File.Exists(newPath)||planned.Contains(newName)){ int c=1; var baseName=newName; do{ newName=$"{baseName} ({c})"; newPath=Path.Combine(dir,newName+ext); c++; } while(File.Exists(newPath)||planned.Contains(newName)); } File.Move(meta.FilePath,newPath); DocumentStore.Move(meta.FilePath,newPath); meta.FilePath=newPath; already?.Add(newName); TxtStatus.Text=$"Datei umbenannt: {newName}{ext}"; } catch(Exception ex){ MessageBoxWpf.Show($"Fehler beim Umbenennen: {ex.Message}"); } }
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
        private System.Windows.Threading.DispatcherTimer? _filterTimer; private int _filterVersion;
        private void TxtFilter_TextChanged(object sender,TextChangedEventArgs e){ _filterTimer??=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(300)}; _filterTimer.Tick-=FilterTimer_Tick; _filterTimer.Tick+=FilterTimer_Tick; _filterTimer.Stop(); _filterTimer.Start(); }
        private async void FilterTimer_Tick(object? sender,EventArgs e){ _filterTimer!.Stop(); var ft=TxtFilter.Text; int v=++_filterVersion; if(string.IsNullOrWhiteSpace(ft)){ _filterCts?.Cancel(); _filterCts=null; LvFiles.ItemsSource=_items; UpdateIndexState(); return; }
        var snapshot=_items.ToList(); TxtStatus.Text="Filter..."; _filterCts?.Cancel(); var cts=_filterCts=new CancellationTokenSource(); var tok=cts.Token;
                    List<DocumentMetadata> result; int textHits=0;
                    var idxProgress=new Progress<(int Done,int Total)>(p=>{ if(v!=_filterVersion) return; ShowIndexBuilding(p.Done,p.Total); });
                                        if(ft.Trim().Length>=3 && !DocumentStore.IsFullTextIndexReady) ShowIndexBuilding(0,0); else ShowSearching();
                    try{ result=await Task.Run(()=>{ if(!DocumentStore.IsFullTextIndexReady) DocumentStore.EnsureFullTextIndex(idxProgress); var full=DocumentStore.SearchFullText(ft,idxProgress); tok.ThrowIfCancellationRequested(); var list=new List<DocumentMetadata>(); var opts=new ParallelOptions{CancellationToken=tok}; var hit=new bool[snapshot.Count];
                        Parallel.For(0,snapshot.Count,opts,k=>{ var i=snapshot[k]; var key=string.Concat(i.FileName,"\u0001",i.InvoiceNumber,"\u0001",i.DocumentNumber,"\u0001",i.CompanyName,"\u0001",i.DocumentType,"\u0001",Convert.ToString(i.Date)); hit[k]=key.Contains(ft,StringComparison.OrdinalIgnoreCase) || (full!=null && full.Contains(i.FilePath)); });
                        for(int k=0;k<hit.Length;k++) if(hit[k]) list.Add(snapshot[k]); if(full!=null) textHits=full.Count; return list; },tok); }
                    catch(OperationCanceledException){ if(v==_filterVersion) UpdateIndexState(); return; }
                    if(v!=_filterVersion) return; LvFiles.ItemsSource=result; UpdateIndexState(); TxtStatus.Text=$"{result.Count:N0} von {snapshot.Count:N0} Belegen" + (textHits>0?$" (inkl. Volltext: {textHits:N0} Treffer im Belegtext)":""); }
                private CancellationTokenSource? _filterCts;
        private System.Windows.Threading.DispatcherTimer? _bookTimer; private int _bookFrame;
        private static readonly string[] BookFrames = { "\U0001F4D5", "\U0001F4D6", "\U0001F4D7", "\U0001F4D6", "\U0001F4D8", "\U0001F4D6", "\U0001F4D9", "\U0001F4D6" };
        private void ShowIndexBuilding(int done,int total)
        {
            if(_bookTimer==null){ _bookTimer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(180)}; _bookTimer.Tick+=(_,_)=>IdxIcon.Text=BookFrames[_bookFrame++%BookFrames.Length]; }
            if(!_bookTimer.IsEnabled) _bookTimer.Start();
            IdxLamp.Fill=System.Windows.Media.Brushes.Orange;
            IdxBar.Visibility=Visibility.Visible; IdxBar.IsIndeterminate=total<=0;
            if(total>0){ IdxBar.Maximum=total; IdxBar.Value=done; }
            IdxText.Text=total>0?$"Index wird erstellt (einmalig) … {done:N0} von {total:N0} Belegen":"Index wird erstellt …";
            TxtStatus.Text=IdxText.Text;
        }
        private void ShowSearching()
        {
            _bookTimer?.Stop();
            IdxLamp.Fill=System.Windows.Media.Brushes.Gold;
            IdxIcon.Text="\U0001F50D";
            IdxBar.Visibility=Visibility.Visible; IdxBar.IsIndeterminate=true;
            IdxText.Text="Suche läuft …";
        }
        private void UpdateIndexState()
        {
            _bookTimer?.Stop(); IdxBar.Visibility=Visibility.Collapsed;
            bool ok=DocumentStore.IsFullTextIndexReady;
            IdxLamp.Fill=ok?System.Windows.Media.Brushes.LimeGreen:System.Windows.Media.Brushes.Gray;
            IdxIcon.Text=ok?"\U0001F4D7":"\U0001F4D5";
            IdxText.Text=ok?"Index bereit":"Suchindex: wird bei der ersten Suche (ab 3 Zeichen) erstellt";
        }
        private void CmbDocType_SelectionChanged(object s,SelectionChangedEventArgs e){ if(_isPopulatingSelectors) return; if(_currentMeta is { Loaded:true } meta){ meta.DocTypeManuallySet=true; UpdateMetadataFromSelections(meta); var picked=e.AddedItems.Count>0 ? e.AddedItems[0] as string : null; if(!string.IsNullOrWhiteSpace(picked)) meta.DocumentType=picked.Trim(); if(ApplyLearnedRules(meta)){ PopulateNumberDate(meta); ScheduleStoreSave(meta); } AutoAssignNumbers(meta); ShowDetails(meta); RefreshNewNamePreview(meta); QueueDocTypeAsk(meta, picked ?? meta.DocumentType); } }

        // Rückfrage für die markierten Belege erst nach abgeschlossener Auswahl (Liste zu, Enter, Feld verlassen) – nicht mitten im
        // Tippen: Passt der Text genau zu einer Belegart, wechselt die Auswahl sofort, und Enter/Leertaste hätten sonst unbemerkt „Ja“ ausgelöst.
        private (DocumentMetadata Meta, string DocType, List<DocumentMetadata> Marked)? _pendingDocTypeAsk;
        private bool _docTypeApplying;

        private void QueueDocTypeAsk(DocumentMetadata meta, string? docType)
        {
            if (string.IsNullOrWhiteSpace(docType)) return;
            var marked = LvFiles.SelectedItems.OfType<DocumentMetadata>().ToList();
            if (marked.Count < 2 || !marked.Contains(meta)) { _pendingDocTypeAsk = null; return; }
            _pendingDocTypeAsk = (meta, docType.Trim(), marked);
            if (!CmbDocType.IsDropDownOpen && !CmbDocType.IsKeyboardFocusWithin) ScheduleDocTypeAsk(false);
        }

        private void ScheduleDocTypeAsk(bool committed)
        {
            if (_pendingDocTypeAsk == null) return;
            Dispatcher.BeginInvoke(new Action(async () => await AskPendingDocTypeAsync(committed)), System.Windows.Threading.DispatcherPriority.Input);
        }

        private void CmbDocType_DropDownClosed(object sender, EventArgs e) => ScheduleDocTypeAsk(true);

        private async Task AskPendingDocTypeAsync(bool committed)
        {
            if (_pendingDocTypeAsk is not { } p || _docTypeApplying) return;
            if (!committed && (CmbDocType.IsDropDownOpen || CmbDocType.IsKeyboardFocusWithin)) return;
            _pendingDocTypeAsk = null;
            await ApplyDocTypeToSelectionAsync(p.Meta, (string.IsNullOrWhiteSpace(p.Meta.DocumentType) ? p.DocType : p.Meta.DocumentType).Trim(), p.Marked);
        }

        /// <summary>Gew&#228;hlte Belegart auf weitere markierte Belege &#252;bernehmen (nach R&#252;ckfrage).</summary>
        private async Task ApplyDocTypeToSelectionAsync(DocumentMetadata current, string docType, List<DocumentMetadata> marked)
        {
            if (string.IsNullOrWhiteSpace(docType)) return;
            var loaded = new HashSet<DocumentMetadata>(_items);
            var others = marked.Where(m => m != current && m.Loaded && loaded.Contains(m)
                && !string.Equals(m.DocumentType?.Trim(), docType, StringComparison.OrdinalIgnoreCase)).ToList();
            if (others.Count == 0) return;
            var question = $"Belegart \u201E{docType}\u201C auch f\u00FCr {(others.Count == 1 ? "den weiteren markierten Beleg" : $"die {others.Count:N0} weiteren markierten Belege")} \u00FCbernehmen?";
            // Standard „Nein“: eine Massenänderung soll nie durch einen versehentlichen Tastendruck bestätigt werden
            if (System.Windows.MessageBox.Show(this, question, "Belegart \u00FCbernehmen", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            _docTypeApplying = true;
            int learned;
            try
            {
                var tick = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < others.Count; i++)
                {
                    var m = others[i];
                    m.DocumentType = docType;
                    m.DocTypeManuallySet = true;
                    AutoAssignNumbers(m);
                    ScheduleStoreSave(m);
                    if (tick.ElapsedMilliseconds >= 40)
                    {
                        tick.Restart();
                        TxtStatus.Text = $"Belegart \u201E{docType}\u201C wird \u00FCbernommen: {i + 1:N0} / {others.Count:N0}...";
                        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
                    }
                }
                learned = await ApplyLearnedRulesAsync(others);
            }
            finally { _docTypeApplying = false; }
            if (_currentMeta != null) RefreshNewNamePreview(_currentMeta);
            TxtStatus.Text = $"Belegart \u201E{docType}\u201C f\u00FCr {others.Count + 1:N0} Belege \u00FCbernommen."
                + (learned > 0 ? $" Gelernte Nummer/Datum bei {learned:N0} Beleg(en) übernommen." : "");
        }
        private void EditableCombo_DropDownClosed(object sender,EventArgs e){ if(_currentMeta is { Loaded:true } cm){ SaveCurrentSettings(cm); AutoAssignNumbers(cm); ShowDetails(cm); RefreshNewNamePreview(cm); } if(sender==CmbDocType) ScheduleDocTypeAsk(false); }
        private void EditableCombo_KeyDown(object sender,System.Windows.Input.KeyEventArgs e){ if(e.Key==System.Windows.Input.Key.Enter){ if(sender is SWC.ComboBox combo){ combo.IsDropDownOpen=false; if(combo==CmbDateCandidates) ExpandDateInput(); if(_currentMeta is { Loaded:true } cm){ SaveCurrentSettings(cm); AutoAssignNumbers(cm); ShowDetails(cm); RefreshNewNamePreview(cm); } if(combo==CmbDocType) ScheduleDocTypeAsk(true); } } }
        private void InsertPosition_Changed(object sender,RoutedEventArgs e){ if(_isPopulatingSelectors) return; if(_currentMeta is { Loaded:true } meta){ SaveCurrentSettings(meta); UpdateMetadataFromSelections(meta); RefreshNewNamePreview(meta); } }
        private void CmbNumberCandidates_SelectionChanged(object s,SelectionChangedEventArgs e){ if(_isPopulatingSelectors) return; if(_currentMeta is { Loaded:true } meta){ meta.NumberManuallySet=true; UpdateMetadataFromSelections(meta); if(e!=null) LearnNumberPattern(meta); AutoAssignNumbers(meta); ShowDetails(meta); RefreshNewNamePreview(meta); LvFiles.Items.Refresh(); } }
        private void CmbNumberCandidates_LostFocus(object s,RoutedEventArgs e)=>CmbNumberCandidates_SelectionChanged(s,null!);
        private void CmbNumberCandidates_TextChanged(object s,TextChangedEventArgs e)=>CmbNumberCandidates_SelectionChanged(s,null!);
        private void EditableCombo_PreviewMouseLeftButtonDown(object s,System.Windows.Input.MouseButtonEventArgs e){ }
        private void CmbDateCandidates_SelectionChanged(object s,SelectionChangedEventArgs e){ if(_isPopulatingSelectors) return; if(_currentMeta is { Loaded:true } meta){ meta.DateManuallySet=true; UpdateMetadataFromSelections(meta); AutoAssignNumbers(meta); ShowDetails(meta); RefreshNewNamePreview(meta); LvFiles.Items.Refresh(); } }
        private void CmbDateCandidates_LostFocus(object s,RoutedEventArgs e){ ExpandDateInput(); CmbDateCandidates_SelectionChanged(s,null!); }
        /// <summary>Kurzeingabe im Datumsfeld vervollständigen (0102 → 01.02.aktuelles Jahr, 010226 → 01.02.2026).</summary>
        private void ExpandDateInput(){ if(Dateiumbenenner.Engine.DateInput.Expand(CmbDateCandidates.Text) is { } full){ CmbDateCandidates.Text=full; if(CmbDateCandidates.Template?.FindName("PART_EditableTextBox",CmbDateCandidates) is SWC.TextBox tb) tb.CaretIndex=full.Length; } }
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

