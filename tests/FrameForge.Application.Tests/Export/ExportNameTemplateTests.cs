using FrameForge.Application.Export;
using FrameForge.Core.Decisions;
using FrameForge.Core.Photos;

namespace FrameForge.Application.Tests.Export;

public sealed class ExportNameTemplateTests
{
    private static readonly DateTime Taken = new(2026, 5, 1, 9, 30, 15, DateTimeKind.Unspecified);

    private static Photo Photo(string path = @"C:\Shoots\Club Match\IMG_0042.JPG", DateTime? taken = null, string? make = "Canon", string? model = "Canon EOS R50") => new()
    {
        FilePath = path,
        FileName = Path.GetFileName(path),
        FolderPath = Path.GetDirectoryName(path)!,
        CaptureLocalTime = taken ?? Taken,
        CameraMake = make,
        CameraModel = model,
        LastModifiedUtc = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc),
    };

    private static ExportNameTemplate Parse(string text)
    {
        Assert.True(ExportNameTemplate.TryParse(text, out var template, out var errors), string.Join(" ", errors));
        return template;
    }

    private static string Name(string pattern, Photo? photo = null, PhotoDecision? decision = null, int seq = 1, string? extension = null) =>
        Parse(pattern).FileNameFor(new ExportNameContext(photo ?? Photo(), decision ?? PhotoDecision.Unset, seq), extension);

    private static string FirstError(string pattern)
    {
        Assert.False(ExportNameTemplate.TryParse(pattern, out var template, out var errors));
        Assert.Same(ExportNameTemplate.Original, template);
        return Assert.Single(errors);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyPattern_KeepsTheOriginalName(string? pattern)
    {
        Assert.True(ExportNameTemplate.TryParse(pattern, out var template, out var errors));
        Assert.Empty(errors);
        Assert.True(template.IsOriginal);

        var context = new ExportNameContext(Photo(), PhotoDecision.Unset, 1);
        Assert.Equal("IMG_0042.JPG", template.FileNameFor(context));
        Assert.Equal("IMG_0042.jpg", template.FileNameFor(context, ".jpg"));
    }

    [Fact]
    public void Tokens_FillFromThePhotoAndDecision()
    {
        var decision = new PhotoDecision(CullState.Keep, 4, ColourLabel.Green);

        Assert.Equal(
            "2026-05-01_093015_IMG_0042_Club Match_Canon EOS R50_4_Keep_Green_007.JPG",
            Name("{date}_{time}_{name}_{folder}_{camera}_{rating}_{decision}_{label}_{seq}", decision: decision, seq: 7));
    }

    [Fact]
    public void Formats_ApplyToDateTimeAndSequence()
    {
        Assert.Equal("20260501-0930-0012.jpg", Name("{date:yyyyMMdd}-{time:HHmm}-{seq:4}", seq: 12, extension: ".jpg"));
        Assert.Equal("May 2026 IMG_0042.JPG", Name("{date:MMM yyyy} {name}")); // invariant month names
    }

    [Fact]
    public void Sequence_WiderThanItsPadding_IsNotCut()
    {
        Assert.Equal("1234.JPG", Name("{seq:2}", seq: 1234));
    }

    [Fact]
    public void TokenNames_AreCaseInsensitive()
    {
        Assert.Equal("IMG_0042-2026.JPG", Name("{NAME}-{Date:yyyy}"));
    }

    [Fact]
    public void Date_FallsBackToTheFileTime_WhenTheCaptureTimeIsUnknown()
    {
        var photo = Photo();
        photo.CaptureLocalTime = null;
        var expected = photo.LastModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(expected + ".JPG", Name("{date}", photo));
    }

    [Theory]
    [InlineData("{name}_{label}", "IMG_0042.JPG")]
    [InlineData("{label}_{name}", "IMG_0042.JPG")]
    [InlineData("{name}_{label}_{rating}_{seq}", "IMG_0042_001.JPG")]
    [InlineData("{label}-{rating} {name}", "IMG_0042.JPG")]
    [InlineData("{name} ({decision})", "IMG_0042 ().JPG")] // "(" is not a separator, so nothing is dropped
    public void EmptyValues_AreLeftOutWithOneSeparator(string pattern, string expected)
    {
        Assert.Equal(expected, Name(pattern));
    }

    [Fact]
    public void AllEmptyValues_FallBackToTheOriginalName()
    {
        Assert.Equal("IMG_0042.JPG", Name("{label}_{decision}", Photo(make: null, model: null)));
    }

    [Fact]
    public void ValuesWithForbiddenCharacters_AreCleaned()
    {
        Assert.Equal("EOS R_5 _ Mk_2_.JPG", Name("{camera}", Photo(make: null, model: "EOS R/5 * Mk:2?")));
        Assert.Equal("05_01_2026.JPG", Name("{date:MM/dd/yyyy}"));
    }

    [Fact]
    public void Braces_CanBeWrittenByDoubling()
    {
        Assert.Equal("{IMG_0042}.JPG", Name("{{{name}}}"));
    }

    [Fact]
    public void TrailingDotsAndSpaces_AreRemoved()
    {
        Assert.Equal("IMG_0042.jpg", Name("{name}. . ", extension: ".jpg"));
    }

    [Theory]
    [InlineData("CON", "CON_.jpg")]
    [InlineData("nul", "nul_.jpg")]
    [InlineData("com1", "com1_.jpg")]
    [InlineData("LPT9.backup", "LPT9_.backup.jpg")]
    [InlineData("CONCERT", "CONCERT.jpg")]
    public void WindowsDeviceNames_GetASuffix(string pattern, string expected)
    {
        Assert.Equal(expected, Name(pattern, extension: ".jpg"));
    }

    [Fact]
    public void LongNames_AreCut()
    {
        var name = Name(new string('a', 300) + "{name}", extension: ".jpg");

        Assert.Equal(ExportNameTemplate.MaxStemLength + ".jpg".Length, name.Length);
        Assert.EndsWith("a.jpg", name, StringComparison.Ordinal);
    }

    [Fact]
    public void LongNames_AreNotCutInsideASurrogatePair()
    {
        var name = Name(new string('a', ExportNameTemplate.MaxStemLength - 1) + "📷📷", extension: ".jpg");

        Assert.EndsWith("a📷.jpg", name, StringComparison.Ordinal);
    }

    [Fact]
    public void PatternWithoutTokens_IsAllowed_TheCallerResolvesClashes()
    {
        var template = Parse("holiday");
        var names = Enumerable.Range(1, 3).Select(i => template.FileNameFor(new ExportNameContext(Photo(), PhotoDecision.Unset, i))).ToList();

        Assert.Equal(["holiday.JPG", "holiday (2).JPG", "holiday (3).JPG"], ExportNamePlanner.Plan(names, []).Select(p => p.TargetFileName));
    }

    [Theory]
    [InlineData("{nmae}", "{nmae} isn't a known token")]
    [InlineData("{name", "has no closing")]
    [InlineData("{name {date}", "has no closing")]
    [InlineData("name}", "has no opening")]
    [InlineData("{date:}", "needs a format")]
    [InlineData("{seq:0}", "digits from 1 to 9")]
    [InlineData("{seq:x}", "digits from 1 to 9")]
    [InlineData("{name:upper}", "doesn't take a format")]
    [InlineData("{date}/{name}", "subfolder")]
    [InlineData(@"{date}\{name}", "subfolder")]
    [InlineData("{name}:{date}", "“:” can't be used")]
    [InlineData("a?b", "“?” can't be used")]
    public void BadPatterns_ExplainWhy(string pattern, string expected)
    {
        Assert.Contains(expected, FirstError(pattern), StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownToken_ListsTheKnownOnes()
    {
        var error = FirstError("{stars}");

        Assert.All(ExportNameTemplate.Tokens, t => Assert.Contains("{" + t + "}", error, StringComparison.Ordinal));
    }

    [Fact]
    public void RepeatedProblems_AreReportedOnce()
    {
        Assert.False(ExportNameTemplate.TryParse("a?b?c", out _, out var errors));

        Assert.Single(errors);
    }

    [Fact]
    public void IsDeterministic()
    {
        var decision = new PhotoDecision(CullState.Review, 2, ColourLabel.Red);

        Assert.Equal(Name("{date}_{seq}_{label}", decision: decision, seq: 3), Name("{date}_{seq}_{label}", decision: decision, seq: 3));
    }
}
