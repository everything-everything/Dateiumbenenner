using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Dateiumbenenner.Engine
{
    /// <summary>Kurzeingabe für Datumsfelder: 0102 → 01.02.(aktuelles Jahr), 010226 → 01.02.2026, 01022026 → 01.02.2026,
    /// auch mit Trennzeichen (1.2 / 1.2.26 / 1-2-2026).</summary>
    public static class DateInput
    {
        private static readonly Regex Digits = new(@"^\d{4}$|^\d{6}$|^\d{8}$", RegexOptions.Compiled);
        private static readonly Regex Separated = new(@"^(?<d>\d{1,2})[./\- ](?<m>\d{1,2})(?:[./\- ]?|[./\- ](?<y>\d{2}|\d{4}))$", RegexOptions.Compiled);

        /// <summary>Vervollständigtes Datum TT.MM.JJJJ oder null, wenn die Eingabe keine Kurzform eines gültigen Datums ist.</summary>
        public static string? Expand(string? input, DateTime? today = null)
        {
            var s = input?.Trim();
            if (string.IsNullOrEmpty(s)) return null;
            int year = (today ?? DateTime.Today).Year;
            string d, m, y;
            if (Digits.IsMatch(s))
            {
                d = s[..2]; m = s.Substring(2, 2); y = s.Length > 4 ? s[4..] : "";
            }
            else if (Separated.Match(s) is { Success: true } x)
            {
                d = x.Groups["d"].Value; m = x.Groups["m"].Value; y = x.Groups["y"].Value;
            }
            else return null;
            int yy = y.Length switch { 0 => year, 2 => 2000 + int.Parse(y), _ => int.Parse(y) };
            if (!DateTime.TryParseExact($"{int.Parse(d):00}.{int.Parse(m):00}.{yy:0000}", "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                return null;
            var result = date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
            return result == s ? null : result;
        }
    }
}
