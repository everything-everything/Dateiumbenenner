using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Writer;
using System.ComponentModel;
using System.Diagnostics;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;
using Dateiumbenenner.Compression;

namespace Dateiumbenenner
{
    public class PdfMergeItem : INotifyPropertyChanged
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName => Path.GetFileName(FilePath);
        public int PageCount { get; set; }
        public int Order { get; set; }
        public long FileSizeBytes { get; set; }
        public double? Dpi { get; set; }
        public string SizeDisplay => FileSizeBytes <= 0 ? string.Empty : (DocumentMetadata.UseMegaBytes ? (FileSizeBytes / (1024d * 1024d)).ToString("F2") + " MB" : (FileSizeBytes / 1024d).ToString("F2") + " KB");
        public string DpiDisplay => Dpi.HasValue ? Dpi.Value.ToString("F0") : string.Empty;
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        public void RefreshSizeDisplay() => OnPropertyChanged(nameof(SizeDisplay));
    }

    public static class PdfMergeManager
    {
        public static async Task<string> MergePdfsAsync(List<PdfMergeItem> items, string outputPath, PdfCompressionSettings? compressionSettings = null)
        {
            return await Task.Run(() =>
            {
                if (items == null || items.Count == 0)
                    throw new ArgumentException("Keine PDF-Dateien zum Zusammenführen ausgewählt.");

                var sortedItems = items.OrderBy(x => x.Order).ToList();
                using var resultDocument = new PdfDocumentBuilder();
                foreach (var item in sortedItems)
                {
                    if (!File.Exists(item.FilePath)) continue;
                    try
                    {
                        using var sourceDoc = PdfPigDocument.Open(item.FilePath);
                        foreach (var page in sourceDoc.GetPages())
                        {
                            resultDocument.AddPage(sourceDoc, page.Number);
                        }
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException($"Fehler beim Verarbeiten von '{item.FileName}': {ex.Message}", ex);
                    }
                }
                File.WriteAllBytes(outputPath, resultDocument.Build());

                if (compressionSettings?.Enabled == true)
                {
                    var before = new FileInfo(outputPath).Length;
                    var compressor = CompressionSelector.Select(compressionSettings);
                    var finalPath = compressor.Compress(outputPath, compressionSettings);
                    var after = new FileInfo(finalPath).Length;
                    Debug.WriteLine($"Kompression via {compressor.Name}: vorher={before}, nachher={after}, Änderung={(before - after)} bytes");
                    outputPath = finalPath;
                }

                return outputPath;
            });
        }

        public static async Task<int> GetPageCountAsync(string filePath)
        {
            return await Task.Run(() =>
            {
                try { using var doc = PdfPigDocument.Open(filePath); return doc.NumberOfPages; } catch { return 0; }
            });
        }
    }
}
