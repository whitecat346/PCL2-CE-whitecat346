using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PCL.Core.Minecraft.Instance.Manifest;

/// <summary>
/// Defines a rule with an action and optional OS/feature conditions.
/// </summary>
public class Rule
{
    [JsonPropertyName("action")]
    public string Action { get; init; } = "allow"; // Default to "allow" if not specified

    [JsonPropertyName("os")]
    public OsSpecific? Os { get; init; }

    [JsonPropertyName("features")]
    public FeaturesSpecific? Features { get; init; }
}

/// <summary>
/// Specifies OS-related conditions for a rule.
/// </summary>
public class OsSpecific
{
    [JsonPropertyName("name")]
    public string? Name { get; init; } // e.g., "windows", "osx", "linux"

    [JsonPropertyName("version")]
    public string? Version { get; init; } // Regex pattern

    [JsonPropertyName("arch")]
    public string? Arch { get; init; } // e.g., "x86", "x64"
}

/// <summary>
/// Specifies feature-related conditions for a rule.
/// Boolean properties are nullable to distinguish "not set" from "set to false".
/// </summary>
public class FeaturesSpecific
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Features { get; init; }
}
