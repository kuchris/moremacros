using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using MoreMacros;
using Hotbar = FFXIVClientStructs.FFXIV.Client.UI.Misc.RaptureHotbarModule;
using SlotType = FFXIVClientStructs.FFXIV.Client.UI.Misc.RaptureHotbarModule.HotbarSlotType;
using Utf8String = FFXIVClientStructs.FFXIV.Client.System.String.Utf8String;
using UIModule = FFXIVClientStructs.FFXIV.Client.UI.UIModule;

internal static unsafe class Program
{
    private static byte areaPvp;
    private static byte lastPvp;
    private static byte lastIgnoreShared;
    private static int writes;
    private static int stringsAlive;
    private static bool sawEncodedMacro;
    private static bool resolveIcon = true;
    private static int iconLookups;
    private static byte[] expectedIconLine = [];

    private static int Main(string[] args)
    {
        var gameDataIndex = Array.IndexOf(args, "--game-data");
        if (gameDataIndex >= 0)
        {
            if (gameDataIndex + 1 >= args.Length || !Directory.Exists(args[gameDataIndex + 1]))
            {
                Console.Error.WriteLine("Usage: --game-data <path to game/sqpack>");
                return 2;
            }
            using var data = new Lumina.GameData(args[gameDataIndex + 1]);
            var row = data.Excel.GetSheet<Lumina.Excel.Sheets.Action>(Lumina.Data.Language.English).GetRow(7439);
            Console.WriteLine($"Earthly Star: Action row {row.RowId}, IconId {row.Icon}");
            return 0;
        }
        // Only this standalone process's function pointers are replaced. No game
        // process is opened. The real FFXIVClientStructs call exposes omitted defaults.
        Hotbar.Addresses.SetAndSaveSlot.Value = (nint)(delegate* unmanaged<Hotbar*, uint, uint, byte, uint, byte, byte, void>)&Save;
        GameMain.Addresses.IsInPvPArea.Value = (nint)(delegate* unmanaged<byte>)&IsInPvp;
        var module = (Hotbar*)NativeMemory.AllocZeroed((nuint)sizeof(Hotbar));
        var failed = 0;
        try
        {
            Utf8String.Addresses.Ctor.Value = (nint)(delegate* unmanaged<Utf8String*, Utf8String*>)&StringCtor;
            Utf8String.Addresses.Dtor.Value = (nint)(delegate* unmanaged<Utf8String*, void>)&StringDtor;
            Utf8String.Addresses.SetString.Value = (nint)(delegate* unmanaged<Utf8String*, byte*, void>)&StringSet;
            RaptureMacroModule.Addresses.TryResolveMacroIcon.Value = (nint)(delegate* unmanaged<RaptureMacroModule*, UIModule*, byte*, uint*, int, uint, uint*, byte>)&ResolveIcon;
            Hotbar.HotbarSlot.Addresses.GetIconIdForSlot.Value = (nint)(delegate* unmanaged<Hotbar.HotbarSlot*, byte, uint, int>)&IconId;
            var at = new MoreMacros.Core.AutoTranslatePhrase(56, 7439, "Earthly Star");
            var iconEntry = new MoreMacros.Core.MacroEntry { Content = "/micon " + at.Token, AutoTranslate = [at] };
            expectedIconLine = "/micon "u8.ToArray().Concat(new Lumina.Text.SeStringBuilder().BeginMacro(Lumina.Text.Payloads.MacroCode.Fixed)
                .AppendUIntExpression(55).AppendUIntExpression(7439).EndMacro().ToArray()).ToArray();
            Check(NativeMacroIcons.Resolve((UIModule*)1, iconEntry) == 3143 && sawEncodedMacro && writes == 0,
                "Icon resolver receives the real Earthly Star payload in plugin-owned storage without hotbar writes");
            Check(stringsAlive == 0 && iconLookups == 1, "Temporary icon strings are released after resolution");
            resolveIcon = false;
            Check(NativeMacroIcons.Resolve((UIModule*)1, iconEntry) is null && stringsAlive == 0 && iconLookups == 1,
                "Invalid /micon releases scratch storage and does not request an unrelated icon");
            var node = new FFXIVClientStructs.FFXIV.Component.GUI.AtkResNode { ScreenX = 2353, ScreenY = 507 };
            var addon = new FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase { X = 2345, Y = 508, RootNode = &node };
            Check(NativeWindowPosition.Read(&addon) == new System.Numerics.Vector2(2353, 507),
                "Overlay follows the drawn position during drag (captured live frame)");
            var phrase = new MoreMacros.Core.AutoTranslatePhrase(2, 203, "Hello!");
            var encoded = MoreMacros.Core.AutoTranslate.EncodeLines(new() { Content = phrase.Token, AutoTranslate = [phrase] })[0];
            var gameString = new Lumina.Text.SeStringBuilder().BeginMacro(Lumina.Text.Payloads.MacroCode.Fixed)
                .AppendUIntExpression(1).AppendUIntExpression(203).EndMacro().ToArray();
            Check(encoded.SequenceEqual(gameString), "Completion catalogue Hello! encodes identically to Lumina's game payload");
            var appearance = new Hotbar.HotbarUIIntermediate { DrawAnts = true, IsTransformationActionUsable = true };
            HotbarAppearance.Apply(&appearance, true);
            Check(!appearance.DrawAnts && !appearance.IsTransformationActionUsable, "Extra macros have no skill glow or pulse");
            Check(appearance.ActionAvailable1 && appearance.ActionAvailable2, "Removing animation keeps macros executable");
            foreach (var mode in new byte[] { 0, 1 })
            {
                areaPvp = mode;
                module->PvPHotbarsActive = mode != 0;
                var slot = (Hotbar.HotbarSlot*)Unsafe.AsPointer(ref module->Hotbars[4].Slots[0]);
                *slot = default;
                writes = 0;
                HotbarWriter.WriteCurrent(module, 4, 0, SlotType.GeneralAction, 0x4D000001);
                Check(slot->CommandType == SlotType.GeneralAction && slot->CommandId == 0x4D000001,
                    $"{(mode == 0 ? "PvE" : "PvP")} placement updates the live cell (API received PvP={lastPvp})");
                Check(lastPvp == mode && lastIgnoreShared == 0 && writes == 1, "Save targets the current mode and respects shared bars");
                slot->CommandType = SlotType.GeneralAction;
                slot->CommandId = 0x4D000001;
                HotbarWriter.WriteCurrent(module, 4, 0, SlotType.Empty, 0);
                Check(slot->CommandType == SlotType.Empty && slot->CommandId == 0, "Removal clears the live cell in the same mode");
            }
        }
        finally { NativeMemory.Free(module); }
        return failed == 0 ? 0 : 1;

        void Check(bool success, string title)
        {
            Console.WriteLine((success ? "PASS " : "FAIL ") + title);
            if (!success) failed++;
        }
    }

