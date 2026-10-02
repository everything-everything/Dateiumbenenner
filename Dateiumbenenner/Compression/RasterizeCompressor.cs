using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using UglyToad.PdfPig.Writer;

namespace Dateiumbenenner.Compression
{
    public class RasterizeCompressor : IPdfCompressor
    {
        public string Name => "Rasterize";

        // Zum Rendern von PDF-Seiten in Bilder ist ein PDF-Renderer nötig (noch nicht eingebunden).
        // Bis dahin deaktiviert, damit keine Inhalte durch leere Seiten ersetzt werden.
        public bool IsAvailable() => false;

        public string Compress(string inputPath, PdfCompressionSettings settings)
            => throw new System.NotSupportedException("Rasterisierung benötigt einen PDF-Renderer.");

        /// <summary>Kodiert ein gerendertes Seitenbild als JPEG (WPF-Encoder, keine externe Bibliothek).</summary>
        public static byte[] EncodeJpeg(BitmapSource image, int quality)
        {
            var encoder = new JpegBitmapEncoder { QualityLevel = System.Math.Clamp(quality, 1, 100) };
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }

        /// <summary>Erstellt aus JPEG-Seitenbildern eine PDF (PdfPig-Builder). Seitengröße in Punkt (1/72 Zoll).</summary>
        public static byte[] BuildPdfFromJpegs(IEnumerable<(byte[] Jpeg, double WidthPt, double HeightPt)> pages)
        {
            var builder = new PdfDocumentBuilder();
            foreach (var (jpeg, w, h) in pages)
            {
                var page = builder.AddPage(w, h);
                page.AddJpeg(jpeg, new UglyToad.PdfPig.Core.PdfRectangle(0, 0, w, h));
            }
            return builder.Build();
        }
    }
}
