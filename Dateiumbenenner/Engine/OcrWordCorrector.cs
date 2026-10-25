using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Dateiumbenenner.Engine
{
    /// <summary>
    /// Vorsichtige OCR-Wortkorrektur ohne externe Wörterbücher:
    /// Kandidaten kommen aus der mitgelieferten Fachwortliste, den eigenen Wörtern (@dictionary), bekannten Firmennamen
    /// und den aus den eigenen Belegen gelernten Wörtern (<see cref="OcrVocabulary"/>). Die Suche erfolgt mit SymSpell (MIT).
    /// Ein Wort wird nur ersetzt, wenn der Unterschied durch typische OCR-Verwechslungen erklärbar ist (z. B. rn/m, 1/l, 0/o, q/g, a/ä)
    /// und es weder bekannt noch laut Windows-Rechtschreibprüfung richtig ist.
    /// </summary>
    internal static class OcrWordCorrector
    {
        private const string ResourceName = "Dateiumbenenner.Data.OcrFachwoerter.txt";
        /// <summary>Ab so vielen Belegen gilt ein gelerntes Wort als geschützt.</summary>
        private const int ProtectDocs = 3;
        /// <summary>Ab so vielen Belegen darf ein gelerntes Wort Ziel einer Korrektur (nur bei Ziffern im Wort) bzw. Wortverbindung sein.</summary>
        private const int TargetDocs = 5;
        private const int ListWeight = 60;
        private const int CompanyWeight = 30;

        [Flags]
        private enum Kind { None = 0, Domain = 1, User = 2, Company = 4 }

        private sealed class Index
        {
            public required SymSpell Spell { get; init; }
            public required Dictionary<string, (string Canonical, Kind Kind)> Words { get; init; }
            public required object UserSource { get; init; }
            public required int CompanyCount { get; init; }
        }

        private static readonly Regex ChunkRx = new(@"\S+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex TokenRx = new(@"[\p{L}\p{N}]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly object _lock = new();
        private static volatile Index? _index;
        private static string[]? _domainWords;

        // Typische OCR-Verwechslungen: (im gelesenen Text, im richtigen Wort). Umlaute nur in eine Richtung (OCR verliert Punkte, erfindet sie selten).
        private static readonly (string Read, string Real)[] Confusions = BuildConfusions();

        private static (string, string)[] BuildConfusions()
        {
            var both = new (string, string)[]
            {
                ("rn", "m"), ("cl", "d"), ("li", "h"), ("vv", "w"), ("ri", "n"),
                ("1", "l"), ("1", "i"), ("l", "i"), ("0", "o"), ("5", "s"), ("8", "b"), ("6", "b"), ("9", "g"), ("q", "g"), ("2", "z"),
                ("c", "e"), ("h", "b"), ("u", "n"), ("f", "t"),
            };
            var oneWay = new (string, string)[] { ("ii", "ü"), ("u", "ü"), ("a", "ä"), ("o", "ö"), ("b", "ß") };
            return both.Concat(both.Select(p => (p.Item2, p.Item1))).Concat(oneWay).ToArray();
        }

        private static readonly Dictionary<char, char[]> DigitLetters = new()
        {
            ['1'] = new[] { 'l', 'i' }, ['0'] = new[] { 'o' }, ['5'] = new[] { 's' }, ['8'] = new[] { 'b' },
            ['6'] = new[] { 'b' }, ['9'] = new[] { 'g' }, ['2'] = new[] { 'z' },
        };

        /// <summary>Mitgelieferte Fachwörter (für Tests und Hilfe).</summary>
        public static IReadOnlyList<string> DomainWords
        {
            get
            {
                if (_domainWords != null) return _domainWords;
                var list = new List<string>();
                try
                {
                    using var s = typeof(OcrWordCorrector).Assembly.GetManifestResourceStream(ResourceName);
                    if (s != null)
                    {
                        using var r = new StreamReader(s, Encoding.UTF8);
                        string? line;
                        while ((line = r.ReadLine()) != null)
                        {
                            line = line.Trim();
                            if (line.Length >= 2 && !line.StartsWith('#')) list.Add(line);
                        }
                    }
                }
                catch { }
                return _domainWords = list.ToArray();
            }
        }

        /// <summary>Wörter aus dem Belegtext lernen (Grundlage für den Schutz von Namen/Fachbegriffen).</summary>
        public static void Learn(string? text) => OcrVocabulary.Learn(text);

        /// <summary>True, wenn das (zusammengesetzte) Wort bekannt ist – für das Zusammenfügen getrennter Wörter („Rechnu ng“).</summary>
        public static bool IsKnownWord(string word, ICollection<string> userWords)
        {
            if (string.IsNullOrEmpty(word)) return false;
            var lower = word.ToLowerInvariant();
            if (GetIndex(userWords).Words.ContainsKey(lower)) return true;
            return word.All(char.IsLetter) && OcrVocabulary.DocFrequency(lower) >= TargetDocs;
        }

        /// <summary>Korrigiert einzelne Wörter im Text (nur eindeutige, durch OCR-Verwechslungen erklärbare Fälle).</summary>
        public static string Correct(string text, ICollection<string> userWords)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var idx = GetIndex(userWords);
            var proposals = new List<(int Start, int Length, string Original, string Replacement, bool LettersOnly)>();
            var memo = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (Match chunk in ChunkRx.Matches(text))
            {
                var c = chunk.Value;
                // Mail-/Web-Adressen und Kennungen nicht anfassen
                if (c.Contains('@') || c.Contains("://", StringComparison.Ordinal) || c.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) continue;
                foreach (Match tm in TokenRx.Matches(c))
                {
                    var tok = tm.Value;
                    if (tok.Length < 4 || tok.Length > 40) continue;
                    int letters = tok.Count(char.IsLetter), digits = tok.Length - letters;
                    bool lettersOnly = digits == 0;
                    if (!lettersOnly && (digits > 2 || letters < 4 || letters < 2 * digits)) continue;
                    if (!memo.TryGetValue(tok, out var rep)) memo[tok] = rep = Propose(idx, tok, lettersOnly);
                    if (rep != null && rep != tok) proposals.Add((chunk.Index + tm.Index, tok.Length, tok, rep, lettersOnly));
                }
            }
            if (proposals.Count == 0) return text;

            // Was Windows als richtig kennt, bleibt unverändert
            var ask = proposals.Where(p => p.LettersOnly).Select(p => CheckForm(p.Original)).Distinct(StringComparer.Ordinal).ToList();
            var correct = ask.Count > 0 ? WindowsSpellChecker.CorrectWords(ask) : new HashSet<string>();

            var sb = new StringBuilder(text.Length);
            int pos = 0;
            foreach (var p in proposals)
            {
                if (p.LettersOnly && correct.Contains(CheckForm(p.Original))) continue;
                sb.Append(text, pos, p.Start - pos).Append(p.Replacement);
                pos = p.Start + p.Length;
            }
            sb.Append(text, pos, text.Length - pos);
            return sb.ToString();
        }

        /// <summary>Windows überspringt Wörter in GROSSBUCHSTABEN (gelten immer als richtig) – daher in normaler Schreibweise prüfen.</summary>
        private static string CheckForm(string word) =>
            word.Length > 1 && word.All(char.IsUpper) ? word[0] + word[1..].ToLowerInvariant() : word;

        private static string? Propose(Index idx, string tok, bool lettersOnly)
        {
            var lower = tok.ToLowerInvariant();
            if (idx.Words.ContainsKey(lower)) return null;
            int ownDocs = lettersOnly ? OcrVocabulary.DocFrequency(lower) : 0;
            if (!lettersOnly)
            {
                // „Lager5“, „1Rechnung“: Zahl hängt nur an einem bekannten Wort -> keine Verwechslung
                var core = lower.Trim('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
                if (core.Length < lower.Length && (idx.Words.ContainsKey(core) || OcrVocabulary.DocFrequency(core) >= TargetDocs)) return null;
            }
            int maxOps = tok.Length < 6 ? 1 : 2;

            var cands = new List<(string Term, string Canonical, int Cost, long Weight)>();
            foreach (var s in idx.Spell.Lookup(lower, SymSpell.Verbosity.All, 2))
            {
                if (s.term == lower || !idx.Words.TryGetValue(s.term, out var e)) continue;
                // Wörter ohne Ziffern nur auf Fachwörter/eigene Wörter korrigieren, Firmenwörter nur bei Ziffernverwechslung
                if (lettersOnly && (e.Kind & (Kind.Domain | Kind.User)) == 0) continue;
                int cost = OcrCost(lower, s.term, maxOps);
                if (cost <= 0) continue;
                long weight = ((e.Kind & (Kind.Domain | Kind.User)) != 0 ? ListWeight : CompanyWeight) + OcrVocabulary.DocFrequency(s.term);
                cands.Add((s.term, e.Canonical, cost, weight));
            }
            if (!lettersOnly)
            {
                // Ziffern durch Buchstaben ersetzen und in den gelernten Wörtern suchen („Liefersche1n“)
                foreach (var v in DigitVariants(lower))
                {
                    if (idx.Words.ContainsKey(v) || cands.Any(c => c.Term == v)) continue;
                    int df = OcrVocabulary.DocFrequency(v);
                    int cost = lower.Count(char.IsDigit);
                    if (df >= TargetDocs && cost <= maxOps) cands.Add((v, v, cost, df));
                }
            }
            if (cands.Count == 0) return null;

            var ordered = cands.OrderBy(c => c.Cost).ThenByDescending(c => c.Weight).ToList();
            var best = ordered[0];
            // Mehrdeutig (z. B. zwei Fachwörter gleich nah) -> nichts ändern
            if (ordered.Count > 1 && ordered[1].Cost == best.Cost && ordered[1].Weight * 5 > best.Weight) return null;
            // Häufig in den eigenen Belegen -> vermutlich ein echtes Wort/Name, außer das Ziel ist viel häufiger
            if (ownDocs >= ProtectDocs && best.Weight < 20L * ownDocs) return null;
            return ApplyCase(tok, best.Canonical);
        }

        private static IEnumerable<string> DigitVariants(string lower)
        {
            var results = new List<string> { "" };
            foreach (var ch in lower)
            {
                if (char.IsDigit(ch))
                {
                    if (!DigitLetters.TryGetValue(ch, out var opts)) return Array.Empty<string>();
                    results = results.SelectMany(r => opts.Select(o => r + o)).ToList();
                }
                else results = results.Select(r => r + ch).ToList();
            }
            return results;
        }

        /// <summary>Anzahl OCR-Verwechslungen, die <paramref name="read"/> in <paramref name="real"/> überführen; -1, wenn nicht erklärbar oder zu viele.</summary>
        internal static int OcrCost(string read, string real, int maxOps)
        {
            int n = read.Length, m = real.Length;
            const int Inf = int.MaxValue / 2;
            var dp = new int[n + 1, m + 1];
            for (int i = 0; i <= n; i++) for (int j = 0; j <= m; j++) dp[i, j] = Inf;
            dp[0, 0] = 0;
            for (int i = 0; i <= n; i++)
            {
                for (int j = 0; j <= m; j++)
                {
                    int cur = dp[i, j];
                    if (cur >= Inf) continue;
                    if (i < n && j < m && read[i] == real[j] && cur < dp[i + 1, j + 1]) dp[i + 1, j + 1] = cur;
                    if (cur >= maxOps) continue;
                    foreach (var (r, t) in Confusions)
                    {
                        if (i + r.Length > n || j + t.Length > m) continue;
                        if (string.CompareOrdinal(read, i, r, 0, r.Length) != 0 || string.CompareOrdinal(real, j, t, 0, t.Length) != 0) continue;
                        if (cur + 1 < dp[i + r.Length, j + t.Length]) dp[i + r.Length, j + t.Length] = cur + 1;
                    }
                }
            }
            int res = dp[n, m];
            return res <= maxOps ? res : -1;
        }

        /// <summary>Schreibweise des Originals übernehmen (GROSS bleibt GROSS); Wörter wie „GmbH“ in ihrer üblichen Schreibweise.</summary>
        private static string ApplyCase(string original, string canonical)
        {
            var letters = original.Where(char.IsLetter).ToList();
            if (letters.Count >= 2 && letters.All(char.IsUpper)) return canonical.ToUpperInvariant();
            if (canonical.Skip(1).Any(char.IsUpper)) return canonical;
            if (char.IsUpper(original[0])) return char.ToUpperInvariant(canonical[0]) + canonical[1..];
            if (char.IsLower(original[0])) return char.ToLowerInvariant(canonical[0]) + canonical[1..];
            return canonical;
        }

        private static Index GetIndex(ICollection<string> userWords)
        {
            int companies = CompanyMatcher.KnownCount;
            var idx = _index;
            if (idx != null && ReferenceEquals(idx.UserSource, userWords) && idx.CompanyCount == companies) return idx;
            lock (_lock)
            {
                idx = _index;
                if (idx != null && ReferenceEquals(idx.UserSource, userWords) && idx.CompanyCount == companies) return idx;
                return _index = Build(userWords, companies);
            }
        }

        private static Index Build(ICollection<string> userWords, int companyCount)
        {
            var words = new Dictionary<string, (string Canonical, Kind Kind)>(StringComparer.Ordinal);
            void Add(string w, Kind k)
            {
                w = w.Trim();
                if (w.Length < 2) return;
                var l = w.ToLowerInvariant();
                words[l] = words.TryGetValue(l, out var e) ? (e.Canonical, e.Kind | k) : (w, k);
            }
            foreach (var w in DomainWords) Add(w, Kind.Domain);
            foreach (var w in userWords.ToArray()) Add(w, Kind.User);
            foreach (var name in CompanyMatcher.KnownNames())
                foreach (Match m in TokenRx.Matches(name))
                    if (m.Value.Length >= 4 && m.Value.All(char.IsLetter)) Add(m.Value, Kind.Company);

            var spell = new SymSpell(Math.Max(16, words.Count), 2, 7);
            foreach (var l in words.Keys)
                if (l.Length >= 3) spell.CreateDictionaryEntry(l, 1);
            return new Index { Spell = spell, Words = words, UserSource = userWords, CompanyCount = companyCount };
        }
    }
}
