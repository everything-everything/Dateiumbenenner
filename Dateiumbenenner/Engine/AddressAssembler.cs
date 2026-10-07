using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Dateiumbenenner.Engine
{
    /// <summary>
    /// Setzt Adressen aus der Wortfolge (Lesereihenfolge) zusammen und ÜBERSPRINGT fremde Wörter dazwischen:
    /// "Weißensteiner Straße [989439] 87 73525 Schwäbisch Gmünd" -> "Weißensteiner Straße 87 73525 Schwäbisch Gmünd".
    /// Reihenfolge: Straße -> Hausnummer (1-4 Ziffern) -> PLZ (5 Ziffern) -> Ort (1-2 Wörter).
    /// Erste Adresse ohne "Herrn/Frau/Firma" davor = Absender (Markierung "@ABS ").
    /// </summary>
    public static class AddressAssembler
    {
        private const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.Compiled;
        private static readonly Regex StreetEnd = new(@"(straße|strasse|str\.?|weg|platz|allee|gasse|ring|damm|chaussee|ufer|markt)$", Opt);
        private static readonly Regex StreetWithNumber = new(@"^(.+?(?:straße|strasse|str\.?))(\d{1,4}[a-zA-Z]?)$", Opt);
        private static readonly Regex BareSuffix = new(@"^(Straße|Strasse|Str\.?|Weg|Platz|Allee|Gasse|Ring|Damm|Chaussee|Ufer|Markt)$", Opt);
        private static readonly Regex Preposition = new(@"^(Am|An|Im|In|Zum|Zur|Auf|Hinter|Unter|Alte|Alter|Neue|Neuer)$", Opt);
        private static readonly Regex HouseNo = new(@"^\d{1,4}[a-zA-Z]?$", RegexOptions.Compiled);
        private static readonly Regex Zip = new(@"^(?:D-?)?(\d{5})$", RegexOptions.Compiled);
        private static readonly Regex CityWord = new(@"^[A-ZÄÖÜ][a-zäöüß\-\.]+$", RegexOptions.Compiled);
        private static readonly Regex UpperStart = new(@"^[A-ZÄÖÜ]", RegexOptions.Compiled);
        private static readonly Regex Stop = new(@"^(Herrn?|Frau|Firma|Rechnung\w*|Kunden\w*|Auftrag\w*|Beleg\w*|Datum|Seite|Tel\w*|Fax|Bei|USt\w*|Steuer\w*|IBAN|BIC|Leistung\w*)", Opt);
        private static readonly Regex Recipient = new(@"^(Herrn?|Frau|Firma)$", Opt);

        public static List<string> Assemble(IEnumerable<string> segments)
        {
            var w = segments
                .SelectMany(s => s.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .Select(t => t.Trim('-', '–', '—', '·', '•', '|', ',', ';', ':', '‚', '„', '"'))
                .Where(t => t.Length > 0)
                .ToList();

            var result = new List<string>();
            bool senderFound = false;

            for (int i = 0; i < w.Count; i++)
            {
                string? name = null, no = null;
                var swn = StreetWithNumber.Match(w[i]);
                if (swn.Success)
                {
                    name = swn.Groups[1].Value; no = swn.Groups[2].Value;          // "Oberbettringerstr.75"
                }
                else if (BareSuffix.IsMatch(w[i]))
                {
                    // "Weißensteiner Straße", "Am Alten Weg"
                    if (i == 0 || !UpperStart.IsMatch(w[i - 1]) || Stop.IsMatch(w[i - 1]) || w[i - 1].Any(char.IsDigit)) continue;
                    name = w[i - 1] + " " + w[i];
                    if (i >= 2 && Preposition.IsMatch(w[i - 2])) name = w[i - 2] + " " + name;
                }
                else if (StreetEnd.IsMatch(w[i]) && w[i].Length > 5 && UpperStart.IsMatch(w[i]) && !w[i].Any(char.IsDigit))
                {
                    name = w[i];                                                       // "Hauptstraße", "Bahnhofstr."
                }
                else continue;

                // Hausnummer: erste 1-4-stellige Zahl, fremde Wörter/Nummern (z. B. Rechnungsnummer) überspringen
                int k = i + 1;
                if (no == null)
                    for (int j = i + 1; j < Math.Min(w.Count, i + 7); j++)
                    {
                        if (Zip.IsMatch(w[j])) break;
                        if (HouseNo.IsMatch(w[j])) { no = w[j]; k = j + 1; break; }
                    }

                // PLZ
                string? zip = null;
                for (int j = k; j < Math.Min(w.Count, k + 6); j++)
                {
                    var zm = Zip.Match(w[j]);
                    if (zm.Success) { zip = zm.Groups[1].Value; k = j + 1; break; }
                }
                if (zip == null) continue;

                // Ort (1-2 Wörter)
                var city = new List<string>();
                for (int j = k; j < w.Count && city.Count < 2; j++)
                {
                    if (Stop.IsMatch(w[j]) || !CityWord.IsMatch(w[j])) break;
                    city.Add(w[j]);
                }
                if (city.Count == 0) continue;

                bool recipient = false;
                for (int j = Math.Max(0, i - 8); j < i; j++)
                    if (Recipient.IsMatch(w[j])) recipient = true;

                var text = string.Join(" ", new[] { name, no, zip, string.Join(" ", city) }.Where(s => !string.IsNullOrEmpty(s)));
                if (recipient) result.Add("Herrn " + text);
                else if (!senderFound) { result.Add("@ABS " + text); senderFound = true; }
                else result.Add(text);
            }
            return result;
        }
    }
}
