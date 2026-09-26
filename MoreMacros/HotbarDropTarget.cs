using System.Numerics;

namespace MoreMacros;

internal sealed record HotbarDropTarget(uint Bar, uint Slot, Vector2 Position, Vector2 Size, string? Error,
    ulong CharacterId = 0, byte Job = 0, bool Shared = false, bool PvP = false)
{
    public string Label => $"Hotbar {Bar + 1}, slot {Slot + 1}";
}
