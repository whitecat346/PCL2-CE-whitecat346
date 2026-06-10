using PCL.Core.Minecraft.Instance.Manifest;
using System;
using System.IO;

namespace PCL.Core.Minecraft.Instance.Versioning;

/// <summary>
/// Parser that extracts the Minecraft version from the instance folder name.
/// Performs heuristic version detection without regular expressions.
/// Position in chain: sixth (lowest priority / last resort).
/// </summary>
internal sealed class FolderNameParser : IVersionParser
{
    /// <inheritdoc />
    public VersionParserResult? Parse(VersionManifest? manifest, string indiePath)
    {
        if (string.IsNullOrWhiteSpace(indiePath))
            return null;

        // Extract the last folder name from the path
        var folderName = Path.GetFileName(indiePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(folderName))
            return null;

        // Attempt to parse the folder name as a version string
        var versionStr = _TryExtractVersion(folderName);
        if (versionStr is null)
            return null;

        return new VersionParserResult(
            VersionHelper.TryParseVersion(versionStr),
            versionStr,
            $"folder name: {folderName}");
    }

    /// <summary>
    /// Attempts to extract a version-like substring from the folder name using heuristic scanning.
    /// </summary>
    private static string? _TryExtractVersion(string folderName)
    {
        var span = folderName.AsSpan();

        // If the folder name itself looks like a version, use it directly
        if (_LooksLikeVersion(span))
            return folderName;

        // Scan for embedded version patterns
        for (var i = 0; i < span.Length; i++)
        {
            // Look for patterns like "1.x" or "2x.xx"
            if (span[i] == '1' && i + 1 < span.Length && span[i + 1] == '.')
            {
                var end = _FindVersionEnd(span.Slice(i));
                if (end > 2)
                    return span.Slice(i, end).ToString();
            }

            // Look for snapshot patterns like "20w14a", "21w37a"
            if (i + 5 < span.Length &&
                char.IsDigit(span[i]) && char.IsDigit(span[i + 1]) &&
                span[i + 2] == 'w' &&
                char.IsDigit(span[i + 3]) && char.IsDigit(span[i + 4]))
            {
                var end = i + 5;
                while (end < span.Length && char.IsLetter(span[end]))
                    end++;
                return span.Slice(i, end - i).ToString();
            }
        }

        return null;
    }

    /// <summary>
    /// Determines the end of a version string starting from <paramref name="span"/>.
    /// </summary>
    private static int _FindVersionEnd(ReadOnlySpan<char> span)
    {
        for (var i = 1; i < span.Length; i++)
        {
            var c = span[i];
            if (!char.IsDigit(c) && c != '.' && c != '-' && c != '_')
                return i;
        }

        return span.Length;
    }

    /// <summary>
    /// Heuristic check: does the string look like a Minecraft version string?
    /// </summary>
    private static bool _LooksLikeVersion(ReadOnlySpan<char> span)
    {
        if (span.Length == 0)
            return false;

        // "1.x" pattern (e.g., "1.20.1", "1.19")
        if (span.Length >= 3 && span[0] == '1' && span[1] == '.')
            return true;

        // "2x.xx" pattern for future major versions (e.g., "20.14", "21.37")
        if (span.Length >= 3 && char.IsDigit(span[0]) && char.IsDigit(span[1]) && span[2] == '.')
            return true;

        // Snapshot pattern: "YYwWWx" (e.g., "20w14a")
        if (span.Length >= 6 &&
            char.IsDigit(span[0]) && char.IsDigit(span[1]) &&
            span[2] == 'w' &&
            char.IsDigit(span[3]) && char.IsDigit(span[4]) &&
            char.IsLetter(span[5]))
            return true;

        return false;
    }
}
