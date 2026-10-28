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
                    return Dateiumbenenner.Engine.SecureStorage.ReadAllLines(FilePath).Select(l => l.Trim()).Where(l => l.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }
            catch { }
            return Defaults.ToList();
        }

        public static void Save(IEnumerable<string> items)
        {
            var list = items.Select(l => l.Trim()).Where(l => l.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            Dateiumbenenner.Engine.SecureStorage.WriteAllLines(FilePath, list);
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

        // Gruß- und Dankesformeln aus Briefen sind nie ein Firmenname (fest eingebaut, gilt auch ohne Eintrag in der Liste).
        // Auch OCR-Schreibweisen: "Grüßen", "Gruessen", "GrüBen", "Grusse".
        private const string GreetWord = @"(?:Gr(?:\u00FC|ue|u)(?:\u00DF|ss|B|s)(?:en?)?|Gr(?:\u00FC|ue|u)(?:\u00DF|ss|B))";
        private static readonly Regex LetterPhraseRx = new(
            @"^\s*(?:(?:mit\s+)?(?:(?:den|einem)\s+)?(?:(?:freundliche[nmr]?|beste[nmr]?|herzliche[nmr]?|viele[nmr]?|liebe[nmr]?|sch(?:\u00F6|oe|o)ne[nmr]?|kollegiale[nmr]?|sonnige[nmr]?)\s+)+(?:" + GreetWord + @"|Dank)"
            + @"|" + GreetWord + @"(?=\s*(?:$|aus|von|und|[,.!]))"
            + @"|Danke(?:\s*sch(?:\u00F6|oe|o)n)?(?=\s*(?:$|f(?:\u00FC|ue|u)r|[,.!]))"
            + @"|Hochachtungsvoll|Sehr\s+geehrte\w*|Guten\s+(?:Tag|Morgen|Abend)|Wir\s+(?:danken|bedanken|freuen)|Herzlichen\s+Gl(?:\u00FC|ue|u)ckwunsch|MfG)(?![\p{L}\d])",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Länge einer Gruß-/Dankesformel am Anfang ("Mit freundlichen Grüßen", "Vielen Dank"), sonst 0.</summary>
        public static int LetterPhraseLength(string? candidate)
        {
            if (string.IsNullOrEmpty(candidate)) return 0;
            var m = LetterPhraseRx.Match(candidate);
            return m.Success ? m.Length : 0;
        }

        public static bool IsLetterPhrase(string? candidate) => LetterPhraseLength(candidate) > 0;
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
                    try { if (File.Exists(FilePath)) _current = JsonSerializer.Deserialize<OwnCompany>(Dateiumbenenner.Engine.SecureStorage.ReadAllText(FilePath)); } catch { }
                    return _current ??= new OwnCompany();
                }
            }
        }

        public static void Save(OwnCompany value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            Dateiumbenenner.Engine.SecureStorage.WriteAllText(FilePath, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
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

    /// <summary>Gelerntes Nummernformat je Firma + Belegtyp (Bezeichnung und Aufbau, z. B. "999-AAA").</summary>
    public class NumberPattern
    {
        public string Label { get; set; } = "";
        public string Shape { get; set; } = "";
        public int Count { get; set; }
    }

    public static class NumberPatternStore
    {
        private static Dictionary<string, NumberPattern>? _map;
        private static readonly object Sync = new();

        public static string FilePath => Path.Combine(Plugins.PluginManager.BaseDirectory, "number_patterns.json");

        /// <summary>Ziffer -> 9, Buchstabe -> A, Leerzeichen entfallen, Trennzeichen bleiben.</summary>
        public static string Shape(string value)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var c in value)
                if (char.IsDigit(c)) sb.Append('9');
                else if (char.IsLetter(c)) sb.Append('A');
                else if (!char.IsWhiteSpace(c)) sb.Append(c);
            return sb.ToString();
        }

        private static Dictionary<string, NumberPattern> Map
        {
            get
            {
                if (_map != null) return _map;
                try
                {
                    if (File.Exists(FilePath))
                        _map = JsonSerializer.Deserialize<Dictionary<string, NumberPattern>>(Dateiumbenenner.Engine.SecureStorage.ReadAllText(FilePath));
                }
                catch { }
                return _map ??= new Dictionary<string, NumberPattern>();
            }
        }

        public static NumberPattern? Get(string? company, string? docType)
        {
            var k = DocTemplateStore.Key(company, docType);
            if (k == null) return null;
            lock (Sync) return Map.TryGetValue(k, out var p) ? p : null;
        }

        public static void Learn(string? company, string? docType, string? label, string? value)
        {
            var k = DocTemplateStore.Key(company, docType);
            if (k == null || string.IsNullOrWhiteSpace(value)) return;
            var shape = Shape(value);
            lock (Sync)
            {
                if (Map.TryGetValue(k, out var p) && p.Shape == shape && p.Label == (label ?? "")) p.Count++;
                else Map[k] = new NumberPattern { Label = label ?? "", Shape = shape, Count = 1 };
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                    Dateiumbenenner.Engine.SecureStorage.WriteAllText(FilePath, JsonSerializer.Serialize(Map, new JsonSerializerOptions { WriteIndented = true }));
                }
                catch { }
            }
        }
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
                        _map = JsonSerializer.Deserialize<Dictionary<string, DocTemplate>>(Dateiumbenenner.Engine.SecureStorage.ReadAllText(FilePath));
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
                    Dateiumbenenner.Engine.SecureStorage.WriteAllText(FilePath, JsonSerializer.Serialize(Map, new JsonSerializerOptions { WriteIndented = true }));
                }
                catch { }
            }
        }
    }
}
