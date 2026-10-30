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

        // Bezeichnung ohne "Nr.": "Invoice: INV-129229 /G2", "Numero W12243 - G 1"
        private static readonly Regex LooseLabel = new(
            @"(?<![\p{L}\-])(?<label>Invoice|Numero|Número|Numéro|Nummer|Number)\b\.?\s*(?:[:#]\s*|\s+)(?<value>[A-Z]{0,5}-?\d[A-Z0-9\-/]*)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static double LooseLabelWeight(string label) => label.ToLowerInvariant() switch
        {
            "invoice" => 4.5,
            "numero" or "número" or "numéro" => 2,
            _ => 1.5
        };

        private static readonly Regex AlnumToken = new(@"(?<![A-Za-z0-9])[A-Za-z0-9]{6,}(?![A-Za-z0-9])", RegexOptions.Compiled);

        /// <summary>Gleich lang und nur an 1–2 Stellen Buchstabe statt Ziffer (OCR: 4700D012354425 neben 47000012354425).</summary>
        private static bool IsOcrTwin(string a, string b)
        {
            if (a.Length != b.Length || a.Length < 6 || string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++)
            {
                if (char.ToUpperInvariant(a[i]) == char.ToUpperInvariant(b[i])) continue;
                if (char.IsDigit(a[i]) == char.IsDigit(b[i]) || ++diff > 2) return false;
            }
            return a.Count(char.IsDigit) >= a.Length / 2;
        }

        // Fahrgestellnummer (FIN/VIN): 17 Zeichen, Buchstaben und Ziffern ohne I, O, Q
        private static readonly Regex VinRegex = new(@"(?<![A-Za-z0-9])(?<value>[A-HJ-NPR-Z0-9]{17})(?![A-Za-z0-9])", RegexOptions.Compiled);
        private static readonly Regex VinLabel = new(@"Fahrgestell|Fahrzeug\s*-?\s*Ident|Fzg\.?\s*-?\s*Ident|Ident\s*-?\s*Nr|\bFIN\b|\bVIN\b|Chassis", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Wert hat die Form einer Fahrgestellnummer (17 Zeichen, Buchstaben und Ziffern gemischt, ohne I/O/Q).</summary>
        private static bool IsVin(string? value)
        {
            value = value?.Trim();
            if (value is not { Length: 17 }) return false;
            var m = VinRegex.Match(value);
            return m.Success && m.Length == 17 && value.Count(char.IsDigit) >= 3 && value.Count(char.IsLetter) >= 2;
        }

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

        /// <summary>Korrigiert typische OCR-Verwechslungen in überwiegend numerischen Werten.
        /// Nur einzelne O/I/l zwischen Ziffern – Buchstabenpräfixe wie "INV" bleiben erhalten.</summary>
        private static string FixDigits(string v)
        {
            int digits = v.Count(char.IsDigit);
            if (digits < v.Length / 2) return v;
            static bool Confusable(char c) => c is 'O' or 'o' or 'I' or 'l';
            static bool RealLetter(char c) => char.IsLetter(c) && !Confusable(c);
            var chars = v.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (!Confusable(v[i])) continue;
                if (i > 0 && RealLetter(v[i - 1]) || i + 1 < v.Length && RealLetter(v[i + 1])) continue;
                chars[i] = v[i] is 'O' or 'o' ? '0' : '1';
            }
            return new string(chars);
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
            var block = new LineBlock(lines);
            var hit = block.Mark(LooseLabel, block.Mark(VinRegex, block.Mark(PlainNumber, block.Mark(NumberLabel))));

            for (int i = 0; i < lines.Length; i++)
            {
                if (!hit[i]) continue;
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

                foreach (Match m in LooseLabel.Matches(lines[i]))
                {
                    // Teil einer vollständigen Bezeichnung ("Invoice Number", "Kunden Nummer") – dort bewertet bzw. ausgeschlossen
                    if (NumberLabel.Matches(lines[i]).Any(n => n.Index <= m.Index && m.Index < n.Index + n.Length)) continue;
                    var lv = ReadValue(lines[i], m.Groups["value"].Index) ?? m.Groups["value"].Value;
                    lv = FixDigits(lv.Trim('-', '/', '|', ':', '.'));
                    if (lv.Count(char.IsDigit) < 3) continue;
                    field.Add(lv, LooseLabelWeight(m.Groups["label"].Value) + (i < 30 ? 0.5 : 0), m.Groups["label"].Value);
                }

                // Fahrgestellnummer: als Vorschlag anbieten, aber hinter Rechnungs-/Belegnummern (außer Regeln bevorzugen sie)
                foreach (Match m in VinRegex.Matches(lines[i]))
                {
                    var vin = m.Groups["value"].Value;
                    if (!IsVin(vin)) continue;
                    bool labeled = VinLabel.IsMatch(lines[i]) || (i > 0 && VinLabel.IsMatch(lines[i - 1]));
                    field.Add(vin, labeled ? 0.9 : 0.5, labeled ? "Fahrgestell-Nr." : "FIN-Muster");
                }
            }

            // OCR-Varianten gefundener Nummern an anderer Stelle ebenfalls anbieten (etwas dahinter)
            var found = field.Candidates.Where(c => c.Score >= 0 && c.Value.Length >= 6 && c.Value.All(char.IsLetterOrDigit)).ToList();
            if (found.Count > 0)
                foreach (Match m in AlnumToken.Matches(text))
                    if (found.FirstOrDefault(c => IsOcrTwin(c.Value, m.Value)) is { } twin)
                        field.Add(m.Value, Math.Max(0, twin.Score - 1), twin.Source + " (OCR-Variante)");
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
            var block = new LineBlock(lines);
            var hit = block.Mark(DateRegex);
            foreach (var rx in DateRuleStore.MonthNameRegexes) block.Mark(rx, hit);
            block.Mark(DateRuleStore.OcrNumericRx, hit);

            for (int i = 0; i < lines.Length; i++)
            {
                if (!hit[i]) continue;
                void Add(int index, string date, bool alt = false, bool ocr = false)
                {
                    // Nächstgelegene Bezeichnung links vom Datum (höchstens 40 Zeichen Abstand)
                    var left = lines[i].Substring(0, index);
                    var labels = DateLabel.Matches(left).Cast<Match>().ToList();
                    var label = labels.LastOrDefault(l => index - (l.Index + l.Length) <= 40);
                    double score = label != null ? DateLabelWeight(label.Groups["label"].Value) : 0.8;
                    if (i < 30) score += 0.3;
                    // Zweite Lesart einer mehrdeutigen Kurzform (25Oct23): nur als weiterer Vorschlag
                    if (alt) { score = Math.Max(0, score - 0.6); field.Add(date, score, (label?.Value ?? "Datumsmuster") + " (andere Lesart)"); return; }
                    // OCR-Lesefehler (25.05,25 / 2 5 . 0 1 . 023): etwas hinter sauber gelesenen Daten
                    if (ocr) { score = Math.Max(0, score - 0.3); field.Add(date, score, (label?.Value ?? "Datumsmuster") + " (OCR korrigiert)"); return; }
                    field.Add(date, score, label?.Value ?? "Datumsmuster");
                }
                foreach (Match m in DateRegex.Matches(lines[i]))
                    if (TryNormalize(m, out var date)) Add(m.Index, date);
                // Monatsnamen: 23. Okt. 2025, 23Okt25, 25-Dez-23, 2025-MAY-10
                foreach (var h in DateRuleStore.FindDates(lines[i], DateFormatKind.Monatsname)) Add(h.Index, h.Date, h.Alt);
                foreach (var o in DateRuleStore.FindOcrDates(lines[i])) Add(o.Index, o.Date, ocr: true);
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
            ("Rechnung",             new Regex(@"\bRechnung\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), 4),
            ("Rechnungskorrektur",   new Regex(@"\b(Rechnungs\s*-?\s*korrektur|Korrektur\s*-?\s*rechnung)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), 6),
            ("Korrektur",            new Regex(@"\bKorrektur\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), 3),
            ("Sammelrechnung",       new Regex(@"\bSammel\s*-?\s*rechnung\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), 6),
            ("Reparaturrechnung",    new Regex(@"\bReparatur\s*-?\s*rechnung\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), 6),
            ("Gutschriftsrechnung",  new Regex(@"\bGutschrifts?\s*-?\s*rechnung\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), 6),
            ("Storno",               new Regex(@"\bStorno\b(?!\s*-?\s*rechnung)", RegexOptions.IgnoreCase | RegexOptions.Compiled), 4),
            ("Lastschrift-Avis",     new Regex(@"\bLastschrift\s*-?\s*Avis\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), 6),
            ("Avis",                 new Regex(@"(?<!Lastschrift\s*-?\s*)\bAvis\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), 4),
            ("Sendungsinformation",  new Regex(@"\bSendungs?information\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), 4),
            ("Werkstattauftrag",     new Regex(@"\bWerkstatt\s*-?\s*Auftrag\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), 6),
            ("Reparaturauftrag",     new Regex(@"\bReparatur\s*-?\s*auftrag\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), 6),
            ("Materialschein",       new Regex(@"\bMaterial\s*-?\s*schein\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), 5),
            ("Bestellung",           new Regex(@"\bBestellung\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), 3),
            ("Auftrag",              new Regex(@"(?<!(Werkstatt|Reparatur)\s*-?\s*)\bAuftrag\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), 3),
            ("Schreiben",            new Regex(@"\bSchreiben\s+vom\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), 3),
        };

        // Nummernbezeichnung der Belegart ("Rechnungsnummer", "Rechnungs-Nr.", "Lieferscheinnummer", "Lieferschein Nummer"):
        // eigener Hinweis; zusammen mit dem Titel ("Lieferschein" + "Lieferschein-Nr.") ein starker Hinweis.
        // "Belegnummer" ist neutral (kommt bei jeder Belegart vor).
        private static readonly (string Type, Regex Rx, double Weight)[] NumberLabelRules =
        {
            ("Rechnung",     NumberLabelRx("Rechnungs?"), 2),
            ("Gutschrift",   NumberLabelRx("Gutschrifts?"), 2),
            ("Lieferschein", NumberLabelRx("Lieferscheins?"), 1.5),
            ("Angebot",      NumberLabelRx("Angebots?"), 1.5),
        };

        private static Regex NumberLabelRx(string word) =>
            new(@"\b" + word + @"\s*-?\s*(?:Nr|Nummer|No)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly HashSet<string> BuiltInTypes = new(TypeRules.Select(r => r.Type), StringComparer.OrdinalIgnoreCase);

        public static Field ExtractDocumentType(string? text)
        {
            var field = new Field();
            if (string.IsNullOrEmpty(text)) return field;
            var lines = text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            var scores = new Dictionary<string, double>();
            var title = new Dictionary<string, double>();     // stärkster Titel-Treffer je Belegart
            var labeled = new HashSet<string>();              // Belegarten mit eigener Nummernbezeichnung
            var block = new LineBlock(lines);
            int n = TypeRules.Length;
            var hit = new bool[lines.Length * n];
            for (int r = 0; r < n; r++) block.Mark(TypeRules[r].Rx, hit, n, r);
            int nl = NumberLabelRules.Length;
            var labelHit = new bool[lines.Length * nl];
            for (int r = 0; r < nl; r++) block.Mark(NumberLabelRules[r].Rx, labelHit, nl, r);

            for (int i = 0; i < lines.Length; i++)
            {
                double pos = i < 30 ? 1.5 : 0.5;          // Kopfbereich zählt mehr
                for (int r = 0; r < n; r++)
                {
                    if (!hit[i * n + r]) continue;
                    var (type, rx, weight) = TypeRules[r];
                    foreach (Match m in rx.Matches(lines[i]))
                    {
                        double f = MatchFactor(lines[i], m);
                        scores[type] = scores.GetValueOrDefault(type) + weight * pos * f;
                        if (f >= 0.5) title[type] = Math.Max(title.GetValueOrDefault(type), weight * pos * f);
                    }
                }
                for (int r = 0; r < nl; r++)
                {
                    if (!labelHit[i * nl + r]) continue;
                    var (type, rx, weight) = NumberLabelRules[r];
                    foreach (Match m in rx.Matches(lines[i]))
                    {
                        // "zu Lieferschein-Nr. 4711", "Ihre Rechnungsnummer": Bezug auf einen anderen Beleg
                        bool reference = IsReference(lines[i], m);
                        scores[type] = scores.GetValueOrDefault(type) + weight * pos * (reference ? 0.02 : 1.0);
                        if (!reference) labeled.Add(type);
                    }
                }
            }
            // Titel und passende Nummernbezeichnung zusammen ("Lieferschein" + "Lieferscheinnummer")
            foreach (var type in labeled)
                if (title.TryGetValue(type, out var t)) scores[type] += 0.5 * t;
            DocTypeStore.AddScores(block, scores, BuiltInTypes);
            foreach (var kv in scores) field.Add(DocTypeStore.Canonical(kv.Key), kv.Value, "Schl\u00FCsselwort");
            return field;
        }

        private static readonly Regex ColumnGap = new(@"\s{3,}|\t", RegexOptions.Compiled);
        private static readonly Regex LabelSuffix = new(@"^(s?\s*-?\s*(Nr|Nummer|No)\b|s?nummer|s?nr\b|-?datum)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        // Bezug auf einen anderen Beleg: "Ihr Angebot vom", "Lieferschein zu Auftrag", "gemäß unserem Angebot", "Bezug: Angebot"
        private static readonly Regex ReferencePrefix = new(
            @"(?:\b(?:Ihr(?:e[mnrs]?)?|unser(?:e[mnrs]?)?|gem(?:äß|aess|\.)|laut|lt\.|zu[mr]?|aus|auf|bzgl\.?|bez(?:ü|ue)glich|Bezug\s*:?|betr(?:\.|effend|\s*:)|siehe|vgl\.)\s*(?:(?:dem|der|den|das|die|Ihr(?:e[mnrs]?)?|unser(?:e[mnrs]?)?)\s+)?)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Gewichtung eines Belegart-Treffers: eigener Spaltenabschnitt, alleinstehender Titel z\u00E4hlt mehr, Feldbezeichnungen (z. B. "Beleg-Nr") weniger.</summary>
        private static (int Start, int End) Segment(string line, Match m)
        {
            int start = 0, end = line.Length;
            foreach (Match g in ColumnGap.Matches(line))
            {
                if (g.Index + g.Length <= m.Index) start = g.Index + g.Length;
                else if (g.Index >= m.Index + m.Length) { end = g.Index; break; }
            }
            return (start, end);
        }

        private static bool IsReference(string line, Match m)
        {
            int start = Segment(line, m).Start;
            return ReferencePrefix.IsMatch(line.Substring(start, m.Index - start));
        }

        public static double MatchFactor(string line, Match m)
        {
            var (start, end) = Segment(line, m);
            var segment = line.Substring(start, end - start).Trim();
            double f = segment.Length < 40 ? 1.0 : 0.5;
            // Belegart steht meist als alleinstehender Begriff (Titel)
            if (segment.Equals(m.Value.Trim(), StringComparison.OrdinalIgnoreCase)) f *= 2.0;
            // "Lieferschein-Nr", "Lieferschein-Datum": Feldbezeichnung, kein Titel (Nummernbezeichnungen werten NumberLabelRules)
            if (LabelSuffix.IsMatch(line.Substring(m.Index + m.Length))) return 0;
            // Bezug auf einen anderen Beleg (z. B. "Ihr Angebot vom ..." auf einem Lieferschein): praktisch kein Hinweis
            if (ReferencePrefix.IsMatch(line.Substring(start, m.Index - start))) return f * 0.02;
            return f;
        }
    }
}
