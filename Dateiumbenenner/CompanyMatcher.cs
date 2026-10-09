using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Dateiumbenenner
{
    /// <summary>
    /// Merkt sich bestätigte Firmennamen und ordnet OCR-Varianten (z. B. "MUTO-EINMAL-EINS")
    /// per unscharfem Abgleich dem bekannten Namen ("AUTO-EINMAL-EINS") zu.
    /// </summary>
    public static class CompanyMatcher
    {
        private static readonly string StoreFile = Path.Combine(Dateiumbenenner.Plugins.PluginManager.BaseDirectory, "known_companies.json");
        private static readonly object _lock = new();
        private static List<string>? _known;

        /// <summary>Mindestähnlichkeit (0..1), ab der ein Kandidat als bekannte/gleiche Firma gilt (Daten\company_threshold.txt, in %).</summary>
        public static double Threshold
        {
            get
            {
                if (_threshold is double t) return t;
                double v = 0.82;
                try
                {
                    if (File.Exists(ThresholdFile) && double.TryParse(File.ReadAllText(ThresholdFile).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var p) && p >= 50 && p <= 100)
                        v = p / 100.0;
                }
                catch { }
                return (_threshold = v).Value;
            }
            set
            {
                _threshold = Math.Clamp(value, 0.5, 1.0);
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(ThresholdFile)!);
                    File.WriteAllText(ThresholdFile, Math.Round(_threshold.Value * 100).ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
                catch { }
            }
        }
        private static double? _threshold;
        private static readonly string ThresholdFile = Path.Combine(Dateiumbenenner.Plugins.PluginManager.BaseDirectory, "company_threshold.txt");

        /// <summary>Mindestanteil (0..1) gleicher/ähnlicher Einträge zweier Auswahllisten, damit ein weiterer Beleg mit umgestellt wird (Daten\company_list_threshold.txt, in %).</summary>
        public static double ListThreshold
        {
            get
            {
                if (_listThreshold is double t) return t;
                double v = 0.5;
                try
                {
                    if (File.Exists(ListThresholdFile) && double.TryParse(File.ReadAllText(ListThresholdFile).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var p) && p >= 0 && p <= 100)
                        v = p / 100.0;
                }
                catch { }
                return (_listThreshold = v).Value;
            }
            set
            {
                _listThreshold = Math.Clamp(value, 0.0, 1.0);
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(ListThresholdFile)!);
                    File.WriteAllText(ListThresholdFile, Math.Round(_listThreshold.Value * 100).ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
                catch { }
            }
        }
        private static double? _listThreshold;
        private static readonly string ListThresholdFile = Path.Combine(Dateiumbenenner.Plugins.PluginManager.BaseDirectory, "company_list_threshold.txt");

        /// <summary>True, wenn beide Namen gleich oder mindestens zu <see cref="Threshold"/> ähnlich sind.</summary>
        public static bool IsSame(string? a, string? b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
            if (string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
            return Similarity(Normalize(a), Normalize(b)) >= Threshold;
        }

        /// <summary>Anteil (0..1) der Einträge der kürzeren Liste, die in der anderen gleich/ähnlich vorkommen.</summary>
        public static double ListOverlap(IEnumerable<string> a, IEnumerable<string> b)
        {
            var la = a.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            var lb = b.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            if (la.Count == 0 || lb.Count == 0) return 0;
            var small = la.Count <= lb.Count ? la : lb; var large = ReferenceEquals(small, la) ? lb : la;
            return (double)small.Count(x => large.Any(y => IsSame(x, y))) / small.Count;
        }

        private static List<string> Known
        {
            get
            {
                if (_known != null) return _known;
                lock (_lock)
                {
                    if (_known != null) return _known;
                    try { _known = File.Exists(StoreFile) ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(StoreFile)) ?? new() : new(); }
                    catch { _known = new(); }
                    return _known;
                }
            }
        }

        /// <summary>Bestätigten Firmennamen lernen (z. B. bei Auswahl durch den Benutzer).</summary>
        public static void Learn(string? company)
        {
            company = company?.Trim();
            if (string.IsNullOrEmpty(company) || company.Length < 3 || company.Equals("Stop", StringComparison.OrdinalIgnoreCase)) return;
            if (Engine.DocumentEngine.Plz.IsPlaceName(company)) return;
            lock (_lock)
            {
                var list = Known;
                if (list.Any(k => k.Equals(company, StringComparison.OrdinalIgnoreCase))) return;
                list.Add(company);
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(StoreFile)!);
                    File.WriteAllText(StoreFile, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
                }
                catch { }
            }
        }

        /// <summary>
        /// Ersetzt Kandidaten durch bekannte Firmen (falls ähnlich genug), sortiert bekannte Treffer nach vorne
        /// und sucht zusätzlich im Volltext nach bekannten Firmen (auch mit OCR-Fehlern).
        /// </summary>
        public static List<string> Improve(List<string> candidates, string? fullText)
        {
            var known = Known.Where(k => !Engine.DocumentEngine.Plz.IsPlaceName(k)).ToList();
            if (known.Count == 0) return candidates;

            var knownNorm = known.Select(Normalize).ToArray();
            var result = new List<(string Name, double Score)>();
            foreach (var c in candidates)
            {
                var (best, score) = BestMatch(c, known, knownNorm);
                if (best != null && score >= Threshold) Add(result, best, 1.0 + score);  // bekannte Firma bevorzugen
                else Add(result, c, 0.5);
            }

            // Bekannte Firmen direkt im Text suchen (Zeilen/Fenster vergleichen)
            if (!string.IsNullOrEmpty(fullText))
            {
                // Zeilen nur einmal normalisieren (vorher pro bekannter Firma erneut)
                var lines = fullText.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(Normalize).ToArray();
                double threshold = Threshold;
                Span<int> histK = stackalloc int[HistSize];
                Span<int> histW = stackalloc int[HistSize];
                for (int ki = 0; ki < known.Count; ki++)
                {
                    var k = known[ki];
                    var nk = knownNorm[ki];
                    if (nk.Length < 4) continue;
                    FillHistogram(nk, histK);
                    double bestLine = 0;
                    foreach (var nl in lines)
                    {
                        if (nl.Length < nk.Length / 2) continue;
                        if (nl.Contains(nk)) { bestLine = 1; break; }
                        // Gleitendes Fenster in Länge des Firmennamens
                        for (int i = 0; i + nk.Length <= nl.Length; i += Math.Max(1, nk.Length / 4))
                        {
                            var window = nl.AsSpan(i, nk.Length);
                            // Untere Schranke der Levenshtein-Distanz über Zeichenhäufigkeiten:
                            // kann das Fenster weder Schwellwert noch Bestwert erreichen, entfällt die teure Rechnung
                            FillHistogram(window, histW);
                            double upper = 1.0 - (double)HistogramDistance(histK, histW) / nk.Length;
                            if (upper < threshold || upper <= bestLine) continue;
                            var s = Similarity(nk.AsSpan(), window);
                            if (s > bestLine) bestLine = s;
                        }
                    }
                    if (bestLine >= Threshold) Add(result, k, 1.0 + bestLine);
                }
            }

            return result.OrderByDescending(r => r.Score).Select(r => r.Name).ToList();
        }

        /// <summary>True, wenn der Firmenname (normalisiert, unscharf ab <see cref="Threshold"/>) in einer Textzeile vorkommt.</summary>
        public static bool AppearsInText(string? company, string? fullText)
        {
            if (string.IsNullOrWhiteSpace(company) || string.IsNullOrEmpty(fullText)) return false;
            var nk = Normalize(company);
            if (nk.Length == 0) return false;
            if (Normalize(fullText).Contains(nk)) return true;
            if (nk.Length < 4) return false;
            foreach (var line in fullText.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries))
            {
                var nl = Normalize(line);
                if (nl.Length < nk.Length / 2) continue;
                if (nl.Length <= nk.Length) { if (Similarity(nk, nl) >= Threshold) return true; continue; }
                for (int i = 0; i + nk.Length <= nl.Length; i += Math.Max(1, nk.Length / 4))
                    if (Similarity(nk, nl.Substring(i, nk.Length)) >= Threshold) return true;
            }
            return false;
        }

        private static void Add(List<(string Name, double Score)> list, string name, double score)
        {
            var idx = list.FindIndex(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) list.Add((name, score));
            else if (list[idx].Score < score) list[idx] = (list[idx].Name, score);
        }

        private static (string? Best, double Score) BestMatch(string candidate, List<string> known, string[] knownNorm)
        {
            var nc = Normalize(candidate);
            string? best = null; double bestScore = 0;
            for (int i = 0; i < known.Count; i++)
            {
                var s = Similarity(nc, knownNorm[i]);
                if (s > bestScore) { bestScore = s; best = known[i]; }
            }
            return (best, bestScore);
        }

        private const int HistSize = 129; // ASCII + ein Sammelfach für sonstige Zeichen

        private static void FillHistogram(ReadOnlySpan<char> s, Span<int> hist)
        {
            hist.Clear();
            foreach (var c in s) hist[c < 128 ? c : 128]++;
        }

        /// <summary>Bei gleicher Länge: Anzahl nicht zuordenbarer Zeichen = untere Schranke der Levenshtein-Distanz.</summary>
        private static int HistogramDistance(ReadOnlySpan<int> a, ReadOnlySpan<int> b)
        {
            int diff = 0;
            for (int i = 0; i < a.Length; i++) if (a[i] > b[i]) diff += a[i] - b[i];
            return diff;
        }

        /// <summary>Gleicht typische OCR-Verwechslungen an und entfernt Satzzeichen.</summary>
        public static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var t = s.ToUpperInvariant()
                .Replace("ß", "SS").Replace("Ä", "AE").Replace("Ö", "OE").Replace("Ü", "UE")
                .Replace("RN", "M").Replace("VV", "W").Replace("CL", "D");
            var sb = new StringBuilder(t.Length);
            foreach (var ch in t)
            {
                char c = ch switch
                {
                    '0' => 'O', '1' => 'I', 'L' => 'I', '|' => 'I', '!' => 'I',
                    '5' => 'S', '8' => 'B', '6' => 'G', '2' => 'Z',
                    _ => ch
                };
                if (char.IsLetterOrDigit(c)) sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>Ähnlichkeit 0..1 auf Basis der Levenshtein-Distanz.</summary>
        public static double Similarity(string a, string b) => Similarity(a.AsSpan(), b.AsSpan());

        /// <summary>Wie <see cref="Similarity(string,string)"/>, ohne Teilstring-/Array-Allokationen bei kurzen Werten.</summary>
        public static double Similarity(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
        {
            if (a.Length == 0 && b.Length == 0) return 1;
            if (a.Length == 0 || b.Length == 0) return 0;
            Span<int> d = b.Length < 256 ? stackalloc int[b.Length + 1] : new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) d[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                int prev = d[0]; d[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    int tmp = d[j];
                    d[j] = Math.Min(Math.Min(d[j] + 1, d[j - 1] + 1), prev + (a[i - 1] == b[j - 1] ? 0 : 1));
                    prev = tmp;
                }
            }
            return 1.0 - (double)d[b.Length] / Math.Max(a.Length, b.Length);
        }
    }
}
