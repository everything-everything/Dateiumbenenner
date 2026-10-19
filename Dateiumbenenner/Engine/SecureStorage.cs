using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Dateiumbenenner.Engine
{
    /// <summary>
    /// Optionaler Passwortschutz für alle Profile. Ein zufälliger Hauptschlüssel verschlüsselt
    /// Dateien (AES-256-GCM) und die Dokument-Datenbank (SQLCipher). Der Hauptschlüssel ist
    /// sowohl mit dem Passwort als auch mit einem Wiederherstellungsschlüssel umhüllt (Daten\security.json).
    /// </summary>
    public static class SecureStorage
    {
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("DUENC1");
        private const int Iterations = 600_000;
        private static byte[]? _key;

        public static string ConfigFile => Path.Combine(ProfileManager.RootDirectory, "security.json");

        /// <summary>True, wenn ein Passwort eingerichtet ist.</summary>
        public static bool IsEnabled => File.Exists(ConfigFile);

        /// <summary>True, wenn der Schlüssel entsperrt ist (oder kein Schutz eingerichtet ist).</summary>
        public static bool IsUnlocked => !IsEnabled || _key != null;

        /// <summary>Schlüssel für SQLCipher als Hex-Literal, oder null bei unverschlüsselter Datenbank.</summary>
        public static string? DatabaseKey => _key == null ? null : "x'" + Convert.ToHexString(_key) + "'";

        private class Config
        {
            public string PwSalt { get; set; } = "";
            public string PwWrapped { get; set; } = "";
            public string RcSalt { get; set; } = "";
            public string RcWrapped { get; set; } = "";
        }

        private static Config? LoadConfig()
        {
            try { return File.Exists(ConfigFile) ? JsonSerializer.Deserialize<Config>(File.ReadAllText(ConfigFile)) : null; } catch { return null; }
        }

        private static void SaveConfig(Config c)
        {
            Directory.CreateDirectory(ProfileManager.RootDirectory);
            File.WriteAllText(ConfigFile, JsonSerializer.Serialize(c, new JsonSerializerOptions { WriteIndented = true }));
        }

        private static byte[] Derive(string secret, byte[] salt) =>
            Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(secret), salt, Iterations, HashAlgorithmName.SHA256, 32);

        private static string Wrap(byte[] key, string secret, out string salt)
        {
            var s = RandomNumberGenerator.GetBytes(16);
            salt = Convert.ToBase64String(s);
            return Convert.ToBase64String(Encrypt(key, Derive(secret, s)));
        }

        private static byte[]? Unwrap(string wrapped, string salt, string secret)
        {
            try { return Decrypt(Convert.FromBase64String(wrapped), Derive(secret, Convert.FromBase64String(salt))); }
            catch { return null; }
        }

        private static string NormalizeRecovery(string code) => new string(code.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

        private static string NewRecoveryCode()
        {
            const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var chars = Enumerable.Range(0, 24).Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]).ToArray();
            return string.Join("-", Enumerable.Range(0, 6).Select(i => new string(chars, i * 4, 4)));
        }

        /// <summary>Entsperrt mit Passwort. False bei falschem Passwort.</summary>
        public static bool Unlock(string password)
        {
            var c = LoadConfig();
            if (c == null) return false;
            var k = Unwrap(c.PwWrapped, c.PwSalt, password);
            if (k == null) return false;
            _key = k;
            return true;
        }

        /// <summary>Entsperrt mit Wiederherstellungsschlüssel und setzt ein neues Passwort.</summary>
        public static bool Recover(string recoveryCode, string newPassword)
        {
            var c = LoadConfig();
            if (c == null) return false;
            var k = Unwrap(c.RcWrapped, c.RcSalt, NormalizeRecovery(recoveryCode));
            if (k == null) return false;
            _key = k;
            c.PwWrapped = Wrap(k, newPassword, out var s);
            c.PwSalt = s;
            SaveConfig(c);
            return true;
        }

        /// <summary>Richtet den Schutz ein, verschlüsselt alle vorhandenen Daten und liefert den Wiederherstellungsschlüssel.</summary>
        public static string Enable(string password)
        {
            if (IsEnabled) throw new InvalidOperationException("Passwortschutz ist bereits eingerichtet.");
            var key = RandomNumberGenerator.GetBytes(32);
            var recovery = NewRecoveryCode();
            var c = new Config();
            c.PwWrapped = Wrap(key, password, out var ps); c.PwSalt = ps;
            c.RcWrapped = Wrap(key, NormalizeRecovery(recovery), out var rs); c.RcSalt = rs;
            ConvertAll(null, key);
            SaveConfig(c);
            _key = key;
            return recovery;
        }

        public static bool ChangePassword(string oldPassword, string newPassword)
        {
            var c = LoadConfig();
            if (c == null) return false;
            var k = Unwrap(c.PwWrapped, c.PwSalt, oldPassword);
            if (k == null) return false;
            c.PwWrapped = Wrap(k, newPassword, out var s); c.PwSalt = s;
            SaveConfig(c);
            _key = k;
            return true;
        }

        /// <summary>Hebt den Schutz auf und entschlüsselt alle Daten.</summary>
        public static bool Disable(string password)
        {
            var c = LoadConfig();
            if (c == null) return false;
            var k = Unwrap(c.PwWrapped, c.PwSalt, password);
            if (k == null) return false;
            ConvertAll(k, null);
            File.Delete(ConfigFile);
            _key = null;
            return true;
        }

        // ---------- Datei-Helper ----------

        private static byte[] Encrypt(byte[] plain, byte[] key)
        {
            var nonce = RandomNumberGenerator.GetBytes(12);
            var tag = new byte[16];
            var cipher = new byte[plain.Length];
            using (var aes = new AesGcm(key, 16)) aes.Encrypt(nonce, plain, cipher, tag);
            return Magic.Concat(nonce).Concat(tag).Concat(cipher).ToArray();
        }

        private static byte[] Decrypt(byte[] data, byte[] key)
        {
            if (!IsEncrypted(data)) throw new CryptographicException("Kein verschlüsseltes Format.");
            int o = Magic.Length;
            var nonce = data.AsSpan(o, 12);
            var tag = data.AsSpan(o + 12, 16);
            var cipher = data.AsSpan(o + 28);
            var plain = new byte[cipher.Length];
            using (var aes = new AesGcm(key, 16)) aes.Decrypt(nonce, cipher, tag, plain);
            return plain;
        }

        private static bool IsEncrypted(byte[] data) => data.Length >= Magic.Length + 28 && data.AsSpan(0, Magic.Length).SequenceEqual(Magic);

        public static byte[] ReadAllBytes(string path)
        {
            var data = File.ReadAllBytes(path);
            if (!IsEncrypted(data)) return data;
            if (_key == null) throw new UnauthorizedAccessException("Daten sind gesperrt (Passwort erforderlich).");
            return Decrypt(data, _key);
        }

        public static void WriteAllBytes(string path, byte[] data)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(tmp, _key == null ? data : Encrypt(data, _key));
            File.Move(tmp, path, true);
        }

        public static string ReadAllText(string path) => Decode(ReadAllBytes(path));
        public static string[] ReadAllLines(string path)
        {
            var lines = ReadAllText(path).Replace("\r\n", "\n").Split('\n');
            return lines.Length > 0 && lines[^1].Length == 0 ? lines[..^1] : lines;
        }
        public static void WriteAllText(string path, string text) => WriteAllBytes(path, new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray());
        public static void WriteAllLines(string path, IEnumerable<string> lines) => WriteAllText(path, string.Concat(lines.Select(l => l + Environment.NewLine)));
        public static void AppendAllText(string path, string text) => WriteAllText(path, (File.Exists(path) ? ReadAllText(path) : "") + text);

        private static string Decode(byte[] b)
        {
            using var r = new StreamReader(new MemoryStream(b), Encoding.UTF8, true);
            return r.ReadToEnd();
        }

        // ---------- Umstellung ----------

        /// <summary>Dateien, die geschützt werden (je Profil). Nicht: Profilwahl, Plugin-Aktivierung, Schalter-Flags.</summary>
        private static bool IsProtectedFile(string name)
        {
            var ext = Path.GetExtension(name).ToLowerInvariant();
            if (name.Equals("profil.txt", StringComparison.OrdinalIgnoreCase) || name.Equals("plugins.json", StringComparison.OrdinalIgnoreCase)
                || name.Equals("security.json", StringComparison.OrdinalIgnoreCase)
                || name.Equals("DE.txt", StringComparison.OrdinalIgnoreCase) || name.Equals("plz.csv", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Zips.txt", StringComparison.OrdinalIgnoreCase)) return false;
            if (ext is ".flag" or ".on" or ".off" or ".tmp") return false;
            if (name.StartsWith(DocumentStore.FileName, StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        private static IEnumerable<string> ProfileDirectories() =>
            ProfileManager.GetProfiles().Select(ProfileManager.GetDirectory).Where(Directory.Exists);

        private static void ConvertAll(byte[]? from, byte[]? to)
        {
            DocumentStore.Flush();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var dir in ProfileDirectories())
            {
                foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                {
                    if (!IsProtectedFile(Path.GetFileName(f))) continue;
                    if (Path.GetRelativePath(dir, f).StartsWith("Profile" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                    var data = File.ReadAllBytes(f);
                    var plain = IsEncrypted(data) ? (from != null ? Decrypt(data, from) : data) : data;
                    var tmp = f + ".tmp";
                    File.WriteAllBytes(tmp, to == null ? plain : Encrypt(plain, to));
                    File.Move(tmp, f, true);
                }
                var db = Path.Combine(dir, DocumentStore.FileName);
                if (File.Exists(db)) DocumentStore.Rekey(db, from, to);
            }
            ClearTextCache();
        }

        /// <summary>Leert den Klartext-Cache in %LocalAppData% (wird bei Bedarf neu und ggf. verschlüsselt aufgebaut).</summary>
        public static void ClearTextCache()
        {
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dateiumbenenner", "TextCache");
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
            catch { }
        }
    }
}
