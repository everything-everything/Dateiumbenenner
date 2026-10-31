using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Dateiumbenenner.Engine
{
    /// <summary>
    /// PLZ-Verzeichnis. Grundlage ist die mitgelieferte GeoNames-Liste (CC BY 4.0), eingebettet in die signierte Programmdatei –
    /// sie kann daher nicht unbemerkt ausgetauscht werden. Eine geprüfte Aktualisierung (Daten\DE.txt) ersetzt sie;
    /// eigene Ergänzungen aus Daten\plz.csv (je Zeile "PLZ;Ort", auch "," oder Tab als Trenner) kommen immer hinzu.
    /// </summary>
    public class PlzDirectory
    {
        private volatile Dictionary<string, List<string>> _map = new();
        public bool IsLoaded => _map.Count > 0;
        public static string FilePath => Path.Combine(Dateiumbenenner.Plugins.PluginManager.BaseDirectory, "plz.csv");
        /// <summary>Geprüfte Aktualisierung von GeoNames (ersetzt die mitgelieferte Liste).</summary>
        public static string GeoNamesPath => Path.Combine(Dateiumbenenner.Plugins.PluginManager.BaseDirectory, "DE.txt");
        /// <summary>Herkunft der Aktualisierung (Adresse, Abrufzeit, Prüfsumme).</summary>
        public static string SourceInfoPath => GeoNamesPath + ".quelle.json";

        /// <summary>Einzige erlaubte Bezugsquelle für Aktualisierungen.</summary>
        public const string SourceUrl = "https://download.geonames.org/export/zip/DE.zip";
        private const string SourceHost = "download.geonames.org";
        private const string BundledResource = "Dateiumbenenner.Data.GeoNames-DE.zip";
        private const long MaxDownloadBytes = 20L * 1024 * 1024;
        private const long MaxTextBytes = 50L * 1024 * 1024;
        private const int MinZips = 8000, MaxZips = 15000;

        /// <summary>Aktive Quelle als Text für die Anzeige.</summary>
        public string Source { get; private set; } = "";
        /// <summary>Hinweis, wenn eine vorhandene Daten\DE.txt die Prüfung nicht bestanden hat und deshalb nicht verwendet wird.</summary>
        public string? Warning { get; private set; }
        /// <summary>True, wenn die mitgelieferte Liste aktiv ist (keine gültige Daten\DE.txt).</summary>
        public bool IsBundled { get; private set; }

        public PlzDirectory() => Reload();

        private int _version;
        /// <summary>Erhöht sich bei jedem Neuladen (Zwischenspeicher der Adresserkennung werden dadurch ungültig).</summary>
        public int Version => System.Threading.Volatile.Read(ref _version);

        /// <summary>Verzeichnis neu einlesen (z. B. nach Aktualisierung von DE.txt).</summary>
        public void Reload()
        {
            var map = new Dictionary<string, List<string>>();
            string source = "keine PLZ-Liste";
            string? warning = null;
            bool bundled = false;
            try
            {
                if (File.Exists(GeoNamesPath))
                {
                    var update = new Dictionary<string, List<string>>();
                    var problem = Check(Load(File.ReadLines(GeoNamesPath, Encoding.UTF8), update), update.Count);
                    if (problem == null) { map = update; source = DescribeUpdate(); }
                    else warning = $"Daten\\DE.txt wird nicht verwendet: {problem}";
                }
                if (map.Count == 0 && LoadBundled(map) is { } b) { source = b; bundled = true; }
                if (File.Exists(FilePath))
                {
                    Load(File.ReadLines(FilePath, Encoding.UTF8), map);
                    source += " + eigene Ergänzungen (plz.csv)";
                }
            }
            catch { map.Clear(); }
            _map = map;
            Source = source;
            Warning = warning;
            IsBundled = bundled;
            _placeWords = null;
            System.Threading.Interlocked.Increment(ref _version);
        }

        /// <summary>Stand der mitgelieferten Liste (Datum der GeoNames-Datei), null wenn nicht vorhanden.</summary>
        public static DateTime? BundledDate
        {
            get
            {
                try
                {
                    using var s = typeof(PlzDirectory).Assembly.GetManifestResourceStream(BundledResource);
                    if (s == null) return null;
                    using var zip = new System.IO.Compression.ZipArchive(s);
                    return zip.GetEntry("DE.txt")?.LastWriteTime.DateTime;
                }
                catch { return null; }
            }
        }

        private static string? LoadBundled(Dictionary<string, List<string>> map)
        {
            using var s = typeof(PlzDirectory).Assembly.GetManifestResourceStream(BundledResource);
            if (s == null) return null;
            using var zip = new System.IO.Compression.ZipArchive(s);
            var entry = zip.GetEntry("DE.txt");
            if (entry == null) return null;
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
            Load(ReadLines(reader), map);
            return $"mitgeliefert (GeoNames, Stand {entry.LastWriteTime:dd.MM.yyyy})";
        }

        private static IEnumerable<string> ReadLines(TextReader reader)
        {
            string? line;
            while ((line = reader.ReadLine()) != null) yield return line;
        }

        private sealed class SourceInfo
        {
            public string Url { get; set; } = SourceUrl;
            public DateTime Retrieved { get; set; }
            public DateTimeOffset? LastModified { get; set; }
            public string Sha256 { get; set; } = "";
            public long Size { get; set; }
            public int Zips { get; set; }
        }

        private static string DescribeUpdate()
        {
            try
            {
                if (File.Exists(SourceInfoPath)
                    && System.Text.Json.JsonSerializer.Deserialize<SourceInfo>(SecureStorage.ReadAllText(SourceInfoPath)) is { } i)
                    return $"aktualisiert von {new Uri(i.Url).Host} am {i.Retrieved:dd.MM.yyyy HH:mm}"
                        + (i.LastModified is { } lm ? $" (Datenstand {lm.LocalDateTime:dd.MM.yyyy})" : "")
                        + $", SHA-256 {i.Sha256[..Math.Min(16, i.Sha256.Length)]}…";
            }
            catch { }
            string date = "";
            try { date = $", Datei vom {File.GetLastWriteTime(GeoNamesPath):dd.MM.yyyy}"; } catch { }
            return $"Daten\\DE.txt – früher heruntergeladen (Herkunft nicht vermerkt{date}), Prüfung bestanden";
        }

        /// <summary>Ergebnis eines Downloads – noch nicht aktiv, erst <see cref="Activate"/> übernimmt es.</summary>
        public sealed class PlzUpdate
        {
            public string TempPath { get; init; } = "";
            public string Url { get; init; } = SourceUrl;
            public string Sha256 { get; init; } = "";
            public long Size { get; init; }
            public DateTimeOffset? LastModified { get; init; }
            public int Zips { get; init; }
            public int Before { get; init; }
            public int Added { get; init; }
            public int Removed { get; init; }
            public int Changed { get; init; }
            public int Unusual { get; init; }
            /// <summary>Auffällig viele Änderungen gegenüber der aktiven Liste – vor der Übernahme genau prüfen.</summary>
            public bool Suspicious => Before > 0 && (Removed > Before * 0.02 || Changed > Before * 0.05);
        }

        /// <summary>
        /// Lädt DE.zip ausschließlich von <see cref="SourceUrl"/> (HTTPS, keine Umleitungen), begrenzt die Größe,
        /// prüft Format und Plausibilität und vergleicht mit der aktiven Liste. Aktiviert wird noch nichts.
        /// </summary>
        public async System.Threading.Tasks.Task<PlzUpdate> DownloadGeoNamesAsync()
        {
            using var handler = new System.Net.Http.HttpClientHandler { AllowAutoRedirect = false };
            using var http = new System.Net.Http.HttpClient(handler) { Timeout = TimeSpan.FromMinutes(2) };
            using var resp = await http.GetAsync(SourceUrl, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
            if ((int)resp.StatusCode is >= 300 and < 400)
                throw new InvalidOperationException($"Der Server leitet auf eine andere Adresse um ({resp.Headers.Location}). Aus Sicherheitsgründen abgebrochen.");
            resp.EnsureSuccessStatusCode();
            var uri = resp.RequestMessage?.RequestUri;
            if (uri == null || uri.Scheme != Uri.UriSchemeHttps || !uri.Host.Equals(SourceHost, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Unerwartete Quelle: {uri}");
            if (resp.Content.Headers.ContentLength > MaxDownloadBytes)
                throw new InvalidOperationException($"Datei zu groß ({resp.Content.Headers.ContentLength:N0} Byte).");
            byte[] bytes;
            await using (var net = await resp.Content.ReadAsStreamAsync())
                bytes = await ReadLimitedAsync(net, MaxDownloadBytes, "Download");
            var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));

            using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(bytes));
            var entry = zip.GetEntry("DE.txt") ?? throw new InvalidOperationException("DE.txt fehlt im Archiv.");
            if (entry.Length > MaxTextBytes) throw new InvalidOperationException($"DE.txt ist unplausibel groß ({entry.Length:N0} Byte).");
            byte[] text;
            await using (var es = entry.Open()) text = await ReadLimitedAsync(es, MaxTextBytes, "DE.txt");

            var map = new Dictionary<string, List<string>>();
            using (var reader = new StreamReader(new MemoryStream(text), Encoding.UTF8))
            {
                var stats = Load(ReadLines(reader), map);
                if (Check(stats, map.Count) is { } problem) throw new InvalidOperationException(problem);
                var current = _map;
                Directory.CreateDirectory(Path.GetDirectoryName(GeoNamesPath)!);
                var tmp = GeoNamesPath + ".neu";
                await File.WriteAllBytesAsync(tmp, text);
                return new PlzUpdate
                {
                    TempPath = tmp, Sha256 = sha, Size = bytes.Length, LastModified = resp.Content.Headers.LastModified,
                    Zips = map.Count, Before = current.Count, Unusual = stats.Unusual,
                    Added = map.Keys.Count(k => !current.ContainsKey(k)),
                    Removed = current.Keys.Count(k => !map.ContainsKey(k)),
                    Changed = map.Count(kv => current.TryGetValue(kv.Key, out var old)
                        && !old.OrderBy(c => c, StringComparer.OrdinalIgnoreCase).SequenceEqual(kv.Value.OrderBy(c => c, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase)),
                };
            }
        }

        /// <summary>Geprüften Download übernehmen und Herkunft vermerken.</summary>
        public static void Activate(PlzUpdate u)
        {
            File.Move(u.TempPath, GeoNamesPath, true);
            var info = new SourceInfo { Url = u.Url, Retrieved = DateTime.Now, LastModified = u.LastModified, Sha256 = u.Sha256, Size = u.Size, Zips = u.Zips };
            SecureStorage.WriteAllText(SourceInfoPath, System.Text.Json.JsonSerializer.Serialize(info, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }

        /// <summary>Download verwerfen.</summary>
        public static void Discard(PlzUpdate u)
        {
            try { if (File.Exists(u.TempPath)) File.Delete(u.TempPath); } catch { }
        }

        /// <summary>Aktualisierung entfernen – danach gilt wieder die mitgelieferte Liste.</summary>
        public static void RestoreBundled()
        {
            if (File.Exists(GeoNamesPath)) File.Delete(GeoNamesPath);
            if (File.Exists(SourceInfoPath)) File.Delete(SourceInfoPath);
        }

        private static async System.Threading.Tasks.Task<byte[]> ReadLimitedAsync(Stream s, long max, string what)
        {
            using var ms = new MemoryStream();
            var buf = new byte[81920];
            int n;
            while ((n = await s.ReadAsync(buf)) > 0)
            {
                if (ms.Length + n > max) throw new InvalidOperationException($"{what} überschreitet die zulässige Größe von {max / 1024 / 1024} MB.");
                ms.Write(buf, 0, n);
            }
            return ms.ToArray();
        }

        private readonly record struct LoadStats(int Lines, int Accepted, int Invalid, int Unusual);

        // Übliche Zeichen in Orts-/Großkundennamen; Abweichungen (z. B. falsch kodiertes „KÃ¶ln“ im Original) sind nur in geringer Zahl erlaubt
        private static readonly System.Text.RegularExpressions.Regex UsualCity = new(@"^[\p{L}\p{M}0-9 .,'’()/&+\-]+$", System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>Plausibilität: Format fast aller Zeilen gültig, keine Steuerzeichen/überlangen Einträge, PLZ-Anzahl im erwarteten Bereich.</summary>
        private static string? Check(LoadStats s, int zips)
        {
            if (s.Invalid > 0) return $"{s.Invalid} Zeile(n) mit Steuerzeichen oder überlangen Einträgen.";
            if (s.Lines == 0 || s.Accepted < s.Lines * 0.98) return $"Format ungültig ({s.Accepted} von {s.Lines} Zeilen lesbar).";
            if (s.Unusual > s.Accepted * 0.01) return $"Zu viele ungewöhnliche Ortsnamen ({s.Unusual}).";
            if (zips < MinZips || zips > MaxZips) return $"Unplausible Anzahl PLZ ({zips}, erwartet {MinZips}–{MaxZips}).";
            return null;
        }

        /// <summary>
        /// Erkennt je Zeile das Format:
        /// - GeoNames:  "DE ⇥ 73330 ⇥ Schwäbisch Hall ⇥ ..." (Ländercode, PLZ, Ort)
        /// - Einfach:   "73330;Schwäbisch Hall" (Trenner ; , oder Tab)
        /// </summary>
        private static LoadStats Load(IEnumerable<string> lines, Dictionary<string, List<string>> map)
        {
            int total = 0, accepted = 0, invalid = 0, unusual = 0;
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                total++;
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
                if (city.Length > 120 || city.Any(char.IsControl)) { invalid++; continue; }
                if (!UsualCity.IsMatch(city)) unusual++;
                accepted++;
                if (!map.TryGetValue(zip, out var list)) map[zip] = list = new List<string>();
                if (!list.Contains(city, StringComparer.OrdinalIgnoreCase)) list.Add(city);
            }
            return new LoadStats(total, accepted, invalid, unusual);
        }

        private static bool IsZip(string s) => s.Length == 5 && s.All(char.IsDigit);

        /// <summary>Anzahl geladener PLZ (für Statusanzeige).</summary>
        public int Count => _map.Count;

        private static readonly char[] PlaceSeparators = { ' ', '-', '/', '(', ')', '.', ',' };
        private HashSet<string>? _placeWords;

        // Die Liste enthält auch Großkunden ("Stadtsparkasse Augsburg", "Kreissparkasse Ostalb"): Namen mit Institutionswort sind kein Ort
        private static readonly System.Text.RegularExpressions.Regex InstitutionRegex = new(
            @"sparkasse|bank(?![\p{L}])|versicherung|kasse(?![\p{L}])|stadtwerke|verlag(?![\p{L}])|kammer(?![\p{L}])|gericht(?![\p{L}])|agentur|finanzamt|landratsamt|verwaltung",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly System.Text.RegularExpressions.Regex CreditInstitutionRegex = new(
            @"sparkasse|bank(?![\p{L}])", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>true, wenn der Text ein Ortsname oder nur aus Ortsbestandteilen besteht (z. B. "Schwäbisch", "Schwäbisch Hall").</summary>
        public bool IsPlaceName(string? text)
        {
            if (string.IsNullOrWhiteSpace(text) || !IsLoaded) return false;
            var parts = text.Split(PlaceSeparators, StringSplitOptions.RemoveEmptyEntries);
            // Einzelwort nur bei Kreditinstitut ("Kreissparkasse"), sonst bleiben allgemeine Wörter wie "Verwaltung" ausgefiltert
            if (InstitutionRegex.IsMatch(text) && (parts.Length >= 2 || CreditInstitutionRegex.IsMatch(text))) return false;
            var words = _placeWords;
            if (words == null)
            {
                words = new HashSet<string>(StringComparer.Ordinal);
                foreach (var city in _map.Values.SelectMany(v => v))
                {
                    words.Add(Fold(city));
                    foreach (var w in city.Split(PlaceSeparators, StringSplitOptions.RemoveEmptyEntries))
                        if (w.Length >= 3) words.Add(Fold(w));
                }
                _placeWords = words;
            }
            return words.Contains(Fold(text)) || (parts.Length > 0 && parts.All(p => words.Contains(Fold(p))));
        }

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
