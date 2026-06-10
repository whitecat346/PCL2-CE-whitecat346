using PCL.Core.IO.Storage.Cache;
using PCL.Core.IO.Storage.Cache.Model;
using PCL.Core.Logging;
using PCL.Core.Minecraft.Instance.Manifest;
using PCL.Core.Utils.Exts;
using PCL.Core.Utils.Hash;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PCL.Core.Minecraft.Instance;

/// <summary>
/// Two-level cache loader for Minecraft instances (indie/isolation mode).<br/>
/// Fast Path: SQLite-backed InstanceCacheRow lookup (≈1ms).<br/>
/// Slow Path: Parse version.json from disk, detect loaders, upsert cache (50-200ms).<br/>
/// Per-instance semaphore ensures only one load per indiePath at a time.
/// </summary>
public sealed class GameInstanceLoader
{
    private readonly CacheService _cache;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions _JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private const string LogModule = "GameInstanceLoader";

    /// <summary>
    /// Creates a loader using the global <see cref="CacheServiceManager.Current"/> instance.
    /// </summary>
    public GameInstanceLoader() : this(CacheServiceManager.Current) { }

    /// <summary>
    /// Creates a loader with an explicit cache service (for testing or DI).
    /// </summary>
    /// <exception cref="ArgumentNullException">Throws when <paramref name="cache"/>"> is null.</exception>
    public GameInstanceLoader(CacheService cache)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    /// <summary>
    /// Load a single indie instance by its folder path.
    /// Returns <see langword="null"/> if the path does not exist or has no valid version manifest.
    /// </summary>
    public async Task<GameInstance?> LoadInstanceAsync(string indiePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(indiePath))
            return null;

        if (!Directory.Exists(indiePath))
        {
            LogWrapper.Debug(LogModule, $"Instance folder not found: {indiePath}");
            return null;
        }

        var semaphore = _locks.GetOrAdd(indiePath, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await _LoadInstanceCoreAsync(indiePath, ct).ConfigureAwait(false);
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>
    /// Load multiple indie instances concurrently.
    /// Each instance uses its own per-path lock internally.
    /// </summary>
    public async Task<List<GameInstance>> LoadInstancesAsync(IEnumerable<string> indiePaths, CancellationToken ct = default)
    {
        var tasks = indiePaths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => LoadInstanceAsync(p, ct));
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results.Where(r => r is not null).Cast<GameInstance>().ToList();
    }


    private async Task<GameInstance?> _LoadInstanceCoreAsync(string indiePath, CancellationToken ct)
    {
        // SQLite cache lookup
        var cached = await _cache.LookupInstanceAsync(indiePath, ct).ConfigureAwait(false);
        if (cached is not null)
        {
            var jsonPath = _ResolveVersionJson(indiePath);
            if (File.Exists(jsonPath))
            {
                var currentHash = await _ComputeFileSha256Async(jsonPath, ct).ConfigureAwait(false);
                if (string.Equals(cached.SourceJsonHash, currentHash, StringComparison.OrdinalIgnoreCase))
                {
                    LogWrapper.Info(LogModule, $"Fast-path HIT: {indiePath}");
                    return GameInstance.FromCache(cached, indiePath);
                }

                LogWrapper.Info(LogModule, $"Fast-path miss (hash changed): {indiePath}");
            }
            else
            {
                LogWrapper.Warn(LogModule, $"Fast-path miss (version.json deleted): {indiePath}");
            }
        }
        else
        {
            LogWrapper.Debug(LogModule, $"Fast-path miss (no cache): {indiePath}");
        }

        // or Parse version manifest from disk
        var resolvedJson = _ResolveVersionJson(indiePath);
        if (!File.Exists(resolvedJson))
        {
            LogWrapper.Warn(LogModule, $"No version manifest found in: {indiePath}");
            return null;
        }

        byte[] jsonBytes;
        try
        {
            jsonBytes = await File.ReadAllBytesAsync(resolvedJson, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogWrapper.Error(ex, LogModule, $"Failed to read version manifest: {resolvedJson}");
            return null;
        }

        VersionManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<VersionManifest>(jsonBytes, _JsonOpts);
        }
        catch (JsonException ex)
        {
            LogWrapper.Error(ex, LogModule, $"Failed to parse version manifest: {resolvedJson}");
            return null;
        }

        if (manifest is null)
        {
            LogWrapper.Warn(LogModule, $"Deserialized null version manifest: {resolvedJson}");
            return null;
        }

        // Retain raw JSON for downstream consumers
        manifest.JsonOriginData = System.Text.Encoding.UTF8.GetString(jsonBytes);

        var sourceHash = _ComputeSha256(jsonBytes);
        var loaders = _DetectLoaders(indiePath);
        var state = _ResolveState(loaders, manifest.Type);

        var row = new InstanceCacheRow
        {
            InstancePath = indiePath,
            InstanceName = _Coalesce(manifest.Name, manifest.Id, Path.GetFileName(indiePath)),
            InstanceState = state.ToString(),
            VanillaName = manifest.Id,
            VanillaVersion = manifest.Id,
            ReleaseTime = manifest.ReleaseTime,
            MainClass = manifest.MainClass,
            AssetsIndex = manifest.AssetIndex?.Id ?? manifest.Assets,
            InheritsFrom = manifest.InheritsFrom,
            JavaVersion = manifest.JavaVersion?.MajorVersion,
            SourceJsonHash = sourceHash,
            FormatVersion = 1,
            CachedAt = DateTime.UtcNow,
            LastLoadedAt = DateTime.UtcNow,
            Reliable = true,
            CardType = 0,
            IsStarred = false,
            DropNumber = 0,
        };
        row.SetLoaders(loaders);

        try
        {
            await _cache.UpsertInstanceAsync(row, ct).ConfigureAwait(false);
            LogWrapper.Info(LogModule, $"Slow-path UPSERT: {indiePath} [{loaders.Count} loader(s)]");
        }
        catch (Exception ex)
        {
            LogWrapper.Error(ex, LogModule, $"Failed to upsert cache for: {indiePath}");
        }

        return GameInstance.FromCache(row, indiePath);
    }


