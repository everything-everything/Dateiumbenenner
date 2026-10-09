using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Dateiumbenenner.Engine
{
    /// <summary>Ein Kandidat für ein Feld mit Bewertung und Herkunft.</summary>
    public record Candidate(string Value, double Score, string Source);

    /// <summary>Ein erkanntes Feld: bester Wert plus alle Kandidaten (für Auswahllisten).</summary>
    public class Field
    {
        public string? Value => Candidates.Where(c => c.Score >= 0).OrderByDescending(c => c.Score).FirstOrDefault()?.Value;
        public List<Candidate> Candidates { get; } = new();
        public List<string> Values => Candidates.Where(c => c.Score >= 0).OrderByDescending(c => c.Score).Select(c => c.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        /// <summary>true, wenn der Wert von der Engine abgewertet wurde (z. B. Empfänger).</summary>
        public bool IsRejected(string? value) => !string.IsNullOrWhiteSpace(value) &&
            Candidates.Any(c => c.Score < 0 && c.Value.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase)) &&
            !Candidates.Any(c => c.Score >= 0 && c.Value.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase));
        public void Add(string? value, double score, string source)
        {
            value = value?.Trim();
            if (string.IsNullOrEmpty(value)) return;
            var idx = Candidates.FindIndex(c => c.Value.Equals(value, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) Candidates.Add(new Candidate(value, score, source));
            else if (Candidates[idx].Score < score) Candidates[idx] = new Candidate(value, score, source);
        }
    }

    public class AddressResult
    {
        public Field Street { get; } = new();
        public Field Zip { get; } = new();
        public Field City { get; } = new();
    }

    /// <summary>Gemeinsame Erkennungs-Engine für Hauptprogramm und Plugins.</summary>
    public interface IDocumentEngine
    {
        /// <summary>Absenderadresse. companyHint = erkannte Firma, deren Umfeld bevorzugt wird.</summary>
        /// pdfPath: gleichnamige PDF – deren Wortpositionen trennen Spalten sauber (auch im A-Modus).
        AddressResult ExtractAddress(string? text, string? companyHint = null, string? pdfPath = null);
        Field ExtractAmount(string? text);
        Field ExtractNumber(string? text, string? pdfPath = null);
        Field ExtractDate(string? text, string? pdfPath = null);
        Field ExtractDocumentType(string? text, string? pdfPath = null);

        /// <summary>Absender über gelernte Merkmale (USt-ID, IBAN, Telefon, ...) erkennen.</summary>
        SenderProfile? IdentifySender(string? text, string? pdfPath = null);

        /// <summary>Benutzerbestätigung zurückmelden – die Engine lernt daraus.</summary>
        void ConfirmSender(string? text, string company, string? street = null, string? zip = null, string? city = null);

        /// <summary>Firmenkandidaten nach Nähe zur erkannten Absenderstraße/-PLZ sortieren (nächster zuerst).</summary>
        List<string> RankCompaniesByAddress(IEnumerable<string> candidates, string? text, string? pdfPath = null);

        /// <summary>PLZ der eigenen Firma (Empfänger) – werden bei der Absenderadresse ausgeschlossen.</summary>
        IReadOnlyCollection<string> OwnZips { get; }
        void AddOwnZip(string zip);
    }

    public class DocumentEngine : IDocumentEngine
    {
        private static readonly CultureInfo DeCulture = new("de-DE");

        // Portabel: neben der Anwendung speichern (wie known_companies.json)
        private readonly SenderProfileStore _profiles = new(System.IO.Path.Combine(
            Dateiumbenenner.Plugins.PluginManager.BaseDirectory, "sender_profiles.json"));

        public static readonly PlzDirectory Plz = new();
        /// <summary>PLZ-Verzeichnis neu laden; liefert Anzahl der PLZ.</summary>
        public static int ReloadPlz() { Plz.Reload(); return Plz.Count; }
        public static int PlzCount => Plz.Count;

        private static readonly string OwnZipFile = System.IO.Path.Combine(Dateiumbenenner.Plugins.PluginManager.BaseDirectory, "own_zips.txt");
        private readonly HashSet<string> _ownZips = LoadOwnZips();
        private static HashSet<string> LoadOwnZips()
        {
            try { return System.IO.File.Exists(OwnZipFile) ? new HashSet<string>(System.IO.File.ReadAllLines(OwnZipFile).Select(l => l.Trim()).Where(l => l.Length == 5)) : new(); }
            catch { return new(); }
        }
        public IReadOnlyCollection<string> OwnZips { get { lock (_ownZips) return _ownZips.ToList(); } }
        public void AddOwnZip(string zip)
        {
            zip = zip?.Trim() ?? "";
            if (zip.Length != 5) return;
            lock (_ownZips)
            {
                if (!_ownZips.Add(zip)) return;
                try { System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(OwnZipFile)!); System.IO.File.WriteAllLines(OwnZipFile, _ownZips); } catch { }
            }
        }

        [ThreadStatic] private static bool _suppressDebug;

        public SenderProfile? IdentifySender(string? text, string? pdfPath = null)
        {
            AddressResult addr;
            _suppressDebug = true;
            try { addr = ExtractAddress(text, null, pdfPath); }
            finally { _suppressDebug = false; }
            var fp = SenderProfileStore.ExtractFingerprints(text, addr.Street.Value, addr.Zip.Value);
            return _profiles.Identify(fp).Profile;
        }

        public void ConfirmSender(string? text, string company, string? street = null, string? zip = null, string? city = null)
        {
            if (string.IsNullOrWhiteSpace(company)) return;
            CompanyMatcher.Learn(company);
            var fp = SenderProfileStore.ExtractFingerprints(text, street, zip);
            _profiles.Learn(company, fp, street, zip, city);
        }

        public List<string> RankCompaniesByAddress(IEnumerable<string> candidates, string? text, string? pdfPath = null)
        {
            var list = candidates.ToList();
            if (list.Count < 2 || string.IsNullOrEmpty(text)) return list;
            AddressResult addr;
            _suppressDebug = true;
            try { addr = ExtractAddress(text, null, pdfPath); }
            finally { _suppressDebug = false; }
            var street = addr.Street.Value; var zip = addr.Zip.Value;
            if (string.IsNullOrEmpty(street) && string.IsNullOrEmpty(zip)) return list;

            var raw = ReadSiblingTxt(pdfPath);
            var lines = AddressTextNormalizer.Prepare(HasLayout(raw) ? raw! : text);
            var streetKey = string.IsNullOrEmpty(street) ? null : StreetKey(street);
            var anchors = new List<int>();
            for (int i = 0; i < lines.Count; i++)
            {
                var l = lines[i];
                if (zip != null && l.Contains(zip)) { anchors.Add(i); continue; }
                if (streetKey != null) foreach (Match s in StreetRegex.Matches(l)) if (StreetKey(FormatStreet(s)) == streetKey) { anchors.Add(i); break; }
            }
            if (anchors.Count == 0) return list;

            double Score(string c, int order)
            {
                int best = int.MaxValue;
                for (int i = 0; i < lines.Count; i++)
                    if (lines[i].Contains(c, StringComparison.OrdinalIgnoreCase))
                        foreach (var a in anchors)
                        {
                            int d = a - i;                         // Firma steht über der Adresse; darunter folgen nur Ort/Empfänger
                            if (d > 0 && d <= 6 && d < best) best = d;
                        }
                // nahe Kandidaten vorziehen, sonst ursprüngliche Reihenfolge
                return best == int.MaxValue ? 100 + order : best;
            }
            int Count(string c) => lines.Count(l => l.Contains(c, StringComparison.OrdinalIgnoreCase));
            // bei gleichem Abstand: im Briefkopf mehrfach genannter Name (Logo + Absenderzeile) gewinnt
            return list.Select((c, i) => (c, s: Score(c, i), n: Count(c)))
                       .OrderBy(x => x.s).ThenByDescending(x => x.n).Select(x => x.c).ToList();
        }

        // ---------- Betrag ----------
        private static readonly Regex AmountRegex = new(@"(?<![\d,.])(\d{1,3}(?:\.\d{3})+,\d{2}|\d+,\d{2})(?![\d])", RegexOptions.Compiled);
        private static readonly Regex AmountKeywordRegex = new(@"(Gesamtbetrag|Rechnungsbetrag|Endbetrag|Bruttobetrag|Brutto|Zahlbetrag|zu\s+zahlen|Gesamtsumme|Summe|Gesamt|Total)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public Field ExtractAmount(string? text)
        {
            var field = new Field();
            if (string.IsNullOrEmpty(text)) return field;
            var lines = SplitLines(text);
            static decimal Parse(string s) => decimal.TryParse(s, NumberStyles.Number, DeCulture, out var d) ? d : 0m;

            var all = new List<string>();
            var keyword = new List<string>();
            for (int i = 0; i < lines.Count; i++)
            {
                var found = AmountRegex.Matches(lines[i]).Select(m => m.Groups[1].Value).ToList();
                all.AddRange(found);
                if (AmountKeywordRegex.IsMatch(lines[i]))
                {
                    if (found.Count == 0 && i + 1 < lines.Count)
                        found = AmountRegex.Matches(lines[i + 1]).Select(m => m.Groups[1].Value).ToList();
                    keyword.AddRange(found);
                }
            }
            // Schlüsselwort-Beträge zuerst (größter zuerst), danach die übrigen
            var kw = keyword.Distinct().OrderByDescending(Parse).ToList();
            for (int i = 0; i < kw.Count; i++) field.Add(kw[i], 2.0 - i * 0.01, "Schlüsselwort");
            var rest = all.Distinct().Except(kw).OrderByDescending(Parse).ToList();
            for (int i = 0; i < rest.Count; i++) field.Add(rest[i], 1.0 - i * 0.01, "Betragsmuster");
            return field;
        }

        // ---------- Nummer / Datum / Dokumenttyp ----------
        // Formatierte TXT (Zeichenraster) bevorzugen: Werte unter der Bezeichnung werden so gefunden.
        private static string? LayoutText(string? text, string? pdfPath)
        {
            var raw = ReadSiblingTxt(pdfPath);
            return HasLayout(raw) ? raw : text;
        }

        public Field ExtractNumber(string? text, string? pdfPath = null) =>
            DocumentFieldExtractor.ExtractNumber(LayoutText(text, pdfPath));

        public Field ExtractDate(string? text, string? pdfPath = null) =>
            DocumentFieldExtractor.ExtractDate(LayoutText(text, pdfPath));

        public Field ExtractDocumentType(string? text, string? pdfPath = null) =>
            DocumentFieldExtractor.ExtractDocumentType(LayoutText(text, pdfPath));

        // ---------- Adresse ----------
        private static readonly Regex ZipCityRegex = new(@"(?:^|[\s,])(?:D\s*-\s*)?(\d{5})\s+([A-ZÄÖÜ][A-Za-zÄÖÜäöüß\.\-]+(?:\s+(?:[a-zA-ZÄÖÜäöüß\.\-]+))?(?:\s+\([^)]*\))?)", RegexOptions.Compiled);
        // Straße: "Hauptstraße 5", "Weißensteiner Straße 87", "Am Alten Weg 3", "Bahnhofstr. 12a"
        private static readonly Regex StreetRegex = new(@"\b((?:[A-ZÄÖÜ][A-Za-zÄÖÜäöüß\.\-]*\s+){0,3}[A-Za-zÄÖÜäöüß\-]*?(?:straße|strasse|str\.|weg|platz|allee|gasse|ring|damm|chaussee|ufer|markt|Straße|Strasse|Str\.|Weg|Platz|Allee|Gasse|Ring|Damm|Chaussee|Ufer|Markt))\s*(\d+(?:\s*[a-zA-Z](?!\s*-\s*\d))?(?:\s*-\s*\d+\s*[a-zA-Z]?)?)\b", RegexOptions.Compiled);
        private static readonly HashSet<string> CityStopWords = new(StringComparer.OrdinalIgnoreCase)
        { "Seite","Datum","Kunden","Kunde","Nr","Telefon","Tel","Fax","Rechnung","Rechnungs","Beleg","Auftrag","Auftrags","Lieferschein","Gutschrift","Betrag","Summe","Gesamt","Menge","Stück","Stk","Pos","Position","Artikel","EUR","Euro","MwSt","USt","Netto","Brutto","Bei","Bitte","Ihre","Unsere","Konto","IBAN","BIC","Bank" };
        private static readonly Regex NumberLabelBeforeRegex = new(@"(Kunden|Auftrags?|Rechnungs?|Beleg|Liefer\w*|Bestell|Konto|Tel\w*|Fax|Seite|Pos\w*)\s*-?\s*(Nr\.?|nummer)?\s*[:.]?\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RecipientMarkerRegex = new(@"^\s*(Herrn?|Frau|Firma|An\s*:|Empf(ä|ae)nger|Lieferanschrift|Rechnungsanschrift|Kunde\s*:)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public AddressResult ExtractAddress(string? text, string? companyHint = null, string? pdfPath = null)
        {
            var result = new AddressResult();
            // Formatierter OCR-Text (A-Modus/TXT) enthält das Layout bereits als Zeichenraster -> direkt verwenden.
            // Nur wenn kein Layout erkennbar ist (z. B. PdfPig-Fließtext), die Wortpositionen der PDF nutzen.
            // Gleichnamige TXT (formatierter OCR-Modus) im Rohzustand bevorzugen –
            // die OCR-Korrektur reduziert Mehrfach-Leerzeichen und zerstört damit das Layout.
            var rawTxt = ReadSiblingTxt(pdfPath);
            if (HasLayout(rawTxt)) text = rawTxt;
            else if (!HasLayout(text))
            {
                var layout = PdfLayoutReader.ReadLayoutText(pdfPath);
                if (!string.IsNullOrEmpty(layout)) text = layout;
            }
            if (string.IsNullOrEmpty(text)) return result;
            var lines = AddressTextNormalizer.Prepare(text);
            int n = lines.Count;
            var absLines = new HashSet<int>();
            for (int j = 0; j < n; j++)
                if (lines[j].StartsWith("@ABS ")) { absLines.Add(j); lines[j] = lines[j].Substring(5); }

            // Straßen aus Zeilen mit "Herrn/Frau/Firma/An:" = Empfänger -> überall stark abwerten
            var recipientStreets = new HashSet<string>();
            foreach (var l in lines)
                if (RecipientMarkerRegex.IsMatch(l))
                    foreach (Match s in StreetRegex.Matches(l))
                        recipientStreets.Add(StreetKey(FormatStreet(s)));

            // Zeilen, in denen die Firma vorkommt (Absenderblock)
            var hintKey = string.IsNullOrWhiteSpace(companyHint) ? null : companyHint.Trim();
            var companyLines = new List<int>();
            if (hintKey != null)
                for (int j = 0; j < n; j++)
                    if (lines[j].Contains(hintKey, StringComparison.OrdinalIgnoreCase)) companyLines.Add(j);

            // Empfängerblock: bis zu 5 Zeilen nach "Herrn/Frau/Firma/An:"
            var recipient = new HashSet<int>();
            for (int j = 0; j < n; j++)
                if (RecipientMarkerRegex.IsMatch(lines[j]))
                    for (int k = j; k <= Math.Min(n - 1, j + 5); k++) recipient.Add(k);

            double PositionScore(int i, string line, string before)
            {
                double s = 0;
                if (absLines.Contains(i)) s += 6;                                          // zusammengesetzte Absenderadresse
                if (companyLines.Any(c => i >= c && i - c <= 4)) s += 4;                  // direkt nach Firmenname
                if (before.Contains('·') || before.Contains('•') || before.Contains('|') || before.Contains(" - ") || before.Contains(" – ")) s += 3; // Absenderzeile über Anschrift
                if (i >= n - 15) s += 1.5;                                                // Fußzeile
                if (recipient.Contains(i)) s -= 3;                                         // Empfänger
                return s;
            }

            for (int i = 0; i < n; i++)
            {
                // Straßen auch ohne PLZ als Kandidat aufnehmen
                foreach (Match s in StreetRegex.Matches(lines[i]))
                {
                    var st = FormatStreet(s);
                    double ss = 0.5 + PositionScore(i, lines[i], lines[i]) * 0.5;
                    if (recipientStreets.Contains(StreetKey(st))) ss -= 10;
                    result.Street.Add(st, ss, "Straßenmuster");
                }

                foreach (Match zm in ZipCityRegex.Matches(lines[i]))
                {
                    var zip = zm.Groups[1].Value;
                    var city = zm.Groups[2].Value.Trim();
                    if (!Plz.IsValid(zip)) continue;                           // unbekannte PLZ (Kunden-Nr. o. ä.)
                    var official = Plz.Resolve(zip, city);
                    var firstWord = city.Split(' ')[0].TrimEnd('.', ':', ',');
                    if (official == null && CityStopWords.Contains(firstWord)) continue;          // "16126 Seite"
                    if (official != null) city = official;                     // "Schwabisch Hall" -> "Schwäbisch Hall"
                    var before = lines[i].Substring(0, zm.Index);
                    if (NumberLabelBeforeRegex.IsMatch(before)) continue;     // "Kunden-Nr.: 16126"

                    double score = 1.0;
                    var sm = StreetRegex.Match(before);
                    if (sm.Success) score += 3;
                    else if (i > 0) { sm = StreetRegex.Match(lines[i - 1]); if (sm.Success) score += 2; }
                    if (city.Length >= 3) score += 1;
                    if (official != null) score += 2;                          // PLZ im Verzeichnis bestätigt
                    score += PositionScore(i, lines[i], before);
                    if (RecipientMarkerRegex.IsMatch(lines[i])) score -= 5;   // "Herrn ... D-73525 ..."
                    bool own; lock (_ownZips) own = _ownZips.Contains(zip);
                    var oc = OwnCompany.Current;
                    if (!own && oc.Zip.Trim() == zip)
                        own = string.IsNullOrWhiteSpace(oc.Street) || !sm.Success || StreetKey(FormatStreet(sm)) == StreetKey(oc.Street);
                    if (own) score -= 10;                                      // eigene Firma = Empfänger

                    if (sm.Success && recipientStreets.Contains(StreetKey(FormatStreet(sm)))) score -= 10;
                    result.Zip.Add(zip, score, "PLZ/Ort");
                    result.City.Add(city, score, "PLZ/Ort");
                    if (sm.Success) result.Street.Add(FormatStreet(sm), score, "Straße vor PLZ");
                }
            }
            if (!string.IsNullOrEmpty(pdfPath) && !_suppressDebug)
                WriteDebug(pdfPath, rawTxt != null && HasLayout(rawTxt), lines, result);
            return result;
        }

        private static string DebugFlagPath => System.IO.Path.Combine(Dateiumbenenner.Plugins.PluginManager.BaseDirectory, "debug.on");
        private static bool? _debugEnabled;

        /// <summary>Debug-Dateien schreiben (Einstellung, gespeichert als Daten\debug.on).</summary>
        public static bool DebugEnabled
        {
            get => _debugEnabled ??= System.IO.File.Exists(DebugFlagPath);
            set
            {
                _debugEnabled = value;
                try
                {
                    if (value) { System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(DebugFlagPath)!); System.IO.File.WriteAllText(DebugFlagPath, ""); }
                    else if (System.IO.File.Exists(DebugFlagPath)) System.IO.File.Delete(DebugFlagPath);
                }
                catch { }
            }
        }

        /// <summary>Diagnose: letzte Adressanalyse nach Daten\address_debug.txt schreiben.</summary>
        private static void WriteDebug(string? pdfPath, bool usedTxt, List<string> lines, AddressResult r)
        {
            if (!DebugEnabled) return;
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Datei: {pdfPath}");
                sb.AppendLine($"Quelle: {(usedTxt ? "gleichnamige TXT (roh)" : "übergebener Text/PDF")}");
                sb.AppendLine($"PLZ-Verzeichnis: {Plz.Count} Einträge");
                sb.AppendLine("--- Aufbereitete Zeilen ---");
                for (int i = 0; i < lines.Count; i++) sb.AppendLine($"[{i}] {lines[i]}");
                void Dump(string name, Field f)
                {
                    sb.AppendLine($"--- {name} ---");
                    foreach (var c in f.Candidates.OrderByDescending(c => c.Score))
                        sb.AppendLine($"{c.Score,6:0.00}  {c.Value}  ({c.Source})");
                }
                Dump("Straße", r.Street); Dump("PLZ", r.Zip); Dump("Ort", r.City);
                var dir = System.IO.Path.Combine(Dateiumbenenner.Plugins.PluginManager.BaseDirectory, "debug");
                System.IO.Directory.CreateDirectory(dir);
                var name = System.IO.Path.GetFileNameWithoutExtension(pdfPath) ?? "unbekannt";
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, $"address_{name}.txt"), sb.ToString());
                System.IO.File.WriteAllText(System.IO.Path.Combine(
                    Dateiumbenenner.Plugins.PluginManager.BaseDirectory, "address_debug.txt"), sb.ToString());
            }
            catch { }
        }

        private static string? ReadSiblingTxt(string? path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return null;
                var txt = System.IO.Path.ChangeExtension(path, ".txt");
                return System.IO.File.Exists(txt) ? System.IO.File.ReadAllText(txt) : null;
            }
            catch { return null; }
        }

        /// <summary>true, wenn der Text Spaltenabstände (3+ Leerzeichen/Tab) enthält.</summary>
        private static bool HasLayout(string? text) =>
            !string.IsNullOrEmpty(text) &&
            Regex.Matches(text, @"\S(?: {3,}|\t)\S").Count >= 3;

        private static readonly Regex StreetSuffixWord = new(@"^(straße|strasse|str\.?|weg|platz|allee|gasse|ring|damm|chaussee|ufer|markt)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Vergleichsschlüssel: "Joachim BITTNER Oberbettringerstr.75" == "Oberbettringerstr. 75".</summary>
        private static string StreetKey(string street)
        {
            var words = street.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length < 2) return street.ToLowerInvariant();
            var num = words[^1];
            var name = words[^2];
            // "Weißensteiner Straße" -> Vorwort mitnehmen
            if (StreetSuffixWord.IsMatch(name) && words.Length >= 3) name = words[^3] + name;
            name = Regex.Replace(name.ToLowerInvariant(), @"(straße|strasse|str\.?)$", "str");
            return Regex.Replace(name + num, @"[^\wäöüß]", "");
        }

        /// <summary>Entfernt Anrede/Namen vor der Straße ("Herrn Joachim BITTNER Oberbettringerstr." -> "Oberbettringerstr.").</summary>
        private static string CleanStreetName(string name)
        {
            var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
            if (words.Count == 0) return name;
            // Zusammengesetzter Name ("Oberbettringerstr.") -> nur das letzte Wort
            if (!StreetSuffixWord.IsMatch(words[^1])) return words[^1];
            // "Weißensteiner Straße" / "Am Alten Weg": Anrede, Namen in GROSSBUCHSTABEN davor entfernen
            int start = 0;
            for (int i = 0; i < words.Count - 1; i++)
                if (Regex.IsMatch(words[i], @"^(Herrn?|Frau|Firma)$", RegexOptions.IgnoreCase) ||
                    (words[i].Length > 2 && words[i] == words[i].ToUpperInvariant()))
                    start = i + 1;
            return string.Join(" ", words.Skip(start));
        }

        private static string FormatStreet(Match m) => FormatStreetRaw(m);
        private static string FormatStreetRaw(Match m) =>
            $"{CleanStreetName(m.Groups[1].Value.Trim())} {Regex.Replace(m.Groups[2].Value, @"\s+", "")}";

        private static List<string> SplitLines(string text) =>
            text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries).ToList();
    }
}
