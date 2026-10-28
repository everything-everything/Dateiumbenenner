using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Dateiumbenenner.Engine
{
    public enum AmountPosition { Beliebig, Rechts, Darunter, Links, Umschlossen }
    public enum AmountPage { Beliebig, Erste, Letzte }

    /// <summary>Betragsregel je Firma + Belegart ("*" = alle): Begriff, bei dem der Betrag bevorzugt steht.</summary>
    public class AmountRule
    {
        public string Company { get; set; } = "*";
        public string DocType { get; set; } = "*";
        public string Keyword { get; set; } = "";
        public AmountPosition Position { get; set; } = AmountPosition.Beliebig;
        /// <summary>Zweiter Begriff hinter dem Betrag (nur bei "Umschlossen", darf leer sein).</summary>
        public string? KeywordEnd { get; set; }
        public AmountPage Page { get; set; } = AmountPage.Beliebig;
        public int Priority { get; set; } = 10;
    }

    public static class AmountRuleStore
    {
        private static List<AmountRule>? _rules;
        private static readonly object Sync = new();
        private static readonly CultureInfo De = new("de-DE");
        private static readonly Regex AmountRx = new(@"(?<![\d,.])(\d{1,3}(?:\.\d{3})+,\d{2}|\d+,\d{2})(?!\d)", RegexOptions.Compiled);
        /// <summary>Auch "30 Euro" / "30 €" (ohne Nachkommastellen) beim Begriff.</summary>
        private static readonly Regex LooseRx = new(@"(?<![\d,.])(\d{1,3}(?:\.\d{3})+,\d{2}|\d+,\d{2}|\d+(?=\s*(?:€|EUR\b|Euro\b)))(?![\d,])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

        public static string FilePath => Path.Combine(Plugins.PluginManager.BaseDirectory, "amount_rules.json");

        public static List<AmountRule> Rules
        {
            get
            {
                lock (Sync)
                {
                    if (_rules != null) return _rules;
                    try
                    {
                        if (File.Exists(FilePath))
                            _rules = JsonSerializer.Deserialize<List<AmountRule>>(SecureStorage.ReadAllText(FilePath), JsonOptions);
                    }
                    catch { }
                    return _rules ??= new List<AmountRule>();
                }
            }
        }

        public static void Save(IEnumerable<AmountRule> rules)
        {
            var list = rules.Where(r => r != null && !string.IsNullOrWhiteSpace(r.Keyword)).ToList();
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
        private static decimal Parse(string s) => decimal.TryParse(s, NumberStyles.Number, De, out var d) ? d : 0m;

        /// <summary>
        /// Textbereich des Belegs der gew\u00E4hlten Firma: bei mehreren Belegen in einer Datei nur die Seiten (\f),
        /// auf denen die Firma vorkommt. Ohne Treffer bleibt der ganze Text.
        /// </summary>
        public static string SectionFor(string text, string? company)
        {
            var pages = text.Split('\f');
            if (pages.Length < 2 || string.IsNullOrWhiteSpace(company)) return text;
            var key = company.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            if (key.Length < 3) return text;
            var hits = pages.Select((p, i) => (p, i)).Where(x => x.p.Contains(key, StringComparison.OrdinalIgnoreCase)).Select(x => x.i).ToList();
            if (hits.Count == 0 || hits.Count == pages.Length) return text;
            // Folgeseiten ohne Firmennamen geh\u00F6ren zum vorherigen Beleg
            var keep = new List<string>();
            for (int i = 0; i < pages.Length; i++)
            {
                int owner = hits.LastOrDefault(h => h <= i, -1);
                if (owner < 0) continue;
                int next = hits.FirstOrDefault(h => h > owner, pages.Length);
                if (i < next) keep.Add(pages[i]);
            }
            return string.Join("\f", keep);
        }

        /// <summary>Betragskandidaten nach Regeln bewerten. Ohne Regel bleibt die bisherige Bewertung (gr\u00F6\u00DFter Betrag zuerst).</summary>
        public static void Apply(Field amount, string? company, string? docType, string? text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var dt = DocTypeStore.Canonical(docType);
            var rules = Rules.Where(r => !string.IsNullOrWhiteSpace(r.Keyword)
                    && (IsAny(r.Company) || Eq(r.Company, company))
                    && (IsAny(r.DocType) || Eq(DocTypeStore.Canonical(r.DocType), dt)))
                .Select(r => (Rule: r, Spec: !IsAny(r.Company) && !IsAny(r.DocType) ? 1.0 : !IsAny(r.Company) ? 0.8 : !IsAny(r.DocType) ? 0.7 : 0.5))
                .ToList();

            var section = SectionFor(text, company);
            if (section.Length != text.Length)
            {
                // Betr\u00E4ge au\u00DFerhalb des Belegs der gew\u00E4hlten Firma abwerten (direkt ersetzen: Field.Add \u00FCbernimmt nur h\u00F6here Bewertungen)
                var inside = new HashSet<string>(AmountRx.Matches(section).Select(m => m.Groups[1].Value));
                for (int i = 0; i < amount.Candidates.Count; i++)
                    if (amount.Candidates[i] is { Score: >= 0 } c && !inside.Contains(c.Value)
                        && !(c.Value.Contains('.') && section.Contains(c.Value.Replace('.', ' '))))
                        amount.Candidates[i] = c with { Score = Math.Max(0, c.Score - 2.0) };
                foreach (var v in inside.OrderByDescending(Parse).Select((v, i) => (v, i)))
                    amount.Add(v.v, 1.5 - v.i * 0.01, "Beleg der Firma");
            }

            foreach (var (r, spec) in rules)
            {
                var pages = section.Split('\f');
                IEnumerable<string> scope = r.Page switch
                {
                    AmountPage.Erste => new[] { pages[0] },
                    AmountPage.Letzte => new[] { pages[^1] },
                    _ => pages
                };
                var found = scope.SelectMany(p => NearKeyword(p, r)).Distinct().OrderByDescending(Parse).ToList();
                for (int i = 0; i < found.Count; i++)
                    amount.Add(found[i], 50 + r.Priority * spec - i * 0.01, "Begriff " + r.Keyword.Trim());
            }

            // Gelernte Fundstelle (Firma + Belegart): Begriff davor/dahinter, wievielter Betrag, Lage im Beleg
            ValueLocationStore.Apply(amount, ValueKind.Betrag, company, docType, text);
        }

        /// <summary>Betr\u00E4ge beim Begriff: rechts daneben (gleiche Spalte bis zum n\u00E4chsten Begriff) bzw. in den Folgezeilen unter der Begriffsspalte.</summary>
        private static IEnumerable<string> NearKeyword(string page, AmountRule r)
        {
            var lines = page.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            var kwRx = new Regex(@"(?<!\p{L})" + Regex.Escape(r.Keyword.Trim()) + @"(?!\p{L})", RegexOptions.IgnoreCase);
            for (int i = 0; i < lines.Length; i++)
            {
                foreach (Match k in kwRx.Matches(lines[i]))
                {
                    if (r.Position == AmountPosition.Umschlossen)
                    {
                        // Zwischen Begriff und Begriff Ende (gleiche Zeile oder Folgezeile)
                        var tail = lines[i].Substring(k.Index + k.Length) + (i + 1 < lines.Length ? "\n" + lines[i + 1] : "");
                        if (!string.IsNullOrWhiteSpace(r.KeywordEnd))
                        {
                            var e = Regex.Match(tail, @"(?<!\p{L})" + Regex.Escape(r.KeywordEnd.Trim()) + @"(?!\p{L})", RegexOptions.IgnoreCase);
                            if (!e.Success) continue;
                            tail = tail.Substring(0, e.Index);
                        }
                        var u = LooseRx.Match(tail);
                        if (u.Success) yield return u.Groups[1].Value;
                        continue;
                    }
                    if (r.Position is AmountPosition.Links or AmountPosition.Beliebig)
                    {
                        // Links: letzter Betrag vor dem Begriff, ohne weiteres Wort dazwischen
                        var head = lines[i].Substring(0, k.Index);
                        var l = LooseRx.Matches(head).Cast<Match>().LastOrDefault();
                        if (l != null && !Regex.IsMatch(head.Substring(l.Index + l.Length), @"(?!EUR\b|Euro\b)\p{L}{3,}", RegexOptions.IgnoreCase))
                        {
                            yield return l.Groups[1].Value;
                            if (r.Position == AmountPosition.Links) continue;
                        }
                        if (r.Position == AmountPosition.Links) continue;
                    }
                    if (r.Position is AmountPosition.Beliebig or AmountPosition.Rechts)
                    {
                        // Rechts: erster Betrag hinter dem Begriff, bevor ein weiterer Text-Begriff beginnt
                        var rest = lines[i].Substring(k.Index + k.Length);
                        var m = LooseRx.Match(rest);
                        if (m.Success && !Regex.IsMatch(rest.Substring(0, m.Index), @"(?!EUR\b|Euro\b)\p{L}{3,}", RegexOptions.IgnoreCase))
                        {
                            yield return m.Groups[1].Value;
                            continue;
                        }
                    }
                    if (r.Position is AmountPosition.Beliebig or AmountPosition.Darunter)
                    {
                        // Darunter: Betrag, der sich mit der Begriffsspalte \u00FCberschneidet (n\u00E4chste 3 Zeilen)
                        int left = k.Index, right = k.Index + k.Length;
                        for (int j = i + 1; j < Math.Min(lines.Length, i + 4); j++)
                        {
                            var best = LooseRx.Matches(lines[j]).Cast<Match>()
                                .Select(m => (m, d: Math.Abs((m.Index + m.Length / 2.0) - (left + right) / 2.0)))
                                .Where(x => x.m.Index <= right + 6 && x.m.Index + x.m.Length >= left - 6)
                                .OrderBy(x => x.d).FirstOrDefault();
                            if (best.m != null) { yield return best.m.Groups[1].Value; break; }
                        }
                    }
                }
            }
        }
    }
}
