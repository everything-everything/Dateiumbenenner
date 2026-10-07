using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using WeCantSpell.Hunspell;

namespace Dateiumbenenner
{
    /// <summary>
    /// Externe konfigurierbare OCR-Korrektur. Datei OcrCorrections.txt:
    ///   fehler=korrekt            (einfache Ersetzung)
    ///   @labels=A,B,C             (Liste von Label-Wörtern für automatische Abstandskorrektur)
    ///   @regex=pattern=>replacement (optionale Regex Ersetzung)
    ///   @dictionary=wort1,wort2   (Wörterbuch für Rechtschreibprüfung)
    ///   @spellcheck=de_DE,en_US   (Hunspell-Wörterbücher aktivieren)
    /// </summary>
    [Flags]
    public enum GenericOcrRules
    {
        None = 0,
        NormalizeLineEndings = 1 << 0,
        ColonSpacing = 1 << 1,
        SplitLongNumberDate = 1 << 2,
        SpaceBeforeSeite = 1 << 3,
        LabelSpacing = 1 << 4,
        DocumentTypeUndSplit = 1 << 5,
        ReduceMultipleSpaces = 1 << 6,
        NormalizeBlankLines = 1 << 7,
        Trim = 1 << 8,
        All = NormalizeLineEndings | ColonSpacing | SplitLongNumberDate | SpaceBeforeSeite | LabelSpacing | DocumentTypeUndSplit | ReduceMultipleSpaces | NormalizeBlankLines | Trim
    }

    public static class OcrTextCorrector
    {
        // Globale Aktivierung/Deaktivierung der gesamten OCR-Korrektur (Standard: an)
        public static bool OcrEnabled { get; set; } = true;

        public static string CorrectionsFilePath { get; set; } =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "OcrCorrections.txt");

        public static Dictionary<string, string> Corrections { get; private set; } = new();
        private static readonly List<string> _labels = new();
        private static readonly List<(Regex rx, string repl)> _regexReplacements = new();
        private static readonly HashSet<string> _dictionary = new(StringComparer.OrdinalIgnoreCase);
        
        // Hunspell Spell Checker
        private static readonly List<WordList> _spellCheckers = new();
        private static bool _useSpellCheck = false;

        private static bool _loaded;
        private static readonly object _lock = new();
        // Hunspell (WordList) wird zur Sicherheit nur von einem Thread gleichzeitig benutzt
        private static readonly object _spellLock = new();

        public static string Fix(string input, bool applyGenericRules = true)
        {
            if (string.IsNullOrEmpty(input)) return input;
            EnsureLoaded();

            // Vollständig deaktiviert oder Schalter aus -> unveränderten Originaltext zurückgeben
            if (!OcrEnabled || !applyGenericRules)
                return input;

            var text = input;
            foreach (var kv in Corrections)
                text = text.Replace(kv.Key, kv.Value);
            foreach (var (rx, repl) in _regexReplacements)
                text = rx.Replace(text, repl);
            text = FixSpacesInWords(text);
            if (_useSpellCheck && _spellCheckers.Any())
                text = FixSpellingErrors(text);
            text = ApplyGenericRules(text, GenericOcrRules.All);
            return text;
        }

