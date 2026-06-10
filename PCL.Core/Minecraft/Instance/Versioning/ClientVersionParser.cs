using PCL.Core.Minecraft.Instance.Manifest;

namespace PCL.Core.Minecraft.Instance.Versioning;

/// <summary>
/// Parser that extracts the Minecraft version from <c>manifest.Id</c>.
/// This corresponds to the <c>clientVersion</c> field in PCL's downloaded version manifests.
/// Position in chain: first (highest priority).
/// </summary>
internal sealed class ClientVersionParser : IVersionParser
{
    /// <inheritdoc />
    public VersionParserResult? Parse(VersionManifest? manifest, string indiePath)
    {
        if (manifest is null)
            return null;

        var id = manifest.Id;
        if (string.IsNullOrWhiteSpace(id))
            return null;

        return new VersionParserResult(
            VersionHelper.TryParseVersion(id),
            id,
            "manifest.Id (clientVersion)");
    }
}
