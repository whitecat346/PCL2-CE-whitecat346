using System;

namespace PCL.Core.Minecraft.Instance.Versioning;

/// <summary>
/// Represents the result of a version parsing attempt.
/// </summary>
/// <param name="McVersion">The parsed <see cref="Version"/>, or <c>null</c> if the version string could not be parsed as a <c>System.Version</c>.</param>
/// <param name="McVersionString">The raw version string as extracted from the source.</param>
/// <param name="Source">A human-readable description of how the version was determined.</param>
internal readonly record struct VersionParserResult(
    Version? McVersion,
    string McVersionString,
    string Source);
