namespace PCL.Core.Minecraft.Folder;

public record GameFolder(
    string Location,
    string Name,
    GameFolderType Type
)
{
    /// <inheritdoc />
    public override string ToString() => Location;

    /// <inheritdoc />
    public override int GetHashCode() => Location.GetHashCode();
}

public enum GameFolderType
{
    Original,
    RenamedOriginal,
    Custom
}