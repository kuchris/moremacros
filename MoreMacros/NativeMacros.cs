using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Client.UI.Shell;
using MoreMacros.Core;

namespace MoreMacros;

// No pointers to the game's macro slots are retained or written to.
internal static unsafe class NativeMacros
{
    public static bool TryGetWindow(IGameGui gui, out Vector2 position, out float scale)
    {
        var addon = gui.GetAddonByName("Macro");
        position = default;
        scale = 1;
        if (!addon.IsReady || !addon.IsVisible) return false;
        position = NativeWindowPosition.Read((FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase*)addon.Address);
        scale = addon.Scale;
        return float.IsFinite(scale) && scale is >= 0.5f and <= 4;
    }

    public static void Open(uint set = 1)
    {
        var agent = AgentMacro.Instance();
        if (agent == null) throw new InvalidOperationException("The macro window is unavailable. Log in first.");
        agent->OpenMacro(set, 0);
    }

    public static (MacroPage Page, int Skipped) Import(uint set)
    {
        var module = RaptureMacroModule.Instance();
        if (module == null) throw new InvalidOperationException("Game macros are unavailable.");
        var page = new MacroPage { Name = set == 0 ? "Individual copy" : "Shared copy" };
        var skipped = 0;
        for (uint i = 0; i < 100; i++)
        {
            var source = module->GetMacro(set, i);
            if (source == null) continue;
            var lines = new string[15];
            var phrases = new List<AutoTranslatePhrase>();
            try { for (var n = 0; n < 15; n++) lines[n] = NativeAutoTranslate.Decode(source->Lines[n].AsSpan(), phrases); }
            catch (InvalidDataException) { skipped++; continue; }
            var name = source->Name.ToString();
            var content = string.Join('\n', lines).TrimEnd('\n');
            // Only supported payloads are imported; auto-translate retains group/key.
            if (MacroRules.Error(name, content, phrases) is not null) { skipped++; continue; }
            page.Macros[(int)i] = new MacroEntry { Name = name, Content = content, AutoTranslate = phrases, IconId = source->IconId == 0 ? 66001 : source->IconId };
        }
        return (page, skipped);
    }

    public static void Execute(MacroEntry entry)
    {
        var error = MacroRules.Error(entry.Name, entry.Content, entry.AutoTranslate);
        if (error is not null) throw new InvalidOperationException(error);
        if (entry.Content.Length == 0) throw new InvalidOperationException("This macro has no commands.");
        var shell = RaptureShellModule.Instance();
        if (shell == null) throw new InvalidOperationException("The macro runner is unavailable.");
        if (shell->MacroCurrentLine >= 0)
        {
            // A one-line native launcher may still be active on the next framework tick.
            // Only replace that exact launcher; never interrupt an unrelated macro.
            var isLauncher = shell->MacroLines[0].ToString().Trim().Equals(
                $"/moremacros run {entry.Id:D}", StringComparison.OrdinalIgnoreCase);
            for (var i = 1; i < 15; i++)
                isLauncher &= string.IsNullOrWhiteSpace(shell->MacroLines[i].ToString());
            if (!isLauncher || shell->MacroLocked)
                throw new InvalidOperationException("Another macro is running. Wait for it to finish.");
        }

        // Construct strings in their final address: Utf8String contains an inline-buffer pointer.
        var native = (RaptureMacroModule.Macro*)NativeMemory.AllocZeroed((nuint)sizeof(RaptureMacroModule.Macro));
        var constructed = 0;
        var nameConstructed = false;
        try
        {
            native->Name.Ctor();
            nameConstructed = true;
            for (var i = 0; i < 15; i++) { native->Lines[i].Ctor(); constructed++; }
            native->Name.SetString(entry.Name);
            native->IconId = entry.IconId;
            var lines = AutoTranslate.EncodeLines(entry);
            for (var i = 0; i < lines.Length; i++) native->Lines[i].SetString(lines[i]);
            // The game's runner copies these strings into its own execution buffer.
            shell->ExecuteMacro(native);
        }
        finally
        {
            for (var i = 0; i < constructed; i++) native->Lines[i].Dtor();
            if (nameConstructed) native->Name.Dtor();
            NativeMemory.Free(native);
        }
    }
}
