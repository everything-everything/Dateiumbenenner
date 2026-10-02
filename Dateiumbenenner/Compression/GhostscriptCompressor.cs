using System;
using System.Diagnostics;
using System.IO;

namespace Dateiumbenenner.Compression
{
    public class GhostscriptCompressor : IPdfCompressor
    {
        public string Name => "Ghostscript";
        public bool IsAvailable()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "where",
                    Arguments = "gswin64c.exe",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                var output = p!.StandardOutput.ReadToEnd();
                p.WaitForExit();
                if (!string.IsNullOrEmpty(output)) return true;
            }
            catch { }
            return false;
        }

        public string Compress(string inputPath, PdfCompressionSettings settings)
        {
            var gsExe = FindGhostscript();
            if (string.IsNullOrEmpty(gsExe)) return inputPath;
            int dpi = settings.ImageDpi.HasValue ? Math.Clamp(settings.ImageDpi.Value, 72, 600) : 300;
            int jpegQ = Math.Clamp(settings.JpegQuality, 10, 100);
            var tempOut = inputPath + ".gs.tmp.pdf";
            if (File.Exists(tempOut)) File.Delete(tempOut);
            var args = string.Join(" ", new[]
            {
                "-sDEVICE=pdfwrite","-dCompatibilityLevel=1.5","-dNOPAUSE","-dBATCH","-dSAFER",
                "-dPDFSETTINGS=/screen","-dDetectDuplicateImages=true","-dCompressFonts=true","-dSubsetFonts=true","-dEmbedAllFonts=true",
                "-dDownsampleColorImages=true",
                $"-dColorImageResolution={dpi}","-dColorImageDownsampleType=/Average","-dColorImageDownsampleThreshold=1.0",
                $"-dColorImageFilter=/DCTEncode",$"-dJPEGQ={jpegQ}",
                "-dDownsampleGrayImages=true",
                $"-dGrayImageResolution={dpi}","-dGrayImageDownsampleType=/Average","-dGrayImageDownsampleThreshold=1.0",
                $"-dGrayImageFilter=/DCTEncode",
                "-dDownsampleMonoImages=true",
                $"-dMonoImageResolution={dpi}","-dMonoImageDownsampleType=/Subsample","-dMonoImageDownsampleThreshold=1.0",
                $"-sOutputFile=\"{tempOut}\"",$"\"{inputPath}\""
            });
            var psi = new ProcessStartInfo { FileName = gsExe, Arguments = args, UseShellExecute = false, CreateNoWindow = true };
            using var proc = Process.Start(psi); proc!.WaitForExit();
            if (proc.ExitCode != 0 || !File.Exists(tempOut)) return inputPath;
            File.Delete(inputPath); File.Move(tempOut, inputPath);
            return inputPath;
        }

        private static string? FindGhostscript()
        {
            string[] candidates = { "gswin64c.exe", "gswin32c.exe", "gs.exe" };
            foreach (var c in candidates)
            {
                try
                {
                    var psi = new ProcessStartInfo { FileName = "where", Arguments = c, UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
                    using var p = Process.Start(psi);
                    var output = p!.StandardOutput.ReadToEnd(); p.WaitForExit();
                    var path = output.Split(new[] { '\r','\n' }, StringSplitOptions.RemoveEmptyEntries)[0];
                    if (!string.IsNullOrEmpty(path) && File.Exists(path)) return path;
                }
                catch { }
            }
            return null;
        }
    }
}