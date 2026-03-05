using NAudio.Wave;

namespace LMSupply.Console.Host.Infrastructure.Audio;

/// <summary>
/// Converts WAV audio bytes to various output formats.
/// </summary>
public static class AudioConverter
{
    /// <summary>
    /// Converts WAV bytes to the target format.
    /// Returns (data, contentType) pair.
    /// </summary>
    public static (byte[] Data, string ContentType) Convert(byte[] wavBytes, string format)
    {
        return format.ToLowerInvariant() switch
        {
            "pcm"  => (ExtractPcm(wavBytes), "audio/pcm"),
            "mp3"  => (ToMp3(wavBytes), "audio/mpeg"),
            "flac" => (wavBytes, "audio/wav"),   // Fallback: return WAV
            "opus" => (wavBytes, "audio/wav"),   // Fallback: return WAV
            "aac"  => (wavBytes, "audio/wav"),   // Fallback: return WAV
            _      => (wavBytes, "audio/wav")    // wav + default
        };
    }

    private static byte[] ExtractPcm(byte[] wavBytes)
    {
        using var reader = new WaveFileReader(new MemoryStream(wavBytes));
        using var ms = new MemoryStream();
        var buffer = new byte[4096];
        int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
            ms.Write(buffer, 0, read);
        return ms.ToArray();
    }

    private static byte[] ToMp3(byte[] wavBytes)
    {
        try
        {
            return ConvertToMp3Internal(wavBytes);
        }
        catch
        {
            // Graceful fallback to WAV if MP3 conversion fails
            return wavBytes;
        }
    }

    private static byte[] ConvertToMp3Internal(byte[] wavBytes)
    {
        using var reader = new WaveFileReader(new MemoryStream(wavBytes));
        using var ms = new MemoryStream();
        using var writer = new NAudio.Lame.LameMP3FileWriter(ms, reader.WaveFormat, NAudio.Lame.LAMEPreset.STANDARD);
        reader.CopyTo(writer);
        writer.Flush();
        return ms.ToArray();
    }
}
