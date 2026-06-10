using PCL.Core.Minecraft.Instance.Manifest;

namespace PCL.Core.Minecraft.Instance.Versioning;

/// <summary>
/// Parser that extracts the Minecraft version from the <c>inheritsFrom</c> field
/// in the manifest. Used when a version manifest extends another (e.g., Forge installs).
/// For LiteLoader, checks <c>jar</c> field first if present in raw JSON.
/// Position in chain: fourth.
/// </summary>
internal sealed class InheritsFromParser : IVersionParser
{
    /// <inheritdoc />
    public VersionParserResult? Parse(VersionManifest? manifest, string indiePath)
    {
        if (manifest?.InheritsFrom is null)
            return null;

        var inheritsFrom = manifest.InheritsFrom;
        if (string.IsNullOrWhiteSpace(inheritsFrom))
            return null;

        // The inheritsFrom value is used directly as the version string.
        return new VersionParserResult(
            VersionHelper.TryParseVersion(inheritsFrom),
            inheritsFrom,
            "manifest.inheritsFrom");
    }
}
