using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Dateiumbenenner.Engine;
using ComboBox = System.Windows.Controls.ComboBox;

namespace Dateiumbenenner
{
    public partial class NumberRulesWindow : Window
    {
        public sealed class DocTypeRow
        {
            public string Name { get; set; } = "";
            public string Synonyms { get; set; } = "";
            public string Keywords { get; set; } = "";
        }

        private readonly ObservableCollection<NumberRule> _rules;
        private readonly ObservableCollection<DocTypeRow> _docTypes;
        private readonly ObservableCollection<AmountRule> _amountRules;
        private readonly ObservableCollection<DateRule> _dateRules;

        /// <summary>Anzeige der Schreibweisen in der Datum-Tabelle.</summary>
        public sealed record FormatChoice(DateFormatKind Value, string Label);

        private static readonly FormatChoice[] FormatChoices =
        {
            new(DateFormatKind.Beliebig, "(beliebig)"),
            new(DateFormatKind.Numerisch, "TT.MM.JJJJ – (Datum)"),
            new(DateFormatKind.Monatsname, "TT. Monat JJJJ – (DatumJan)"),
            new(DateFormatKind.JahrMonatTag, "JJJJ-MM-TT – (DatumJMT)"),
            new(DateFormatKind.US, "MM/TT/JJJJ – (DatumUS)"),
            new(DateFormatKind.Frei, "eigene Maske – (DatumFrei)"),
        };

        /// <summary>true, wenn nach dem Speichern die Belegnummern neu ermittelt werden sollen.</summary>
        public bool Recalculate { get; private set; }

        private readonly System.Collections.Generic.List<(string Company, string DocType, string Number)> _samples = new();

        public NumberRulesWindow(string? company = null, string? docType = null,
            System.Collections.Generic.IEnumerable<(string? Company, string? DocType, string? Number)>? samples = null) : this(company, docType)
        {
            if (samples != null)
                _samples.AddRange(samples.Where(s => !string.IsNullOrWhiteSpace(s.Company) && !string.IsNullOrWhiteSpace(s.Number))
                    .Select(s => (s.Company!.Trim(), (s.DocType ?? "").Trim(), s.Number!.Trim())));
            if (!string.IsNullOrWhiteSpace(company)) AddSuggestions(company.Trim());
            FillCompanies(company);
        }

        private const string AllCompanies = "(alle Firmen)";

        private void FillCompanies(string? select = null)
        {
            var f = TxtCompanyFilter.Text.Trim();
            var names = _samples.Select(s => s.Company).Concat(_rules.Select(r => r.Company))
                .Concat(_dateRules.Select(r => r.Company)).Concat(_amountRules.Select(r => r.Company))
                .Where(c => !string.IsNullOrWhiteSpace(c) && c != "*").Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(c => f.Length == 0 || c.Contains(f, StringComparison.OrdinalIgnoreCase))
                .OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList();
            names.Insert(0, AllCompanies);
            LstCompanies.ItemsSource = names;
            LstCompanies.SelectedItem = names.FirstOrDefault(n => string.Equals(n, select, StringComparison.OrdinalIgnoreCase)) ?? AllCompanies;
        }

        private void TxtCompanyFilter_TextChanged(object sender, TextChangedEventArgs e) => FillCompanies(LstCompanies.SelectedItem as string);

        private void LstCompanies_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var c = LstCompanies.SelectedItem as string;
            GridRules.CommitEdit(DataGridEditingUnit.Row, true);
            GridDateRules.CommitEdit(DataGridEditingUnit.Row, true);
            GridAmountRules.CommitEdit(DataGridEditingUnit.Row, true);
            var views = new (System.ComponentModel.ICollectionView View, Func<object, string?> Company)[]
            {
                (System.Windows.Data.CollectionViewSource.GetDefaultView(_rules), o => (o as NumberRule)?.Company),
                (System.Windows.Data.CollectionViewSource.GetDefaultView(_dateRules), o => (o as DateRule)?.Company),
                (System.Windows.Data.CollectionViewSource.GetDefaultView(_amountRules), o => (o as AmountRule)?.Company),
            };
            if (c == null || c == AllCompanies) { foreach (var v in views) v.View.Filter = null; return; }
            AddSuggestions(c);
            TxtTestCompany.Text = c;
            foreach (var v in views)
            {
                var get = v.Company;
                v.View.Filter = o => get(o)?.Trim() is { } rc && (string.Equals(rc, c, StringComparison.OrdinalIgnoreCase) || rc == "*");
            }
        }

