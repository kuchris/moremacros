namespace MoreMacros.Core;

// Never encode extra entries as native macros: the client clamps invalid macro
// indices back to native macro zero. These IDs instead use the general-action category.
public static class HotbarLinks
{
    public const byte CommandType = 10;
    public const uint Prefix = 0x4D000000;
    public const uint MaxSequence = 0x00FFFFFF;

    public static bool IsShortcut(byte type, uint id) => type == CommandType
        && (id & 0xFF000000) == Prefix && (id & MaxSequence) != 0;

    public static MacroEntry? Resolve(MacroLibrary library, byte type, uint id) => IsShortcut(type, id)
        ? library.Pages.SelectMany(p => p.Macros).FirstOrDefault(m => m.HotbarId == id) : null;

    public static uint Assign(MacroLibrary library, Guid macroId)
    {
        var entry = library.Find(macroId) ?? throw new InvalidOperationException("Saved macro not found.");
        if (entry.Content.Length == 0) throw new InvalidOperationException("Save some macro commands first.");
        if (entry.HotbarId != 0) return entry.HotbarId;
        if (library.NextHotbarId is 0 or > MaxSequence) throw new InvalidOperationException("No more shortcut IDs are available.");
        return entry.HotbarId = Prefix | library.NextHotbarId++;
    }
}
