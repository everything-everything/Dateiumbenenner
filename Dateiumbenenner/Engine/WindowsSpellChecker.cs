using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Dateiumbenenner.Engine
{
    /// <summary>
    /// Rechtschreibprüfung von Windows (ab Windows 8, nutzt die installierten Sprachpakete, kein Download).
    /// Wird nur als Bestätigung verwendet: Ein Wort, das Windows kennt, wird von der OCR-Korrektur nicht verändert.
    /// Fehlt die Prüfung (z. B. kein Sprachpaket), arbeitet die Korrektur ohne sie weiter.
    /// </summary>
    internal static class WindowsSpellChecker
    {
        private static readonly string[] WantedLanguages = { "de-DE", "en-US" };
        private static readonly object _lock = new();
        private static readonly ConcurrentDictionary<string, bool> _cache = new(StringComparer.Ordinal);
        private static List<ISpellChecker>? _checkers;
        private static readonly List<string> _languages = new();
        private static volatile bool _initialized;

        /// <summary>Verfügbare Sprachen (leer, wenn die Windows-Prüfung nicht nutzbar ist).</summary>
        public static IReadOnlyList<string> Languages
        {
            get
            {
                RunMta(Init);
                lock (_lock) return _languages.ToArray();
            }
        }

        /// <summary>Liefert die Wörter, die laut Windows in mindestens einer Sprache richtig geschrieben sind.</summary>
        public static HashSet<string> CorrectWords(IReadOnlyCollection<string> words)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            var todo = new List<string>();
            foreach (var w in words)
            {
                if (string.IsNullOrWhiteSpace(w)) continue;
                if (_cache.TryGetValue(w, out var ok)) { if (ok) result.Add(w); }
                else todo.Add(w);
            }
            if (todo.Count == 0) return result;

            var found = new List<string>();
            try
            {
                RunMta(() =>
                {
                    Init();
                    lock (_lock)
                    {
                        if (_checkers == null || _checkers.Count == 0) return;
                        foreach (var w in todo)
                        {
                            bool ok = false;
                            foreach (var c in _checkers)
                                if (IsCorrect(c, w)) { ok = true; break; }
                            _cache[w] = ok;
                            if (ok) found.Add(w);
                        }
                    }
                });
            }
            catch { }
            if (_cache.Count > 200_000) _cache.Clear();
            result.UnionWith(found);
            return result;
        }

        // Die COM-Objekte werden im MTA angelegt und benutzt; vom UI-Thread (STA) aus wird auf einen Pool-Thread gewechselt.
        private static void RunMta(Action action)
        {
            if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA) Task.Run(action).GetAwaiter().GetResult();
            else action();
        }

        private static void Init()
        {
            if (_initialized) return;
            lock (_lock)
            {
                if (_initialized) return;
                try
                {
                    if (!OperatingSystem.IsWindowsVersionAtLeast(6, 2)) return;
                    var type = Type.GetTypeFromCLSID(new Guid("7AB36653-1796-484B-BDFA-E74F1DB7C1DC"), false);
                    if (type == null || Activator.CreateInstance(type) is not ISpellCheckerFactory factory) return;
                    var list = new List<ISpellChecker>();
                    foreach (var lang in WantedLanguages)
                    {
                        try
                        {
                            if (factory.IsSupported(lang, out var supported) != 0 || supported == 0) continue;
                            if (factory.CreateSpellChecker(lang, out var checker) != 0 || checker == null) continue;
                            list.Add(checker);
                            _languages.Add(lang);
                        }
                        catch { }
                    }
                    _checkers = list;
                }
                catch { _checkers = null; }
                finally { _initialized = true; }
            }
        }

        private static bool IsCorrect(ISpellChecker checker, string word)
        {
            IEnumSpellingError? errors = null;
            try
            {
                if (checker.Check(word, out errors) != 0 || errors == null) return false;
                int hr = errors.Next(out var error);
                if (error != IntPtr.Zero) Marshal.Release(error);
                return hr == 1; // S_FALSE: kein Fehler gefunden
            }
            catch { return false; }
            finally { if (errors != null) Marshal.ReleaseComObject(errors); }
        }

        [ComImport, Guid("8E018A9D-2415-4677-BF08-794EA61F94BB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ISpellCheckerFactory
        {
            [PreserveSig] int get_SupportedLanguages(out IntPtr value);
            [PreserveSig] int IsSupported([MarshalAs(UnmanagedType.LPWStr)] string languageTag, out int value);
            [PreserveSig] int CreateSpellChecker([MarshalAs(UnmanagedType.LPWStr)] string languageTag, out ISpellChecker value);
        }

        [ComImport, Guid("B6FD0B71-E2BC-4653-8D05-F197E412770B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ISpellChecker
        {
            [PreserveSig] int get_LanguageTag(out IntPtr value);
            [PreserveSig] int Check([MarshalAs(UnmanagedType.LPWStr)] string text, out IEnumSpellingError value);
        }

        [ComImport, Guid("803E3BD4-2828-4410-8290-418D1D73C762"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IEnumSpellingError
        {
            [PreserveSig] int Next(out IntPtr value);
        }
    }
}
