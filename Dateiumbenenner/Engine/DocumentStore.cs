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
        public const int ExtractorVersion = 1;

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
            return c;
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
            public bool NumberManuallySet { get; set; }
            public bool DateManuallySet { get; set; }
            public bool DocTypeManuallySet { get; set; }
            public bool CompanyConfirmed { get; set; }
        }

        public static string Stamp(string path, out long size, out long ticks)
        {
            var fi = new FileInfo(path);
            size = fi.Length;
            ticks = fi.LastWriteTimeUtc.Ticks;
            return PartialHash(path, size);
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
                var hash = Stamp(path, out var size, out var ticks);
                lock (Sync)
                {
                    _ = ConnectionString;
                    using var c = Open();
                    using var cmd = c.CreateCommand();
                    cmd.CommandText = "SELECT Size, Ticks, ContentHash, SettingsKey, ExtractorVersion, Data FROM Documents WHERE Path=$p";
                    cmd.Parameters.AddWithValue("$p", Path.GetFullPath(path));
                    using (var r = cmd.ExecuteReader())
                    {
                        if (r.Read())
                        {
                            var rec = JsonSerializer.Deserialize<Record>(r.GetString(5));
                            upToDate = r.GetInt64(0) == size && r.GetString(2) == hash
                                && r.GetString(3) == settingsKey && r.GetInt32(4) == ExtractorVersion;
                            return rec;
                        }
                    }
                    // Datei verschoben/umbenannt: gleicher Inhalt unter anderem Pfad
                    cmd.CommandText = "SELECT SettingsKey, ExtractorVersion, Data FROM Documents WHERE ContentHash=$h AND Size=$s ORDER BY Updated DESC LIMIT 1";
                    cmd.Parameters.Clear();
                    cmd.Parameters.AddWithValue("$h", hash);
                    cmd.Parameters.AddWithValue("$s", size);
                    using var r2 = cmd.ExecuteReader();
                    if (!r2.Read()) return null;
                    upToDate = r2.GetString(0) == settingsKey && r2.GetInt32(1) == ExtractorVersion;
                    return JsonSerializer.Deserialize<Record>(r2.GetString(2));
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
                var hash = Stamp(path, out var size, out var ticks);
                lock (Sync)
                {
                    _ = ConnectionString;
                    using var c = Open();
                    using var cmd = c.CreateCommand();
                    cmd.CommandText = @"INSERT INTO Documents(Path,Size,Ticks,ContentHash,SettingsKey,ExtractorVersion,Data,Updated)
VALUES($p,$s,$t,$h,$k,$v,$d,$u)
ON CONFLICT(Path) DO UPDATE SET Size=$s,Ticks=$t,ContentHash=$h,SettingsKey=$k,ExtractorVersion=$v,Data=$d,Updated=$u";
                    cmd.Parameters.AddWithValue("$p", Path.GetFullPath(path));
                    cmd.Parameters.AddWithValue("$s", size);
                    cmd.Parameters.AddWithValue("$t", ticks);
                    cmd.Parameters.AddWithValue("$h", hash);
                    cmd.Parameters.AddWithValue("$k", settingsKey);
                    cmd.Parameters.AddWithValue("$v", ExtractorVersion);
                    cmd.Parameters.AddWithValue("$d", JsonSerializer.Serialize(rec));
                    cmd.Parameters.AddWithValue("$u", DateTime.UtcNow.ToString("O"));
                    cmd.ExecuteNonQuery();
                }
            }
            catch
            {
            }
        }

        /// <summary>Plugin-Daten lesen; nur gültig, solange die Datei unverändert ist.</summary>
        public static string? LoadPluginData(string pluginId, string path)
        {
            try
            {
                var hash = Stamp(path, out var size, out _);
                lock (Sync)
                {
                    EnsurePluginTable();
                    using var c = Open();
                    using var cmd = c.CreateCommand();
                    cmd.CommandText = "SELECT Data FROM PluginData WHERE PluginId=$i AND Path=$p AND ContentHash=$h AND Size=$s";
                    cmd.Parameters.AddWithValue("$i", pluginId);
                    cmd.Parameters.AddWithValue("$p", Path.GetFullPath(path));
                    cmd.Parameters.AddWithValue("$h", hash);
                    cmd.Parameters.AddWithValue("$s", size);
                    return cmd.ExecuteScalar() as string;
                }
            }
            catch
            {
                return null;
            }
        }

        public static void SavePluginData(string pluginId, string path, string data)
        {
            try
            {
                if (!File.Exists(path)) return;
                var hash = Stamp(path, out var size, out _);
                lock (Sync)
                {
                    EnsurePluginTable();
                    using var c = Open();
                    using var cmd = c.CreateCommand();
                    cmd.CommandText = @"INSERT INTO PluginData(PluginId,Path,ContentHash,Size,Data,Updated) VALUES($i,$p,$h,$s,$d,$u)
ON CONFLICT(PluginId,Path) DO UPDATE SET ContentHash=$h,Size=$s,Data=$d,Updated=$u";
                    cmd.Parameters.AddWithValue("$i", pluginId);
                    cmd.Parameters.AddWithValue("$p", Path.GetFullPath(path));
                    cmd.Parameters.AddWithValue("$h", hash);
                    cmd.Parameters.AddWithValue("$s", size);
                    cmd.Parameters.AddWithValue("$d", data);
                    cmd.Parameters.AddWithValue("$u", DateTime.UtcNow.ToString("O"));
                    cmd.ExecuteNonQuery();
                }
            }
            catch
            {
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
            _pluginTable = true;
        }

        /// <summary>Pfad nach Umbenennen aktualisieren (Datensatz bleibt erhalten).</summary>
        public static void Move(string oldPath, string newPath)
        {
            try
            {
                lock (Sync)
                {
                    _ = ConnectionString;
                    using var c = Open();
                    using var cmd = c.CreateCommand();
                    cmd.CommandText = "DELETE FROM Documents WHERE Path=$n; UPDATE Documents SET Path=$n WHERE Path=$o; DELETE FROM PluginData WHERE Path=$n; UPDATE PluginData SET Path=$n WHERE Path=$o";
                    EnsurePluginTable();
                    cmd.Parameters.AddWithValue("$o", Path.GetFullPath(oldPath));
                    cmd.Parameters.AddWithValue("$n", Path.GetFullPath(newPath));
                    cmd.ExecuteNonQuery();
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
