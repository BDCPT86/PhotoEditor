namespace FrameForge.Evaluation.Tests;

public sealed class EvaluationMetricsTests
{
    [Fact]
    public void ParseLabels_ReadsHeaderCommentsAndBlanks()
    {
        var labels = EvaluationMetrics.ParseLabels(
        [
            "file,group,decision,faces",
            "# a comment",
            "a.jpg,g1,keep,2",
            "b.jpg,g1,reject,",
            "c.jpg,,,",
        ]);

        Assert.Equal(3, labels.Count);
        Assert.Equal(new PhotoLabel("a.jpg", "g1", LabelDecision.Keep, 2), labels[0]);
        Assert.Equal(new PhotoLabel("b.jpg", "g1", LabelDecision.Reject, null), labels[1]);
        Assert.Equal(new PhotoLabel("c.jpg", "", LabelDecision.Other, null), labels[2]);
    }

    [Theory]
    [InlineData("a.jpg,g1,maybe,")]
    [InlineData("a.jpg,g1,keep,two")]
    public void ParseLabels_RejectsBadValues_WithLineNumber(string line)
    {
        var ex = Assert.Throws<FormatException>(() => EvaluationMetrics.ParseLabels(["file,group,decision,faces", line]));
        Assert.StartsWith("Line 2", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Pairwise_CountsSharedPairs()
    {
        // Labelled: {a,b,c} → 3 pairs. Predicted: {a,b} and {c,d} → 2 pairs, 1 correct.
        var labelled = new Dictionary<string, string> { ["a"] = "x", ["b"] = "x", ["c"] = "x", ["d"] = "" };
        var predicted = new Dictionary<string, string> { ["a"] = "1", ["b"] = "1", ["c"] = "2", ["d"] = "2" };

        var result = EvaluationMetrics.Pairwise(predicted, labelled);

        Assert.Equal((1, 2, 3), (result.TruePositives, result.Predicted, result.Actual));
        Assert.Equal(0.5, result.Precision);
        Assert.Equal(1 / 3d, result.Recall!.Value, 6);
    }

    [Fact]
    public void Metrics_WithEmptyDenominators_AreUndefined_NotZero()
    {
        var none = EvaluationMetrics.Pairwise(new Dictionary<string, string>(), new Dictionary<string, string>());
        Assert.Null(none.Precision);
        Assert.Null(none.Recall);
        Assert.Null(EvaluationMetrics.FaceCounts([]).Recall);
        Assert.Equal("n/a", EvaluationMetrics.Percent(null));
    }

    [Fact]
    public void FaceCounts_CapsMatchesAtLabelledCount()
    {
        var (recall, extra) = EvaluationMetrics.FaceCounts([(3, 2), (0, 2)]);
        Assert.Equal(0.5, recall);
        Assert.Equal(1, extra);
    }

    [Fact]
    public void Picks_AgreementAndFalseRejection()
    {
        var labels = new Dictionary<string, LabelDecision>
        {
            ["a"] = LabelDecision.Keep,
            ["b"] = LabelDecision.Keep,
            ["c"] = LabelDecision.Reject,
            ["d"] = LabelDecision.Reject,
        };

        var (agreement, falseRejection) = EvaluationMetrics.Picks(["a", "c"], ["a", "b", "c", "d"], labels);

        Assert.Equal(0.5, agreement);       // a agreed, c did not
        Assert.Equal(0.5, falseRejection);  // keeper b was not suggested
    }

    [Fact]
    public void Picks_WithoutKeepOrRejectLabels_AreUndefined()
    {
        var labels = new Dictionary<string, LabelDecision> { ["a"] = LabelDecision.Other, ["b"] = LabelDecision.Other };
        var (agreement, falseRejection) = EvaluationMetrics.Picks(["a"], ["a", "b"], labels);
        Assert.Null(agreement);
        Assert.Null(falseRejection);
    }
}
