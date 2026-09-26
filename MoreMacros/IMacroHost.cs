using Dalamud.Bindings.ImGui;
using System.Numerics;
using MoreMacros.Core;

namespace MoreMacros;

internal interface IMacroHost
{
    MacroLibrary Library { get; }
    string Status { get; }
    string? LoadError { get; }
    bool Save();
    void SetStatus(string message);
    void NativePage(uint set);
    void Execute(MacroEntry entry);
    void Import(uint set);
    void Export();
    ImTextureID? GetIcon(uint iconId);
    uint GetMacroIconId(MacroEntry entry);
    IReadOnlyList<uint> IconChoices { get; }
    IReadOnlyList<AutoTranslatePhrase> AutoTranslatePhrases { get; }
    HotbarDropTarget? GetHotbarTarget(Vector2 gamePosition);
    void DropOnHotbar(MacroEntry entry, HotbarDropTarget target);
    void PlaceOnHotbar(MacroEntry entry, int bar, int slot);
    void RemoveHotbarLink(int bar, int slot);
}
