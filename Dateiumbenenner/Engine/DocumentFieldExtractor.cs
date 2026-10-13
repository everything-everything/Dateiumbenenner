using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Dateiumbenenner.Engine
{
    /// <summary>
    /// Erkennung von Rechnungsnummer, Datum und Dokumenttyp.
    /// Arbeitet auf formatiertem OCR-Text (Zeichenraster): Werte dürfen neben ODER unter der Bezeichnung stehen.
    /// </summary>
    public static class DocumentFieldExtractor
    {
        // ---------- Nummer ----------
        // Bezeichnung + optional Wert in derselben Zeile
        private static readonly Regex NumberLabel = new(
            @"(?<label>Rechnungs?|Rg|Re|Invoice|Beleg|Gutschrifts?|Lieferschein|Dokument|Document|Auftrags?|Order|Kunden|Customer|Steuer|USt\.?\s*-?\s*ID|Bestell|Kassen)\.?\s*-?\s*(?:Nr\.?|nummer|No\.?|Number|#)\s*[:.#]?\s*(?<value>[A-Z0-9OIl][A-Z0-9OIl\-/]{2,})?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // "Nr. 12345" ohne Bezeichnung
        private static readonly Regex PlainNumber = new(
            @"(?<![A-Za-zäöü\-])(?:Nr\.?|Nummer)\s*[:.]?\s*(?<value>\d[\d\-/]{2,})",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex TokenAtStart = new(@"^\s*(?<value>[A-Z0-9OIl][A-Z0-9OIl\-/]{2,})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static double LabelWeight(string label)
        {
            label = label.ToLowerInvariant();
            if (label.StartsWith("rechnung") || label is "rg" or "re" || label.StartsWith("invoice")) return 5;
            if (label.StartsWith("document")) return 3;
            if (label.StartsWith("auftrag") || label.StartsWith("order")) return 1.5;   // nur Ersatz, wenn keine Rechnungs-Nr. da
            if (label.StartsWith("gutschrift")) return 5;
            if (label.StartsWith("beleg")) return 3;
            if (label.StartsWith("dokument")) return 3;
            if (label.StartsWith("lieferschein")) return 2.5;
            if (label.StartsWith("kassen")) return 2;
            return -1;   // Kunden-, Auftrags-, Steuer-, USt-, Bestell-Nr. -> keine Rechnungsnummer
        }

        /// <summary>Korrigiert typische OCR-Verwechslungen in überwiegend numerischen Werten.</summary>
        private static string FixDigits(string v)
        {
            int digits = v.Count(char.IsDigit);
            if (digits < v.Length / 2) return v;
            return v.Replace('O', '0').Replace('o', '0').Replace('I', '1').Replace('l', '1');
        }

        private static readonly Regex StopWord = new(@"^(vom|von|am|Datum|Date|Seite|Page|Kunden\w*|Rechnung\w*|Auftrag\w*|USt\w*)$|^\d{1,2}\.\d{1,2}\.\d{2,4}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Liest eine Nummer ab Position, auch mit Trennzeichen ("123-EDE |2 d32", "123 332 134 / 23 d").
        /// Ende bei Spaltenlücke (2+ Leerzeichen), Datum oder Stoppwort.</summary>
        internal static string? ReadValue(string line, int start)
        {
            if (start < 0 || start >= line.Length) return null;
            var rest = line.Substring(start);
            int gap = Regex.Match(rest, @"\s{2,}").Index;
            if (gap > 0) rest = rest.Substring(0, gap);
            var tokens = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var keep = new List<string>();
            foreach (var t in tokens)
            {
                if (StopWord.IsMatch(t)) break;
                bool sep = t.Trim('-', '/', '|', ':', '.').Length == 0;
                bool code = t.Any(char.IsDigit) || (t.Length <= 3 && t.All(char.IsLetterOrDigit) && keep.Count > 0);
                if (!sep && !code && !t.Contains('-') && !t.Contains('|')) break;
                keep.Add(t);
                if (keep.Count >= 8) break;
            }
            while (keep.Count > 0 && keep[^1].Trim('-', '/', '|', ':', '.').Length == 0) keep.RemoveAt(keep.Count - 1);
            return keep.Count == 0 ? null : string.Join(" ", keep);
        }

        public static Field ExtractNumber(string? text)
        {
            var field = new Field();
            if (string.IsNullOrEmpty(text)) return field;
            var lines = text.Replace("\t", "    ").Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);

            for (int i = 0; i < lines.Length; i++)
            {
                foreach (Match m in NumberLabel.Matches(lines[i]))
                {
                    double w = LabelWeight(m.Groups["label"].Value);
                    if (w < 0) continue;
                    var value = m.Groups["value"].Value;
                    double score = w;

                    // Wert steht unter der Bezeichnung (gleiche Spalte, nächste 1-2 Zeilen)
                    if (string.IsNullOrEmpty(value))
                    {
                        for (int k = i + 1; k <= Math.Min(lines.Length - 1, i + 2) && value.Length == 0; k++)
                        {
                            if (lines[k].Length <= m.Index) continue;
                            int from = Math.Max(0, m.Index - 5);
                            var tm = TokenAtStart.Match(lines[k].Substring(from));
                            if (tm.Success) { value = tm.Groups["value"].Value; score -= 0.5; }
                        }
                    }
                    if (!string.IsNullOrEmpty(m.Groups["value"].Value))
                        value = ReadValue(lines[i], m.Groups["value"].Index) ?? value;
                    if (string.IsNullOrEmpty(value) || !value.Any(char.IsDigit)) continue;
                    value = FixDigits(value.Trim('-', '/', '|', ':', '.'));
                    if (i < 30) score += 0.5;                     // Kopfbereich
                    field.Add(value, score, m.Groups["label"].Value + "-Nr.");
                }

                foreach (Match m in PlainNumber.Matches(lines[i]))
                {
                    // Bezeichnung direkt davor? -> schon oben behandelt bzw. ausgeschlossen
                    var before = lines[i].Substring(0, m.Index).TrimEnd();
                    if (Regex.IsMatch(before, @"[A-Za-zäöü]-?$")) continue;
                    var pv = ReadValue(lines[i], m.Groups["value"].Index) ?? m.Groups["value"].Value;
                    field.Add(FixDigits(pv.Trim('-', '/', '|', ':', '.')), 1.0, "Nr.");
                }
            }
            return field;
        }

        // ---------- Datum ----------
        private static readonly Regex DateRegex = new(
            @"(?<!\d)(?<d>[0-3]?\d)\s*[./]\s*(?<m>[01]?\d)\s*[./]\s*(?<y>(?:19|20)?\d{2})(?!\d)",
            RegexOptions.Compiled);

        private static readonly Regex DateLabel = new(
            @"(?<label>Rechnungsdatum|Belegdatum|Gutschriftsdatum|Datum|Leistung\w*|Liefer\w*|Bestell\w*|Auftrags\w*|Fällig\w*|Zahlbar|bis)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static double DateLabelWeight(string label)
        {
            label = label.ToLowerInvariant();
            if (label.StartsWith("rechnungsdatum") || label.StartsWith("belegdatum") || label.StartsWith("gutschriftsdatum")) return 5;
            if (label == "datum") return 4;
            if (label.StartsWith("liefer")) return 1.5;
            if (label.StartsWith("leistung") || label == "bis") return 1;
            if (label.StartsWith("fällig") || label == "zahlbar") return 0.5;
            return 1;
        }

        public static Field ExtractDate(string? text)
        {
            var field = new Field();
            if (string.IsNullOrEmpty(text)) return field;
            var lines = text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);

            for (int i = 0; i < lines.Length; i++)
            {
                foreach (Match m in DateRegex.Matches(lines[i]))
                {
                    if (!TryNormalize(m, out var date)) continue;
                    // Nächstgelegene Bezeichnung links vom Datum (höchstens 40 Zeichen Abstand)
                    var left = lines[i].Substring(0, m.Index);
                    var labels = DateLabel.Matches(left).Cast<Match>().ToList();
                    var label = labels.LastOrDefault(l => m.Index - (l.Index + l.Length) <= 40);
                    double score = label != null ? DateLabelWeight(label.Groups["label"].Value) : 0.8;
                    if (i < 30) score += 0.3;
                    field.Add(date, score, label?.Value ?? "Datumsmuster");
                }
            }
            return field;
        }

        private static bool TryNormalize(Match m, out string date)
        {
            date = "";
            int d = int.Parse(m.Groups["d"].Value), mo = int.Parse(m.Groups["m"].Value), y = int.Parse(m.Groups["y"].Value);
            if (y < 100) y += 2000;
            if (mo < 1 || mo > 12 || d < 1 || d > DateTime.DaysInMonth(y, mo)) return false;
            date = new DateTime(y, mo, d).ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
            return true;
        }

        // ---------- Dokumenttyp ----------
        private static readonly (string Type, Regex Rx, double Weight)[] TypeRules =
        {
            ("Stornorechnung",       new Regex(@"\bStorno\s*-?\s*rechnung\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), 6),
            ("Gutschrift",           new Regex(@"\bGutschrift\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), 5),
            ("Mahnung",              new Regex(@"\b(Mahnung|Zahlungserinnerung)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), 5),
            ("Auftragsbestätigung",  new Regex(@"\bAuftragsbest(ä|ae|a)tigung\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), 5),
            ("Angebot",              new Regex(@"\bAngebot\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), 4),
            ("Lieferschein",         new Regex(@"\bLieferschein\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), 4),
            ("Rechnung",             new Regex(@"\bRechnung\b|\bRechnungs?\s*-?\s*(Nr|nummer)", RegexOptions.IgnoreCase | RegexOptions.Compiled), 4),
        };

        public static Field ExtractDocumentType(string? text)
        {
            var field = new Field();
            if (string.IsNullOrEmpty(text)) return field;
            var lines = text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            var scores = new Dictionary<string, double>();

            for (int i = 0; i < lines.Length; i++)
            {
                double pos = i < 30 ? 1.5 : 0.5;          // Kopfbereich zählt mehr
                foreach (var (type, rx, weight) in TypeRules)
                {
                    int count = rx.Matches(lines[i]).Count;
                    if (count == 0) continue;
                    // "Bei Zahlung bitte ... Beleg-Nr." / "Rechnung" im Fließtext zählt weniger
                    double s = weight * pos * (lines[i].Trim().Length < 40 ? 1.0 : 0.5);
                    scores[type] = scores.GetValueOrDefault(type) + s * count;
                }
            }
            DocTypeStore.AddScores(lines, scores, new HashSet<string>(TypeRules.Select(r => r.Item1), StringComparer.OrdinalIgnoreCase));
            foreach (var kv in scores) field.Add(DocTypeStore.Canonical(kv.Key), kv.Value, "Schl\u00FCsselwort");
            return field;
        }
    }
}
