using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Dateiumbenenner.Engine
{
    public static class DocumentPositionTransfer
    {
        public readonly record struct Span(int Index, int Length);
        public readonly record struct Position(int Line, int Column);
        private static readonly object Sync = new();
        private static readonly Dictionary<string, Regex> Matchers = new(StringComparer.Ordinal);
        private static readonly Queue<string> Keys = new();

        public static IReadOnlyList<Span> FindTerms(string? text, string? term)
            => FindMatches(text, term).Select(m => new Span(m.Index, m.Length)).ToArray();

        public static IEnumerable<Match> FindMatches(string? text, string? term)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(term)) return Array.Empty<Match>();
            var words = Regex.Matches(Regex.Replace(term, @"\bEMail\b", "E Mail", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), @"[\p{L}\p{N}]+")
                .Select(m => m.Value).ToArray();
            if (words.Length == 0) return term.Trim() == "#" ? Regex.Matches(text, "#").ToArray() : Array.Empty<Match>();
            var key = string.Join(" ", words);
            Regex regex;
            lock (Sync)
            {
                if (!Matchers.TryGetValue(key, out regex!))
                {
                    regex = new Regex(string.Join(@"[\s\p{P}\p{S}]*", words.Select(w => string.Join(@"\s*", w.Select(c => Regex.Escape(c.ToString()))))),
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                    if (Matchers.Count == 128) Matchers.Remove(Keys.Dequeue());
                    Matchers.Add(key, regex);
                    Keys.Enqueue(key);
                }
            }
            return regex.Matches(text).Where(m => (m.Index == 0 || !char.IsLetterOrDigit(text[m.Index - 1]))
                && (m.Index + m.Length == text.Length || !char.IsLetterOrDigit(text[m.Index + m.Length])));
        }

        public static Position At(string text, int index)
        {
            if (index < 0 || index >= text.Length) throw new ArgumentOutOfRangeException(nameof(index));
            int line = 0, column = 0;
            for (int i = 0; i < index; i++)
            {
                if (text[i] == '\r' || text[i] == '\n')
                {
                    if (text[i] == '\r' && i + 1 < index && text[i + 1] == '\n') i++;
                    line++;
                    column = 0;
                }
                else column += text[i] == '\t' ? 4 - column % 4 : 1;
            }
            return new Position(line, column);
        }

        public static string? Resolve(string? sourceText, IReadOnlyList<Span> sourceHits, string? targetText,
            IEnumerable<string> candidates, Func<string, IReadOnlyList<Span>> locate)
        {
            // Mehrere Quellfundstellen sind ohne ausdrueckliche Auswahl nicht eindeutig.
            if (string.IsNullOrEmpty(sourceText) || string.IsNullOrEmpty(targetText) || sourceHits.Count != 1) return null;
            var source = At(sourceText, sourceHits[0].Index);
            var matches = candidates.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.OrdinalIgnoreCase)
                .SelectMany(value => locate(value).Select(hit => (Value: value, Hit: hit, Position: At(targetText, hit.Index))))
                .Select(m => (m.Value, m.Hit, Line: Math.Abs(m.Position.Line - source.Line), Column: Math.Abs(m.Position.Column - source.Column)))
                .Where(m => m.Line <= 1 && m.Column <= 8)
                .Select(m => (m.Value, m.Hit, Score: m.Line * 32 + m.Column)).ToList();
            if (matches.Count == 0) return null;
            var best = matches.Where(m => m.Score == matches.Min(n => n.Score)).ToList();
            if (best.Select(m => m.Value).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1) return best[0].Value;
            // Verschachtelte Vorschlaege an derselben Stelle: den vollstaendigen Abschnitt nehmen.
            if (best.Select(m => m.Hit.Index).Distinct().Count() == 1)
            {
                var longest = best.Where(m => m.Hit.Length == best.Max(n => n.Hit.Length)).ToList();
                if (longest.Select(m => m.Value).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1) return longest[0].Value;
            }
            return null;
        }

    }
}