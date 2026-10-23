using System;
using System.Text.RegularExpressions;

namespace Dateiumbenenner.Engine
{
    /// <summary>
    /// Blockweise Vorauswahl: Ein Muster läuft einmal über den gesamten Text statt einzeln über jede Zeile.
    /// Die Zeilen werden mit einem Steuerzeichen verbunden, das keines der verwendeten Muster treffen kann
    /// (für \b, \s*, Lookarounds wirkt es wie ein Zeilenende). Dadurch werden genau die Zeilen mit Treffern markiert;
    /// die eigentliche Auswertung läuft danach unverändert zeilenweise in der ursprünglichen Reihenfolge.
    /// Nur für Muster ohne ".", "^", "$" und ohne negierte Zeichenklassen verwenden.
    /// </summary>
    internal sealed class LineBlock
    {
        public const char Separator = '\u0001';

        public string[] Lines { get; }
        public string Text { get; }
        private readonly int[] _starts;

        public LineBlock(string[] lines)
        {
            Lines = lines;
            _starts = new int[lines.Length];
            int pos = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                _starts[i] = pos;
                pos += lines[i].Length + 1;
            }
            Text = string.Join(Separator, lines);
        }

        private int LineOf(int pos)
        {
            int idx = Array.BinarySearch(_starts, pos);
            return idx >= 0 ? idx : ~idx - 1;
        }

        /// <summary>Setzt flags[zeile * stride + offset] für jede Zeile mit mindestens einem Treffer.</summary>
        public bool[] Mark(Regex rx, bool[]? flags = null, int stride = 1, int offset = 0)
        {
            flags ??= new bool[Lines.Length * stride];
            if (Lines.Length == 0) return flags;
            int start = 0;
            while (start <= Text.Length)
            {
                var m = rx.Match(Text, start);
                if (!m.Success) break;
                int line = LineOf(m.Index);
                flags[line * stride + offset] = true;
                if (line + 1 >= Lines.Length) break;
                start = _starts[line + 1];
            }
            return flags;
        }

        /// <summary>Markiert alle Zeilen (Rückfall für Begriffe, die das Trennzeichen selbst enthalten).</summary>
        public void MarkAll(bool[] flags, int stride = 1, int offset = 0)
        {
            for (int i = 0; i < Lines.Length; i++) flags[i * stride + offset] = true;
        }
    }
}
