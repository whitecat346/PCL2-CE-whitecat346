using PCL.Core.IO.Storage.Cache.Model;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PCL.Core.Minecraft.Instance;

public record GameInstance
{
    public string Path { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public GameInstanceState State { get; init; }
    public int CardType { get; init; }
    public bool IsStarred { get; init; }

    public IReadOnlyList<LoaderEntry> Loaders { get; init; } = [];
    public bool HasLoader(string type) => Loaders.Any(l => l.Type == type);
    public string? GetLoaderVersion(string type) => Loaders.FirstOrDefault(l => l.Type == type)?.Version;

    public string VanillaName { get; init; } = string.Empty;
    public string VanillaVersion { get; init; } = string.Empty;
    public int Drop { get; init; }

    public string MainClass { get; init; } = string.Empty;
    public string? AssetsIndex { get; init; }
    public int? JavaVersion { get; init; }

    public string? Logo { get; init; }
    public string? Description { get; init; }
    public DateTimeOffset? ReleaseTime { get; init; }
    public string IndiePath { get; init; } = string.Empty;

    public static GameInstance FromCache(InstanceCacheRow row, string indiePath) =>
        new()
        {
            Path = row.InstancePath,
            Name = row.InstanceName,
            State = Enum.Parse<GameInstanceState>(row.InstanceState),
            CardType = row.CardType,
            IsStarred = row.IsStarred,
            Loaders = row.GetLoaders(),
            VanillaName = row.VanillaName ?? string.Empty,
            VanillaVersion = row.VanillaVersion ?? string.Empty,
            Drop = row.DropNumber,
            MainClass = row.MainClass ?? string.Empty,
            AssetsIndex = row.AssetsIndex,
            JavaVersion = row.JavaVersion,
            Logo = row.Logo,
            Description = row.Description,
            ReleaseTime = string.IsNullOrEmpty(row.ReleaseTime)
                ? DateTimeOffset.MinValue
                : DateTimeOffset.Parse(row.ReleaseTime),
            IndiePath = indiePath,

        };
}

public enum GameInstanceState
{
    Error,
    Original,
    Snapshot,
    Fool,
    OptiFine,
    Old,
    Forge,
    NeoForge,
    LiteLoader,
    Fabric,
    LegacyFabric,
    Quilt,
    Cleanroom,
    LabyMod
}

public static class GameInstanceLoaders
{
    public const string Forge = "Forge";
    public const string NeoForge = "NeoForge";
    public const string OptiFine = "OptiFine";
    public const string LiteLoader = "LiteLoader";
    public const string Fabric = "Fabric";
    public const string LegacyFabric = "LegacyFabric";
    public const string Quilt = "Quilt";
    public const string Cleanroom = "Cleanroom";
    public const string LabyMod = "LabyMod";
}