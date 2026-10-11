using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Dateiumbenenner
{
    /// <summary>
    /// Dauerhafter Zwischenspeicher für aus PDFs extrahierten Text (nur PDFs ohne gleichnamige TXT).
    /// Ablage in %LocalAppData%\Dateiumbenenner\TextCache, Gültigkeit über Pfad, Größe, Änderungsdatum und SHA256 über Anfang/Ende des Inhalts.
    /// </summary>
    internal static class PdfTextCache
    {
        private const string Header = "DUCACHE3";

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
                stamp = $"{fi.Length}|{fi.LastWriteTimeUtc.Ticks}|{PartialHash(pdfPath, fi.Length)}";
                var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(pdfPath).ToUpperInvariant()));
                return Path.Combine(CacheDir, Convert.ToHexString(hash) + ".txt");
            }
            catch
            {
                return null;
            }
        }

        private const int Chunk = 64 * 1024;

        /// <summary>SHA256 über Anfang und Ende der Datei (je 64 KB) – schnell, erkennt neu geschriebene PDFs.</summary>
        private static string PartialHash(string path, long length)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buf = new byte[Chunk];
            int n = fs.ReadAtLeast(buf, (int)Math.Min(Chunk, length), false);
            sha.AppendData(buf, 0, n);
            if (length > Chunk)
            {
                fs.Seek(Math.Max(Chunk, length - Chunk), SeekOrigin.Begin);
                n = fs.ReadAtLeast(buf, Chunk, false);
                sha.AppendData(buf, 0, n);
            }
            return Convert.ToHexString(sha.GetHashAndReset());
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
