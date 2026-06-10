using PCL.Core.App;
using PCL.Core.App.IoC;
using PCL.Core.App.Localization;
using PCL.Core.IO;
using PCL.Core.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PCL.Core.Minecraft.Folder;

[LifecycleScope("game-folder-manager", "游戏文件夹管理", true)]
[LifecycleService(LifecycleState.Running, Priority = 200)]
public partial class GameFolderManager
{
    private const string ModelName = "GameFolderManager";

    private static readonly HashSet<GameFolder> _GameFolders = [];
    public static IReadOnlyList<GameFolder> GameFolders => _GameFolders.ToList();

    public static GameFolder CurrentFolder
    {
        get => field ?? GameFolder.Empty;
        set;
    }

    [LifecycleStart]
    private static async Task _StartAsync()
    {
        await _ReadCustomAsync().ConfigureAwait(false);
        _ReadOriginal();
        _WriteToConfig();

        foreach (var folder in _GameFolders)
        {
            await _WriteOrCreateLauncherProfileAsync(folder.Location).ConfigureAwait(false);
        }

        Context.DeclareStopped();
    }

    private static async Task _ReadCustomAsync()
    {
        var targetFolders = States.Game.Folders.Split('|');
        foreach (var targetFolder in targetFolders)
        {
            if (string.IsNullOrEmpty(targetFolder))
            {
                continue;
            }

            var spilited = targetFolder.Split('>');
            if (targetFolder is not [.., '\\'] || spilited.Length == 0)
            {
                HintWrapper.Show(Lang.Text("Select.Folder.Invalid", targetFolder), HintTheme.Error);
                continue;
            }

            var name = spilited[0];
            var path = spilited[1];

            try
            {
                await Directories.CheckPermissionWithExceptionAsync(path).ConfigureAwait(false);
                _GameFolders.Add(new GameFolder(path, name, GameFolderType.Custom));
            }
            catch (Exception ex)
            {
                MsgBoxWrapper.Show(
                    $"""
                    {Lang.Text("Select.Folder.Invalid", targetFolder)}
                    
                    {ex.Message}
                    """,
                    Lang.Text("Select.Folder.Invalid.Title"),
                    MsgBoxTheme.Warning
                );
                Context.Error($"Failed to read custom game folder: {targetFolder}", ex);
            }
        }
    }

    /// <summary>
    /// Read the official Minecraft folder and current folder (PCL2-CE)
    /// </summary>
    /// <returns></returns>
    private static void _ReadOriginal()
    {
        var hasMcFolder = false;

        try
        {
            var selfFolder = Path.Combine(Basics.ExecutablePath, "versions");
            if (Directory.Exists(selfFolder))
            {
                _GameFolders.Add(new GameFolder(selfFolder, Lang.Text("Select.Folder.CurrentFolder"), GameFolderType.Original));
                hasMcFolder = true;
            }

            var dirs = Directory.GetDirectories(Basics.ExecutablePath);
            foreach (var dir in dirs)
            {
                var dirName = Path.GetDirectoryName(dir) ?? throw new InvalidOperationException($"Failed to get folder name: {dir}");
                if (dirName.Equals(".minecraft", StringComparison.Ordinal) || dirName.Equals(Path.Combine(dir, "versions"), StringComparison.Ordinal))
                {
                    hasMcFolder = true;
                    _GameFolders.Add(new GameFolder(dir, dirName, GameFolderType.Original));
                }
            }
        }
        catch (Exception ex)
        {
            Context.Error("Failed to search current game folders", ex);
        }

        // official folder
        try
        {
            var mojangPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                ".minecraft"
                );

            if (hasMcFolder && Directory.Exists(mojangPath))
            {
                _GameFolders.Add(new GameFolder(mojangPath, Lang.Text("Select.Folder.OfficialLauncherFolder"), GameFolderType.Original));
            }
        }
        catch (Exception ex)
        {
            Context.Error("Failed to search official game folder", ex);
        }
    }

    private static void _WriteToConfig()
    {
        var config = _GameFolders.Select(gameFolder => $"{gameFolder.Name}>{gameFolder.Location}").ToList();

        if (config.Count == 0)
        {
            config.Add(string.Empty);
        }

        States.Game.Folders = string.Join('|', config);

        if (_GameFolders.Count != 0) return;

        // create a new folder if no exist folder
        var newPath = Path.Combine(Basics.ExecutablePath, ".minecraft");
        Directory.CreateDirectory(Path.Combine(newPath, "versions"));
        _GameFolders.Add(new GameFolder(newPath, Lang.Text("Select.Folder.CurrentFolder"), GameFolderType.Original));
    }

    private static Task _WriteOrCreateLauncherProfileAsync(string dir)
    {
        var enetity = new LauncherProfilesEntity(dir);
        return enetity.CreateDefaultProfileAsync();
    }
}