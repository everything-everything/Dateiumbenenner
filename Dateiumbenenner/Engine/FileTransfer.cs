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

        /// <summary>Verschiebt/kopiert die Dateien. Die Datenbank wird beim Verschieben einmal am Ende für alle Dateien
        /// gemeinsam nachgeführt (eine Transaktion statt zwei Volltextindex-Durchläufen je Datei).</summary>
        public static (List<string> Done, List<string> Errors) Run(IReadOnlyList<string> paths, string target, bool move, IProgress<int>? progress = null)
        {
            var ok = new List<string>();
            var err = new List<string>();
            var moved = new List<(string, string)>();
            int i = 0;
            foreach (var src in paths)
            {
                try
                {
                    var dest = UniqueTarget(target, Path.GetFileName(src));
                    if (move) { File.Move(src, dest); moved.Add((src, dest)); }
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
            if (moved.Count > 0) DocumentStore.MoveMany(moved);
            return (ok, err);
        }

        /// <summary>XML-Belegdefinitionen in den Zielordner kopieren bzw. verschieben. Gemeinsam genutzte (Shared) werden
        /// immer nur kopiert. Vorhandene Ziele werden nie überschrieben (die Kennung steht in der Datei): gleiche Datei → übersprungen,
        /// abweichende → Konflikt.</summary>
        public static (int Moved, int Copied, int Same, List<string> Messages) TransferXml(IEnumerable<(string Path, bool Shared)> files, string target, bool move)
        {
            int moved = 0, copied = 0, same = 0;
            var msg = new List<string>();
            Directory.CreateDirectory(target);
            foreach (var (src, shared) in files)
            {
                var name = Path.GetFileName(src);
                try
                {
                    var dest = Path.Combine(target, name);
                    if (File.Exists(dest))
                    {
                        if (!SameContent(src, dest)) msg.Add($"{name}: im XML-Ziel existiert bereits eine abweichende Datei – nicht überschrieben.");
                        else if (move && !shared) { File.Delete(src); moved++; }
                        else same++;
                        continue;
                    }
                    if (move && !shared) { File.Move(src, dest); moved++; }
                    else { File.Copy(src, dest); copied++; if (move) msg.Add($"{name}: nur kopiert, passt noch zu weiteren Belegen der Liste."); }
                }
                catch (Exception ex)
                {
                    msg.Add($"{name}: {ex.Message}");
                }
            }
            return (moved, copied, same, msg);
        }

        private static bool SameContent(string a, string b)
        {
            var fa = new FileInfo(a);
            var fb = new FileInfo(b);
            if (fa.Length != fb.Length) return false;
            return File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));
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
