using System.Text.Json.Serialization;

namespace Hi3Helper.Http
{
    [JsonSerializable(typeof(Metadata))]
    [JsonSourceGenerationOptions(
#if DEBUG
        WriteIndented = true,
#endif
        NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals,
        AllowTrailingCommas = true
        )]
    internal partial class MetadataJsonContext : JsonSerializerContext { }
}