        // Neues Overload für gezielte Regelwahl
        public static string Fix(string input, GenericOcrRules rules)
        {
            if (string.IsNullOrEmpty(input)) return input;
            EnsureLoaded();

            // Vollständig deaktiviert oder keine Regeln gewünscht -> Original zurückgeben
            if (!OcrEnabled || rules == GenericOcrRules.None)
                return input;

            var text = input;
            foreach (var kv in Corrections)
                text = text.Replace(kv.Key, kv.Value);
            foreach (var (rx, repl) in _regexReplacements)
                text = rx.Replace(text, repl);
            text = FixSpacesInWords(text);
            if (_useSpellCheck && _spellCheckers.Any())
                text = FixSpellingErrors(text);
            text = ApplyGenericRules(text, rules);
            return text;
        }

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            lock (_lock)
            {
                if (_loaded) return;
                Corrections.Clear();
                _labels.Clear();
                _regexReplacements.Clear();
                _dictionary.Clear();
                
                // Alte Spell Checker freigeben
                _spellCheckers.Clear();
                _useSpellCheck = false;
                
                // Standard-Wörterbuch mit häufigen OCR-relevanten Wörtern
                InitializeDefaultDictionary();
                
                try
                {
                    if (File.Exists(CorrectionsFilePath))
                    {
                        foreach (var rawLine in File.ReadAllLines(CorrectionsFilePath))
                        {
                            var line = rawLine.Trim();
                            if (line.Length == 0 || line.StartsWith("#")) continue;

                            // @labels=...
                            if (line.StartsWith("@labels=", StringComparison.OrdinalIgnoreCase))
                            {
                                var list = line.Substring(8)
                                               .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                                               .Select(s => s.Trim())
                                               .Where(s => s.Length > 0);
                                foreach (var l in list)
                                    if (!_labels.Contains(l)) _labels.Add(l);
                                continue;
                            }

                            // @regex=pattern=>replacement
                            if (line.StartsWith("@regex=", StringComparison.OrdinalIgnoreCase))
                            {
                                var spec = line.Substring(7);
                                var arrow = spec.IndexOf("=>", StringComparison.Ordinal);
                                if (arrow > 0)
                                {
                                    var pat = spec.Substring(0, arrow).Trim();
                                    var repl = spec.Substring(arrow + 2).Trim();
                                    try { _regexReplacements.Add((new Regex(pat, RegexOptions.Compiled), repl)); } catch { }
                                }
                                continue;
                            }

                            // @dictionary=wort1,wort2,...
                            if (line.StartsWith("@dictionary=", StringComparison.OrdinalIgnoreCase))
                            {
                                var words = line.Substring(12)
                                               .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                                               .Select(s => s.Trim())
                                               .Where(s => s.Length > 0);
                                foreach (var word in words)
                                    _dictionary.Add(word);
                                continue;
                            }

                            // @spellcheck=de_DE,en_US (NEU)
                            if (line.StartsWith("@spellcheck=", StringComparison.OrdinalIgnoreCase))
                            {
                                var languages = line.Substring(12)
                                               .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                                               .Select(s => s.Trim())
                                               .Where(s => s.Length > 0);
                                foreach (var lang in languages)
                                {
                                    LoadHunspellDictionary(lang);
                                }
                                continue;
                            }

                            // Standard Muster=Ersetzung
                            var eq = line.IndexOf('=');
                            if (eq <= 0) continue;
                            var key = line[..eq];
                            var value = line[(eq + 1)..];
                            if (!Corrections.ContainsKey(key))
                                Corrections[key] = value;
                        }
                    }
                    else
                    {
                        File.WriteAllText(CorrectionsFilePath,
                            "# OCR Korrekturen\n" +
                            "# Einfach: falsch=richtig\n" +
                            "# Labels (Abstände automatisch): @labels=Auftragsnummer,Datum,Kundennummer,Belegnummer,Standort,Lieferdatum,Besteller,Seite\n" +
                            "# Regex: @regex=(\\d{6,})(\\d{4}/\\d{1,2})=>$1 $2\n" +
                            "# Wörterbuch für Rechtschreibprüfung: @dictionary=Rechnung,Gutschrift,Lieferschein\n" +
                            "# Hunspell Rechtschreibprüfung (benötigt .dic/.aff Dateien im Dictionaries-Ordner):\n" +
                            "# @spellcheck=de_DE,en_US\n" +
                            "# Direkte Korrekturen für häufige OCR-Fehler:\n" +
                            "Rechnu ng=Rechnung\n" +
                            "Gu tschrift=Gutschrift\n" +
                            "Liefe rschein=Lieferschein\n");
                    }
                }
                catch { }
                _loaded = true;
            }
        }

        private static void LoadHunspellDictionary(string language)
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var dictDir = Path.Combine(baseDir, "Dictionaries");
                var affFile = Path.Combine(dictDir, $"{language}.aff");
                var dicFile = Path.Combine(dictDir, $"{language}.dic");

                if (File.Exists(affFile) && File.Exists(dicFile))
                {
                    var hunspell = WordList.CreateFromFiles(dicFile, affFile);
                    _spellCheckers.Add(hunspell);
                    _useSpellCheck = true;
                }
            }
            catch
            {
                // Fehler beim Laden ignorieren
            }
        }

        private static void InitializeDefaultDictionary()
        {
            // Häufige Dokumenttypen und OCR-relevante Begriffe
            var defaultWords = new[]
            {
                "Rechnung", "Gutschrift", "Lieferschein", "Bestellung", "Angebot", "Mahnung",
                "Sammelrechnung", "Stornorechnung", "Beleg", "Auftrag", "Lieferung",
                "Rechnungsnummer", "Belegnummer", "Auftragsnummer", "Kundennummer",
                "Datum", "Rechnungsdatum", "Lieferdatum", "Bestelldatum",
                "Kunde", "Lieferant", "Besteller", "Empfänger", "Absender",
                "Straße", "Postleitzahl", "Stadt", "Land",
                "Betrag", "Summe", "Netto", "Brutto", "Steuer", "Mehrwertsteuer",
                "Artikel", "Position", "Menge", "Preis", "Einzelpreis", "Gesamtpreis",
                "Seite", "Blatt", "Kopie", "Original",
                "Standort", "Filiale", "Niederlassung",
                "Zahlungsbedingungen", "Zahlungsziel", "Skonto",
                "Rabatt", "Prozent", "Euro", "Währung",
                "Widmann", "GmbH", "KG", "AG", "UG", "OHG"
            };

            foreach (var word in defaultWords)
                _dictionary.Add(word);
        }

        private static string FixSpacesInWords(string text)
        {
            // VEREINFACHT: Nur offensichtliche OCR-Fehler korrigieren
            // Mehrfach-Durchgang für komplexere Fälle wie "Widma nn" ? "Widmann"
            for (int pass = 0; pass < 2; pass++) // Reduziert auf 2 Durchgänge
            {
                bool changed = false;
                
                // SEHR RESTRIKTIV: Findet Wörter die durch EINZELNES Leerzeichen getrennt sind
                // UND nur wenn der zweite Teil sehr kurz ist (max 2 Zeichen)
                // z.B. "Rechnu ng" oder "Widma nn" aber NICHT "WAGER FISCHER"
                var pattern = @"\b(\w{3,})(\s)(\w{1,2})\b";
                
                text = Regex.Replace(text, pattern, match =>
                {
                    var part1 = match.Groups[1].Value;
                    var space = match.Groups[2].Value;
                    var part2 = match.Groups[3].Value;
                    var combined = part1 + part2;
                    
                    // NUR korrigieren wenn:
                    // 1. Der zweite Teil nur 1-2 Zeichen hat
                    // 2. UND das kombinierte Wort im Dictionary existiert
                    if (part2.Length <= 2)
                    {
                        // Prüfe zuerst im lokalen Dictionary
                        if (_dictionary.Contains(combined))
                        {
                            changed = true;
                            return combined;
                        }
                        
                        // Auch versuchen mit Großbuchstaben am Anfang
                        var combinedCapitalized = Capitalize(part1) + part2;
                        if (_dictionary.Contains(combinedCapitalized))
                        {
                            changed = true;
                            return combinedCapitalized;
                        }
                        
                        // Falls Hunspell aktiv ist, auch dort prüfen
                        if (_useSpellCheck)
                        {
                            if (IsCorrectSpelling(combined))
                            {
                                changed = true;
                                return combined;
                            }
                            if (IsCorrectSpelling(combinedCapitalized))
                            {
                                changed = true;
                                return combinedCapitalized;
                            }
                        }
                    }
                    
                    return match.Value; // Keine Änderung - Leerzeichen BLEIBT
                });
                
                // Wenn keine Änderungen mehr, beende die Schleife
                if (!changed) break;
            }
            
            return text;
        }

        private static string FixSpellingErrors(string text)
        {
            if (!_useSpellCheck || !_spellCheckers.Any())
                return text;

            // Wort für Wort durchgehen und Rechtschreibfehler korrigieren
            var wordPattern = @"\b([A-Za-zÄÖÜäöüß]+)\b";
            
            return Regex.Replace(text, wordPattern, match =>
            {
                var word = match.Groups[1].Value;
                
                // Nur Wörter mit mindestens 4 Buchstaben prüfen (zu kurze Wörter überspringen)
                if (word.Length < 4)
                    return word;
                
                // Zahlen und gemischte Wörter überspringen
                if (word.Any(char.IsDigit))
                    return word;
                
                // Prüfe ob das Wort korrekt ist
                if (IsCorrectSpelling(word))
                    return word;
                
                // Hole Korrekturvorschläge
                var suggestions = GetSuggestions(word);
                if (suggestions.Any())
                {
                    // Nimm den ersten (wahrscheinlichsten) Vorschlag
                    return suggestions.First();
                }
                
                return word; // Keine Änderung wenn keine Vorschläge
            });
        }

        private static bool IsCorrectSpelling(string word)
        {
            if (!_useSpellCheck || !_spellCheckers.Any())
                return false;

            // Prüfe in allen geladenen Wörterbüchern
            lock (_spellLock)
            {
                foreach (var checker in _spellCheckers)
                {
                    if (checker.Check(word))
                        return true;
                }
            }
            
            return false;
        }

        private static List<string> GetSuggestions(string word)
        {
            var allSuggestions = new List<string>();
            
            if (!_useSpellCheck || !_spellCheckers.Any())
                return allSuggestions;

            // Sammle Vorschläge aus allen Wörterbüchern
            lock (_spellLock)
            foreach (var checker in _spellCheckers)
            {
                var suggestions = checker.Suggest(word).ToList();
                if (suggestions != null && suggestions.Count > 0)
                {
                    allSuggestions.AddRange(suggestions);
                }
            }
            
            return allSuggestions.Distinct().ToList();
        }

        private static string ApplyGenericRules(string text)
        {
            // Alte Signatur bleibt für Abwärtskompatibilität und ruft nun Flags-Version auf
            return ApplyGenericRules(text, GenericOcrRules.All);
        }

        private static string ApplyGenericRules(string text, GenericOcrRules rules)
        {
            // NormalizeLineEndings
            if (rules.HasFlag(GenericOcrRules.NormalizeLineEndings))
                text = text.Replace("\r\n", "\n").Replace("\r", "\n");

            if (rules.HasFlag(GenericOcrRules.ColonSpacing))
                text = Regex.Replace(text, "(\\b\\w+:)(?!\\s)", "$1 ");

            if (rules.HasFlag(GenericOcrRules.SplitLongNumberDate))
                text = Regex.Replace(text, "(\\d{10,})(\\d{4}/\\d{1,2})", "$1 $2");

            if (rules.HasFlag(GenericOcrRules.SpaceBeforeSeite))
                text = Regex.Replace(text, "(\\d{4}/\\d{1,2})(Seite)", "$1 $2", RegexOptions.IgnoreCase);

            if (rules.HasFlag(GenericOcrRules.LabelSpacing))
            {
                foreach (var label in _labels)
                {
                    if (string.IsNullOrWhiteSpace(label)) continue;
                    text = Regex.Replace(text, $"{Regex.Escape(label)}(?=\\d)", label + " ");
                    text = Regex.Replace(text, $"(?<=\\d){Regex.Escape(label)}", " " + label);
                }
            }

            if (rules.HasFlag(GenericOcrRules.DocumentTypeUndSplit))
                text = Regex.Replace(text, "\\b(Rechnung|Gutschrift|Sammelrechnung|Stornorechnung|Beleg|Angebot|Lieferschein|Bestellung|Mahnung)und\\b", "$1 und", RegexOptions.IgnoreCase);

            if (rules.HasFlag(GenericOcrRules.ReduceMultipleSpaces))
                text = Regex.Replace(text, "[ ]{2,}", " ");

            if (rules.HasFlag(GenericOcrRules.NormalizeBlankLines))
                text = Regex.Replace(text, "\n{3,}", "\n\n");

            if (rules.HasFlag(GenericOcrRules.NormalizeLineEndings))
                text = text.Replace("\n", "\r\n");

            if (rules.HasFlag(GenericOcrRules.Trim))
                text = text.Trim();

            return text;
        }

        private static string Capitalize(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..];
    }
}
