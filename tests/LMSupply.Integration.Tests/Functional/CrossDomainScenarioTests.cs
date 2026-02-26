using LMSupply.Captioner;
using LMSupply.Detector;
using LMSupply.Embedder;
using LMSupply.Generator;
using LMSupply.Generator.Models;
using LMSupply.ImageGenerator;
using LMSupply.Integration.Tests.Helpers;
using LMSupply.Ocr;
using LMSupply.Reranker;
using LMSupply.Segmenter;
using LMSupply.Synthesizer;
using LMSupply.Transcriber;
using LMSupply.Translator;

namespace LMSupply.Integration.Tests.Functional;

/// <summary>
/// Cross-domain scenario tests that validate real-world pipelines
/// combining multiple AI domains in sequence.
/// Requires GPU + network access. Run locally only.
/// </summary>
[Trait("Category", "Functional")]
[Trait("Category", "LocalOnly")]
[Trait("Axis", "CrossDomain")]
public class CrossDomainScenarioTests
{
    // ── Scenario 1: TTS → STT Roundtrip (already in SynthesizerFunctionalTests) ──
    // Covered in Cycle 5

    // ── Scenario 5: RAG Pipeline (Embed → Rerank) ───────────────────

    [Fact]
    [Trait("Scenario", "RAG-Pipeline")]
    public async Task Scenario5_RagPipeline_EmbedThenRerank()
    {
        // 1. Create a small document corpus
        string[] documents =
        [
            "Machine learning is a subset of artificial intelligence that uses data to learn.",
            "The weather forecast predicts sunny skies for tomorrow.",
            "Deep learning uses neural networks with multiple layers.",
            "Cooking pasta requires boiling water and adding salt.",
            "Natural language processing helps computers understand human language.",
            "The stock market experienced significant volatility today.",
            "Reinforcement learning trains agents through reward signals.",
            "Gardening in spring requires proper soil preparation.",
            "Computer vision enables machines to interpret visual information.",
            "The new restaurant downtown serves excellent sushi.",
        ];

        var query = "What is machine learning?";

        // 2. Embed query and documents
        await using var embedder = await LocalEmbedder.LoadAsync("fast");

        var queryEmb = await embedder.EmbedAsync(query);
        var docEmbs = await embedder.EmbedAsync(documents);

        // 3. Compute cosine similarities → pick top 5
        var similarities = docEmbs
            .Select((emb, idx) => (Index: idx, Similarity: LocalEmbedder.CosineSimilarity(queryEmb, emb)))
            .OrderByDescending(x => x.Similarity)
            .Take(5)
            .ToList();

        similarities.Should().HaveCount(5);

        // ML-related documents should be in top 5
        var top5Indices = similarities.Select(s => s.Index).ToHashSet();
        top5Indices.Should().Contain(0, "ML definition should be in top 5 by embedding");
        top5Indices.Should().Contain(2, "deep learning should be in top 5 by embedding");

        // 4. Rerank the top 5
        var top5Docs = similarities.Select(s => documents[s.Index]).ToArray();

        await using var reranker = await LocalReranker.LoadAsync("fast");
        var reranked = await reranker.RerankAsync(query, top5Docs, topK: 3);

        reranked.Should().HaveCount(3);

        // The top reranked result should be ML-related
        reranked[0].Document.Should().ContainAny("machine", "Machine", "learning",
            "top reranked result should be ML-related");
    }

    // ── Scenario 4: Image Analysis Pipeline (Detect + Segment + Caption) ─

    [Fact]
    [Trait("Scenario", "ImageAnalysis-Pipeline")]
    public async Task Scenario4_ImageAnalysis_DetectSegmentCaption()
    {
        var imageBytes = TestDataHelper.CreateGradientBmp(640, 480);

        // 1. Detect objects
        await using var detector = await LocalDetector.LoadAsync("fast");
        var detections = await detector.DetectAsync(imageBytes);
        detections.Should().NotBeNull();

        // 2. Segment the image
        await using var segmenter = await LocalSegmenter.LoadAsync("fast");
        var segmentation = await segmenter.SegmentAsync(imageBytes);
        segmentation.Width.Should().BeGreaterThan(0);
        segmentation.Height.Should().BeGreaterThan(0);
        segmentation.ClassMap.Should().NotBeEmpty();

        // 3. Caption the image
        await using var captioner = await LocalCaptioner.LoadAsync("fast");
        var caption = await captioner.CaptionAsync(imageBytes);
        caption.Caption.Should().NotBeNullOrEmpty();

        // Pipeline completes without error — all three domains work on same image
    }

