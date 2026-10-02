using System.IO;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Writer;

namespace Dateiumbenenner.Compression
{
    public class RewriteCompressor : IPdfCompressor
    {
        public string Name => "Rewrite";
        public bool IsAvailable() => true;
        public string Compress(string inputPath, PdfCompressionSettings settings)
        {
            var tmp = inputPath + ".rewrite.tmp";
            using var src = PdfDocument.Open(inputPath);
            using var builder = new PdfDocumentBuilder();
            foreach (var page in src.GetPages()) builder.AddPage(src, page.Number);
            File.WriteAllBytes(tmp, builder.Build());
            File.Delete(inputPath); File.Move(tmp, inputPath);
            return inputPath;
        }
    }
}