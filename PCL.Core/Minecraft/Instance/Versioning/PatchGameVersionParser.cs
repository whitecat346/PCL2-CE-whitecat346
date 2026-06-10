using System;
using System.Text.Json;
using PCL.Core.Minecraft.Instance.Manifest;

namespace PCL.Core.Minecraft.Instance.Versioning;

/// <summary>
/// Parser that extracts the Minecraft version from HMCL-format <c>patches</c> array
/// in the raw JSON data. Looks for <c>patches[i].game.version</c> or <c>patches[i].version</c>
/// where <c>patches[i].id == "game"</c>.
/// Position in chain: second.
/// </summary>
internal sealed class PatchGameVersionParser : IVersionParser
{
    /// <inheritdoc />
    public VersionParserResult? Parse(VersionManifest? manifest, string indiePath)
    {
        if (manifest?.JsonOriginData is null)
            return null;

        using var doc = JsonDocument.Parse(manifest.JsonOriginData);
        if (!doc.RootElement.TryGetProperty("patches", out var patches))
            return null;

        foreach (var patch in patches.EnumerateArray())
        {
            // Check if this patch targets "game"
            if (!patch.TryGetProperty("id", out var id) ||
                !string.Equals(id.GetString(), "game", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Try patches[i].game.version (HMCL fork format)
            if (patch.TryGetProperty("game", out var game) &&
                game.TryGetProperty("version", out var version))
            {
                var versionStr = version.GetString();
                if (!string.IsNullOrWhiteSpace(versionStr))
                    return new VersionParserResult(
                        VersionHelper.TryParseVersion(versionStr),
                        versionStr,
                        "HMCL patches.game.version");
            }

            // Try patches[i].version (standard HMCL format)
            if (patch.TryGetProperty("version", out var directVersion))
            {
                var versionStr = directVersion.GetString();
                if (!string.IsNullOrWhiteSpace(versionStr))
                    return new VersionParserResult(
                        VersionHelper.TryParseVersion(versionStr),
                        versionStr,
                        "HMCL patches.version");
            }
        }

        return null;
    }
}
