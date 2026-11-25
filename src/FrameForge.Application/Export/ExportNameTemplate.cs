using System.Globalization;
using System.Text;
using FrameForge.Core.Decisions;
using FrameForge.Core.Photos;

namespace FrameForge.Application.Export;

/// <summary>What a name template can draw on for one exported photo. <paramref name="Sequence"/> counts from 1 in export order.</summary>
public sealed record ExportNameContext(Photo Photo, PhotoDecision Decision, int Sequence);

/// <summary>
/// A file-name pattern such as <c>{date}_{name}</c> or <c>{date:yyyyMMdd}-{seq:4}</c>. Pure and deterministic.
/// <list type="bullet">
/// <item>Tokens are case-insensitive. <c>{{</c> and <c>}}</c> write a literal brace.</item>
/// <item>A token with no value (no label, unrated, no camera…) is left out together with one separator
/// (<c>-</c>, <c>_</c>, space or <c>.</c>) next to it, so <c>{name}_{label}</c> gives "IMG_0001" for an unlabelled photo.</item>
/// <item>Characters Windows does not allow in a file name are replaced by "_" in token values; typed in the pattern
/// itself they are an error. Names are cut to <see cref="MaxStemLength"/> characters.</item>
/// <item>Clashes are not resolved here: <see cref="ExportNamePlanner"/> adds " (n)" afterwards.</item>
/// </list>
/// An empty pattern is <see cref="Original"/>: the photo's own file name.
/// </summary>
public sealed class ExportNameTemplate
{
    /// <summary>Longest name (without extension) a template produces; keeps paths well under Windows' 260-character limit.</summary>
    public const int MaxStemLength = 120;

    private const string Separators = "-_ .";

    private static readonly DateTime FormatProbe = new(2026, 12, 31, 23, 59, 58, DateTimeKind.Unspecified);

    private static readonly HashSet<string> ReservedNames = new(
        ["CON", "PRN", "AUX", "NUL", .. Enumerable.Range(0, 10).SelectMany(i => new[] { $"COM{i}", $"LPT{i}" }), "COM¹", "COM²", "COM³", "LPT¹", "LPT²", "LPT³"],
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<char> InvalidChars = [.. Path.GetInvalidFileNameChars()];

    private readonly IReadOnlyList<Part> _parts;

    private ExportNameTemplate(string text, IReadOnlyList<Part> parts)
    {
        Text = text;
        _parts = parts;
    }

    /// <summary>Token names, for help text and error messages.</summary>
    public static IReadOnlyList<string> Tokens { get; } = ["name", "date", "time", "seq", "folder", "camera", "rating", "decision", "label"];

    /// <summary>Keeps each photo's own file name.</summary>
    public static ExportNameTemplate Original { get; } = new(string.Empty, []);

    public string Text { get; }

    public bool IsOriginal => _parts.Count == 0;

    /// <summary>Parses <paramref name="text"/>; empty or blank text gives <see cref="Original"/>.</summary>
    /// <returns>False with one or more readable <paramref name="errors"/> when the pattern cannot be used.</returns>
    public static bool TryParse(string? text, out ExportNameTemplate template, out IReadOnlyList<string> errors)
    {
        template = Original;
        if (string.IsNullOrWhiteSpace(text))
        {
            errors = [];
            return true;
        }

        var problems = new List<string>();
        var parts = new List<Part>();
        var literal = new StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '{' && i + 1 < text.Length && text[i + 1] == '{')
            {
                literal.Append('{');
                i++;
            }
            else if (c == '}' && i + 1 < text.Length && text[i + 1] == '}')
            {
                literal.Append('}');
                i++;
            }
            else if (c == '}')
            {
                problems.Add("A “}” has no opening “{”. Type “}}” for a brace in the name.");
            }
            else if (c == '{')
            {
                var close = text.IndexOf('}', i + 1);
                var open = text.IndexOf('{', i + 1);
                if (close < 0 || (open >= 0 && open < close))
                {
                    problems.Add("A “{” has no closing “}”. Type “{{” for a brace in the name.");
                    continue;
                }

                if (literal.Length > 0)
                {
                    parts.Add(new Part(literal.ToString(), null, null));
                    literal.Clear();
                }

                if (ParseToken(text[(i + 1)..close], problems) is { } token)
                {
                    parts.Add(token);
                }

                i = close;
            }
            else if (c is '\\' or '/')
            {
                problems.Add($"“{c}” would make a subfolder; subfolders aren't supported in names.");
            }
            else if (InvalidChars.Contains(c))
            {
                problems.Add(char.IsControl(c) ? "The pattern contains a control character." : $"“{c}” can't be used in a file name.");
            }
            else
            {
                literal.Append(c);
            }
        }

        if (literal.Length > 0)
        {
            parts.Add(new Part(literal.ToString(), null, null));
        }

        errors = problems.Distinct(StringComparer.Ordinal).ToList();
        if (errors.Count > 0)
        {
            return false;
        }

