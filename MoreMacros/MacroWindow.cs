using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using MoreMacros.Core;

namespace MoreMacros;

internal sealed class MacroWindow
{
    private readonly IMacroHost plugin;
    private Guid pageId;
    private int selected;
    private bool active;
    private bool loaded;
    private string name = "";
    private string content = "";
    private string tags = "";
    private int icon = 66001;
    private int iconInput = 66001;
    private Guid? dragging;
    private int hotbarNumber = 1;
    private int hotbarSlot = 1;
    private Guid? placementMacro;
    private bool openPlacement;
    private string rename = "";
    private string search = "";
    private bool dirty;
    private string? validation;
    private float scale = 1;
    private MacroEntry? clipboard;
    private List<AutoTranslatePhrase> phrases = [];
    private bool openAutoTranslate;
    private bool focusPhraseSearch;
    private bool restoreCommands;
    private string phraseSearch = "";
    private string completionText = "";
    private int completionStart;
    private int completionEnd;
    private int phraseSelection;
    private string? pendingText;
    private int pendingCursor;
    private Guid? deletionMacro;
    private bool openDeleteMacro;
    private MacroPage Page => plugin.Library.Pages.First(p => p.Id == pageId);
    private MacroEntry Entry => Page.Macros[selected];
    private const ImGuiWindowFlags OverlayFlags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove |
        ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoScrollbar |
        ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNavFocus;

    public MacroWindow(IMacroHost plugin)
    {
        this.plugin = plugin;
        pageId = plugin.Library.Pages[0].Id;
    }

    public void Activate() => active = true;
    public void OnNativeClosed() { dragging = null; /* Keep the editor draft. */ }
    public void SelectPage(Guid id)
    {
        if (!Commit()) return;
        pageId = id;
        selected = 0;
        loaded = false;
        search = "";
        active = true;
    }

    public void Draw(Vector2 nativePosition, float nativeScale)
    {
        scale = nativeScale;
        var origin = ImGui.GetMainViewport().Pos + nativePosition;
        PushTheme();
        try
        {
            if (!active)
            {
                ImGui.SetNextWindowViewport(ImGui.GetMainViewport().ID);
                ImGui.SetNextWindowPos(origin + new Vector2(456, 42) * scale);
                ImGui.SetNextWindowSize(new Vector2(146, 27) * scale);
                if (ImGui.Begin("##MoreMacrosTab", OverlayFlags | ImGuiWindowFlags.NoBackground))
                {
                    ImGui.SetWindowFontScale(scale);
                    if (ImGui.Button("MoreMacros", new Vector2(144, 25) * scale)) active = true;
                }
                ImGui.End();
                return;
            }

            ImGui.SetNextWindowViewport(ImGui.GetMainViewport().ID);
            ImGui.SetNextWindowPos(origin + new Vector2(6, 42) * scale);
            ImGui.SetNextWindowSize(new Vector2(764, 504) * scale);
            if (ImGui.Begin("##MoreMacrosPage", OverlayFlags))
            {
                ImGui.SetWindowFontScale(scale);
                if (plugin.LoadError is not null)
                {
                    ImGui.TextWrapped(plugin.LoadError);
                    if (ImGui.Button("Back to game macros")) active = false;
                }
                else
                {
                    if (!loaded) LoadEditor();
                    DrawTabs();
                    DrawGrid();
                    DrawEditor();
                    DrawPopups();
                    DrawHotbarDrag();
                    At(6, 483);
                    var status = validation ?? plugin.Status;
                    ImGui.TextColored(validation is null ? new Vector4(.79f, .76f, .66f, 1) : new Vector4(1, .55f, .42f, 1), status);
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip(status);
                }
            }
            ImGui.End();
        }
        finally { ImGui.PopStyleColor(12); ImGui.PopStyleVar(5); }
    }

