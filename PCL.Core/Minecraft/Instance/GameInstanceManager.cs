using PCL.Core.App.IoC;
using PCL.Core.IO.Storage.Cache;
using PCL.Core.IO.Storage.Cache.Model;
using PCL.Core.Minecraft.Folder;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PCL.Core.Minecraft.Instance;

/// <summary>
/// Manages CRUD operations for <see cref="GameInstance"/> objects backed by <see cref="CacheService"/>.
/// Provides instance state management and refresh/reload capability.
/// </summary>
[LifecycleScope("instances-manager", "游戏实例管理")]
[LifecycleService(LifecycleState.Running, Priority = 100)]
public sealed partial class GameInstanceManager
{
    private static CacheService Cache => CacheServiceManager.Current;
    private static readonly GameInstanceLoader _Loader = new(Cache);

    /// <summary>
    /// 当前选中的实例（如果有）。这个属性不参与缓存，仅在运行时内存中维护。
    /// </summary>
    public static GameInstance? CurrentSelectedInstance
    {
        get => field;
        set
        {
            field = value;

            // TODO: need to trigger comp manager update to clear old instance's components and load new instance's components
        }
    } = null;

    [LifecycleStart]
    private static void _Start()
    {
        Context.Debug("GameInstanceManager initialized.");
    }

    /// <summary>
    /// Gets a single instance by its indie path identifier.
    /// </summary>
    /// <returns>
    /// Returns <see langword="null"/> if the instance is not found in cache or cannot be loaded.
    /// </returns>
    // ReSharper disable once FlagArgument
    public static async Task<GameInstance?> GetInstanceAsync(string indiePath, bool forceUpdate = false, CancellationToken ct = default)
    {
        try
        {
            if (forceUpdate)
            {
                var instance = await _Loader.LoadInstanceAsync(indiePath, ct).ConfigureAwait(false);
                if (instance is not null)
                {
                    await UpsertInstanceAsync(instance, ct).ConfigureAwait(false);
                }
                return instance;
            }

            var row = await Cache.LookupInstanceAsync(indiePath, ct).ConfigureAwait(false);
            return row is not null ? GameInstance.FromCache(row, indiePath) : await _Loader.LoadInstanceAsync(indiePath, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Context.Error($"Failed to get instance '{indiePath}'", ex);
            return null;
        }
    }

    /// <summary>
    /// Gets all cached instances.
    /// Returns an empty list if no instances are cached or an error occurs.
    /// </summary>
    public static async Task<IReadOnlyList<GameInstance>> GetAllInstancesAsync(CancellationToken ct = default)
    {
        try
        {
            var rows = await Cache.GetAllInstancesAsync(ct).ConfigureAwait(false);
            return rows.Select(r => GameInstance.FromCache(r, r.InstancePath)).ToList();
        }
        catch (Exception ex)
        {
            Context.Error("Failed to get all instances", ex);
            return [];
        }
    }

    public static async Task<IReadOnlyList<GameInstance>> SeekInstancesAsync(CancellationToken ct = default)
    {
        var folders = GameFolderManager.GameFolders.Select(f => Path.Combine(f.Location, "versions"));
        var instances = new List<GameInstance>();
        foreach (var folder in folders)
        {
            var instance = await GetInstanceAsync(folder, true, ct).ConfigureAwait(false);
            if (instance is not null)
            {
                instances.Add(instance);
            }
        }

        return instances;
    }

    /// <summary>
    /// Creates or updates an instance in the cache.
    /// </summary>
    public static async Task UpsertInstanceAsync(GameInstance instance, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        try
        {
            var row = _ToCacheRow(instance);
            await Cache.UpsertInstanceAsync(row, ct).ConfigureAwait(false);
            Context.Trace($"Instance '{instance.IndiePath}' upserted successfully.");
        }
        catch (Exception ex)
        {
            Context.Error($"Failed to upsert instance '{instance.IndiePath}'", ex);
            throw;
        }
    }

    /// <summary>
    /// Deletes an instance from the cache by its indie path.
    /// No-op if the instance does not exist.
    /// </summary>
    public static async Task DeleteInstanceAsync(string indiePath, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(indiePath)) return;

        try
        {
            await Cache.DeleteInstanceAsync(indiePath, ct).ConfigureAwait(false);
            Context.Trace($"Instance '{indiePath}' deleted from cache.");
        }
        catch (Exception ex)
        {
            Context.Error($"Failed to delete instance '{indiePath}'", ex);
        }
    }

