using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Dateiumbenenner.Engine
{
    /// <summary>Wo das falsche Muster in der Nummer steht.</summary>
    public enum FixPosition { Anfang, Ende, Beliebig }

    /// <summary>
    /// Korrektur eines wiederkehrenden OCR-Fehlers in Nummern, z. B. Länge 14, „47000“ am Anfang → „4700D“
    /// (4700012345F831 → 4700D12345F831). Im Muster: $ = Ziffer, ? = Buchstabe, * = beliebiges Zeichen;
    /// in der Ersetzung behält $, ? oder * das Zeichen an dieser Stelle.
    /// </summary>
    public sealed class NumberFixRule
    {
        public bool Active { get; set; } = true;
        /// <summary>Länge der ganzen Nummer (Buchstaben und Ziffern ohne Trennzeichen); null = beliebig.</summary>
        public int? Length { get; set; }
        public string Wrong { get; set; } = "";
        public string Right { get; set; } = "";
        public FixPosition Position { get; set; } = FixPosition.Anfang;
        public string? Note { get; set; }

        [JsonIgnore]
        public bool IsValid => !string.IsNullOrEmpty(Wrong) && (Length is null || (Length > 0 && Wrong.Length <= Length));

        public NumberFixRule Clone() => (NumberFixRule)MemberwiseClone();
    }

    /// <summary>Fundstelle einer Korrektur in einer Textdatei (Index im Dateitext, Zeile 1-basiert).</summary>
    public sealed record NumberFixHit(string File, int Line, int Index, string Old, string New, string LineText, NumberFixRule Rule);

    public sealed record NumberFixResult(List<string> ChangedFiles, int Replacements, List<string> Errors);

    public static class NumberFixStore
    {
        private static readonly object Sync = new();
        private static List<NumberFixRule>? _rules;
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
        private static readonly Regex TokenRx = new(@"(?<![\p{L}\d])[\p{L}\d]+(?![\p{L}\d])", RegexOptions.Compiled);

        public static string FilePath => Path.Combine(Plugins.PluginManager.BaseDirectory, "txt_number_fixes.json");
        public static string BackupRoot => Path.Combine(Plugins.PluginManager.BaseDirectory, "TxtBackup");
        private static string LastMarker => Path.Combine(BackupRoot, "last.txt");

        /// <summary>TXT-Dateien wurden geändert (Korrektur oder Rückgängig) – Hauptprogramm und Plugins werten die Belege neu aus.</summary>
        public static event Action<IReadOnlyCollection<string>>? TextFilesChanged;

        public static void RaiseTextFilesChanged(IReadOnlyCollection<string> files)
        {
            if (files.Count > 0) TextFilesChanged?.Invoke(files);
        }

        public static List<NumberFixRule> Rules
        {
            get
            {
                lock (Sync)
                {
                    if (_rules != null) return _rules;
                    try
                    {
                        if (File.Exists(FilePath))
                            _rules = JsonSerializer.Deserialize<List<NumberFixRule>>(SecureStorage.ReadAllText(FilePath), JsonOptions);
                    }
                    catch { }
                    return _rules ??= new List<NumberFixRule>();
                }
            }
        }

        public static void Save(IEnumerable<NumberFixRule> rules)
        {
            var list = rules.Where(r => r != null && !string.IsNullOrEmpty(r.Wrong)).Select(r => r.Clone()).ToList();
            lock (Sync)
            {
                _rules = list;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                    SecureStorage.WriteAllText(FilePath, JsonSerializer.Serialize(list, JsonOptions));
                }
                catch { }
            }
        }

        private static bool CharMatches(char pattern, char c) => pattern switch
        {
            '$' => char.IsDigit(c),
            '?' => char.IsLetter(c),
            '*' => true,
            _ => char.ToUpperInvariant(pattern) == char.ToUpperInvariant(c)
        };

        private static bool MatchesAt(string token, int start, string wrong)
        {
            if (start < 0 || start + wrong.Length > token.Length) return false;
            for (int k = 0; k < wrong.Length; k++)
                if (!CharMatches(wrong[k], token[start + k])) return false;
            return true;
        }

        private static string Replacement(string segment, string right)
        {
            if (right.Length != segment.Length) return right.Replace("*", "").Replace("$", "").Replace("?", "");
            var sb = new StringBuilder(right.Length);
            for (int k = 0; k < right.Length; k++) sb.Append(right[k] is '*' or '$' or '?' ? segment[k] : right[k]);
            return sb.ToString();
        }

        /// <summary>Korrigierte Nummer oder null, wenn die Regel nicht passt bzw. nichts ändert.</summary>
        public static string? Fix(string token, NumberFixRule r)
        {
            if (!r.Active || !r.IsValid || string.IsNullOrEmpty(token)) return null;
            if (r.Length is int len && token.Length != len) return null;
            string? result = null;
            switch (r.Position)
            {
                case FixPosition.Anfang:
                    if (MatchesAt(token, 0, r.Wrong)) result = Replacement(token[..r.Wrong.Length], r.Right) + token[r.Wrong.Length..];
                    break;
                case FixPosition.Ende:
                    int s = token.Length - r.Wrong.Length;
                    if (MatchesAt(token, s, r.Wrong)) result = token[..s] + Replacement(token[s..], r.Right);
                    break;
                default:
                    var sb = new StringBuilder();
                    bool any = false;
                    for (int i = 0; i < token.Length;)
                    {
                        if (MatchesAt(token, i, r.Wrong))
                        {
                            sb.Append(Replacement(token.Substring(i, r.Wrong.Length), r.Right));
                            i += r.Wrong.Length;
                            any = true;
                        }
                        else sb.Append(token[i++]);
                    }
                    if (any) result = sb.ToString();
                    break;
            }
            return result == null || result == token ? null : result;
        }

        /// <summary>Alle Korrekturen in einem Text (erste passende Regel je Nummer).</summary>
        public static List<NumberFixHit> FindHits(string file, string text, IReadOnlyList<NumberFixRule> rules)
        {
            var hits = new List<NumberFixHit>();
            var active = rules.Where(r => r.Active && r.IsValid).ToList();
            if (active.Count == 0 || string.IsNullOrEmpty(text)) return hits;
            int line = 1, lineStart = 0, scanned = 0;
            foreach (Match m in TokenRx.Matches(text))
            {
                foreach (var r in active)
                {
                    if (Fix(m.Value, r) is not { } fixedToken) continue;
                    for (; scanned < m.Index; scanned++)
                        if (text[scanned] == '\n') { line++; lineStart = scanned + 1; }
                    int end = text.IndexOf('\n', m.Index);
                    var lineText = text[lineStart..(end < 0 ? text.Length : end)].TrimEnd('\r').Trim();
                    hits.Add(new NumberFixHit(file, line, m.Index, m.Value, fixedToken, lineText, r));
                    break;
                }
            }
            return hits;
        }

        /// <summary>
        /// Regel aus einem Beispiel ableiten (4700012345F831 → 4700D12345F831 ergibt Länge 14, „47000“ am Anfang → „4700D“).
        /// Der kürzere Teil bis zur Änderung bestimmt die Position (Anfang oder Ende).
        /// </summary>
        public static NumberFixRule? Derive(string? wrongExample, string? rightExample)
        {
            var w = (wrongExample ?? "").Trim();
            var r = (rightExample ?? "").Trim();
            if (w.Length == 0 || r.Length == 0 || string.Equals(w, r, StringComparison.OrdinalIgnoreCase)) return null;
            if (w.Length != r.Length)
                return new NumberFixRule { Length = w.Length, Wrong = w, Right = r, Position = FixPosition.Anfang, Note = $"{w} → {r}" };
            int first = 0, last = w.Length - 1;
            while (first < w.Length && char.ToUpperInvariant(w[first]) == char.ToUpperInvariant(r[first])) first++;
            while (last > first && char.ToUpperInvariant(w[last]) == char.ToUpperInvariant(r[last])) last--;
            bool atStart = last + 1 <= w.Length - first;
            return atStart
                ? new NumberFixRule { Length = w.Length, Wrong = w[..(last + 1)], Right = r[..(last + 1)], Position = FixPosition.Anfang, Note = $"{w} → {r}" }
                : new NumberFixRule { Length = w.Length, Wrong = w[first..], Right = r[first..], Position = FixPosition.Ende, Note = $"{w} → {r}" };
        }

        private static Encoding Ansi
        {
            get
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                return Encoding.GetEncoding(1252);
            }
        }

        /// <summary>Textdatei mit ihrer Kodierung lesen (BOM, sonst UTF-8 wenn gültig, sonst Windows-1252) – damit sie unverändert zurückgeschrieben werden kann.</summary>
        public static (string Text, Encoding Encoding) ReadText(string path)
        {
            var bytes = File.ReadAllBytes(path);
            Encoding enc;
            int skip = 0;
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) { enc = new UTF8Encoding(true); skip = 3; }
            else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) { enc = new UnicodeEncoding(false, true); skip = 2; }
            else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) { enc = new UnicodeEncoding(true, true); skip = 2; }
            else
            {
                try { new UTF8Encoding(false, true).GetString(bytes); enc = new UTF8Encoding(false); }
                catch (DecoderFallbackException) { enc = Ansi; }
            }
            return (enc.GetString(bytes, skip, bytes.Length - skip), enc);
        }

        private static void WriteText(string path, string text, Encoding enc) =>
            File.WriteAllBytes(path, enc.GetPreamble().Concat(enc.GetBytes(text)).ToArray());

        /// <summary>Korrekturen in die Dateien schreiben; die Originale werden vorher gesichert (rückgängig mit <see cref="UndoLast"/>).</summary>
        public static NumberFixResult Apply(IEnumerable<NumberFixHit> hits)
        {
            var changed = new List<string>();
            var errors = new List<string>();
            int count = 0;
            var dir = Path.Combine(BackupRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            var manifest = new List<string[]>();
            foreach (var g in hits.GroupBy(h => h.File, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var (text, enc) = ReadText(g.Key);
                    var sb = new StringBuilder(text);
                    int n = 0;
                    foreach (var h in g.OrderByDescending(h => h.Index))
                    {
                        // Datei seit der Vorschau geändert: diese Stelle auslassen
                        if (h.Index + h.Old.Length > text.Length || string.CompareOrdinal(text, h.Index, h.Old, 0, h.Old.Length) != 0) continue;
                        sb.Remove(h.Index, h.Old.Length).Insert(h.Index, h.New);
                        n++;
                    }
                    if (n == 0) continue;
                    Directory.CreateDirectory(dir);
                    var backup = Path.Combine(dir, $"{manifest.Count:0000}_{Path.GetFileName(g.Key)}");
                    File.Copy(g.Key, backup, true);
                    manifest.Add(new[] { g.Key, backup });
                    WriteText(g.Key, sb.ToString(), enc);
                    changed.Add(g.Key);
                    count += n;
                }
                catch (Exception ex) { errors.Add($"{Path.GetFileName(g.Key)}: {ex.Message}"); }
            }
            if (manifest.Count > 0)
            {
                try
                {
                    File.WriteAllText(Path.Combine(dir, "manifest.json"), JsonSerializer.Serialize(manifest));
                    File.WriteAllText(LastMarker, dir);
                }
                catch (Exception ex) { errors.Add("Sicherung: " + ex.Message); }
            }
            return new NumberFixResult(changed, count, errors);
        }

        /// <summary>Beschreibung der letzten rückgängig machbaren Korrektur oder null.</summary>
        public static string? LastChangeInfo()
        {
            try
            {
                if (!File.Exists(LastMarker)) return null;
                var dir = File.ReadAllText(LastMarker).Trim();
                var mf = Path.Combine(dir, "manifest.json");
                if (!File.Exists(mf)) return null;
                var n = JsonSerializer.Deserialize<List<string[]>>(File.ReadAllText(mf))?.Count ?? 0;
                return $"{Directory.GetCreationTime(dir):dd.MM.yyyy HH:mm}, {n} Datei(en)";
            }
            catch { return null; }
        }

        /// <summary>Letzte Korrektur rückgängig machen (Originale zurückkopieren).</summary>
        public static NumberFixResult UndoLast()
        {
            var changed = new List<string>();
            var errors = new List<string>();
            try
            {
                var dir = File.ReadAllText(LastMarker).Trim();
                var manifest = JsonSerializer.Deserialize<List<string[]>>(File.ReadAllText(Path.Combine(dir, "manifest.json"))) ?? new();
                foreach (var e in manifest)
                {
                    try { File.Copy(e[1], e[0], true); changed.Add(e[0]); }
                    catch (Exception ex) { errors.Add($"{Path.GetFileName(e[0])}: {ex.Message}"); }
                }
                if (errors.Count == 0) File.Delete(LastMarker);
            }
            catch (Exception ex) { errors.Add(ex.Message); }
            return new NumberFixResult(changed, changed.Count, errors);
        }
    }
}
