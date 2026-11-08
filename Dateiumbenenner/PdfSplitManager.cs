using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UglyToad.PdfPig.Writer;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;

namespace Dateiumbenenner
{
    public enum PdfSplitMode { SinglePages, EveryN, Ranges, BlankPages }

    public sealed class PdfSplitSettings
    {
        public PdfSplitMode Mode { get; init; } = PdfSplitMode.SinglePages;
        public int EveryN { get; init; } = 1;
        public string Ranges { get; init; } = "";
        public bool SkipBlankPages { get; init; }
        public string NamePattern { get; init; } = PdfSplitManager.DefaultPattern;
        /// <summary>Leer = Ordner der Originaldatei.</summary>
        public string OutputFolder { get; init; } = "";
    }

    public class PdfSplitItem : INotifyPropertyChanged
    {
        private string _ranges = "";
        private string _parts = "";
        public string FilePath { get; set; } = string.Empty;
        public string FileName => Path.GetFileName(FilePath);
        public int PageCount { get; set; }
        public long FileSizeBytes { get; set; }
        /// <summary>Seiten ohne Text und ohne Bild (1-basiert).</summary>
        public HashSet<int> BlankPages { get; set; } = new();
        public string SizeDisplay => FileSizeBytes <= 0 ? string.Empty : (DocumentMetadata.UseMegaBytes ? (FileSizeBytes / (1024d * 1024d)).ToString("F2") + " MB" : (FileSizeBytes / 1024d).ToString("F2") + " KB");
        public string BlankDisplay => BlankPages.Count == 0 ? "" : string.Join(", ", BlankPages.OrderBy(p => p));
        /// <summary>Eigene Seitenbereiche nur für diese Datei (leer = Einstellung unten).</summary>
        public string Ranges { get => _ranges; set { if (_ranges == value) return; _ranges = value ?? ""; OnPropertyChanged(nameof(Ranges)); RangesChanged?.Invoke(this); } }
        public string PartsDisplay { get => _parts; set { if (_parts == value) return; _parts = value; OnPropertyChanged(nameof(PartsDisplay)); } }
        public event Action<PdfSplitItem>? RangesChanged;
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        public void RefreshSizeDisplay() => OnPropertyChanged(nameof(SizeDisplay));
    }

    public static class PdfSplitManager
    {
        public const string DefaultPattern = "{name}_Teil{n}";

        /// <summary>Seitenzahl und leere Seiten (kein Text, kein Bild) ermitteln.</summary>
        public static Task<(int Pages, HashSet<int> Blank)> AnalyzeAsync(string filePath) => Task.Run(() =>
        {
            var blank = new HashSet<int>();
            try
            {
                using var doc = PdfPigDocument.Open(filePath);
                foreach (var page in doc.GetPages())
                {
                    bool hasText = page.Letters.Any(l => !string.IsNullOrWhiteSpace(l.Value));
                    if (!hasText && !page.GetImages().Any()) blank.Add(page.Number);
                }
                return (doc.NumberOfPages, blank);
            }
            catch { return (0, blank); }
        });

        /// <summary>Teile als Seitenlisten (1-basiert). Wirft FormatException bei ungültigen Seitenbereichen.</summary>
        public static List<List<int>> Plan(PdfSplitItem item, PdfSplitSettings s)
        {
            int n = item.PageCount;
            var parts = new List<List<int>>();
            if (n <= 0) return parts;
            if (!string.IsNullOrWhiteSpace(item.Ranges)) parts = ParseRanges(item.Ranges, n);
            else switch (s.Mode)
            {
                case PdfSplitMode.SinglePages:
                    parts = Enumerable.Range(1, n).Select(p => new List<int> { p }).ToList();
                    break;
                case PdfSplitMode.EveryN:
                    int size = Math.Max(1, s.EveryN);
                    for (int start = 1; start <= n; start += size)
                        parts.Add(Enumerable.Range(start, Math.Min(size, n - start + 1)).ToList());
                    break;
                case PdfSplitMode.Ranges:
                    parts = ParseRanges(s.Ranges, n);
                    break;
                case PdfSplitMode.BlankPages:
                    var cur = new List<int>();
                    for (int p = 1; p <= n; p++)
                    {
                        if (item.BlankPages.Contains(p)) { if (cur.Count > 0) parts.Add(cur); cur = new List<int>(); }
                        else cur.Add(p);
                    }
                    if (cur.Count > 0) parts.Add(cur);
                    break;
            }
            if (s.SkipBlankPages || s.Mode == PdfSplitMode.BlankPages && string.IsNullOrWhiteSpace(item.Ranges))
                parts = parts.Select(p => p.Where(x => !item.BlankPages.Contains(x)).ToList()).ToList();
            return parts.Where(p => p.Count > 0).ToList();
        }

        private static readonly Regex RangeToken = new(@"^\s*(\d*)\s*(-?)\s*(\d*)\s*$", RegexOptions.Compiled);

