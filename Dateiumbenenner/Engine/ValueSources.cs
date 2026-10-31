using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Dateiumbenenner.Engine
{
    public enum SourceField { Nummer, Datum, Betrag }

    /// <summary>Fundstelle eines Wertes im Belegtext: Zeile, Begriff im Umfeld und dessen Thema.</summary>
    public sealed record ValueHit(int Line, int Index, int Length, string LineText, string? Label, string Where, string Topic, int LabelIndex = -1, int LabelLength = 0)
    {
        /// <summary>Kurzbeschreibung, z. B. „Zeile 12: „Rechnungs-Nr.:“ links daneben → Rechnungsnummer“.</summary>
        public string Describe() => Label == null
            ? $"Zeile {Line}: kein Begriff im Umfeld"
            : $"Zeile {Line}: \u201E{Label}\u201C {Where} \u2192 {Topic}";
    }

    /// <summary>
    /// Sucht einen erkannten oder gewählten Wert (Nummer, Datum, Betrag) im Belegtext und ermittelt die Zeile
    /// sowie den Begriff davor bzw. darüber. Kommt der Wert mehrfach vor, gewinnt die Stelle mit passendem Begriff.
    /// </summary>
    public static class ValueSources
    {
        private static readonly CultureInfo De = new("de-DE");
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);

        private static readonly Regex DateRx = new(@"(?<!\d)(?<d>[0-3]?\d)\s*[./]\s*(?<m>[01]?\d)\s*[./]\s*(?<y>(?:19|20)?\d{2})(?!\d)", RegexOptions.Compiled);
        private static readonly Regex IsoDateRx = new(@"(?<!\d)(?<y>(?:19|20)\d{2})-(?<m>[01]\d)-(?<d>[0-3]\d)(?!\d)", RegexOptions.Compiled);
        private static readonly Regex LongDateRx = new(@"(?<!\d)(?<d>[0-3]?\d)\.\s*(?<m>Januar|Jan|Februar|Feb|M\u00E4rz|Maerz|M\u00E4r|April|Apr|Mai|Juni|Jun|Juli|Jul|August|Aug|September|Sept|Sep|Oktober|Okt|November|Nov|Dezember|Dez)\.?\s*(?<y>(?:19|20)\d{2})(?!\d)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex AmountRx = new(@"(?<![\d,.])(-?\d{1,3}(?:\.\d{3})+,\d{2}|-?\d+,\d{2})(?!\d)", RegexOptions.Compiled);
        private static readonly Regex GapRx = new(@"\s{2,}", RegexOptions.Compiled);
        private static readonly Regex RunRx = new(@"\S+(?: \S+)*", RegexOptions.Compiled);

        private static readonly (string Topic, Regex Rx)[] NumberTopics = Topics(
            ("Rechnungsnummer", @"rechnungs?\s*-?\s*(nr|nummer|no)|\b(re|rg)\.?\s*-?\s*nr|invoice|\brechnung\b"),
            ("Gutschriftsnummer", @"gutschrift"),
            ("Lieferscheinnummer", @"lieferschein"),
            ("Auftragsnummer", @"auftrag|\border"),
            ("Bestellnummer", @"bestell"),
            ("Kundennummer", @"kunden|customer"),
            ("Angebotsnummer", @"angebot"),
            ("Belegnummer", @"beleg|dokument|document"),
            ("Nummer", @"\bnr\b|nr\.|nummer|\bno\b|#|num[eé]ro|número|\bnumber"));

        private static readonly (string Topic, Regex Rx)[] DateTopics = Topics(
            ("Rechnungsdatum", @"rechnungsdatum|invoice\s*date"),
            ("Belegdatum", @"belegdatum|gutschriftsdatum"),
            ("Lieferdatum", @"liefer"),
            ("Leistungsdatum", @"leistung"),
            ("Bestelldatum", @"bestell|auftrag"),
            ("F\u00E4lligkeit", @"f\u00E4llig|faellig|zahlbar"),
            ("Datum", @"datum|date|\bvom\b|\bam\b"));

        private static readonly (string Topic, Regex Rx)[] AmountTopics = Topics(
            ("Gesamtbetrag", @"gesamtbetrag|gesamtsumme|endbetrag|endsumme|rechnungsbetrag|bruttobetrag|brutto|zahlbetrag|zu\s+zahlen|total"),
            ("Nettobetrag", @"netto|warenwert|zwischensumme"),
            ("MwSt.", @"mwst|ust|umsatzsteuer|mehrwertsteuer"),
            ("Summe", @"summe|gesamt|betrag"));

        private static (string, Regex)[] Topics(params (string Topic, string Pattern)[] list) =>
            list.Select(t => (t.Topic, new Regex(t.Pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled))).ToArray();

        private static (string Topic, Regex Rx)[] TopicsOf(SourceField f) =>
            f == SourceField.Nummer ? NumberTopics : f == SourceField.Datum ? DateTopics : AmountTopics;

        /// <summary>Gehört das Thema zum Feld (z. B. „Rechnungsnummer“ zu Nummer)?</summary>
        public static bool IsTopicOf(SourceField field, string? topic) => topic != null && TopicsOf(field).Any(t => t.Topic == topic);

        /// <summary>Thema eines Begriffs (z. B. „Rg.-Nr.:“ → Rechnungsnummer); zuerst passend zum Feld, sonst aus den anderen Feldern.</summary>
        public static string? Classify(SourceField field, string? label)
        {
            if (string.IsNullOrWhiteSpace(label)) return null;
            foreach (var (topic, rx) in TopicsOf(field)) if (rx.IsMatch(label)) return topic;
            foreach (var f in new[] { SourceField.Nummer, SourceField.Datum, SourceField.Betrag }.Where(f => f != field))
                foreach (var (topic, rx) in TopicsOf(f)) if (rx.IsMatch(label)) return topic;
            return null;
        }

        public static ValueHit? Locate(SourceField field, string? text, string? value)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrWhiteSpace(value)) return null;
            List<(int Index, int Length)> hits;
            try { hits = Find(field, text, value.Trim()); }
            catch { return null; }
            if (hits.Count == 0) return null;
            var lineStarts = new List<int> { 0 };
            for (int i = 0; i < text.Length; i++) if (text[i] == '\n') lineStarts.Add(i + 1);
            var fieldTopics = TopicsOf(field).Select(t => t.Topic).ToHashSet();
            ValueHit? best = null;
            int bestScore = int.MinValue;
            foreach (var (idx, len) in hits)
            {
                var src = Describe(field, text, lineStarts, idx, len);
                int score = src.Label == null ? 0 : fieldTopics.Contains(src.Topic) ? 2 : 1;
                // Gleichwertig: Beträge stehen als Summe meist unten, Nummer/Datum im Kopf
                if (score > bestScore || (score == bestScore && field == SourceField.Betrag)) { best = src; bestScore = score; }
            }
            return best;
        }

        private static List<(int, int)> Find(SourceField field, string text, string value)
        {
            var result = new List<(int, int)>();
            switch (field)
            {
                case SourceField.Nummer:
                {
                    var chars = value.Where(c => !char.IsWhiteSpace(c)).ToList();
                    if (chars.Count == 0) break;
                    // Leerzeichen innerhalb der Nummer und typische OCR-Verwechslungen (0/O, 1/I/l) zulassen
                    var sb = new StringBuilder(@"(?<![\p{L}\d])");
                    for (int i = 0; i < chars.Count; i++)
                    {
                        if (i > 0) sb.Append(@"[ \t]{0,2}");
                        sb.Append(chars[i] switch
                        {
                            '0' or 'O' or 'o' => "[0Oo]",
                            '1' or 'I' or 'l' => "[1Il]",
                            var c => Regex.Escape(c.ToString())
                        });
                    }
                    sb.Append(@"(?![\p{L}\d])");
                    foreach (Match m in Regex.Matches(text, sb.ToString(), RegexOptions.IgnoreCase, Timeout)) result.Add((m.Index, m.Length));
                    break;
                }
                case SourceField.Datum:
                {
                    if (!TryParseDate(value, out var target)) break;
                    foreach (var rx in new[] { DateRx, IsoDateRx, LongDateRx })
                        foreach (Match m in rx.Matches(text))
                            if (TryDate(m, out var d) && d == target) result.Add((m.Index, m.Length));
                    var wanted = target.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
                    foreach (var h in DateRuleStore.FindDates(text, DateFormatKind.Monatsname))
                        if (h.Date == wanted && !result.Any(r => r.Item1 < h.Index + h.Length && h.Index < r.Item1 + r.Item2)) result.Add((h.Index, h.Length));
                    result.Sort((a, b) => a.Item1.CompareTo(b.Item1));
                    break;
                }
                case SourceField.Betrag:
                {
                    if (!TryParseAmount(value, out var target)) break;
                    foreach (Match m in AmountRx.Matches(text))
                        if (TryParseAmount(m.Groups[1].Value, out var a) && Math.Abs(a) == Math.Abs(target))
                            result.Add((m.Groups[1].Index, m.Groups[1].Length));
                    break;
                }
            }
            return result;
        }

        private static bool TryParseAmount(string s, out decimal d)
        {
            s = s.Replace("\u20AC", "").Replace("EUR", "", StringComparison.OrdinalIgnoreCase).Replace(" ", "").Trim();
            // "834.79" (Punkt als Dezimaltrenner) ebenso wie "1.234,56"
            int sep = s.LastIndexOfAny(new[] { ',', '.' });
            if (sep >= 0 && s[sep] == '.' && s.Length - sep - 1 is 1 or 2 && s.IndexOf(',') < 0)
                s = s.Replace(".", ",");
            return decimal.TryParse(s, NumberStyles.Number, De, out d);
        }

        private static bool TryParseDate(string s, out DateTime d) =>
            DateTime.TryParseExact(s.Trim(), new[] { "dd.MM.yyyy", "d.M.yyyy", "dd.MM.yy", "d.M.yy", "yyyy-MM-dd", "dd/MM/yyyy" }, De, DateTimeStyles.None, out d)
            || DateTime.TryParse(s.Trim(), De, DateTimeStyles.None, out d);

        private static bool TryDate(Match m, out DateTime date)
        {
            date = default;
            if (!int.TryParse(m.Groups["d"].Value, out var d) || !int.TryParse(m.Groups["y"].Value, out var y)) return false;
            var ms = m.Groups["m"].Value;
            int mo;
            if (!int.TryParse(ms, out mo))
            {
                var k = ms.ToLowerInvariant().Replace("ae", "\u00E4");
                mo = Array.FindIndex(new[] { "jan", "feb", "m\u00E4r", "apr", "mai", "jun", "jul", "aug", "sep", "okt", "nov", "dez" }, p => k.StartsWith(p)) + 1;
            }
            if (y < 100) y += 2000;
            if (mo < 1 || mo > 12 || d < 1 || y < 1900 || y > 2099 || d > DateTime.DaysInMonth(y, mo)) return false;
            date = new DateTime(y, mo, d);
            return true;
        }

        private static string LineAt(string text, List<int> lineStarts, int line)
        {
            int s = lineStarts[line];
            int e = line + 1 < lineStarts.Count ? lineStarts[line + 1] - 1 : text.Length;
            return text.Substring(s, Math.Max(0, e - s)).TrimEnd('\r').Replace('\t', ' ');
        }

        private static ValueHit Describe(SourceField field, string text, List<int> lineStarts, int idx, int len)
        {
            int line = lineStarts.BinarySearch(idx);
            if (line < 0) line = ~line - 1;
            var lineText = LineAt(text, lineStarts, line);
            int col = Math.Min(idx - lineStarts[line], lineText.Length);

            // 1. Begriff links daneben (bis zur letzten Spaltenlücke, ohne vorangehende Werte)
            var label = LeftLabel(lineText.Substring(0, col));
            var where = "links daneben";
            int labelLine = line;
            // 2. Begriff darüber (gleiche Spalte, bis zu zwei Zeilen)
            if (label == null)
            {
                for (int p = line - 1, seen = 0; p >= 0 && seen < 2 && label == null; p--)
                {
                    var above = LineAt(text, lineStarts, p);
                    if (above.Trim().Length == 0) continue;
                    seen++;
                    label = AboveLabel(above, col, len);
                    labelLine = p;
                }
                where = "dar\u00FCber";
            }
            // 3. Begriff rechts daneben, nur wenn er ein Thema hat (z. B. „4711 Rechnungsnummer“)
            if (label == null)
            {
                int end = Math.Min(col + len, lineText.Length);
                var right = RunRx.Match(lineText.Substring(end).TrimStart());
                if (right.Success && right.Value.Any(char.IsLetter) && Classify(field, right.Value) != null)
                {
                    label = Shorten(right.Value, fromEnd: false);
                    where = "rechts daneben";
                    labelLine = line;
                }
            }
            int labelIdx = -1;
            if (label != null)
            {
                int c = NearestIndex(LineAt(text, lineStarts, labelLine), label, col);
                if (c >= 0) labelIdx = lineStarts[labelLine] + c;
            }
            return new ValueHit(line + 1, idx, len, lineText.Trim(), label, label == null ? "" : where, Classify(field, label) ?? "anderes",
                labelIdx, labelIdx < 0 ? 0 : label!.Length);
        }

        /// <summary>Vorkommen des Begriffs in der Zeile, das der Spalte des Wertes am nächsten liegt.</summary>
        private static int NearestIndex(string line, string label, int col)
        {
            int best = -1;
            for (int i = line.IndexOf(label, StringComparison.Ordinal); i >= 0; i = line.IndexOf(label, i + 1, StringComparison.Ordinal))
                if (best < 0 || Math.Abs(i - col) < Math.Abs(best - col)) best = i;
            return best;
        }

        private static string? LeftLabel(string left)
        {
            left = left.TrimEnd();
            if (left.Length == 0) return null;
            var parts = GapRx.Split(left);
            var seg = parts[^1];
            return seg.Any(char.IsLetter) ? Shorten(seg, fromEnd: true) : null;
        }

        private static string? AboveLabel(string above, int col, int len)
        {
            Match? best = null;
            int bestDist = int.MaxValue;
            foreach (Match r in RunRx.Matches(above))
            {
                if (!r.Value.Any(char.IsLetter)) continue;
                int s = r.Index, e = r.Index + r.Length;
                // Überlappung mit der Spalte des Wertes = Abstand 0, sonst Zeichenabstand
                int dist = e <= col ? col - e : s >= col + len ? s - (col + len) : 0;
                if (dist < bestDist) { bestDist = dist; best = r; }
            }
            return best != null && bestDist <= 8 ? Shorten(best.Value, fromEnd: true) : null;
        }

        /// <summary>Höchstens fünf Wörter; Wörter mit Ziffern ohne Buchstaben (vorangehende Werte) abschneiden.</summary>
        private static string? Shorten(string seg, bool fromEnd)
        {
            var tokens = seg.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
            var keep = new List<string>();
            var order = fromEnd ? Enumerable.Range(0, tokens.Count).Reverse() : Enumerable.Range(0, tokens.Count);
            foreach (var i in order)
            {
                var t = tokens[i];
                if (t.Any(char.IsDigit) && !t.Any(char.IsLetter)) break;
                keep.Add(t);
                if (keep.Count >= 5) break;
            }
            if (fromEnd) keep.Reverse();
            var s = string.Join(" ", keep).Trim();
            return s.Any(char.IsLetter) ? s : null;
        }
    }
}
