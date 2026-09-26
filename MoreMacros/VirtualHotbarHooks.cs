using System.Runtime.InteropServices;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using MoreMacros.Core;
using Hotbar = FFXIVClientStructs.FFXIV.Client.UI.Misc.RaptureHotbarModule;
using Slot = FFXIVClientStructs.FFXIV.Client.UI.Misc.RaptureHotbarModule.HotbarSlot;
using SlotType = FFXIVClientStructs.FFXIV.Client.UI.Misc.RaptureHotbarModule.HotbarSlotType;
using Intermediate = FFXIVClientStructs.FFXIV.Client.UI.Misc.RaptureHotbarModule.HotbarUIIntermediate;

namespace MoreMacros;

// General-action IDs are full-width during normal hotbar storage and drag/drop.
// The private range never resolves through Individual/Shared macro indices.
internal sealed unsafe class VirtualHotbarHooks : IDisposable
{
    private delegate byte ExecuteDelegate(Hotbar* self, Slot* slot);
    private delegate int IconDelegate(Slot* self, SlotType type, uint id);
    private delegate nint NameDelegate(Slot* self, SlotType type, uint id);
    private delegate byte PopulateDelegate(Hotbar* self, Slot* slot, Intermediate* data);
    private readonly IMacroHost host;
    private readonly Hook<ExecuteDelegate>? execute;
    private readonly Hook<IconDelegate>? icon;
    private readonly Hook<NameDelegate>? name;
    private readonly Hook<PopulateDelegate>? populate;
    private readonly Dictionary<string, nint> names = new();

    public VirtualHotbarHooks(IMacroHost host, IGameInteropProvider interop)
    {
        this.host = host;
        try
        {
            execute = interop.HookFromAddress<ExecuteDelegate>((nint)Hotbar.MemberFunctionPointers.ExecuteSlot, OnExecute);
            icon = interop.HookFromAddress<IconDelegate>((nint)Slot.MemberFunctionPointers.GetIconIdForSlot, OnIcon);
            name = interop.HookFromAddress<NameDelegate>((nint)Slot.MemberFunctionPointers.GetDisplayNameForSlot, OnName);
            populate = interop.HookFromAddress<PopulateDelegate>((nint)Hotbar.MemberFunctionPointers.PopulateIntermediateFromSlot, OnPopulate);
            icon.Enable(); name.Enable(); populate.Enable(); execute.Enable();
        }
        catch { Dispose(); throw; }
    }

    private MacroEntry? Find(SlotType type, uint id) => HotbarLinks.Resolve(host.Library, (byte)type, id);
    private static bool Owned(SlotType type, uint id) => HotbarLinks.IsShortcut((byte)type, id);

    private byte OnExecute(Hotbar* self, Slot* slot)
    {
        if (slot == null || !Owned(slot->CommandType, slot->CommandId)) return execute!.Original(self, slot);
        try
        {
            var entry = Find(slot->CommandType, slot->CommandId);
            if (entry is null) { host.SetStatus("This MoreMacros link no longer has a saved macro."); return 0; }
            host.Execute(entry);
            return 1;
        }
        catch (Exception ex) { Plugin.Log.Error(ex, "MoreMacros hotbar execution failed."); return 0; }
    }

    private int OnIcon(Slot* self, SlotType type, uint id)
    {
        if (!Owned(type, id)) return icon!.Original(self, type, id);
        var entry = Find(type, id);
        return (int)(entry is null ? 66001 : host.GetMacroIconId(entry));
    }

    private nint OnName(Slot* self, SlotType type, uint id)
    {
        if (!Owned(type, id)) return name!.Original(self, type, id);
        var entry = Find(type, id);
        var label = entry is null ? "MoreMacros: missing macro" : string.IsNullOrEmpty(entry.Name) ? "MoreMacros" : entry.Name;
        // Native callers borrow this buffer. Keep it alive until hooks are detached.
        if (!names.TryGetValue(label, out var pointer)) names[label] = pointer = Marshal.StringToCoTaskMemUTF8(label);
        return pointer;
    }

    private byte OnPopulate(Hotbar* self, Slot* slot, Intermediate* data)
    {
        var result = populate!.Original(self, slot, data);
        if (slot == null || data == null || !Owned(slot->CommandType, slot->CommandId)) return result;
        var available = Find(slot->CommandType, slot->CommandId) is { Content.Length: > 0 };
        // The original call retains the native general-action drag payload, keybind
        // label and tooltip. Availability belongs to our saved macro, not a game sheet row.
        HotbarAppearance.Apply(data, available);
        return 0;
    }

    public void Dispose()
    {
        execute?.Dispose(); populate?.Dispose(); name?.Dispose(); icon?.Dispose();
        foreach (var pointer in names.Values) Marshal.FreeCoTaskMem(pointer);
        names.Clear();
    }
}
