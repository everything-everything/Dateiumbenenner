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

        /// <summary>Mindestähnlichkeit (0..1), ab der ein Kandidat als bekannte Firma gilt.</summary>
        public static double Threshold { get; set; } = 0.82;

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
            var known = Known.ToList();
            if (known.Count == 0) return candidates;

            var result = new List<(string Name, double Score)>();
            foreach (var c in candidates)
            {
                var (best, score) = BestMatch(c, known);
                if (best != null && score >= Threshold) Add(result, best, 1.0 + score);  // bekannte Firma bevorzugen
                else Add(result, c, 0.5);
            }

            // Bekannte Firmen direkt im Text suchen (Zeilen/Fenster vergleichen)
            if (!string.IsNullOrEmpty(fullText))
            {
                var lines = fullText.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var k in known)
                {
                    var nk = Normalize(k);
                    if (nk.Length < 4) continue;
                    double bestLine = 0;
                    foreach (var line in lines)
                    {
                        var nl = Normalize(line);
                        if (nl.Length < nk.Length / 2) continue;
                        if (nl.Contains(nk)) { bestLine = 1; break; }
                        // Gleitendes Fenster in Länge des Firmennamens
                        for (int i = 0; i + nk.Length <= nl.Length; i += Math.Max(1, nk.Length / 4))
                        {
                            var s = Similarity(nk, nl.Substring(i, nk.Length));
                            if (s > bestLine) bestLine = s;
                        }
                    }
                    if (bestLine >= Threshold) Add(result, k, 1.0 + bestLine);
                }
            }

            return result.OrderByDescending(r => r.Score).Select(r => r.Name).ToList();
        }

        private static void Add(List<(string Name, double Score)> list, string name, double score)
        {
            var idx = list.FindIndex(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) list.Add((name, score));
            else if (list[idx].Score < score) list[idx] = (list[idx].Name, score);
        }

        private static (string? Best, double Score) BestMatch(string candidate, List<string> known)
        {
            var nc = Normalize(candidate);
            string? best = null; double bestScore = 0;
            foreach (var k in known)
            {
                var s = Similarity(nc, Normalize(k));
                if (s > bestScore) { bestScore = s; best = k; }
            }
            return (best, bestScore);
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
        public static double Similarity(string a, string b)
        {
            if (a.Length == 0 && b.Length == 0) return 1;
            if (a.Length == 0 || b.Length == 0) return 0;
            var d = new int[b.Length + 1];
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