        /// <summary>"1-3; 4; 6-" → je Bereich ein Teil. "-3" = bis Seite 3, "6-" = ab Seite 6 bis Ende, "5-2" = rückwärts.</summary>
        public static List<List<int>> ParseRanges(string text, int pageCount)
        {
            var parts = new List<List<int>>();
            bool skipped = false;
            foreach (var raw in (text ?? "").Split(new[] { ';', ',', '\n', '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (raw.Trim().Length == 0) continue;
                var m = RangeToken.Match(raw);
                if (!m.Success || m.Groups[1].Value.Length == 0 && m.Groups[3].Value.Length == 0)
                    throw new FormatException($"Ungültiger Seitenbereich: \"{raw.Trim()}\"");
                bool dash = m.Groups[2].Value.Length > 0;
                int from = m.Groups[1].Value.Length > 0 ? int.Parse(m.Groups[1].Value) : 1;
                int to = m.Groups[3].Value.Length > 0 ? int.Parse(m.Groups[3].Value) : (dash ? pageCount : from);
                if (from < 1 || to < 1) throw new FormatException($"Seite 0 gibt es nicht: \"{raw.Trim()}\"");
                // Bereiche gelten für mehrere Dateien: komplett hinter dem Ende → überspringen, teilweise → kürzen
                if (from > pageCount && to > pageCount) { skipped = true; continue; }
                from = Math.Min(from, pageCount); to = Math.Min(to, pageCount);
                parts.Add(from <= to ? Enumerable.Range(from, to - from + 1).ToList() : Enumerable.Range(to, from - to + 1).Reverse().ToList());
            }
            if (parts.Count == 0)
                throw new FormatException(skipped ? $"Kein Seitenbereich liegt innerhalb der {pageCount} Seiten." : "Keine Seitenbereiche angegeben (z. B. 1-3; 4; 6-).");
            return parts;
        }

        /// <summary>Kurzbeschreibung für die Liste, z. B. "3 Teile: 1-2 | 3-4 | 5".</summary>
        public static string Describe(PdfSplitItem item, PdfSplitSettings s)
        {
            try
            {
                var parts = Plan(item, s);
                if (parts.Count == 0) return "keine Seiten";
                var list = string.Join(" | ", parts.Take(12).Select(PagesText));
                if (parts.Count > 12) list += " | …";
                return $"{parts.Count} {(parts.Count == 1 ? "Teil" : "Teile")}: {list}";
            }
            catch (FormatException ex) { return "⚠ " + ex.Message; }
        }

        /// <summary>Seitenliste kompakt: "1-3, 5".</summary>
        public static string PagesText(IReadOnlyList<int> pages)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < pages.Count; i++)
            {
                int j = i;
                while (j + 1 < pages.Count && pages[j + 1] == pages[j] + 1) j++;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(j > i ? $"{pages[i]}-{pages[j]}" : pages[i].ToString());
                i = j;
            }
            return sb.ToString();
        }

        /// <summary>Dateiname eines Teils: {name}, {n}, {von}, {bis}, {seiten}.</summary>
        public static string PartFileName(string pattern, string sourcePath, int index, int count, IReadOnlyList<int> pages)
        {
            if (string.IsNullOrWhiteSpace(pattern)) pattern = DefaultPattern;
            var width = count.ToString().Length;
            var name = pattern
                .Replace("{name}", Path.GetFileNameWithoutExtension(sourcePath), StringComparison.OrdinalIgnoreCase)
                .Replace("{n}", index.ToString().PadLeft(width, '0'), StringComparison.OrdinalIgnoreCase)
                .Replace("{von}", pages[0].ToString(), StringComparison.OrdinalIgnoreCase)
                .Replace("{bis}", pages[^1].ToString(), StringComparison.OrdinalIgnoreCase)
                .Replace("{seiten}", PagesText(pages).Replace(", ", "+"), StringComparison.OrdinalIgnoreCase);
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            name = name.Trim().TrimEnd('.');
            if (name.Length == 0) name = Path.GetFileNameWithoutExtension(sourcePath) + "_" + index;
            return name + ".pdf";
        }

        private static string UniquePath(string folder, string fileName, ISet<string> used)
        {
            var path = Path.Combine(folder, fileName);
            var stem = Path.GetFileNameWithoutExtension(fileName);
            for (int i = 2; File.Exists(path) || used.Contains(path); i++) path = Path.Combine(folder, $"{stem} ({i}).pdf");
            used.Add(path);
            return path;
        }

        /// <summary>Datei in Teile zerlegen; liefert die erstellten Dateien.</summary>
        public static Task<List<string>> SplitAsync(PdfSplitItem item, PdfSplitSettings s) => Task.Run(() =>
        {
            var parts = Plan(item, s);
            if (parts.Count == 0) throw new InvalidOperationException("Keine Seiten zum Speichern (alle Seiten leer oder ausgelassen).");
            var folder = string.IsNullOrWhiteSpace(s.OutputFolder) ? Path.GetDirectoryName(item.FilePath)! : s.OutputFolder;
            Directory.CreateDirectory(folder);
            var created = new List<string>();
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var source = PdfPigDocument.Open(item.FilePath);
                for (int i = 0; i < parts.Count; i++)
                {
                    using var builder = new PdfDocumentBuilder();
                    foreach (var page in parts[i]) builder.AddPage(source, page);
                    var target = UniquePath(folder, PartFileName(s.NamePattern, item.FilePath, i + 1, parts.Count, parts[i]), used);
                    File.WriteAllBytes(target, builder.Build());
                    created.Add(target);
                }
            }
            catch
            {
                // halbfertige Teile nicht liegen lassen
                foreach (var f in created) try { File.Delete(f); } catch { }
                throw;
            }
            return created;
        });
    }
}
