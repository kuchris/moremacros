using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace MoreMacros;

internal static unsafe class HotbarWriter
{
    public static void WriteCurrent(RaptureHotbarModule* module, uint bar, uint slot,
        RaptureHotbarModule.HotbarSlotType type, uint id)
    {
        // Despite its binding name, the last argument selects a PvE/PvP bank.
        // A selector different from IsInPvPArea writes ScratchSlot and saved data
        // only. Always select the current area so the live cell changes as well.
        module->SetAndSaveSlot(bar, slot, type, id, ignoreSharedHotbars: false,
            allowSaveToPvP: GameMain.IsInPvPArea());
    }
}