    /// <summary>
    /// Resolves the version manifest JSON path for an indie instance.
    /// Tries <c>version.json</c> first, then <c>&lt;folderName&gt;.json</c>.
    /// </summary>
    private static string _ResolveVersionJson(string indiePath)
    {
        var primary = Path.Combine(indiePath, "version.json");
        if (File.Exists(primary))
            return primary;

        return Path.Combine(indiePath, $"{Path.GetFileName(indiePath)}.json");
    }


    /// <summary>
    /// Scans the instance directory for known mod-loader indicators.
    /// Checks both <c>config/&lt;loader&gt;/</c> directories and <c>mods/</c> JAR patterns.
    /// </summary>
    private static List<LoaderEntry> _DetectLoaders(string indiePath)
    {
        var loaders = new List<LoaderEntry>();
        var configDir = Path.Combine(indiePath, "config");
        var modsDir = Path.Combine(indiePath, "mods");

        CheckConfigMarker("fabric", "fabric");
        CheckConfigMarker("forge", "forge");
        CheckConfigMarker("neoforge", "neoforge");
        CheckConfigMarker("quilt", "quilt");
        CheckConfigMarker("liteloader", "liteloader");
        CheckConfigMarker("legacyfabric", "legacyfabric");
        CheckConfigMarker("cleanroom", "cleanroom");
        CheckConfigMarker("labymod", "labymod");

        if (!Directory.Exists(modsDir)) return loaders;

        try
        {
            CheckModsPattern("fabric", "fabric-loader-*.jar");
            CheckModsPattern("forge", "forge-*.jar");
            CheckModsPattern("neoforge", "neoforge-*.jar");
            CheckModsPattern("quilt", "quilt-*.jar");
            CheckModsPattern("liteloader", "liteloader-*.jar");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogWrapper.Warn(ex, LogModule, $"Error scanning mods directory: {modsDir}");
        }

        return loaders;

        void CheckModsPattern(string type, string pattern)
        {
            if (loaders.Any(l => l.Type == type))
                return;
            if (Directory.GetFiles(modsDir, pattern, SearchOption.TopDirectoryOnly).Length > 0)
                loaders.Add(new LoaderEntry { Type = type, Version = string.Empty });
        }

        void CheckConfigMarker(string type, string subDir)
        {
            var path = Path.Combine(configDir, subDir);
            if (Directory.Exists(path) && loaders.All(l => l.Type != type))
                loaders.Add(new LoaderEntry { Type = type, Version = string.Empty });
        }
    }

    /// <summary>
    /// Maps detected loaders (plus version type) to a <see cref="GameInstanceState"/>.
    /// </summary>
    private static GameInstanceState _ResolveState(List<LoaderEntry> loaders, string? versionType)
    {
        if (loaders.Count > 0)
        {
            return loaders[0].Type switch
            {
                "fabric" => GameInstanceState.Fabric,
                "forge" => GameInstanceState.Forge,
                "neoforge" => GameInstanceState.NeoForge,
                "quilt" => GameInstanceState.Quilt,
                "liteloader" => GameInstanceState.LiteLoader,
                "legacyfabric" => GameInstanceState.LegacyFabric,
                "cleanroom" => GameInstanceState.Cleanroom,
                "labymod" => GameInstanceState.LabyMod,
                _ => GameInstanceState.Original,
            };
        }

        return versionType switch
        {
            "snapshot" => GameInstanceState.Snapshot,
            "old_alpha" or "old_beta" => GameInstanceState.Old,
            _ => GameInstanceState.Original,
        };
    }

    /// <summary>Compute SHA-256 of a file's content, returning lowercase hex.</summary>
    private static async Task<string> _ComputeFileSha256Async(string filePath, CancellationToken ct)
    {
        await using var fs = new FileStream(
            filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 65536, FileOptions.Asynchronous);
        var hash = await SHA256Provider.Instance.ComputeHashAsync(fs, ct).ConfigureAwait(false);
        return hash.ToHexString();
    }

    /// <summary>Compute SHA-256 of a byte array, returning lowercase hex.</summary>
    private static string _ComputeSha256(byte[] data)
    {
        var hash = SHA256.HashData(data);
        return hash.ToHexString();
    }

    /// <summary>Returns the first non-null, non-empty string.</summary>
    private static string _Coalesce(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;
}
