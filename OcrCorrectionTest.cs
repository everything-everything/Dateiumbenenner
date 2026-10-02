using System;
using System.IO;
using Dateiumbenenner;

namespace OcrCorrectionTest
{
    /// <summary>
    /// Einfaches Testprogramm für die OCR-Korrektur mit Hunspell
    /// Kann als Konsolenanwendung kompiliert werden
    /// </summary>
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("====================================================");
            Console.WriteLine("  OCR-Korrektur Test mit Hunspell");
            Console.WriteLine("====================================================");
            Console.WriteLine();

            // Test 1: Leerzeichen in Wörtern
            TestCorrection("Rechnu ng", "Rechnung");
            TestCorrection("Widma nn", "Widmann");
            TestCorrection("Gu tschrift", "Gutschrift");
            TestCorrection("Liefe rschein", "Lieferschein");

            // Test 2: Mehrere Fehler
            TestCorrection("Rechnu ng vom 12. Juni 2024", "Rechnung vom 12. Juni 2024");

            // Test 3: Komplexer Text
            var complexText = @"
Rechnu ng
Widma nn GmbH
Datum: 12. Juni 2024
Rechnungs nummer: 123456
Be trag: 1.234,56 Euro
";
            Console.WriteLine();
            Console.WriteLine("Test: Komplexer Text");
            Console.WriteLine("--------------------------------------------------");
            Console.WriteLine("Original:");
            Console.WriteLine(complexText);
            Console.WriteLine();
            Console.WriteLine("Korrigiert:");
            var corrected = OcrTextCorrector.Fix(complexText);
            Console.WriteLine(corrected);

            Console.WriteLine();
            Console.WriteLine("====================================================");
            Console.WriteLine("Test abgeschlossen. Drücken Sie eine Taste...");
            Console.ReadKey();
        }

        static void TestCorrection(string input, string expected)
        {
            var result = OcrTextCorrector.Fix(input);
            var success = result == expected;
            
            Console.Write($"Test: '{input}' -> '{result}' ");
            if (success)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("?");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"? (erwartet: '{expected}')");
            }
            Console.ResetColor();
        }
    }
}
