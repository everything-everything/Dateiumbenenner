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
    public enum DatePosition { Beliebig, Rechts, Darunter, Links }

    /// <summary>Schreibweise des Datums im Beleg (entspricht den Archiv-Funktionen Datum, DatumJan, DatumJMT, DatumUS, DatumFrei).</summary>
    public enum DateFormatKind { Beliebig, Numerisch, Monatsname, JahrMonatTag, US, Frei }

    /// <summary>Datumsregel je Firma + Belegart ("*" = alle): Begriff, bei dem das Belegdatum steht, und dessen Schreibweise.</summary>
    public class DateRule
    {
        public string Company { get; set; } = "*";
        public string DocType { get; set; } = "*";
        /// <summary>Begriff beim Datum (z. B. Rechnungsdatum, Lieferdatum); leer = nur Schreibweise festlegen.</summary>
        public string Keyword { get; set; } = "";
        public DatePosition Position { get; set; } = DatePosition.Beliebig;
        public DateFormatKind Format { get; set; } = DateFormatKind.Beliebig;
        /// <summary>Eigene Maske bei Format „Frei“: T/M/J = Ziffer, ? = Trennzeichen, sonst wörtlich (z. B. TT?MM?JJ).</summary>
        public string? Mask { get; set; }
        public int Priority { get; set; } = 10;
    }

    public static class DateRuleStore
    {
        private static List<DateRule>? _rules;
        private static readonly object Sync = new();
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

        private static readonly Regex NumericRx = new(@"(?<!\d)(?<d>[0-3]?\d)\s?[./-]\s?(?<m>[01]?\d)\s?[./-]\s?(?<y>(?:19|20)?\d{2})(?!\d)", RegexOptions.Compiled);
        // Monatsnamen auch OCR-tolerant (0 statt O: 0kt, 0ct, N0v); Trennzeichen optional (23Okt25, 25-Dez-23, 23. Okt. 2025)
        private const string MonthAlt = @"Jan(?:uar)?|January|Feb(?:ruar)?|February|M(?:ä|ae)rz|Mär|Mrz|March|Mar|Apr(?:il)?|Mai|May|Jun(?:i|e)?|Jul(?:i|y)?|Aug(?:ust)?|Sep(?:t(?:ember)?)?|[O0]kt(?:[o0]ber)?|[O0]ct(?:[o0]ber)?|N[o0]v(?:ember)?|Dez(?:ember)?|Dec(?:ember)?";
        private const string MonthSep = @"[ \t\u00A0]*[./-]?[ \t\u00A0]*";
        private static readonly Regex MonthRx = new(@"(?<!\d)(?<d>[0-3]?\d)" + MonthSep + "(?<mn>" + MonthAlt + @")(?!\p{L})" + MonthSep + @"(?<y>(?:19|20)?\d{2})(?!\d)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        /// <summary>Jahr zuerst mit Monatsname, z. B. 2025-MAY-10.</summary>
        private static readonly Regex YearMonthRx = new(@"(?<!\d)(?<y>(?:19|20)\d{2})" + MonthSep + "(?<mn>" + MonthAlt + @")(?!\p{L})" + MonthSep + @"(?<d>[0-3]?\d)(?!\d)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex YmdRx = new(@"(?<!\d)(?<y>(?:19|20)\d{2})[./-](?<m>[01]?\d)[./-](?<d>[0-3]?\d)(?!\d)", RegexOptions.Compiled);
        private static readonly Regex UsRx = new(@"(?<!\d)(?<m>0?[1-9]|1[0-2])/(?<d>0?[1-9]|[12]\d|3[01])/(?<y>(?:19|20)?\d{2})(?!\d)", RegexOptions.Compiled);

        /// <summary>Muster für Daten mit Monatsnamen (zur Vorauswahl von Zeilen).</summary>
        internal static readonly Regex[] MonthNameRegexes = { MonthRx, YearMonthRx };

        public static string FilePath => Path.Combine(Plugins.PluginManager.BaseDirectory, "date_rules.json");

        public static List<DateRule> Rules
        {
            get
            {
                lock (Sync)
                {
                    if (_rules != null) return _rules;
                    try
                    {
                        if (File.Exists(FilePath))
                            _rules = JsonSerializer.Deserialize<List<DateRule>>(SecureStorage.ReadAllText(FilePath), JsonOptions);
                    }
                    catch { }
                    return _rules ??= new List<DateRule>();
                }
            }
        }

        public static void Save(IEnumerable<DateRule> rules)
        {
            var list = rules.Where(r => r != null && (!string.IsNullOrWhiteSpace(r.Keyword) || r.Format != DateFormatKind.Beliebig)).ToList();
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

        /// <summary>Passende Regeln, die spezifischsten (Firma + Belegart) zuerst.</summary>
        public static List<(DateRule Rule, double Specificity)> RulesFor(string? company, string? docType, IEnumerable<DateRule>? rules = null)
        {
            var dt = DocTypeStore.Canonical(docType);
            return (rules ?? Rules)
                .Where(r => (IsAny(r.Company) || Eq(r.Company, company)) && (IsAny(r.DocType) || Eq(DocTypeStore.Canonical(r.DocType), dt)))
                .Select(r => (Rule: r, Spec: !IsAny(r.Company) && !IsAny(r.DocType) ? 1.0 : !IsAny(r.Company) ? 0.8 : !IsAny(r.DocType) ? 0.7 : 0.5))
                .OrderByDescending(x => x.Spec).ThenByDescending(x => x.Rule.Priority)
                .ToList();
        }

        /// <summary>Festgelegte Schreibweise für Firma + Belegart (spezifischste Regel mit Format), sonst null.</summary>
        public static DateRule? FormatFor(string? company, string? docType, IEnumerable<DateRule>? rules = null) =>
            RulesFor(company, docType, rules).Select(x => x.Rule).FirstOrDefault(r => r.Format != DateFormatKind.Beliebig);

        /// <summary>Archiv-Funktion zur Schreibweise: Datum, DatumJan, DatumJMT, DatumUS, DatumFrei(Maske); null = keine Festlegung.</summary>
        public static string? ArchiveFunction(DateRule? r) => r?.Format switch
        {
            DateFormatKind.Numerisch => "Datum",
            DateFormatKind.Monatsname => "DatumJan",
            DateFormatKind.JahrMonatTag => "DatumJMT",
            DateFormatKind.US => "DatumUS",
            DateFormatKind.Frei when !string.IsNullOrWhiteSpace(r.Mask) => $"DatumFrei({r.Mask.Trim()})",
            _ => null
        };

        /// <summary>Archiv-Funktion für Firma + Belegart (für XML-Erzeugung und XML-Editor).</summary>
        public static string? ArchiveFunction(string? company, string? docType) => ArchiveFunction(FormatFor(company, docType));

        /// <summary>Schreibweise eines Datums im Text erkennen (für Vorschläge).</summary>
        public static DateFormatKind DetectFormat(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return DateFormatKind.Beliebig;
            if (MonthRx.IsMatch(raw) || YearMonthRx.IsMatch(raw)) return DateFormatKind.Monatsname;
            if (YmdRx.IsMatch(raw)) return DateFormatKind.JahrMonatTag;
            if (NumericRx.IsMatch(raw)) return DateFormatKind.Numerisch;
            if (UsRx.IsMatch(raw)) return DateFormatKind.US;
            return DateFormatKind.Beliebig;
        }

        /// <summary>
        /// Alle Daten einer Zeile in der Schreibweise (Beliebig = alle bekannten), normalisiert auf TT.MM.JJJJ.
        /// Mehrdeutige Kurzformen (25Oct23 = 25.10.2023 oder 23.10.2025) liefern beide Lesarten an gleicher Position;
        /// die zweite hat Alt = true (weiterer Vorschlag).
        /// </summary>
        public static List<(int Index, int Length, string Raw, string Date, bool Alt)> FindDates(string line, DateFormatKind format = DateFormatKind.Beliebig, string? mask = null)
        {
            var list = new List<(int, int, string, string, bool)>();
            if (string.IsNullOrEmpty(line)) return list;
            void AddAll(Regex rx)
            {
                foreach (Match m in rx.Matches(line))
                {
                    if (Normalize(m) is not { } d || list.Any(x => x.Item1 == m.Index)) continue;
                    var alt = YearFirstReading(m, d);
                    // Englische Monatsnamen: Jahr zuerst (25Oct23 = 23.10.2025), die europäische Lesart als weiterer Vorschlag
                    if (alt != null && IsEnglishMonth(m.Groups["mn"].Value)) (d, alt) = (alt, d);
                    // Eine Lesart weit in der Zukunft (31Dec05 ≠ 05.12.2031) ist unwahrscheinlich
                    if (alt != null && IsFarFuture(d) && !IsFarFuture(alt)) (d, alt) = (alt, d);
                    list.Add((m.Index, m.Length, m.Value, d, false));
                    if (alt != null) list.Add((m.Index, m.Length, m.Value, alt, true));
                }
            }
            switch (format)
            {
                case DateFormatKind.Numerisch: AddAll(NumericRx); break;
                case DateFormatKind.Monatsname: AddAll(MonthRx); AddAll(YearMonthRx); break;
                case DateFormatKind.JahrMonatTag: AddAll(YmdRx); break;
                case DateFormatKind.US: AddAll(UsRx); break;
                case DateFormatKind.Frei:
                    foreach (var alt in (mask ?? "").Split('/').Where(a => a.Trim().Length > 0)) AddAll(MaskRegex(alt.Trim()));
                    break;
                default:
                    AddAll(MonthRx); AddAll(YearMonthRx); AddAll(YmdRx); AddAll(NumericRx);
                    break;
            }
            return list.OrderBy(x => x.Item1).ToList();
        }

        /// <summary>Freie Maske: TT/MM/JJ(JJ) als Tag/Monat/Jahr, $ = Ziffer, ? = Trennzeichen, sonst wörtlich.</summary>
        private static Regex MaskRegex(string mask)
        {
            var sb = new System.Text.StringBuilder("(?<!\\d)");
            var used = new HashSet<char>();
            for (int i = 0; i < mask.Length;)
            {
                char c = char.ToUpperInvariant(mask[i]);
                int n = 1;
                while (i + n < mask.Length && char.ToUpperInvariant(mask[i + n]) == c) n++;
                string? group = c switch { 'T' or 'D' => "d", 'M' => "m", 'J' or 'Y' => "y", _ => null };
                if (group != null && used.Add(c)) sb.Append($"(?<{group}>\\d{{{n}}})");
                else if (group != null || c == '$') sb.Append($"\\d{{{n}}}");
                else if (c == '?') sb.Append($"[^\\s\\p{{L}}\\d]{{{n}}}");
                else if (c == ' ') sb.Append("\\s+");
                else sb.Append(Regex.Escape(mask.Substring(i, n)));
                i += n;
            }
            sb.Append("(?!\\d)");
            return new Regex(sb.ToString(), RegexOptions.IgnoreCase);
        }

        private static readonly string[][] MonthNames =
        {
            new[] { "jan" }, new[] { "feb" }, new[] { "mär", "mae", "mrz", "mar" }, new[] { "apr" }, new[] { "mai", "may" }, new[] { "jun" },
            new[] { "jul" }, new[] { "aug" }, new[] { "sep" }, new[] { "okt", "oct" }, new[] { "nov" }, new[] { "dez", "dec" }
        };

        private static int MonthOf(string name)
        {
            var n = name.ToLowerInvariant().Replace('0', 'o');
            return Array.FindIndex(MonthNames, a => a.Any(p => n.StartsWith(p, StringComparison.Ordinal))) + 1;
        }

        private static readonly HashSet<string> EnglishOnlyMonths = new(StringComparer.Ordinal) { "january", "february", "march", "mar", "may", "june", "july" };

        private static bool IsEnglishMonth(string name)
        {
            var n = name.ToLowerInvariant().Replace('0', 'o');
            return n.StartsWith("oct", StringComparison.Ordinal) || n.StartsWith("dec", StringComparison.Ordinal) || EnglishOnlyMonths.Contains(n);
        }

        private static bool IsFarFuture(string date) =>
            DateTime.TryParseExact(date, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) && dt > DateTime.Today.AddYears(1);

        private static string? Format(int y, int mo, int d)
        {
            if (y < 100) y += 2000;
            if (y < 1900 || y > 2100 || mo < 1 || mo > 12 || d < 1 || d > DateTime.DaysInMonth(y, mo)) return null;
            return new DateTime(y, mo, d).ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        }

        private static string? Normalize(Match m)
        {
            if (!int.TryParse(m.Groups["d"].Value, out var d) || !int.TryParse(m.Groups["y"].Value, out var y)) return null;
            int mo;
            if (m.Groups["mn"].Success) mo = MonthOf(m.Groups["mn"].Value);
            else if (!int.TryParse(m.Groups["m"].Value, out mo)) return null;
            return Format(y, mo, d);
        }

        /// <summary>
        /// Zweite Lesart einer Kurzform mit Monatsname und je zwei Ziffern (JJ-Mon-TT statt TT-Mon-JJ), z. B. 25Oct23 = 23.10.2025.
        /// Nicht bei „23. Okt 25“ (Punkt = Tag) oder vierstelligem Jahr; null, wenn ungültig oder gleich.
        /// </summary>
        private static string? YearFirstReading(Match m, string primary)
        {
            var dg = m.Groups["d"]; var yg = m.Groups["y"]; var mn = m.Groups["mn"];
            if (!mn.Success || dg.Length != 2 || yg.Length != 2 || dg.Index > mn.Index) return null;
            var between = m.Value.Substring(dg.Index + dg.Length - m.Index, mn.Index - dg.Index - dg.Length);
            if (between.Contains('.')) return null;
            var alt = Format(int.Parse(dg.Value), MonthOf(mn.Value), int.Parse(yg.Value));
            return alt == primary ? null : alt;
        }

        /// <summary>
        /// Datumskandidaten nach Regeln bewerten: Daten beim Begriff (und in der festgelegten Schreibweise) nach vorne.
        /// Regeln ohne Begriff werten nur Daten in der festgelegten Schreibweise auf. Rückgabe: bester Regeltreffer oder null.
        /// </summary>
        public static string? Apply(Field date, string? company, string? docType, string? text, IEnumerable<DateRule>? rules = null)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var section = AmountRuleStore.SectionFor(text, company);
            var lines = section.Split(new[] { "\r\n", "\n", "\r", "\f" }, StringSplitOptions.None);
            string? best = null; double bestScore = double.MinValue;
            foreach (var (r, spec) in RulesFor(company, docType, rules))
            {
                var found = string.IsNullOrWhiteSpace(r.Keyword)
                    ? lines.SelectMany(l => FindDates(l, r.Format, r.Mask)).Where(x => !x.Alt).Select(x => x.Date).Distinct().ToList()
                    : NearKeyword(lines, r).Distinct().ToList();
                double baseScore = string.IsNullOrWhiteSpace(r.Keyword) ? 20 : 50;
                for (int i = 0; i < found.Count; i++)
                {
                    double score = baseScore + r.Priority * spec - i * 0.01;
                    date.Add(found[i], score, string.IsNullOrWhiteSpace(r.Keyword) ? "Datumsformat" : "Begriff " + r.Keyword.Trim());
                    if (score > bestScore) { bestScore = score; best = found[i]; }
                }
            }
            return best;
        }

        /// <summary>Daten beim Begriff: rechts daneben (ohne weiteres Wort dazwischen), links davor bzw. darunter (Begriffsspalte, nächste 3 Zeilen).</summary>
        private static IEnumerable<string> NearKeyword(string[] lines, DateRule r)
        {
            var kwRx = new Regex(@"(?<!\p{L})" + Regex.Escape(r.Keyword.Trim()).Replace("\\ ", "\\s*") + @"(?!\p{L})", RegexOptions.IgnoreCase);
            for (int i = 0; i < lines.Length; i++)
            {
                foreach (Match k in kwRx.Matches(lines[i]))
                {
                    if (r.Position is DatePosition.Beliebig or DatePosition.Rechts)
                    {
                        var rest = lines[i].Substring(k.Index + k.Length);
                        var hit = FindDates(rest, r.Format, r.Mask).FirstOrDefault(x => !x.Alt);
                        if (hit.Raw != null && hit.Index <= 40 && !Regex.IsMatch(rest.Substring(0, hit.Index), @"\p{L}{3,}"))
                        {
                            yield return hit.Date;
                            continue;
                        }
                    }
                    if (r.Position is DatePosition.Beliebig or DatePosition.Links)
                    {
                        var head = lines[i].Substring(0, k.Index);
                        var hit = FindDates(head, r.Format, r.Mask).LastOrDefault(x => !x.Alt);
                        if (hit.Raw != null && !Regex.IsMatch(head.Substring(hit.Index + hit.Length), @"\p{L}{3,}"))
                        {
                            yield return hit.Date;
                            continue;
                        }
                    }
                    if (r.Position is DatePosition.Beliebig or DatePosition.Darunter)
                    {
                        int left = k.Index, right = k.Index + k.Length;
                        for (int j = i + 1; j < Math.Min(lines.Length, i + 4); j++)
                        {
                            var hit = FindDates(lines[j], r.Format, r.Mask)
                                .Where(x => !x.Alt && x.Index <= right + 6 && x.Index + x.Length >= left - 6)
                                .OrderBy(x => Math.Abs(x.Index + x.Length / 2.0 - (left + right) / 2.0)).FirstOrDefault();
                            if (hit.Raw != null) { yield return hit.Date; break; }
                        }
                    }
                }
            }
        }
    }
}
