namespace Lmm.Translation;

/// <summary>Machine translation backend. Implementations must be safe to call concurrently.</summary>
public interface ITranslationProvider
{
    string Name { get; }

    /// <summary>Translate <paramref name="text"/>; implementations may chunk long input.</summary>
    Task<string> TranslateAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken cancellationToken = default);
}

/// <summary>
/// Free MyMemory endpoint, the same backend the reference courseware uses.
/// No API key, ~500 bytes per request, so paragraphs are chunked on sentence
/// boundaries before being sent.
/// </summary>
public sealed class MyMemoryTranslationProvider : ITranslationProvider
{
    private const int ChunkLimit = 420;

    private readonly HttpClient _http;

    public MyMemoryTranslationProvider(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("InkCase/0.1");
    }

    public string Name => "MyMemory";

    public async Task<string> TranslateAsync(
        string text,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";

        var parts = new List<string>();
        foreach (var chunk in Chunk(text))
            parts.Add(await TranslateChunkAsync(chunk, sourceLanguage, targetLanguage, cancellationToken));
        return string.Join("", parts);
    }

    private async Task<string> TranslateChunkAsync(string chunk, string from, string to, CancellationToken ct)
    {
        var url = $"https://api.mymemory.translated.net/get?q={Uri.EscapeDataString(chunk)}&langpair={from}|{to}";
        using var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var document = await System.Text.Json.JsonDocument.ParseAsync(stream, cancellationToken: ct);

        if (!document.RootElement.TryGetProperty("responseData", out var data) ||
            !data.TryGetProperty("translatedText", out var translated))
        {
            throw new TranslationException("翻译服务返回了意外的数据格式。");
        }

        var result = translated.GetString() ?? "";
        if (result.Contains("MYMEMORY WARNING", StringComparison.OrdinalIgnoreCase))
            throw new TranslationException("翻译服务今日配额已用完，请稍后再试或换用别的翻译源。");

        return result;
    }

    /// <summary>Split on sentence ends, keeping every chunk under the service's byte limit.</summary>
    internal static IEnumerable<string> Chunk(string text)
    {
        var sentences = System.Text.RegularExpressions.Regex.Split(text, @"(?<=[.!?;])\s+");
        var buffer = new System.Text.StringBuilder();

        foreach (var sentence in sentences)
        {
            if (sentence.Length == 0) continue;

            if (buffer.Length > 0 && buffer.Length + sentence.Length + 1 > ChunkLimit)
            {
                yield return buffer.ToString();
                buffer.Clear();
            }

            if (sentence.Length > ChunkLimit)
            {
                foreach (var piece in HardSplit(sentence, ChunkLimit)) yield return piece;
                continue;
            }

            if (buffer.Length > 0) buffer.Append(' ');
            buffer.Append(sentence);
        }

        if (buffer.Length > 0) yield return buffer.ToString();
    }

    private static IEnumerable<string> HardSplit(string value, int size)
    {
        for (int i = 0; i < value.Length; i += size)
            yield return value.Substring(i, Math.Min(size, value.Length - i));
    }
}

public sealed class TranslationException(string message) : Exception(message);
