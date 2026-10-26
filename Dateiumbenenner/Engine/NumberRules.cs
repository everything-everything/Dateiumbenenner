using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Dateiumbenenner.Engine
{
    public enum NumberRuleKind { Maske, Merkmale }

    /// <summary>Lage des Begriffs zur Nummer (bevorzugte Position, kein Muss).</summary>
    public enum KeywordPosition { Beliebig, VorNummer, HinterNummer, Umschlossen }

    /// <summary>
    /// Nummernregel je Firma + Belegart ("*" = alle). Maske: $ = Ziffer, ? = Buchstabe, # = Ziffer oder Buchstabe,
    /// * = beliebiges Zeichen, Leerzeichen = Leerzeichen, sonstige Zeichen w\u00F6rtlich.
    /// Merkmale: ungef\u00E4hre L\u00E4nge und erlaubte Zeichenklassen.
    /// </summary>
    public class NumberRule
    {
        public string Company { get; set; } = "*";
        public string DocType { get; set; } = "*";
        public int Priority { get; set; } = 10;
        public NumberRuleKind Kind { get; set; } = NumberRuleKind.Maske;
        public string? Mask { get; set; }
        public int? MinLength { get; set; }
        public int? MaxLength { get; set; }
        public bool AllowDigits { get; set; } = true;
        public bool AllowLetters { get; set; }
        public bool AllowSpace { get; set; }
        public bool AllowSymbols { get; set; }
        public string? Prefix { get; set; }
        /// <summary>Automatisch aus manueller Auswahl gelernt (niedrige Priorit\u00E4t).</summary>
        public bool Learned { get; set; }
        /// <summary>Gew\u00E4hlte Beispielnummer (daraus wird die Maske \u00FCbersetzt).</summary>
        public string? Example { get; set; }
        /// <summary>Begriff, bei dem die Nummer am wahrscheinlichsten steht (z. B. "Beleg-Nr").</summary>
        public string? Keyword { get; set; }
        public KeywordPosition KeywordPosition { get; set; } = KeywordPosition.Beliebig;
        /// <summary>Zweiter Begriff hinter der Nummer (nur bei "Umschlossen").</summary>
        public string? KeywordEnd { get; set; }
        /// <summary>Bisher erkannte Nummern zur Auswahl (nur Dialog).</summary>
        [JsonIgnore] public List<string> Examples { get; set; } = new();

        // Dialog-Sicht auf Keyword/KeywordEnd/KeywordPosition: Begriff vor bzw. nach der Nummer
        private bool _eitherShownAfter;
        private bool _eitherPending;

        /// <summary>Begriff, der vor der Nummer steht (Dialog).</summary>
        [JsonIgnore]
        public string? KeywordBefore
        {
            get => KeywordPosition == KeywordPosition.HinterNummer || (KeywordPosition == KeywordPosition.Beliebig && _eitherShownAfter) ? null : Keyword;
            set => SetKeywords(value, KeywordAfter, KeywordEitherSide);
        }

        /// <summary>Begriff, der hinter der Nummer steht (Dialog).</summary>
        [JsonIgnore]
        public string? KeywordAfter
        {
            get => KeywordPosition switch
            {
                KeywordPosition.HinterNummer => Keyword,
                KeywordPosition.Umschlossen => KeywordEnd,
                KeywordPosition.Beliebig when _eitherShownAfter => Keyword,
                _ => null
            };
            set => SetKeywords(KeywordBefore, value, KeywordEitherSide);
        }

        /// <summary>Ein einzelner Begriff darf vor oder hinter der Nummer stehen (Dialog).</summary>
        [JsonIgnore]
        public bool KeywordEitherSide
        {
            get => string.IsNullOrWhiteSpace(Keyword) ? _eitherPending : KeywordPosition == KeywordPosition.Beliebig;
            set { _eitherPending = value; SetKeywords(KeywordBefore, KeywordAfter, value); }
        }

        private void SetKeywords(string? before, string? after, bool eitherSide)
        {
            bool b = !string.IsNullOrWhiteSpace(before), a = !string.IsNullOrWhiteSpace(after);
            _eitherShownAfter = false;
            if (b && a) { Keyword = before; KeywordEnd = after; KeywordPosition = KeywordPosition.Umschlossen; }
            else if (b) { Keyword = before; KeywordEnd = null; KeywordPosition = eitherSide ? KeywordPosition.Beliebig : KeywordPosition.VorNummer; }
            else if (a) { Keyword = after; KeywordEnd = null; KeywordPosition = eitherSide ? KeywordPosition.Beliebig : KeywordPosition.HinterNummer; _eitherShownAfter = eitherSide; }
            else { Keyword = null; KeywordEnd = null; KeywordPosition = KeywordPosition.Beliebig; }
        }
    }

    public static class NumberRuleStore
    {
        private static List<NumberRule>? _rules;
        private static readonly object Sync = new();
        private const int MaxLearnedPriority = 5;

        public static string FilePath => Path.Combine(Plugins.PluginManager.BaseDirectory, "number_rules.json");

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

        public static List<NumberRule> Rules
        {
            get
            {
                lock (Sync)
                {
                    if (_rules != null) return _rules;
                    try
                    {
                        if (File.Exists(FilePath))
                            _rules = JsonSerializer.Deserialize<List<NumberRule>>(SecureStorage.ReadAllText(FilePath), JsonOptions);
                    }
                    catch { }
                    return _rules ??= new List<NumberRule>();
                }
            }
        }

        public static void Save(IEnumerable<NumberRule> rules)
        {
            var list = rules.Where(r => r != null).ToList();
            lock (Sync)
            {
                _rules = list;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                    SecureStorage.WriteAllText(FilePath, JsonSerializer.Serialize(list, JsonOptions));
                }
                catch { }
            }
        }

        private static bool Eq(string? a, string? b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
        private static bool IsAny(string? s) => string.IsNullOrWhiteSpace(s) || s.Trim() == "*";

        /// <summary>Passende Regeln mit Gewichtung nach Genauigkeit (Firma+Belegart 1.0, nur eins 0.8 bzw. 0.7, beides * 0.5).</summary>
        public static List<(NumberRule Rule, double Specificity)> RulesFor(string? company, string? docType)
        {
            var dt = DocTypeStore.Canonical(docType);
            var result = new List<(NumberRule, double)>();
            foreach (var r in Rules.ToList())
            {
                bool anyC = IsAny(r.Company), anyD = IsAny(r.DocType);
                if (!anyC && !Eq(r.Company, company)) continue;
                if (!anyD && !Eq(DocTypeStore.Canonical(r.DocType), dt)) continue;
                double spec = !anyC && !anyD ? 1.0 : !anyC ? 0.8 : !anyD ? 0.7 : 0.5;
                result.Add((r, spec));
            }
            return result;
        }

        /// <summary>Bewertung eines Wertes (0 = passt zu keiner Regel). H\u00F6chstpriorisierte Regel dominiert.</summary>
        public static double Score(string? company, string? docType, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return 0;
            double best = 0;
            foreach (var (r, spec) in RulesFor(company, docType))
                best = Math.Max(best, Match(r, value.Trim()) * r.Priority * spec);
            return best;
        }

        /// <summary>\u00C4hnlichkeit 0..1. Exakter Masken-Treffer = 1, Abweichungen (l\u00E4nger/k\u00FCrzer/andere Zeichen) werden abgestuft.</summary>
        public static double Match(NumberRule rule, string value)
        {
            double f;
            if (rule.Kind == NumberRuleKind.Maske)
            {
                if (string.IsNullOrEmpty(rule.Mask)) return 0;
                int dist = Distance(rule.Mask, value);
                if (dist == 0) f = 1.0;
                else
                {
                    double sim = 1.0 - (double)dist / Math.Max(rule.Mask.Length, value.Length);
                    f = sim < 0.5 ? 0 : sim * sim * 0.8;
                }
            }
            else
            {
                int len = value.Length, min = rule.MinLength ?? 0, max = rule.MaxLength ?? int.MaxValue;
                int off = len < min ? min - len : len > max ? len - max : 0;
                double lenF = Math.Max(0, 1.0 - 0.15 * off);
                int bad = value.Count(c => !Allowed(rule, c));
                double clsF = bad == 0 ? 1.0 : Math.Max(0, 0.5 - 0.1 * (bad - 1));
                bool hasDigit = !rule.AllowDigits || value.Any(char.IsDigit);
                f = lenF * clsF * (hasDigit ? 1.0 : 0.5);
            }
            if (!string.IsNullOrEmpty(rule.Prefix) && !value.StartsWith(rule.Prefix, StringComparison.OrdinalIgnoreCase)) f *= 0.7;
            return f;
        }

        private static bool Allowed(NumberRule r, char c) =>
            char.IsDigit(c) ? r.AllowDigits : char.IsLetter(c) ? r.AllowLetters : char.IsWhiteSpace(c) ? r.AllowSpace : r.AllowSymbols;

        private static bool Fits(char m, char c) => m switch
        {
            '$' => char.IsDigit(c),
            '?' => char.IsLetter(c),
            '#' => char.IsLetterOrDigit(c),
            '*' => true,
            ' ' => char.IsWhiteSpace(c),
            _ => char.ToUpperInvariant(m) == char.ToUpperInvariant(c)
        };

        /// <summary>Editierdistanz zwischen Maske und Wert (Ersetzen/Einf\u00FCgen/L\u00F6schen je 1).</summary>
        private static int Distance(string mask, string value)
        {
            var d = new int[mask.Length + 1, value.Length + 1];
            for (int i = 0; i <= mask.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= value.Length; j++) d[0, j] = j;
            for (int i = 1; i <= mask.Length; i++)
                for (int j = 1; j <= value.Length; j++)
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (Fits(mask[i - 1], value[j - 1]) ? 0 : 1));
            return d[mask.Length, value.Length];
        }

        /// <summary>Maske -> Regex (ohne Anker).</summary>
        public static string MaskToRegex(string mask)
        {
            var sb = new StringBuilder();
            foreach (var m in mask)
                sb.Append(m switch
                {
                    '$' => @"\d",
                    '?' => @"\p{L}",
                    '#' => @"[\p{L}\d]",
                    '*' => @"\S",
                    ' ' => @"\s",
                    _ => Regex.Escape(m.ToString())
                });
            return sb.ToString();
        }

        /// <summary>Maske aus einem Beispielwert (Ziffer -> $, Buchstabe -> ?, Rest w\u00F6rtlich).</summary>
        public static string MaskFromValue(string value)
        {
            var sb = new StringBuilder();
            foreach (var c in value.Trim())
                sb.Append(char.IsDigit(c) ? '$' : char.IsLetter(c) ? '?' : char.IsWhiteSpace(c) ? ' ' : c);
            return sb.ToString();
        }

        /// <summary>Nummern-Kandidaten erg\u00E4nzen (Masken-Treffer im Text, Schl\u00FCsselw\u00F6rter der Belegart) und nach Regeln bewerten.</summary>
        public static void Apply(Field number, string? company, string? docType, string? text)
        {
            var rules = RulesFor(company, docType);
            if (!string.IsNullOrEmpty(text))
            {
                foreach (var (r, spec) in rules.Where(x => x.Rule.Kind == NumberRuleKind.Maske && !string.IsNullOrEmpty(x.Rule.Mask))
                             .OrderByDescending(x => x.Rule.Priority * x.Specificity))
                {
                    try
                    {
                        // Vorgegebenes Muster zuerst: exakte Textstellen vor allen anderen Kandidaten
                        foreach (Match m in Regex.Matches(text, @"(?<![\p{L}\d])" + MaskToRegex(r.Mask!) + @"(?![\p{L}\d])", RegexOptions.IgnoreCase))
                            number.Add(m.Value, 100 + r.Priority * spec, "Muster " + r.Mask);
                    }
                    catch { }
                }
                foreach (var (r, spec) in rules.Where(x => !string.IsNullOrWhiteSpace(x.Rule.Keyword)))
                {
                    try
                    {
                        // Bevorzugte Position: zusätzlich zum Muster-Treffer (gleich aufgebaute Nummern an anderer Stelle
                        // bleiben möglich, liegen aber dahinter)
                        foreach (var v in NearKeyword(r, text).Distinct(StringComparer.OrdinalIgnoreCase))
                        {
                            var existing = number.Candidates.FirstOrDefault(c => c.Value.Equals(v, StringComparison.OrdinalIgnoreCase))?.Score ?? 0;
                            number.Add(v, Math.Max(existing, 50 + r.Priority * spec * Math.Max(0.3, Match(r, v))) + 20, "Begriff " + r.Keyword!.Trim());
                        }
                    }
                    catch { }
                }
                var def = DocTypeStore.Get(docType);
                if (def != null)
                    foreach (var kw in def.NumberKeywords.Where(k => !string.IsNullOrWhiteSpace(k)))
                    {
                        try
                        {
                            var rx = new Regex(@"(?<!\p{L})" + Regex.Escape(kw.Trim()) + @"\.?\s*(?:-?\s*(?:Nr\.?|Nummer|No\.?)\s*)?[:#]?\s*([\p{L}\d][\p{L}\d\-/]{2,})", RegexOptions.IgnoreCase);
                            foreach (Match m in rx.Matches(text))
                                if (m.Groups[1].Value.Any(char.IsDigit)) number.Add(m.Groups[1].Value, 3, kw.Trim());
                        }
                        catch { }
                    }
            }
            if (rules.Count > 0)
                foreach (var c in number.Candidates.ToList())
                {
                    double bonus = Score(company, docType, c.Value);
                    if (bonus > 0) number.Add(c.Value, c.Score + bonus, c.Source);
                }
            // Gelernte Fundstelle (Begriff davor/dahinter, wievielte gleichartige Nummer, Lage) entscheidet zwischen gleich aufgebauten Nummern
            ValueLocationStore.Apply(number, ValueKind.Nummer, company, docType, text);
        }

        /// <summary>Werte an der bevorzugten Begriffsposition (gleiche Zeile oder Folgezeile).</summary>
        private static IEnumerable<string> NearKeyword(NumberRule r, string text)
        {
            string Kw(string k) => @"(?<!\p{L})" + Regex.Escape(k.Trim()) + @"(?!\p{L})\.?\s*[:#]?";
            string val = r.Kind == NumberRuleKind.Maske && !string.IsNullOrEmpty(r.Mask)
                ? @"(?<![\p{L}\d])(?<v>" + MaskToRegex(r.Mask) + @")(?![\p{L}\d])"
                : @"(?<![\p{L}\d])(?<v>[\p{L}\d][\p{L}\d\-/]{2,})";
            const string gap = @"[^\S\n]*(?:\n[^\S\n]*)?";
            var patterns = new List<string>();
            if (r.KeywordPosition is KeywordPosition.Beliebig or KeywordPosition.VorNummer)
                patterns.Add(Kw(r.Keyword!) + gap + val);
            if (r.KeywordPosition is KeywordPosition.Beliebig or KeywordPosition.HinterNummer)
                patterns.Add(val + gap + @"(?<!\p{L})" + Regex.Escape(r.Keyword!.Trim()) + @"(?!\p{L})");
            if (r.KeywordPosition == KeywordPosition.Umschlossen)
                patterns.Add(Kw(r.Keyword!) + gap + val + gap + (string.IsNullOrWhiteSpace(r.KeywordEnd) ? "" : @"(?<!\p{L})" + Regex.Escape(r.KeywordEnd.Trim()) + @"(?!\p{L})"));
            foreach (var p in patterns)
                foreach (Match m in Regex.Matches(text, p, RegexOptions.IgnoreCase))
                {
                    var v = m.Groups["v"].Value.Trim();
                    if (v.Any(char.IsDigit)) yield return v;
                }
        }

        /// <summary>Bester Wert aus Kandidaten und Text nach Regeln (null, wenn keine Regel passt).</summary>
        public static string? Best(string? company, string? docType, IEnumerable<string?> candidates, string? text)
        {
            if (RulesFor(company, docType).Count == 0 && !ValueLocationStore.Has(ValueKind.Nummer, company, docType)) return null;
            var f = new Field();
            int n = 0;
            foreach (var c in candidates) f.Add(c, Math.Max(0, 1 - 0.01 * n++), "Kandidat");
            Apply(f, company, docType, text);
            var best = f.Candidates.Select(c => (c.Value, S: Score(company, docType, c.Value), T: c.Score, L: c.Source == "Fundstelle"))
                .Where(x => x.S > 0 || x.L).OrderByDescending(x => x.T).ThenByDescending(x => x.S).FirstOrDefault();
            return best.Value;
        }

        /// <summary>Manuelle Masken-Regel setzen/entfernen (z. B. aus dem INI-Plugin).</summary>
        public static void SetManualMask(string? company, string? docType, string? mask)
        {
            if (string.IsNullOrWhiteSpace(company) || string.IsNullOrWhiteSpace(docType)) return;
            var list = Rules.ToList();
            list.RemoveAll(r => !r.Learned && r.Kind == NumberRuleKind.Maske && Eq(r.Company, company) && Eq(r.DocType, docType));
            if (!string.IsNullOrWhiteSpace(mask))
                list.Add(new NumberRule { Company = company.Trim(), DocType = DocTypeStore.Canonical(docType) ?? docType.Trim(), Mask = mask.Trim(), Priority = 10 });
            Save(list);
        }

        /// <summary>Aus manueller Auswahl lernen: gelernte Regel anlegen bzw. verst\u00E4rken (max. Priorit\u00E4t 5).</summary>
        public static void Learn(string? company, string? docType, string? value)
        {
            if (string.IsNullOrWhiteSpace(company) || string.IsNullOrWhiteSpace(docType) || string.IsNullOrWhiteSpace(value)) return;
            var mask = MaskFromValue(value);
            var list = Rules.ToList();
            var r = list.FirstOrDefault(x => x.Learned && Eq(x.Company, company) && Eq(x.DocType, DocTypeStore.Canonical(docType)) && x.Mask == mask);
            if (r != null) r.Priority = Math.Min(MaxLearnedPriority, r.Priority + 1);
            else list.Add(new NumberRule { Company = company.Trim(), DocType = DocTypeStore.Canonical(docType) ?? docType.Trim(), Mask = mask, Priority = 1, Learned = true });
            Save(list);
        }
    }

    /// <summary>Belegart mit Umschreibungen (z. B. "Sendeinformation" -> Lieferschein) und Schl\u00FCsselw\u00F6rtern f\u00FCr die Nummer.</summary>
    public class DocTypeDefinition
    {
        public string Name { get; set; } = "";
        public List<string> Synonyms { get; set; } = new();
        public List<string> NumberKeywords { get; set; } = new();
    }

    public static class DocTypeStore
    {
        private static List<DocTypeDefinition>? _items;
        private static readonly object Sync = new();

        public static string FilePath => Path.Combine(Plugins.PluginManager.BaseDirectory, "doc_types.json");

        private static List<DocTypeDefinition> Defaults() => new()
        {
            new() { Name = "Rechnung", NumberKeywords = { "Rechnungsnummer", "Rechnungsnr", "Rechnung" } },
            new() { Name = "Sammelrechnung", NumberKeywords = { "Rechnungsnummer", "Rechnungsnr" } },
            new() { Name = "Stornorechnung", NumberKeywords = { "Stornorechnung", "Rechnungsnummer" } },
            new() { Name = "Gutschrift", NumberKeywords = { "Gutschriftsnummer", "Gutschriftsnr", "Gutschrift" } },
            new() { Name = "Barverkauf", NumberKeywords = { "Barverkauf", "Rechnungsnummer" } },
            new() { Name = "Beleg", NumberKeywords = { "Belegnummer", "Belegnr", "Beleg" } },
            new() { Name = "Angebot", NumberKeywords = { "Angebotsnummer", "Angebotsnr", "Angebot" } },
            new() { Name = "Lieferschein", Synonyms = { "Sendeinformation", "Versandmitteilung", "Lieferavis" }, NumberKeywords = { "Lieferscheinnummer", "Lieferschein", "Liefernr", "Sendungsnummer" } },
            new() { Name = "Bestellung", NumberKeywords = { "Bestellnummer", "Bestellung" } },
            new() { Name = "Mahnung", NumberKeywords = { "Mahnungsnummer", "Mahnung" } },
            new() { Name = "Rechnungskorrektur", Synonyms = { "Korrekturrechnung" }, NumberKeywords = { "Rechnungsnummer", "Korrekturnummer", "Rechnungskorrektur" } },
            new() { Name = "Korrektur", NumberKeywords = { "Korrekturnummer", "Korrektur" } },
            new() { Name = "Reparaturrechnung", NumberKeywords = { "Rechnungsnummer", "Rechnungsnr", "Reparaturrechnung" } },
            new() { Name = "Gutschriftsrechnung", NumberKeywords = { "Gutschriftsnummer", "Rechnungsnummer", "Gutschriftsrechnung" } },
            new() { Name = "Storno", NumberKeywords = { "Stornonummer", "Storno" } },
            new() { Name = "Avis", NumberKeywords = { "Avisnummer", "Avis" } },
            new() { Name = "Lastschrift-Avis", NumberKeywords = { "Avisnummer", "Lastschrift-Avis", "Avis" } },
            new() { Name = "Sendungsinformation", Synonyms = { "Sendunginformation" }, NumberKeywords = { "Sendungsnummer", "Lieferscheinnummer", "Sendungsinformation" } },
            new() { Name = "Werkstattauftrag", Synonyms = { "Werkstatt-Auftrag", "Werkstatt Auftrag" }, NumberKeywords = { "Auftragsnummer", "Auftragsnr", "Werkstattauftrag" } },
            new() { Name = "Reparaturauftrag", NumberKeywords = { "Auftragsnummer", "Auftragsnr", "Reparaturauftrag" } },
            new() { Name = "Materialschein", NumberKeywords = { "Materialscheinnummer", "Materialschein" } },
            new() { Name = "Auftrag", NumberKeywords = { "Auftragsnummer", "Auftragsnr", "Auftrag" } },
            new() { Name = "Auftragsbestätigung", NumberKeywords = { "Auftragsnummer", "Auftragsnr", "Auftragsbestätigung" } },
            new() { Name = "Schreiben", NumberKeywords = { "Aktenzeichen", "Zeichen", "Schreiben" } },
        };

        public static List<DocTypeDefinition> Items
        {
            get
            {
                lock (Sync)
                {
                    if (_items != null) return _items;
                    try
                    {
                        if (File.Exists(FilePath))
                            _items = JsonSerializer.Deserialize<List<DocTypeDefinition>>(SecureStorage.ReadAllText(FilePath));
                    }
                    catch { }
                    if (_items == null || _items.Count == 0)
                    {
                        _items = Defaults();
                        Save(_items);
                    }
                    else
                    {
                        var missing = Defaults().Where(d => !_items.Any(x => x.Name.Equals(d.Name, StringComparison.OrdinalIgnoreCase))).ToList();
                        if (missing.Count > 0)
                        {
                            _items.AddRange(missing);
                            Save(_items);
                        }
                    }
                    return _items;
                }
            }
        }

        public static List<string> Names => Items.Select(d => d.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();

        public static void Save(IEnumerable<DocTypeDefinition> items)
        {
            var list = items.Where(d => !string.IsNullOrWhiteSpace(d.Name)).ToList();
            lock (Sync)
            {
                _items = list;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                    SecureStorage.WriteAllText(FilePath, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
                }
                catch { }
            }
        }

        public static DocTypeDefinition? Get(string? nameOrSynonym)
        {
            if (string.IsNullOrWhiteSpace(nameOrSynonym)) return null;
            var t = nameOrSynonym.Trim();
            return Items.FirstOrDefault(d => d.Name.Equals(t, StringComparison.OrdinalIgnoreCase))
                ?? Items.FirstOrDefault(d => d.Synonyms.Any(s => s.Trim().Equals(t, StringComparison.OrdinalIgnoreCase)));
        }

        /// <summary>Umschreibung -> festgelegte Belegart (unbekannte Werte bleiben unver\u00E4ndert).</summary>
        public static string? Canonical(string? t) => Get(t)?.Name ?? t?.Trim();

        /// <summary>Belegarten-Bewertung aus Namen und Umschreibungen (Kopfbereich und kurze Zeilen z\u00E4hlen mehr).</summary>
        public static void AddScores(string[] lines, Dictionary<string, double> scores, ISet<string> builtIn) =>
            AddScores(new LineBlock(lines), scores, builtIn);

        private sealed record TermSet(string Culture, (string Type, string Word)[] Words, (string Type, Regex Rx, bool AllLines)[] Terms);
        private static TermSet? _terms;

        /// <summary>Suchbegriffe nur bei geänderten Belegarten/Umschreibungen neu aufbauen (statt bei jedem Beleg).</summary>
        private static (string Type, Regex Rx, bool AllLines)[] Terms(ISet<string> builtIn)
        {
            var words = new List<(string Type, string Word)>();
            foreach (var d in Items)
            {
                if (string.IsNullOrWhiteSpace(d.Name)) continue;
                var ws = d.Synonyms.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
                if (!builtIn.Contains(d.Name)) ws.Add(d.Name);
                foreach (var w in ws) words.Add((d.Name, w.Trim()));
            }
            var culture = System.Globalization.CultureInfo.CurrentCulture.Name;
            var cached = _terms;
            if (cached != null && cached.Culture == culture && cached.Words.SequenceEqual(words)) return cached.Terms;
            var terms = words.Select(w => (w.Type,
                new Regex(@"(?<!\p{L})" + Regex.Escape(w.Word) + @"(?!\p{L})", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                w.Word.IndexOfAny(new[] { LineBlock.Separator, '\r', '\n' }) >= 0)).ToArray();
            _terms = new TermSet(culture, words.ToArray(), terms);
            return terms;
        }

        internal static void AddScores(LineBlock block, Dictionary<string, double> scores, ISet<string> builtIn)
        {
            var terms = Terms(builtIn);
            if (terms.Length == 0) return;
            var lines = block.Lines;
            int n = terms.Length;
            var hit = new bool[lines.Length * n];
            for (int t = 0; t < n; t++)
            {
                if (terms[t].AllLines) block.MarkAll(hit, n, t);
                else block.Mark(terms[t].Rx, hit, n, t);
            }
            for (int i = 0; i < lines.Length; i++)
            {
                double pos = i < 30 ? 1.5 : 0.5;
                for (int t = 0; t < n; t++)
                {
                    if (!hit[i * n + t]) continue;
                    var (type, rx, _) = terms[t];
                    foreach (Match m in rx.Matches(lines[i]))
                        scores[type] = scores.GetValueOrDefault(type) + 4 * pos * DocumentFieldExtractor.MatchFactor(lines[i], m);
                }
            }
        }
    }
}
