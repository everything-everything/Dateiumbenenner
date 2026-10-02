using System.IO;
using System.Windows.Media.Imaging;
using UglyToad.PdfPig.Writer;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Dateiumbenenner.Compression
{
    /// <summary>
    /// Rendert jede PDF-Seite (Windows.Data.Pdf) als Bild, kodiert sie als JPEG (WPF) und
    /// baut daraus eine neue PDF (PdfPig). Text ist danach nicht mehr markierbar.
    /// </summary>
    public class RasterizeCompressor : IPdfCompressor
    {
        public string Name => "Rasterize";

        private const int DefaultDpi = 200;

        public bool IsAvailable() => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 10240);

        public string Compress(string inputPath, PdfCompressionSettings settings)
        {
            var pdfBytes = Task.Run(() => RasterizeAsync(inputPath, settings)).GetAwaiter().GetResult();
            var tmp = inputPath + ".raster.tmp";
            File.WriteAllBytes(tmp, pdfBytes);
            File.Move(tmp, inputPath, overwrite: true);
            return inputPath;
        }

        private static async Task<byte[]> RasterizeAsync(string inputPath, PdfCompressionSettings settings)
        {
            int dpi = settings.ImageDpi ?? DefaultDpi;
            var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(inputPath));
            var doc = await PdfDocument.LoadFromFileAsync(file);
            var pages = new List<(byte[] Jpeg, double WidthPt, double HeightPt)>();

            for (uint i = 0; i < doc.PageCount; i++)
            {
                using var page = doc.GetPage(i);
                // page.Size ist in DIP (1/96 Zoll)
                double wDip = page.Size.Width, hDip = page.Size.Height;
                using var ras = new InMemoryRandomAccessStream();
                await page.RenderToStreamAsync(ras, new PdfPageRenderOptions
                {
                    DestinationWidth = (uint)Math.Max(1, Math.Round(wDip * dpi / 96.0)),
                    DestinationHeight = (uint)Math.Max(1, Math.Round(hDip * dpi / 96.0)),
                    BackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255)
                });

                ras.Seek(0);
                using var png = ras.AsStreamForRead();
                var frame = BitmapFrame.Create(png, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                pages.Add((EncodeJpeg(frame, settings.JpegQuality), wDip * 72.0 / 96.0, hDip * 72.0 / 96.0));
            }

            return BuildPdfFromJpegs(pages);
        }

        /// <summary>Kodiert ein gerendertes Seitenbild als JPEG (WPF-Encoder, keine externe Bibliothek).</summary>
        public static byte[] EncodeJpeg(BitmapSource image, int quality)
        {
            var encoder = new JpegBitmapEncoder { QualityLevel = Math.Clamp(quality, 1, 100) };
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
