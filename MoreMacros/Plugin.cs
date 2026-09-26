using System.Collections.Concurrent;
using Dalamud.Bindings.ImGui;
using System.Numerics;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using MoreMacros.Core;

namespace MoreMacros;

public sealed class Plugin : IDalamudPlugin, IMacroHost
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager Commands { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static ITextureProvider Textures { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IChatGui Chat { get; private set; } = null!;
    [PluginService] internal static IGameInteropProvider Interop { get; private set; } = null!;

    private readonly ConcurrentQueue<Action> pending = new();
    private readonly LibraryStore store;
    private readonly MacroWindow window;
    private readonly MacroIconProvider macroIcons = new();
    private bool refreshPending = true;
    private readonly VirtualHotbarHooks? hotbarHooks;
    private string HotbarBackupDirectory => Path.Combine(PluginInterface.GetPluginConfigDirectory(), "hotbar-link-backups");
    private readonly GameIconLoader icons = new((id, hiRes) => Textures.GetFromGameIcon(
        new Dalamud.Interface.Textures.GameIconLookup(id, hiRes: hiRes)).GetWrapOrDefault()?.Handle);
    public MacroLibrary Library { get; }
    public string Status { get; private set; } = "Ready";
    public string? LoadError { get; }

    public Plugin()
    {
        store = new LibraryStore(PluginInterface.GetPluginConfigDirectory());
        try { Library = store.Load(); }
        catch (Exception ex)
        {
            Library = new MacroLibrary();
            LoadError = $"Could not load library.json. Your file was preserved. Restore library.json.bak before reloading. {ex.Message}";
            Log.Error(ex, "Library load failed; editing is disabled.");
        }
        window = new MacroWindow(this);
        try { if (LoadError is null) hotbarHooks = new VirtualHotbarHooks(this, Interop); }
        catch (Exception ex) { Status = "Hotbar links unavailable. See /xllog."; Log.Error(ex, "Could not initialize hotbar links."); }
        Commands.AddHandler("/moremacros", new CommandInfo(OnCommand) { HelpMessage = "Open extra macro pages. /moremacros run <macro ID> runs a saved macro." });
        PluginInterface.UiBuilder.Draw += Draw;
        PluginInterface.UiBuilder.OpenMainUi += Open;
        PluginInterface.UiBuilder.OpenConfigUi += Open;
        Framework.Update += Update;
    }

    private void Draw()
    {
        if (!ClientState.IsLoggedIn) return;
        if (NativeMacros.TryGetWindow(GameGui, out var position, out var scale))
        {
            _ = AutoTranslatePhrases;
            window.Draw(position, scale);
        }
        else window.OnNativeClosed();
    }

    private void Open() => Queue(() => { NativeMacros.Open(); window.Activate(); });
    private void OnCommand(string command, string args)
    {
        var parts = args.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) { Open(); return; }
        if (parts.Length == 2 && parts[0].Equals("run", StringComparison.OrdinalIgnoreCase) && Guid.TryParse(parts[1], out var id))
        {
            var macro = Library.Find(id);
            if (macro is null || LoadError is not null) Chat.PrintError("MoreMacros: saved macro not found or library unavailable.");
            else Execute(macro);
            return;
        }
        Chat.Print("MoreMacros: /moremacros or /moremacros run <macro ID>");
    }

    internal void Queue(Action action) => pending.Enqueue(action);
    private void Update(IFramework framework)
    {
        if (refreshPending && ClientState.IsLoggedIn)
        {
            try { macroIcons.Refresh(Library, NativeMacroIcons.Resolve); }
            catch (Exception ex) { Log.Error(ex, "Could not resolve macro icons; keeping manually chosen icons."); }
            NativeHotbars.Refresh();
            refreshPending = false;
        }
        while (pending.TryDequeue(out var action))
        {
            try
            {
                if (!ClientState.IsLoggedIn) throw new InvalidOperationException("Log into a character first.");
                action();
            }
            catch (Exception ex) { Status = ex.Message; Log.Error(ex, "MoreMacros operation failed."); Chat.PrintError($"MoreMacros: {ex.Message}"); }
        }
    }

    public bool Save()
    {
        if (LoadError is not null) return false;
        try { store.Save(Library); Status = "Saved"; refreshPending = true; return true; }
        catch (Exception ex) { Status = $"Save failed: {ex.Message}"; Log.Error(ex, "Could not save macro library."); return false; }
    }

    public void SetStatus(string message) => Status = message;
    public void NativePage(uint set) => Queue(() => NativeMacros.Open(set));
    public ImTextureID? GetIcon(uint iconId) => icons.Get(iconId);
    public uint GetMacroIconId(MacroEntry entry) => macroIcons.Get(entry);
    private uint[]? iconChoices;
    private IReadOnlyList<AutoTranslatePhrase> autoTranslatePhrases = [];
    private bool catalogRequested;
    public IReadOnlyList<AutoTranslatePhrase> AutoTranslatePhrases
    {
        get
        {
            if (!catalogRequested)
            {
                catalogRequested = true;
                Queue(() =>
                {
                    try { autoTranslatePhrases = NativeAutoTranslate.ReadCatalog(); }
                    finally { if (autoTranslatePhrases.Count == 0) catalogRequested = false; }
                });
            }
            return autoTranslatePhrases;
        }
    }
    public IReadOnlyList<uint> IconChoices => iconChoices ??= Enumerable.Range(66000, 400).Select(i => (uint)i)
        .Where(id => Textures.TryGetIconPath(new Dalamud.Interface.Textures.GameIconLookup(id), out _)
            || Textures.TryGetIconPath(new Dalamud.Interface.Textures.GameIconLookup(id, hiRes: false), out _)).ToArray();
    HotbarDropTarget? IMacroHost.GetHotbarTarget(Vector2 position) => NativeHotbars.FindTarget(GameGui, position);
    void IMacroHost.DropOnHotbar(MacroEntry entry, HotbarDropTarget target)
    {
        var id = entry.Id;
        Queue(() =>
        {
            if (hotbarHooks is null) throw new InvalidOperationException("Hotbar support could not start. Reload MoreMacros and check /xllog.");
            var current = Library.Find(id) ?? throw new InvalidOperationException("Saved macro not found.");
            var oldId = current.HotbarId;
            var counter = Library.NextHotbarId;
            HotbarLinks.Assign(Library, id);
            if (!Save()) { current.HotbarId = oldId; Library.NextHotbarId = counter; return; }
            Status = NativeHotbars.Place(GameGui, current, target, HotbarBackupDirectory);
            Chat.Print($"MoreMacros: {Status}");
        });
    }
    void IMacroHost.PlaceOnHotbar(MacroEntry entry, int bar, int slot)
    {
        var target = NativeHotbars.GetTarget(GameGui, bar - 1, slot - 1);
        if (target is null) { SetStatus("Show that normal hotbar first, then try again."); return; }
        ((IMacroHost)this).DropOnHotbar(entry, target);
    }
    void IMacroHost.RemoveHotbarLink(int bar, int slot) => Queue(() =>
    {
        Status = NativeHotbars.Remove(GameGui, bar - 1, slot - 1);
    });
    public void Execute(MacroEntry entry)
    {
        var copy = entry.Duplicate();
        copy.Id = entry.Id;
        Queue(() => { NativeMacros.Execute(copy); Status = $"Executed {copy.Name}"; });
    }

    public void Import(uint set) => Queue(() =>
    {
        if (LoadError is not null) return;
        var result = NativeMacros.Import(set);
        Library.Pages.Add(result.Page);
        if (!Save()) { Library.Pages.Remove(result.Page); return; }
        window.SelectPage(result.Page.Id);
        Status = result.Skipped == 0 ? "Copied game macros into a new page."
            : $"Copied page; skipped {result.Skipped} macros with unsupported text. Auto-translate phrases are supported.";
        Chat.Print($"MoreMacros: {Status}");
    });

    public void Export()
    {
        try { var path = store.Export(Library); Status = "Backup exported"; Chat.Print($"MoreMacros backup: {path}"); }
        catch (Exception ex) { Status = $"Export failed: {ex.Message}"; }
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= Draw;
        PluginInterface.UiBuilder.OpenMainUi -= Open;
        PluginInterface.UiBuilder.OpenConfigUi -= Open;
        Framework.Update -= Update;
        Commands.RemoveHandler("/moremacros");
        pending.Clear();
        hotbarHooks?.Dispose();
        _ = Framework.RunOnFrameworkThread(NativeHotbars.Refresh);
        // Only explicitly saved edits are persisted; a corrupt library is never overwritten.
    }
}
