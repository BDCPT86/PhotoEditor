using System.Globalization;
using FrameForge.Core.Decisions;

namespace FrameForge.Application.Export;

/// <summary>
/// Writes an RFC 4180 CSV manifest. Values that a spreadsheet would treat as a formula
/// (leading =, +, -, @, tab, or carriage return) are prefixed with an apostrophe.
/// </summary>
public static class ManifestWriter
{
    public static readonly string[] Columns =
        ["FilePath", "FileName", "Decision", "Rating", "Label", "CaptureTime", "Width", "Height", "Camera"];

    public static void Write(TextWriter writer, IEnumerable<ExportItem> items)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(items);

        writer.Write(string.Join(',', Columns));
        writer.Write("\r\n");

        foreach (var item in items)
        {
            var photo = item.Photo;
            var camera = string.Join(' ', new[] { photo.CameraMake, photo.CameraModel }.Where(s => !string.IsNullOrWhiteSpace(s)));
            string?[] values =
            [
                photo.FilePath,
                photo.FileName,
                item.Decision.State.ToString(),
                item.Decision.Rating?.ToString(CultureInfo.InvariantCulture),
                item.Decision.Label == ColourLabel.None ? null : item.Decision.Label.ToString(),
                photo.CaptureLocalTime?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                photo.Width?.ToString(CultureInfo.InvariantCulture),
                photo.Height?.ToString(CultureInfo.InvariantCulture),
                camera,
            ];

            writer.Write(string.Join(',', values.Select(Escape)));
            writer.Write("\r\n");
        }
    }

    internal static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            value = "'" + value;
        }

        var needsQuotes = value.AsSpan().IndexOfAny(",\"\r\n") >= 0;
        return needsQuotes ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;
    }
}
