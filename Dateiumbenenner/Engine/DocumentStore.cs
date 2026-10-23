using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Dateiumbenenner
{
    /// <summary>
    /// Interne SQLite-Datenbank je Profil. Speichert pro Datei Text und Auswertungsergebnisse,
    /// damit beim nächsten Laden nur neue/geänderte Dateien ausgewertet werden (inkrementell).
    /// Verschobene/umbenannte Dateien werden über den Inhalts-Hash wiedergefunden (differenziell);
    /// manuell gesetzte Werte bleiben dabei erhalten.
    /// </summary>
    public static class DocumentStore
    {
        public const string FileName = "dokumente.db";
        /// <summary>Erhöhen, wenn sich die Erkennungslogik ändert – dann werden alle Dateien neu ausgewertet.</summary>
        public const int ExtractorVersion = 2;

        private static readonly object Sync = new();
        private static string? _connString;

        private static string ConnectionString
        {
            get
            {
                if (_connString != null) return _connString;
                var dir = ProfileManager.ActiveDirectory;
                Directory.CreateDirectory(dir);
                _connString = new SqliteConnectionStringBuilder { DataSource = Path.Combine(dir, FileName), Cache = SqliteCacheMode.Shared }.ToString();
                using var c = Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = @"PRAGMA journal_mode=WAL;
CREATE TABLE IF NOT EXISTS Documents(
  Path TEXT PRIMARY KEY COLLATE NOCASE,
  Size INTEGER NOT NULL,
  Ticks INTEGER NOT NULL,
  ContentHash TEXT NOT NULL,
  SettingsKey TEXT NOT NULL,
  ExtractorVersion INTEGER NOT NULL,
  Data TEXT NOT NULL,
  Updated TEXT NOT NULL);
CREATE INDEX IF NOT EXISTS IX_Documents_Hash ON Documents(ContentHash, Size);";
                cmd.ExecuteNonQuery();
                return _connString;
            }
        }

        private static SqliteConnection Open()
        {
            var c = new SqliteConnection(_connString);
            c.Open();
            ApplyKey(c, Dateiumbenenner.Engine.SecureStorage.DatabaseKey);
            // Im WAL-Modus sicher und deutlich schneller: kein Warten auf die Platte bei jedem Commit
            using var p = c.CreateCommand();
            p.CommandText = "PRAGMA synchronous=NORMAL;";
            p.ExecuteNonQuery();
            return c;
        }

        private static void ApplyKey(SqliteConnection c, string? hexKey)
        {
            if (hexKey == null) return;
            using var k = c.CreateCommand();
            k.CommandText = $"PRAGMA key = \"{hexKey}\";";
            k.ExecuteNonQuery();
        }

        /// <summary>Wandelt eine Datenbankdatei zwischen unverschlüsselt und verschlüsselt (SQLCipher) um.</summary>
        internal static void Rekey(string dbPath, byte[]? from, byte[]? to)
        {
            string? Hex(byte[]? k) => k == null ? null : "x'" + Convert.ToHexString(k) + "'";
            var tmp = dbPath + ".neu";
            if (File.Exists(tmp)) File.Delete(tmp);
            var cs = new SqliteConnectionStringBuilder { DataSource = dbPath, Pooling = false }.ToString();
            using (var c = new SqliteConnection(cs))
            {
                c.Open();
                ApplyKey(c, Hex(from));
                using var cmd = c.CreateCommand();
                cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                cmd.ExecuteNonQuery();
                cmd.CommandText = $"ATTACH DATABASE $f AS target KEY \"{Hex(to) ?? ""}\";";
                cmd.Parameters.AddWithValue("$f", tmp);
                cmd.ExecuteNonQuery();
                cmd.Parameters.Clear();
                cmd.CommandText = "SELECT sqlcipher_export('target'); DETACH DATABASE target;";
                cmd.ExecuteNonQuery();
            }
            File.Move(tmp, dbPath, true);
            foreach (var ext in new[] { "-wal", "-shm" }) if (File.Exists(dbPath + ext)) File.Delete(dbPath + ext);
            _connString = null;
            _fts = null;
        }

        /// <summary>Gespeicherter Zustand einer Datei.</summary>
        public class Record
        {
            public string? FullText { get; set; }
            public int PageCount { get; set; }
            public string? CompanyName { get; set; }
            public List<string> CompanyCandidates { get; set; } = new();
            public string? DocumentType { get; set; }
            public string? InvoiceNumber { get; set; }
            public string? DocumentNumber { get; set; }
            public string? Date { get; set; }
            public string? SelectedNumber { get; set; }
            public string? SelectedDate { get; set; }
            public List<string> NumberCandidates { get; set; } = new();
            public List<string> DateCandidates { get; set; } = new();
            public string? Street { get; set; }
            public string? Zip { get; set; }
            public string? City { get; set; }
            /// <summary>Adresse per „Firma übertragen“ bestätigt (auch leer) – nicht neu ermitteln.</summary>
            public bool AddressManuallySet { get; set; }
            public bool NumberManuallySet { get; set; }
            public bool DateManuallySet { get; set; }
            public bool DocTypeManuallySet { get; set; }
            public bool CompanyConfirmed { get; set; }
            /// <summary>Dateinamen-Aufbau der Datei (Trennzeichen, Segment, Einfügepositionen), z. B. aus einer übernommenen Vorlage.</summary>
            public Dateiumbenenner.Engine.DocTemplate? Layout { get; set; }
            /// <summary>Vorlage, deren Übernahme für diese Datei abgelehnt wurde (nicht erneut fragen).</summary>
            public string? TemplateDeclined { get; set; }
        }

        /// <summary>Bereits berechnete Teil-Hashes (gültig, solange Größe und Änderungszeit gleich sind).</summary>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (long Size, long Ticks, string Hash)> StampCache = new(StringComparer.OrdinalIgnoreCase);

        public static string Stamp(string path, out long size, out long ticks)
        {
            var fi = new FileInfo(path);
            size = fi.Length;
            ticks = fi.LastWriteTimeUtc.Ticks;
            if (StampCache.TryGetValue(fi.FullName, out var s) && s.Size == size && s.Ticks == ticks) return s.Hash;
            var hash = PartialHash(path, size);
            StampCache[fi.FullName] = (size, ticks, hash);
            return hash;
        }

        private static string PartialHash(string path, long length)
        {
            const int chunk = 64 * 1024;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buf = new byte[chunk];
            int n = fs.ReadAtLeast(buf, (int)Math.Min(chunk, length), false);
            sha.AppendData(buf, 0, n);
            if (length > chunk)
            {
                fs.Seek(Math.Max(chunk, length - chunk), SeekOrigin.Begin);
                n = fs.ReadAtLeast(buf, chunk, false);
                sha.AppendData(buf, 0, n);
            }
            return Convert.ToHexString(sha.GetHashAndReset());
        }

        /// <summary>
        /// Liefert den gespeicherten Datensatz. <paramref name="upToDate"/> = Datei, Einstellungen und Erkennungslogik unverändert
        /// (Ergebnis kann direkt verwendet werden). Sonst ggf. nur ein Datensatz zur Übernahme manueller Werte (Datei verschoben/geändert).
        /// </summary>
        public static Record? TryGet(string path, string settingsKey, out bool upToDate)
        {
            upToDate = false;
            try
            {
                var full = Path.GetFullPath(path);
                // Noch nicht geschriebener Stand hat Vorrang (wird mit der aktuellen Datei geschrieben)
                PendingDoc? pd;
                lock (PendingSync) PendingDocs.TryGetValue(full, out pd);
                if (pd != null)
                {
                    upToDate = pd.SettingsKey == settingsKey;
                    return JsonSerializer.Deserialize(pd.Data, StoreJsonContext.Default.Record);
                }
                var fi = new FileInfo(full);
                long size = fi.Length, ticks = fi.LastWriteTimeUtc.Ticks;
                if (!TakeDoc(full, out var row))
                {
                    lock (Sync)
                    {
                        _ = ConnectionString;
                        using var c = Open();
                        using var cmd = c.CreateCommand();
                        cmd.CommandText = "SELECT Size, Ticks, ContentHash, SettingsKey, ExtractorVersion, Data FROM Documents WHERE Path=$p";
                        cmd.Parameters.AddWithValue("$p", full);
                        using var r = cmd.ExecuteReader();
                        row = r.Read() ? new DocRow(r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetString(3), r.GetInt32(4), r.GetString(5)) : null;
                    }
                }
                if (row != null)
                {
                    var rec = JsonSerializer.Deserialize(row.Data, StoreJsonContext.Default.Record);
                    bool same = row.Size == size && row.SettingsKey == settingsKey && row.Version == ExtractorVersion;
                    // Schnellpfad: Größe + Änderungszeit gleich -> kein Lesen der Datei nötig
                    if (row.Size == size && row.Ticks == ticks) StampCache[full] = (size, ticks, row.Hash);
                    if (same && row.Ticks == ticks) { upToDate = true; return rec; }
                    upToDate = same && row.Hash == Stamp(full, out _, out _);
                    return rec;
                }
                var hash = Stamp(full, out _, out _);
                lock (Sync)
                {
                    _ = ConnectionString;
                    using var c = Open();
                    using var cmd = c.CreateCommand();
                    // Datei verschoben/umbenannt: gleicher Inhalt unter anderem Pfad
                    cmd.CommandText = "SELECT SettingsKey, ExtractorVersion, Data FROM Documents WHERE ContentHash=$h AND Size=$s ORDER BY Updated DESC LIMIT 1";
                    cmd.Parameters.AddWithValue("$h", hash);
                    cmd.Parameters.AddWithValue("$s", size);
                    using var r2 = cmd.ExecuteReader();
                    if (!r2.Read()) return null;
                    upToDate = r2.GetString(0) == settingsKey && r2.GetInt32(1) == ExtractorVersion;
                    return JsonSerializer.Deserialize(r2.GetString(2), StoreJsonContext.Default.Record);
                }
            }
            catch
            {
                return null;
            }
        }

        public static void Save(string path, string settingsKey, Record rec)
        {
            try
            {
                if (!File.Exists(path)) return;
                var full = Path.GetFullPath(path);
                var doc = new PendingDoc(full, settingsKey, JsonSerializer.Serialize(rec, StoreJsonContext.Default.Record), SearchText(rec));
                lock (PendingSync) PendingDocs[full] = doc;
                StartFlusher();
            }
            catch
            {
            }
        }

        private sealed record PendingDoc(string FullPath, string SettingsKey, string Data, string Text);
        private static readonly Dictionary<string, PendingDoc> PendingDocs = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Wartende Belegstände gebündelt schreiben (Datensatz + Volltextindex in einer Transaktion je Block).</summary>
        private static void FlushDocuments()
        {
            List<PendingDoc> batch;
            lock (PendingSync) batch = PendingDocs.Values.ToList();
            foreach (var chunk in batch.Chunk(500))
            {
                var rows = new List<(PendingDoc D, string Hash, long Size, long Ticks)>();
                foreach (var d in chunk)
                {
                    try { if (File.Exists(d.FullPath)) rows.Add((d, Stamp(d.FullPath, out var size, out var ticks), size, ticks)); }
                    catch { }
                }
                try
                {
                    lock (Sync)
                    {
                        _ = ConnectionString;
                        using var c = Open();
                        bool fts = EnsureFts(c);
                        using var tx = c.BeginTransaction();
                        using var cmd = c.CreateCommand();
                        cmd.Transaction = tx;
                        cmd.CommandText = @"INSERT INTO Documents(Path,Size,Ticks,ContentHash,SettingsKey,ExtractorVersion,Data,Updated)
VALUES($p,$s,$t,$h,$k,$v,$d,$u)
ON CONFLICT(Path) DO UPDATE SET Size=$s,Ticks=$t,ContentHash=$h,SettingsKey=$k,ExtractorVersion=$v,Data=$d,Updated=$u";
                        var pp = cmd.Parameters.Add("$p", SqliteType.Text);
                        var ps = cmd.Parameters.Add("$s", SqliteType.Integer);
                        var pt = cmd.Parameters.Add("$t", SqliteType.Integer);
                        var ph = cmd.Parameters.Add("$h", SqliteType.Text);
                        var pk = cmd.Parameters.Add("$k", SqliteType.Text);
                        var pv = cmd.Parameters.Add("$v", SqliteType.Integer);
                        var pd = cmd.Parameters.Add("$d", SqliteType.Text);
                        var pu = cmd.Parameters.Add("$u", SqliteType.Text);
                        using var del = c.CreateCommand();
                        del.Transaction = tx;
                        del.CommandText = "DELETE FROM DocText WHERE Path=$p";
                        var dp = del.Parameters.Add("$p", SqliteType.Text);
                        using var ins = c.CreateCommand();
                        ins.Transaction = tx;
                        ins.CommandText = "INSERT INTO DocText(Path,Text) VALUES($p,$t)";
                        var ip = ins.Parameters.Add("$p", SqliteType.Text);
                        var it = ins.Parameters.Add("$t", SqliteType.Text);
                        var now = DateTime.UtcNow.ToString("O");
                        foreach (var (d, h, size, ticks) in rows)
                        {
                            // Inzwischen überholt oder verschoben: der aktuelle Eintrag wird im nächsten Durchgang geschrieben
                            if (!IsCurrent(d)) continue;
                            pp.Value = d.FullPath; ps.Value = size; pt.Value = ticks; ph.Value = h; pk.Value = d.SettingsKey; pv.Value = ExtractorVersion; pd.Value = d.Data; pu.Value = now;
                            cmd.ExecuteNonQuery();
                            if (fts)
                            {
                                dp.Value = d.FullPath; del.ExecuteNonQuery();
                                ip.Value = d.FullPath; it.Value = d.Text; ins.ExecuteNonQuery();
                            }
                        }
                        tx.Commit();
                        foreach (var r in rows) ForgetDoc(r.D.FullPath);
                    }
                }
                catch
                {
                }
                lock (PendingSync)
                    foreach (var d in chunk)
                        if (PendingDocs.TryGetValue(d.FullPath, out var cur) && ReferenceEquals(cur, d)) PendingDocs.Remove(d.FullPath);
            }
        }

        private static bool IsCurrent(PendingDoc d)
        {
            lock (PendingSync) return PendingDocs.TryGetValue(d.FullPath, out var cur) && ReferenceEquals(cur, d);
        }

        /// <summary>Alle wartenden Schreibvorgänge (Belegstände und Plugin-Daten) sofort ausführen.</summary>
        public static void Flush()
        {
            FlushDocuments();
            FlushPluginData();
        }

        private static bool? _fts;
        /// <summary>True, wenn der Volltext-Suchindex bereits erstellt und geöffnet ist.</summary>
        public static bool IsFullTextIndexReady => _fts == true;
        private static string SearchText(Record rec) => string.Join("\n", rec.CompanyName, rec.FullText);

        /// <summary>Volltextindex (FTS5, Trigramm = Teilwortsuche) anlegen und einmalig aus vorhandenen Datensätzen füllen.</summary>
        private static bool EnsureFts(SqliteConnection c, IProgress<(int Done, int Total)>? progress = null)
        {
            if (_fts.HasValue) return _fts.Value;
            try
            {
                using var cmd = c.CreateCommand();
                cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE name='DocText'";
                bool exists = Convert.ToInt64(cmd.ExecuteScalar()) > 0;
                if (!exists)
                {
                    cmd.CommandText = "CREATE VIRTUAL TABLE DocText USING fts5(Path UNINDEXED, Text, tokenize='trigram case_sensitive 0')";
                    cmd.ExecuteNonQuery();
                    var rows = new List<(string, string)>();
                    cmd.CommandText = "SELECT count(*) FROM Documents";
                    int total = Convert.ToInt32(cmd.ExecuteScalar()), done = 0;
                    progress?.Report((0, total));
                    cmd.CommandText = "SELECT Path, Data FROM Documents";
                    using (var r = cmd.ExecuteReader())
                        while (r.Read())
                        {
                            var rec = JsonSerializer.Deserialize(r.GetString(1), StoreJsonContext.Default.Record);
                            if (rec != null) rows.Add((r.GetString(0), SearchText(rec)));
                            if (++done % 200 == 0) progress?.Report((done, total));
                        }
                    progress?.Report((total, total));
                    using var tx = c.BeginTransaction();
                    using var ins = c.CreateCommand();
                    ins.Transaction = tx;
                    ins.CommandText = "INSERT INTO DocText(Path,Text) VALUES($p,$t)";
                    var pp = ins.Parameters.Add("$p", SqliteType.Text);
                    var pt = ins.Parameters.Add("$t", SqliteType.Text);
                    foreach (var (p, t) in rows) { pp.Value = p; pt.Value = t; ins.ExecuteNonQuery(); }
                    tx.Commit();
                }
                _fts = true;
            }
            catch
            {
                _fts = false;
            }
            return _fts.Value;
        }

        /// <summary>Volltextindex vorab aufbauen (z. B. beim ersten eingegebenen Zeichen), ohne zu suchen.</summary>
        public static bool EnsureFullTextIndex(IProgress<(int Done, int Total)>? progress = null)
        {
            if (_fts.HasValue) return _fts.Value;
            try
            {
                lock (Sync)
                {
                    _ = ConnectionString;
                    using var c = Open();
                    return EnsureFts(c, progress);
                }
            }
            catch { return false; }
        }

        /// <summary>Pfade aller Belege, deren Text den Suchbegriff enthält (mind. 3 Zeichen). null = Index nicht verfügbar.</summary>
        public static HashSet<string>? SearchFullText(string query, IProgress<(int Done, int Total)>? progress = null)
        {
            if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 3) return null;
            if (_fts != true && !EnsureFullTextIndex(progress)) return null;
            try
            {
                // Eigene Leseverbindung ohne globale Sperre: WAL erlaubt Lesen parallel zu laufenden Schreibvorgängen
                _ = ConnectionString;
                using var c = new SqliteConnection(new SqliteConnectionStringBuilder(_connString) { Cache = SqliteCacheMode.Private }.ToString());
                c.Open();
                ApplyKey(c, Dateiumbenenner.Engine.SecureStorage.DatabaseKey);
                using var cmd = c.CreateCommand();
                cmd.CommandText = "SELECT Path FROM DocText WHERE DocText MATCH $q";
                cmd.Parameters.AddWithValue("$q", "Text:\"" + query.Trim().Replace("\"", "\"\"") + "\"");
                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using var r = cmd.ExecuteReader();
                while (r.Read()) set.Add(r.GetString(0));
                return set;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Plugin-Daten lesen (noch nicht geschriebene Werte aus der Warteschlange haben Vorrang).</summary>
        public static string? LoadPluginData(string pluginId, string path)
        {
            try
            {
                var full = Path.GetFullPath(path);
                var key = PendingKey(pluginId, full);
                lock (PendingSync)
                    if (Pending.TryGetValue(key, out var p)) return p.Data;
                var fi = new FileInfo(full);
                long size = fi.Length, ticks = fi.LastWriteTimeUtc.Ticks;
                if (!TakePlugin(key, out var row))
                {
                    lock (Sync)
                    {
                        EnsurePluginTable();
                        using var c = Open();
                        using var cmd = c.CreateCommand();
                        cmd.CommandText = "SELECT ContentHash, Size, Ticks, Data FROM PluginData WHERE PluginId=$i AND Path=$p";
                        cmd.Parameters.AddWithValue("$i", pluginId);
                        cmd.Parameters.AddWithValue("$p", full);
                        using var r = cmd.ExecuteReader();
                        row = r.Read() ? new PluginRow(r.GetString(0), r.GetInt64(1), r.GetInt64(2), r.GetString(3)) : null;
                    }
                }
                if (row == null || row.Size != size) return null;
                // Schnellpfad: Größe + Änderungszeit gleich -> Datei muss nicht gelesen werden
                if (row.Ticks == ticks) { StampCache[full] = (size, ticks, row.Hash); return row.Data; }
                return row.Hash == Stamp(full, out _, out _) ? row.Data : null;
            }
            catch
            {
                return null;
            }
        }

        private sealed record PendingWrite(string PluginId, string FullPath, string Data);
        private static readonly object PendingSync = new();
        private static readonly Dictionary<string, PendingWrite> Pending = new(StringComparer.OrdinalIgnoreCase);
        private static bool _flushRunning, _exitHooked;
        private static string PendingKey(string pluginId, string fullPath) => pluginId + "\u0001" + fullPath;

        /// <summary>Plugin-Daten speichern: wird gesammelt und gebündelt in einer Transaktion geschrieben
        /// (tausende Einzelschreibvorgänge würden die Datenbank – und damit die Suche – minutenlang blockieren).</summary>
        public static void SavePluginData(string pluginId, string path, string data)
        {
            try
            {
                var full = Path.GetFullPath(path);
                lock (PendingSync) Pending[PendingKey(pluginId, full)] = new PendingWrite(pluginId, full, data);
                StartFlusher();
            }
            catch
            {
            }
        }

        private static void StartFlusher()
        {
            lock (PendingSync)
            {
                if (!_exitHooked) { _exitHooked = true; AppDomain.CurrentDomain.ProcessExit += (_, _) => Flush(); }
                if (_flushRunning) return;
                _flushRunning = true;
            }
            Task.Run(FlushLoop);
        }

        private static void FlushLoop()
        {
            while (true)
            {
                Thread.Sleep(250);
                lock (PendingSync)
                    if (Pending.Count == 0 && PendingDocs.Count == 0) { _flushRunning = false; return; }
                try { Flush(); }
                catch { }
            }
        }

        /// <summary>Alle wartenden Plugin-Daten sofort schreiben (in Blöcken, damit Lesezugriffe dazwischen drankommen).</summary>
        public static void FlushPluginData()
        {
            List<KeyValuePair<string, PendingWrite>> batch;
            lock (PendingSync) batch = new(Pending);
            if (batch.Count == 0) return;
            var stamps = new Dictionary<string, (string Hash, long Size, long Ticks)?>(StringComparer.OrdinalIgnoreCase);
            foreach (var chunk in batch.Chunk(500))
            {
                var rows = new List<(PendingWrite W, string Hash, long Size, long Ticks)>();
                foreach (var (_, w) in chunk)
                {
                    if (!stamps.TryGetValue(w.FullPath, out var st))
                    {
                        try { st = File.Exists(w.FullPath) ? (Stamp(w.FullPath, out var size, out var ticks), size, ticks) : null; }
                        catch { st = null; }
                        stamps[w.FullPath] = st;
                    }
                    if (st is { } s) rows.Add((w, s.Hash, s.Size, s.Ticks));
                }
                try
                {
                    lock (Sync)
                    {
                        EnsurePluginTable();
                        using var c = Open();
                        using var tx = c.BeginTransaction();
                        using var cmd = c.CreateCommand();
                        cmd.Transaction = tx;
                        cmd.CommandText = @"INSERT INTO PluginData(PluginId,Path,ContentHash,Size,Ticks,Data,Updated) VALUES($i,$p,$h,$s,$t,$d,$u)
ON CONFLICT(PluginId,Path) DO UPDATE SET ContentHash=$h,Size=$s,Ticks=$t,Data=$d,Updated=$u";
                        var pi = cmd.Parameters.Add("$i", SqliteType.Text);
                        var pp = cmd.Parameters.Add("$p", SqliteType.Text);
                        var ph = cmd.Parameters.Add("$h", SqliteType.Text);
                        var ps = cmd.Parameters.Add("$s", SqliteType.Integer);
                        var pt = cmd.Parameters.Add("$t", SqliteType.Integer);
                        var pd = cmd.Parameters.Add("$d", SqliteType.Text);
                        var pu = cmd.Parameters.Add("$u", SqliteType.Text);
                        var now = DateTime.UtcNow.ToString("O");
                        foreach (var (w, h, size, ticks) in rows)
                        {
                            // Inzwischen überholt oder mit der Datei verschoben: nicht mehr schreiben
                            lock (PendingSync)
                                if (!Pending.TryGetValue(PendingKey(w.PluginId, w.FullPath), out var cur) || !ReferenceEquals(cur, w)) continue;
                            pi.Value = w.PluginId; pp.Value = w.FullPath; ph.Value = h; ps.Value = size; pt.Value = ticks; pd.Value = w.Data; pu.Value = now;
                            cmd.ExecuteNonQuery();
                        }
                        tx.Commit();
                        foreach (var (w, h, size, ticks) in rows) UpdatePlugin(PendingKey(w.PluginId, w.FullPath), new PluginRow(h, size, ticks, w.Data));
                    }
                }
                catch
                {
                }
                // Erst nach dem Schreiben aus der Warteschlange nehmen (Lesen sieht bis dahin den neuen Wert); neuere Werte bleiben stehen
                lock (PendingSync)
                    foreach (var (key, w) in chunk)
                        if (Pending.TryGetValue(key, out var cur) && ReferenceEquals(cur, w)) Pending.Remove(key);
            }
        }

        private static bool _pluginTable;
        private static void EnsurePluginTable()
        {
            _ = ConnectionString;
            if (_pluginTable) return;
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"CREATE TABLE IF NOT EXISTS PluginData(
  PluginId TEXT NOT NULL,
  Path TEXT NOT NULL COLLATE NOCASE,
  ContentHash TEXT NOT NULL,
  Size INTEGER NOT NULL,
  Data TEXT NOT NULL,
  Updated TEXT NOT NULL,
  PRIMARY KEY(PluginId, Path));";
            cmd.ExecuteNonQuery();
            // Änderungszeit ergänzt: gleiche Größe + Zeit -> gültig ohne die Datei zu lesen (ältere Einträge haben 0 und werden per Hash geprüft)
            cmd.CommandText = "SELECT count(*) FROM pragma_table_info('PluginData') WHERE name='Ticks'";
            if (Convert.ToInt64(cmd.ExecuteScalar()) == 0)
            {
                cmd.CommandText = "ALTER TABLE PluginData ADD COLUMN Ticks INTEGER NOT NULL DEFAULT 0";
                cmd.ExecuteNonQuery();
            }
            _pluginTable = true;
        }

        private sealed record DocRow(long Size, long Ticks, string Hash, string SettingsKey, int Version, string Data);
        private sealed record PluginRow(string Hash, long Size, long Ticks, string Data);

        /// <summary>Vorab gelesene Datensätze eines Ladevorgangs (Eintrag wird bei Verwendung entnommen).</summary>
        private sealed class PrefetchScope : IDisposable
        {
            public readonly System.Collections.Concurrent.ConcurrentDictionary<string, DocRow?> Docs = new(StringComparer.OrdinalIgnoreCase);
            public readonly System.Collections.Concurrent.ConcurrentDictionary<string, PluginRow?> Plugin = new(StringComparer.OrdinalIgnoreCase);
            public void Dispose()
            {
                lock (ScopeSync) _scopes = _scopes.Where(s => s != this).ToArray();
                Docs.Clear();
                Plugin.Clear();
            }
        }
        private static readonly object ScopeSync = new();
        private static volatile PrefetchScope[] _scopes = Array.Empty<PrefetchScope>();

        /// <summary>
        /// Liest die Datensätze vieler Dateien gebündelt (eine Abfrage je Ordner statt einer je Datei). Bis zum Freigeben bedienen
        /// <see cref="TryGet"/> und <see cref="LoadPluginData"/> diese Dateien ohne eigene Datenbankabfrage.
        /// </summary>
        public static IDisposable Prefetch(IEnumerable<string> paths, bool documents, params string[] pluginIds)
        {
            var scope = new PrefetchScope();
            try
            {
                var wanted = new HashSet<string>(paths.Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);
                // Bei wenigen Dateien lohnt sich das nicht (Einzelabfragen sind dann schneller als ein Ordnerbereich)
                if (wanted.Count < 32 || (!documents && pluginIds.Length == 0)) return scope;
                var roots = new List<string>();
                foreach (var d in wanted.Select(Path.GetDirectoryName).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(d => d.Length))
                    if (!roots.Any(r => IsUnder(d, r))) roots.Add(d);
                lock (ScopeSync) _scopes = _scopes.Append(scope).ToArray();
                // Unter derselben Sperre wie Schreibvorgänge: kein veralteter Stand im Vorab-Ergebnis
                lock (Sync)
                {
                    _ = ConnectionString;
                    if (pluginIds.Length > 0) EnsurePluginTable();
                    using var c = Open();
                    foreach (var root in roots)
                    {
                        var lo = root.TrimEnd('\\') + "\\";
                        var hi = root.TrimEnd('\\') + "]";
                        if (documents)
                        {
                            using var cmd = c.CreateCommand();
                            cmd.CommandText = "SELECT Path, Size, Ticks, ContentHash, SettingsKey, ExtractorVersion, Data FROM Documents WHERE Path >= $lo AND Path < $hi";
                            cmd.Parameters.AddWithValue("$lo", lo);
                            cmd.Parameters.AddWithValue("$hi", hi);
                            using var r = cmd.ExecuteReader();
                            while (r.Read())
                            {
                                var p = r.GetString(0);
                                if (wanted.Contains(p)) scope.Docs[p] = new DocRow(r.GetInt64(1), r.GetInt64(2), r.GetString(3), r.GetString(4), r.GetInt32(5), r.GetString(6));
                            }
                        }
                        foreach (var id in pluginIds)
                        {
                            using var cmd = c.CreateCommand();
                            cmd.CommandText = "SELECT Path, ContentHash, Size, Ticks, Data FROM PluginData WHERE PluginId=$i AND Path >= $lo AND Path < $hi";
                            cmd.Parameters.AddWithValue("$i", id);
                            cmd.Parameters.AddWithValue("$lo", lo);
                            cmd.Parameters.AddWithValue("$hi", hi);
                            using var r = cmd.ExecuteReader();
                            while (r.Read())
                            {
                                var p = r.GetString(0);
                                if (wanted.Contains(p)) scope.Plugin[PendingKey(id, p)] = new PluginRow(r.GetString(1), r.GetInt64(2), r.GetInt64(3), r.GetString(4));
                            }
                        }
                    }
                    // Nicht gefunden = kein Eintrag vorhanden (spart die Einzelabfrage)
                    foreach (var p in wanted)
                    {
                        if (documents) scope.Docs.TryAdd(p, null);
                        foreach (var id in pluginIds) scope.Plugin.TryAdd(PendingKey(id, p), null);
                    }
                }
            }
            catch
            {
                scope.Docs.Clear();
                scope.Plugin.Clear();
            }
            return scope;
        }

        private static bool IsUnder(string dir, string root) =>
            dir.Equals(root, StringComparison.OrdinalIgnoreCase) || dir.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

        private static bool TakeDoc(string full, out DocRow? row)
        {
            foreach (var s in _scopes) if (s.Docs.TryRemove(full, out row)) return true;
            row = null;
            return false;
        }

        private static bool TakePlugin(string key, out PluginRow? row)
        {
            foreach (var s in _scopes) if (s.Plugin.TryRemove(key, out row)) return true;
            row = null;
            return false;
        }

        private static void ForgetDoc(string full)
        {
            foreach (var s in _scopes) s.Docs.TryRemove(full, out _);
        }

        private static void UpdatePlugin(string key, PluginRow row)
        {
            foreach (var s in _scopes) if (s.Plugin.ContainsKey(key)) s.Plugin[key] = row;
        }

        private static void ForgetPath(string full)
        {
            ForgetDoc(full);
            foreach (var s in _scopes)
                foreach (var k in s.Plugin.Keys.Where(k => k.EndsWith("\u0001" + full, StringComparison.OrdinalIgnoreCase)).ToList()) s.Plugin.TryRemove(k, out _);
        }

        /// <summary>Pfad nach Umbenennen aktualisieren (Datensatz bleibt erhalten).</summary>
        public static void Move(string oldPath, string newPath) => MoveMany(new[] { (oldPath, newPath) });

        /// <summary>Pfade nach Umbenennen/Verschieben aktualisieren (Datensätze bleiben erhalten) – alle Dateien in einer Transaktion
        /// und mit nur einem Durchlauf durch den Volltextindex (vorher zwei vollständige Durchläufe je Datei).</summary>
        public static void MoveMany(IEnumerable<(string OldPath, string NewPath)> moves)
        {
            try
            {
                var pairs = new List<(string O, string N)>();
                foreach (var (op, np) in moves)
                    try { pairs.Add((Path.GetFullPath(op), Path.GetFullPath(np))); } catch { }
                if (pairs.Count == 0) return;
                // Noch nicht geschriebene Daten wandern mit
                lock (PendingSync)
                {
                    foreach (var (o, n) in pairs)
                    {
                        bool sameFile = o.Equals(n, StringComparison.OrdinalIgnoreCase);
                        if (!sameFile)
                            foreach (var k in Pending.Where(kv => kv.Value.FullPath.Equals(n, StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key).ToList()) Pending.Remove(k);
                        foreach (var (k, w) in Pending.Where(kv => kv.Value.FullPath.Equals(o, StringComparison.OrdinalIgnoreCase)).ToList())
                        {
                            Pending.Remove(k);
                            Pending[PendingKey(w.PluginId, n)] = w with { FullPath = n };
                        }
                        if (!sameFile) PendingDocs.Remove(n);
                        if (PendingDocs.Remove(o, out var doc)) PendingDocs[n] = doc with { FullPath = n };
                    }
                }
                lock (Sync)
                {
                    _ = ConnectionString;
                    EnsurePluginTable();
                    using var c = Open();
                    bool fts = EnsureFts(c);
                    using var tx = c.BeginTransaction();
                    var pluginIds = new List<string>();
                    using (var q = c.CreateCommand())
                    {
                        q.Transaction = tx;
                        q.CommandText = "SELECT DISTINCT PluginId FROM PluginData";
                        using var r = q.ExecuteReader();
                        while (r.Read()) pluginIds.Add(r.GetString(0));
                    }
                    // Volltextindex: Pfad steht dort ohne Index – Zeilennummern einmal für alle Dateien ermitteln
                    var ftsRows = new Dictionary<string, List<long>>(StringComparer.OrdinalIgnoreCase);
                    if (fts)
                    {
                        using var q = c.CreateCommand();
                        q.Transaction = tx;
                        q.CommandText = "SELECT rowid, Path FROM DocText";
                        using var r = q.ExecuteReader();
                        while (r.Read())
                        {
                            if (r.IsDBNull(1)) continue;
                            var p = r.GetString(1);
                            if (!ftsRows.TryGetValue(p, out var l)) ftsRows[p] = l = new List<long>();
                            l.Add(r.GetInt64(0));
                        }
                    }
                    using var del = c.CreateCommand();
                    del.Transaction = tx;
                    del.CommandText = "DELETE FROM Documents WHERE Path=$n";
                    var dn = del.Parameters.Add("$n", SqliteType.Text);
                    using var upd = c.CreateCommand();
                    upd.Transaction = tx;
                    upd.CommandText = "UPDATE Documents SET Path=$n WHERE Path=$o";
                    var uo = upd.Parameters.Add("$o", SqliteType.Text);
                    var un = upd.Parameters.Add("$n", SqliteType.Text);
                    using var pdel = c.CreateCommand();
                    pdel.Transaction = tx;
                    pdel.CommandText = "DELETE FROM PluginData WHERE PluginId=$id AND Path=$n";
                    var pdi = pdel.Parameters.Add("$id", SqliteType.Text);
                    var pdn = pdel.Parameters.Add("$n", SqliteType.Text);
                    using var pupd = c.CreateCommand();
                    pupd.Transaction = tx;
                    pupd.CommandText = "UPDATE PluginData SET Path=$n WHERE PluginId=$id AND Path=$o";
                    var pui = pupd.Parameters.Add("$id", SqliteType.Text);
                    var puo = pupd.Parameters.Add("$o", SqliteType.Text);
                    var pun = pupd.Parameters.Add("$n", SqliteType.Text);
                    using var fdel = c.CreateCommand();
                    fdel.Transaction = tx;
                    fdel.CommandText = "DELETE FROM DocText WHERE rowid=$r";
                    var fdr = fdel.Parameters.Add("$r", SqliteType.Integer);
                    using var fupd = c.CreateCommand();
                    fupd.Transaction = tx;
                    fupd.CommandText = "UPDATE DocText SET Path=$n WHERE rowid=$r";
                    var fun = fupd.Parameters.Add("$n", SqliteType.Text);
                    var fur = fupd.Parameters.Add("$r", SqliteType.Integer);
                    foreach (var (o, n) in pairs)
                    {
                        // Nur Groß-/Kleinschreibung geändert: nicht vorher löschen (wäre derselbe Datensatz)
                        bool sameFile = o.Equals(n, StringComparison.OrdinalIgnoreCase);
                        if (!sameFile) { dn.Value = n; del.ExecuteNonQuery(); }
                        uo.Value = o; un.Value = n; upd.ExecuteNonQuery();
                        foreach (var id in pluginIds)
                        {
                            if (!sameFile) { pdi.Value = id; pdn.Value = n; pdel.ExecuteNonQuery(); }
                            pui.Value = id; puo.Value = o; pun.Value = n; pupd.ExecuteNonQuery();
                        }
                        if (fts)
                        {
                            if (!sameFile && ftsRows.Remove(n, out var old))
                                foreach (var r in old) { fdr.Value = r; fdel.ExecuteNonQuery(); }
                            if (ftsRows.Remove(o, out var rows))
                            {
                                foreach (var r in rows) { fun.Value = n; fur.Value = r; fupd.ExecuteNonQuery(); }
                                ftsRows[n] = rows;
                            }
                        }
                    }
                    tx.Commit();
                    foreach (var (o, n) in pairs) { ForgetPath(o); ForgetPath(n); }
                }
            }
            catch
            {
            }
        }

        /// <summary>Einträge, deren Datei nicht mehr existiert, entfernen.</summary>
        public static int Cleanup()
        {
            try
            {
                lock (Sync)
                {
                    _ = ConnectionString;
                    using var c = Open();
                    var gone = new List<string>();
                    using (var cmd = c.CreateCommand())
                    {
                        cmd.CommandText = "SELECT Path FROM Documents";
                        using var r = cmd.ExecuteReader();
                        while (r.Read()) if (!File.Exists(r.GetString(0))) gone.Add(r.GetString(0));
                    }
                    using var tx = c.BeginTransaction();
                    foreach (var p in gone)
                    {
                        using var del = c.CreateCommand();
                        del.Transaction = tx;
                        del.CommandText = "DELETE FROM Documents WHERE Path=$p";
                        del.Parameters.AddWithValue("$p", p);
                        del.ExecuteNonQuery();
                        if (EnsureFts(c)) { del.CommandText = "DELETE FROM DocText WHERE Path=$p"; del.ExecuteNonQuery(); }
                    }
                    tx.Commit();
                    return gone.Count;
                }
            }
            catch
            {
                return 0;
            }
        }
    }
}
