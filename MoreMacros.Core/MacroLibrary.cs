using System.Text;
using System.Text.Json.Serialization;

namespace MoreMacros.Core;

public sealed class MacroEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Content { get; set; } = "";
    public uint IconId { get; set; } = 66001;
    public uint HotbarId { get; set; }
    public string Tags { get; set; } = "";
    public List<AutoTranslatePhrase> AutoTranslate { get; set; } = [];
    [JsonIgnore] public bool IsEmpty => Name.Length == 0 && Content.Length == 0;

    public MacroEntry Duplicate() => new() { Name = Name, Content = Content, IconId = IconId, Tags = Tags, AutoTranslate = [.. AutoTranslate] };
}

public sealed class MacroPage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Extra 1";
    public List<MacroEntry> Macros { get; set; } = Enumerable.Range(0, 100).Select(_ => new MacroEntry()).ToList();
}

public sealed class MacroLibrary
{
    public int Version { get; set; } = 3;
    public List<MacroPage> Pages { get; set; } = [new()];
    public uint NextHotbarId { get; set; } = 1;

    public MacroEntry? Find(Guid id) => Pages.SelectMany(p => p.Macros).FirstOrDefault(m => m.Id == id);

    public void Validate()
    {
        if (Version != 3) throw new InvalidDataException($"Unsupported library version: {Version}.");
        if (Pages is null || Pages.Count == 0) throw new InvalidDataException("The library needs at least one page.");
        var ids = new HashSet<Guid>();
        var hotbarIds = new HashSet<uint>();
        if (NextHotbarId is 0 or > HotbarLinks.MaxSequence + 1)
            throw new InvalidDataException("Invalid hotbar ID counter.");
        foreach (var page in Pages)
        {
            if (page is null || page.Id == Guid.Empty || !ids.Add(page.Id) || string.IsNullOrWhiteSpace(page.Name))
                throw new InvalidDataException("Invalid page name or ID.");
            if (page.Macros is null || page.Macros.Count != 100)
                throw new InvalidDataException("Every page must contain exactly 100 slots.");
            foreach (var macro in page.Macros)
            {
                if (macro is null || macro.Id == Guid.Empty || !ids.Add(macro.Id) || macro.Tags is null || macro.AutoTranslate is null)
                    throw new InvalidDataException("Invalid or duplicate macro ID.");
                var error = MacroRules.Error(macro.Name, macro.Content, macro.AutoTranslate);
                if (error is not null) throw new InvalidDataException(error);
                if (macro.HotbarId != 0 && (!HotbarLinks.IsShortcut(HotbarLinks.CommandType, macro.HotbarId)
                    || !hotbarIds.Add(macro.HotbarId) || (macro.HotbarId & HotbarLinks.MaxSequence) >= NextHotbarId))
                    throw new InvalidDataException("Invalid or duplicate hotbar link ID.");
            }
        }
    }
}

public static class MacroRules
{
    public static string Normalize(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');
    public static string[] Lines(string text) => Normalize(text).Split('\n');
    public static int CountCharacters(string text) => text.EnumerateRunes().Count();

    public static string? Error(string? name, string? content, IReadOnlyList<AutoTranslatePhrase>? phrases = null)
    {
        if (name is null || content is null) return "Macro text is missing.";
        if (CountCharacters(name) > 20) return "Names can contain up to 20 characters.";
        if (name.Any(char.IsControl)) return "Names cannot contain control characters.";
        if (content.Any(c => char.IsControl(c) && c is not '\r' and not '\n'))
            return "Unsupported control characters. Use Tab to insert auto-translate phrases.";
        var phraseError = AutoTranslate.Error(content, phrases ?? []);
        if (phraseError is not null) return phraseError;
        var lines = Lines(content);
        if (lines.Length > 15) return "Macros can contain up to 15 lines. Split this into another macro.";
        if (lines.Any(line => CountCharacters(line) > 180)) return "Each macro line can contain up to 180 characters.";
        return null;
    }
}
