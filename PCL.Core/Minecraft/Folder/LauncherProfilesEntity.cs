using PCL.Core.Logging;
using PCL.Core.Utils.Exts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace PCL.Core.Minecraft.Folder;

public class LauncherProfilesEntity(string folderPath)
{
    private static readonly JsonSerializerOptions _JsonOpt = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    public async Task<LauncherProfilesData> LoadProfilesAsync(CancellationToken ct = default)
    {
        var path = _GetProfilePath();

        var content = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        var dto = JsonSerializer.Deserialize<LauncherProfilesData>(content, _JsonOpt);
        ArgumentNullException.ThrowIfNull(dto);
        return dto;
    }

    public Task SaveProfilesAsync(LauncherProfilesData data, CancellationToken ct = default)
    {
        var path = _GetProfilePath();
        var content = JsonSerializer.Serialize(data, _JsonOpt);
        return File.WriteAllTextAsync(path, content, ct);
    }

    private string _GetProfilePath() =>
    folderPath.EndsWithF(".json") ? folderPath : Path.Combine(folderPath, "launcher_profiles.json");

    public async Task CreateDefaultProfileAsync()
    {
        try
        {

            var now = DateTime.Now;
            var data = new LauncherProfilesData
            {
                Profiles = new Dictionary<string, ProfileEntry>
                {
                    ["PCL"] = new()
                    {
                        Icon = "Grass",
                        Name = "PCL",
                        LastVersionId = "latest-release",
                        Type = "latest-release",
                        LastUsed = now.ToString("O")
                    }
                },
                SelectedProfile = "PCL",
                ClientToken = "23323323323323323323323323323333"
            };

            await SaveProfilesAsync(data, CancellationToken.None).ConfigureAwait(false);

            LogWrapper.Info("Successfully created launcher_profiles.json");
        }
        catch (Exception ex)
        {
            LogWrapper.Error(ex, "Failed to create or write launcher profile");
        }
    }
}