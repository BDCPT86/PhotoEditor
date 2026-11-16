namespace FrameForge.Analysis;

/// <param name="Score">Variance of the Laplacian in the sharpest grid cell. Higher = more fine detail. Heuristic.</param>
/// <param name="GlobalVariance">Variance of the Laplacian over the whole image.</param>
/// <param name="PeakRow">Row (0-based, top) of the sharpest cell.</param>
/// <param name="PeakColumn">Column (0-based, left) of the sharpest cell.</param>
/// <param name="CellScores">Per-cell variance, row-major, <c>GridSize × GridSize</c> entries.</param>
public sealed record SharpnessResult(
    double Score,
    double GlobalVariance,
    int GridSize,
    int PeakRow,
    int PeakColumn,
    double[] CellScores,
    int AnalysedWidth,
    int AnalysedHeight);

/// <summary>
/// Focus/blur heuristic: variance of the 4-neighbour Laplacian of luminance. Blur removes high-frequency detail,
/// lowering the variance. Measured per cell and scored by the sharpest cell, so a sharp subject on a soft background
/// still scores as sharp. Known weaknesses: sensor noise raises the score; flat, low-contrast scenes lower it.
/// </summary>
public static class SharpnessAnalyzer
{
    public const string Version = "laplacian-variance-grid-v1";

    public static SharpnessResult Analyze(AnalysisImage image, int gridSize)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentOutOfRangeException.ThrowIfLessThan(gridSize, 1);

        // Each cell needs interior pixels; shrink the grid for tiny images.
        var grid = Math.Max(1, Math.Min(gridSize, Math.Min(image.Width, image.Height) / 8));
        var lum = image.ToLuminance();
        int w = image.Width, h = image.Height;

        var cellSum = new double[grid * grid];
        var cellSumSq = new double[grid * grid];
        var cellCount = new long[grid * grid];
        double sum = 0, sumSq = 0;
        long count = 0;

        for (var y = 1; y < h - 1; y++)
        {
            var row = y * w;
            var cellRow = Math.Min(grid - 1, y * grid / h);
            for (var x = 1; x < w - 1; x++)
            {
                var i = row + x;
                double laplacian = (4 * lum[i]) - lum[i - 1] - lum[i + 1] - lum[i - w] - lum[i + w];
                var cell = (cellRow * grid) + Math.Min(grid - 1, x * grid / w);
                cellSum[cell] += laplacian;
                cellSumSq[cell] += laplacian * laplacian;
                cellCount[cell]++;
                sum += laplacian;
                sumSq += laplacian * laplacian;
                count++;
            }
        }

        var scores = new double[grid * grid];
        var peak = 0;
        for (var c = 0; c < scores.Length; c++)
        {
            scores[c] = Variance(cellSum[c], cellSumSq[c], cellCount[c]);
            if (scores[c] > scores[peak])
            {
                peak = c;
            }
        }

        return new SharpnessResult(
            Score: Math.Round(scores[peak], 3),
            GlobalVariance: Math.Round(Variance(sum, sumSq, count), 3),
            GridSize: grid,
            PeakRow: peak / grid,
            PeakColumn: peak % grid,
            CellScores: scores.Select(s => Math.Round(s, 3)).ToArray(),
            AnalysedWidth: w,
            AnalysedHeight: h);
    }

    private static double Variance(double sum, double sumSq, long n)
    {
        if (n < 2)
        {
            return 0;
        }

        var mean = sum / n;
        return Math.Max(0, (sumSq / n) - (mean * mean));
    }
}
