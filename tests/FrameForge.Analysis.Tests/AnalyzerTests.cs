namespace FrameForge.Analysis.Tests;

public sealed class SharpnessAnalyzerTests
{
    private static readonly int[] BlurRadii = [0, 1, 2, 4];

    [Fact]
    public void BlurLowersTheScore_Monotonically()
    {
        var sharp = Fixtures.Scene();
        var scores = BlurRadii
            .Select(r => r == 0 ? sharp : Fixtures.BoxBlur(sharp, r))
            .Select(img => SharpnessAnalyzer.Analyze(img, 4).Score)
            .ToArray();

        for (var i = 1; i < scores.Length; i++)
        {
            Assert.True(scores[i] < scores[i - 1], $"blur step {i}: {scores[i]} should be < {scores[i - 1]}");
        }

        Assert.True(scores[0] > scores[3] * 10, $"sharp {scores[0]} vs heavy blur {scores[3]}");
    }

    [Fact]
    public void FlatImage_ScoresZero() =>
        Assert.Equal(0, SharpnessAnalyzer.Analyze(Fixtures.Solid(64, 64, 120, 120, 120), 4).Score);

    [Fact]
    public void SharpSubjectOnBlurredBackground_IsFoundByTheGrid()
    {
        var scene = Fixtures.Scene(320, 240);
        // Blur everything except the bottom-right quadrant.
        var mostlyBlurred = Fixtures.BoxBlur(scene, 4, (x, y) => !(x >= 240 && y >= 180));
        var allBlurred = Fixtures.BoxBlur(scene, 4);

        var partial = SharpnessAnalyzer.Analyze(mostlyBlurred, 4);
        var full = SharpnessAnalyzer.Analyze(allBlurred, 4);

        Assert.Equal((3, 3), (partial.PeakRow, partial.PeakColumn));
        Assert.True(partial.Score > full.Score * 5, $"sharp region {partial.Score} vs blurred {full.Score}");
        Assert.True(partial.GlobalVariance < partial.Score, "the whole-image figure is diluted by the background");
        Assert.Equal(16, partial.CellScores.Length);
    }

    [Fact]
    public void TinyImages_UseASmallerGrid()
    {
        var result = SharpnessAnalyzer.Analyze(Fixtures.Scene(16, 16), 4);
        Assert.Equal(2, result.GridSize);
    }

    [Fact]
    public void IsDeterministic() =>
        Assert.Equal(SharpnessAnalyzer.Analyze(Fixtures.Scene(seed: 7), 4), SharpnessAnalyzer.Analyze(Fixtures.Scene(seed: 7), 4), new SharpnessComparer());

    private sealed class SharpnessComparer : IEqualityComparer<SharpnessResult>
    {
        public bool Equals(SharpnessResult? x, SharpnessResult? y) =>
            x is not null && y is not null && x.Score == y.Score && x.GlobalVariance == y.GlobalVariance && x.CellScores.SequenceEqual(y.CellScores);

        public int GetHashCode(SharpnessResult obj) => obj.Score.GetHashCode();
    }
}

public sealed class ExposureAnalyzerTests
{
    [Fact]
    public void White_IsFullyHighlightClipped()
    {
        var result = ExposureAnalyzer.Analyze(Fixtures.Solid(50, 50, 255, 255, 255));
        Assert.Equal(100, result.HighlightClippedPercent);
        Assert.Equal(0, result.ShadowClippedPercent);
        Assert.Equal(255, result.MeanLuminance, precision: 1);
    }

    [Fact]
    public void SingleSaturatedChannel_CountsAsHighlightClipping()
    {
        // A saturated red is "clipped" in that channel even though luma is moderate.
        var result = ExposureAnalyzer.Analyze(Fixtures.Solid(10, 10, 255, 40, 40));
        Assert.Equal(100, result.HighlightClippedPercent);
        Assert.InRange(result.MeanLuminance, 70, 90);
    }

    [Fact]
    public void HalfBlack_IsHalfShadowClipped()
    {
        var image = AnalysisImage.Create(100, 10, (x, _) => x < 50 ? ((byte)0, (byte)0, (byte)0) : ((byte)128, (byte)128, (byte)128));
        var result = ExposureAnalyzer.Analyze(image);
        Assert.Equal(50, result.ShadowClippedPercent);
        Assert.Equal(0, result.HighlightClippedPercent);
        Assert.Equal(64, result.MeanLuminance, precision: 1);
    }

    [Fact]
    public void Histogram_CountsEveryPixel()
    {
        var result = ExposureAnalyzer.Analyze(Fixtures.Scene());
        Assert.Equal(ExposureAnalyzer.HistogramBins, result.Histogram.Length);
        Assert.Equal(320 * 240, result.Histogram.Sum());
    }
}

public sealed class PerceptualHasherTests
{
    [Fact]
    public void IdenticalImages_HaveDistanceZero_AndHashIsDeterministic()
    {
        var a = PerceptualHasher.Compute(Fixtures.Scene(seed: 3));
        var b = PerceptualHasher.Compute(Fixtures.Scene(seed: 3));
        Assert.Equal(a, b);
        Assert.Equal(0, PerceptualHasher.Distance(a, b));
    }

    [Fact]
    public void NearDuplicates_AreClose_DifferentScenesAreFar()
    {
        var original = Fixtures.Scene(320, 240, seed: 5);
        var hash = PerceptualHasher.Compute(original);

        var brighter = PerceptualHasher.Distance(hash, PerceptualHasher.Compute(Fixtures.Brighten(original, 12)));
        var resized = PerceptualHasher.Distance(hash, PerceptualHasher.Compute(Fixtures.Resize(original, 160, 120)));
        var noisy = PerceptualHasher.Distance(hash, PerceptualHasher.Compute(Fixtures.Jitter(original, 6, seed: 9)));
        var softer = PerceptualHasher.Distance(hash, PerceptualHasher.Compute(Fixtures.BoxBlur(original, 1)));
        var different = PerceptualHasher.Distance(hash, PerceptualHasher.Compute(Fixtures.OtherScene()));

        Assert.InRange(brighter, 0, 4);
        Assert.InRange(resized, 0, 6);
        Assert.InRange(noisy, 0, 6);
        Assert.InRange(softer, 0, 6);
        Assert.True(different >= 20, $"different scene distance {different}");
    }

    [Fact]
    public void Distance_IsHammingDistance()
    {
        Assert.Equal(0, PerceptualHasher.Distance(0xFFUL, 0xFFUL));
        Assert.Equal(8, PerceptualHasher.Distance(0xFFUL, 0x00UL));
        Assert.Equal(64, PerceptualHasher.Distance(ulong.MaxValue, 0UL));
    }

    [Fact]
    public void Format_IsSixteenHexDigits() =>
        Assert.Equal("00000000000000ff", PerceptualHasher.Format(0xFF));
}
