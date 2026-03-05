namespace LMSupply.Console.Host.Infrastructure.Vision;

public static class RleEncoder
{
    /// <summary>Encodes pixels of a specific class as COCO-style RLE counts string.</summary>
    public static string Encode(int[] classMap, int classId)
    {
        var sb = new System.Text.StringBuilder();
        int count = 1;
        int current = classMap[0] == classId ? 1 : 0;

        for (int i = 1; i < classMap.Length; i++)
        {
            int val = classMap[i] == classId ? 1 : 0;
            if (val == current)
            {
                count++;
            }
            else
            {
                sb.Append(count.ToString(System.Globalization.CultureInfo.InvariantCulture));
                sb.Append(',');
                count = 1;
                current = val;
            }
        }
        sb.Append(count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return sb.ToString();
    }
}