    // ── Scenario 6: Multilingual Pipeline (Translate → Embed) ───────

    [Fact]
    [Trait("Scenario", "Multilingual-Pipeline")]
    public async Task Scenario6_MultilingualPipeline_TranslateAndEmbed()
    {
        var koreanText = "기계 학습은 인공 지능의 한 분야입니다.";

        // 1. Translate Korean → English
        await using var translator = await LocalTranslator.LoadAsync("ko-en");
        var translation = await translator.TranslateAsync(koreanText);

        translation.TranslatedText.Should().NotBeNullOrEmpty();

        // 2. Embed the English translation
        await using var embedder = await LocalEmbedder.LoadAsync("fast");
        var translatedEmb = await embedder.EmbedAsync(translation.TranslatedText);

        // 3. Also embed a reference English text
        var referenceEmb = await embedder.EmbedAsync("Machine learning is a field of artificial intelligence.");

        // 4. The translated embedding should have meaningful similarity to reference
        var similarity = LocalEmbedder.CosineSimilarity(translatedEmb, referenceEmb);
        similarity.Should().BeGreaterThan(0.3f,
            "translated Korean text about ML should have >0.3 similarity to English reference");
    }

    // ── Scenario: Embed semantic consistency across languages ────────

    [Fact]
    [Trait("Scenario", "SemanticConsistency")]
    public async Task Scenario_EmbedAndRerank_ConsistentSemantics()
    {
        await using var embedder = await LocalEmbedder.LoadAsync("fast");

        // Create semantically related and unrelated pairs
        var catEmb = await embedder.EmbedAsync("cat");
        var kittenEmb = await embedder.EmbedAsync("kitten");
        var carEmb = await embedder.EmbedAsync("car");
        var vehicleEmb = await embedder.EmbedAsync("vehicle");

        var catKitten = LocalEmbedder.CosineSimilarity(catEmb, kittenEmb);
        var carVehicle = LocalEmbedder.CosineSimilarity(carEmb, vehicleEmb);
        var catCar = LocalEmbedder.CosineSimilarity(catEmb, carEmb);

        // Within-category similarity should exceed cross-category
        catKitten.Should().BeGreaterThan(catCar, "cat-kitten > cat-car");
        carVehicle.Should().BeGreaterThan(catCar, "car-vehicle > cat-car");

        // Now verify reranker agrees with embedder's ranking
        await using var reranker = await LocalReranker.LoadAsync("fast");

        string[] docs = ["kitten", "car", "dog", "airplane"];
        var reranked = await reranker.RerankAsync("cat", docs);

        // "kitten" should rank higher than "airplane" for query "cat"
        var kittenRank = reranked.ToList().FindIndex(r => r.Document == "kitten");
        var airplaneRank = reranked.ToList().FindIndex(r => r.Document == "airplane");
        kittenRank.Should().BeLessThan(airplaneRank,
            "reranker should rank 'kitten' above 'airplane' for query 'cat'");
    }

    // ── Scenario: Multiple models loaded simultaneously ─────────────

    [Fact]
    [Trait("Scenario", "MultiModel")]
    public async Task Scenario_MultipleModels_LoadedSimultaneously()
    {
        // Load 3 different domain models at the same time
        await using var embedder = await LocalEmbedder.LoadAsync("fast");
        await using var reranker = await LocalReranker.LoadAsync("fast");
        await using var translator = await LocalTranslator.LoadAsync("ko-en");

        // Use each one
        var emb = await embedder.EmbedAsync("test");
        emb.Length.Should().BeGreaterThan(0);

        string[] docs = ["hello", "world"];
        var ranked = await reranker.RerankAsync("hello", docs);
        ranked.Should().HaveCount(2);

        var translated = await translator.TranslateAsync("안녕하세요");
        translated.TranslatedText.Should().NotBeNullOrEmpty();

        // All three work without resource conflicts
    }

    // ── Scenario 2: OCR → Translate ──────────────────────────────────

