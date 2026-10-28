using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Dateiumbenenner.Engine
{
    public enum CompanySource : byte { Datenbank, Gelernt, Gleif }

    public readonly record struct CompanyHit(string Name, string? Zip, string? City, CompanySource Source);

    /// <summary>
    /// Firmenverzeichnis als Stütze der Firmenerkennung: eigene Datenbank (Plugin), gelernte Firmen mit PLZ und optional
    /// das freie GLEIF-Verzeichnis (CC0). Liefert nur Pluspunkte – unbekannte Firmen werden nie abgewertet.
    /// </summary>
    public static class CompanyDirectory
    {
        private sealed class Layer
        {
            public static readonly Layer Empty = new(new Dictionary<string, CompanyHit[]>(), 0);
            public Layer(Dictionary<string, CompanyHit[]> map, int names) { Map = map; Names = names; }
            public Dictionary<string, CompanyHit[]> Map { get; }
            public int Names { get; }
        }

        private static volatile Layer _db = Layer.Empty, _learned = Layer.Empty, _gleif = Layer.Empty;
        private static readonly object _learnLock = new();
        private static List<LearnedEntry> _learnedList = new();
        private static Task? _loadTask;

        public static event EventHandler? Changed;

        public static string DbCachePath => Path.Combine(Plugins.PluginManager.BaseDirectory, "company_directory_db.json");
        public static string LearnedPath => Path.Combine(Plugins.PluginManager.BaseDirectory, "company_directory_learned.json");
        /// <summary>GLEIF ist Referenzwissen wie das PLZ-Verzeichnis: einmal für alle Profile.</summary>
        public static string GleifPath => Path.Combine(ProfileManager.RootDirectory, "gleif_de.dat");
        public static string GleifInfoPath => GleifPath + ".quelle.json";

        public static int DbCount => _db.Names;
        public static int LearnedCount => _learned.Names;
        public static int GleifCount => _gleif.Names;
        public static DateTime? DbLoaded { get; private set; }
        public static bool IsLoaded => _loadTask?.IsCompleted == true;

        /// <summary>Gespeicherte Daten (Datenbank-Stand, Gelerntes, GLEIF) einmalig im Hintergrund laden.</summary>
        public static Task EnsureLoadedAsync() => _loadTask ??= Task.Run(() =>
        {
            try
            {
                if (File.Exists(DbCachePath))
                {
                    var c = JsonSerializer.Deserialize<DbCache>(SecureStorage.ReadAllText(DbCachePath));
                    if (c?.Entries != null) { _db = Build(c.Entries.Select(e => new CompanyHit(e.Name, e.Zip, e.City, CompanySource.Datenbank))); DbLoaded = c.Loaded; }
                }
            }
            catch { }
            try
            {
                if (File.Exists(LearnedPath))
                {
                    var l = JsonSerializer.Deserialize<List<LearnedEntry>>(SecureStorage.ReadAllText(LearnedPath)) ?? new();
                    lock (_learnLock) { _learnedList = l; _learned = Build(l.Select(e => new CompanyHit(e.Name, e.Zip, e.City, CompanySource.Gelernt))); }
                }
            }
            catch { }
            try { LoadGleif(); } catch { }
            Changed?.Invoke(null, EventArgs.Empty);
        });

        // ---------- Normalisierung ----------

        private static readonly Regex LegalForms = new(
            @"\b(gesellschaft\s+mit\s+beschraenkter\s+haftung|kommanditgesellschaft\s+auf\s+aktien|aktiengesellschaft|kommanditgesellschaft|" +
            @"offene\s+handelsgesellschaft|unternehmergesellschaft|eingetragene[rn]?\s+(?:kaufmann|kauffrau|genossenschaft|verein)|europaeische\s+gesellschaft|" +
            @"gmbh\s*&\s*co\.?\s*kgaa|gmbh\s*&\s*co\.?\s*kg|ag\s*&\s*co\.?\s*kg|ug\s*\(haftungsbeschraenkt\)|haftungsbeschraenkt|ggmbh|gmbh|mbh|kgaa|ag|kg|ohg|gbr|ug|" +
            @"partgmbb|partg|e\.?\s*k(fm|fr)?\.?|e\.?\s*v\.?|e\.?\s*g\.?|se|ltd|inc|co|corp|llc|bv|nv|sarl|s\.?a\.?r\.?l|sa|spa|srl)\b\.?",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex NonWord = new(@"[^\p{L}\p{N}]+", RegexOptions.Compiled);

        /// <summary>Vergleichsschlüssel: klein, Umlaute ausgeschrieben, ohne Rechtsform und Satzzeichen ("ZAPF GmbH" = "Zapf").</summary>
        public static string Key(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            var s = name.ToLowerInvariant().Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue").Replace("ß", "ss");
            s = LegalForms.Replace(s, " ");
            s = NonWord.Replace(s, " ").Trim();
            return s.Length < 3 ? "" : s;
        }

        private static Layer Build(IEnumerable<CompanyHit> hits)
        {
            var tmp = new Dictionary<string, List<CompanyHit>>(StringComparer.Ordinal);
            int names = 0;
            foreach (var h in hits)
            {
                var k = Key(h.Name);
                if (k.Length == 0) continue;
                var zip = NormalizeZip(h.Zip);
                if (!tmp.TryGetValue(k, out var list)) { tmp[k] = list = new List<CompanyHit>(1); names++; }
                if (list.Any(x => x.Zip == zip)) continue;
                list.Add(h with { Zip = zip, City = string.IsNullOrWhiteSpace(h.City) ? null : h.City.Trim() });
            }
            return new Layer(tmp.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray(), StringComparer.Ordinal), names);
        }

        private static string? NormalizeZip(string? zip)
        {
            if (string.IsNullOrWhiteSpace(zip)) return null;
            var z = zip.Trim();
            if (z.StartsWith("D-", StringComparison.OrdinalIgnoreCase)) z = z[2..].Trim();
            return z.Length == 5 && z.All(char.IsDigit) ? z : null;
        }

        private static readonly Regex ZipRegex = new(@"(?<![\d.,])(?:D\s?-\s?)?(\d{5})(?![\d,]|\.\d)", RegexOptions.Compiled);

        /// <summary>Alle gültigen deutschen PLZ im Text.</summary>
        public static HashSet<string> ZipsInText(string? text)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(text)) return set;
            foreach (Match m in ZipRegex.Matches(text))
            {
                var z = m.Groups[1].Value;
                if (DocumentEngine.Plz.IsValid(z)) set.Add(z);
            }
            return set;
        }

        // ---------- Abfrage ----------

        /// <summary>Alle Einträge zum Namen (eigene Datenbank, Gelerntes, GLEIF).</summary>
        public static List<CompanyHit> Lookup(string? name)
        {
            var k = Key(name);
            var r = new List<CompanyHit>();
            if (k.Length == 0) return r;
            foreach (var layer in new[] { _db, _learned, _gleif })
                if (layer.Map.TryGetValue(k, out var hits)) r.AddRange(hits);
            return r;
        }

        /// <summary>
        /// Pluspunkte für einen Firmenkandidaten: bekannt aus eigener DB/Gelerntem +1, zusätzlich eine ihrer PLZ im Text +3;
        /// nur in GLEIF +0,5 bzw. mit PLZ im Text +2. Unbekannt = 0 (nie negativ).
        /// </summary>
        public static double Bonus(string? candidate, ISet<string> zipsInText)
        {
            double best = 0;
            foreach (var h in Lookup(candidate))
            {
                bool zipHit = h.Zip != null && zipsInText.Contains(h.Zip);
                double b = h.Source == CompanySource.Gleif ? (zipHit ? 2 : 0.5) : (zipHit ? 3 : 1);
                if (b > best) best = b;
            }
            return best;
        }

        // ---------- Eigene Datenbank (Plugin) ----------

        private sealed class DbCache
        {
            public DateTime Loaded { get; set; }
            public List<LearnedEntry> Entries { get; set; } = new();
        }

        /// <summary>Firmen aus der eigenen Datenbank übernehmen und für den nächsten Start zwischenspeichern.</summary>
        public static void SetDatabase(IEnumerable<Plugins.CompanyRecord> records)
        {
            var list = records.Where(r => !string.IsNullOrWhiteSpace(r.Name))
                .Select(r => new LearnedEntry { Name = r.Name.Trim(), Zip = NormalizeZip(r.Zip), City = r.City?.Trim() })
                .ToList();
            _db = Build(list.Select(e => new CompanyHit(e.Name, e.Zip, e.City, CompanySource.Datenbank)));
            DbLoaded = DateTime.Now;
            try { SecureStorage.WriteAllText(DbCachePath, JsonSerializer.Serialize(new DbCache { Loaded = DbLoaded.Value, Entries = list })); } catch { }
            Changed?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>Datenbank-Stand und Gelerntes im Speicher verwerfen (nach "Lerndaten leeren"); GLEIF bleibt.</summary>
        public static void ResetLocal()
        {
            _db = Layer.Empty; DbLoaded = null;
            lock (_learnLock) { _learnedList = new(); _learned = Layer.Empty; }
            Changed?.Invoke(null, EventArgs.Empty);
        }

        // ---------- Gelernt ----------

        public sealed class LearnedEntry
        {
            public string Name { get; set; } = "";
            public string? Zip { get; set; }
            public string? City { get; set; }
        }

        /// <summary>Vom Benutzer bestätigte Firma mit Absender-PLZ merken.</summary>
        public static void Learn(string? name, string? zip, string? city)
        {
            var z = NormalizeZip(zip);
            if (string.IsNullOrWhiteSpace(name) || z == null || Key(name).Length == 0) return;
            var n = name.Trim();
            lock (_learnLock)
            {
                if (_learnedList.Any(e => e.Zip == z && string.Equals(Key(e.Name), Key(n), StringComparison.Ordinal))) return;
                _learnedList.Add(new LearnedEntry { Name = n, Zip = z, City = city?.Trim() });
                _learned = Build(_learnedList.Select(e => new CompanyHit(e.Name, e.Zip, e.City, CompanySource.Gelernt)));
                try { SecureStorage.WriteAllText(LearnedPath, JsonSerializer.Serialize(_learnedList)); } catch { }
            }
        }

        // ---------- GLEIF ----------

        public sealed class GleifInfo
        {
            public string Url { get; set; } = "";
            public DateTime Publish { get; set; }
            public DateTime Retrieved { get; set; }
            public long Size { get; set; }
            public string Sha256 { get; set; } = "";
            public int Companies { get; set; }
            public int Records { get; set; }
        }

        public static GleifInfo? GleifSource
        {
            get
            {
                try { return File.Exists(GleifInfoPath) ? JsonSerializer.Deserialize<GleifInfo>(SecureStorage.ReadAllText(GleifInfoPath)) : null; }
                catch { return null; }
            }
        }

        private static void LoadGleif()
        {
            if (!File.Exists(GleifPath)) { _gleif = Layer.Empty; return; }
            var bytes = SecureStorage.ReadAllBytes(GleifPath);
            _gleif = Build(ReadGleifLines(bytes));
        }

        private static IEnumerable<CompanyHit> ReadGleifLines(byte[] bytes)
        {
            using var gz = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress);
            using var r = new StreamReader(gz, Encoding.UTF8);
            // PLZ und Orte wiederholen sich tausendfach – nur einmal im Speicher halten.
            var pool = new Dictionary<string, string>(StringComparer.Ordinal);
            string Share(string s) => pool.TryGetValue(s, out var x) ? x : pool[s] = s;
            string? line;
            while ((line = r.ReadLine()) != null)
            {
                var p = line.Split('\t');
                if (p.Length >= 3) yield return new CompanyHit(p[0], Share(p[1]), Share(p[2]), CompanySource.Gleif);
            }
        }

        public static void RemoveGleif()
        {
            if (File.Exists(GleifPath)) File.Delete(GleifPath);
            if (File.Exists(GleifInfoPath)) File.Delete(GleifInfoPath);
            _gleif = Layer.Empty;
            Changed?.Invoke(null, EventArgs.Empty);
        }

        public const string GleifApi = "https://goldencopy.gleif.org/api/v2/golden-copies/publishes/latest";
        private const string GleifHost = "goldencopy.gleif.org";
        private const long MaxGleifBytes = 2L * 1024 * 1024 * 1024;
        private const int MinGleifCompanies = 20_000, MaxGleifCompanies = 3_000_000;

        /// <summary>Laufender GLEIF-Import (null = keiner). Läuft weiter, auch wenn das Fenster geschlossen wird.</summary>
        public static Task? GleifJob { get; private set; }
        private static CancellationTokenSource? _gleifCts;
        public static string GleifStatus { get; private set; } = "";
        public static double GleifProgress { get; private set; }
        public static event EventHandler? GleifProgressChanged;

        private static void Report(string text, double pct)
        {
            GleifStatus = text; GleifProgress = pct;
            GleifProgressChanged?.Invoke(null, EventArgs.Empty);
        }

        public static void CancelGleif() => _gleifCts?.Cancel();

        /// <summary>GLEIF-Gesamtdatei (Golden Copy, CC0) laden, nur aktive deutsche Firmen übernehmen. Ergebnis: Meldung.</summary>
        public static Task StartGleifImport()
        {
            if (GleifJob is { IsCompleted: false }) return GleifJob;
            _gleifCts = new CancellationTokenSource();
            var ct = _gleifCts.Token;
            GleifJob = Task.Run(async () =>
            {
                try { await ImportGleifAsync(ct); }
                catch (OperationCanceledException) { Report("GLEIF: abgebrochen.", 0); }
                catch (Exception ex) { Report("GLEIF: Fehler – " + ex.Message, 0); }
            });
            return GleifJob;
        }

        private static async Task ImportGleifAsync(CancellationToken ct)
        {
            Report("GLEIF: aktuelle Veröffentlichung wird abgefragt…", 0);
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Dateiumbenenner");

            string url; long size; int records; DateTime publish;
            using (var meta = await http.GetAsync(GleifApi, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                CheckResponse(meta);
                var json = await meta.Content.ReadAsStringAsync(ct);
                if (json.Length > 1_000_000) throw new InvalidOperationException("Antwort der GLEIF-Schnittstelle ist unplausibel groß.");
                using var doc = JsonDocument.Parse(json);
                var data = doc.RootElement.GetProperty("data");
                var csv = data.GetProperty("lei2").GetProperty("full_file").GetProperty("csv");
                url = csv.GetProperty("url").GetString() ?? "";
                size = csv.GetProperty("size").GetInt64();
                records = csv.TryGetProperty("record_count", out var rc) ? rc.GetInt32() : 0;
                publish = DateTime.TryParse(data.GetProperty("publish_date").GetString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var p) ? p : DateTime.Now;
            }
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !uri.Host.Equals(GleifHost, StringComparison.OrdinalIgnoreCase)
                || !uri.AbsolutePath.StartsWith("/storage/golden-copy-files/", StringComparison.Ordinal) || !uri.AbsolutePath.EndsWith(".csv.zip", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Unerwartete Download-Adresse: {url}");
            if (size <= 0 || size > MaxGleifBytes) throw new InvalidOperationException($"Unplausible Dateigröße: {size:N0} Byte.");

            var tmp = Path.Combine(Path.GetTempPath(), $"gleif_{Guid.NewGuid():N}.csv.zip");
            try
            {
                string sha;
                using (var resp = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct))
                {
                    CheckResponse(resp);
                    if (resp.Content.Headers.ContentLength is long cl && cl != size) throw new InvalidOperationException($"Dateigröße weicht ab ({cl:N0} statt {size:N0} Byte).");
                    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    await using var net = await resp.Content.ReadAsStreamAsync(ct);
                    await using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 20, true))
                    {
                        var buf = new byte[1 << 20];
                        long total = 0; int n; var sw = System.Diagnostics.Stopwatch.StartNew();
                        while ((n = await net.ReadAsync(buf, ct)) > 0)
                        {
                            total += n;
                            if (total > size) throw new InvalidOperationException("Download größer als angekündigt – abgebrochen.");
                            hash.AppendData(buf, 0, n);
                            await fs.WriteAsync(buf.AsMemory(0, n), ct);
                            if (sw.ElapsedMilliseconds > 300) { Report($"GLEIF: Download {total / 1048576.0:N0} / {size / 1048576.0:N0} MB", 60.0 * total / size); sw.Restart(); }
                        }
                        if (total != size) throw new InvalidOperationException($"Download unvollständig ({total:N0} von {size:N0} Byte).");
                    }
                    sha = Convert.ToHexString(hash.GetHashAndReset());
                }

                Report("GLEIF: deutsche Firmen werden herausgesucht…", 60);
                var sb = new StringBuilder();
                int companies = 0, seen = 0;
                using (var zip = ZipFile.OpenRead(tmp))
                {
                    var entry = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidOperationException("Keine CSV-Datei im Archiv.");
                    using var reader = new StreamReader(entry.Open(), Encoding.UTF8, true, 1 << 20);
                    var csv = new CsvReader(reader);
                    var header = csv.Read(int.MaxValue) ?? throw new InvalidOperationException("CSV ist leer.");
                    int Col(string name) { int i = header.IndexOf(name); if (i < 0) throw new InvalidOperationException($"Spalte {name} fehlt – Format geändert?"); return i; }
                    int cName = Col("Entity.LegalName"), cOther = Col("Entity.OtherEntityNames.OtherEntityName.1"),
                        cCity = Col("Entity.LegalAddress.City"), cCountry = Col("Entity.LegalAddress.Country"),
                        cZip = Col("Entity.LegalAddress.PostalCode"), cStatus = Col("Entity.EntityStatus");
                    int max = new[] { cName, cOther, cCity, cCountry, cZip, cStatus }.Max();
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    List<string>? f;
                    while ((f = csv.Read(max)) != null)
                    {
                        ct.ThrowIfCancellationRequested();
                        seen++;
                        if (sw.ElapsedMilliseconds > 300)
                        {
                            Report($"GLEIF: {seen:N0} Einträge geprüft, {companies:N0} deutsche Firmen", 60 + (records > 0 ? 39.0 * Math.Min(1, (double)seen / records) : 0));
                            sw.Restart();
                        }
                        if (f.Count <= max || f[cCountry] != "DE" || f[cStatus] != "ACTIVE") continue;
                        var z = NormalizeZip(f[cZip]);
                        if (z == null) continue;
                        static string Clean(string s) => s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();
                        var city = Clean(f[cCity]);
                        foreach (var name in new[] { f[cName], f[cOther] })
                            if (!string.IsNullOrWhiteSpace(name)) sb.Append(Clean(name)).Append('\t').Append(z).Append('\t').Append(city).Append('\n');
                        companies++;
                    }
                }
                if (records > 0 && seen < records * 0.9) throw new InvalidOperationException($"Nur {seen:N0} von {records:N0} Einträgen gelesen – Datei unvollständig?");
                if (companies < MinGleifCompanies || companies > MaxGleifCompanies)
                    throw new InvalidOperationException($"Unplausible Anzahl deutscher Firmen: {companies:N0}. Nichts übernommen.");

                Report("GLEIF: wird gespeichert…", 99);
                using var ms = new MemoryStream();
                using (var gz = new GZipStream(ms, CompressionLevel.Optimal, true))
                {
                    var b = Encoding.UTF8.GetBytes(sb.ToString());
                    gz.Write(b, 0, b.Length);
                }
                var bytes = ms.ToArray();
                var layer = Build(ReadGleifLines(bytes));
                Directory.CreateDirectory(Path.GetDirectoryName(GleifPath)!);
                SecureStorage.WriteAllBytes(GleifPath, bytes);
                var info = new GleifInfo { Url = url, Publish = publish, Retrieved = DateTime.Now, Size = size, Sha256 = sha, Companies = companies, Records = seen };
                SecureStorage.WriteAllText(GleifInfoPath, JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true }));
                _gleif = layer;
                Report($"GLEIF fertig: {companies:N0} aktive deutsche Firmen (Stand {publish:dd.MM.yyyy}).", 100);
                Changed?.Invoke(null, EventArgs.Empty);
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
        }

        private static void CheckResponse(HttpResponseMessage resp)
        {
            if ((int)resp.StatusCode is >= 300 and < 400)
                throw new InvalidOperationException($"Der Server leitet auf eine andere Adresse um ({resp.Headers.Location}). Aus Sicherheitsgründen abgebrochen.");
            resp.EnsureSuccessStatusCode();
            var u = resp.RequestMessage?.RequestUri;
            if (u == null || u.Scheme != Uri.UriSchemeHttps || !u.Host.Equals(GleifHost, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Unerwartete Quelle: {u}");
        }

        /// <summary>Einfacher CSV-Leser (RFC 4180, Anführungszeichen, Zeilenumbrüche in Feldern); Felder hinter maxCol werden übersprungen.</summary>
        internal sealed class CsvReader
        {
            private readonly TextReader _r;
            private readonly char[] _buf = new char[1 << 16];
            private int _pos, _len;
            private readonly StringBuilder _sb = new();
            public CsvReader(TextReader r) => _r = r;

            private int Next()
            {
                if (_pos >= _len) { _len = _r.Read(_buf, 0, _buf.Length); _pos = 0; if (_len <= 0) return -1; }
                return _buf[_pos++];
            }
            private int Peek()
            {
                if (_pos >= _len) { _len = _r.Read(_buf, 0, _buf.Length); _pos = 0; if (_len <= 0) return -1; }
                return _buf[_pos];
            }

            public List<string>? Read(int maxCol)
            {
                var fields = new List<string>();
                int c = Next();
                if (c == -1) return null;
                while (true)
                {
                    bool keep = fields.Count <= maxCol;
                    _sb.Clear();
                    if (c == '"')
                    {
                        while (true)
                        {
                            c = Next();
                            if (c == -1) break;
                            if (c == '"')
                            {
                                if (Peek() == '"') { Next(); if (keep) _sb.Append('"'); continue; }
                                c = Next();
                                break;
                            }
                            if (keep) _sb.Append((char)c);
                        }
                    }
                    else
                    {
                        while (c != -1 && c != ',' && c != '\n' && c != '\r') { if (keep) _sb.Append((char)c); c = Next(); }
                    }
                    if (keep) fields.Add(_sb.ToString());
                    if (c == ',') { c = Next(); continue; }
                    if (c == '\r' && Peek() == '\n') Next();
                    return fields;
                }
            }
        }
    }
}
