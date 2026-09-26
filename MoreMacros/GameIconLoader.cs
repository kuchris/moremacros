using Dalamud.Bindings.ImGui;

namespace MoreMacros;

internal sealed class GameIconLoader(Func<uint, bool, ImTextureID?> load)
{
    private readonly HashSet<(uint Id, bool HiRes)> missing = [];

    public ImTextureID? Get(uint iconId)
    {
        // Texture handles are borrowed for this frame only. Cache failures, never handles.
        return TryLoad(iconId, true) ?? TryLoad(iconId, false);
    }

    private ImTextureID? TryLoad(uint iconId, bool hiRes)
    {
        if (missing.Contains((iconId, hiRes))) return null;
        try { return load(iconId, hiRes); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Missing/invalid textures must not escape the draw callback.
            missing.Add((iconId, hiRes));
            return null;
        }
    }
}
