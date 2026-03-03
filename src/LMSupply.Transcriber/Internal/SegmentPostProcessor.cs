using System.IO.Compression;
using System.Text;

namespace LMSupply.Transcriber.Internal;

/// <summary>
/// Post-processes transcription segments to remove hallucinations and low-quality output.
/// Pipeline: consecutive dedup → compression ratio filter → no-speech filter → re-index → rebuild text.
/// </summary>
internal static class SegmentPostProcessor
{
    /// <summary>
    /// Applies post-processing filters to transcription segments.
    /// </summary>
    public static (List<TranscriptionSegment> Segments, string Text) Process(
        List<TranscriptionSegment> segments, TranscribeOptions? options)
    {
        if (segments.Count == 0)
            return ([], string.Empty);

        var compressionThreshold = options?.CompressionRatioThreshold ?? 2.4f;
        var noSpeechThreshold = options?.NoSpeechThreshold ?? 0.6f;

        var filtered = new List<TranscriptionSegment>(segments.Count);

        // Step 1: Consecutive duplicate removal
        TranscriptionSegment? previous = null;
        foreach (var segment in segments)
        {
            if (previous != null &&
                string.Equals(segment.Text.Trim(), previous.Text.Trim(), StringComparison.Ordinal))
            {
                continue; // Skip consecutive duplicate
            }

            filtered.Add(segment);
            previous = segment;
        }

        // Step 2: Compression ratio filtering
        for (int i = filtered.Count - 1; i >= 0; i--)
        {
            var ratio = filtered[i].CompressionRatio ?? ComputeCompressionRatio(filtered[i].Text);
            if (ratio > compressionThreshold)
            {
                filtered.RemoveAt(i);
            }
        }

        // Step 3: No-speech probability filtering (only when value is populated)
        for (int i = filtered.Count - 1; i >= 0; i--)
        {
            var noSpeech = filtered[i].NoSpeechProb;
            if (noSpeech.HasValue && noSpeech.Value > noSpeechThreshold)
            {
                filtered.RemoveAt(i);
            }
        }

        // Step 4: Re-index segment IDs
        var result = new List<TranscriptionSegment>(filtered.Count);
        for (int i = 0; i < filtered.Count; i++)
        {
            var s = filtered[i];
            result.Add(new TranscriptionSegment
            {
                Id = i,
                Start = s.Start,
                End = s.End,
                Text = s.Text,
                AvgLogProb = s.AvgLogProb,
                NoSpeechProb = s.NoSpeechProb,
                CompressionRatio = s.CompressionRatio,
                Words = s.Words
            });
        }

        // Step 5: Rebuild text
        var text = string.Join(" ", result.Select(s => s.Text));

        return (result, text);
    }

    /// <summary>
    /// Computes text compression ratio using deflate, matching OpenAI Whisper's approach.
    /// Higher ratios indicate more repetitive text (potential hallucination).
    /// </summary>
    internal static float ComputeCompressionRatio(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 1.0f;

        var bytes = Encoding.UTF8.GetBytes(text);
        using var ms = new MemoryStream();
        using (var deflate = new DeflateStream(ms, CompressionLevel.Fastest, leaveOpen: true))
        {
            deflate.Write(bytes, 0, bytes.Length);
        }

        return (float)bytes.Length / Math.Max(ms.Length, 1);
    }
}
