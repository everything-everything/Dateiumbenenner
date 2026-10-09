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
