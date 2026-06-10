using PCL.Core.Minecraft.Instance.Manifest;

namespace PCL.Core.Minecraft.Instance.Versioning;

/// <summary>
/// Strategy interface for parsing a Minecraft version from a version manifest.
/// Each implementation attempts to extract the version using a specific strategy,
/// returning <c>null</c> if it cannot determine the version.
/// </summary>
internal interface IVersionParser
{
    /// <summary>
    /// Attempts to parse a Minecraft version from the given manifest and instance path.
    /// </summary>
    /// <param name="manifest">The version manifest to parse, or <c>null</c> if unavailable.</param>
    /// <param name="indiePath">The instance folder path, used as a fallback source.</param>
    /// <returns>
    /// A <see cref="VersionParserResult"/> if the version was successfully determined;
    /// <c>null</c> if this parser could not extract a version.
    /// </returns>
    VersionParserResult? Parse(VersionManifest? manifest, string indiePath);
}
