using System.Collections.Generic;
using System.Text.Json.Serialization;

public class LotClassificationResult
{
    [JsonPropertyName("categories")]
    public List<string> Categories { get; set; } = default!;

    [JsonPropertyName("title")]
    public string Title { get; set; } = default!;

    [JsonPropertyName("isSharedOwnership")]
    public bool IsSharedOwnership { get; set; }

    [JsonPropertyName("suggestedCategory")]
    public string? SuggestedCategory { get; set; }
}
