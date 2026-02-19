using FluentAssertions;
using LMSupply.Transcriber.Decoding;

namespace LMSupply.Transcriber.Tests;

/// <summary>
/// Tests for Whisper language detection: token mapping, probability calculation, and SOT sequence.
/// </summary>
public class WhisperLanguageDetectionTests
{
    [Fact]
    public void GetLanguageToken_KnownLanguages_ReturnValidTokenInRange()
    {
        // Tokens are computed as LanguageTokenStart + index in SupportedLanguages
        string[] testLanguages = ["en", "zh", "de", "es", "ko", "ja", "fr"];

        foreach (var lang in testLanguages)
        {
            var token = WhisperTokenizer.GetLanguageToken(lang);
            token.Should().NotBeNull($"'{lang}' should have a valid token");
            token!.Value.Should().BeInRange(
                WhisperTokenizer.LanguageTokenStart,
                WhisperTokenizer.LanguageTokenEnd,
                $"token for '{lang}' should be in language token range");
        }
    }

    [Fact]
    public void GetLanguageToken_FirstLanguageIsEnglish()
    {
        // "en" is the first entry in SupportedLanguages → index 0 → token 50259
        WhisperTokenizer.GetLanguageToken("en").Should().Be(WhisperTokenizer.LanguageTokenStart);
    }

    [Fact]
    public void GetLanguageFromToken_FirstToken_ReturnsEnglish()
    {
        WhisperTokenizer.GetLanguageFromToken(WhisperTokenizer.LanguageTokenStart)
            .Should().Be("en");
    }

    [Fact]
    public void GetLanguageFromToken_KnownTokens_ReturnCorrectLanguage()
    {
        // Verify a few known positions by looking up the dictionary order
        var languages = WhisperTokenizer.SupportedLanguages.Keys.ToList();

        for (int i = 0; i < Math.Min(10, languages.Count); i++)
        {
            var token = WhisperTokenizer.LanguageTokenStart + i;
            var language = WhisperTokenizer.GetLanguageFromToken(token);
            language.Should().Be(languages[i]);
        }
    }

    [Fact]
    public void LanguageToken_RoundTrip_AllLanguagesInTokenRange()
    {
        foreach (var (code, _) in WhisperTokenizer.SupportedLanguages)
        {
            var token = WhisperTokenizer.GetLanguageToken(code);
            token.Should().NotBeNull($"language '{code}' should have a valid token");

            // Only test round-trip for tokens within the valid language token range.
            // Some languages (e.g., "yue"/Cantonese, added in Whisper large-v3) have tokens
            // that exceed the standard range (50259-50357) and overlap with special tokens.
            if (!WhisperTokenizer.IsLanguageToken(token!.Value))
                continue;

            var roundTripped = WhisperTokenizer.GetLanguageFromToken(token.Value);
            roundTripped.Should().Be(code, $"round-trip for '{code}' should match");
        }
    }

    [Theory]
    [InlineData(50259, true)]   // en
    [InlineData(50357, true)]   // last language token
    [InlineData(50258, false)]  // StartOfTranscript
    [InlineData(50358, false)]  // Translate token (just past range)
    [InlineData(0, false)]
    public void IsLanguageToken_CorrectlyIdentifiesRange(int tokenId, bool expected)
    {
        WhisperTokenizer.IsLanguageToken(tokenId).Should().Be(expected);
    }

    [Fact]
    public void GetLanguageFromToken_NonLanguageToken_ReturnsNull()
    {
        WhisperTokenizer.GetLanguageFromToken(0).Should().BeNull();
        WhisperTokenizer.GetLanguageFromToken(50258).Should().BeNull();
        WhisperTokenizer.GetLanguageFromToken(50358).Should().BeNull();
    }

    [Fact]
    public void GetLanguageToken_InvalidCode_ReturnsNull()
    {
        WhisperTokenizer.GetLanguageToken("xx").Should().BeNull();
        WhisperTokenizer.GetLanguageToken("invalid").Should().BeNull();
    }

