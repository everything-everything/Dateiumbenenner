using System.Collections.Generic;
using System.Linq;

namespace Dateiumbenenner.Compression
{
    public static class CompressionSelector
    {
        private static readonly List<IPdfCompressor> _all = new()
        {
            new GhostscriptCompressor(),
            new RewriteCompressor(),
            new RasterizeCompressor()
        };

        public static IPdfCompressor Select(PdfCompressionSettings settings)
        {
            // Erzeuge Prioritäten basierend auf Mode
            var mode = settings.Mode.ToString();
            IEnumerable<IPdfCompressor> ordered;
            if (mode == "Rasterize")
                ordered = _all.OrderByDescending(c => c.Name == "Rasterize");
            else if (mode == "Ghostscript")
                ordered = _all.Where(c => c.Name != "Rasterize").OrderByDescending(c => c.Name == "Ghostscript");
            else
                ordered = _all.Where(c => c.Name == "Rewrite");
            return ordered.FirstOrDefault(c => c.IsAvailable()) ?? new RewriteCompressor();
        }
    }
}