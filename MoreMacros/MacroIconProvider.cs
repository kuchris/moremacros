using MoreMacros.Core;

namespace MoreMacros;

internal sealed class MacroIconProvider
{
    private readonly Dictionary<Guid, uint> resolved = [];

    // Filled on the framework thread; draw callbacks only read the resolved IDs.
    public void Refresh(MacroLibrary library, Func<MacroEntry, uint?> resolve)
    {
        resolved.Clear();
        foreach (var entry in library.Pages.SelectMany(p => p.Macros))
            if (HasCommand(entry.Content) && resolve(entry) is uint icon && icon > 0) resolved[entry.Id] = icon;
    }

    public uint Get(MacroEntry entry) => resolved.GetValueOrDefault(entry.Id, entry.IconId);

    public static bool HasCommand(string content) => MacroRules.Lines(content).Any(line =>
    {
        var command = line.TrimStart().Split(' ', 2)[0];
        return command.Equals("/micon", StringComparison.OrdinalIgnoreCase)
            || command.Equals("/macroicon", StringComparison.OrdinalIgnoreCase);
    });
}
