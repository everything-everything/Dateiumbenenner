using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Dateiumbenenner
{
    /// <summary>
    /// Profile (Standard, 1, 2, ...): jedes Profil hat einen eigenen Datenordner mit allen Einstellungen,
    /// gelernten Einträgen und eigener Dokument-Datenbank. Gilt für Hauptprogramm und Plugins gemeinsam.
    /// </summary>
    public static class ProfileManager
    {
        public const string DefaultProfile = "Standard";

        /// <summary>Globaler Datenordner neben der EXE (enthält Profil-Auswahl und Plugin-Aktivierung).</summary>
        public static string RootDirectory => Path.Combine(AppContext.BaseDirectory, "Daten");
        private static string ProfilesDirectory => Path.Combine(RootDirectory, "Profile");
        private static string ActiveFile => Path.Combine(RootDirectory, "profil.txt");

        private static string? _active;
        public static string ActiveProfile
        {
            get
            {
                if (_active != null) return _active;
                try { _active = File.Exists(ActiveFile) ? File.ReadAllText(ActiveFile).Trim() : null; } catch { }
                if (string.IsNullOrWhiteSpace(_active) || !Exists(_active)) _active = DefaultProfile;
                return _active;
            }
        }

        /// <summary>Datenordner des aktiven Profils. Standard = bisheriger Ordner "Daten" (abwärtskompatibel).</summary>
        public static string ActiveDirectory => GetDirectory(ActiveProfile);

        public static string GetDirectory(string profile) =>
            profile.Equals(DefaultProfile, StringComparison.OrdinalIgnoreCase) ? RootDirectory : Path.Combine(ProfilesDirectory, profile);

        public static bool Exists(string profile) =>
            profile.Equals(DefaultProfile, StringComparison.OrdinalIgnoreCase) || Directory.Exists(GetDirectory(profile));

        public static IReadOnlyList<string> GetProfiles()
        {
            var list = new List<string> { DefaultProfile };
            if (Directory.Exists(ProfilesDirectory))
                list.AddRange(Directory.GetDirectories(ProfilesDirectory).Select(Path.GetFileName).OfType<string>()
                    .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase));
            return list;
        }

        /// <summary>Legt ein Profil an; optional werden die Einstellungen (ohne Dokument-Datenbank) aus einem Profil kopiert.</summary>
        public static void Create(string name, string? copyFrom)
        {
            if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new ArgumentException("Ungültiger Profilname.");
            if (Exists(name)) throw new InvalidOperationException($"Profil „{name}“ existiert bereits.");
            var target = GetDirectory(name);
            Directory.CreateDirectory(target);
            if (copyFrom == null) return;
            var src = GetDirectory(copyFrom);
            if (!Directory.Exists(src)) return;
            foreach (var f in Directory.GetFiles(src))
            {
                var fn = Path.GetFileName(f);
                if (fn.StartsWith(DocumentStore.FileName, StringComparison.OrdinalIgnoreCase) || fn.Equals("profil.txt", StringComparison.OrdinalIgnoreCase)) continue;
                File.Copy(f, Path.Combine(target, fn), true);
            }
        }

        public static void SetActive(string profile)
        {
            Directory.CreateDirectory(RootDirectory);
            File.WriteAllText(ActiveFile, profile);
            _active = profile;
        }
    }
}