    private void DrawTabs()
    {
        At(0, 0);
        if (ImGui.Button("Individual", S(213, 25)) && Commit()) { active = false; plugin.NativePage(0); }
        At(220, 0);
        if (ImGui.Button("Shared", S(213, 25)) && Commit()) { active = false; plugin.NativePage(1); }
        At(446, 0);
        ImGui.SetNextItemWidth(225 * scale);
        if (ImGui.BeginCombo("##Pages", Page.Name))
        {
            foreach (var page in plugin.Library.Pages)
                if (ImGui.Selectable(page.Name + "##" + page.Id, page.Id == pageId)) SelectPage(page.Id);
            ImGui.EndCombo();
        }
        if (ImGui.IsItemHovered() && ImGui.IsMouseReleased(ImGuiMouseButton.Right)) ImGui.OpenPopup("Page menu");
        At(678, 0);
        if (ImGui.Button("+", S(32, 25)) && Commit())
        {
            var page = new MacroPage { Name = "Extra " + (plugin.Library.Pages.Count + 1) };
            plugin.Library.Pages.Add(page);
            if (plugin.Save()) SelectPage(page.Id);
            else plugin.Library.Pages.Remove(page);
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Add a page of 100 macros");
        At(718, 0);
        if (ImGui.Button("...", S(36, 25))) ImGui.OpenPopup("Page menu");
        var openRename = false;
        var openDelete = false;
        ImGui.SetNextWindowViewport(ImGui.GetMainViewport().ID);
        if (ImGui.BeginPopup("Page menu"))
        {
            if (ImGui.MenuItem("Rename page...") && Commit()) { rename = Page.Name; openRename = true; }
            if (ImGui.MenuItem("Copy Individual into a new page") && Commit()) plugin.Import(0);
            if (ImGui.MenuItem("Copy Shared into a new page") && Commit()) plugin.Import(1);
            if (ImGui.MenuItem("Export backup") && Commit()) plugin.Export();
            ImGui.Separator();
            if (ImGui.MenuItem("Delete page...", "", false, plugin.Library.Pages.Count > 1) && Commit()) openDelete = true;
            ImGui.EndPopup();
        }
        if (openRename) ImGui.OpenPopup("Rename page");
        if (openDelete) ImGui.OpenPopup("Delete page");
    }

    private void DrawGrid()
    {
        var draw = ImGui.GetWindowDrawList();
        for (var i = 0; i < 100; i++)
        {
            var entry = Page.Macros[i];
            var matches = search.Length == 0 || entry.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || entry.Tags.Contains(search, StringComparison.OrdinalIgnoreCase)
                || entry.Content.Contains(search, StringComparison.OrdinalIgnoreCase);
            At(i % 10 * 44 + 1, 32 + i / 10 * 44);
            ImGui.PushID(i);
            var pos = ImGui.GetCursorScreenPos();
            var clicked = ImGui.InvisibleButton("slot", S(42, 42));
            var hovered = ImGui.IsItemHovered();
            if (dragging is null && ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left) && !entry.IsEmpty && Commit())
            {
                selected = i;
                LoadEditor();
                dragging = Entry.Id;
            }
            var end = pos + S(42, 42);
            draw.AddRectFilled(pos, end, matches ? 0xFF666761u : 0xFF393A37u, 5 * scale);
            draw.AddRect(pos, end, i == selected ? 0xFFBCE6FFu : 0xFF9B9F98u, 5 * scale, ImDrawFlags.None, (i == selected ? 2 : 1) * scale);
            if (matches)
            {
                var texture = plugin.GetIcon(plugin.GetMacroIconId(entry));
                if (texture is not null) draw.AddImage(texture.Value, pos + S(2, 2), end - S(2, 2));
                else draw.AddText(pos + S(13, 9), 0xFFDDDDDDu, "M");
            }
            if (entry.IsEmpty) draw.AddText(pos + S(3, 25), 0xFFB5B7B2u, i.ToString("00"));
            if (hovered)
            {
                if (!entry.IsEmpty) ImGui.SetTooltip($"#{i:00}  {entry.Name}\n{entry.Tags}\nDrag to an empty hotbar slot. Double-click to execute.");
                else ImGui.SetTooltip($"Empty slot #{i:00}");
            }
            if (clicked && dragging is null && Commit()) { selected = i; LoadEditor(); }
            if (hovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left) && Commit()) plugin.Execute(Entry);
            ImGui.SetNextWindowViewport(ImGui.GetMainViewport().ID);
            if (ImGui.BeginPopupContextItem("Slot options"))
            {
                if (ImGui.MenuItem("Execute", "", false, !entry.IsEmpty) && Commit()) plugin.Execute(Page.Macros[i]);
                if (ImGui.MenuItem("Copy") && Commit()) clipboard = Page.Macros[i].Duplicate();
                if (ImGui.MenuItem("Paste into empty slot", "", false, clipboard is not null && entry.IsEmpty) && Commit())
                {
                    var previous = Page.Macros[i];
                    Page.Macros[i] = clipboard!.Duplicate();
                    if (!plugin.Save()) Page.Macros[i] = previous;
                    if (selected == i) LoadEditor();
                }
                if (ImGui.MenuItem("Place on hotbar...", "", false, !entry.IsEmpty) && Commit())
                { placementMacro = Page.Macros[i].Id; openPlacement = true; }
                ImGui.Separator();
                if (ImGui.MenuItem("Delete macro...", "", false, !entry.IsEmpty) && Commit())
                { deletionMacro = Page.Macros[i].Id; openDeleteMacro = true; }
                ImGui.EndPopup();
            }
            ImGui.PopID();
        }
        At(5, 473);
        ImGui.TextColored(new Vector4(.86f, .81f, .66f, 1), $"Macro #{selected:00}                       {Page.Macros.Count(m => !m.IsEmpty)}/100");
    }

    private void DrawEditor()
    {
        At(452, 32);
        ImGui.TextUnformatted("Name");
        At(689, 32);
        ImGui.TextDisabled($"{MacroRules.CountCharacters(name)}/20");
        At(452, 51);
        ImGui.SetNextItemWidth(300 * scale);
        if (ImGui.InputText("##Name", ref name, 256)) dirty = true;
        At(452, 80);
        ImGui.TextUnformatted("Tags");
        At(452, 99);
        ImGui.SetNextItemWidth(300 * scale);
        if (ImGui.InputTextWithHint("##Tags", "raid, crafting, social", ref tags, 256)) dirty = true;
        At(452, 130);
        ImGui.TextUnformatted("Commands");
        At(689, 130);
        ImGui.TextDisabled($"{MacroRules.Lines(content).Length}/15");
        At(452, 150);
        if (restoreCommands) { ImGui.SetKeyboardFocusHere(); restoreCommands = false; }
        if (ImGui.InputTextMultiline("##Commands", ref content, 16384, S(300, 221),
            ImGuiInputTextFlags.CallbackCompletion | ImGuiInputTextFlags.CallbackAlways, CommandsCallback)) dirty = true;
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Type part of a phrase, then press Tab for auto-translate.");
        if (openAutoTranslate) { ImGui.OpenPopup("Auto-translate"); openAutoTranslate = false; focusPhraseSearch = true; }
        At(452, 379);
        if (ImGui.Button("Save", S(68, 26))) Commit();
        At(528, 379);
        if (ImGui.Button("Execute", S(88, 26)) && Commit()) plugin.Execute(Entry);
        At(624, 379);
        if (ImGui.Button("Icon...", S(128, 26))) { iconInput = icon; ImGui.OpenPopup("Choose icon"); }
        At(452, 412);
        if (ImGui.Button("Place on hotbar...", S(214, 25)) && Commit())
        { placementMacro = Entry.Id; openPlacement = true; }
        At(674, 412);
        if (ImGui.Button("Clear", S(78, 25))) { deletionMacro = Entry.Id; openDeleteMacro = true; }
        At(452, 445);
        ImGui.SetNextItemWidth(300 * scale);
        ImGui.InputTextWithHint("##Search", "Search names, tags and commands", ref search, 256);
        if (dirty)
        {
            At(619, 130);
            ImGui.TextColored(new Vector4(1, .8f, .3f, 1), "Edited");
        }
        validation = MacroRules.Error(name, content, phrases);
    }

    private void DrawPopups()
    {
        DrawAutoTranslate();
        DrawPageDialogs();
        if (openPlacement) { ImGui.OpenPopup("Place on hotbar"); openPlacement = false; }
        ImGui.SetNextWindowViewport(ImGui.GetMainViewport().ID);
        if (ImGui.BeginPopup("Place on hotbar"))
        {
            ImGui.TextUnformatted("Choose an empty slot on a visible, unlocked hotbar.");
            ImGui.TextDisabled("Individual and Shared macro slots are not used.");
            ImGui.SetNextItemWidth(140 * scale);
            if (ImGui.InputInt("Hotbar (1-10)", ref hotbarNumber)) hotbarNumber = Math.Clamp(hotbarNumber, 1, 10);
            ImGui.SetNextItemWidth(140 * scale);
            if (ImGui.InputInt("Slot (1-12)", ref hotbarSlot)) hotbarSlot = Math.Clamp(hotbarSlot, 1, 12);
            if (ImGui.Button("Place") && placementMacro is { } id && plugin.Library.Find(id) is { } entry)
                plugin.PlaceOnHotbar(entry, hotbarNumber, hotbarSlot);
            ImGui.SameLine();
            if (ImGui.Button("Remove MoreMacros link")) plugin.RemoveHotbarLink(hotbarNumber, hotbarSlot);
            ImGui.SameLine();
            if (ImGui.Button("Close")) ImGui.CloseCurrentPopup();
            ImGui.TextWrapped(plugin.Status);
            ImGui.EndPopup();
        }
        ImGui.SetNextWindowViewport(ImGui.GetMainViewport().ID);
        if (ImGui.BeginPopup("Choose icon"))
        {
            ImGui.TextUnformatted("Macro icons");
            if (ImGui.BeginChild("Icon grid", S(390, 280)))
            {
                for (var i = 0; i < plugin.IconChoices.Count; i++)
                {
                    if (i % 8 != 0) ImGui.SameLine();
                    var id = plugin.IconChoices[i];
                    var tex = plugin.GetIcon(id);
                    ImGui.PushID((int)id);
                    var pressed = tex is null ? ImGui.Button("...", S(30, 30))
                        : ImGui.ImageButton(tex.Value, S(30, 30));
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip(tex is null ? $"Icon {id} is unavailable or still loading." : $"Icon {id}");
                    ImGui.PopID();
                    if (pressed && tex is not null) { icon = (int)id; dirty = true; Commit(); ImGui.CloseCurrentPopup(); }
                }
            }
            ImGui.EndChild();
            ImGui.TextUnformatted("Or enter a game icon ID:");
            if (ImGui.InputInt("##IconId", ref iconInput)) iconInput = Math.Clamp(iconInput, 1, 250000);
            var preview = plugin.GetIcon((uint)iconInput);
            if (preview is not null) ImGui.Image(preview.Value, S(32, 32));
            else ImGui.TextDisabled("Icon unavailable or still loading");
            ImGui.BeginDisabled(preview is null);
            if (ImGui.Button("Use icon")) { icon = iconInput; dirty = true; Commit(); ImGui.CloseCurrentPopup(); }
            ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.Button("Close")) ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }
        if (openDeleteMacro) { ImGui.OpenPopup("Delete macro"); openDeleteMacro = false; }
        if (BeginDialog("Delete macro"))
        {
            ImGui.TextUnformatted("Delete this macro?");
            ImGui.TextUnformatted("Its hotbar links will stop working.");
            if (ImGui.Button("Delete macro") && deletionMacro is { } id)
            {
                var index = Page.Macros.FindIndex(m => m.Id == id);
                if (index >= 0)
                {
                    var old = Page.Macros[index];
                    Page.Macros[index] = new MacroEntry();
                    if (!plugin.Save()) Page.Macros[index] = old;
                    if (index == selected) LoadEditor();
                }
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel")) ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }
    }

    private void DrawPageDialogs()
    {
        if (BeginDialog("Rename page"))
        {
            ImGui.InputText("Page name", ref rename, 80);
            if (ImGui.Button("Rename") && !string.IsNullOrWhiteSpace(rename))
            {
                var old = Page.Name;
                Page.Name = rename.Trim();
                if (!plugin.Save()) Page.Name = old;
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel")) ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }
        if (BeginDialog("Delete page"))
        {
            ImGui.TextUnformatted($"Delete '{Page.Name}' and all macros on this page?");
            ImGui.TextUnformatted("Export a backup from the page menu if you want to keep a copy.");
            if (ImGui.Button("Delete page"))
            {
                var old = Page;
                var index = plugin.Library.Pages.IndexOf(old);
                plugin.Library.Pages.Remove(old);
                if (plugin.Save()) { pageId = plugin.Library.Pages[0].Id; selected = 0; loaded = false; }
                else plugin.Library.Pages.Insert(index, old);
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel")) ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }
    }

    private void DrawHotbarDrag()
    {
        if (dragging is not { } id) return;
        var entry = plugin.Library.Find(id);
        if (entry is null || ImGui.IsKeyPressed(ImGuiKey.Escape)) { dragging = null; return; }
        var mouse = ImGui.GetMousePos();
        var viewport = ImGui.GetMainViewport().Pos;
        var panel = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var overEditor = mouse.X >= panel.X && mouse.Y >= panel.Y && mouse.X < panel.X + size.X && mouse.Y < panel.Y + size.Y;
        var target = overEditor ? null : plugin.GetHotbarTarget(mouse - viewport);
        if (target is not null)
            ImGui.GetForegroundDrawList().AddRect(target.Position + viewport, target.Position + target.Size + viewport,
                target.Error is null ? 0xFF70E6A0u : 0xFF6060FFu, 3, ImDrawFlags.None, 3);
        ImGui.BeginTooltip();
        var image = plugin.GetIcon(plugin.GetMacroIconId(entry));
        if (image is not null) { ImGui.Image(image.Value, S(32, 32)); ImGui.SameLine(); }
        ImGui.TextUnformatted(entry.Name);
        ImGui.TextUnformatted(target?.Error ?? target?.Label ?? "Drop onto an empty, unlocked hotbar slot.");
        ImGui.TextDisabled("No Individual or Shared macro slot needed. Esc to cancel.");
        ImGui.EndTooltip();
        if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            if (target is { Error: null }) plugin.DropOnHotbar(entry, target);
            else plugin.SetStatus(target?.Error ?? "Drag canceled. Drop onto a visible normal hotbar slot.");
            dragging = null;
        }
        else if (!ImGui.IsMouseDown(ImGuiMouseButton.Left)) dragging = null;
    }

    private void LoadEditor()
    {
        name = Entry.Name;
        content = Entry.Content;
        phrases = [.. Entry.AutoTranslate];
        pendingText = null;
        tags = Entry.Tags;
        icon = (int)Entry.IconId;
        dirty = false;
        loaded = true;
        validation = null;
    }

    private bool Commit()
    {
        if (!loaded || !dirty) return true;
        validation = MacroRules.Error(name, content, phrases);
        if (validation is not null) { plugin.SetStatus(validation); return false; }
        var old = Entry;
        Page.Macros[selected] = new MacroEntry { Id = old.Id, HotbarId = old.HotbarId, Name = name, Content = MacroRules.Normalize(content), Tags = tags, IconId = (uint)icon,
            AutoTranslate = phrases.Where(p => content.Contains(p.Token, StringComparison.Ordinal)).ToList() };
        if (!plugin.Save()) { Page.Macros[selected] = old; return false; }
        dirty = false;
        return true;
    }

    private Vector2 S(float x, float y) => new(x * scale, y * scale);
    private void PrepareDialog()
    {
        ImGui.SetNextWindowViewport(ImGui.GetMainViewport().ID);
        ImGui.SetNextWindowPos(ImGui.GetWindowPos() + S(382, 252), ImGuiCond.Always, new Vector2(.5f));
    }
    private bool BeginDialog(string title)
    {
        PrepareDialog();
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, S(12, 10));
        var visible = ImGui.BeginPopup(title, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoMove);
        ImGui.PopStyleVar();
        return visible;
    }

    private int CommandsCallback(ref ImGuiInputTextCallbackData data)
    {
        if (pendingText is { } replacement)
        {
            data.DeleteChars(0, data.BufTextLen);
            data.InsertChars(0, replacement);
            data.CursorPos = data.SelectionStart = data.SelectionEnd = pendingCursor;
            pendingText = null;
            dirty = true;
        }
        if (data.EventFlag == ImGuiInputTextFlags.CallbackCompletion)
        {
            completionText = Encoding.UTF8.GetString(data.BufTextSpan);
            completionEnd = Encoding.UTF8.GetCharCount(data.BufTextSpan[..data.CursorPos]);
            (completionStart, phraseSearch) = AutoTranslate.CompletionRange(completionText, completionEnd, plugin.AutoTranslatePhrases);
            phraseSelection = 0;
            openAutoTranslate = true;
        }
        return 0;
    }

    private void DrawAutoTranslate()
    {
        PrepareDialog();
        ImGui.SetNextWindowSize(S(440, 310));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, S(12, 10));
        var visible = ImGui.BeginPopup("Auto-translate", ImGuiWindowFlags.NoMove);
        ImGui.PopStyleVar();
        if (!visible) return;
        ImGui.TextUnformatted("Auto-translate");
        if (focusPhraseSearch) { ImGui.SetKeyboardFocusHere(); focusPhraseSearch = false; }
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextWithHint("##PhraseSearch", "Search phrases", ref phraseSearch, 256)) phraseSelection = 0;
        var catalog = plugin.AutoTranslatePhrases;
        var matches = catalog.Where(p => p.Text.Contains(phraseSearch, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(p => p.Text.StartsWith(phraseSearch, StringComparison.OrdinalIgnoreCase)).Take(100).ToArray();
        if (ImGui.IsKeyPressed(ImGuiKey.DownArrow)) phraseSelection++;
        if (ImGui.IsKeyPressed(ImGuiKey.UpArrow)) phraseSelection--;
        phraseSelection = Math.Clamp(phraseSelection, 0, Math.Max(0, matches.Length - 1));
        AutoTranslatePhrase? chosen = ImGui.IsKeyPressed(ImGuiKey.Enter) && matches.Length > 0 ? matches[phraseSelection] : null;
        if (ImGui.BeginChild("Phrases", S(0, 210)))
        {
            if (catalog.Count == 0) ImGui.TextDisabled("Loading game phrases...");
            else if (matches.Length == 0) ImGui.TextDisabled("No matching phrases.");
            for (var i = 0; i < matches.Length; i++)
            {
                if (ImGui.Selectable(matches[i].Text + "##" + i, i == phraseSelection)) chosen = matches[i];
                if (i == phraseSelection && (ImGui.IsKeyPressed(ImGuiKey.DownArrow) || ImGui.IsKeyPressed(ImGuiKey.UpArrow))) ImGui.SetScrollHereY();
            }
        }
        ImGui.EndChild();
        ImGui.TextDisabled("Up/Down: choose   Enter: insert   Esc: cancel");
        if (chosen is not null)
        {
            var replacement = completionText[..completionStart] + chosen.Token + completionText[completionEnd..];
            var updated = phrases.Where(p => p.Token != chosen.Token).Append(chosen).ToList();
            var error = MacroRules.Error(name, replacement, updated);
            if (error is null && Encoding.UTF8.GetByteCount(replacement) < 16384)
            {
                phrases = updated;
                pendingText = replacement;
                pendingCursor = Encoding.UTF8.GetByteCount(completionText[..completionStart] + chosen.Token);
                restoreCommands = true;
                ImGui.CloseCurrentPopup();
            }
            else plugin.SetStatus(error ?? "The editor is full.");
        }
        ImGui.EndPopup();
    }
    private void At(float x, float y) => ImGui.SetCursorPos(S(x, y));
    private void PushTheme()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, S(5, 3));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 3f * scale);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(.25f, .265f, .27f, 1));
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(.91f, .9f, .86f, 1));
        ImGui.PushStyleColor(ImGuiCol.TextDisabled, new Vector4(.65f, .65f, .61f, 1));
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(.28f, .29f, .28f, 1));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(.43f, .42f, .34f, 1));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(.5f, .44f, .29f, 1));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(.29f, .29f, .28f, 1));
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(.36f, .36f, .32f, 1));
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(.38f, .37f, .31f, 1));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(.56f, .53f, .41f, 1));
        ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(.4f, .37f, .26f, 1));
        ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(.2f, .21f, .21f, 1));
    }
}
