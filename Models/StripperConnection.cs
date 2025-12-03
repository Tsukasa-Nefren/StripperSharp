using System;
using System.Text.Json.Serialization;

namespace Kxnrl.StripperSharp.Models;

internal class StripperConnection
{
    [JsonPropertyName("output")]
    public string? Output { get; init; }

    [JsonPropertyName("target")]
    public string? Target { get; init; }

    [JsonPropertyName("input")]
    public string? Input { get; init; }

    [JsonPropertyName("param")]
    public string? Param { get; init; }

    [JsonPropertyName("delay")]
    public float? Delay { get; init; }

    [JsonPropertyName("limit")]
    public int? Limit { get; init; }

    [Obsolete("Use Output instead", true)]
    [JsonPropertyName("outputname")]
    public string? OutputName
    {
        init => Output = value;
    }

    [Obsolete("Use Input instead", true)]
    [JsonPropertyName("inputname")]
    public string? InputName
    {
        init => Input = value;
    }

    [Obsolete("Use Target instead", true)]
    [JsonPropertyName("targetname")]
    public string? TargetName
    {
        init => Target = value;
    }

    [Obsolete("Use Param instead", true)]
    [JsonPropertyName("overrideparam")]
    public string? OverrideParam
    {
        init => Param = value;
    }

    [Obsolete("Use Limit instead", true)]
    [JsonPropertyName("timestofire")]
    public int? TimesToFire
    {
        init => Limit = value;
    }
}
