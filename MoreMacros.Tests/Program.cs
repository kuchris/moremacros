using System.Text.Json;
using MoreMacros.Core;

var tests = new List<(string, Action)>();
void Test(string title, Action action) => tests.Add((title, action));
void Assert(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
void Reject(Action action)
{
    try { action(); }
    catch (Exception ex) when (ex is InvalidDataException or JsonException) { return; }
    throw new Exception("Invalid input was accepted");
}
string Scratch() => Path.Combine(Path.GetTempPath(), "MoreMacros-tests", Guid.NewGuid().ToString("N"));

Test("100 slots per page with unique stable IDs", () =>
{
    var library = new MacroLibrary();
    library.Pages.Add(new MacroPage());
    library.Validate();
    Assert(library.Pages.SelectMany(p => p.Macros).Select(m => m.Id).Distinct().Count() == 200);
});
Test("Japanese text, glyphs, tags and blank lines survive disk round trip", () =>
{
    var store = new LibraryStore(Scratch());
    var library = new MacroLibrary();
    var entry = library.Pages[0].Macros[99];
    entry.Name = "左・集合";
    entry.Content = "/p 左側に集合！ ★\n\n/echo Done <wait.3>";
    entry.Tags = "raid, 日本語";
    store.Save(library);
    var restored = store.Load().Find(entry.Id)!;
    Assert(restored.Content == entry.Content && restored.Name == entry.Name && restored.Tags == entry.Tags);
});
Test("Latest save retains a recoverable previous library", () =>
{
    var store = new LibraryStore(Scratch());
    var library = new MacroLibrary();
    library.Pages[0].Macros[0].Content = "/echo before";
    store.Save(library);
    library.Pages[0].Macros[0].Content = "/echo after";
    store.Save(library);
    Assert(store.Load().Pages[0].Macros[0].Content == "/echo after");
    Assert(JsonSerializer.Deserialize<MacroLibrary>(File.ReadAllText(store.BackupPath))!.Pages[0].Macros[0].Content == "/echo before");
});
Test("Corrupt input is rejected without replacing the source", () =>
{
    var dir = Scratch(); Directory.CreateDirectory(dir);
    var store = new LibraryStore(dir);
    File.WriteAllText(store.FilePath, "broken json");
    Reject(() => store.Load());
    Assert(File.ReadAllText(store.FilePath) == "broken json");
});
Test("Invalid saves preserve the existing file", () =>
{
    var store = new LibraryStore(Scratch());
    var library = new MacroLibrary(); store.Save(library);
    var before = File.ReadAllText(store.FilePath);
    library.Pages[0].Macros.RemoveAt(99);
    Reject(() => store.Save(library));
    Assert(File.ReadAllText(store.FilePath) == before);
});
Test("Stable hotbar IDs survive renaming and page reordering", () =>
{
    var library = new MacroLibrary();
    var target = library.Pages[0].Macros[50]; target.Content = "/echo target";
    library.Pages[0].Name = "Raid";
    library.Pages.Insert(0, new MacroPage());
    Assert(ReferenceEquals(library.Find(target.Id), target));
    var copy = target.Duplicate();
    Assert(copy.Id != target.Id && copy.Content == target.Content);
});
Test("15 lines accepted, 16 rejected; CRLF handled", () =>
{
    Assert(MacroRules.Error("test", string.Join("\r\n", Enumerable.Repeat("/echo x", 15))) is null);
    Assert(MacroRules.Error("test", string.Join('\n', Enumerable.Repeat("/echo x", 16))) is not null);
});
Test("20 Unicode characters accepted without splitting surrogate pairs", () =>
{
    Assert(MacroRules.Error(string.Concat(Enumerable.Repeat("😀", 20)), "/echo x") is null);
    Assert(MacroRules.Error(new string('日', 21), "") is not null);
});
Test("Game payloads and embedded nulls are rejected rather than flattened", () =>
{
    Assert(MacroRules.Error("macro", "/p \u0002payload\u0003") is not null);
    Assert(MacroRules.Error("macro", "/echo test\0/echo hidden") is not null);
});
Test("180-character line limit", () =>
{
    Assert(MacroRules.Error("", new string('日', 180)) is null);
    Assert(MacroRules.Error("", new string('日', 181)) is not null);
});
Test("Malformed schemas and duplicate IDs are rejected", () =>
{
    var library = new MacroLibrary { Version = 99 }; Reject(library.Validate);
    library.Version = 3;
    library.Pages[0].Macros[1].Id = library.Pages[0].Macros[0].Id;
    Reject(library.Validate);
    library = new MacroLibrary { Pages = [] }; Reject(library.Validate);
});
Test("Export is independently loadable and preserves IDs", () =>
{
    var store = new LibraryStore(Scratch());
    var library = new MacroLibrary();
    var export = store.Export(library);
    var copy = JsonSerializer.Deserialize<MacroLibrary>(File.ReadAllText(export))!;
    copy.Validate();
    Assert(copy.Pages[0].Macros[99].Id == library.Pages[0].Macros[99].Id);
});

Test("All 200 original macro links and normal general actions bypass the plugin", () =>
{
    for (uint set = 0; set < 2; set++) for (uint index = 0; index < 100; index++)
        Assert(!HotbarLinks.IsShortcut(7, (set << 8) | index));
    for (uint id = 0; id < 10000; id++) Assert(!HotbarLinks.IsShortcut(10, id));
    Assert(!HotbarLinks.IsShortcut(7, HotbarLinks.Prefix | 1));
    Assert(!HotbarLinks.IsShortcut(10, HotbarLinks.Prefix));
});
Test("More than 200 extra macros can link without any native macro slots", () =>
{
    var library = new MacroLibrary();
    library.Pages.Add(new MacroPage()); library.Pages.Add(new MacroPage());
    foreach (var entry in library.Pages.SelectMany(p => p.Macros))
    {
        entry.Content = "/echo extra";
        var id = HotbarLinks.Assign(library, entry.Id);
        Assert(HotbarLinks.IsShortcut(10, id));
        Assert(HotbarLinks.Resolve(library, 10, id) == entry);
    }
    library.Validate();
    Assert(library.NextHotbarId == 301);
});
Test("Shortcut mapping survives file reload, rename and icon changes", () =>
{
    var library = new MacroLibrary();
    var entry = library.Pages[0].Macros[0]; entry.Content = "/echo extra";
    var id = HotbarLinks.Assign(library, entry.Id);
    entry.Name = "Renamed"; entry.IconId = 66003;
    var store = new LibraryStore(Scratch()); store.Save(library);
    var loaded = store.Load();
    Assert(HotbarLinks.Assign(loaded, entry.Id) == id && loaded.NextHotbarId == 2);
    Assert(HotbarLinks.Resolve(loaded, 10, id) is { Name: "Renamed", IconId: 66003 });
});
Test("Deleting a linked macro never reassigns its old shortcut to another macro", () =>
{
    var library = new MacroLibrary();
    var entry = library.Pages[0].Macros[0]; entry.Content = "/echo extra";
    var id = HotbarLinks.Assign(library, entry.Id);
    library.Pages[0].Macros[0] = entry.Duplicate();
    var replacement = library.Pages[0].Macros[0];
    Assert(replacement.HotbarId == 0);
    Assert(HotbarLinks.Assign(library, replacement.Id) != id);
    Assert(HotbarLinks.Resolve(library, 10, id) is null);
    library.Validate();
});
Test("Old libraries load without assigning IDs or changing any macro", () =>
{
    var library = new MacroLibrary();
    var json = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(library))!;
    json["Version"] = 1;
    json.AsObject().Remove("NextHotbarId");
    foreach (var entry in json["Pages"]![0]!["Macros"]!.AsArray()) entry!.AsObject().Remove("HotbarId");
    var directory = Scratch(); Directory.CreateDirectory(directory);
    var store = new LibraryStore(directory);
    File.WriteAllText(store.FilePath, json.ToJsonString());
    var loaded = store.Load();
    loaded.Validate();
    Assert(loaded.NextHotbarId == 1 && loaded.Pages[0].Macros.All(m => m.HotbarId == 0));
    Assert(loaded.Pages[0].Macros[0].Id == library.Pages[0].Macros[0].Id);
    Assert(loaded.Version == 3 && File.ReadAllText(store.FilePath) == json.ToJsonString());
});
Test("Duplicate shortcut IDs and counters that could reuse old IDs are rejected", () =>
{
    var library = new MacroLibrary();
    var entry = library.Pages[0].Macros[0]; entry.Content = "/echo extra";
    HotbarLinks.Assign(library, entry.Id);
    library.Pages[0].Macros[1].HotbarId = entry.HotbarId;
    Reject(library.Validate);
    library.Pages[0].Macros[1].HotbarId = 0;
    library.NextHotbarId = 1;
    Reject(library.Validate);
});

