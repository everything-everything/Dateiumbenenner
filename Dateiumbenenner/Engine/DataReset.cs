using System;
using System.IO;
using System.Linq;

namespace Dateiumbenenner.Engine
{
    /// <summary>
    /// Löscht Dokument-Datenbank und gelernte Daten des aktiven Profils.
    /// Einstellungen (eigene Firma, Ausschlüsse, eigene PLZ, PLZ-Verzeichnis, Plugin-Einstellungen, Schalter) bleiben erhalten.
    /// </summary>
    public static class DataReset
    {
        private static string FlagFile => Path.Combine(ProfileManager.ActiveDirectory, "clear_on_exit.flag");

        private static readonly string[] LearnedPrefixes =
        {
            DocumentStore.FileName,
            "known_companies.json",
            "sender_profiles.json",
            "sender_profiles_ambiguous.json",
            "number_patterns.json",
            "doc_templates.json",
            "address_debug.txt",
            "firma_umstellen.log",
            "company_directory_",
            "crosscheck_cache.json",
            OcrVocabulary.FileName,
        };

        public static bool ClearOnExit
        {
            get => File.Exists(FlagFile);
            set
            {
                try
                {
                    if (value) { Directory.CreateDirectory(ProfileManager.ActiveDirectory); File.WriteAllText(FlagFile, ""); }
                    else if (File.Exists(FlagFile)) File.Delete(FlagFile);
                }
                catch { }
            }
        }

        /// <summary>Löscht die Daten und gibt die Anzahl nicht löschbarer Dateien zurück.</summary>
        public static int Clear()
        {
            SenderProfileStore.DiscardPendingWrites();
            OcrVocabulary.Forget();
            CrossCheck.Forget();
            CompanyDirectory.ResetLocal();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            var dir = ProfileManager.ActiveDirectory;
            if (!Directory.Exists(dir)) return 0;
            int failed = 0;
            foreach (var f in Directory.GetFiles(dir))
            {
                var name = Path.GetFileName(f);
                if (!LearnedPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase))) continue;
                try { File.Delete(f); } catch { failed++; }
            }
            var debug = Path.Combine(dir, "debug");
            try { if (Directory.Exists(debug)) Directory.Delete(debug, true); } catch { failed++; }
            return failed;
        }
    }
}
