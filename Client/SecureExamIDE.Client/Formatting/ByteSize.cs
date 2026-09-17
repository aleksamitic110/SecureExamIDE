using System.Globalization;

namespace SecureExamIDE.Client.Formatting;

public static class ByteSize
{
    // "812 KB", "1.4 GB": what a student needs to judge whether to start a download on this
    // connection, not an exact count.
    public static string Format(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        string number = unit == 0 || value >= 100
            ? value.ToString("0", CultureInfo.InvariantCulture)
            : value.ToString("0.#", CultureInfo.InvariantCulture);

        return $"{number} {units[unit]}";
    }
}
