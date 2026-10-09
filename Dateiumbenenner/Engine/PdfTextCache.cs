using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Dateiumbenenner
{
    /// <summary>
    /// Dauerhafter Zwischenspeicher für aus PDFs extrahierten Text (nur PDFs ohne gleichnamige TXT).
    /// Ablage in %LocalAppData%\Dateiumbenenner\TextCache, Gültigkeit über Pfad, Größe und Änderungsdatum.
    /// </summary>
    internal static class PdfTextCache
    {
        private const string Header = "DUCACHE1";

        private static readonly string CacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Dateiumbenenner", "TextCache");

        private static string? GetCacheFile(string pdfPath, out string stamp)
        {
            stamp = string.Empty;
            try
            {
                var fi = new FileInfo(pdfPath);
                if (!fi.Exists) return null;
                stamp = $"{fi.Length}|{fi.LastWriteTimeUtc.Ticks}";
                var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(pdfPath).ToUpperInvariant()));
                return Path.Combine(CacheDir, Convert.ToHexString(hash) + ".txt");
            }
            catch
            {
                return null;
            }
        }

        public static bool TryGet(string pdfPath, out string text, out int pageCount)
        {
            text = string.Empty;
            pageCount = 0;
            try
            {
                var file = GetCacheFile(pdfPath, out var stamp);
                if (file == null || !File.Exists(file)) return false;
                using var reader = new StreamReader(file, Encoding.UTF8);
                if (reader.ReadLine() != Header) return false;
                if (reader.ReadLine() != stamp) return false;
                if (!int.TryParse(reader.ReadLine(), out pageCount)) return false;
                text = reader.ReadToEnd();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void Store(string pdfPath, string text, int pageCount)
        {
            try
            {
                var file = GetCacheFile(pdfPath, out var stamp);
                if (file == null) return;
                Directory.CreateDirectory(CacheDir);
                var tmp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(tmp, $"{Header}\n{stamp}\n{pageCount}\n{text}", Encoding.UTF8);
                File.Move(tmp, file, true);
            }
            catch
            {
            }
        }
    }
}
