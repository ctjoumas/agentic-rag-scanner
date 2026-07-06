using Newtonsoft.Json;

namespace AgenticRagScannerApi.Services;

/// <summary>
/// Read model for a single categorization-vocabulary entry stored in the RegDocs container - an impact
/// area (<c>doc_type = "ImpactAreas"</c>) or a tag (<c>doc_type = "tags"</c>). Only the fields the
/// runtime needs are projected (<c>id</c>, <c>doc_type</c>, <c>name</c>); the seeder-side documents
/// (<c>ImpactAreaDocument</c> / <c>TagDocument</c>) share the same shape. Serialized by the Cosmos SDK's
/// Newtonsoft serializer, hence the <see cref="JsonProperty"/> attributes.
/// </summary>
public sealed class VocabularyDocument : ICosmosEntity
{
    [JsonProperty("id")]
    public string Id { get; init; } = string.Empty;

    [JsonProperty("doc_type")]
    public string DocType { get; init; } = string.Empty;

    [JsonProperty("name")]
    public string Name { get; init; } = string.Empty;
}
