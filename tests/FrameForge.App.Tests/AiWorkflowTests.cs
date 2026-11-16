using FrameForge.App.ViewModels;
using FrameForge.Application.Ai;
using FrameForge.Core.Decisions;
using FrameForge.Core.Settings;

namespace FrameForge.App.Tests;

public sealed class AiWorkflowTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>IMG_00–02 share a scene embedding; IMG_01 has the sharpest face; IMG_03 has a soft face.</summary>
    private static async Task<CullingFixture> WithAiAsync()
    {
        var f = new CullingFixture();
        await f.Settings.SetAsync(AppSettings.AiFacesEnabled, true, Ct);
        await f.Settings.SetAsync(AppSettings.AiScenesEnabled, true, Ct);
        float[] scene = [1, 0, 0];
        double[] faceSharpness = [60, 300, 90];
        for (var i = 0; i < 3; i++)
        {
            f.AiService.Results[f.Photos[i].Id] = new AiPhotoAnalysis
            {
                Embedding = scene,
                Faces = new FacesResult([new FaceFinding(0.3f, 0.3f, 0.2f, 0.2f, 0.9f, faceSharpness[i])]),
            };
        }

        f.AiService.Results[f.Photos[3].Id] = new AiPhotoAnalysis { Faces = new FacesResult([new FaceFinding(0.3f, 0.3f, 0.2f, 0.2f, 0.9f, 10)]) };
        f.AiService.ModelsAvailable = true;
        return f;
    }

    [Fact]
    public async Task AiInsights_ShowAsBadges_AndInDetails()
    {
        using var f = await WithAiAsync();
        await f.LoadAsync();

        Assert.True(f.Tile(1).ShowAiPickBadge);
        Assert.False(f.Tile(0).ShowAiPickBadge);
        Assert.Equal("☺ 1", f.Tile(0).FaceBadgeText);
        Assert.True(f.Tile(3).ShowFacesSoftBadge);
        Assert.Contains("AI suggested pick", f.Tile(1).AccessibleStatus, StringComparison.Ordinal);

        f.AnalysisPanel.DetailsTile = f.Tile(1);
        Assert.Contains(f.AnalysisPanel.Details, l => l.Topic == "AI · Suggested pick" && l.Explanation.Contains(AiInsights.Disclaimer, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AiFilters_ShowOnlyMatchingPhotos()
    {
        using var f = await WithAiAsync();
        await f.LoadAsync();

        f.Library.Filter.Analysis = AnalysisFilterChoice.AiPick;
        Assert.Equal(["IMG_01.jpg"], f.Library.Photos.Select(t => t.FileName));

        f.Library.Filter.Analysis = AnalysisFilterChoice.AiFacesSoft;
        Assert.Equal(["IMG_03.jpg"], f.Library.Photos.Select(t => t.FileName));
    }

    [Fact]
    public async Task AnalyzeWithAi_NeverChangesDecisions()
    {
        using var f = await WithAiAsync();
        await f.LoadAsync();

        await f.AnalysisPanel.AnalyzeCommand.ExecuteAsync(null);

        Assert.Equal(1, f.AiService.AnalyzeCalls);
        Assert.All(f.Photos, p => Assert.Equal(PhotoDecision.Unset, f.Decisions.Get(p.Id)));
        Assert.All(f.Library.Photos, t => Assert.Equal(PhotoDecision.Unset, t.Decision));
        Assert.Contains("AI: ", f.Library.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AiOffByDefault_AnalyzeDoesNotRunAi()
    {
        using var f = new CullingFixture();
        await f.LoadAsync();

        await f.AnalysisPanel.AnalyzeCommand.ExecuteAsync(null);

        Assert.Equal(0, f.AiService.AnalyzeCalls);
        Assert.DoesNotContain("AI", f.Library.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AiEnabledWithoutModels_AnalysisStillWorks_AndExplainsWhy()
    {
        using var f = new CullingFixture();
        await f.Settings.SetAsync(AppSettings.AiFacesEnabled, true, Ct);
        await f.LoadAsync();

        await f.AnalysisPanel.AnalyzeCommand.ExecuteAsync(null);

        Assert.Equal(1, f.AnalysisService.AnalyzeCalls);
        Assert.Contains("Analysis finished", f.Library.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("AI analysis skipped", f.Library.StatusMessage, StringComparison.Ordinal);
        Assert.All(f.Library.Photos, t => Assert.Equal(AiInsight.None, t.Ai));
    }
}
