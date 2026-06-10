using PCL.Core.Logging;
using PCL.Core.Minecraft.Instance;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PCL.Core.Minecraft.Launch;

/// <summary>
/// Selects the optimal Java runtime for launching a Minecraft game instance.
/// Priority chain: Instance-specific → Version-appropriate → Global preference → Auto-scan fallback.
/// </summary>
public class LaunchJavaSelector
{
    private const string ModuleName = "LaunchJavaSelector";

    private static JavaManager Manager => JavaService.JavaManager;

    /// <summary>
    /// Select the best Java runtime for the given game instance.
    /// </summary>
    /// <param name="instance">The game instance to select Java for.</param>
    /// <param name="customJavaPath">Optional: instance-specific Java path override.</param>
    /// <param name="globalJavaPath">Optional: globally configured Java path.</param>
    /// <returns>The selected Java entry, or null if no suitable Java is found.</returns>
    public async Task<JavaEntry?> SelectJavaAsync(
        GameInstance instance,
        string? customJavaPath = null,
        string? globalJavaPath = null)
    {
        // ===== Priority 1: Instance-specific Java path =====
        if (!string.IsNullOrWhiteSpace(customJavaPath))
        {
            var entry = Manager.AddOrGet(customJavaPath);
            if (entry is not null && entry.IsEnabled)
            {
                LogWrapper.Info(ModuleName, $"Selected instance-specific Java: {entry}");
                return entry;
            }
            LogWrapper.Warn(ModuleName, $"Instance-specific Java path unavailable: {customJavaPath}");
        }

        // ===== Priority 2: Version-appropriate auto-selection =====
        var (minVersion, maxVersion) = GetRequiredJavaVersion(instance);
        LogWrapper.Info(ModuleName, $"Searching Java in range [{minVersion}, {maxVersion}] for instance '{instance.Name}'");

        var candidates = await Manager.SelectSuitableJavaAsync(minVersion, maxVersion).ConfigureAwait(false);
        if (candidates.Length > 0)
        {
            LogWrapper.Info(ModuleName, $"Auto-selected Java: {candidates[0]}");
            return candidates[0];
        }

        // No candidates found — trigger a rescan and retry
        LogWrapper.Info(ModuleName, "No matching Java found, triggering rescan...");
        await Manager.ScanJavaAsync().ConfigureAwait(false);
        candidates = await Manager.SelectSuitableJavaAsync(minVersion, maxVersion).ConfigureAwait(false);
        if (candidates.Length > 0)
        {
            LogWrapper.Info(ModuleName, $"Auto-selected Java after rescan: {candidates[0]}");
            return candidates[0];
        }

        // ===== Priority 3: Global Java preference =====
        if (!string.IsNullOrWhiteSpace(globalJavaPath))
        {
            var entry = Manager.AddOrGet(globalJavaPath);
            if (entry is not null && entry.IsEnabled)
            {
                LogWrapper.Info(ModuleName, $"Selected globally configured Java: {entry}");
                return entry;
            }
            LogWrapper.Warn(ModuleName, $"Global Java path unavailable: {globalJavaPath}");
        }

        // ===== Priority 4: Fallback — any enabled Java =====
        LogWrapper.Warn(ModuleName, "Falling back to any available enabled Java");
        var allJava = Manager.GetSortedJavaList();
        var fallback = allJava.FirstOrDefault(static j => j is { IsEnabled: true, Installation.IsStillAvailable: true });
        if (fallback is not null)
        {
            LogWrapper.Warn(ModuleName, $"Fallback Java (potential version mismatch): {fallback}");
            return fallback;
        }

        LogWrapper.Error(ModuleName, $"No suitable Java found for instance '{instance.Name}'");
        return null;
    }

