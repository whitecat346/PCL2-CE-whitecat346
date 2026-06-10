using System;
using System.Collections.Generic;
using PCL.Core.Minecraft.Instance.Manifest;

namespace PCL.Core.Minecraft.Instance.Versioning;

/// <summary>
/// Parser that extracts the Minecraft version by scanning library names (Maven coordinates)
/// for known patterns such as <c>net.minecraft:client:&lt;version&gt;</c>.
/// Also handles Forge/OptiFine/Fabric-like library version extraction as fallback.
/// Position in chain: fifth.
/// </summary>
internal sealed class LibraryVersionParser : IVersionParser
{
    private static readonly HashSet<string> _KnownMcArtifacts = new(StringComparer.OrdinalIgnoreCase)
    {
        "client",
        "server",
        "minecraft",
    };

    /// <inheritdoc />
    public VersionParserResult? Parse(VersionManifest? manifest, string indiePath)
    {
        if (manifest?.Libraries is null || manifest.Libraries.Count == 0)
            return null;

        // Pass 1: look for net.minecraft:client/server with a clean version
        foreach (var lib in manifest.Libraries)
        {
            if (string.IsNullOrWhiteSpace(lib.Name))
                continue;

            var result = _TryParseMcLibrary(lib.Name);
            if (result is not null)
                return result;
        }

        // Pass 2: scan all library names for any recognizable version string
        foreach (var lib in manifest.Libraries)
        {
            if (string.IsNullOrWhiteSpace(lib.Name))
                continue;

            var version = _ExtractVersionFromMaven(lib.Name);
            if (version is not null)
            {
                return new VersionParserResult(
                    VersionHelper.TryParseVersion(version),
                    version,
                    $"library name: {lib.Name}");
            }
        }

        return null;
    }

    /// <summary>
    /// Checks if the Maven coordinate matches net.minecraft:client/server and extracts the version.
    /// </summary>
    private static VersionParserResult? _TryParseMcLibrary(string name)
    {
        // Maven coordinate: group:artifact:version
        var span = name.AsSpan();

        // Match "net.minecraft:" prefix
        const string netMc = "net.minecraft:";
        if (!span.StartsWith(netMc.AsSpan(), StringComparison.OrdinalIgnoreCase))
            return null;

        span = span.Slice(netMc.Length);

        // Extract artifact name (up to next ':')
        var colonIdx = span.IndexOf(':');
        if (colonIdx < 0)
            return null;

        var artifact = span.Slice(0, colonIdx);
        if (!_KnownMcArtifacts.Contains(artifact.ToString()))
            return null;

        // The remainder is the version (with optional classifier after another ':')
        span = span.Slice(colonIdx + 1);
        var secondColon = span.LastIndexOf(':');
        var version = secondColon > 0 ? span.Slice(0, secondColon) : span;

        var versionStr = version.ToString();
        return string.IsNullOrWhiteSpace(versionStr)
            ? null
            : new VersionParserResult(
                VersionHelper.TryParseVersion(versionStr),
                versionStr,
                $"library: net.minecraft:{artifact}");
    }

    /// <summary>
    /// Attempts to extract a version string from any Maven coordinate.
    /// Handles patterns like <c>group:artifact:version</c> or <c>group:artifact:version:classifier</c>.
    /// </summary>
    private static string? _ExtractVersionFromMaven(string name)
    {
        // Split by ':' and look for the last segment that looks like a version
        var segments = name.Split(':');
        if (segments.Length < 3)
            return null;

        // The version is typically the third segment, or the second-to-last if there's a classifier
        // But be careful: some coordinates have extra segments
        for (var i = segments.Length - 1; i >= 2; i--)
        {
            var candidate = segments[i].AsSpan();
            if (candidate.Length > 0 && (char.IsDigit(candidate[0]) || candidate[0] == 'v'))
            {
                return segments[i];
            }
        }

        return null;
    }
}
