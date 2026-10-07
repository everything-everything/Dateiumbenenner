using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Dateiumbenenner.Engine
{
    /// <summary>
    /// Bereitet OCR-Text NUR für die Adresserkennung auf:
    /// Aufzählungszeichen entfernen, ß-Fehler korrigieren, Schlüsselwörter abtrennen,
    /// zerrissene Kurzzeilen ("Test / StraBe / 87 / 73330 / Schwabisch / Hall") zusammenfügen.
    /// </summary>
    public static class AddressTextNormalizer
    {
        private static readonly Regex LeadingBullet = new(@"^[\s\-–•·:*>|]+", RegexOptions.Compiled);
        // StraBe, Stra8e, StraRe, Stra13e -> Straße
        private static readonly Regex SharpS = new(@"(stra)(?:B|8|R|13|\u00DF)(e)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        // Schlüsselwörter, die nicht zur Adresse gehören -> Zeile dort trennen
        private static readonly Regex KeywordSplit = new(@"\s(?=(?:Rechnung|Rechnungs|Lieferschein|Kunden|Auftrags?|Beleg|Datum|Seite|Tel|Telefon|Fax|USt|Steuer|IBAN|BIC)\b)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex KeywordStart = new(
            @"^(?:Rechnung|Rechnungs|Lieferschein|Kunden|Auftrags?|Beleg|Datum|Seite|Tel|Telefon|Fax|USt|Steuer|IBAN|BIC|Leistung\w*|Herst\w*)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex KeywordLine = new(
            @"\b(Rechnung|Nr\.?|Datum|Seite|Tel|Fax|IBAN|BIC|USt|EUR|Summe|Betrag)\b|:",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Textstück innerhalb einer Zeile
        private static readonly Regex Segment = new(@"\S+(?: {1,2}\S+)*", RegexOptions.Compiled);

        private class Block
        {
            public int LastY, LastStart, LastEnd;
            public List<string> Lines { get; } = new();
        }

        /// <summary>
        /// Formatierter OCR-Text ist ein Zeichenraster: Zeichenspalte = X-Position auf der Seite.
        /// Textstücke, die in aufeinanderfolgenden Zeilen horizontal überlappen, bilden einen Block
        /// (z. B. Adressblock links, Belegdaten rechts). Jeder Block wird getrennt aufbereitet.
        /// </summary>
        public static List<string> Prepare(string text)
        {
            var raw = text.Replace("\t", "    ").Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            var blocks = new List<Block>();
            for (int y = 0; y < raw.Length; y++)
            {
                foreach (Match m in Segment.Matches(raw[y]))
                {
                    int s = m.Index, e = m.Index + m.Length;
                    // Block aus der vorherigen (oder vorletzten) Zeile, der horizontal überlappt; nächstgelegener Start gewinnt
                    var b = blocks
                        .Where(b => b.LastY < y && y - b.LastY <= 2 && s < b.LastEnd + 1 && e > b.LastStart - 1)
                        .OrderBy(b => Math.Abs(s - b.LastStart))
                        .FirstOrDefault();
                    if (b == null) { b = new Block(); blocks.Add(b); }
                    b.LastY = y; b.LastStart = s; b.LastEnd = e;
                    b.Lines.Add(m.Value);
                }
            }

            var result = new List<string>();

            // Blockunabhängig: alle Textstücke in Lesereihenfolge – fängt Adressen ab,
            // deren Zeilen durch Einrückung/Leerzeilen in verschiedene Blöcke gerieten.
            var flat = new List<string>();
            foreach (var line in raw)
            {
                bool afterKeyword = false;   // "Rechnung-Nr.:      989439" -> Wert gehört zur Bezeichnung, nicht zur Adresse
                foreach (Match m in Segment.Matches(line))
                {
                    var l = LeadingBullet.Replace(m.Value, "").Trim();
                    if (l.Length == 0) continue;
                    if (afterKeyword) continue;
                    if (KeywordStart.IsMatch(l) || KeywordLine.IsMatch(l) && l.Contains(':')) { afterKeyword = true; continue; }
                    l = SharpS.Replace(l, x => x.Groups[1].Value + "ße");
                    l = InnerB.Replace(l, "ß");
                    l = Regex.Replace(l, @"(\d{5})\s*[—–-]\s+", "$1 ");   // "D-73525 — Schwäbisch"
                    flat.AddRange(KeywordSplit.Split(l).Select(p => p.Trim())
                        .Where(p => p.Length > 0 && !KeywordStart.IsMatch(p)));
                }
            }
            result.AddRange(ZipWindows(flat));
            result.InsertRange(0, AddressAssembler.Assemble(flat));
            // Spaltenbasiert (Straße / PLZ / Ort untereinander, Belegdaten in rechter Spalte) hat Vorrang
            result.InsertRange(0, ColumnAddressReader.Read(text));

            foreach (var b in blocks)
            {
                var cleaned = new List<string>();
                var side = new List<string>();   // Belegdaten ("Rechnung-Nr.: ...") – unterbrechen die Adresse nicht
                foreach (var line in b.Lines)
                {
                    var l = LeadingBullet.Replace(line, "").Trim();
                    if (l.Length == 0) continue;
                    l = SharpS.Replace(l, m => m.Groups[1].Value + "ße");
                    l = InnerB.Replace(l, "ß");                    // "WeiBensteiner" -> "Weißensteiner"
                    // "Straße Rechnung-Nr.
                    foreach (var p in KeywordSplit.Split(l).Select(p => p.Trim()).Where(p => p.Length > 0))
                    {
                        if (KeywordStart.IsMatch(p)) side.Add(p);
                        else cleaned.Add(p);
                    }
                }
                result.AddRange(ZipWindows(cleaned));          // untereinander geschriebene Adressen
                result.AddRange(MergeShortLines(cleaned));
                result.AddRange(side);
            }
            return result;
        }

        private static readonly Regex InnerB = new(@"(?<=[a-zäöü])B(?=[a-zäöü])", RegexOptions.Compiled);
        private static readonly Regex ZipStart = new(@"^(?:D\s*-\s*)?\d{5}\b", RegexOptions.Compiled);
        private static readonly Regex DigitStart = new(@"^\d", RegexOptions.Compiled);

        /// <summary>
        /// Setzt an jeder Zeile an, die mit einer PLZ beginnt, und fügt bis zu 3 Kurzzeilen davor
        /// (Straße, Hausnummer) und bis zu 2 danach (mehrteiliger Ort) zu einer Zeile zusammen.
        /// </summary>
        private static IEnumerable<string> ZipWindows(List<string> lines)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                if (!ZipStart.IsMatch(lines[i])) continue;
                int start = i;
                while (start > 0 && i - start < 3 && IsShort(lines[start - 1])) start--;
                int end = i;
                while (end + 1 < lines.Count && end - i < 2 && IsShort(lines[end + 1]) && !DigitStart.IsMatch(lines[end + 1])) end++;
                var joined = string.Join(" ", lines.Skip(start).Take(end - start + 1));
                // "Herrn/Frau" direkt davor -> Empfängeradresse kennzeichnen
                if (lines.Skip(Math.Max(0, start - 3)).Take(start - Math.Max(0, start - 3)).Any(x => Regex.IsMatch(x, @"^(Herrn?|Frau|Firma)\b", RegexOptions.IgnoreCase))) joined = "Herrn " + joined;
                yield return joined;
            }
        }

        private static int WordCount(string s) => s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        private static bool IsShort(string s) => WordCount(s) <= 2 && s.Length <= 30 && !KeywordLine.IsMatch(s);

        /// <summary>Fügt aufeinanderfolgende Kurzzeilen zu einer Zeile zusammen (max. 6 Zeilen, max. 10 Wörter).</summary>
        private static List<string> MergeShortLines(List<string> lines)
        {
            var result = new List<string>();
            int i = 0;
            while (i < lines.Count)
            {
                var cur = lines[i];
                int j = i + 1, merged = 1;
                if (IsShort(cur))
                {
                    while (j < lines.Count && merged < 6 && IsShort(lines[j]) && WordCount(cur) + WordCount(lines[j]) <= 10)
                    {
                        cur += " " + lines[j];
                        j++; merged++;
                    }
                }
                result.Add(cur);
                i = j;
            }
            return result;
        }
    }
}
