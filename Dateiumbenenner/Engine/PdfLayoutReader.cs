using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Dateiumbenenner.Engine
{
    /// <summary>
    /// Liest Wortpositionen aus einer PDF und baut daraus Textzeilen, in denen große
    /// horizontale Abstände (Spaltengrenzen) als Tab markiert sind.
    /// Liefert null, wenn die PDF keine Textebene hat (reiner Bild-Scan) – dann wird der normale Text verwendet.
    /// </summary>
    public static class PdfLayoutReader
    {
        /// <summary>Mindestanzahl Wörter, ab der die Textebene als brauchbar gilt.</summary>
        private const int MinWords = 15;

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<(string Path, int MaxPages), (DateTime Stamp, string? Text)> _cache = new();
        private const int MaxCacheEntries = 10000;

        /// <summary>Ergebnis je PDF zwischenspeichern – ExtractAddress wird pro Beleg mehrfach aufgerufen.</summary>
        public static string? ReadLayoutText(string? pdfPath, int maxPages = 2)
        {
            try
            {
                if (string.IsNullOrEmpty(pdfPath) || !File.Exists(pdfPath)
                    || !pdfPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) return null;

                var stamp = File.GetLastWriteTimeUtc(pdfPath);
                var key = (pdfPath.ToUpperInvariant(), maxPages);
                if (_cache.TryGetValue(key, out var hit) && hit.Stamp == stamp) return hit.Text;
                var text = ReadLayoutTextCore(pdfPath, maxPages);
                if (_cache.Count >= MaxCacheEntries) _cache.Clear();
                _cache[key] = (stamp, text);
                return text;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Seitenformat und Zeilen mit Position (relativ 0..1, Y von oben) der ersten Seite.</summary>
        public sealed class PageLayout
        {
            public bool Landscape { get; init; }
            public List<(string Text, double X, double Y)> Lines { get; } = new();

            /// <summary>Zone im 3x3-Raster: z. B. "oben links", "unten mitte"; null, wenn Text nicht gefunden.</summary>
            public (int Col, int Row)? FindZone(string text)
            {
                foreach (var l in Lines)
                    if (l.Text.Contains(text, StringComparison.OrdinalIgnoreCase))
                        return (Math.Min(2, (int)(l.X * 3)), Math.Min(2, (int)(l.Y * 3)));
                return null;
            }

            public static string ZoneName((int Col, int Row) z)
                => new[] { "oben", "mitte", "unten" }[z.Row] + " " + new[] { "links", "mitte", "rechts" }[z.Col];
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime Stamp, PageLayout? Layout)> _layoutCache = new();

        public static PageLayout? ReadPageLayout(string? pdfPath)
        {
            try
            {
                if (string.IsNullOrEmpty(pdfPath) || !File.Exists(pdfPath)
                    || !pdfPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) return null;
                var stamp = File.GetLastWriteTimeUtc(pdfPath);
                var key = pdfPath.ToUpperInvariant();
                if (_layoutCache.TryGetValue(key, out var hit) && hit.Stamp == stamp) return hit.Layout;
                PageLayout? result = null;
                using (var doc = PdfDocument.Open(pdfPath))
                {
                    if (doc.NumberOfPages > 0)
                    {
                        var page = doc.GetPage(1);
                        double w = page.Width, h = page.Height;
                        if (page.Rotation.Value % 180 != 0) (w, h) = (h, w);
                        result = new PageLayout { Landscape = w > h };
                        var words = page.GetWords().Where(x => !string.IsNullOrWhiteSpace(x.Text)).ToList();
                        foreach (var line in GroupLines(words))
                        {
                            var first = line.OrderBy(x => x.BoundingBox.Left).First();
                            result.Lines.Add((BuildLine(line),
                                Math.Clamp(first.BoundingBox.Left / Math.Max(1, page.Width), 0, 1),
                                Math.Clamp(1 - first.BoundingBox.Bottom / Math.Max(1, page.Height), 0, 1)));
                        }
                        if (words.Count < MinWords) result.Lines.Clear();
                    }
                }
                if (_layoutCache.Count >= MaxCacheEntries) _layoutCache.Clear();
                _layoutCache[key] = (stamp, result);
                return result;
            }
            catch { return null; }
        }

        private static string? ReadLayoutTextCore(string pdfPath, int maxPages)
        {
            try
            {
                using var doc = PdfDocument.Open(pdfPath);
                var sb = new StringBuilder();
                int wordCount = 0;
                int pageCount = Math.Min(maxPages, doc.NumberOfPages);
                for (int p = 1; p <= pageCount; p++)
                {
                    var page = doc.GetPage(p);
                    var words = page.GetWords().Where(w => !string.IsNullOrWhiteSpace(w.Text)).ToList();
                    wordCount += words.Count;
                    foreach (var line in GroupLines(words))
                        sb.AppendLine(BuildLine(line));
                }
                return wordCount >= MinWords ? sb.ToString() : null;
            }
            catch
            {
                return null; // beschädigt/verschlüsselt -> Fallback auf TXT
            }
        }

        /// <summary>Wörter mit ähnlicher Grundlinie zu einer Zeile zusammenfassen (von oben nach unten).</summary>
        private static List<List<Word>> GroupLines(List<Word> words)
        {
            var lines = new List<List<Word>>();
            foreach (var w in words.OrderByDescending(w => w.BoundingBox.Bottom))
            {
                double h = Math.Max(1, w.BoundingBox.Height);
                var target = lines.FirstOrDefault(l =>
                {
                    var refWord = l[0];
                    return Math.Abs(refWord.BoundingBox.Bottom - w.BoundingBox.Bottom) <= h * 0.5;
                });
                if (target == null) lines.Add(new List<Word> { w });
                else target.Add(w);
            }
            return lines;
        }

        /// <summary>
        /// Seitentext wie page.Text; enthält die Seite gedrehten (senkrechten) Text, werden diese Wörter
        /// je Drehrichtung in Leserichtung zu einer Zeile zusammengefasst ("folgende und Firma DFH Haus GmbH,").
        /// </summary>
        public static string PageText(Page page)
        {
            var words = page.GetWords().Where(w => !string.IsNullOrWhiteSpace(w.Text)).ToList();
            var rotated = words.Where(w => w.TextOrientation is TextOrientation.Rotate90 or TextOrientation.Rotate270).ToList();
            if (rotated.Count == 0) return page.Text;
            var sb = new StringBuilder();
            foreach (var line in GroupLines(words.Except(rotated).ToList()))
                sb.AppendLine(BuildLine(line));
            // Rotate90: von unten nach oben lesen, Rotate270: von oben nach unten; Spalten nach X trennen
            foreach (var g in rotated.GroupBy(w => (w.TextOrientation, Col: Math.Round(w.BoundingBox.Centroid.X / 6))))
            {
                var ordered = g.Key.TextOrientation == TextOrientation.Rotate90
                    ? g.OrderBy(w => w.BoundingBox.Centroid.Y)
                    : g.OrderByDescending(w => w.BoundingBox.Centroid.Y);
                sb.AppendLine(string.Join(" ", ordered.Select(w => w.Text)));
            }
            return sb.ToString();
        }

        /// <summary>Wörter einer Zeile von links nach rechts; großer Abstand = Spaltengrenze (Tab).</summary>
        private static string BuildLine(List<Word> line)
        {
            var sorted = line.OrderBy(w => w.BoundingBox.Left).ToList();
            var sb = new StringBuilder(sorted[0].Text);
            for (int i = 1; i < sorted.Count; i++)
            {
                var prev = sorted[i - 1];
                var cur = sorted[i];
                double charWidth = prev.BoundingBox.Width / Math.Max(1, prev.Text.Length);
                double gap = cur.BoundingBox.Left - prev.BoundingBox.Right;
                sb.Append(gap > Math.Max(charWidth * 3, 8) ? "\t" : " ");
                sb.Append(cur.Text);
            }
            return sb.ToString();
        }
    }
}
