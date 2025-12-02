using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kxnrl.StripperSharp.Models;

internal class StripperFile
{
    [JsonPropertyName("add")]
    public List<Dictionary<string, JsonDocument>>? Add { get; init; }

    [JsonPropertyName("modify")]
    public List<Dictionary<string, JsonDocument>>? Modify { get; init; }

    [JsonPropertyName("remove")]
    public List<Dictionary<string, JsonDocument>>? Remove { get; init; }

    [Obsolete("Use Remove instead", true)]
    [JsonPropertyName("filter")]
    public List<Dictionary<string, JsonDocument>>? Filter
    {
        init => Remove = value;
    }
}