        template = new ExportNameTemplate(text, parts);
        return true;
    }

    /// <summary>
    /// The file name for one photo, before clash handling.
    /// </summary>
    /// <param name="extension">Extension to use, such as ".jpg"; null keeps the source's own.</param>
    public string FileNameFor(ExportNameContext context, string? extension = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        var sourceName = Path.GetFileName(context.Photo.FilePath);
        var ext = extension ?? Path.GetExtension(sourceName);
        if (IsOriginal)
        {
            return extension is null ? sourceName : Path.ChangeExtension(sourceName, extension);
        }

        var output = new StringBuilder();
        var dropNextSeparator = false;
        foreach (var part in _parts)
        {
            if (part.Literal is { } literal)
            {
                output.Append(dropNextSeparator && Separators.Contains(literal[0], StringComparison.Ordinal) ? literal[1..] : literal);
                dropNextSeparator = false;
                continue;
            }

            var value = Clean(Value(part, context));
            if (value.Length > 0)
            {
                output.Append(value);
                dropNextSeparator = false;
            }
            else if (output.Length > 0 && Separators.Contains(output[^1], StringComparison.Ordinal))
            {
                output.Length--;
            }
            else
            {
                dropNextSeparator = output.Length == 0 || dropNextSeparator;
            }
        }

        var stem = Finish(output.ToString());
        if (stem.Length == 0)
        {
            stem = Path.GetFileNameWithoutExtension(sourceName);
        }

        return stem + ext;
    }

    private static Part? ParseToken(string body, List<string> problems)
    {
        var colon = body.IndexOf(':', StringComparison.Ordinal);
        var name = (colon < 0 ? body : body[..colon]).Trim().ToLowerInvariant();
        var format = colon < 0 ? null : body[(colon + 1)..];
        if (!Tokens.Contains(name))
        {
            problems.Add($"{{{body}}} isn't a known token. Use {string.Join(", ", Tokens.Select(t => "{" + t + "}"))}.");
            return null;
        }

        switch (name)
        {
            case "date" or "time" when format is not null:
                if (format.Length == 0)
                {
                    problems.Add($"{{{name}:}} needs a format after the colon, such as {{{name}:{(name == "date" ? "yyyyMMdd" : "HH-mm")}}}.");
                    return null;
                }

                try
                {
                    _ = FormatProbe.ToString(format, CultureInfo.InvariantCulture);
                }
                catch (FormatException)
                {
                    problems.Add($"“{format}” isn't a date or time format.");
                    return null;
                }

                break;

            case "seq" when format is not null:
                if (format.Length != 1 || format[0] is < '1' or > '9')
                {
                    problems.Add("{seq:N} takes a number of digits from 1 to 9, such as {seq:4}.");
                    return null;
                }

                break;

            case not ("date" or "time" or "seq") when format is not null:
                problems.Add($"{{{name}}} doesn't take a format.");
                return null;
        }

        return new Part(null, name, format);
    }

    private static string Value(Part part, ExportNameContext context)
    {
        var photo = context.Photo;
        var culture = CultureInfo.InvariantCulture;
        return part.Token switch
        {
            "name" => Path.GetFileNameWithoutExtension(photo.FilePath),
            "date" => Taken(photo).ToString(part.Format ?? "yyyy-MM-dd", culture),
            "time" => Taken(photo).ToString(part.Format ?? "HHmmss", culture),
            "seq" => context.Sequence.ToString("D" + (part.Format ?? "3"), culture),
            "folder" => Path.GetFileName(Path.TrimEndingDirectorySeparator(photo.FolderPath)),
            "camera" => CameraName.Format(photo.CameraMake, photo.CameraModel),
            "rating" => context.Decision.Rating?.ToString(culture) ?? string.Empty,
            "decision" => context.Decision.State == CullState.Unset ? string.Empty : context.Decision.State.ToString(),
            "label" => context.Decision.Label == ColourLabel.None ? string.Empty : context.Decision.Label.ToString(),
            _ => throw new InvalidOperationException($"Unhandled token {part.Token}."),
        };
    }

    /// <summary>When the photo was taken, falling back to the file's modified time (local).</summary>
    private static DateTime Taken(Photo photo) =>
        photo.CaptureLocalTime ?? DateTime.SpecifyKind(photo.LastModifiedUtc, DateTimeKind.Utc).ToLocalTime();

    private static string Clean(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            builder.Append(InvalidChars.Contains(c) || char.IsControl(c) ? '_' : c);
        }

        return builder.ToString().Trim();
    }

    /// <summary>Windows rules: no leading spaces, no trailing spaces or dots, no device names; then the length cap.</summary>
    private static string Finish(string stem)
    {
        stem = stem.TrimStart().TrimEnd(' ', '.');
        var info = new StringInfo(stem);
        if (info.LengthInTextElements > MaxStemLength)
        {
            stem = info.SubstringByTextElements(0, MaxStemLength).TrimEnd(' ', '.');
        }

        // "NUL" and "NUL.backup" are both device names to Windows; "NUL_.backup" is not.
        var device = stem.Split('.')[0].TrimEnd();
        return ReservedNames.Contains(device) ? stem.Insert(device.Length, "_") : stem;
    }

    /// <summary>Either literal text or a token (lower-case name, optional format).</summary>
    private sealed record Part(string? Literal, string? Token, string? Format);
}
