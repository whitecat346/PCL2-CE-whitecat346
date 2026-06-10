namespace PCL;

/// <summary>
/// UI-only projection for Minecraft instance cards.
/// Core data and launch-time state live in <see cref="GameInstance"/>.
/// </summary>
public sealed record GameInstanceUi(
    GameInstance Instance,
    string Description,
    string? Logo,
    McInstanceCardType DisplayType,
    bool IsStarred)
{
    public string Name => Instance.Name;
    public string PathInstance => Instance.PathInstance;
    public string PathIndie => Instance.PathIndie;

    public static GameInstanceUi FromInstance(GameInstance instance) =>
        new(instance, instance.Desc, instance.Logo, instance.displayType, instance.IsStar);

    public static GameInstanceUi FromLegacy(McInstance instance) =>
        new(instance, instance.Desc, instance.Logo, instance.displayType, instance.IsStar);
}
