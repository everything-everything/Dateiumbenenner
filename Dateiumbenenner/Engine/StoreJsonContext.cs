using System.Text.Json.Serialization;

namespace Dateiumbenenner
{
    /// <summary>Zur Kompilierzeit erzeugter JSON-Code für gespeicherte Belegstände (gleiches Format wie die Standard-Serialisierung, aber ohne Reflection).</summary>
    [JsonSerializable(typeof(DocumentStore.Record))]
    internal partial class StoreJsonContext : JsonSerializerContext
    {
    }
}
