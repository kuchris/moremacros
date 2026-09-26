using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using MoreMacros.Core;
using Slot = FFXIVClientStructs.FFXIV.Client.UI.Misc.RaptureHotbarModule.HotbarSlot;
using SlotType = FFXIVClientStructs.FFXIV.Client.UI.Misc.RaptureHotbarModule.HotbarSlotType;

namespace MoreMacros;

internal static unsafe class NativeMacroIcons
{
    public static uint? Resolve(MacroEntry entry) => Resolve(UIModule.Instance(), entry);

    internal static uint? Resolve(UIModule* ui, MacroEntry entry)
    {
        if (ui == null || !MacroIconProvider.HasCommand(entry.Content)) return null;
        var lines = AutoTranslate.EncodeLines(entry);
        // The native resolver takes a module plus set/index. Give it plugin-owned
        // storage, never the game's Individual or Shared banks. The current client
        // reads only the selected macro from this object; services come from ui.
        var scratch = (RaptureMacroModule*)NativeMemory.AllocZeroed((nuint)sizeof(RaptureMacroModule));
        var macro = (RaptureMacroModule.Macro*)Unsafe.AsPointer(ref scratch->Individual[0]);
        var constructed = 0;
        var nameConstructed = false;
        try
        {
            macro->Name.Ctor(); nameConstructed = true;
            for (var i = 0; i < 15; i++) { macro->Lines[i].Ctor(); constructed++; }
            macro->Name.SetString(entry.Name);
            macro->IconId = entry.IconId;
            for (var i = 0; i < lines.Length; i++) macro->Lines[i].SetString(lines[i]);
            SlotType type = SlotType.Empty;
            uint rowId = 0, itemId = 0;
            if (!scratch->TryResolveMacroIcon(ui, &type, &rowId, 0, 0, &itemId)
                || type is SlotType.Empty or SlotType.Macro || rowId == 0) return null;
            var slot = new Slot { CommandType = type, CommandId = rowId, ApparentSlotType = type,
                ApparentActionId = rowId, OriginalApparentSlotType = type, OriginalApparentActionId = rowId, RecipeItemId = itemId };
            var icon = slot.GetIconIdForSlot(type, rowId);
            return icon > 0 ? (uint)icon : null;
        }
        finally
        {
            for (var i = 0; i < constructed; i++) macro->Lines[i].Dtor();
            if (nameConstructed) macro->Name.Dtor();
            NativeMemory.Free(scratch);
        }
    }
}
