using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Dateiumbenenner.Engine
{
    /// <summary>
    /// Spaltenbasierte Adresserkennung: Der formatierte OCR-Text ist ein Zeichenraster,
    /// die Zeichenposition entspricht der X-Position auf der Seite. Textstücke mit ähnlicher
    /// linker Kante bilden eine Spalte; innerhalb einer Spalte wird von oben nach unten gelesen.
    /// So stören Belegdaten in einer rechten Spalte (Rechnung-Nr., Datum) die Adresse nicht mehr,
    /// und Straße / PLZ / Ort in getrennten Zeilen werden zusammengesetzt.
    /// </summary>
    public static class ColumnAddressReader
    {
        private const int Tolerance = 4;      // Zeichen Abweichung der linken Kante
        private const int MaxGap = 4;         // max. Leerzeilen innerhalb eines Adressblocks

        private static readonly Regex Segment = new(@"\S+(?: {1,2}\S+)*", RegexOptions.Compiled);
        private static readonly Regex LeadingBullet = new(@"^[\s\-–•·:*>|]+", RegexOptions.Compiled);
        private static readonly Regex SharpS = new(@"(stra)(?:B|8|R|13|\u00DF)(e)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex InnerB = new(@"(?<=[a-zäöü])B(?=[a-zäöü])", RegexOptions.Compiled);
        private static readonly Regex DocLabel = new(
            @"^(?:Rechnung|Rechnungs|Lieferschein|Kunden|Auftrag|Auftrags|Beleg|Datum|Seite|Tel|Telefon|Fax|USt|Steuer|IBAN|BIC)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private sealed class Piece
        {
            public int Y, X;
            public string Text = string.Empty;
        }

        private sealed class Column
        {
            public int X;
            public List<Piece> Pieces { get; } = new();
        }

        /// <summary>Liefert Adresskandidaten je Spalte (Reihenfolge: Spalten von oben links).</summary>
        public static List<string> Read(string text)
        {
            var raw = text.Replace("\t", "    ").Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            var columns = new List<Column>();

            for (int y = 0; y < raw.Length; y++)
            {
                foreach (Match m in Segment.Matches(raw[y]))
                {
                    var t = Clean(m.Value);
                    if (t.Length == 0) continue;
                    var piece = new Piece { Y = y, X = m.Index, Text = t };
                    var col = columns
                        .Where(c => Math.Abs(c.X - m.Index) <= Tolerance)
                        .OrderBy(c => Math.Abs(c.X - m.Index))
                        .FirstOrDefault();
                    if (col == null) { col = new Column { X = m.Index }; columns.Add(col); }
                    col.Pieces.Add(piece);
                }
            }

            var result = new List<string>();
            foreach (var col in columns.OrderBy(c => c.Pieces[0].Y).ThenBy(c => c.X))
            {
                // Spalte in Blöcke teilen, wenn große vertikale Lücken auftreten
                var block = new List<string>();
                int lastY = -100;
                foreach (var p in col.Pieces)
                {
                    if (p.Y - lastY > MaxGap && block.Count > 0) { Flush(block, result); block.Clear(); }
                    lastY = p.Y;
                    // Belegbezeichnungen innerhalb der Spalte überspringen (z. B. "Rechnung-Nr.:")
                    if (DocLabel.IsMatch(p.Text)) continue;
                    block.Add(p.Text);
                }
                Flush(block, result);
            }
            return result;
        }

        private static void Flush(List<string> block, List<string> result)
        {
            if (block.Count == 0) return;
            foreach (var a in AddressAssembler.Assemble(block))
                if (!result.Contains(a)) result.Add(a);
        }

        private static string Clean(string s)
        {
            var l = LeadingBullet.Replace(s, "").Trim();
            l = SharpS.Replace(l, x => x.Groups[1].Value + "ße");
            l = InnerB.Replace(l, "ß");
            l = Regex.Replace(l, @"(\d{5})\s*[—–-]\s+", "$1 ");
            return l;
        }
    }
}
