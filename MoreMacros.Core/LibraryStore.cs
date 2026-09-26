using System.Text.Json;

namespace MoreMacros.Core;

public sealed class LibraryStore(string directory)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public string FilePath { get; } = Path.Combine(directory, "library.json");
    public string BackupPath => FilePath + ".bak";

    public MacroLibrary Load()
    {
        if (!File.Exists(FilePath)) return new MacroLibrary();
        var library = JsonSerializer.Deserialize<MacroLibrary>(File.ReadAllText(FilePath), Options)
            ?? throw new InvalidDataException("The macro library is empty.");
        // Older plugin builds must not silently discard shortcut IDs or phrase data.
        // Migrate in memory; the original file is retained as .bak on the next save.
        if (library.Version is 1 or 2) library.Version = 3;
        library.Validate();
        return library;
    }

    public void Save(MacroLibrary library)
    {
        library.Validate();
        Directory.CreateDirectory(directory);
        var temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, library, Options);
                stream.Flush(true);
            }
            if (File.Exists(FilePath)) File.Replace(temp, FilePath, BackupPath);
            else File.Move(temp, FilePath);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    public string Export(MacroLibrary library)
    {
        library.Validate();
        var exports = Path.Combine(directory, "exports");
        Directory.CreateDirectory(exports);
        var path = Path.Combine(exports, $"MoreMacros-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(library, Options));
        return path;
    }
}