    [Fact]
    [Trait("Scenario", "OCR-Translate")]
    public async Task Scenario2_OcrToTranslate_PipelineCompletes()
    {
        // 1. OCR: recognize text from an image (gradient has no real text, but pipeline should complete)
        await using var ocr = await LocalOcr.LoadAsync("fast");
        var imageBytes = TestDataHelper.CreateGradientBmp(400, 100);
        var ocrResult = await ocr.RecognizeAsync(imageBytes);

        ocrResult.Should().NotBeNull();
        ocrResult.FullText.Should().NotBeNull();

        // 2. Translate: if OCR found text, translate it; otherwise use fallback
        var textToTranslate = string.IsNullOrWhiteSpace(ocrResult.FullText)
            ? "기계 학습" // Fallback Korean text
            : ocrResult.FullText;

        await using var translator = await LocalTranslator.LoadAsync("ko-en");
        var translation = await translator.TranslateAsync(textToTranslate);

        translation.TranslatedText.Should().NotBeNullOrEmpty(
            "OCR → Translate pipeline should produce translated text");
    }

    // ── Scenario 3: Caption → ImageGen → Caption ─────────────────────

    [Fact]
    [Trait("Scenario", "Caption-ImageGen-Caption")]
    public async Task Scenario3_CaptionToImageGenToCaption_PipelineCompletes()
    {
        // 1. Caption original image
        await using var captioner = await LocalCaptioner.LoadAsync("fast");
        var imageBytes = TestDataHelper.CreateGradientBmp(256, 256);
        var originalCaption = await captioner.CaptionAsync(imageBytes);

        originalCaption.Caption.Should().NotBeNullOrEmpty();

        // 2. Generate new image from caption
        await using var generator = await LocalImageGenerator.LoadAsync("fast");
        var generated = await generator.GenerateAsync(
            originalCaption.Caption,
            new ImageGenerator.GenerationOptions { Steps = 2, Seed = 42 });

        generated.ImageData.Should().NotBeEmpty();

        // 3. Re-caption the generated image
        var reCaption = await captioner.CaptionAsync(generated.ImageData);

        reCaption.Caption.Should().NotBeNullOrEmpty(
            "re-captioning generated image should produce text");

        // Pipeline completes — caption→generate→re-caption works end-to-end
    }

    // ── Scenario: Transcribe → Translate ─────────────────────────────

    [Fact]
    [Trait("Scenario", "Transcribe-Translate")]
    public async Task Scenario_TranscribeToTranslate_PipelineCompletes()
    {
        // 1. Synthesize some speech first (so we have audio to transcribe)
        await using var synthesizer = await LocalSynthesizer.LoadAsync("fast");
        var synthesis = await synthesizer.SynthesizeAsync("Hello, how are you today?");
        var wavBytes = synthesis.ToWavBytes();

        // 2. Transcribe the speech to text
        await using var transcriber = await LocalTranscriber.LoadAsync("fast");
        var transcription = await transcriber.TranscribeAsync(wavBytes);

        transcription.Text.Should().NotBeNullOrEmpty("transcription should produce text");

        // 3. Translate the transcription to Korean
        await using var translator = await LocalTranslator.LoadAsync("en-ko");
        var translation = await translator.TranslateAsync(transcription.Text);

        translation.TranslatedText.Should().NotBeNullOrEmpty(
            "Synthesize → Transcribe → Translate pipeline should produce translated text");
    }

    // ── Scenario: Generate → Embed → similarity check ────────────────

    [Fact]
    [Trait("Scenario", "Generate-Embed")]
    public async Task Scenario_GenerateAndEmbed_OutputHasSemanticMeaning()
    {
        // 1. Generate text about a topic
        await using var generator = await LocalGenerator.LoadAsync("gguf:fast");
        var generated = await generator.GenerateCompleteAsync(
            "Machine learning is",
            new Generator.Models.GenerationOptions { MaxTokens = 30 });

        generated.Should().NotBeNullOrEmpty();

        // 2. Embed the generated text and a reference
        await using var embedder = await LocalEmbedder.LoadAsync("fast");
        var generatedEmb = await embedder.EmbedAsync("Machine learning is " + generated);
        var referenceEmb = await embedder.EmbedAsync("Artificial intelligence and machine learning");
        var unrelatedEmb = await embedder.EmbedAsync("Italian pasta recipes and cooking techniques");

        // 3. Generated text about ML should be more similar to ML reference than to cooking
        var relevantSim = LocalEmbedder.CosineSimilarity(generatedEmb, referenceEmb);
        var unrelatedSim = LocalEmbedder.CosineSimilarity(generatedEmb, unrelatedEmb);

        relevantSim.Should().BeGreaterThan(unrelatedSim,
            "generated ML text should be semantically closer to ML reference than to cooking");
    }

    // ── Scenario: Synthesize → Transcribe → Embed quality check ──────

