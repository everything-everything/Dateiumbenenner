using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Dateiumbenenner.Engine
{
    /// <summary>
    /// Fasst kurze OCR-Zeilen zu breiteren Zeilen zusammen (Zeilenbreite global oder je Beleg).
    /// Grenzen bleiben erhalten: Leerzeilen, Zeilen mit Ziffern/Doppelpunkt (Nummern, Beträge, Datum, PLZ, Labels)
    /// und Zeilen, die mit einer Rechtsform enden (Firmenname bleibt eigene Zeile).
    /// </summary>
    public static class TextReflow
    {
        public const int DefaultWidth = 100;
        private static readonly string GlobalFile = Path.Combine(Dateiumbenenner.Plugins.PluginManager.BaseDirectory, "Daten", "zeilenbreite.txt");
        private static readonly string FilesFile = Path.Combine(Dateiumbenenner.Plugins.PluginManager.BaseDirectory, "Daten", "zeilenbreite_belege.json");
        private static readonly object _lock = new();
        private static int? _width;
        private static Dictionary<string, int>? _perFile;

        private static readonly Regex KeepOwn = new(@"\d|:", RegexOptions.Compiled);
        private static readonly Regex LegalEnd = new(@"\b(GmbH|AG|KG|OHG|UG|GbR|e\.?\s?K\.?|SE|mbH|Co\.?\s?KG)[,.]?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Globale Zeilenbreite für alle Belege; 0 = aus (Originalzeilen).</summary>
        public static int Width
        {
            get
            {
                lock (_lock)
                {
                    if (_width == null)
                    {
                        try { _width = File.Exists(GlobalFile) && int.TryParse(Dateiumbenenner.Engine.SecureStorage.ReadAllText(GlobalFile).Trim(), out var w) ? Math.Max(0, w) : DefaultWidth; }
                        catch { _width = DefaultWidth; }
                    }
                    return _width.Value;
                }
            }
            set
            {
                lock (_lock)
                {
                    _width = Math.Max(0, value);
                    try { Directory.CreateDirectory(Path.GetDirectoryName(GlobalFile)!); Dateiumbenenner.Engine.SecureStorage.WriteAllText(GlobalFile, _width.Value.ToString()); } catch { }
                }
            }
        }

        private static Dictionary<string, int> PerFile
        {
            get
            {
                if (_perFile != null) return _perFile;
                try { _perFile = File.Exists(FilesFile) ? JsonSerializer.Deserialize<Dictionary<string, int>>(Dateiumbenenner.Engine.SecureStorage.ReadAllText(FilesFile)) : null; } catch { }
                _perFile = new Dictionary<string, int>(_perFile ?? new(), StringComparer.OrdinalIgnoreCase);
                return _perFile;
            }
        }

        /// <summary>Zeilenbreite nur für diesen Beleg (null = globale Einstellung).</summary>
        public static int? GetFileWidth(string? path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            lock (_lock) return PerFile.TryGetValue(Path.GetFileName(path), out var w) ? w : null;
        }

        public static void SetFileWidth(string? path, int? width)
        {
            if (string.IsNullOrEmpty(path)) return;
            lock (_lock)
            {
                var key = Path.GetFileName(path);
                if (width == null) PerFile.Remove(key); else PerFile[key] = Math.Max(0, width.Value);
                try { Directory.CreateDirectory(Path.GetDirectoryName(FilesFile)!); Dateiumbenenner.Engine.SecureStorage.WriteAllText(FilesFile, JsonSerializer.Serialize(PerFile)); } catch { }
            }
        }

        public static int EffectiveWidth(string? path) => GetFileWidth(path) ?? Width;

        public static string Apply(string? text, string? path = null) => Apply(text, EffectiveWidth(path));

        public static string Apply(string? text, int width)
        {
            if (string.IsNullOrEmpty(text) || width <= 0) return text ?? string.Empty;
            var sb = new StringBuilder(text.Length);
            var line = new StringBuilder();
            bool lineClosed = false;

            void Flush() { if (line.Length > 0) { sb.AppendLine(line.ToString()); line.Clear(); } }

            foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                var t = raw.Trim();
                if (t.Length == 0) { Flush(); sb.AppendLine(); lineClosed = false; continue; }
                bool own = KeepOwn.IsMatch(t);
                if (own || lineClosed || line.Length + 1 + t.Length > width) Flush();
                if (line.Length > 0) line.Append(line[^1] == '-' ? "" : " ");
                line.Append(t);
                lineClosed = own || LegalEnd.IsMatch(t);
            }
            Flush();
            return sb.ToString();
        }
    }
}
