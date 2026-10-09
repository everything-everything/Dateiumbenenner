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

    /// <summary>Eigene Firmenadresse (Daten\own_company.json). Belege ohne fremde Adresse gelten als eigene/interne Belege.</summary>
    public class OwnCompany
    {
        public string Name { get; set; } = string.Empty;
        public string Street { get; set; } = string.Empty;
        public string Zip { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;

        private static OwnCompany? _current;
        private static readonly object Sync = new();
        public static string FilePath => Path.Combine(Plugins.PluginManager.BaseDirectory, "own_company.json");

        public static OwnCompany Current
        {
            get
            {
                lock (Sync)
                {
                    if (_current != null) return _current;
                    try { if (File.Exists(FilePath)) _current = JsonSerializer.Deserialize<OwnCompany>(File.ReadAllText(FilePath)); } catch { }
                    return _current ??= new OwnCompany();
                }
            }
        }

        public static void Save(OwnCompany value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
            lock (Sync) _current = value;
        }

        public bool HasName => !string.IsNullOrWhiteSpace(Name);

        /// <summary>True, wenn der Kandidat der eigenen Firma entspricht (gleich oder OCR-ähnlich).</summary>
        public bool Matches(string? candidate)
        {
            if (!HasName || string.IsNullOrWhiteSpace(candidate)) return false;
            if (candidate.Contains(Name, StringComparison.OrdinalIgnoreCase) || Name.Contains(candidate, StringComparison.OrdinalIgnoreCase) && candidate.Length >= 4) return true;
            return CompanyMatcher.Similarity(CompanyMatcher.Normalize(candidate), CompanyMatcher.Normalize(Name)) >= CompanyMatcher.Threshold;
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