    /// <summary>
    /// Determines the required Java version range for the given game instance.
    /// Uses the manifest's declared JavaVersion when available, otherwise infers
    /// from the vanilla Minecraft version and installed loaders.
    /// </summary>
    /// <returns>A tuple of (minVersion, maxVersion) representing the acceptable Java version range.</returns>
    public static (Version minVersion, Version maxVersion) GetRequiredJavaVersion(GameInstance instance)
    {
        // If the manifest explicitly declares a Java major version, use it directly.
        // Examples: 1.17 → 16, 1.18–1.20 → 17, 1.21+ → 21
        if (instance.JavaVersion is not null)
        {
            var major = (int)instance.JavaVersion;
            var max = major >= 21
                ? new Version(999, 999, 999) // Java 21+ accepts any newer version
                : new Version(major, 999, 999);
            return (new Version(major, 0, 0), max);
        }

        // Infer from vanilla version string when manifest has no JavaVersion field (pre-1.17).
        var vanilla = instance.VanillaVersion;
        var hasModernLoader = instance.HasLoader("fabric")
                           || instance.HasLoader("quilt")
                           || instance.HasLoader("legacyfabric");

        if (Version.TryParse(vanilla, out var v))
        {
            // For pre-1.13 snapshots, the version might just be a number.
            if (v.Major == 1)
            {
                switch (v.Minor)
                {
                    // ≤1.16.x — Java 8 is the standard requirement.
                    // Fabric/Quilt are more lenient and can run on newer Java.
                    case <= 16:
                        return hasModernLoader ?
                            (new Version(8, 0, 0), new Version(999, 999, 999)) :
                            (new Version(8, 0, 0), new Version(8, 999, 999));
                    // 1.17.x requires Java 16 specifically.
                    case 17:
                        return (new Version(16, 0, 0), new Version(16, 999, 999));
                    // 1.18.x – 1.20.x requires Java 17.
                    case >= 18 and <= 20:
                        return (new Version(17, 0, 0), new Version(17, 999, 999));
                    // 1.21+ requires Java 21, but any newer version is also acceptable.
                    case >= 21:
                        return (new Version(21, 0, 0), new Version(999, 999, 999));
                }
            }
        }

        // Fallback: accept any Java 8 or newer.
        LogWrapper.Warn(ModuleName, $"Unable to determine Java version from '{vanilla}', allowing all Java 8+");
        return (new Version(8, 0, 0), new Version(999, 999, 999));
    }

    /// <summary>
    /// Generates the default JVM arguments for launching Minecraft with the given Java runtime.
    /// Includes memory allocation and garbage collection settings appropriate for the Java version.
    /// </summary>
    public static List<string> GetDefaultJvmArgs(JavaEntry java, GameInstance instance)
    {
        var args = new List<string>();
        var majorVersion = java.Installation.MajorVersion;

        var memInfo = GC.GetGCMemoryInfo();
        var totalMemoryBytes = memInfo.TotalAvailableMemoryBytes;
        var xmx = (int)Math.Clamp(totalMemoryBytes / 4 / 1024 / 1024, 512, 4096);
        args.Add($"-Xmx{xmx}m");
        args.Add("-Xms256m");

        switch (majorVersion)
        {
            case >= 17:
                // Modern ZGC: low-pause, suitable for large heaps.
                args.Add("-XX:+UseZGC");
                args.Add("-XX:+ZGenerational");
                break;
            case >= 11:
                // G1GC is the default for JDK 11+, but explicitly request it
                // to ensure consistent behavior across all JDK builds.
                args.Add("-XX:+UseG1GC");
                args.Add("-XX:G1HeapRegionSize=4M");
                args.Add("-XX:+ParallelRefProcEnabled");
                args.Add("-XX:-UseParallelGC");
                break;
            case >= 8:
                // Java 8: prefer G1GC if available (JDK 8u20+), fall back to CMS.
                args.Add("-XX:+UseG1GC");
                args.Add("-XX:G1HeapRegionSize=4M");
                args.Add("-XX:+UseStringDeduplication");
                args.Add("-XX:-UseParallelGC");
                args.Add("-XX:-UseConcMarkSweepGC");
                break;
        }

        // ---- Common ----
        args.Add("-Dfile.encoding=UTF-8");
        args.Add("-Djava.io.tmpdir=.java-tmp");

        // ---- Server-side optimisations for single-player ----
        if (majorVersion >= 8)
        {
            args.Add("-XX:+UnlockExperimentalVMOptions");
            args.Add("-XX:+DisableExplicitGC");
        }

        // ---- JVM 9+ module flags ----
        if (majorVersion >= 9)
        {
            args.Add("--add-opens");
            args.Add("java.base/java.lang=ALL-UNNAMED");
            args.Add("--add-opens");
            args.Add("java.base/java.util=ALL-UNNAMED");
        }

        // ---- JVM 16+ flags ----
        if (majorVersion >= 16)
        {
            args.Add("--add-opens");
            args.Add("java.base/java.lang.ref=ALL-UNNAMED");
            args.Add("--add-opens");
            args.Add("java.base/java.text=ALL-UNNAMED");
        }

        return args;
    }
}
