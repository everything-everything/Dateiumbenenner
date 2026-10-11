using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Dateiumbenenner.Engine
{
    /// <summary>
    /// Fügt durch OCR/PDF zerrissene Zeilen wieder zusammen:
    /// "Teststraße\n5" -> "Teststraße 5", "Lieferdatum\n.:\n14.05.2024" -> "Lieferdatum: 14.05.2024",
    /// "BIC\nDEUTDESS" -> "BIC: DEUTDESS", "73529\nSchwäbisch Gmünd" -> "73529 Schwäbisch Gmünd".
    /// </summary>
    public static class TextLineJoiner
    {
        private static readonly Regex StreetEnd = new(@"(stra(ß|ss)e|str\.?|weg|gasse|platz|allee|ring|damm|ufer|steig|pfad)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex HouseNo = new(@"^\d{1,4}\s?[a-zA-Z]?(\s?[-/]\s?\d{1,4}[a-zA-Z]?)?$", RegexOptions.Compiled);
        private static readonly Regex PunctOnly = new(@"^[.:;]+$", RegexOptions.Compiled);
        private static readonly Regex LabelColon = new(@"^[\p{L}][\p{L}\d .\-/]{1,40}:$", RegexOptions.Compiled);
        private static readonly Regex KnownLabel = new(@"^(BIC|SWIFT|IBAN|PLZ|Ort|Datum|Rechnungsdatum|Lieferdatum|Leistungsdatum|Belegdatum|Rechnungs-?nr\.?|Rechnungsnummer|Kunden-?nr\.?|Kundennummer|Auftrags-?nr\.?|Auftragsnummer|USt-?Id(Nr)?\.?|Steuer-?Nr\.?|Telefon|Tel\.?|Fax|E-?Mail)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex Value = new(@"^(\d{1,2}\.\d{1,2}\.\d{2,4}|[A-Z0-9][A-Z0-9 ./\-]{2,40})$", RegexOptions.Compiled);
        private static readonly Regex IbanStart = new(@"\bIBAN\s*:?\s*([A-Z]{2}\d{2}[A-Z0-9 ]{0,40})$", RegexOptions.IgnoreCase | RegexOptions.Compiled, System.TimeSpan.FromMilliseconds(50));
        private static readonly Regex IbanGroup = new(@"^[A-Z0-9]{1,4}$", RegexOptions.Compiled);
        private static readonly Regex Plz = new(@"^\d{5}$", RegexOptions.Compiled);
        private static readonly Regex Place = new(@"^\p{Lu}[\p{L} .\-()]{2,40}$", RegexOptions.Compiled);

        private static readonly Regex SingleWord = new(@"^\p{L}[\p{L}.,&\-]{0,30}$", RegexOptions.Compiled);
        private static readonly Regex LegalFormWord = new(@"^(GmbH|AG|KG|OHG|UG|GbR|eK|e\.K\.|SE|mbH|Co\.?)[,.]?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Senkrechter Text zerfällt in Einzelwort-Zeilen ("GmbH," / "Haus" / "DFH" / "Firma").
        /// Ab 3 solchen Zeilen zu einer Zeile verbinden; beginnt die Folge mit einer Rechtsform, ist sie rückwärts.
        /// Zahlen/Tabellenwerte sind keine Einzelwörter und bleiben unberührt.
        /// </summary>
        public static string Join(string? text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
            var lines = WordRunJoiner.Join(text.Replace("\r\n", "\n").Split('\n'));
            var result = new List<string>(lines.Length);
            foreach (var raw in lines)
            {
                var cur = raw.Trim();
                if (result.Count > 0 && cur.Length > 0)
                {
                    int p = result.Count - 1;
                    var prev = result[p].TrimEnd();
                    var prevT = prev.Trim();
                    // "IBAN DE64 5907" + "3717" + "0000" ... -> "IBAN DE64 5907 3717 0000 ..." (bis volle IBAN-Länge)
                    var iban = IbanGroup.IsMatch(cur) && prevT.Contains("IBAN", System.StringComparison.OrdinalIgnoreCase)
                        ? IbanStart.Match(prevT) : Match.Empty;
                    if (iban.Success)
                    {
                        var digits = Regex.Replace(iban.Groups[1].Value, @"\s", "");
                        int need = digits.StartsWith("DE", System.StringComparison.OrdinalIgnoreCase) ? 22 : 34;
                        if (digits.Length + cur.Length <= need) { result[p] = prev + " " + cur; continue; }
                    }
                    // "Lieferdatum" + ".:" -> "Lieferdatum:"
                    if (PunctOnly.IsMatch(cur) && prevT.Length > 0 && char.IsLetter(prevT[^1]))
                    { result[p] = prev + ":"; continue; }
                    // "Teststraße" + "5" -> "Teststraße 5"
                    if (HouseNo.IsMatch(cur) && StreetEnd.IsMatch(prevT))
                    { result[p] = prev + " " + cur; continue; }
                    // "Lieferdatum:" / "BIC" + Wert -> "Lieferdatum: 14.05.2024"
                    if (Value.IsMatch(cur) && (LabelColon.IsMatch(prevT) || KnownLabel.IsMatch(prevT)))
                    { result[p] = prev.TrimEnd(' ', '.', ':') + ": " + cur; continue; }
                    // "73529" + "Schwäbisch Gmünd" -> "73529 Schwäbisch Gmünd"
                    if (Plz.IsMatch(prevT) && Place.IsMatch(cur) && DocumentEngine.Plz.IsPlaceName(cur.Split(' ')[0]))
                    { result[p] = prev + " " + cur; continue; }
                }
                result.Add(raw.TrimEnd('\r'));
            }
            return string.Join("\n", result);
        }
    }
}
