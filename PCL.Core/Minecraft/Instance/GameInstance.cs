using PCL.Core.IO.Storage.Cache.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;

namespace PCL.Core.Minecraft.Instance;

public record GameInstance
{
    private object? _info;
    private JsonObject? _jsonObject;
    private JsonObject? _jsonVersion;
    private string? _inheritInstanceName;
    private bool? _isHmclFormatJson;
    private bool? _isLoaded;

    public GameInstance()
    {
    }

    public GameInstance(string name)
    {
        Path = NormalizeInstancePath(name);
        Name = System.IO.Path.GetFileName(Path.TrimEnd('\\', '/'));
        IndiePath = Path;
    }

    public string Path { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public GameInstanceState State { get; set; }
    public int CardType { get; set; }
    public bool IsStarred { get; set; }

    public IReadOnlyList<LoaderEntry> Loaders { get; set; } = [];
    public bool HasLoader(string type) => Loaders.Any(l => l.Type == type);
    public string? GetLoaderVersion(string type) => Loaders.FirstOrDefault(l => l.Type == type)?.Version;

    public string VanillaName { get; set; } = string.Empty;
    public string VanillaVersion { get; set; } = string.Empty;
    public int Drop { get; set; }

    public string MainClass { get; set; } = string.Empty;
    public string? AssetsIndex { get; set; }
    public int? JavaVersion { get; set; }

    public string? Logo { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset? ReleaseTime { get; set; }
    public string IndiePath { get; set; } = string.Empty;

    /// <summary>
    /// Temporary bridge used while callers are migrated away from the legacy UI instance type.
    /// </summary>
    public object? LegacyInstance { get; set; }

    public string PathInstance
    {
        get => GetLegacyValue<string>(nameof(PathInstance)) ?? EnsureTrailingSlash(Path);
        set => Path = EnsureTrailingSlash(value);
    }

    public string PathIndie
    {
        get => GetLegacyValue<string>(nameof(PathIndie)) ?? EnsureTrailingSlash(IndiePath);
        set => IndiePath = EnsureTrailingSlash(value);
    }

    public string Desc
    {
        get => GetLegacyValue<string>(nameof(Desc)) ?? Description ?? string.Empty;
        set => Description = value;
    }

    public DateTime releaseTime
    {
        get => GetLegacyDateTime(nameof(releaseTime)) ??
               ReleaseTime?.DateTime ??
               new DateTime(1970, 1, 1, 15, 0, 0);
        set => ReleaseTime = value;
    }

    public GameInstanceState state
    {
        get
        {
            var legacy = GetLegacyValue<object>(nameof(state));
            return legacy is null ? State : Enum.Parse<GameInstanceState>(legacy.ToString() ?? nameof(GameInstanceState.Error));
        }
        set => State = value;
    }

    public GameInstanceCardType displayType
    {
        get
        {
            var legacy = GetLegacyValue<object>(nameof(displayType));
            return legacy is null
                ? (GameInstanceCardType)CardType
                : Enum.Parse<GameInstanceCardType>(legacy.ToString() ?? nameof(GameInstanceCardType.Auto));
        }
        set => CardType = (int)value;
    }

    public bool IsStar
    {
        get => GetLegacyBoolean(nameof(IsStar)) ?? IsStarred;
        set => IsStarred = value;
    }

    public bool IsLoaded
    {
        get => GetLegacyBoolean(nameof(IsLoaded)) ?? _isLoaded ?? true;
        set => _isLoaded = value;
    }

    public dynamic Info
    {
        get => GetLegacyValue<object>(nameof(Info)) ?? _info ?? CreateFallbackInfo();
        set => _info = value;
    }

    public dynamic JsonObject
    {
        get => GetLegacyValue<JsonObject>(nameof(JsonObject)) ?? _jsonObject ?? new JsonObject();
        set => _jsonObject = value;
    }

    public dynamic JsonVersion
    {
        get => GetLegacyValue<JsonObject>(nameof(JsonVersion)) ?? _jsonVersion ?? new JsonObject();
        set => _jsonVersion = value;
    }

    public bool IsOldJson => JsonObject["minecraftArguments"] is not null &&
                             (string?)JsonObject["minecraftArguments"] != string.Empty;

    public bool IsHmclFormatJson
    {
        get => GetLegacyBoolean(nameof(IsHmclFormatJson)) ?? _isHmclFormatJson ?? false;
        set => _isHmclFormatJson = value;
    }

    public string InheritInstanceName
    {
        get => GetLegacyValue<string>(nameof(InheritInstanceName)) ?? _inheritInstanceName ?? string.Empty;
        set => _inheritInstanceName = value;
    }

    public bool Modable
    {
        get
        {
            var legacy = GetLegacyBoolean(nameof(Modable));
            if (legacy.HasValue)
                return legacy.Value;
            return HasLoader(GameInstanceLoaders.Fabric) ||
                   HasLoader(GameInstanceLoaders.LegacyFabric) ||
                   HasLoader(GameInstanceLoaders.Quilt) ||
                   HasLoader(GameInstanceLoaders.Forge) ||
                   HasLoader(GameInstanceLoaders.LiteLoader) ||
                   HasLoader(GameInstanceLoaders.NeoForge) ||
                   HasLoader(GameInstanceLoaders.Cleanroom) ||
                   displayType == GameInstanceCardType.API;
        }
    }

    public bool Check()
    {
        var result = InvokeLegacy(nameof(Check));
        if (result is bool checkedResult)
        {
            SyncFromLegacy();
            return checkedResult;
        }

        if (string.IsNullOrEmpty(PathInstance) || !System.IO.Directory.Exists(PathInstance))
        {
            state = GameInstanceState.Error;
            return false;
        }

        state = GameInstanceState.Original;
        return true;
    }

    public GameInstance Load()
    {
        InvokeLegacy(nameof(Load));
        SyncFromLegacy();
        IsLoaded = true;
        return this;
    }

    public string GetDefaultDescription()
    {
        return InvokeLegacy(nameof(GetDefaultDescription)) as string ??
               Description ??
               VanillaName;
    }

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

    public static string NormalizeInstancePath(string name)
    {
        var path = name.Contains(':')
            ? name
            : System.IO.Path.Combine(Folder.GameFolderManager.CurrentFolder.Location, "versions", name);
        return EnsureTrailingSlash(path);
    }

    public static string EnsureTrailingSlash(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;
        return path.EndsWith('\\') || path.EndsWith('/') ? path : path + "\\";
    }

    private object CreateFallbackInfo() =>
        new
        {
            VanillaName,
            vanilla = Version.TryParse(VanillaVersion, out var parsed) ? parsed : new Version(9999, 0, 0),
            Drop,
            Reliable = true,
            Valid = Drop > 0,
            HasForge = HasLoader(GameInstanceLoaders.Forge),
            Forge = GetLoaderVersion(GameInstanceLoaders.Forge) ?? string.Empty,
            HasNeoForge = HasLoader(GameInstanceLoaders.NeoForge),
            NeoForge = GetLoaderVersion(GameInstanceLoaders.NeoForge) ?? string.Empty,
            HasCleanroom = HasLoader(GameInstanceLoaders.Cleanroom),
            Cleanroom = GetLoaderVersion(GameInstanceLoaders.Cleanroom) ?? string.Empty,
            HasFabric = HasLoader(GameInstanceLoaders.Fabric),
            Fabric = GetLoaderVersion(GameInstanceLoaders.Fabric) ?? string.Empty,
            HasLegacyFabric = HasLoader(GameInstanceLoaders.LegacyFabric),
            LegacyFabric = GetLoaderVersion(GameInstanceLoaders.LegacyFabric) ?? string.Empty,
            HasQuilt = HasLoader(GameInstanceLoaders.Quilt),
            Quilt = GetLoaderVersion(GameInstanceLoaders.Quilt) ?? string.Empty,
            HasLabyMod = HasLoader(GameInstanceLoaders.LabyMod),
            LabyMod = GetLoaderVersion(GameInstanceLoaders.LabyMod) ?? string.Empty,
            HasOptiFine = HasLoader(GameInstanceLoaders.OptiFine),
            OptiFine = GetLoaderVersion(GameInstanceLoaders.OptiFine) ?? string.Empty,
            HasLiteLoader = HasLoader(GameInstanceLoaders.LiteLoader),
            OptiFineCode = 0,
            ForgelikeCode = 0
        };

    private T? GetLegacyValue<T>(string name)
    {
        if (LegacyInstance is null)
            return default;

        try
        {
            var type = LegacyInstance.GetType();
            var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            var raw = property is not null
                ? property.GetValue(LegacyInstance)
                : type.GetField(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(LegacyInstance);
            if (raw is null)
                return default;
            if (raw is T value)
                return value;
            if (typeof(T).IsEnum)
                return (T)Enum.Parse(typeof(T), raw.ToString() ?? string.Empty);
            return (T)Convert.ChangeType(raw, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T));
        }
        catch
        {
            return default;
        }
    }

    private bool? GetLegacyBoolean(string name)
    {
        var raw = GetLegacyValue<object>(name);
        return raw switch
        {
            bool value => value,
            null => null,
            _ when bool.TryParse(raw.ToString(), out var parsed) => parsed,
            _ => null
        };
    }

    private DateTime? GetLegacyDateTime(string name)
    {
        var raw = GetLegacyValue<object>(name);
        return raw switch
        {
            DateTime value => value,
            DateTimeOffset value => value.DateTime,
            null => null,
            _ when DateTime.TryParse(raw.ToString(), out var parsed) => parsed,
            _ => null
        };
    }

    private object? InvokeLegacy(string name)
    {
        if (LegacyInstance is null)
            return default;

        try
        {
            var method = LegacyInstance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public, []);
            if (method is null)
                return default;
            return method.Invoke(LegacyInstance, null);
        }
        catch
        {
            return default;
        }
    }

    private void SyncFromLegacy()
    {
        if (LegacyInstance is null)
            return;

        Path = GetLegacyValue<string>(nameof(PathInstance)) ?? Path;
        Name = GetLegacyValue<string>(nameof(Name)) ?? Name;
        IndiePath = GetLegacyValue<string>(nameof(PathIndie)) ?? IndiePath;
        Description = GetLegacyValue<string>(nameof(Desc)) ?? Description;
        Logo = GetLegacyValue<string>(nameof(Logo)) ?? Logo;
        IsStarred = GetLegacyBoolean(nameof(IsStar)) ?? IsStarred;
        _isLoaded = GetLegacyBoolean(nameof(IsLoaded)) ?? _isLoaded;
        _info = GetLegacyValue<object>(nameof(Info)) ?? _info;
        _jsonObject = GetLegacyValue<JsonObject>(nameof(JsonObject)) ?? _jsonObject;
        _jsonVersion = GetLegacyValue<JsonObject>(nameof(JsonVersion)) ?? _jsonVersion;
        _inheritInstanceName = GetLegacyValue<string>(nameof(InheritInstanceName)) ?? _inheritInstanceName;
        _isHmclFormatJson = GetLegacyBoolean(nameof(IsHmclFormatJson)) ?? _isHmclFormatJson;
        ReleaseTime = releaseTime;
        State = state;
        CardType = (int)displayType;
    }
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

public enum GameInstanceCardType
{
    Star = -1,
    Auto = 0,
    Hidden = 1,
    API = 2,
    OriginalLike = 3,
    Rubbish = 4,
    Fool = 5,
    Error = 6
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