    [Fact]
    public void GetSotSequence_NoLanguage_OmitsLanguageToken()
    {
        var sot = WhisperTokenizer.GetSotSequence(language: null, timestamps: false);

        sot.Should().Contain(WhisperTokenizer.StartOfTranscriptToken);
        sot.Should().Contain(WhisperTokenizer.TranscribeToken);
        sot.Should().Contain(WhisperTokenizer.NoTimestampsToken);
        // No language token
        sot.Should().NotContain(t => WhisperTokenizer.IsLanguageToken(t));
    }

    [Fact]
    public void GetSotSequence_WithLanguage_IncludesLanguageToken()
    {
        var sot = WhisperTokenizer.GetSotSequence(language: "en", timestamps: false);

        sot.Should().Contain(WhisperTokenizer.StartOfTranscriptToken);
        sot.Should().Contain(50259); // English language token
        sot.Should().Contain(WhisperTokenizer.TranscribeToken);
    }

    [Fact]
    public void ComputeLanguageTokenProbability_DominantToken_ReturnsHighProbability()
    {
        // Create logits array where English token has much higher value
        var logits = new float[51000];
        var enToken = WhisperTokenizer.LanguageTokenStart; // 50259 = "en"

        // Set all language tokens to low value
        for (int i = WhisperTokenizer.LanguageTokenStart; i <= WhisperTokenizer.LanguageTokenEnd; i++)
        {
            logits[i] = -10f;
        }

        // Set English token very high
        logits[enToken] = 10f;

        var prob = WhisperDecoder.ComputeLanguageTokenProbability(logits, enToken);

        prob.Should().BeGreaterThan(0.99f, "dominant token should have near-1.0 probability");
    }

    [Fact]
    public void ComputeLanguageTokenProbability_UniformLogits_ReturnsEqualProbability()
    {
        var logits = new float[51000];

        // All language tokens have equal logits
        for (int i = WhisperTokenizer.LanguageTokenStart; i <= WhisperTokenizer.LanguageTokenEnd; i++)
        {
            logits[i] = 5.0f;
        }

        var languageCount = WhisperTokenizer.LanguageTokenEnd - WhisperTokenizer.LanguageTokenStart + 1;
        var expectedProb = 1.0f / languageCount;

        var prob = WhisperDecoder.ComputeLanguageTokenProbability(logits, WhisperTokenizer.LanguageTokenStart);

        prob.Should().BeApproximately(expectedProb, 0.001f,
            "uniform logits should yield equal probability for each token");
    }

    [Fact]
    public void ComputeLanguageTokenProbability_OutOfRange_ReturnsZero()
    {
        var logits = new float[51000];

        // Token outside language range
        WhisperDecoder.ComputeLanguageTokenProbability(logits, 0).Should().Be(0f);
        WhisperDecoder.ComputeLanguageTokenProbability(logits, 50358).Should().Be(0f);
    }

    [Fact]
    public void ComputeLanguageTokenProbability_ShortLogitsArray_ReturnsZero()
    {
        // Logits array too short to contain language tokens
        var logits = new float[100];

        WhisperDecoder.ComputeLanguageTokenProbability(logits, WhisperTokenizer.LanguageTokenStart)
            .Should().Be(0f);
    }

    [Fact]
    public void ComputeLanguageTokenProbability_TwoCompetingLanguages_SplitsProbability()
    {
        var logits = new float[51000];

        // Set all language tokens very low
        for (int i = WhisperTokenizer.LanguageTokenStart; i <= WhisperTokenizer.LanguageTokenEnd; i++)
        {
            logits[i] = -100f;
        }

        // Set en and zh to equal high values
        var enToken = WhisperTokenizer.LanguageTokenStart;      // en
        var zhToken = WhisperTokenizer.LanguageTokenStart + 1;  // zh
        logits[enToken] = 10f;
        logits[zhToken] = 10f;

        var enProb = WhisperDecoder.ComputeLanguageTokenProbability(logits, enToken);
        var zhProb = WhisperDecoder.ComputeLanguageTokenProbability(logits, zhToken);

        enProb.Should().BeApproximately(0.5f, 0.01f);
        zhProb.Should().BeApproximately(0.5f, 0.01f);
    }
}
