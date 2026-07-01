using AgenticRagScannerApi.Services;
using Newtonsoft.Json;

namespace AgenticRagScannerApi.Seeding;

/// <summary>
/// A Cosmos document representing a single taxonomy tag. Stored in the RegDocs container with
/// <c>doc_type = "tags"</c> (the container's partition key). Serialized by the Cosmos SDK's
/// Newtonsoft serializer, hence the <see cref="JsonProperty"/> attributes.
/// </summary>
internal sealed class TagDocument : ICosmosEntity
{
    [JsonProperty("id")]
    public string Id { get; init; } = Guid.NewGuid().ToString();

    [JsonProperty("doc_type")]
    public string DocType { get; init; } = TagSeeder.TagDocType;

    [JsonProperty("name")]
    public string Name { get; init; } = string.Empty;
}
