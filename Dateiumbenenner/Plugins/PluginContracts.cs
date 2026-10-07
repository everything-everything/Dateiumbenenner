using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;

namespace Dateiumbenenner.Plugins
{
    /// <summary>Funktionen des Hauptprogramms, die Plugins nutzen dürfen.</summary>
    public interface IPluginHost
    {
        Window MainWindow { get; }
        Task<DocumentMetadata> AnalyzeFileAsync(string filePath, string? textSourcePath = null);
        IReadOnlyList<string> DocumentTypes { get; }
        List<string> ExtractCompanyNames(string? fullText, string? pdfPath = null);
        /// <summary>Gemeinsame Erkennungs-Engine (Adresse, Betrag, ...).</summary>
        Dateiumbenenner.Engine.IDocumentEngine Engine { get; }
        /// <summary>A-Modus: nur PDFs laden, Text aus gleichnamiger .txt verwenden. Gilt für alle Register.</summary>
        bool AMode { get; set; }
        event System.EventHandler? AModeChanged;
        /// <summary>OCR-Korrektur für eingelesene Texte. Gilt für alle Register.</summary>
        bool OcrCorrection { get; set; }
        event System.EventHandler? OcrCorrectionChanged;
        /// <summary>Plugins können eine Adressquelle (z. B. Datenbank) als Fallback anmelden.</summary>
        void RegisterAddressProvider(IAddressProvider provider);
    }

    /// <summary>Liefert eine Firmenadresse (Fallback, wenn das Dokument keine Adresse liefert).</summary>
    public interface IAddressProvider
    {
        Task<AddressInfo?> LookupAddressAsync(string company);
    }

    public record AddressInfo(string? Street, string? Zip, string? City);

    /// <summary>Schnittstelle, die jede Plugin-DLL implementieren muss.</summary>
    public interface IPlugin
    {
        string Id { get; }
        string Name { get; }
        string Description { get; }
        string Version { get; }
        void Initialize(IPluginHost host);
        /// <summary>Inhalt der Registerkarte, die beim Aktivieren eingefügt wird.</summary>
        FrameworkElement CreateTabContent();
    }
}
