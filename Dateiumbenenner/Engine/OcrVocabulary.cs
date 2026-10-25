using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace Dateiumbenenner.Engine
{
    /// <summary>
    /// Aus den eigenen Belegen gelernte Wörter: je Wort die Anzahl der Belege, in denen es vorkommt.
    /// Bleibt lokal im Profilordner (bei Passwortschutz verschlüsselt) und wird mit „Gelernte Daten löschen“ entfernt.
    /// Häufige Wörter schützen Namen und Fachbegriffe vor einer Korrektur; nur Wörter ohne Ziffern werden gelernt.
    /// </summary>
    internal static class OcrVocabulary
    {
        public const string FileName = "ocr_woerter.json";
        private const int MaxWords = 300_000;
        private const int MaxDocs = 100_000;
        private static readonly Regex TokenRx = new(@"[\p{L}\p{N}]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly object _lock = new();
        private static ConcurrentDictionary<string, int>? _words;
        private static HashSet<ulong> _docSet = new();
        private static Queue<ulong> _docOrder = new();
        private static bool _dirty, _blocked, _exitHooked;
        private static System.Threading.Timer? _saveTimer;

        private sealed class Data
        {
            public int V { get; set; } = 1;
            public List<ulong> Docs { get; set; } = new();
            public Dictionary<string, int> Words { get; set; } = new();
        }

        private static string FilePath => Path.Combine(ProfileManager.ActiveDirectory, FileName);

        /// <summary>In wie vielen Belegen das Wort (klein geschrieben) vorkam.</summary>
        public static int DocFrequency(string lower) => Words.TryGetValue(lower, out var c) ? c : 0;

        public static int Count => Words.Count;

        private static ConcurrentDictionary<string, int> Words
        {
            get
            {
                var w = _words;
                if (w != null) return w;
                lock (_lock)
                {
                    if (_words != null) return _words;
                    var dict = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
                    try
                    {
                        if (File.Exists(FilePath))
                        {
                            var data = JsonSerializer.Deserialize<Data>(SecureStorage.ReadAllText(FilePath));
                            if (data != null)
                            {
                                foreach (var kv in data.Words) if (kv.Value > 0) dict[kv.Key] = kv.Value;
                                foreach (var h in data.Docs) if (_docSet.Add(h)) _docOrder.Enqueue(h);
                            }
                        }
                    }
                    catch { _blocked = true; } // nicht lesbar (z. B. gesperrt) -> Datei nicht überschreiben
                    if (!_exitHooked) { _exitHooked = true; AppDomain.CurrentDomain.ProcessExit += (_, _) => Save(); }
                    return _words = dict;
                }
            }
        }

        /// <summary>Wörter eines Belegtexts lernen (jeder Beleg zählt nur einmal, auch bei erneuter Auswertung).</summary>
        public static void Learn(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var tokens = new HashSet<string>(StringComparer.Ordinal);
            var all = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match m in TokenRx.Matches(text))
            {
                var t = m.Value;
                all.Add(t);
                if (t.Length < 4 || t.Length > 40 || !t.All(char.IsLetter)) continue;
                tokens.Add(t.ToLowerInvariant());
            }
            if (tokens.Count == 0) return;
            // Reihenfolgeunabhängiger Fingerabdruck (inkl. Zahlen), damit ein anders umbrochener Text nicht doppelt zählt,
            // Belege nach gleicher Vorlage mit anderen Nummern aber schon
            ulong hash = (ulong)all.Count * 0x9E3779B97F4A7C15UL;
            foreach (var t in all) hash += Fnv(t);

            var words = Words;
            lock (_lock)
            {
                if (_blocked || !_docSet.Add(hash)) return;
                _docOrder.Enqueue(hash);
                while (_docOrder.Count > MaxDocs) _docSet.Remove(_docOrder.Dequeue());
                foreach (var t in tokens) words.AddOrUpdate(t, 1, (_, c) => c + 1);
                if (words.Count > MaxWords)
                    foreach (var kv in words) if (kv.Value <= 1) words.TryRemove(kv.Key, out _);
                _dirty = true;
                _saveTimer ??= new System.Threading.Timer(_ => Save(), null, Timeout.Infinite, Timeout.Infinite);
                _saveTimer.Change(10_000, Timeout.Infinite);
            }
        }

        public static void Save()
        {
            string json;
            lock (_lock)
            {
                if (!_dirty || _blocked || _words == null) return;
                json = JsonSerializer.Serialize(new Data { Docs = _docOrder.ToList(), Words = new Dictionary<string, int>(_words, StringComparer.Ordinal) });
                _dirty = false;
            }
            try { SecureStorage.WriteAllText(FilePath, json); }
            catch { lock (_lock) _dirty = true; }
        }

        /// <summary>Gelerntes vergessen (beim Löschen der gelernten Daten); danach wird in dieser Sitzung nichts mehr gespeichert.</summary>
        public static void Forget()
        {
            lock (_lock)
            {
                _words?.Clear();
                _docSet.Clear();
                _docOrder.Clear();
                _dirty = false;
                _blocked = true;
                _saveTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }

        private static ulong Fnv(string s)
        {
            ulong h = 14695981039346656037UL;
            foreach (var ch in s) { h ^= ch; h *= 1099511628211UL; }
            return h;
        }
    }
}
