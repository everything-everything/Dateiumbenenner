using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Dateiumbenenner.Engine
{
    public enum ValueKind { Nummer, Betrag }

    /// <summary>
    /// Fundstelle eines manuell gewählten Wertes (je Firma + Belegart): Wörter davor/dahinter,
    /// der wievielte gleichartige Wert im Text (von vorne/hinten) und die ungefähre Lage im Beleg.
    /// </summary>
    public class ValueLocation
    {
        /// <summary>Aufbau der Nummer ($ = Ziffer, ? = Buchstabe), leer bei Beträgen.</summary>
        public string Mask { get; set; } = "";
        /// <summary>Wörter vor dem Wert, nächstes zuerst.</summary>
        public List<string> Before { get; set; } = new();
        /// <summary>Wörter hinter dem Wert, nächstes zuerst.</summary>
        public List<string> After { get; set; } = new();
        /// <summary>1 = erster gleichartiger Wert im Text.</summary>
        public int Occurrence { get; set; }
        /// <summary>1 = letzter gleichartiger Wert im Text.</summary>
        public int FromEnd { get; set; }
        public int Total { get; set; }
        /// <summary>Lage im Text 0 (Anfang) .. 1 (Ende).</summary>
        public double LinePos { get; set; }
        public string? Example { get; set; }
        public int Count { get; set; } = 1;
        public DateTime Updated { get; set; }
    }

    public static class ValueLocationStore
    {
        private const int MaxPerKey = 6;
        private static Dictionary<string, List<ValueLocation>>? _map;
        private static readonly object Sync = new();
        private static readonly CultureInfo De = new("de-DE");
        private static readonly Regex WordRx = new(@"\p{L}{2,}", RegexOptions.Compiled);
        private static readonly Regex AmountRx = new(@"(?<![\d,.])(\d{1,3}(?:\.\d{3})+,\d{2}|\d+,\d{2})(?![\d,])", RegexOptions.Compiled);
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

        public static string FilePath => Path.Combine(Plugins.PluginManager.BaseDirectory, "value_locations.json");

        private static Dictionary<string, List<ValueLocation>> Map
        {
            get
            {
                if (_map != null) return _map;
                try
                {
                    if (File.Exists(FilePath))
                        _map = JsonSerializer.Deserialize<Dictionary<string, List<ValueLocation>>>(SecureStorage.ReadAllText(FilePath), JsonOptions);
                }
                catch { }
                return _map ??= new Dictionary<string, List<ValueLocation>>();
            }
        }

        private static string? Key(ValueKind kind, string? company, string? docType)
        {
            var dt = DocTypeStore.Canonical(docType) ?? docType;
            return string.IsNullOrWhiteSpace(company) || string.IsNullOrWhiteSpace(dt)
                ? null : kind + "|" + company.Trim().ToLowerInvariant() + "|" + dt.Trim().ToLowerInvariant();
        }

        public static List<ValueLocation> Get(ValueKind kind, string? company, string? docType)
        {
            var k = Key(kind, company, docType);
            if (k == null) return new();
            lock (Sync) return Map.TryGetValue(k, out var l) ? l.ToList() : new();
        }

        public static bool Has(ValueKind kind, string? company, string? docType) => Get(kind, company, docType).Count > 0;

        /// <summary>Gelernte Fundstellen für Firma + Belegart verwerfen.</summary>
        public static void Clear(ValueKind kind, string? company, string? docType)
        {
            var k = Key(kind, company, docType);
            if (k == null) return;
            lock (Sync) { if (Map.Remove(k)) Persist(); }
        }

        private static void Persist()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                SecureStorage.WriteAllText(FilePath, JsonSerializer.Serialize(Map, JsonOptions));
            }
            catch { }
        }

        private static string Norm(ValueKind kind, string s)
            => kind == ValueKind.Betrag
                ? (decimal.TryParse(s.Trim(), NumberStyles.Number, De, out var d) ? d.ToString("0.00", CultureInfo.InvariantCulture) : s.Replace(".", "").Trim())
                : Regex.Replace(s, @"\s+", "").ToUpperInvariant();

        private sealed record Occ(string Value, int Index, List<string> Before, List<string> After, int Occurrence, int FromEnd, int Total, double LinePos, string Mask);

        /// <summary>Alle gleichartigen Werte im Text mit Umfeld (Nummer: gleicher Aufbau wie mask; Betrag: alle Beträge).</summary>
        private static List<Occ> Occurrences(ValueKind kind, string text, string? mask)
        {
            Regex rx;
            if (kind == ValueKind.Betrag) rx = AmountRx;
            else
            {
                if (string.IsNullOrEmpty(mask)) return new();
                try { rx = new Regex(@"(?<![\p{L}\d])(" + NumberRuleStore.MaskToRegex(mask) + @")(?![\p{L}\d])", RegexOptions.IgnoreCase); }
                catch { return new(); }
            }
            var matches = rx.Matches(text).Cast<Match>().ToList();
            var lineStarts = new List<int> { 0 };
            for (int i = 0; i < text.Length; i++) if (text[i] == '\n') lineStarts.Add(i + 1);
            var result = new List<Occ>();
            for (int n = 0; n < matches.Count; n++)
            {
                var m = matches[n];
                var g = m.Groups[1];
                int line = lineStarts.BinarySearch(g.Index);
                if (line < 0) line = ~line - 1;
                int ls = lineStarts[line];
                int le = line + 1 < lineStarts.Count ? lineStarts[line + 1] - 1 : text.Length;
                var head = text.Substring(ls, g.Index - ls);
                var tail = text.Substring(g.Index + g.Length, Math.Max(0, le - g.Index - g.Length));
                var before = WordRx.Matches(head).Cast<Match>().Select(w => w.Value.ToLowerInvariant()).Reverse().Take(3).ToList();
                if (before.Count == 0)
                {
                    // Wert steht am Zeilenanfang: Begriff in der vorherigen nicht leeren Zeile
                    for (int p = line - 1; p >= 0 && p >= line - 2 && before.Count == 0; p--)
                    {
                        int ps = lineStarts[p], pe = lineStarts[p + 1] - 1;
                        before = WordRx.Matches(text.Substring(ps, Math.Max(0, pe - ps))).Cast<Match>().Select(w => w.Value.ToLowerInvariant()).Reverse().Take(3).ToList();
                    }
                }
                var after = WordRx.Matches(tail).Cast<Match>().Select(w => w.Value.ToLowerInvariant()).Take(2).ToList();
                double pos = lineStarts.Count <= 1 ? 0 : (double)line / (lineStarts.Count - 1);
                result.Add(new Occ(g.Value.Trim(), g.Index, before, after, n + 1, matches.Count - n, matches.Count, pos,
                    kind == ValueKind.Nummer ? NumberRuleStore.MaskFromValue(g.Value) : ""));
            }
            return result;
        }

        /// <summary>Manuelle Auswahl lernen: Fundstelle des Wertes im Text merken (neueste zuerst).</summary>
        public static void Learn(ValueKind kind, string? company, string? docType, string? text, string? value)
        {
            var k = Key(kind, company, docType);
            if (k == null || string.IsNullOrEmpty(text) || string.IsNullOrWhiteSpace(value)) return;
            var mask = kind == ValueKind.Nummer ? NumberRuleStore.MaskFromValue(value) : null;
            var target = Norm(kind, value);
            var hits = Occurrences(kind, text, mask).Where(o => Norm(kind, o.Value) == target).ToList();
            if (hits.Count == 0) return;
            // Bei mehrfach vorkommendem Wert die Stelle mit Begriff davor bevorzugen
            var h = hits.FirstOrDefault(o => o.Before.Count > 0) ?? hits[0];
            var loc = new ValueLocation
            {
                Mask = h.Mask, Before = h.Before, After = h.After, Occurrence = h.Occurrence, FromEnd = h.FromEnd,
                Total = h.Total, LinePos = Math.Round(h.LinePos, 3), Example = value.Trim(), Updated = DateTime.Now
            };
            lock (Sync)
            {
                if (!Map.TryGetValue(k, out var list)) Map[k] = list = new List<ValueLocation>();
                var same = list.FirstOrDefault(x => x.Mask == loc.Mask && x.Occurrence == loc.Occurrence
                    && x.Before.FirstOrDefault() == loc.Before.FirstOrDefault() && x.After.FirstOrDefault() == loc.After.FirstOrDefault());
                if (same != null) { list.Remove(same); loc.Count = same.Count + 1; }
                list.Insert(0, loc);
                if (list.Count > MaxPerKey) list.RemoveRange(MaxPerKey, list.Count - MaxPerKey);
                Persist();
            }
        }

        private static double Similarity(ValueLocation s, Occ o)
        {
            double b = 0;
            for (int i = 0; i < Math.Min(3, Math.Min(s.Before.Count, o.Before.Count)); i++)
            {
                if (s.Before[i] != o.Before[i]) break;
                b += i == 0 ? 20 : i == 1 ? 6 : 3;
            }
            if (b == 0 && s.Before.Count > 0 && o.Before.Contains(s.Before[0])) b += 6;
            if (s.After.Count > 0 && o.After.Count > 0 && s.After[0] == o.After[0])
                b += 8 + (s.After.Count > 1 && o.After.Count > 1 && s.After[1] == o.After[1] ? 3 : 0);
            if (s.Total > 1 && s.Occurrence == o.Occurrence) b += 6;
            if (s.Total > 1 && s.FromEnd == o.FromEnd) b += 4;
            b += 4 * Math.Max(0, 1 - Math.Abs(s.LinePos - o.LinePos) * 5);
            if (s.Mask.Length > 0 && s.Mask != o.Mask) b *= 0.5;
            return b;
        }

        /// <summary>
        /// Bewertet die Werte im Text nach den gelernten Fundstellen und hebt sie im Feld an
        /// (neueste Auswahl zählt am stärksten). Gibt den Wert mit der besten Fundstelle zurück.
        /// </summary>
        public static string? Apply(Field field, ValueKind kind, string? company, string? docType, string? text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var samples = Get(kind, company, docType);
            if (samples.Count == 0) return null;
            var best = new Dictionary<string, (string Value, double Bonus)>();
            var cache = new Dictionary<string, List<Occ>>();
            for (int i = 0; i < samples.Count; i++)
            {
                var s = samples[i];
                double weight = (i == 0 ? 1.0 : 0.5) * (1 + Math.Min(s.Count, 5) * 0.05);
                var cacheKey = kind == ValueKind.Betrag ? "" : s.Mask;
                if (!cache.TryGetValue(cacheKey, out var occs)) cache[cacheKey] = occs = Occurrences(kind, text, s.Mask);
                foreach (var o in occs)
                {
                    var bonus = Similarity(s, o) * weight;
                    var n = Norm(kind, o.Value);
                    if (!best.TryGetValue(n, out var cur) || cur.Bonus < bonus) best[n] = (o.Value, bonus);
                }
            }
            string? top = null; double topBonus = 0;
            foreach (var (n, (value, bonus)) in best)
            {
                if (bonus < 8) continue;
                var existing = field.Candidates.FirstOrDefault(c => Norm(kind, c.Value) == n);
                double baseScore = existing?.Score ?? (field.Candidates.Count > 0 ? field.Candidates.Max(c => c.Score) : 0);
                field.Add(existing?.Value ?? value, Math.Max(0, baseScore) + bonus, "Fundstelle");
                if (bonus > topBonus) { topBonus = bonus; top = existing?.Value ?? value; }
            }
            return top;
        }
    }
}
