using System.Numerics;
using System.Text.Json;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using MoreMacros.Core;

namespace MoreMacros;

internal static unsafe class NativeHotbars
{
    public static HotbarDropTarget? FindTarget(IGameGui gui, Vector2 position)
    {
        foreach (var target in Targets(gui))
            if (position.X >= target.Position.X && position.Y >= target.Position.Y
                && position.X < target.Position.X + target.Size.X && position.Y < target.Position.Y + target.Size.Y)
                return target;
        return null;
    }

    public static HotbarDropTarget? GetTarget(IGameGui gui, int bar, int slot) =>
        Targets(gui).FirstOrDefault(t => t.Bar == bar && t.Slot == slot);

    private static List<HotbarDropTarget> Targets(IGameGui gui)
    {
        var result = new List<HotbarDropTarget>();
        var hotbars = RaptureHotbarModule.Instance();
        if (hotbars == null) return result;
        for (var n = 0; n < 10; n++)
        {
            var addon = gui.GetAddonByName(n == 0 ? "_ActionBar" : $"_ActionBar{n:00}");
            if (!addon.IsReady || !addon.IsVisible) continue;
            var bar = (AddonActionBarBase*)addon.Address;
            if (bar->RaptureHotbarId >= 10 || bar->DisplayPetBar || bar->IsCrossHotbar) continue;
            var count = Math.Min(Math.Min((int)bar->ActionBarSlotVector.LongCount, bar->SlotCount), 12);
            for (var i = 0; i < count; i++)
            {
                var component = bar->ActionBarSlotVector[i].ComponentDragDrop;
                // Empty slots can hide their icon. The containing drag/drop node still
                // describes the slot and is the correct target for an empty-cell drop.
                var node = component == null ? null : component->OwnerNode;
                if (node == null) continue;
                var start = new Vector2(node->ScreenX, node->ScreenY);
                var size = new Vector2(
                    Math.Abs(node->Transform.M11) * node->Width + Math.Abs(node->Transform.M21) * node->Height,
                    Math.Abs(node->Transform.M12) * node->Width + Math.Abs(node->Transform.M22) * node->Height);
                if (size.X <= 0 || size.Y <= 0) continue;
                var slot = hotbars->GetSlotById(bar->RaptureHotbarId, (uint)i);
                var error = bar->IsLocked ? "Unlock your hotbars first."
                    : slot == null || slot->CommandType != RaptureHotbarModule.HotbarSlotType.Empty
                        ? "Drop onto an empty hotbar slot." : null;
                result.Add(new HotbarDropTarget(bar->RaptureHotbarId, (uint)i, start, size, error,
                    hotbars->CharacterContentId, hotbars->ActiveHotbarClassJobId,
                    hotbars->IsHotbarShared(bar->RaptureHotbarId), hotbars->PvPHotbarsActive));
            }
        }
        return result;
    }

    public static string Place(IGameGui gui, MacroEntry entry, HotbarDropTarget target, string backupDirectory)
    {
        var current = GetTarget(gui, (int)target.Bar, (int)target.Slot);
        if (current is null) throw new InvalidOperationException("The hotbar moved or closed. Try again.");
        if (current.CharacterId != target.CharacterId || current.Job != target.Job
            || current.Shared != target.Shared || current.PvP != target.PvP)
            throw new InvalidOperationException("The character or hotbar setup changed. Try again.");
        if (current.Error is not null) throw new InvalidOperationException(current.Error);
        if (!HotbarLinks.IsShortcut(HotbarLinks.CommandType, entry.HotbarId))
            throw new InvalidOperationException("Save the shortcut link first.");
        var hotbars = RaptureHotbarModule.Instance();
        if (hotbars == null || !hotbars->ModuleReady) throw new InvalidOperationException("Hotbars are not ready.");

        Directory.CreateDirectory(backupDirectory);
        var path = Path.Combine(backupDirectory, $"{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.json");
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, new { CharacterId = hotbars->CharacterContentId,
                Job = hotbars->ActiveHotbarClassJobId, Shared = hotbars->IsHotbarShared(target.Bar),
                PvP = hotbars->PvPHotbarsActive, target.Bar, target.Slot, BeforeType = 0, BeforeId = 0,
                AfterType = HotbarLinks.CommandType, AfterId = entry.HotbarId, MacroId = entry.Id });
            stream.Flush(true);
        }
        // Only the explicitly chosen empty hotbar cell is saved. No game macro bank
        // is read, reserved, replaced, or saved by this operation.
        HotbarWriter.WriteCurrent(hotbars, target.Bar, target.Slot, (RaptureHotbarModule.HotbarSlotType)HotbarLinks.CommandType, entry.HotbarId);
        var check = hotbars->GetSlotById(target.Bar, target.Slot);
        if (check == null || (byte)check->CommandType != HotbarLinks.CommandType || check->CommandId != entry.HotbarId)
            throw new InvalidOperationException("Hotbar placement could not be confirmed.");
        return $"Added to {target.Label}. No Individual or Shared macro slot used.";
    }

    public static string Remove(IGameGui gui, int bar, int slot)
    {
        var target = GetTarget(gui, bar, slot) ?? throw new InvalidOperationException("Show that normal hotbar first.");
        if (target.Error == "Unlock your hotbars first.") throw new InvalidOperationException(target.Error);
        var hotbars = RaptureHotbarModule.Instance();
        if (hotbars == null) throw new InvalidOperationException("Hotbars are not ready.");
        var native = hotbars->GetSlotById(target.Bar, target.Slot);
        if (native == null || !HotbarLinks.IsShortcut((byte)native->CommandType, native->CommandId))
            throw new InvalidOperationException("That slot is not a MoreMacros link. Nothing was changed.");
        HotbarWriter.WriteCurrent(hotbars, target.Bar, target.Slot, RaptureHotbarModule.HotbarSlotType.Empty, 0);
        return $"Removed link from {target.Label}. Saved macro kept.";
    }

    public static void Refresh()
    {
        var hotbars = RaptureHotbarModule.Instance();
        if (hotbars == null) return;
        for (uint bar = 0; bar < 18; bar++) for (uint slot = 0; slot < 16; slot++)
        {
            var native = hotbars->GetSlotById(bar, slot);
            if (native != null && HotbarLinks.IsShortcut((byte)native->CommandType, native->CommandId))
                native->IsLoaded = true;
        }
    }
}
