using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace LMSupply.Transcriber.Decoding;

/// <summary>
/// Whisper-specific tokenizer for decoding generated token IDs to text.
/// Uses GPT-2 BPE tokenizer with Whisper's special tokens.
/// </summary>
internal sealed class WhisperTokenizer : IDisposable
{
    /// <summary>
    /// Supported Whisper language codes mapped to ISO 639-1 codes.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> SupportedLanguages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = "english", ["zh"] = "chinese", ["de"] = "german", ["es"] = "spanish",
        ["ru"] = "russian", ["ko"] = "korean", ["fr"] = "french", ["ja"] = "japanese",
        ["pt"] = "portuguese", ["tr"] = "turkish", ["pl"] = "polish", ["ca"] = "catalan",
        ["nl"] = "dutch", ["ar"] = "arabic", ["sv"] = "swedish", ["it"] = "italian",
        ["id"] = "indonesian", ["hi"] = "hindi", ["fi"] = "finnish", ["vi"] = "vietnamese",
        ["he"] = "hebrew", ["uk"] = "ukrainian", ["el"] = "greek", ["ms"] = "malay",
        ["cs"] = "czech", ["ro"] = "romanian", ["da"] = "danish", ["hu"] = "hungarian",
        ["ta"] = "tamil", ["no"] = "norwegian", ["th"] = "thai", ["ur"] = "urdu",
        ["hr"] = "croatian", ["bg"] = "bulgarian", ["lt"] = "lithuanian", ["la"] = "latin",
        ["mi"] = "maori", ["ml"] = "malayalam", ["cy"] = "welsh", ["sk"] = "slovak",
        ["te"] = "telugu", ["fa"] = "persian", ["lv"] = "latvian", ["bn"] = "bengali",
        ["sr"] = "serbian", ["az"] = "azerbaijani", ["sl"] = "slovenian", ["kn"] = "kannada",
        ["et"] = "estonian", ["mk"] = "macedonian", ["br"] = "breton", ["eu"] = "basque",
        ["is"] = "icelandic", ["hy"] = "armenian", ["ne"] = "nepali", ["mn"] = "mongolian",
        ["bs"] = "bosnian", ["kk"] = "kazakh", ["sq"] = "albanian", ["sw"] = "swahili",
        ["gl"] = "galician", ["mr"] = "marathi", ["pa"] = "punjabi", ["si"] = "sinhala",
        ["km"] = "khmer", ["sn"] = "shona", ["yo"] = "yoruba", ["so"] = "somali",
        ["af"] = "afrikaans", ["oc"] = "occitan", ["ka"] = "georgian", ["be"] = "belarusian",
        ["tg"] = "tajik", ["sd"] = "sindhi", ["gu"] = "gujarati", ["am"] = "amharic",
        ["yi"] = "yiddish", ["lo"] = "lao", ["uz"] = "uzbek", ["fo"] = "faroese",
        ["ht"] = "haitian creole", ["ps"] = "pashto", ["tk"] = "turkmen", ["nn"] = "nynorsk",
        ["mt"] = "maltese", ["sa"] = "sanskrit", ["lb"] = "luxembourgish", ["my"] = "myanmar",
        ["bo"] = "tibetan", ["tl"] = "tagalog", ["mg"] = "malagasy", ["as"] = "assamese",
        ["tt"] = "tatar", ["haw"] = "hawaiian", ["ln"] = "lingala", ["ha"] = "hausa",
        ["ba"] = "bashkir", ["jw"] = "javanese", ["su"] = "sundanese", ["yue"] = "cantonese"
    };
    private readonly Dictionary<int, string> _idToToken;
    private readonly Dictionary<string, int> _tokenToId;
    private readonly Dictionary<string, string> _bytesToUnicode;

    // Whisper special tokens
    public const int EndOfTextToken = 50257;       // <|endoftext|>
    public const int StartOfTranscriptToken = 50258; // <|startoftranscript|>
    public const int TranslateToken = 50358;       // <|translate|>
    public const int TranscribeToken = 50359;      // <|transcribe|>
    public const int StartOfLmToken = 50360;       // <|startoflm|>
    public const int StartOfPrevToken = 50361;     // <|startofprev|>
    public const int NoSpeechToken = 50362;        // <|nospeech|>
    public const int NoTimestampsToken = 50363;    // <|notimestamps|>

    // First timestamp token (0.00s)
    public const int TimestampBeginToken = 50364;

    // Language token range
    public const int LanguageTokenStart = 50259;
    public const int LanguageTokenEnd = 50357; // Inclusive

    public int VocabSize { get; }

    private WhisperTokenizer(
        Dictionary<int, string> idToToken,
        Dictionary<string, int> tokenToId)
    {
        _idToToken = idToToken;
        _tokenToId = tokenToId;
        _bytesToUnicode = CreateBytesToUnicode();
        VocabSize = idToToken.Count;
    }

    /// <summary>
    /// Creates a tokenizer from a model directory containing vocab.json.
    /// </summary>
    public static async Task<WhisperTokenizer> LoadAsync(string modelDir, CancellationToken cancellationToken = default)
    {
        var vocabPath = Path.Combine(modelDir, "vocab.json");
        if (!File.Exists(vocabPath))
        {
            throw new FileNotFoundException($"Vocabulary file not found: {vocabPath}");
        }

        var json = await File.ReadAllTextAsync(vocabPath, cancellationToken);
        using var doc = JsonDocument.Parse(json);

        var tokenToId = new Dictionary<string, int>(StringComparer.Ordinal);
        var idToToken = new Dictionary<int, string>();

        foreach (var property in doc.RootElement.EnumerateObject())
        {
            var token = property.Name;
            var id = property.Value.GetInt32();
            tokenToId[token] = id;
            idToToken[id] = token;
        }

        return new WhisperTokenizer(idToToken, tokenToId);
    }

    /// <summary>
    /// Decodes a sequence of token IDs to text.
    /// </summary>
    public string Decode(ReadOnlySpan<int> tokenIds, bool skipSpecialTokens = true)
    {
        var sb = new StringBuilder();

        foreach (var tokenId in tokenIds)
        {
            if (skipSpecialTokens && IsSpecialToken(tokenId))
                continue;

            if (_idToToken.TryGetValue(tokenId, out var token))
            {
                sb.Append(token);
            }
        }

        // Convert GPT-2 BPE tokens back to text
        return DecodeBytes(sb.ToString());
    }

    /// <summary>
    /// Gets the token ID for a given token string.
    /// </summary>
    public int? GetTokenId(string token)
    {
        return _tokenToId.TryGetValue(token, out var id) ? id : null;
    }

    /// <summary>
    /// Gets the token string for a given token ID.
    /// </summary>
    public string? GetToken(int tokenId)
    {
        return _idToToken.TryGetValue(tokenId, out var token) ? token : null;
    }

    /// <summary>
    /// Checks if a token ID is a special token.
    /// </summary>
    public static bool IsSpecialToken(int tokenId)
    {
        return tokenId >= EndOfTextToken;
    }

    /// <summary>
    /// Checks if a token ID is a timestamp token.
    /// </summary>
    public static bool IsTimestampToken(int tokenId)
    {
        return tokenId >= TimestampBeginToken;
    }

    /// <summary>
    /// Converts a timestamp token to seconds.
    /// </summary>
    public static float TimestampTokenToSeconds(int tokenId)
    {
        if (tokenId < TimestampBeginToken)
            return 0f;

        return (tokenId - TimestampBeginToken) * 0.02f; // Each token = 20ms
    }

    /// <summary>
    /// Checks if a token ID is a language token.
    /// </summary>
    public static bool IsLanguageToken(int tokenId)
    {
        return tokenId >= LanguageTokenStart && tokenId <= LanguageTokenEnd;
    }

    /// <summary>
    /// Gets the language code for a language token.
    /// </summary>
    public static string? GetLanguageFromToken(int tokenId)
    {
        if (!IsLanguageToken(tokenId))
            return null;

        // Language tokens are computed as LanguageTokenStart + index
        // Reverse the calculation to get the language code
        var index = tokenId - LanguageTokenStart;
        var languageCodes = SupportedLanguages.Keys.ToList();
        
        if (index >= 0 && index < languageCodes.Count)
        {
            var lang = languageCodes[index];
            return lang;
        }

        return null;
    }

    /// <summary>
    /// Gets the token ID for a language code.
    /// </summary>
    public static int? GetLanguageToken(string languageCode)
    {
        // Language tokens are NOT in vocab.json - they're computed as offsets from LanguageTokenStart
        // The order matches the SupportedLanguages dictionary order
        var languageCodes = SupportedLanguages.Keys.ToList();
        var index = languageCodes.FindIndex(c => c.Equals(languageCode, StringComparison.OrdinalIgnoreCase));
        
        if (index >= 0)
        {
            var token = LanguageTokenStart + index;
            return token;
        }
        
        return null;
    }

    /// <summary>
    /// Gets the SOT (start of transcript) sequence for transcription.
    /// </summary>
    /// <param name="language">ISO 639-1 language code (e.g., "en", "zh", "ja").</param>
    /// <param name="timestamps">Whether to include timestamps in output.</param>
    /// <returns>Array of token IDs for the SOT sequence.</returns>
    public static int[] GetSotSequence(string? language = null, bool timestamps = false)
    {
        var tokens = new List<int> { StartOfTranscriptToken };

        // Add language token
        if (language != null)
        {
            // Normalize language code (lowercase, handle common aliases)
            var normalizedLang = NormalizeLanguageCode(language);
            var langToken = GetLanguageToken(normalizedLang);

            if (langToken.HasValue)
            {
                tokens.Add(langToken.Value);
                Trace.TraceInformation($"[WhisperTokenizer] Using language: {normalizedLang} (token: {langToken.Value})");
            }
            else
            {
                Trace.TraceWarning($"[WhisperTokenizer] Warning: Language '{language}' not found in vocabulary. " +
                    $"Supported codes: {string.Join(", ", SupportedLanguages.Keys.Take(10))}...");
            }
        }
        else
        {
            Trace.TraceInformation("[WhisperTokenizer] No language specified, model will auto-detect language.");
        }

        // Add task token (transcribe)
        tokens.Add(TranscribeToken);

        // Add no timestamps token if not using timestamps
        if (!timestamps)
        {
            tokens.Add(NoTimestampsToken);
        }

        Trace.TraceInformation($"[WhisperTokenizer] SOT sequence: [{string.Join(", ", tokens)}]");
        return [.. tokens];
    }

    /// <summary>
    /// Normalizes a language code to the format expected by Whisper.
    /// </summary>
    private static string NormalizeLanguageCode(string language)
    {
        // Handle common aliases and normalize to lowercase
        var normalized = language.ToLowerInvariant().Trim();

        // Handle full language names -> ISO codes
        return normalized switch
        {
            "chinese" or "mandarin" => "zh",
            "english" => "en",
            "japanese" => "ja",
            "korean" => "ko",
            "spanish" => "es",
            "french" => "fr",
            "german" => "de",
            "italian" => "it",
            "portuguese" => "pt",
            "russian" => "ru",
            "arabic" => "ar",
            "hindi" => "hi",
            "turkish" => "tr",
            "vietnamese" => "vi",
            "thai" => "th",
            "indonesian" => "id",
            "dutch" => "nl",
            "polish" => "pl",
            "swedish" => "sv",
            "hebrew" => "he",
            "cantonese" => "yue",
            _ => normalized
        };
    }

    // GPT-2 uses a specific byte-to-unicode mapping for BPE tokens
    private static Dictionary<string, string> CreateBytesToUnicode()
    {
        var bs = new List<int>();
        // Visible ASCII (33-126)
        for (int i = 33; i <= 126; i++) bs.Add(i);
        // Latin supplement (161-172, 174-255)
        for (int i = 161; i <= 172; i++) bs.Add(i);
        for (int i = 174; i <= 255; i++) bs.Add(i);

        var cs = new List<int>(bs);
        int n = 0;
        for (int b = 0; b < 256; b++)
        {
            if (!bs.Contains(b))
            {
                bs.Add(b);
                cs.Add(256 + n);
                n++;
            }
        }

        var result = new Dictionary<string, string>();
        for (int i = 0; i < bs.Count; i++)
        {
            result[((char)cs[i]).ToString()] = ((char)bs[i]).ToString();
        }

        return result;
    }

    private string DecodeBytes(string text)
    {
        var sb = new StringBuilder();
        foreach (var c in text)
        {
            var cs = c.ToString();
            if (_bytesToUnicode.TryGetValue(cs, out var decoded))
            {
                sb.Append(decoded);
            }
            else
            {
                sb.Append(c);
            }
        }

        // The result might have multi-byte UTF-8 sequences
        // We need to decode them properly
        try
        {
            var bytes = new byte[sb.Length];
            for (int i = 0; i < sb.Length; i++)
            {
                bytes[i] = (byte)sb[i];
            }
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            // Fallback to direct string if UTF-8 decoding fails
            return sb.ToString();
        }
    }

    public void Dispose()
    {
        // No unmanaged resources
    }
}
