using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using FFXIVClientStructs.FFXIV.Client.UI;
using MoreMacros.Core;

namespace MoreMacros;

internal static unsafe class NativeAutoTranslate
{
    // Snapshot the game's already-loaded catalogue on the framework thread.
    // No calls to Update/ClearCompletionData; the native chat input keeps its state.
    public static IReadOnlyList<AutoTranslatePhrase> ReadCatalog()
    {
        var ui = UIModule.Instance();
        if (ui == null) return [];
        var categories = ui->CompletionModule.CategoryData;
        if (categories.LongCount is <= 0 or > 256) return [];
        var phrases = new List<AutoTranslatePhrase>();
        foreach (var pointer in categories)
        {
            var category = pointer.Value;
            if (category == null || category->Group >= 200) continue;
            var count = Math.Min(category->CompletionTexts.LongCount, category->CompletionData.LongCount);
            if (count is < 0 or > 30000) continue;
            for (var i = 0; i < count; i++)
            {
                var data = category->CompletionData[i];
                var bytes = category->CompletionTexts[i].AsSpan();
                if (bytes.Length is 0 or > 2048 || data.RowId == 0) continue;
                var text = SeString.Parse(bytes).TextValue.Trim(AutoTranslatePhrase.Open, AutoTranslatePhrase.Close, ' ');
                if (text.Length == 0 || text.Any(char.IsControl)) continue;
                phrases.Add(new AutoTranslatePhrase((uint)category->Group + 1, data.RowId, text));
            }
        }
        return phrases.DistinctBy(p => p.Token).OrderBy(p => p.Text, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static string Decode(ReadOnlySpan<byte> bytes, List<AutoTranslatePhrase> phrases)
    {
        var result = new System.Text.StringBuilder();
        foreach (var payload in SeString.Parse(bytes).Payloads)
        {
            if (payload is TextPayload text) result.Append(text.Text);
            else if (payload is AutoTranslatePayload at)
            {
                var phrase = new AutoTranslatePhrase(at.Group, at.Key,
                    at.Text.Trim(AutoTranslatePhrase.Open, AutoTranslatePhrase.Close, ' '));
                if (!phrases.Contains(phrase)) phrases.Add(phrase);
                result.Append(phrase.Token);
            }
            else throw new InvalidDataException("This macro contains an unsupported text payload.");
        }
        return result.ToString();
    }
}
