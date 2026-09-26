using System.Numerics;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace MoreMacros;

internal static unsafe class NativeWindowPosition
{
    public static Vector2 Read(AtkUnitBase* addon)
    {
        // X/Y can already contain the next drag position while the current frame
        // still displays RootNode.ScreenX/Y. Follow the geometry the game draws.
        var root = addon->RootNode;
        return root != null && float.IsFinite(root->ScreenX) && float.IsFinite(root->ScreenY)
            ? new(root->ScreenX, root->ScreenY) : new(addon->X, addon->Y);
    }
}
