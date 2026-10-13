using System;
using System.Collections.Generic;
using System.IO;
using Dateiumbenenner;

namespace Dateiumbenenner.Engine
{
    /// <summary>Dateien samt gleichnamiger .txt/.ini/.xml verschieben oder kopieren (ohne Überschreiben).</summary>
    public static class FileTransfer
    {
        private static readonly string[] SideExtensions = { ".txt", ".ini", ".xml" };

        public static (List<string> Done, List<string> Errors) Run(IReadOnlyList<string> paths, string target, bool move, IProgress<int>? progress = null)
        {
            var ok = new List<string>();
            var err = new List<string>();
            int i = 0;
            foreach (var src in paths)
            {
                try
                {
                    var dest = UniqueTarget(target, Path.GetFileName(src));
                    if (move) { File.Move(src, dest); DocumentStore.Move(src, dest); }
                    else File.Copy(src, dest);
                    foreach (var ext in SideExtensions)
                    {
                        var side = Path.ChangeExtension(src, ext);
                        if (side.Equals(src, StringComparison.OrdinalIgnoreCase) || !File.Exists(side)) continue;
                        var sideDest = Path.ChangeExtension(dest, ext);
                        if (File.Exists(sideDest)) continue;
                        if (move) File.Move(side, sideDest); else File.Copy(side, sideDest);
                    }
                    ok.Add(src);
                }
                catch (Exception ex)
                {
                    err.Add($"{Path.GetFileName(src)}: {ex.Message}");
                }
                if (++i % 50 == 0 || i == paths.Count) progress?.Report(i);
            }
            return (ok, err);
        }

        public static string UniqueTarget(string dir, string name)
        {
            var dest = Path.Combine(dir, name);
            if (!File.Exists(dest)) return dest;
            var stem = Path.GetFileNameWithoutExtension(name);
            var ext = Path.GetExtension(name);
            for (int n = 2; ; n++)
            {
                dest = Path.Combine(dir, $"{stem}_{n}{ext}");
                if (!File.Exists(dest)) return dest;
            }
        }
    }
}
