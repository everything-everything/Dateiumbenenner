using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Dateiumbenenner.Engine
{
    /// <summary>
    /// Senkrechter Text zerfällt in Einzelwort-Zeilen ("GmbH," / "Haus" / "DFH" / "Firma").
    /// Nur rund um eine Rechtsform zusammenfügen; beginnt die Folge mit der Rechtsform, ist sie rückwärts.
    /// </summary>
    public static class WordRunJoiner
    {
        private static readonly Regex SingleWord = new(@"^\p{L}[\p{L}.,&\-]{0,30}$", RegexOptions.Compiled);
        private static readonly Regex LegalFormWord = new(@"^(GmbH|AG|KG|OHG|UG|GbR|eK|e\.K\.|SE|mbH|Co\.?)[,.]?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex StreetEnd = new(@"(stra(ß|ss)e|str\.?|weg|gasse|platz|allee|ring|damm|ufer|steig|pfad)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static bool Word(string l)
        {
            var t = l.Trim();
            return SingleWord.IsMatch(t) && !StreetEnd.IsMatch(t) && !LegalFormWord.IsMatch(t);
        }

        public static string[] Join(string[] lines)
        {
            var result = new List<string>(lines.Length);
            int i = 0;
            while (i < lines.Length)
            {
                if (LegalFormWord.IsMatch(lines[i].Trim()))
                {
                    // Rückwärts: "GmbH," / "Haus" / "DFH" / "Firma" (max. 6 Wörter danach)
                    int j = i + 1;
                    while (j < lines.Length && j - i <= 6 && Word(lines[j])) j++;
                    if (j - i >= 3)
                    {
                        var words = lines[i..j].Select(l => l.Trim()).ToList();
                        words.Reverse();
                        result.Add(string.Join(" ", words));
                        i = j;
                        continue;
                    }
                    // Vorwärts: "DFH" / "Haus" / "GmbH" (max. 4 Wörter davor)
                    int s = result.Count;
                    while (s > 0 && result.Count - s < 4 && Word(result[s - 1])) s--;
                    if (result.Count - s >= 2)
                    {
                        var joined = string.Join(" ", result.Skip(s).Select(l => l.Trim()).Append(lines[i].Trim()));
                        result.RemoveRange(s, result.Count - s);
                        result.Add(joined);
                        i++;
                        continue;
                    }
                }
                result.Add(lines[i++]);
            }
            return result.ToArray();
        }
    }
}
