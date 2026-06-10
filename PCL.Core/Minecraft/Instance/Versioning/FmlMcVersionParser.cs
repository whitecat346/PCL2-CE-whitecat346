using PCL.Core.Minecraft.Instance.Manifest;
using System;
using System.Collections.Generic;

namespace PCL.Core.Minecraft.Instance.Versioning;

/// <summary>
/// Parser that extracts the Minecraft version from the <c>--fml.mcVersion</c> JVM argument
/// added by Forge / NeoForge / LabyMod, or from version-like strings in JVM arguments.
/// Position in chain: third.
/// </summary>
internal sealed class FmlMcVersionParser : IVersionParser
{
    private const string FmlArg = "--fml.mcVersion";

    /// <inheritdoc />
    public VersionParserResult? Parse(VersionManifest? manifest, string indiePath)
    {
        if (manifest?.Arguments is null)
            return null;

        // First, look in game arguments for --fml.mcVersion <value>
        var result = _FindFmlVersion(manifest.Arguments.Game, "game");
        if (result is not null)
            return result;

        // Fall back to JVM arguments
        result = _FindFmlVersion(manifest.Arguments.Jvm, "jvm");
        if (result is not null)
            return result;

        // Last, scan JVM arguments for a LabyMod-style version string
        foreach (var arg in manifest.Arguments.Jvm)
        {
            if (arg is not StringArgument strArg)
                continue;

            var value = strArg.Value.AsSpan().Trim('"');
            if (VersionHelper.TryParseSimpleVersion(value, out var parsed, out var versionStr))
            {
                return new VersionParserResult(
                    parsed,
                    versionStr,
                    "LabyMod JVM argument version");
            }
        }

        return null;
    }

    private static VersionParserResult? _FindFmlVersion(List<ArgumentElement> args, string sourceName)
    {
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (args[i] is not StringArgument { Value: FmlArg })
                continue;

            if (args[i + 1] is not StringArgument valueArg)
                continue;

            var versionStr = valueArg.Value.Trim('"');
            if (!string.IsNullOrWhiteSpace(versionStr))
            {
                return new VersionParserResult(
                    VersionHelper.TryParseVersion(versionStr),
                    versionStr,
                    $"--fml.mcVersion from {sourceName} arguments");
            }
        }

        return null;
    }
}