    [UnmanagedCallersOnly]
    private static byte IsInPvp() => areaPvp;

    [UnmanagedCallersOnly]
    private static Utf8String* StringCtor(Utf8String* value)
    {
        *value = default;
        value->StringPtr = (byte*)NativeMemory.AllocZeroed(1);
        value->BufSize = value->BufUsed = 1;
        value->IsEmpty = true;
        stringsAlive++;
        return value;
    }

    [UnmanagedCallersOnly]
    private static void StringSet(Utf8String* value, byte* text)
    {
        var span = MemoryMarshal.CreateReadOnlySpanFromNullTerminated(text);
        NativeMemory.Free(value->StringPtr);
        value->StringPtr = (byte*)NativeMemory.AllocZeroed((nuint)(span.Length+1));
        span.CopyTo(new Span<byte>(value->StringPtr, span.Length));
        value->BufSize = value->BufUsed = span.Length+1;
        value->StringLength = span.Length;
        value->IsEmpty = span.Length == 0;
    }

    [UnmanagedCallersOnly]
    private static void StringDtor(Utf8String* value)
    {
        NativeMemory.Free(value->StringPtr);
        *value = default;
        stringsAlive--;
    }

    [UnmanagedCallersOnly]
    private static byte ResolveIcon(RaptureMacroModule* scratch, UIModule* ui, byte* type, uint* row, int set, uint index, uint* item)
    {
        sawEncodedMacro = set == 0 && index == 0 && scratch->Individual[0].Lines[0].AsSpan().SequenceEqual(expectedIconLine);
        if (!resolveIcon) return 0;
        *type = (byte)SlotType.Action; *row = 7439; *item = 0;
        return 1;
    }

    [UnmanagedCallersOnly]
    private static int IconId(Hotbar.HotbarSlot* slot, byte type, uint id)
    {
        iconLookups++;
        return type == (byte)SlotType.Action && id == 7439 ? 3143 : 0;
    }

    [UnmanagedCallersOnly]
    private static void Save(Hotbar* module, uint bar, uint slot, byte type, uint id, byte ignoreShared, byte isPvp)
    {
        lastPvp = isPvp;
        lastIgnoreShared = ignoreShared;
        writes++;
        // Captured client behavior: a different PvP selector writes through ScratchSlot
        // and the save path; it does not modify the current live hotbar.
        var destination = isPvp == areaPvp
            ? (Hotbar.HotbarSlot*)Unsafe.AsPointer(ref module->Hotbars[(int)bar].Slots[(int)slot]) : &module->ScratchSlot;
        destination->CommandType = (SlotType)type;
        destination->CommandId = id;
    }
}
