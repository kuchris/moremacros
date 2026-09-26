using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Bindings.ImGui;
using MoreMacros;
using MoreMacros.Core;

internal static unsafe class Program
{
    private static int Main(string[] args)
    {
        var iconRepro = args.Contains("--icon-repro");
        var dragRepro = args.Contains("--drag-repro");
        var textRepro = args.Contains("--text-repro");
        var deleteRepro = args.Contains("--delete-repro");
        var macroIconRepro = args.Contains("--micon-repro");
        var output = Path.GetFullPath(args.FirstOrDefault(a => !a.StartsWith("--")) ?? "artifacts/ui");
        Directory.CreateDirectory(output);
        var context = ImGui.CreateContext();
        try
        {
            var io = ImGui.GetIO();
            io.IniFilename = null;
            io.LogFilename = null;
            io.DisplaySize = new Vector2(1200, 900);
            io.DeltaTime = 1f / 60;
            io.Fonts.AddFontDefault();
            byte* pixels;
            int tw, th;
            io.Fonts.GetTexDataAsRGBA32(0, &pixels, &tw, &th);
            io.Fonts.SetTexID(0, new ImTextureID(1UL));
            var host = new PreviewHost();
            var window = new MacroWindow(host);
            window.Activate();
            var scale = 1f;
            void Frame()
            {
                ImGui.NewFrame();
                ImGui.GetBackgroundDrawList().AddText(new Vector2(18, 18), 0xFFD0D0D0,
                    "MoreMacros - offline UI render (placeholder icons; no game connection)");
                window.Draw(new Vector2(12, 12), scale);
                ImGui.Render();
            }
            void Click(float x, float y)
            {
                io.AddMousePosEvent(x, y); Frame();
                io.AddMouseButtonEvent(0, true); Frame();
                io.AddMouseButtonEvent(0, false); Frame();
                Frame();
            }
            void Check(bool condition, string message)
            {
                if (!condition) throw new Exception(message);
                Console.WriteLine("PASS " + message);
            }
            Frame(); Frame();
            if (macroIconRepro)
            {
                var entry = host.Library.Pages[0].Macros[0];
                var phrase = new AutoTranslatePhrase(56, 7439, "Earthly Star");
                entry.Content = "/micon " + phrase.Token;
                entry.AutoTranslate = [phrase];
                host.ResolvedMacroIcon = 3143; // verified from installed Action row 7439
                host.Save(); host.RequestedIcons.Clear(); Frame();
                Check(host.RequestedIcons.Contains(3143), "Saved /micon Earthly Star drives the editor's displayed icon");
                Check(host.GetMacroIconId(entry) == 3143, "Native hotbar callback uses the same resolved icon");
                entry.Content = "/echo unchanged"; host.Save();
                Check(host.GetMacroIconId(entry) == entry.IconId, "Removing /micon restores the manually chosen icon");
                entry.Content = "/macroicon missing action"; host.ResolvedMacroIcon = null; host.Save();
                Check(host.GetMacroIconId(entry) == entry.IconId, "An unresolved /macroicon safely keeps the chosen icon");
                return 0;
            }
            if (deleteRepro)
            {
                void RightClick(float x, float y)
                {
                    io.AddMousePosEvent(x, y); Frame();
                    io.AddMouseButtonEvent(1, true); Frame();
                    io.AddMouseButtonEvent(1, false); Frame(); Frame();
                }
                ImGuiWindowPtr Popup()
                {
                    var stack = ImGui.GetCurrentContext().OpenPopupStack;
                    return new ImGuiWindowPtr(stack.Data[stack.Size-1].Window);
                }
                var original = host.Library.Pages[0].Macros[0];
                HotbarLinks.Assign(host.Library, original.Id);
                // Delete the right-clicked slot, even when another slot is selected.
                host.Library.Pages[0].Macros[1].Content = "/echo keep";
                Click(81, 102);
                RightClick(38, 102);
                var menu = Popup();
                Rasterize(ImGui.GetDrawData(), pixels, tw, th, 800, 575, Path.Combine(output, "right-click.png"));
                Click(menu.Pos.X + 60, menu.Pos.Y + menu.Size.Y - 8);
                Frame(); Frame();
                var dialog = Popup();
                Check(Vector2.Distance(dialog.Pos + dialog.Size / 2, new Vector2(400,306)) < 3,
                    "Delete confirmation is centred on the macro editor");
                Check((nint)dialog.Viewport.Handle == (nint)ImGui.GetMainViewport().Handle && (dialog.Flags & ImGuiWindowFlags.Modal) == 0,
                    "Delete uses a small popup in the game viewport without a separate modal window");
                Rasterize(ImGui.GetDrawData(), pixels, tw, th, 800, 575, Path.Combine(output, "delete-macro.png"));
                Click(dialog.Pos.X + 60, dialog.Pos.Y + dialog.Size.Y - 20);
                Check(host.Library.Pages[0].Macros[0].IsEmpty && host.Library.Pages[0].Macros[1].Content == "/echo keep",
                    "Right-click Delete removes the targeted macro and keeps the selected neighbour");
                Check(HotbarLinks.Resolve(host.Library, 10, original.HotbarId) is null,
                    "Deleted macro links cannot run another macro");
                Click(712, 66); // new page
                RightClick(520, 66);
                menu = Popup();
                Click(menu.Pos.X + 65, menu.Pos.Y + menu.Size.Y - 8);
                Frame(); Frame();
                dialog = Popup();
                Check(Vector2.Distance(dialog.Pos + dialog.Size/2, new Vector2(400,306)) < 3,
                    "Right-click page Delete stays centred on the editor");
                Click(dialog.Pos.X + 50, dialog.Pos.Y + dialog.Size.Y - 20);
                Check(host.Library.Pages.Count == 1 && host.Library.Pages[0].Macros[1].Content == "/echo keep",
                    "Page deletion keeps other pages intact");
                return 0;
            }
            if (textRepro)
            {
                Click(712, 66); // new page
                Click(495, 225);
                foreach (var ch in "/echo Hello") io.AddInputCharacter(ch);
                Frame();
                io.AddKeyEvent(ImGuiKey.Tab, true); Frame();
                io.AddKeyEvent(ImGuiKey.Tab, false); Frame(); Frame();
                Check(ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopupId | ImGuiPopupFlags.AnyPopupLevel),
                    "Tab in Commands opens auto-translate choices");
                Rasterize(ImGui.GetDrawData(), pixels, tw, th, 1200, 900, Path.Combine(output, "auto-translate.png"));
                io.AddKeyEvent(ImGuiKey.Enter, true); Frame();
                io.AddKeyEvent(ImGuiKey.Enter, false); Frame(); Frame();
                Click(490, 445);
                var saved = host.Library.Pages[1].Macros[0];
                Check(saved.Content == "/echo " + host.AutoTranslatePhrases[0].Token,
                    "Enter replaces only the typed phrase and keeps the command prefix");
                Check(saved.AutoTranslate is [{ Group: 2, Key: 203 }], "Save keeps the real auto-translate group/key");
                Check(AutoTranslate.EncodeLines(saved)[0].SequenceEqual(new byte[] {47,101,99,104,111,32,2,46,3,2,204,3}),
                    "Saved editor text encodes to an actual game auto-translate payload");
                return 0;
            }
            if (dragRepro)
            {
                io.AddMousePosEvent(38, 102); Frame();
                io.AddMouseButtonEvent(0, true); Frame();
                io.AddMousePosEvent(70, 104); Frame();
                io.AddMousePosEvent(870, 170); Frame();
                Check(host.Dropped is null, "Dragging without release never writes a hotbar slot");
                io.AddMouseButtonEvent(0, false); Frame(); Frame();
                Check(host.Dropped == host.Library.Pages[0].Macros[0].Id, "Dragging a saved macro onto a hotbar dispatches that macro on release");
                host.Dropped = null;
                host.DropError = "Unlock your hotbars first.";
                io.AddMousePosEvent(38, 102); Frame();
                io.AddMouseButtonEvent(0, true); Frame();
                io.AddMousePosEvent(870, 170); Frame();
                io.AddMouseButtonEvent(0, false); Frame();
                Check(host.Dropped is null && host.Status.Contains("Unlock"), "Locked hotbars refuse the drop and show a reason");
                host.DropError = null;
                io.AddMousePosEvent(38, 102); Frame();
                io.AddMouseButtonEvent(0, true); Frame();
                io.AddMousePosEvent(1100, 800); Frame();
                io.AddMouseButtonEvent(0, false); Frame();
                Check(host.Dropped is null, "Dropping outside a hotbar does not place a shortcut");
                io.AddKeyEvent(ImGuiKey.Escape, true); Frame();
                io.AddKeyEvent(ImGuiKey.Escape, false); Frame();
                Click(550, 479);
                Frame(); Frame();
                Rasterize(ImGui.GetDrawData(), pixels, tw, th, 1200, 900, Path.Combine(output, "hotbar-placement.png"));
                Click(575, 285);
                Check(host.Dropped == host.Library.Pages[0].Macros[0].Id, "Place dialog dispatches the selected extra macro without a native macro launcher");
                Click(685, 285);
                Check(host.Dropped is null && !host.Library.Pages[0].Macros[0].IsEmpty, "Removing a hotbar link keeps its saved macro");
                return 0;
            }
            if (iconRepro)
            {
                var hotbarId = HotbarLinks.Assign(host.Library, host.Library.Pages[0].Macros[0].Id);
                host.MissingIcon = 66002;
                Click(12 + 6 + 680, 12 + 42 + 391);
                Frame();
                Check(host.MissingIconRequests > 0, "Icon picker exercises missing icon 66002");
                Check(ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopupId | ImGuiPopupFlags.AnyPopupLevel), "Missing icon does not close the icon picker or abort drawing");
                Check(host.MissingIconRequests == 2, "Missing high and standard resolution icons are tried once each, then cached");
                var fallback = new GameIconLoader((_, hiRes) => hiRes ? throw new KeyNotFoundException() : new ImTextureID(2UL));
                Check(fallback.Get(66002)?.Handle == 2, "Standard resolution is used when only the high resolution icon is missing");
                Rasterize(ImGui.GetDrawData(), pixels, tw, th, 1200, 900, Path.Combine(output, "icon-picker.png"));
                host.ReturnTextures = true;
                Frame(); Frame();
                Click(815, 480);
                Check(host.Library.Pages[0].Macros[0].IconId == 66003, "Selecting an available texture saves its icon after a missing-icon error");
                Check(host.Library.Pages[0].Macros[0].HotbarId == hotbarId, "Saving a new icon keeps the existing native hotbar link");
                return 0;
            }
            Rasterize(ImGui.GetDrawData(), pixels, tw, th, 800, 575, Path.Combine(output, "editor-100.png"));
            Click(12 + 6 + 694, 12 + 42 + 12);
            Check(host.Library.Pages.Count == 2, "Add-page button creates a persisted 100-slot page");
            Click(12 + 6 + 485, 12 + 42 + 61);
            foreach (var ch in "Test macro") io.AddInputCharacter(ch); Frame();
            Click(12 + 6 + 480, 12 + 42 + 170);
            foreach (var ch in "/echo UI smoke test") io.AddInputCharacter(ch); Frame();
            Click(12 + 6 + 480, 12 + 42 + 391);
            Check(host.Library.Pages[1].Macros[0].Name == "Test macro", "Name input saves to the selected page");
            Check(host.Library.Pages[1].Macros[0].Content == "/echo UI smoke test", "Command editor saves text without truncation");
            Click(12 + 6 + 564, 12 + 42 + 391);
            Check(host.Executed == host.Library.Pages[1].Macros[0].Id, "Execute dispatches the selected saved macro");
            scale = 1.5f; Frame(); Frame();
            Rasterize(ImGui.GetDrawData(), pixels, tw, th, 1200, 860, Path.Combine(output, "editor-150.png"));
            scale = 1; Frame();
            Click(12 + 6 + 320, 12 + 42 + 12);
            Check(host.NativeSet == 1, "Shared tab hands control back to the native page");
            Frame();
            Rasterize(ImGui.GetDrawData(), pixels, tw, th, 800, 120, Path.Combine(output, "entry-tab.png"));
            Console.WriteLine("Screenshots: " + output);
            return 0;
        }
        finally { ImGui.DestroyContext(context); }
    }

    // Software render the actual ImGui draw buffers, including the real font atlas.
    private static void Rasterize(ImDrawDataPtr data, byte* atlas, int tw, int th, int width, int height, string path)
    {
        var target = Enumerable.Repeat(unchecked((int)0xFF202426), width * height).ToArray();
        for (var n = 0; n < data.CmdListsCount; n++)
        {
            var list = data.CmdLists[n];
            for (var c = 0; c < list->CmdBuffer.Size; c++)
            {
                var cmd = list->CmdBuffer.Data[c];
                if (cmd.UserCallback != null) continue;
                for (uint i = 0; i < cmd.ElemCount; i += 3)
                {
                    var a = list->VtxBuffer.Data[list->IdxBuffer.Data[cmd.IdxOffset + i] + cmd.VtxOffset];
                    var b = list->VtxBuffer.Data[list->IdxBuffer.Data[cmd.IdxOffset + i + 1] + cmd.VtxOffset];
                    var d = list->VtxBuffer.Data[list->IdxBuffer.Data[cmd.IdxOffset + i + 2] + cmd.VtxOffset];
                    var area = Cross(b.Pos - a.Pos, d.Pos - a.Pos);
                    if (Math.Abs(area) < 1e-5f) continue;
                    var minX = (int)Math.Max(0, Math.Max(cmd.ClipRect.X, Math.Floor(Math.Min(a.Pos.X, Math.Min(b.Pos.X, d.Pos.X)))));
                    var minY = (int)Math.Max(0, Math.Max(cmd.ClipRect.Y, Math.Floor(Math.Min(a.Pos.Y, Math.Min(b.Pos.Y, d.Pos.Y)))));
                    var maxX = (int)Math.Min(width, Math.Min(cmd.ClipRect.Z, Math.Ceiling(Math.Max(a.Pos.X, Math.Max(b.Pos.X, d.Pos.X)))));
                    var maxY = (int)Math.Min(height, Math.Min(cmd.ClipRect.W, Math.Ceiling(Math.Max(a.Pos.Y, Math.Max(b.Pos.Y, d.Pos.Y)))));
                    for (var y = minY; y < maxY; y++) for (var x = minX; x < maxX; x++)
                    {
                        var p = new Vector2(x + .5f, y + .5f);
                        var wa = Cross(b.Pos - p, d.Pos - p) / area;
                        var wb = Cross(d.Pos - p, a.Pos - p) / area;
                        var wc = 1 - wa - wb;
                        if (wa < 0 || wb < 0 || wc < 0) continue;
                        var uv = a.Uv * wa + b.Uv * wb + d.Uv * wc;
                        var tx = Math.Clamp((int)(uv.X * tw), 0, tw - 1);
                        var ty = Math.Clamp((int)(uv.Y * th), 0, th - 1);
                        var tex = atlas + (ty * tw + tx) * 4;
                        float Channel(int shift) => ((a.Col >> shift & 255) * wa + (b.Col >> shift & 255) * wb + (d.Col >> shift & 255) * wc);
                        var alpha = Channel(24) * tex[3] / 65025f;
                        var dst = (uint)target[y * width + x];
                        var red = (int)(Channel(0) * tex[0] / 255 * alpha + (dst >> 16 & 255) * (1 - alpha));
                        var green = (int)(Channel(8) * tex[1] / 255 * alpha + (dst >> 8 & 255) * (1 - alpha));
                        var blue = (int)(Channel(16) * tex[2] / 255 * alpha + (dst & 255) * (1 - alpha));
                        target[y * width + x] = unchecked((int)0xFF000000) | red << 16 | green << 8 | blue;
                    }
                }
            }
        }
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var locked = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        Marshal.Copy(target, 0, locked.Scan0, target.Length);
        bitmap.UnlockBits(locked);
        bitmap.Save(path, ImageFormat.Png);
    }

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
}

