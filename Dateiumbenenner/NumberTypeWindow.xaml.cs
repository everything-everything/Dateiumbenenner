using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Dateiumbenenner.Engine;

namespace Dateiumbenenner
{
    /// <summary>Nummer je Firma + Belegart festlegen: falsche Nummer (Begriff + Muster) ausschließen, eine andere bevorzugen, Nummernfeld wählen.</summary>
    public partial class NumberTypeWindow : Window
    {
        private sealed record TargetChoice(NumberTarget Value, string Label)
        {
            public override string ToString() => Label;
        }

        private static readonly TargetChoice[] Targets =
        {
            new(NumberTarget.Automatisch, "Automatisch (nach Belegart)"),
            new(NumberTarget.Rechnungsnummer, "Rechnungsnummer"),
            new(NumberTarget.Belegnummer, "Belegnummer"),
            new(NumberTarget.Keine, "Keine Nummer (Felder leer lassen)"),
        };

        private readonly string _company;
        private readonly string _docType;
        private readonly KeywordPosition _exPosition;
        private readonly Dictionary<string, string> _labelValues = new(StringComparer.OrdinalIgnoreCase);

        public NumberTypeWindow(string company, string docType, string? number, string? text, IEnumerable<string>? candidates)
        {
            InitializeComponent();
            _company = company.Trim();
            _docType = DocTypeStore.Canonical(docType) ?? docType.Trim();
            TxtHeader.Text = $"Firma: {_company}   \u00B7   Belegart: {_docType}";
            number = number?.Trim();
            string? exLabel = null;
            if (!string.IsNullOrEmpty(number))
            {
                var hit = ValueSources.Locate(SourceField.Nummer, text, number);
                exLabel = CleanLabel(hit?.Label);
                _exPosition = hit?.Where == "rechts daneben" ? KeywordPosition.HinterNummer : KeywordPosition.VorNummer;
                TxtCurrent.Text = $"Aktuelle Nummer: {number} \u2013 {hit?.Describe() ?? "im Text nicht gefunden"}";
                TxtExKeyword.Text = exLabel ?? "";
                TxtExMask.Text = NumberRuleStore.MaskFromValue(number);
            }
            else
            {
                _exPosition = KeywordPosition.VorNummer;
                TxtCurrent.Text = "Aktuell ist keine Nummer gew\u00E4hlt.";
            }

            // Begriffe anderer Nummern im Beleg als Vorschlag für „stattdessen“
            if (!string.IsNullOrEmpty(text) && candidates != null)
                foreach (var c in candidates.Where(c => !string.IsNullOrWhiteSpace(c) && !c.Trim().Equals(number, StringComparison.OrdinalIgnoreCase))
                             .Distinct(StringComparer.OrdinalIgnoreCase).Take(25))
                {
                    var label = CleanLabel(ValueSources.Locate(SourceField.Nummer, text, c)?.Label);
                    if (label != null && !label.Equals(exLabel, StringComparison.OrdinalIgnoreCase) && !_labelValues.ContainsKey(label))
                        _labelValues[label] = c.Trim();
                }
            var keywords = _labelValues.Keys.ToList();
            foreach (var kw in DocTypeStore.Get(_docType)?.NumberKeywords ?? new List<string>())
                if (!string.IsNullOrWhiteSpace(kw) && !keywords.Contains(kw.Trim(), StringComparer.OrdinalIgnoreCase)) keywords.Add(kw.Trim());
            CmbPreferKeyword.ItemsSource = keywords;

            CmbTarget.ItemsSource = Targets;
            var current = NumberTargetStore.Get(_company, _docType);
            CmbTarget.SelectedItem = Targets.First(t => t.Value == current);

            // Eingaben setzen den zugehörigen Haken
            TxtExKeyword.TextChanged += (_, _) => ChkExclude.IsChecked = true;
            TxtExMask.TextChanged += (_, _) => ChkExclude.IsChecked = true;
            TxtPreferMask.TextChanged += (_, _) => ChkPrefer.IsChecked = true;
            CmbPreferKeyword.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
                new System.Windows.Controls.TextChangedEventHandler((_, _) => ChkPrefer.IsChecked = CmbPreferKeyword.Text.Trim().Length > 0));
        }

        /// <summary>Begriff ohne abschließende Satzzeichen (z. B. „Rechnungs-Nr.:“ → „Rechnungs-Nr“).</summary>
        private static string? CleanLabel(string? label)
        {
            var s = label?.Trim().TrimEnd(':', '#', '.', ' ', '\t').Trim();
            return string.IsNullOrEmpty(s) || !s.Any(char.IsLetter) ? null : s;
        }

        private void CmbPreferKeyword_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (CmbPreferKeyword.SelectedItem is string label && _labelValues.TryGetValue(label, out var value))
                TxtPreferMask.Text = NumberRuleStore.MaskFromValue(value);
        }

        private static string? Trimmed(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            string? exKeyword = Trimmed(TxtExKeyword.Text), exMask = Trimmed(TxtExMask.Text);
            string? prKeyword = Trimmed(CmbPreferKeyword.Text), prMask = Trimmed(TxtPreferMask.Text);
            bool exclude = ChkExclude.IsChecked == true, prefer = ChkPrefer.IsChecked == true;
            if (exclude && exKeyword == null && exMask == null)
            {
                System.Windows.MessageBox.Show(this, "Zum Ausschlie\u00DFen bitte Begriff und/oder Muster angeben.", Title);
                return;
            }
            if (prefer && prKeyword == null)
            {
                System.Windows.MessageBox.Show(this, "Bitte den Begriff der gew\u00FCnschten Nummer angeben.", Title);
                return;
            }
            bool Eq(string? a, string? b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
            bool Mine(NumberRule r) => Eq(r.Company, _company) && Eq(DocTypeStore.Canonical(r.DocType), _docType);
            var rules = NumberRuleStore.Rules.ToList();
            bool changed = false;
            if (exclude && !rules.Any(r => r.Exclude && Mine(r) && Eq(r.Keyword, exKeyword) && Eq(r.Mask, exMask)))
            {
                rules.Insert(0, new NumberRule
                {
                    Company = _company, DocType = _docType, Exclude = true, Kind = NumberRuleKind.Maske, Mask = exMask,
                    Keyword = exKeyword, KeywordPosition = exKeyword == null ? KeywordPosition.Beliebig : _exPosition, Priority = 10
                });
                changed = true;
            }
            if (prefer && !rules.Any(r => !r.Exclude && Mine(r) && Eq(r.Keyword, prKeyword) && Eq(r.Mask, prMask)))
            {
                rules.Insert(0, new NumberRule
                {
                    Company = _company, DocType = _docType, Kind = NumberRuleKind.Maske, Mask = prMask,
                    Keyword = prKeyword, KeywordPosition = KeywordPosition.VorNummer, Priority = 20
                });
                changed = true;
            }
            if (changed) NumberRuleStore.Save(rules);
            if (CmbTarget.SelectedItem is TargetChoice t) NumberTargetStore.Set(_company, _docType, t.Value);
            DialogResult = true;
        }
    }
}
