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
            var view = System.Windows.Data.CollectionViewSource.GetDefaultView(_rules);
            GridRules.CommitEdit(DataGridEditingUnit.Row, true);
            if (c == null || c == AllCompanies) { view.Filter = null; return; }
            AddSuggestions(c);
            TxtTestCompany.Text = c;
            view.Filter = o => o is NumberRule r && (string.Equals(r.Company?.Trim(), c, StringComparison.OrdinalIgnoreCase) || r.Company?.Trim() == "*");
        }

        private void BtnSuggest_Click(object sender, RoutedEventArgs e)
        {
            var c = TxtTestCompany.Text.Trim();
            if (c.Length == 0) { System.Windows.MessageBox.Show(this, "Bitte unten bei Firma einen Namen eingeben.", Title); return; }
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
            Dispatcher.BeginInvoke(new Action(UpdateTest), System.Windows.Threading.DispatcherPriority.Background);

        private void Test_Changed(object sender, TextChangedEventArgs e) => UpdateTest();

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

        private bool SaveAll(bool askEmpty = true)
        {
            GridRules.CommitEdit(DataGridEditingUnit.Row, true);
            GridDocTypes.CommitEdit(DataGridEditingUnit.Row, true);
            GridAmountRules.CommitEdit(DataGridEditingUnit.Row, true);
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
