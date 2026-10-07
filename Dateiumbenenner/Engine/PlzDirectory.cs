using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Dateiumbenenner.Engine
{
    /// <summary>
    /// PLZ-Verzeichnis aus Daten\plz.csv (Format je Zeile: "PLZ;Ort", auch "," oder Tab als Trenner,
    /// Kopfzeile wird ignoriert). Fehlt die Datei, ist das Verzeichnis leer und alle PLZ gelten als gültig.
    /// </summary>
    public class PlzDirectory
    {
        private volatile Dictionary<string, List<string>> _map = new();
        public bool IsLoaded => _map.Count > 0;
        public static string FilePath => Path.Combine(Dateiumbenenner.Plugins.PluginManager.BaseDirectory, "plz.csv");
        /// <summary>GeoNames-Originaldatei (https://download.geonames.org/export/zip/DE.zip), kann unverändert abgelegt werden.</summary>
        public static string GeoNamesPath => Path.Combine(Dateiumbenenner.Plugins.PluginManager.BaseDirectory, "DE.txt");

        public PlzDirectory() => Reload();

        /// <summary>Verzeichnis neu einlesen (z. B. nach Aktualisierung von DE.txt).</summary>
        public void Reload()
        {
            var map = new Dictionary<string, List<string>>();
            try
            {
                foreach (var file in new[] { FilePath, GeoNamesPath })
                    if (File.Exists(file)) Load(file, map);
            }
            catch { map.Clear(); }
            _map = map;
        }

        /// <summary>Lädt DE.zip von GeoNames, ersetzt DE.txt und liefert die Anzahl der PLZ.</summary>
        public static async System.Threading.Tasks.Task<int> DownloadGeoNamesAsync()
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMinutes(2) };
            var bytes = await http.GetByteArrayAsync("https://download.geonames.org/export/zip/DE.zip");
            using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(bytes));
            var entry = zip.GetEntry("DE.txt") ?? throw new InvalidOperationException("DE.txt fehlt im Archiv.");
            Directory.CreateDirectory(Path.GetDirectoryName(GeoNamesPath)!);
            var tmp = GeoNamesPath + ".tmp";
            using (var src = entry.Open())
            using (var dst = File.Create(tmp))
                await src.CopyToAsync(dst);
            var lines = File.ReadLines(tmp).Count(l => l.Contains('\t'));
            if (lines < 1000) { File.Delete(tmp); throw new InvalidOperationException($"Download unvollständig ({lines} Zeilen)."); }
            File.Move(tmp, GeoNamesPath, true);
            return lines;
        }

        /// <summary>
        /// Erkennt je Zeile das Format:
        /// - GeoNames:  "DE ⇥ 73330 ⇥ Schwäbisch Hall ⇥ ..." (Ländercode, PLZ, Ort)
        /// - Einfach:   "73330;Schwäbisch Hall" (Trenner ; , oder Tab)
        /// </summary>
        private static void Load(string file, Dictionary<string, List<string>> map)
        {
            foreach (var line in File.ReadLines(file, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                // Tab-Dateien nur an Tabs trennen (Ortsnamen können Kommas enthalten)
                var parts = line.Contains('\t') ? line.Split('\t') : line.Split(';', ',');
                if (parts.Length < 2) continue;
                for (int k = 0; k < parts.Length; k++) parts[k] = parts[k].Trim().Trim('"').Trim();

                string zip, city;
                if (parts.Length >= 3 && parts[0].Length == 2 && parts[0].All(char.IsLetter) && IsZip(parts[1]))
                { zip = parts[1]; city = parts[2]; }                 // GeoNames
                else
                { zip = parts[0]; city = parts[1]; }                 // PLZ;Ort

                if (zip.Length == 4 && zip.All(char.IsDigit)) zip = "0" + zip; // Excel entfernt führende 0
                if (!IsZip(zip) || city.Length == 0) continue;
                if (!map.TryGetValue(zip, out var list)) map[zip] = list = new List<string>();
                if (!list.Contains(city, StringComparer.OrdinalIgnoreCase)) list.Add(city);
            }
        }

        private static bool IsZip(string s) => s.Length == 5 && s.All(char.IsDigit);

        /// <summary>Anzahl geladener PLZ (für Statusanzeige).</summary>
        public int Count => _map.Count;

        /// <summary>true, wenn PLZ bekannt ist (oder kein Verzeichnis geladen).</summary>
        public bool IsValid(string zip) => !IsLoaded || _map.ContainsKey(zip);

        /// <summary>Offizieller Ortsname zur PLZ, bei mehreren der zum OCR-Text ähnlichste.</summary>
        public string? Resolve(string zip, string? ocrCity)
        {
            if (!_map.TryGetValue(zip, out var list) || list.Count == 0) return null;
            if (list.Count == 1 || string.IsNullOrWhiteSpace(ocrCity)) return list[0];
            return list.OrderByDescending(c => Similarity(Fold(c), Fold(ocrCity))).First();
        }

        public static string Fold(string s) => s.ToUpperInvariant()
            .Replace("Ä", "A").Replace("Ö", "O").Replace("Ü", "U").Replace("ß", "SS").Replace(" ", "").Replace("-", "");

        public static double Similarity(string a, string b)
        {
            if (a.Length == 0 || b.Length == 0) return 0;
            var d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
                for (int j = 1; j <= b.Length; j++)
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            return 1.0 - (double)d[a.Length, b.Length] / Math.Max(a.Length, b.Length);
        }
    }
}