        private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!ReferenceEquals(e.OriginalSource, Tabs) || TabDocTypes == null) return;
            // Belegarten gelten für alle Firmen
            PnlCompanies.IsEnabled = !TabDocTypes.IsSelected;
        }

        private void BtnSuggest_Click(object sender, RoutedEventArgs e)
        {
            var c = TxtTestCompany.Text.Trim();
            if (c.Length == 0) { System.Windows.MessageBox.Show(this, "Bitte oben bei Firma einen Namen eingeben.", Title); return; }
            if (AddSuggestions(c) == 0) System.Windows.MessageBox.Show(this, $"Keine neuen Vorschl\u00E4ge f\u00FCr \u201E{c}\u201C.", Title);
        }

        /// <summary>Legt f\u00FCr die Firma je Belegart eine Regel an \u2013 Maske aus bisher erkannten Nummern, sonst leer zum Ausf\u00FCllen.</summary>
        private int AddSuggestions(string company)
        {
            bool Eq(string? a, string? b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
            var types = DocTypeStore.Names.Concat(_samples.Where(s => Eq(s.Company, company)).Select(s => s.DocType))
                .Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var added = new System.Collections.Generic.List<NumberRule>();
            foreach (var t in types)
            {
                var nums = _samples.Where(s => Eq(s.Company, company) && Eq(s.DocType, t))
                    .GroupBy(s => s.Number, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).Select(g => g.Key).ToList();
                var existing = _rules.FirstOrDefault(r => Eq(r.Company, company) && Eq(r.DocType, t));
                if (existing != null)
                {
                    foreach (var n in nums) if (!existing.Examples.Contains(n)) existing.Examples.Add(n);
                    continue;
                }
                var first = nums.FirstOrDefault();
                var r = new NumberRule { Company = company, DocType = t, Examples = nums, Example = first, Mask = first == null ? null : NumberRuleStore.MaskFromValue(first) };
                _rules.Insert(added.Count, r);
                added.Add(r);
            }
            GridRules.Items.Refresh();
            if (added.Count > 0) GridRules.ScrollIntoView(added[0]);
            return added.Count;
        }

        public NumberRulesWindow(string? company, string? docType)
        {
            InitializeComponent();
            ColKind.ItemsSource = Enum.GetValues(typeof(NumberRuleKind));
            ColKeywordPos.ItemsSource = Enum.GetValues(typeof(KeywordPosition));
            _rules = new ObservableCollection<NumberRule>(NumberRuleStore.Rules
                .Select(Clone)
                .OrderBy(r => r.Learned).ThenBy(r => r.Company).ThenBy(r => r.DocType).ThenByDescending(r => r.Priority));
            _docTypes = new ObservableCollection<DocTypeRow>(DocTypeStore.Items.Select(d => new DocTypeRow
            {
                Name = d.Name,
                Synonyms = string.Join("; ", d.Synonyms),
                Keywords = string.Join("; ", d.NumberKeywords)
            }));
            GridRules.ItemsSource = _rules;
            GridDocTypes.ItemsSource = _docTypes;
            ColAmountPos.ItemsSource = Enum.GetValues(typeof(AmountPosition));
            ColAmountPage.ItemsSource = Enum.GetValues(typeof(AmountPage));
            _amountRules = new ObservableCollection<AmountRule>(AmountRuleStore.Rules.Select(r => new AmountRule
            {
                Company = r.Company, DocType = r.DocType, Keyword = r.Keyword, Position = r.Position, KeywordEnd = r.KeywordEnd, Page = r.Page, Priority = r.Priority
            }));
            GridAmountRules.ItemsSource = _amountRules;
            ColDatePos.ItemsSource = Enum.GetValues(typeof(DatePosition));
            ColDateFormat.ItemsSource = FormatChoices;
            _dateRules = new ObservableCollection<DateRule>(DateRuleStore.Rules.Select(r => new DateRule
            {
                Company = r.Company, DocType = r.DocType, Keyword = r.Keyword, Position = r.Position, Format = r.Format, Mask = r.Mask, Priority = r.Priority
            }));
            GridDateRules.ItemsSource = _dateRules;
            TxtTestCompany.Text = company ?? "";
            TxtTestDocType.Text = docType ?? "";
        }

        private static NumberRule Clone(NumberRule r) => new()
        {
            Company = r.Company, DocType = r.DocType, Priority = r.Priority, Kind = r.Kind, Mask = r.Mask,
            MinLength = r.MinLength, MaxLength = r.MaxLength, AllowDigits = r.AllowDigits, AllowLetters = r.AllowLetters,
            Prefix = r.Prefix, Learned = r.Learned, Example = r.Example,
                        Examples = string.IsNullOrEmpty(r.Example) ? new() : new() { r.Example }
        };

        private static System.Collections.Generic.List<string> Split(string? s) =>
            (s ?? "").Split(';').Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        private void BtnAddRule_Click(object sender, RoutedEventArgs e)
        {
            var r = new NumberRule
            {
                Company = string.IsNullOrWhiteSpace(TxtTestCompany.Text) ? "*" : TxtTestCompany.Text.Trim(),
                DocType = string.IsNullOrWhiteSpace(TxtTestDocType.Text) ? "*" : TxtTestDocType.Text.Trim(),
                Mask = string.IsNullOrWhiteSpace(TxtTestValue.Text) ? null : NumberRuleStore.MaskFromValue(TxtTestValue.Text)
            };
            _rules.Insert(0, r);
            System.Windows.Data.CollectionViewSource.GetDefaultView(_rules).Refresh();
            GridRules.SelectedItem = r;
            GridRules.ScrollIntoView(r);
        }

        private void BtnDeleteRule_Click(object sender, RoutedEventArgs e)
        {
            foreach (var r in GridRules.SelectedItems.OfType<NumberRule>().ToList()) _rules.Remove(r);
            UpdateTest();
        }

        private void BtnDeleteLearned_Click(object sender, RoutedEventArgs e)
        {
            foreach (var r in _rules.Where(r => r.Learned).ToList()) _rules.Remove(r);
            UpdateTest();
        }

        private void BtnAddAmountRule_Click(object sender, RoutedEventArgs e)
        {
            var r = new AmountRule
            {
                Company = string.IsNullOrWhiteSpace(TxtTestCompany.Text) ? "*" : TxtTestCompany.Text.Trim(),
                DocType = string.IsNullOrWhiteSpace(TxtTestDocType.Text) ? "*" : TxtTestDocType.Text.Trim(),
                Keyword = "Gesamtbetrag"
            };
            _amountRules.Insert(0, r);
            GridAmountRules.SelectedItem = r;
            GridAmountRules.ScrollIntoView(r);
        }

        private void BtnDeleteAmountRule_Click(object sender, RoutedEventArgs e)
        {
            foreach (var r in GridAmountRules.SelectedItems.OfType<AmountRule>().ToList()) _amountRules.Remove(r);
        }

        private void BtnAddDateRule_Click(object sender, RoutedEventArgs e)
        {
            AddDateRule(new DateRule
            {
                Company = string.IsNullOrWhiteSpace(TxtTestCompany.Text) ? "*" : TxtTestCompany.Text.Trim(),
                DocType = string.IsNullOrWhiteSpace(TxtTestDocType.Text) ? "*" : TxtTestDocType.Text.Trim(),
                Keyword = "Rechnungsdatum"
            });
        }

        private void AddDateRule(DateRule r)
        {
            _dateRules.Insert(0, r);
            System.Windows.Data.CollectionViewSource.GetDefaultView(_dateRules).Refresh();
            GridDateRules.SelectedItem = r;
            GridDateRules.ScrollIntoView(r);
            UpdateDateTest();
        }

        private void BtnDeleteDateRule_Click(object sender, RoutedEventArgs e)
        {
            foreach (var r in GridDateRules.SelectedItems.OfType<DateRule>().ToList()) _dateRules.Remove(r);
            UpdateDateTest();
        }

        /// <summary>Begriff (gleiche Zeile links bzw. Spalte darüber) und Schreibweise des Datums aus dem angezeigten Beleg als Regel anlegen.</summary>
        private void BtnDateFromDocument_Click(object sender, RoutedEventArgs e)
        {
            var target = DateRuleStore.FindDates(_docDate ?? "").FirstOrDefault().Date;
            if (string.IsNullOrEmpty(_docText) || target == null)
            {
                System.Windows.MessageBox.Show(this, "Kein Beleg mit erkanntem Datum geöffnet – den Dialog über einen Beleg („…“ neben Nummer, Datum oder Betrag) öffnen.", Title);
                return;
            }
            var rule = SuggestDateRule(_docText, target);
            if (rule == null)
            {
                System.Windows.MessageBox.Show(this, $"Das Datum {_docDate} wurde im Belegtext nicht gefunden.", Title);
                return;
            }
            rule.Company = string.IsNullOrWhiteSpace(TxtTestCompany.Text) ? "*" : TxtTestCompany.Text.Trim();
            rule.DocType = string.IsNullOrWhiteSpace(TxtTestDocType.Text) ? "*" : TxtTestDocType.Text.Trim();
            var same = _dateRules.FirstOrDefault(r => string.Equals(r.Company, rule.Company, StringComparison.OrdinalIgnoreCase)
                && string.Equals(r.DocType, rule.DocType, StringComparison.OrdinalIgnoreCase)
                && string.Equals(r.Keyword?.Trim(), rule.Keyword, StringComparison.OrdinalIgnoreCase));
            if (same != null)
            {
                same.Position = rule.Position; same.Format = rule.Format; same.Mask = rule.Mask;
                GridDateRules.Items.Refresh();
                GridDateRules.SelectedItem = same;
                UpdateDateTest();
                return;
            }
            AddDateRule(rule);
        }

        /// <summary>Regel zum Datum im Text: Begriff links in der Zeile (Lage Rechts) oder in der Spalte darüber (Lage Darunter), Schreibweise wie im Text.</summary>
        public static DateRule? SuggestDateRule(string text, string date)
        {
            var lines = text.Split(new[] { "\r\n", "\n", "\r", "\f" }, StringSplitOptions.None);
            var labelRx = new System.Text.RegularExpressions.Regex(@"(?<t>\p{L}[\p{L}\-]{2,})\.?\s*[.:]?\s*$");
            DateRule? fallback = null;
            for (int i = 0; i < lines.Length; i++)
                foreach (var hit in DateRuleStore.FindDates(lines[i]).Where(h => h.Date == date))
                {
                    var format = DateRuleStore.DetectFormat(hit.Raw);
                    var left = labelRx.Match(lines[i].Substring(0, hit.Index));
                    if (left.Success)
                        return new DateRule { Keyword = left.Groups["t"].Value, Position = DatePosition.Rechts, Format = format };
                    for (int j = i - 1; j >= Math.Max(0, i - 2); j--)
                    {
                        var above = System.Text.RegularExpressions.Regex.Matches(lines[j], @"\p{L}[\p{L}\-]{2,}")
                            .Where(m => m.Index <= hit.Index + hit.Length + 6 && m.Index + m.Length >= hit.Index - 6)
                            .OrderBy(m => Math.Abs(m.Index + m.Length / 2.0 - (hit.Index + hit.Length / 2.0))).FirstOrDefault();
                        if (above != null) return new DateRule { Keyword = above.Value, Position = DatePosition.Darunter, Format = format };
                    }
                    fallback ??= new DateRule { Format = format };
                }
            return fallback;
        }

        private void BtnAddDocType_Click(object sender, RoutedEventArgs e)
        {
            var row = new DocTypeRow { Name = "Neue Belegart" };
            _docTypes.Add(row);
            GridDocTypes.SelectedItem = row;
            GridDocTypes.ScrollIntoView(row);
        }

        private void BtnDeleteDocType_Click(object sender, RoutedEventArgs e)
        {
            foreach (var r in GridDocTypes.SelectedItems.OfType<DocTypeRow>().ToList()) _docTypes.Remove(r);
        }

        private void GridRules_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e) =>
            Dispatcher.BeginInvoke(new Action(() => { UpdateTest(); UpdateDateTest(); }), System.Windows.Threading.DispatcherPriority.Background);

        private void Test_Changed(object sender, TextChangedEventArgs e) { UpdateTest(); UpdateDateTest(); }

        /// <summary>Datumsregeln testen: Textzeile (oder Text des angezeigten Belegs) mit den Regeln der Firma/Belegart.</summary>
        private void UpdateDateTest()
        {
            if (TxtDateTestResult == null || _dateRules == null) return;
            var text = string.IsNullOrWhiteSpace(TxtDateTest.Text) ? _docText : TxtDateTest.Text;
            string company = TxtTestCompany.Text.Trim(), docType = TxtTestDocType.Text.Trim();
            var fn = DateRuleStore.ArchiveFunction(DateRuleStore.FormatFor(company, docType, _dateRules));
            var xml = fn == null ? "" : $"XML: ({fn})";
            if (string.IsNullOrWhiteSpace(text)) { TxtDateTestResult.Text = xml; return; }
            var field = new Field();
            var hit = DateRuleStore.Apply(field, company, docType, text, _dateRules);
            var source = hit == null ? null : field.Candidates.FirstOrDefault(c => c.Value == hit)?.Source;
            TxtDateTestResult.Text = (hit == null ? "Keine Datumsregel findet ein Datum" : $"Datum {hit} ({source})") + (xml.Length > 0 ? "  ·  " + xml : "");
        }

        private void UpdateTest()
        {
            if (TxtTestResult == null || _rules == null) return;
            var value = TxtTestValue.Text?.Trim();
            if (string.IsNullOrEmpty(value)) { TxtTestResult.Text = ""; return; }
            string company = TxtTestCompany.Text.Trim(), docType = TxtTestDocType.Text.Trim();
            bool Any(string? s) => string.IsNullOrWhiteSpace(s) || s.Trim() == "*";
            bool Eq(string? a, string? b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
            var best = _rules
                .Where(r => (Any(r.Company) || Eq(r.Company, company)) && (Any(r.DocType) || Eq(r.DocType, docType)))
                .Select(r => (Rule: r, Match: NumberRuleStore.Match(r, value)))
                .OrderByDescending(x => x.Match * x.Rule.Priority)
                .FirstOrDefault();
            TxtTestResult.Text = best.Rule == null || best.Match <= 0
                ? "Keine passende Regel"
                : $"\u00DCbereinstimmung {best.Match:P0} mit {(best.Rule.Kind == NumberRuleKind.Maske ? best.Rule.Mask : "Merkmalen")} (Priorit\u00E4t {best.Rule.Priority}, Punkte {best.Match * best.Rule.Priority:0.0})";
        }

        private void Example_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox cb && cb.SelectedItem is string s) ApplyExample(cb, s);
        }

        private void Example_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is ComboBox cb) ApplyExample(cb, cb.Text);
        }

        /// <summary>\u00DCbersetzt die gew\u00E4hlte Beispielnummer in eine Maske und fragt nach dem Speichern.</summary>
        private void ApplyExample(ComboBox cb, string? value)
        {
            if (cb.DataContext is not NumberRule r) return;
            value = value?.Trim();
            if (string.IsNullOrEmpty(value)) return;
            var mask = NumberRuleStore.MaskFromValue(value);
            if (string.Equals(r.Mask, mask, StringComparison.Ordinal) && r.Example == value) return;
            r.Example = value;
            r.Mask = mask;
            r.Kind = NumberRuleKind.Maske;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                GridRules.Items.Refresh();
                UpdateTest();
                if (System.Windows.MessageBox.Show(this, $"Nummer \u201E{value}\u201C \u2192 Maske \u201E{mask}\u201C\nf\u00FCr {r.Company} / {r.DocType} jetzt speichern?", Title,
                        MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    SaveAll(askEmpty: false);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private string? _docPath;
        private string? _docText;
        private string? _docDate;
        private PdfPreviewWindow? _preview;
        private readonly List<ValueHit> _hits = new();

        /// <summary>Betrag-Reiter vorwählen.</summary>
        public void ShowAmountTab() => TabAmount.IsSelected = true;

        /// <summary>Datum-Reiter vorwählen.</summary>
        public void ShowDateTab() => TabDate.IsSelected = true;

        /// <summary>Ausgewählten Beleg anzeigen: Vorschau (PDF/TXT) und Fundstellen von Nummer, Datum und Betrag.</summary>
        public void SetDocument(string? filePath, string? fullText, string? number, string? date, string? amount)
        {
            _docPath = filePath;
            _docText = fullText;
            _docDate = date;
            UpdateDateTest();
            if (string.IsNullOrEmpty(filePath)) { PnlDocument.Visibility = Visibility.Collapsed; return; }
            PnlDocument.Visibility = Visibility.Visible;
            TxtDocument.Text = "Beleg: " + System.IO.Path.GetFileName(filePath);
            TxtDocument.ToolTip = filePath;
            PnlSources.Children.Clear();
            _hits.Clear();
            foreach (var (name, field, value) in new[] { ("Nummer", SourceField.Nummer, number), ("Datum", SourceField.Datum, date), ("Betrag", SourceField.Betrag, amount) })
            {
                if (string.IsNullOrWhiteSpace(value)) continue;
                var src = ValueSources.Locate(field, fullText, value);
                var tb = new TextBlock { Margin = new Thickness(0, 1, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
                tb.Inlines.Add(new System.Windows.Documents.Run($"{name} {value}: ") { FontWeight = FontWeights.SemiBold });
                var hint = new System.Windows.Documents.Run(src?.Describe() ?? "im Text nicht gefunden") { Foreground = src != null ? HintBrush : System.Windows.Media.Brushes.DimGray };
                tb.Inlines.Add(hint);
                if (src != null)
                {
                    _hits.Add(src);
                    tb.ToolTip = $"Zeile {src.Line}: {src.LineText}\n(Klicken: Fundstelle in der Vorschau zeigen)";
                    tb.Cursor = System.Windows.Input.Cursors.Hand;
                    tb.MouseEnter += (_, _) => { hint.Foreground = HintHoverBrush; hint.TextDecorations = TextDecorations.Underline; };
                    tb.MouseLeave += (_, _) => { hint.Foreground = HintBrush; hint.TextDecorations = null; };
                    tb.MouseLeftButtonUp += (_, _) => ShowPreview(src);
                }
                PnlSources.Children.Add(tb);
            }
        }

        private static readonly System.Windows.Media.Brush HintBrush = Freeze(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3A, 0x6E, 0xA5)));
        private static readonly System.Windows.Media.Brush HintHoverBrush = Freeze(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1F, 0x4E, 0x8C)));
        private static System.Windows.Media.Brush Freeze(System.Windows.Media.SolidColorBrush b) { b.Freeze(); return b; }

        private void BtnPreview_Click(object sender, RoutedEventArgs e) => ShowPreview(null);

        private void ShowPreview(ValueHit? at)
        {
            if (string.IsNullOrEmpty(_docPath)) return;
            try
            {
                // Eigenes Fenster: Fenster, die vor diesem Dialog offen waren, sind währenddessen gesperrt
                if (_preview == null)
                {
                    _preview = new PdfPreviewWindow { Owner = this };
                    _preview.Closed += (_, _) => _preview = null;
                    _preview.Show();
                }
                // direkt in der Text-Ansicht, alle Fundstellen markiert, zur gewählten scrollen
                var marks = _hits.Select(h => (h.Index, h.Length, h.LabelIndex, h.LabelLength)).ToList();
                _preview.ShowDocumentText(_docPath, _docText, marks, at == null ? 0 : Math.Max(0, _hits.IndexOf(at)));
                _preview.Activate();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(this, "Vorschau nicht m\u00F6glich: " + ex.Message, Title);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            try { _preview?.Close(); } catch { }
            base.OnClosed(e);
        }

        private bool SaveAll(bool askEmpty = true)
        {
            GridRules.CommitEdit(DataGridEditingUnit.Row, true);
            GridDocTypes.CommitEdit(DataGridEditingUnit.Row, true);
            GridAmountRules.CommitEdit(DataGridEditingUnit.Row, true);
            GridDateRules.CommitEdit(DataGridEditingUnit.Row, true);
            var empty = _rules.Count(r => r.Kind == NumberRuleKind.Maske && string.IsNullOrWhiteSpace(r.Mask));
            bool keepEmpty = true;
            if (askEmpty && empty > 0)
            {
                var res = System.Windows.MessageBox.Show(this, $"{empty} Eintr\u00E4ge haben noch keine Maske.\nTrotzdem speichern, um sie sp\u00E4ter zu erg\u00E4nzen?", Title,
                    MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (res == MessageBoxResult.Cancel) return false;
                keepEmpty = res == MessageBoxResult.Yes;
            }
            var dupe = _docTypes.GroupBy(d => d.Name.Trim(), StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Key.Length > 0 && g.Count() > 1);
            if (dupe != null)
            {
                System.Windows.MessageBox.Show(this, $"Belegart \u201E{dupe.Key}\u201C ist mehrfach vorhanden.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            DocTypeStore.Save(_docTypes.Select(d => new DocTypeDefinition
            {
                Name = d.Name.Trim(),
                Synonyms = Split(d.Synonyms),
                NumberKeywords = Split(d.Keywords)
            }));
            NumberRuleStore.Save(_rules.Where(r => keepEmpty || r.Kind == NumberRuleKind.Merkmale || !string.IsNullOrWhiteSpace(r.Mask)).Select(r =>
            {
                r.Company = string.IsNullOrWhiteSpace(r.Company) ? "*" : r.Company.Trim();
                r.DocType = string.IsNullOrWhiteSpace(r.DocType) ? "*" : r.DocType.Trim();
                return r;
            }));
            AmountRuleStore.Save(_amountRules.Select(r =>
            {
                r.Company = string.IsNullOrWhiteSpace(r.Company) ? "*" : r.Company.Trim();
                r.DocType = string.IsNullOrWhiteSpace(r.DocType) ? "*" : r.DocType.Trim();
                return r;
            }));
            DateRuleStore.Save(_dateRules.Select(r =>
            {
                r.Company = string.IsNullOrWhiteSpace(r.Company) ? "*" : r.Company.Trim();
                r.DocType = string.IsNullOrWhiteSpace(r.DocType) ? "*" : r.DocType.Trim();
                r.Keyword = r.Keyword?.Trim() ?? "";
                r.Mask = string.IsNullOrWhiteSpace(r.Mask) ? null : r.Mask.Trim();
                return r;
            }));
            return true;
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (!SaveAll()) return;
            DialogResult = true;
        }

        private void BtnSaveRecalc_Click(object sender, RoutedEventArgs e)
        {
            if (!SaveAll()) return;
            Recalculate = true;
            DialogResult = true;
        }
    }
}
