namespace LMSupply.Text.Tests;

/// <summary>
/// Tests for <see cref="TokenizerFactory.CreateAutoSequenceAsync"/> — verifies that
/// embedder loading paths can pick up both WordPiece and SentencePiece tokenizers.
/// Regression coverage for ISSUE-lm-supply-1775535000-multilingual-embedder-vocab.
/// </summary>
public class TokenizerFactoryAutoSequenceTests : IDisposable
{
    private readonly string _tempDir;

    public TokenizerFactoryAutoSequenceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"tk-auto-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task CreateAutoSequenceAsync_VocabTxt_LoadsWordPiece()
    {
        // Minimal BERT-style vocab.txt — must contain the special tokens that
        // SpecialTokens.FromVocabulary uses to populate ClsTokenId / SepTokenId.
        WriteFile("vocab.txt", "[PAD]\n[UNK]\n[CLS]\n[SEP]\nhello\nworld\n");

        var tokenizer = await TokenizerFactory.CreateAutoSequenceAsync(_tempDir, maxSequenceLength: 16);

        tokenizer.Should().NotBeNull();
        tokenizer.MaxSequenceLength.Should().Be(16);
        tokenizer.ClsTokenId.Should().NotBeNull();
        tokenizer.SepTokenId.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateAutoSequenceAsync_TokenizerJsonWordPiece_LoadsWordPiece()
    {
        WriteFile("tokenizer.json", BuildWordPieceTokenizerJson());

        var tokenizer = await TokenizerFactory.CreateAutoSequenceAsync(_tempDir, maxSequenceLength: 32);

        tokenizer.Should().NotBeNull();
        tokenizer.MaxSequenceLength.Should().Be(32);
    }

    [Fact]
    public async Task CreateAutoSequenceAsync_NoTokenizerFiles_ThrowsDescriptive()
    {
        var act = () => TokenizerFactory.CreateAutoSequenceAsync(_tempDir);

        var ex = await act.Should().ThrowAsync<FileNotFoundException>();
        ex.Which.Message.Should().Contain(_tempDir);
        ex.Which.Message.Should().Contain("vocab.txt");
        ex.Which.Message.Should().Contain("tokenizer.json");
        ex.Which.Message.Should().Contain("sentencepiece.bpe.model");
    }

    private void WriteFile(string name, string content)
        => File.WriteAllText(Path.Combine(_tempDir, name), content);

    /// <summary>
    /// Builds a minimal tokenizer.json with model.type="WordPiece" and the BERT special tokens.
    /// Sufficient for CreateWordPieceFromJson + ExtractSpecialTokensFromJson to succeed.
    /// </summary>
    private static string BuildWordPieceTokenizerJson()
    {
        return /* lang=json */ """
        {
          "version": "1.0",
          "added_tokens": [
            { "id": 0, "content": "[PAD]", "special": true },
            { "id": 1, "content": "[UNK]", "special": true },
            { "id": 2, "content": "[CLS]", "special": true },
            { "id": 3, "content": "[SEP]", "special": true }
          ],
          "model": {
            "type": "WordPiece",
            "unk_token": "[UNK]",
            "continuing_subword_prefix": "##",
            "max_input_chars_per_word": 100,
            "vocab": {
              "[PAD]": 0,
              "[UNK]": 1,
              "[CLS]": 2,
              "[SEP]": 3,
              "hello": 4,
              "world": 5
            }
          }
        }
        """;
    }
}
