using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;

namespace Dateiumbenenner.Engine
{
    /// <summary>XML-Belegdefinition (BE_….xml, Wurzel &lt;Parameters&gt;): Firma, Kategorie und Erkennungsmerkmale.</summary>
    public sealed class XmlDefinition
    {
        public string Path { get; init; } = "";
        public string FileName => System.IO.Path.GetFileName(Path);
        public string Company { get; init; } = "";
        public string Category { get; init; } = "";
        /// <summary>Erkennungsmerkmale, normalisiert (Kleinbuchstaben, Leerraum zusammengefasst).</summary>
        public IReadOnlyList<string> Features { get; init; } = Array.Empty<string>();
    }

    /// <summary>Findet die zu Belegen gehörenden XML-Belegdefinitionen: alle Erkennungsmerkmale stehen im Belegtext
    /// (wie bei der Belegerkennung); Definitionen ohne Merkmale über den Firmennamen.</summary>
    public static class XmlDefinitions
    {
        /// <summary>Standard-Quellordner: Unterordner „XML“ des Belegordners, sonst der Belegordner selbst.</summary>
        public static string DefaultSourceFolder(string documentFolder)
        {
            var sub = System.IO.Path.Combine(documentFolder, "XML");
            return Directory.Exists(sub) ? sub : documentFolder;
        }

        /// <summary>Alle Belegdefinitionen eines Ordners (ohne Unterordner); andere XML-Dateien werden übergangen.</summary>
        public static List<XmlDefinition> Load(string folder)
        {
            var list = new List<XmlDefinition>();
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return list;
            foreach (var file in Directory.EnumerateFiles(folder, "*.xml"))
            {
                try
                {
                    var def = Read(file);
                    if (def != null) list.Add(def);
                }
                catch
                {
                }
            }
            return list.OrderBy(d => d.FileName, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static XmlDefinition? Read(string file)
        {
            var doc = new XmlDocument { XmlResolver = null };
            using (var reader = XmlReader.Create(file, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
                doc.Load(reader);
            var root = doc.DocumentElement;
            if (root == null || root.Name != "Parameters") return null;
            var features = new List<string>();
            foreach (XmlNode n in root.SelectNodes("Parameter/Erkennungsmerkmal")!)
            {
                var v = Normalize(Decode(n.InnerText));
                if (v.Length > 0 && !features.Contains(v)) features.Add(v);
            }
            var company = root.GetAttribute("Kategorie1").Trim();
            if (company.Length == 0) company = root.GetAttribute("AdresseDTM").Trim();
            return new XmlDefinition { Path = file, Company = company, Category = root.GetAttribute("Kategorie2").Trim(), Features = features };
        }

        private static string Decode(string value)
        {
            if (!value.StartsWith("B64_", StringComparison.Ordinal)) return value;
            try { return Encoding.UTF8.GetString(Convert.FromBase64String(value.Substring(4))); }
            catch { return value; }
        }

        /// <summary>Kleinbuchstaben, jeder Leerraum (auch Zeilenumbrüche) als ein Leerzeichen.</summary>
        public static string Normalize(string? text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new StringBuilder(text.Length);
            bool space = false;
            foreach (var ch in text)
            {
                if (char.IsWhiteSpace(ch)) { space = sb.Length > 0; continue; }
                if (space) { sb.Append(' '); space = false; }
                sb.Append(char.ToLowerInvariant(ch));
            }
            return sb.ToString();
        }

        /// <summary>True, wenn die Definition zum Beleg passt. <paramref name="normalizedText"/> = <see cref="Normalize"/> des Belegtexts.</summary>
        public static bool Matches(XmlDefinition def, string normalizedText, string? company)
        {
            if (def.Features.Count > 0)
                return normalizedText.Length > 0 && def.Features.All(f => normalizedText.Contains(f, StringComparison.Ordinal));
            return def.Company.Length > 0 && CompanyMatcher.IsSame(def.Company, company);
        }
    }
}
