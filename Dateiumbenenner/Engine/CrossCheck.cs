using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.Recognizers.Text;
using Microsoft.Recognizers.Text.DateTime;
using Microsoft.Recognizers.Text.NumberWithUnit;
using StringComparer = System.StringComparer;

namespace Dateiumbenenner.Engine
{
    /// <summary>Ergebnis der Gegenprüfung eines Belegtextes mit Microsoft Recognizers-Text.</summary>
    public sealed class CrossCheckAnalysis
    {
        /// <summary>Gefundene Kalenderdaten (dd.MM.yyyy), Datum hinter "…datum" zuerst.</summary>
        public List<string> Dates { get; set; } = new();
        /// <summary>Gefundene Euro-Beträge.</summary>
        public List<decimal> Amounts { get; set; } = new();
        public DateTime Stamp { get; set; }
    }

    public sealed record CrossCheckVerdict(string Flag, string Info, string? SuggestedDate, bool Deviation);

    /// <summary>
    /// Zweitmeinung für Datum und Betrag (Microsoft Recognizers-Text, MIT). Überschreibt nie etwas –
    /// Abweichungen werden nur markiert und als zusätzlicher Vorschlag angeboten.
    /// </summary>
    public static class CrossCheck
    {
        private const int MaxChars = 30_000, MaxCache = 5_000;
        private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");
        private static readonly object _lock = new();
        private static Dictionary<string, CrossCheckAnalysis>? _cache;
        private static bool _dirty;

        public static string CachePath => Path.Combine(Plugins.PluginManager.BaseDirectory, "crosscheck_cache.json");

        public static string KeyOf(string text)
        {
            var h = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(text));
            return Convert.ToHexString(h, 0, 16);
        }

        private static Dictionary<string, CrossCheckAnalysis> Cache
        {
            get
            {
                if (_cache != null) return _cache;
                try
                {
                    if (File.Exists(CachePath))
                        _cache = JsonSerializer.Deserialize<Dictionary<string, CrossCheckAnalysis>>(SecureStorage.ReadAllText(CachePath));
                }
                catch { }
                return _cache ??= new Dictionary<string, CrossCheckAnalysis>(StringComparer.Ordinal);
            }
        }

        public static bool TryGet(string key, out CrossCheckAnalysis analysis)
        {
            lock (_lock) return Cache.TryGetValue(key, out analysis!);
        }

        /// <summary>Text auswerten (langsam – im Hintergrund aufrufen) und zwischenspeichern.</summary>
        public static CrossCheckAnalysis Analyze(string key, string text)
        {
            var t = text.Length > MaxChars ? text[..MaxChars] : text;
            var a = new CrossCheckAnalysis { Stamp = DateTime.Now };
            var preferred = new List<string>();
            foreach (var r in DateTimeRecognizer.RecognizeDateTime(t, Culture.German))
            {
                if (r.TypeName is not ("datetimeV2.date" or "datetimeV2.datetime")) continue;
                if (r.Resolution == null || !r.Resolution.TryGetValue("values", out var v) || v is not System.Collections.IEnumerable values) continue;
                foreach (var item in values)
                {
                    if (item is not IDictionary<string, string> d || !d.TryGetValue("value", out var val) || val.Length < 10) continue;
                    if (d.TryGetValue("timex", out var timex) && timex.Contains('X')) continue;   // ohne Jahr
                    if (!DateTime.TryParseExact(val[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)) continue;
                    if (dt.Year < 1990 || dt.Year > DateTime.Now.Year + 5) continue;
                    var s = dt.ToString("dd.MM.yyyy", De);
                    if (a.Dates.Contains(s)) continue;
                    int from = Math.Max(0, r.Start - 40);
                    if (t.Substring(from, r.Start - from).Contains("datum", StringComparison.OrdinalIgnoreCase)) preferred.Add(s);
                    a.Dates.Add(s);
                    break;
                }
            }
            a.Dates = preferred.Concat(a.Dates.Except(preferred)).ToList();
            foreach (var r in NumberWithUnitRecognizer.RecognizeCurrency(t, Culture.German))
            {
                if (r.Resolution == null || !r.Resolution.TryGetValue("value", out var v) || v == null) continue;
                if (r.Resolution.TryGetValue("isoCurrency", out var iso) && iso is string cur && cur.Length > 0 && cur != "EUR") continue;
                var s = v.ToString()!;
                if (!decimal.TryParse(s, NumberStyles.Number, De, out var amount) && !decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out amount)) continue;
                amount = Math.Round(amount, 2);
                if (amount > 0 && !a.Amounts.Contains(amount)) a.Amounts.Add(amount);
            }
            lock (_lock)
            {
                var c = Cache;
                c[key] = a;
                if (c.Count > MaxCache)
                    foreach (var old in c.OrderBy(kv => kv.Value.Stamp).Take(c.Count - MaxCache).Select(kv => kv.Key).ToList()) c.Remove(old);
                _dirty = true;
            }
            return a;
        }

