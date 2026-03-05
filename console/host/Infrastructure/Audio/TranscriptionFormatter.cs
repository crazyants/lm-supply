using System.Globalization;
using System.Text;

namespace LMSupply.Console.Host.Infrastructure.Audio;

public static class TranscriptionFormatter
{
    public static string ToSrt(IReadOnlyList<Models.OpenAI.TranscriptionSegment>? segments, string fallbackText)
    {
        if (segments == null || segments.Count == 0)
            return $"1\n00:00:00,000 --> 00:00:01,000\n{fallbackText}\n";

        var sb = new StringBuilder();
        for (int i = 0; i < segments.Count; i++)
        {
            var s = segments[i];
            sb.AppendLine(CultureInfo.InvariantCulture, $"{i + 1}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"{FormatSrtTime(s.Start)} --> {FormatSrtTime(s.End)}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"{s.Text.Trim()}");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    public static string ToVtt(IReadOnlyList<Models.OpenAI.TranscriptionSegment>? segments, string fallbackText)
    {
        if (segments == null || segments.Count == 0)
            return $"WEBVTT\n\n00:00:00.000 --> 00:00:01.000\n{fallbackText}\n";

        var sb = new StringBuilder();
        sb.AppendLine("WEBVTT");
        sb.AppendLine();
        foreach (var s in segments)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"{FormatVttTime(s.Start)} --> {FormatVttTime(s.End)}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"{s.Text.Trim()}");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string FormatSrtTime(float seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        return string.Create(CultureInfo.InvariantCulture,
            $"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2},{ts.Milliseconds:D3}");
    }

    private static string FormatVttTime(float seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        return string.Create(CultureInfo.InvariantCulture,
            $"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds:D3}");
    }
}
