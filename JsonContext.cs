using System.Text.Json.Serialization;
// ReSharper disable PartialTypeWithSinglePart

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