internal sealed class PreviewHost : IMacroHost
{
    private readonly MacroIconProvider macroIcons = new();
    public uint? ResolvedMacroIcon;
    public HashSet<uint> RequestedIcons { get; } = [];
    public uint GetMacroIconId(MacroEntry entry) => macroIcons.Get(entry);
    private readonly GameIconLoader icons;
    public uint? MissingIcon { get; set; }
    public int MissingIconRequests { get; private set; }
    public bool ReturnTextures { get; set; }
    public MacroLibrary Library { get; } = new();
    public string Status { get; private set; } = "Ready";
    public string? LoadError => null;
    public Guid? Executed { get; private set; }
    public uint? NativeSet { get; private set; }
    public Guid? Dropped { get; set; }
    public string? DropError { get; set; }
    public IReadOnlyList<uint> IconChoices { get; } = Enumerable.Range(66001, 100).Select(i => (uint)i).ToArray();
    public IReadOnlyList<AutoTranslatePhrase> AutoTranslatePhrases { get; } = [new(2, 203, "Hello!"), new(2, 202, "Good morning!")];
    public HotbarDropTarget? GetHotbarTarget(Vector2 position) => position.X is >= 850 and < 898 && position.Y is >= 150 and < 198
        ? new HotbarDropTarget(0, 0, new Vector2(850, 150), new Vector2(48, 48), DropError) : null;
    public void DropOnHotbar(MacroEntry entry, HotbarDropTarget target)
    {
        Dropped = entry.Id;
        HotbarLinks.Assign(Library, entry.Id);
        Save();
    }
    public void PlaceOnHotbar(MacroEntry entry, int bar, int slot) =>
        DropOnHotbar(entry, new HotbarDropTarget((uint)(bar - 1), (uint)(slot - 1), Vector2.Zero, Vector2.One, null));
    public void RemoveHotbarLink(int bar, int slot) => Dropped = null;
    public PreviewHost()
    {
        icons = new GameIconLoader((id, hiRes) =>
        {
            if (id == MissingIcon)
            {
                MissingIconRequests++;
                throw new KeyNotFoundException($"The icon with the ID {id} HiRes {hiRes} was not found.");
            }
            return ReturnTextures ? new ImTextureID(1UL) : null;
        });
        Library.Pages[0].Name = "Raid";
        Library.Pages[0].Macros[0].Name = "Positions";
        Library.Pages[0].Macros[0].Content = "/p Group 1: Left\n/p Group 2: Right\n/echo Ready <se.1>";
        Library.Pages[0].Macros[0].Tags = "raid, positions";
    }
    public bool Save() { Library.Validate(); macroIcons.Refresh(Library, _ => ResolvedMacroIcon); Status = "Saved"; return true; }
    public void SetStatus(string message) => Status = message;
    public void NativePage(uint set) => NativeSet = set;
    public void Execute(MacroEntry entry) => Executed = entry.Id;
    public void Import(uint set) { }
    public void Export() { }
    public ImTextureID? GetIcon(uint iconId) { RequestedIcons.Add(iconId); return icons.Get(iconId); }
}