        public static void Save()
        {
            string json;
            lock (_lock)
            {
                if (!_dirty || _cache == null) return;
                json = JsonSerializer.Serialize(_cache);
                _dirty = false;
            }
            try { SecureStorage.WriteAllText(CachePath, json); } catch { }
        }

        public static void Forget()
        {
            lock (_lock) { _cache = null; _dirty = false; }
        }

        private static decimal? ParseAmount(string? s) =>
            !string.IsNullOrWhiteSpace(s) && decimal.TryParse(s.Replace("€", "").Replace("EUR", "").Trim(), NumberStyles.Number, De, out var d) ? Math.Round(d, 2) : null;

        /// <summary>Eigenes Ergebnis (Datum dd.MM.yyyy, Betrag) mit der Gegenprüfung vergleichen.</summary>
        public static CrossCheckVerdict Compare(CrossCheckAnalysis a, string? ourDate, string? ourAmount)
        {
            var info = new List<string>();
            bool deviation = false, confirmed = false;
            string? suggested = null;

            if (a.Dates.Count > 0)
            {
                var top = string.Join(", ", a.Dates.Take(4));
                if (string.IsNullOrWhiteSpace(ourDate)) { deviation = true; suggested = a.Dates[0]; info.Add($"Datum: keins erkannt – Gegenprüfung findet {top}"); }
                else if (a.Dates.Contains(ourDate.Trim())) { confirmed = true; info.Add($"Datum {ourDate} bestätigt"); }
                else { deviation = true; suggested = a.Dates[0]; info.Add($"Datum {ourDate} – Gegenprüfung findet {top}"); }
            }
            else info.Add("Datum: Gegenprüfung findet keins (Schreibweisen wie 23Okt25 kennt sie nicht)");

            var our = ParseAmount(ourAmount);
            if (a.Amounts.Count > 0)
            {
                var max = a.Amounts.Max();
                if (our == null) { deviation = true; info.Add($"Betrag: keiner erkannt – Gegenprüfung: höchster Betrag {max.ToString("N2", De)} €"); }
                else if (a.Amounts.Contains(our.Value)) { confirmed = true; info.Add($"Betrag {our.Value.ToString("N2", De)} € bestätigt"); }
                else { deviation = true; info.Add($"Betrag {our.Value.ToString("N2", De)} € – Gegenprüfung findet {string.Join(", ", a.Amounts.OrderByDescending(x => x).Take(4).Select(x => x.ToString("N2", De)))} €"); }
            }
            else info.Add("Betrag: Gegenprüfung findet keinen");

            var flag = deviation ? "⚠" : confirmed ? "✓" : "–";
            return new CrossCheckVerdict(flag, "Gegenprüfung (Recognizers-Text, Test):\n" + string.Join("\n", info), suggested, deviation);
        }
    }
}
