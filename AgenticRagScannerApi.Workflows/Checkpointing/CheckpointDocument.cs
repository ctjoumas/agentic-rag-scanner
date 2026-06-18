using Newtonsoft.Json;

namespace AgenticRagScannerApi.Workflows.Checkpointing;

/// <summary>
/// The Cosmos document persisted per checkpoint. The MAF checkpoint payload is stored as raw JSON in
/// <see cref="ValueJson"/> (the workflow serializes its state with System.Text.Json), which keeps it
/// independent of the Cosmos SDK's Newtonsoft-based document serializer. Partitioned by
/// <see cref="SessionId"/>.
/// </summary>
internal sealed class CheckpointDocument
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [JsonProperty("checkpointId")]
    public string CheckpointId { get; set; } = string.Empty;

    [JsonProperty("parentCheckpointId")]
    public string? ParentCheckpointId { get; set; }

    [JsonProperty("valueJson")]
    public string ValueJson { get; set; } = string.Empty;
}
