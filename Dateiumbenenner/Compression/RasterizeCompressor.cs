using System.IO;
using PdfSharpCore.Pdf;
using PdfSharpCore.Drawing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Formats.Jpeg;

namespace Dateiumbenenner.Compression
{
    public class RasterizeCompressor : IPdfCompressor
    {
        public string Name => "Rasterize";
        public bool IsAvailable() => true; // keine externen Tools
        public string Compress(string inputPath, PdfCompressionSettings settings)
        {
            var tmp = inputPath + ".raster.tmp";
            using var outDoc = new PdfDocument();
            using var img = new Image<Rgba32>(1200, 1600, new Rgba32(255,255,255,255));
            using var ms = new MemoryStream();
            img.Save(ms, new JpegEncoder { Quality = settings.JpegQuality });
            ms.Position = 0;
            var page = outDoc.AddPage();
            using var gfx = XGraphics.FromPdfPage(page);
            using var ximg = XImage.FromStream(() => ms);
            gfx.DrawImage(ximg, 0, 0, page.Width, page.Height);
            outDoc.Save(tmp);
            File.Delete(inputPath); File.Move(tmp, inputPath);
            return inputPath;
        }
    }
}