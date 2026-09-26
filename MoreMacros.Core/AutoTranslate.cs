using System.Text;
using System.Text.Json.Serialization;

namespace MoreMacros.Core;

public sealed record AutoTranslatePhrase(uint Group, uint Key, string Text)
{
    public const char Open = '\uE040';
    public const char Close = '\uE041';
    [JsonIgnore] public string Token => $"{Open}{Text}{Close}";
}

// The editor shows bracketed phrases; their group/key is stored separately.
// Edited or unrecognised brackets are refused instead of silently becoming plain chat.
public static class AutoTranslate
{
    public static string? Error(string content, IReadOnlyList<AutoTranslatePhrase>? phrases)
    {
        if (phrases is null || phrases.Any(p => p is null || p.Group is 0 or > 200 || p.Key == 0
            || string.IsNullOrWhiteSpace(p.Text) || p.Text.Any(c => char.IsControl(c) || c is AutoTranslatePhrase.Open or AutoTranslatePhrase.Close)))
            return "Invalid auto-translate phrase data.";
        if (phrases.GroupBy(p => p.Token).Any(g => g.Select(p => (p.Group, p.Key)).Distinct().Count() > 1))
            return "Two different auto-translate phrases have the same text.";
        for (var i = 0; i < content.Length; i++)
        {
            if (content[i] == AutoTranslatePhrase.Close) return "Incomplete auto-translate phrase. Undo the edit or remove both brackets.";
            if (content[i] != AutoTranslatePhrase.Open) continue;
            var end = content.IndexOf(AutoTranslatePhrase.Close, i + 1);
            if (end < 0 || !phrases.Any(p => content.AsSpan(i, end-i+1).SequenceEqual(p.Token)))
                return "Unknown auto-translate phrase. Undo the edit or insert it again with Tab.";
            i = end;
        }
        return null;
    }

    public static byte[][] EncodeLines(MacroEntry entry)
    {
        var error = MacroRules.Error(entry.Name, entry.Content, entry.AutoTranslate);
        if (error is not null) throw new InvalidDataException(error);
        return MacroRules.Lines(entry.Content).Select(line => EncodeLine(line, entry.AutoTranslate)).ToArray();
    }

    private static byte[] EncodeLine(string line, IReadOnlyList<AutoTranslatePhrase> phrases)
    {
        using var stream = new MemoryStream();
        for (var i = 0; i < line.Length;)
        {
            var start = line.IndexOf(AutoTranslatePhrase.Open, i);
            if (start < 0) start = line.Length;
            stream.Write(Encoding.UTF8.GetBytes(line[i..start]));
            if (start == line.Length) break;
            var end = line.IndexOf(AutoTranslatePhrase.Close, start);
            var phrase = phrases.First(p => p.Token == line[start..(end+1)]);
            var body = Integer(phrase.Group - 1).Concat(Integer(phrase.Key)).ToArray();
            stream.WriteByte(2); stream.WriteByte(0x2E);
            stream.Write(Integer((uint)body.Length)); stream.Write(body); stream.WriteByte(3);
            i = end + 1;
        }
        return stream.ToArray();
    }

    private static byte[] Integer(uint value)
    {
        if (value < 0xCF) return [(byte)(value+1)];
        var bytes = new List<byte> { 0xF0 };
        for (var i = 3; i >= 0; i--)
        {
            var part = (byte)(value >> (i*8));
            if (part == 0) continue;
            bytes[0] |= (byte)(1 << i); bytes.Add(part);
        }
        bytes[0]--;
        return bytes.ToArray();
    }

    // Prefer the longest suffix matching a phrase ("Good morn"), keeping /p etc.
    public static (int Start, string Query) CompletionRange(string text, int cursor, IReadOnlyList<AutoTranslatePhrase> phrases)
    {
        cursor = Math.Clamp(cursor, 0, text.Length);
        var lineStart = text.LastIndexOf('\n', Math.Max(0, cursor-1), cursor) + 1;
        for (var start = lineStart; start < cursor; start++)
        {
            if (start > lineStart && !char.IsWhiteSpace(text[start-1])) continue;
            var query = text[start..cursor];
            if (query.Length > 0 && phrases.Any(p => p.Text.StartsWith(query, StringComparison.OrdinalIgnoreCase))) return (start, query);
        }
        var word = cursor;
        while (word > lineStart && !char.IsWhiteSpace(text[word-1]) && text[word-1] != AutoTranslatePhrase.Close) word--;
        return (word, text[word..cursor]);
    }
}