    /// <summary>
    /// Updates the state of an instance by its indie path.
    /// No-op if the instance is not found in cache.
    /// </summary>
    public static async Task SetInstanceStateAsync(string indiePath, GameInstanceState state, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(indiePath)) return;

        try
        {
            var row = await Cache.LookupInstanceAsync(indiePath, ct).ConfigureAwait(false);
            if (row is null)
            {
                Context.Warn($"Cannot set state for unknown instance '{indiePath}'");
                return;
            }

            var updated = row with { InstanceState = state.ToString() };
            await Cache.UpsertInstanceAsync(updated, ct).ConfigureAwait(false);
            Context.Trace($"Instance '{indiePath}' state set to {state}.");
        }
        catch (Exception ex)
        {
            Context.Error($"Failed to set state for instance '{indiePath}'", ex);
            throw;
        }
    }

    /// <summary>
    /// Forces a reload of an instance by clearing the cache entry and re-fetching.
    /// Returns the refreshed instance, or <see langword="null"/> if it no longer exists or cannot be loaded.
    /// </summary>
    public static async Task<GameInstance?> RefreshInstanceAsync(string indiePath, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(indiePath)) return null;

        try
        {
            // Remove the cached entry so the next lookup reads fresh data
            await Cache.DeleteInstanceAsync(indiePath, ct).ConfigureAwait(false);

            // Re-fetch (will return null if the instance no longer exists on disk)
            return await GetInstanceAsync(indiePath, false, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Context.Error($"Failed to refresh instance '{indiePath}'", ex);
            return null;
        }
    }

    /// <summary>
    /// Gets all instances matching a specific <see cref="GameInstanceState"/>.
    /// Returns an empty list if no instances match or an error occurs.
    /// </summary>
    public static async Task<IReadOnlyList<GameInstance>> GetInstancesByStateAsync(GameInstanceState state, CancellationToken ct = default)
    {
        try
        {
            var all = await GetAllInstancesAsync(ct).ConfigureAwait(false);
            return all.Where(i => i.State == state).ToList();
        }
        catch (Exception ex)
        {
            Context.Error($"Failed to get instances by state '{state}'", ex);
            return [];
        }
    }

    /// <summary>
    /// Converts a <see cref="GameInstance"/> model to an <see cref="InstanceCacheRow"/> for cache storage.
    /// </summary>
    private static InstanceCacheRow _ToCacheRow(GameInstance instance)
    {
        var loaders = instance.Loaders
            .Select(l => new LoaderEntry { Type = l.Type, Version = l.Version })
            .ToList();

        var row = new InstanceCacheRow
        {
            InstancePath = instance.IndiePath,
            InstanceName = instance.Name,
            InstanceState = instance.State.ToString(),
            CardType = instance.CardType,
            IsStarred = instance.IsStarred,
            Logo = instance.Logo,
            Description = instance.Description,
            ReleaseTime = instance.ReleaseTime?.ToString("O"),
            VanillaName = instance.VanillaName,
            VanillaVersion = instance.VanillaVersion,
            DropNumber = instance.Drop,
            MainClass = instance.MainClass,
            AssetsIndex = instance.AssetsIndex,
            JavaVersion = instance.JavaVersion,
        };
        row.SetLoaders(loaders);
        return row;
    }
}
