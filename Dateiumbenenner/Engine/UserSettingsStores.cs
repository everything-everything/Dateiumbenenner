using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Dateiumbenenner.Engine
{
    /// <summary>Begriffe, die nie als Firmenname erkannt werden sollen (Daten\company_exclusions.txt, ein Begriff pro Zeile).</summary>
    public static class CompanyExclusions
    {
        private static readonly string[] Defaults = { "Wartungstabellen", "Wartungstabelle", "Inhaltsverzeichnis", "Seite" };
        private static List<string>? _items;
        private static readonly object Sync = new();

        public static string FilePath => Path.Combine(Plugins.PluginManager.BaseDirectory, "company_exclusions.txt");

        public static IReadOnlyList<string> Items
        {
            get { lock (Sync) return _items ??= Load(); }
        }

        private static List<string> Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return File.ReadAllLines(FilePath).Select(l => l.Trim()).Where(l => l.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }
            catch { }
            return Defaults.ToList();
        }

        public static void Save(IEnumerable<string> items)
        {
            var list = items.Select(l => l.Trim()).Where(l => l.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllLines(FilePath, list);
            lock (Sync) _items = list;
        }

        /// <summary>True, wenn der Kandidat einen ausgeschlossenen Begriff als ganzes Wort enthält.</summary>
        public static bool IsExcluded(string? candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate)) return false;
            foreach (var word in Items)
                if (Regex.IsMatch(candidate, @"(?<!\w)" + Regex.Escape(word) + @"(?!\w)", RegexOptions.IgnoreCase))
                    return true;
            return false;
        }
    }

    /// <summary>Dateinamen-Eigenschaften je Firma + Dokumenttyp (Daten\doc_templates.json).</summary>
    public class DocTemplate
    {
        public string? Delimiter { get; set; }
        public int? SegmentIndex { get; set; }
        public bool UseDelimiterPosition { get; set; } = true;
        public bool RemoveDuplicates { get; set; }
        public bool ReplaceSegments { get; set; }
        public bool RemoveAttachment { get; set; }
        public List<TemplatePosition> InsertPositions { get; set; } = new();
        public string? PrefixDelimiter { get; set; }
        public string? PrefixSegment { get; set; }
        public string? PrefixRemove { get; set; }
    }

    public class TemplatePosition
    {
        public int Position { get; set; }
        public string ValueType { get; set; } = "";
        public bool Enabled { get; set; } = true;
    }

    public static class DocTemplateStore
    {
        private static Dictionary<string, DocTemplate>? _map;
        private static readonly object Sync = new();

        public static string FilePath => Path.Combine(Plugins.PluginManager.BaseDirectory, "doc_templates.json");

        public static string? Key(string? company, string? docType)
            => string.IsNullOrWhiteSpace(company) || string.IsNullOrWhiteSpace(docType)
                ? null : company.Trim().ToLowerInvariant() + "|" + docType.Trim().ToLowerInvariant();

        private static Dictionary<string, DocTemplate> Map
        {
            get
            {
                if (_map != null) return _map;
                try
                {
                    if (File.Exists(FilePath))
                        _map = JsonSerializer.Deserialize<Dictionary<string, DocTemplate>>(File.ReadAllText(FilePath));
                }
                catch { }
                return _map ??= new Dictionary<string, DocTemplate>();
            }
        }

        public static DocTemplate? Get(string? company, string? docType)
        {
            var k = Key(company, docType);
            if (k == null) return null;
            lock (Sync) return Map.TryGetValue(k, out var t) ? t : null;
        }

        public static void Set(string company, string docType, DocTemplate t)
        {
            var k = Key(company, docType);
            if (k == null) return;
            lock (Sync)
            {
                Map[k] = t;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                    File.WriteAllText(FilePath, JsonSerializer.Serialize(Map, new JsonSerializerOptions { WriteIndented = true }));
                }
                catch { }
            }
        }
    }
}
