using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Dateiumbenenner.Engine
{
    /// <summary>Gelerntes Absender-Profil: eindeutige Merkmale + bestätigte Werte.</summary>
    public class SenderProfile
    {
        public string Company { get; set; } = "";
        public HashSet<string> Fingerprints { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public string? Street { get; set; }
        public string? Zip { get; set; }
        public string? City { get; set; }
        public int Confirmations { get; set; }
        public DateTime LastUsed { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// Speichert Absender-Profile und erkennt den Absender eines neuen Belegs über
    /// USt-ID, Steuernummer, IBAN, Telefon, Fax, E-Mail-Domain, Webseite und Straße+PLZ.
    /// </summary>
    public class SenderProfileStore
    {
        private readonly string _file;
        private readonly object _lock = new();
        private List<SenderProfile> _profiles;

        private static readonly Regex UstIdRx = new(@"\bDE\s?\d{3}\s?\d{3}\s?\d{3}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex TaxNoRx = new(@"\b\d{2,3}/\d{3}/\d{4,5}\b", RegexOptions.Compiled);
        private static readonly Regex IbanRx = new(@"\bDE\d{2}(?:\s?\d{4}){4}\s?\d{2}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex PhoneRx = new(@"(?:Tel|Telefon|Fon|Fax|Telefax)\.?\s*:?\s*((?:\+49|0)[\d\s/\-()]{6,20}\d)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex MailRx = new(@"@([a-z0-9\-]+\.[a-z]{2,})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex WebRx = new(@"\bwww\.([a-z0-9\-]+\.[a-z]{2,})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public SenderProfileStore(string file)
        {
            _file = file;
            try { _profiles = File.Exists(file) ? JsonSerializer.Deserialize<List<SenderProfile>>(File.ReadAllText(file)) ?? new() : new(); }
            catch { _profiles = new(); }
        }

        public IReadOnlyList<SenderProfile> Profiles { get { lock (_lock) return _profiles.ToList(); } }

        /// <summary>Eindeutige Merkmale aus dem Text ziehen (normalisiert).</summary>
        public static HashSet<string> ExtractFingerprints(string? text, string? street = null, string? zip = null)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(text)) return set;
            static string Digits(string s) => Regex.Replace(s, @"[^\dA-Z]", "", RegexOptions.IgnoreCase).ToUpperInvariant();
            foreach (Match m in UstIdRx.Matches(text)) set.Add("UST:" + Digits(m.Value));
            foreach (Match m in TaxNoRx.Matches(text)) set.Add("STNR:" + Digits(m.Value));
            foreach (Match m in IbanRx.Matches(text)) set.Add("IBAN:" + Digits(m.Value));
            foreach (Match m in PhoneRx.Matches(text)) { var d = Regex.Replace(m.Groups[1].Value, @"\D", ""); if (d.Length >= 7) set.Add("TEL:" + d); }
            foreach (Match m in MailRx.Matches(text)) { var dom = m.Groups[1].Value.ToLowerInvariant(); if (!IsGenericMail(dom)) set.Add("MAIL:" + dom); }
            foreach (Match m in WebRx.Matches(text)) set.Add("WEB:" + m.Groups[1].Value.ToLowerInvariant());
            if (!string.IsNullOrEmpty(street) && !string.IsNullOrEmpty(zip))
                set.Add("ADR:" + zip + "|" + Regex.Replace(street.ToUpperInvariant(), @"[^A-Z0-9]", ""));
            return set;
        }

        private static bool IsGenericMail(string d) =>
            d is "gmail.com" or "gmx.de" or "web.de" or "t-online.de" or "outlook.com" or "hotmail.com" or "yahoo.de" or "freenet.de";

        /// <summary>Absender anhand der Merkmale finden. Gewichtung: USt/IBAN/StNr stark, Tel/Web/Mail mittel, Adresse schwach.</summary>
        public (SenderProfile? Profile, double Score) Identify(HashSet<string> fingerprints)
        {
            if (fingerprints.Count == 0) return (null, 0);
            SenderProfile? best = null; double bestScore = 0;
            lock (_lock)
            {
                foreach (var p in _profiles)
                {
                    double score = 0;
                    foreach (var f in fingerprints)
                    {
                        if (!p.Fingerprints.Contains(f)) continue;
                        score += f.StartsWith("UST:") || f.StartsWith("IBAN:") || f.StartsWith("STNR:") ? 3
                               : f.StartsWith("TEL:") || f.StartsWith("WEB:") || f.StartsWith("MAIL:") ? 2 : 1;
                    }
                    if (score > bestScore) { bestScore = score; best = p; }
                }
            }
            // Mindestens ein starkes oder zwei mittlere Merkmale
            return bestScore >= 3 ? (best, bestScore) : (null, bestScore);
        }

        /// <summary>Bestätigung durch den Benutzer: Profil anlegen bzw. ergänzen.</summary>
        public void Learn(string company, HashSet<string> fingerprints, string? street, string? zip, string? city)
        {
            company = company.Trim();
            if (company.Length < 2 || fingerprints.Count == 0) return;
            lock (_lock)
            {
                // Merkmale, die bisher einer ANDEREN Firma zugeordnet waren, dort entfernen (Korrektur)
                foreach (var other in _profiles.Where(p => !p.Company.Equals(company, StringComparison.OrdinalIgnoreCase)))
                    other.Fingerprints.ExceptWith(fingerprints);
                _profiles.RemoveAll(p => p.Fingerprints.Count == 0);

                var prof = _profiles.FirstOrDefault(p => p.Company.Equals(company, StringComparison.OrdinalIgnoreCase));
                if (prof == null) { prof = new SenderProfile { Company = company }; _profiles.Add(prof); }
                prof.Fingerprints.UnionWith(fingerprints);
                if (!string.IsNullOrWhiteSpace(street)) prof.Street = street;
                if (!string.IsNullOrWhiteSpace(zip)) prof.Zip = zip;
                if (!string.IsNullOrWhiteSpace(city)) prof.City = city;
                prof.Confirmations++;
                prof.LastUsed = DateTime.Now;
                Save();
            }
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
                File.WriteAllText(_file, JsonSerializer.Serialize(_profiles, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }
    }
}
