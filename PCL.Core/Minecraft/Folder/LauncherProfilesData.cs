using System.Collections.Generic;

namespace PCL.Core.Minecraft.Folder;

public record LauncherProfilesData
{
    public Dictionary<string, ProfileEntry>? Profiles { get; set; }
    public string? SelectedProfile { get; set; }
    public string? ClientToken { get; set; }
    public Dictionary<string, AccountEntry>? AuthenticationDatabase { get; set; }
    public SelectedUserEntry? SelectedUser { get; set; }
}

public record ProfileEntry
{
    public string? Name { get; set; }
    public string? Icon { get; set; }
    public string? LastVersionId { get; set; }
    public string? Type { get; set; }
    public string? LastUsed { get; set; }
}

public record AccountEntry
{
    public string? UserName { get; set; }
    public Dictionary<string, ProfileDisplayEntry>? Profiles { get; set; }
}

public record ProfileDisplayEntry
{
    public string? DisplayName { get; set; }
}

public record SelectedUserEntry
{
    public string? Account { get; set; }
    public string? Profile { get; set; }
}