    [Fact]
    [Trait("Scenario", "TTS-STT-Embed")]
    public async Task Scenario_SynthesizeTranscribeEmbed_SemanticPreservation()
    {
        var originalText = "The weather is beautiful today.";

        // 1. Synthesize text to speech
        await using var synthesizer = await LocalSynthesizer.LoadAsync("fast");
        var audio = await synthesizer.SynthesizeAsync(originalText);
        var wavBytes = audio.ToWavBytes();

        // 2. Transcribe back to text
        await using var transcriber = await LocalTranscriber.LoadAsync("fast");
        var transcription = await transcriber.TranscribeAsync(wavBytes);

        transcription.Text.Should().NotBeNullOrEmpty();

        // 3. Embed original and transcribed text
        await using var embedder = await LocalEmbedder.LoadAsync("fast");
        var originalEmb = await embedder.EmbedAsync(originalText);
        var transcribedEmb = await embedder.EmbedAsync(transcription.Text);
        var unrelatedEmb = await embedder.EmbedAsync("Database query optimization techniques");

        // 4. TTS→STT output should be semantically closer to original than to unrelated text
        var relevantSim = LocalEmbedder.CosineSimilarity(originalEmb, transcribedEmb);
        var unrelatedSim = LocalEmbedder.CosineSimilarity(originalEmb, unrelatedEmb);

        relevantSim.Should().BeGreaterThan(unrelatedSim,
            "TTS→STT roundtrip should preserve semantic meaning");
    }

    // ── Scenario 6 Full: Translate → Embed → Generate ────────────────

    [Fact]
    [Trait("Scenario", "Multilingual-Full-Pipeline")]
    public async Task Scenario6_Full_TranslateEmbedGenerate()
    {
        var koreanText = "인공지능은 컴퓨터가 사람처럼 생각하게 만드는 기술입니다.";

        // 1. Translate Korean → English
        await using var translator = await LocalTranslator.LoadAsync("ko-en");
        var translation = await translator.TranslateAsync(koreanText);
        translation.TranslatedText.Should().NotBeNullOrEmpty();

        // 2. Embed the English translation + reference
        await using var embedder = await LocalEmbedder.LoadAsync("fast");
        var translatedEmb = await embedder.EmbedAsync(translation.TranslatedText);
        translatedEmb.Length.Should().BeGreaterThan(0);

        // 3. Generate a summary using the translated text
        await using var generator = await LocalGenerator.LoadAsync("gguf:fast");
        var summary = await generator.GenerateCompleteAsync(
            $"Summarize in one sentence: {translation.TranslatedText}",
            new Generator.Models.GenerationOptions { MaxTokens = 50 });

        summary.Should().NotBeNullOrEmpty(
            "Translate → Embed → Generate pipeline should produce summary text");

        // 4. Verify semantic consistency: summary should be closer to translation than to unrelated
        var summaryEmb = await embedder.EmbedAsync(summary);
        var unrelatedEmb = await embedder.EmbedAsync("A recipe for chocolate cake with eggs and flour.");

        var relevantSim = LocalEmbedder.CosineSimilarity(translatedEmb, summaryEmb);
        var unrelatedSim = LocalEmbedder.CosineSimilarity(translatedEmb, unrelatedEmb);

        relevantSim.Should().BeGreaterThan(unrelatedSim,
            "generated summary should be semantically closer to source than to unrelated text");
    }

    // ── Scenario: Parallel inference across domains ──────────────────

    [Fact]
    [Trait("Scenario", "ParallelInference")]
    public async Task Scenario_ParallelInference_AllCompleteWithoutConflict()
    {
        // Load models
        await using var embedder = await LocalEmbedder.LoadAsync("fast");
        await using var reranker = await LocalReranker.LoadAsync("fast");
        await using var synthesizer = await LocalSynthesizer.LoadAsync("fast");

        // Run inference in parallel across different domains
        var embedTask = embedder.EmbedAsync("parallel test").AsTask();
        var rerankTask = reranker.RerankAsync("test", ["hello", "world", "test"]);
        var synthTask = synthesizer.SynthesizeAsync("parallel");

        await Task.WhenAll(embedTask, rerankTask, synthTask);

        var emb = await embedTask;
        var reranked = await rerankTask;
        var audio = await synthTask;

        emb.Length.Should().BeGreaterThan(0);
        reranked.Should().NotBeEmpty();
        audio.AudioSamples.Should().NotBeEmpty();
    }
}
