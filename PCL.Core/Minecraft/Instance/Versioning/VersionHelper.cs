using System;
using System.Globalization;

namespace PCL.Core.Minecraft.Instance.Versioning;

/// <summary>
/// Shared utility for parsing Minecraft version strings into <see cref="Version"/> objects.
/// Avoids regular expressions; uses <see cref="ReadOnlySpan{T}"/> for efficient string operations.
/// </summary>
internal static class VersionHelper
{
    /// <summary>
    /// Attempts to parse a Minecraft version string (e.g., "1.20.1", "1.19") into a <see cref="Version"/>.
    /// Returns <c>null</c> if the string does not represent a valid version.
    /// Handles format: major.minor[.build] with optional suffixes.
    /// </summary>
    public static Version? TryParseVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return null;

        var span = version.AsSpan().Trim();

        // Strip common suffixes like "_unobfuscated", "-pre", "-rc", etc.
        var dashIdx = span.IndexOfAny("-+_ ".AsSpan());
        if (dashIdx > 0)
            span = span.Slice(0, dashIdx);

        return _TryParseVersionCore(span);
    }

    /// <summary>
    /// Attempts to parse a version-like string from a span, returning both the parsed <see cref="Version"/>
    /// and the extracted version substring. Designed for scanning argument lists.
    /// </summary>
    public static bool TryParseSimpleVersion(ReadOnlySpan<char> span, out Version? version, out string versionStr)
    {
        version = null;
        versionStr = string.Empty;

        if (span.Length == 0)
            return false;

        // Must start with a digit
        if (!char.IsDigit(span[0]))
            return false;

        // Find the end of the version pattern
        var end = 0;
        while (end < span.Length)
        {
            var c = span[end];
            if (char.IsDigit(c) || c == '.')
            {
                end++;
            }
            else
            {
                // Allow a single trailing letter only (e.g., "1.12.2a" - rare)
                if (end > 0 && char.IsLetter(c) && end + 1 >= span.Length)
                    end++;
                break;
            }
        }

        if (end < 3) // Too short to be a version
            return false;

        var candidate = span.Slice(0, end);
        var parsed = _TryParseVersionCore(candidate);
        if (parsed is null)
            return false;

        version = parsed;
        versionStr = candidate.ToString();
        return true;
    }

    /// <summary>
    /// Core version parsing: "1.20.1" → (1, 20, 1), "1.19" → (1, 19, 0), "20w14a" → null.
    /// </summary>
    private static Version? _TryParseVersionCore(ReadOnlySpan<char> span)
    {
        if (span.Length == 0)
            return null;

        // Count segments
        var dotCount = 0;
        foreach (var c in span)
        {
            if (c == '.')
                dotCount++;
        }

        switch (dotCount)
        {
            case 0:
            {
                // Single number - unlikely for MC but handle it
                if (int.TryParse(span, NumberStyles.None, CultureInfo.InvariantCulture, out var major))
                    return new Version(major, 0);
                return null;
            }
            case 1:
            {
                // Format: "1.19"
                var dotIdx = span.IndexOf('.');
                var majorStr = span.Slice(0, dotIdx);
                var minorStr = span.Slice(dotIdx + 1);

                if (int.TryParse(majorStr, NumberStyles.None, CultureInfo.InvariantCulture, out var major) &&
                    _TryParseIntBound(minorStr, out var minor))
                {
                    return new Version(major, minor, 0);
                }

                return null;
            }
            case 2:
            {
                // Format: "1.20.1" or "1.19.2"
                var firstDot = span.IndexOf('.');
                var secondDot = span.Slice(firstDot + 1).IndexOf('.') + firstDot + 1;

                var majorStr = span.Slice(0, firstDot);
                var minorStr = span.Slice(firstDot + 1, secondDot - firstDot - 1);
                var buildStr = span.Slice(secondDot + 1);

                if (int.TryParse(majorStr, NumberStyles.None, CultureInfo.InvariantCulture, out var major) &&
                    _TryParseIntBound(minorStr, out var minor) &&
                    _TryParseIntBound(buildStr, out var build))
                {
                    return new Version(major, minor, build);
                }

                return null;
            }
            default:
                // More than 3 segments - not a standard MC version
                return null;
        }
    }

    /// <summary>
    /// Tries to parse an integer from a span, ensuring it's non-negative and within a reasonable bound.
    /// </summary>
    private static bool _TryParseIntBound(ReadOnlySpan<char> span, out int result)
    {
        result = 0;
        if (span.Length is 0 or > 4) // MC versions won't have segments > 9999
            return false;

        // Quick check: all characters must be digits
        foreach (var c in span)
        {
            if (!char.IsDigit(c))
                return false;
        }

        return int.TryParse(span, NumberStyles.None, CultureInfo.InvariantCulture, out result);
    }
}