Test("Auto-translate payloads survive save, reload, copy, and hotbar assignment", () =>
{
    var phrase = new AutoTranslatePhrase(2, 203, "Hello!");
    var library = new MacroLibrary();
    var entry = library.Pages[0].Macros[0];
    entry.Content = "/p " + phrase.Token + " 日本語\n/echo done";
    entry.AutoTranslate = [phrase];
    HotbarLinks.Assign(library, entry.Id);
    var before = AutoTranslate.EncodeLines(entry);
    var store = new LibraryStore(Scratch()); store.Save(library);
    var loaded = store.Load().Find(entry.Id)!;
    Assert(loaded.HotbarId == entry.HotbarId && loaded.AutoTranslate.SequenceEqual(entry.AutoTranslate));
    Assert(AutoTranslate.EncodeLines(loaded)[0].SequenceEqual(before[0]));
    var copy = loaded.Duplicate(); copy.AutoTranslate.Clear();
    Assert(loaded.AutoTranslate.Count == 1 && copy.HotbarId == 0);
});
Test("Damaged or unknown auto-translate tokens are rejected", () =>
{
    var phrase = new AutoTranslatePhrase(2, 203, "Hello!");
    Assert(MacroRules.Error("", phrase.Token, [phrase]) is null);
    Assert(MacroRules.Error("", phrase.Token) is not null);
    Assert(MacroRules.Error("", phrase.Token.Replace("Hello", "Hi"), [phrase]) is not null);
    Assert(MacroRules.Error("", phrase.Token[..^1], [phrase]) is not null);
    Assert(MacroRules.Error("", phrase.Token[1..], [phrase]) is not null);
});
Test("Completion finds multiword phrases without replacing commands or Japanese text", () =>
{
    AutoTranslatePhrase[] phrases = [new(2, 202, "Good morning!"), new(2, 203, "Hello!")];
    Assert(AutoTranslate.CompletionRange("/p 日本語 Good morn", 16, phrases) == (7, "Good morn"));
    Assert(AutoTranslate.CompletionRange("", 0, phrases) == (0, ""));
    Assert(AutoTranslate.CompletionRange("/p ", 3, phrases) == (3, ""));
});
Test("Version 2 migration retains existing links and backs up the old format", () =>
{
    var library = new MacroLibrary();
    var entry = library.Pages[0].Macros[0]; entry.Content = "/echo previous";
    var id = HotbarLinks.Assign(library, entry.Id);
    var json = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(library))!;
    json["Version"] = 2;
    foreach (var macro in json["Pages"]![0]!["Macros"]!.AsArray()) macro!.AsObject().Remove("AutoTranslate");
    var dir = Scratch(); Directory.CreateDirectory(dir);
    var store = new LibraryStore(dir); File.WriteAllText(store.FilePath, json.ToJsonString());
    var loaded = store.Load();
    Assert(loaded.Version == 3 && loaded.Find(entry.Id)!.HotbarId == id);
    store.Save(loaded);
    Assert(File.ReadAllText(store.BackupPath) == json.ToJsonString());
});

var failed = 0;
foreach (var (title, action) in tests)
{
    try { action(); Console.WriteLine($"PASS {title}"); }
    catch (Exception ex) { failed++; Console.WriteLine($"FAIL {title}: {ex.Message}"); }
}
Console.WriteLine($"{tests.Count - failed}/{tests.Count} checks passed.");
return failed == 0 ? 0 : 1;
