namespace Dateiumbenenner.Compression
{
    public interface IPdfCompressor
    {
        string Name { get; }
        bool IsAvailable();
        // Returns output path (may replace input)
        string Compress(string inputPath, PdfCompressionSettings settings);
    }
}